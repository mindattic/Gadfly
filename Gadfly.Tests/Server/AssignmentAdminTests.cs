using Gadfly.Assessment;
using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The assignments console (GAD-T298): status worked out in SQL, the list and grid, and the bulk changes (dates, another attempt,
/// reset keeping the audit copy, unassign, accommodations), each audited and kept inside the organization.
/// </summary>
[TestFixture]
public class AssignmentAdminTests : ServerTest
{
    private static async Task<(Seed.ExamWorld World, TenantSnapshot Admin)> WorldAsync(int attempts = 1, DateTimeOffset? closes = null)
    {
        var w = await Seed.ExamAsync(attempts: attempts, closes: closes);
        var admin = Member(w.Org, await Seed.MemberAsync(Services, w.Org, OrgRoles.Admin), OrgRoles.Admin);
        await using var db = SystemDb();
        db.OrganizationMembers.Add(new MindAttic.Authentication.Entities.AuthMembership
            { OrganizationId = w.Org, UserId = w.StudentId, Role = OrgRoles.Student, CreatedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (w, admin);
    }

    private static async Task<T> AsAsync<T>(TenantSnapshot who, Func<AssignmentAdminService, Task<T>> work)
    {
        await using var scope = Scope(who);
        return await work(new AssignmentAdminService(scope.ServiceProvider));
    }

    private static async Task<Guid> EndedAttemptAsync(Seed.ExamWorld w, int attempt = 1, GradeStatus? grade = null)
    {
        var now = Clock.GetUtcNow();
        await using var db = SystemDb();
        var s = new ExamSession
        {
            OrganizationId = w.Org, AssignmentId = w.AssignmentId, ExamId = w.ExamId, StudentUserId = w.StudentId, Attempt = attempt,
            State = SessionState.Ended, EndReason = "Mastery", CreatedAt = now.AddMinutes(-30), StartedAt = now.AddMinutes(-30), EndedAt = now.AddMinutes(-5),
        };
        db.ExamSessions.Add(s);
        if (grade is { } g) db.FinalGrades.Add(new FinalGrade { SessionId = s.Id, OrganizationId = w.Org, Status = g, FinalPercent = 80m, UpdatedAt = now });
        await db.SaveChangesAsync();
        return s.Id;
    }

    private static Task<AssignmentListRow> RowAsync(TenantSnapshot admin, Seed.ExamWorld w) =>
        AsAsync(admin, async s => (await s.ListAsync(new AssignmentQuery { ExamId = w.ExamId })).Rows.Single(r => r.Id == w.AssignmentId));

    [Test]
    public async Task Status_FollowsTheAttempts_AndTheFilterAgreesWithIt()
    {
        var (w, admin) = await WorldAsync();
        Assert.That((await RowAsync(admin, w)).Progress, Is.EqualTo(AssignmentProgress.NotStarted));

        await EndedAttemptAsync(w);
        Assert.That((await RowAsync(admin, w)).Progress, Is.EqualTo(AssignmentProgress.AwaitingGrading));
        var awaiting = await AsAsync(admin, s => s.ListAsync(new AssignmentQuery { ExamId = w.ExamId, Progress = AssignmentProgressFilter.AwaitingGrading }));
        Assert.That(awaiting.Total, Is.EqualTo(1));
        var notStarted = await AsAsync(admin, s => s.ListAsync(new AssignmentQuery { ExamId = w.ExamId, Progress = AssignmentProgressFilter.NotStarted }));
        Assert.That(notStarted.Total, Is.Zero);

        await using (var db = SystemDb())
        {
            var sid = await db.ExamSessions.Where(x => x.AssignmentId == w.AssignmentId).Select(x => x.Id).SingleAsync();
            db.FinalGrades.Add(new FinalGrade { SessionId = sid, OrganizationId = w.Org, Status = GradeStatus.Finalized, FinalPercent = 72.5m, UpdatedAt = Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        var graded = await RowAsync(admin, w);
        Assert.That(graded.Progress, Is.EqualTo(AssignmentProgress.Graded));
        Assert.That(graded.Score, Does.StartWith("72.5%"));

        await using (var db = SystemDb())
            await db.ExamAssignments.Where(a => a.Id == w.AssignmentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.ResultsReleasedAt, Clock.GetUtcNow()));
        Assert.That((await RowAsync(admin, w)).Progress, Is.EqualTo(AssignmentProgress.Released));
    }

    [Test]
    public async Task List_StaysInsideTheOrganization()
    {
        var (w, admin) = await WorldAsync();
        var (other, _) = await WorldAsync();
        var page = await AsAsync(admin, s => s.ListAsync(new AssignmentQuery()));
        Assert.That(page.Rows.Select(r => r.Id), Does.Contain(w.AssignmentId).And.Not.Contain(other.AssignmentId));
        var r = await AsAsync(admin, s => s.GrantAttemptAsync([other.AssignmentId], "not mine"));
        Assert.That(r.Ok, Is.False);
        await using var db = SystemDb();
        Assert.That(await db.ExamAssignments.Where(a => a.Id == other.AssignmentId).Select(a => a.AttemptsAllowed).SingleAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Grid_ShowsAssignedCells_AndLeavesTheRestEmpty()
    {
        var (w, admin) = await WorldAsync();
        await Seed.MemberAsync(Services, w.Org, OrgRoles.Student);
        var grid = await AsAsync(admin, s => s.GridAsync(null, null, 1, 50));
        Assert.That(grid.Students, Has.Count.EqualTo(2));
        Assert.That(grid.Exams.Select(e => e.Id), Does.Contain(w.ExamId));
        Assert.That(grid.Cells.Keys, Is.EquivalentTo(new[] { (w.StudentId, w.ExamId) }));
    }

    [Test]
    public async Task Dates_SetAndMove_RefusingAWindowThatClosesBeforeItOpens()
    {
        var due = Clock.GetUtcNow().AddDays(5);
        var (w, admin) = await WorldAsync(closes: due);

        var moved = await AsAsync(admin, s => s.MoveDueDatesAsync([w.AssignmentId], 3));
        Assert.That(moved.Changed, Is.EqualTo(1), moved.Message);
        Assert.That((await RowAsync(admin, w)).ClosesAt, Is.EqualTo(due.AddDays(3)));

        var bad = await AsAsync(admin, s => s.SetDatesAsync([w.AssignmentId], true, due.AddDays(30), false, null));
        Assert.That(bad.Changed, Is.Zero);
        Assert.That(bad.Problems.Single(), Does.Contain("close before it opens"));

        var cleared = await AsAsync(admin, s => s.SetDatesAsync([w.AssignmentId], false, null, true, null));
        Assert.That(cleared.Changed, Is.EqualTo(1));
        Assert.That((await RowAsync(admin, w)).ClosesAt, Is.Null);

        await using var db = OrgDb(w.Org);
        Assert.That(await db.AuditEvents.CountAsync(e => e.EntityId == w.AssignmentId.ToString()
            && (e.Action == "admin.assignment-due-moved" || e.Action == "admin.assignment-dates-set")), Is.EqualTo(2));
    }

    [Test]
    public async Task GrantAttempt_NeedsAReason_AndIsAudited()
    {
        var (w, admin) = await WorldAsync();
        Assert.That((await AsAsync(admin, s => s.GrantAttemptAsync([w.AssignmentId], ""))).Ok, Is.False);
        var r = await AsAsync(admin, s => s.GrantAttemptAsync([w.AssignmentId], "Fire alarm during the exam"));
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That((await RowAsync(admin, w)).AttemptsAllowed, Is.EqualTo(2));
        await using var db = OrgDb(w.Org);
        var e = await db.AuditEvents.SingleAsync(x => x.Action == "admin.attempt-granted" && x.EntityId == w.AssignmentId.ToString());
        Assert.That(e.Reason, Is.EqualTo("Fire alarm during the exam"));
    }

    [Test]
    public async Task Reset_ArchivesTheGrades_KeepsTheAttempt_AndGrantsAnother()
    {
        var (w, admin) = await WorldAsync();
        var sid = await EndedAttemptAsync(w, grade: GradeStatus.Finalized);

        var r = await AsAsync(admin, s => s.ResetAttemptAsync([w.AssignmentId], "Wrong exam version"));
        Assert.That(r.Ok, Is.True, r.Message);

        await using var db = OrgDb(w.Org);
        Assert.That(await db.ExamSessions.AnyAsync(s => s.Id == sid), Is.True, "the attempt and its transcript stay");
        var held = await db.FinalGrades.SingleAsync(g => g.SessionId == sid);
        Assert.That(held.Status, Is.EqualTo(GradeStatus.ExaminationInProgress), "held out of grading, owner decision (d)");
        Assert.That(held.FinalPercent, Is.Null);
        Assert.That(await db.AuditEvents.AnyAsync(e => e.Action == "desk.scores-archived" && e.SessionId == sid), Is.True, "an archived copy is kept");
        Assert.That(await db.AuditEvents.AnyAsync(e => e.Action == "admin.attempt-reset" && e.Reason == "Wrong exam version"), Is.True);
        Assert.That(await db.ExamAssignments.Where(a => a.Id == w.AssignmentId).Select(a => a.AttemptsAllowed).SingleAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Reset_WithNoAttempt_ChangesNothing()
    {
        var (w, admin) = await WorldAsync();
        var r = await AsAsync(admin, s => s.ResetAttemptAsync([w.AssignmentId], "just because"));
        Assert.That(r.Ok, Is.False);
        Assert.That((await RowAsync(admin, w)).AttemptsAllowed, Is.EqualTo(1));
    }

    [Test]
    public async Task Unassign_RemovesOnlyAssignmentsWithoutAttempts()
    {
        var (w, admin) = await WorldAsync();
        await EndedAttemptAsync(w);
        var kept = await AsAsync(admin, s => s.UnassignAsync([w.AssignmentId], "Dropped the course"));
        Assert.That(kept.Changed, Is.Zero);
        await using (var db = SystemDb()) Assert.That(await db.ExamAssignments.AnyAsync(a => a.Id == w.AssignmentId), Is.True);

        var second = await Seed.ExamAsync(organization: w.Org);
        var removed = await AsAsync(admin, s => s.UnassignAsync([second.AssignmentId], "Assigned by mistake"));
        Assert.That(removed.Changed, Is.EqualTo(1), removed.Message);
        await using (var db = OrgDb(w.Org))
        {
            Assert.That(await db.ExamAssignments.AnyAsync(a => a.Id == second.AssignmentId), Is.False);
            Assert.That(await db.AuditEvents.AnyAsync(e => e.Action == "admin.assignment-removed" && e.EntityId == second.AssignmentId.ToString()
                                                           && e.Reason == "Assigned by mistake"), Is.True);
        }
    }

    [Test]
    public async Task Accommodations_AreSetPerAssignment_AndAudited()
    {
        var (w, admin) = await WorldAsync();
        Assert.That((await AsAsync(admin, s => s.SetExtraTimeAsync([w.AssignmentId], 1.5m))).Changed, Is.EqualTo(1));
        Assert.That((await AsAsync(admin, s => s.SetEyeContactAsync([w.AssignmentId], true))).Ok, Is.True);
        Assert.That((await AsAsync(admin, s => s.SetTypedAnswersAsync([w.AssignmentId], true))).Ok, Is.True);
        var row = await RowAsync(admin, w);
        Assert.That((row.TimeMultiplier, row.EyeContact, row.TypedAnswers), Is.EqualTo((1.5m, true, true)));

        var typed = await AsAsync(admin, s => s.ListAsync(new AssignmentQuery { ExamId = w.ExamId, Accommodation = AssignmentAccommodationFilter.TypedAnswers }));
        Assert.That(typed.Total, Is.EqualTo(1));
        Assert.That((await AsAsync(admin, s => s.SetExtraTimeAsync([w.AssignmentId], 9m))).Ok, Is.False);

        await using var db = OrgDb(w.Org);
        Assert.That(await db.AuditEvents.AnyAsync(e => e.Action == "assessment.accommodation.time" && e.EntityId == w.AssignmentId.ToString()), Is.True);
        Assert.That(await db.AuditEvents.AnyAsync(e => e.Action == "assessment.accommodation.gaze" && e.EntityId == w.AssignmentId.ToString()), Is.True);
    }

    [Test]
    public async Task Assign_ByPastedNames_ReportsTheUnknown()
    {
        var (w, admin) = await WorldAsync();
        var newcomer = Seed.UserName();
        await Seed.MemberAsync(Services, w.Org, OrgRoles.Student, newcomer);
        var (ids, unknown) = await AsAsync(admin, s => s.ResolveStudentsAsync($"{newcomer}\nnobody-{Guid.NewGuid():N}"));
        Assert.That(ids, Has.Count.EqualTo(1));
        Assert.That(unknown, Has.Count.EqualTo(1));
        var r = await AsAsync(admin, s => s.AssignAsync(w.ExamId, ids, null, null, 2, 1m));
        Assert.That(r.Ok, Is.True, r.Message);
        var page = await AsAsync(admin, s => s.ListAsync(new AssignmentQuery { ExamId = w.ExamId }));
        Assert.That(page.Total, Is.EqualTo(2));
    }

    [Test]
    public async Task OnlyOwnersAndAdmins_GetIn()
    {
        var (w, _) = await WorldAsync();
        var instructor = Member(w.Org, await Seed.MemberAsync(Services, w.Org, OrgRoles.Instructor), OrgRoles.Instructor);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => AsAsync(instructor, s => s.ListAsync(new AssignmentQuery())));
        var r = await AsAsync(instructor, s => s.GrantAttemptAsync([w.AssignmentId], "sneaky"));
        Assert.That(r.Ok, Is.False);
        await using var db = OrgDb(w.Org);
        Assert.That(await db.AuditEvents.AnyAsync(e => e.Action == "access.denied" && e.ActorRole == OrgRoles.Instructor), Is.True);
    }
}
