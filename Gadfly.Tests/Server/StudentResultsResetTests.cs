using Gadfly.Assessment;
using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Resetting one student's results for chosen exams from their profile (GAD-T790): only that student's attempts at those exams go,
/// with what hangs off them, other students and other exams stay, one audit entry is written, and only owners and admins may do it.
/// </summary>
[TestFixture]
public class StudentResultsResetTests : ServerTest
{
    private sealed record World(Guid Org, TenantSnapshot Admin, Guid StudentA, Guid StudentB, Guid Exam1, Guid Exam2,
        Guid A1, Guid A2, Guid B1, Guid AssignmentA1, Guid AssignmentA2, Guid AssignmentB1);

    /// <summary>Students A and B in one organization; A sat exams 1 and 2, B sat exam 1; every attempt graded and released.</summary>
    private static async Task<World> WorldAsync()
    {
        var w1 = await Seed.ExamAsync();
        var w2 = await Seed.ExamAsync(organization: w1.Org);
        var admin = Member(w1.Org, await Seed.MemberAsync(Services, w1.Org, OrgRoles.Admin), OrgRoles.Admin);
        var now = Clock.GetUtcNow();
        await using var db = SystemDb();
        foreach (var s in new[] { w1.StudentId, w2.StudentId })
            db.OrganizationMembers.Add(new MindAttic.Authentication.Entities.AuthMembership
                { OrganizationId = w1.Org, UserId = s, Role = OrgRoles.Student, CreatedUtc = DateTime.UtcNow });
        var a2 = new ExamAssignment { OrganizationId = w1.Org, ExamId = w2.ExamId, StudentUserId = w1.StudentId, AttemptsAllowed = 1, AssignedAt = now };
        var b1 = new ExamAssignment { OrganizationId = w1.Org, ExamId = w1.ExamId, StudentUserId = w2.StudentId, AttemptsAllowed = 1, AssignedAt = now };
        db.ExamAssignments.AddRange(a2, b1);
        await db.SaveChangesAsync();

        Guid Attempt(Guid assignment, Guid exam, Guid student)
        {
            var s = new ExamSession
            {
                OrganizationId = w1.Org, AssignmentId = assignment, ExamId = exam, StudentUserId = student, State = SessionState.Ended,
                EndReason = "Mastery", CreatedAt = now.AddMinutes(-30), StartedAt = now.AddMinutes(-30), EndedAt = now.AddMinutes(-5),
            };
            var turn = new SessionTurn { OrganizationId = w1.Org, SessionId = s.Id, Index = 0, ConceptKey = "photosynthesis", Move = "Elicit", Question = "Why?", AskedAt = now, AnswerText = "Light." };
            db.AddRange(s, turn);
            db.Add(new StudentResponse { OrganizationId = w1.Org, SessionId = s.Id, TurnId = turn.Id, OriginalText = "Light.", VerifiedText = "Light.", SubmittedAt = now });
            db.Add(new FinalGrade { OrganizationId = w1.Org, SessionId = s.Id, Status = GradeStatus.Finalized, FinalPercent = 80m, UpdatedAt = now });
            return s.Id;
        }
        var sa1 = Attempt(w1.AssignmentId, w1.ExamId, w1.StudentId);
        var sa2 = Attempt(a2.Id, w2.ExamId, w1.StudentId);
        var sb1 = Attempt(b1.Id, w1.ExamId, w2.StudentId);
        await db.SaveChangesAsync();
        await db.ExamAssignments.Where(a => a.OrganizationId == w1.Org).ExecuteUpdateAsync(u => u.SetProperty(a => a.ResultsReleasedAt, now));
        return new World(w1.Org, admin, w1.StudentId, w2.StudentId, w1.ExamId, w2.ExamId, sa1, sa2, sb1, w1.AssignmentId, a2.Id, b1.Id);
    }

    private static async Task<T> AsAsync<T>(TenantSnapshot who, Func<StudentAdminService, Task<T>> work)
    {
        await using var scope = Scope(who);
        return await work(new StudentAdminService(scope.ServiceProvider));
    }

    [Test]
    public async Task Reset_ErasesOnlyThatStudentsAttemptsAtTheChosenExams()
    {
        var w = await WorldAsync();

        var r = await AsAsync(w.Admin, s => s.ResetResultsAsync(w.StudentA, [w.Exam1], "Sat the wrong version"));
        Assert.That(r.Ok, Is.True, r.Message);

        await using var db = SystemDb();
        Assert.That(await db.ExamSessions.AnyAsync(s => s.Id == w.A1), Is.False, "the chosen attempt is erased");
        Assert.That(await db.SessionTurns.AnyAsync(t => t.SessionId == w.A1), Is.False);
        Assert.That(await db.StudentResponses.AnyAsync(t => t.SessionId == w.A1), Is.False);
        Assert.That(await db.FinalGrades.AnyAsync(g => g.SessionId == w.A1), Is.False);
        Assert.That(await db.ExamAssignments.Where(a => a.Id == w.AssignmentA1).Select(a => a.ResultsReleasedAt).SingleAsync(), Is.Null);
        Assert.That(await db.ExamAssignments.AnyAsync(a => a.Id == w.AssignmentA1), Is.True, "the assignment stays, so they can take it again");

        foreach (var kept in new[] { w.A2, w.B1 })
        {
            Assert.That(await db.ExamSessions.AnyAsync(s => s.Id == kept), Is.True, "another exam's or another student's attempt stays");
            Assert.That(await db.SessionTurns.AnyAsync(t => t.SessionId == kept), Is.True);
            Assert.That(await db.StudentResponses.AnyAsync(t => t.SessionId == kept), Is.True);
            Assert.That(await db.FinalGrades.AnyAsync(g => g.SessionId == kept), Is.True);
        }
        Assert.That(await db.ExamAssignments.Where(a => a.Id == w.AssignmentA2 || a.Id == w.AssignmentB1).AllAsync(a => a.ResultsReleasedAt != null), Is.True);

        var audit = await db.AuditEvents.Where(e => e.OrganizationId == w.Org && e.Action == StudentAdminService.ResultsResetAction).ToListAsync();
        Assert.That(audit, Has.Count.EqualTo(1));
        Assert.That(audit[0].EntityId, Is.EqualTo(w.StudentA.ToString()));
        Assert.That(audit[0].Reason, Is.EqualTo("Sat the wrong version"));
        Assert.That(audit[0].AfterJson, Does.Contain(w.Exam1.ToString()).And.Contain("ExamSessions"));
    }

    [Test]
    public async Task Reset_SeveralExamsAtOnce()
    {
        var w = await WorldAsync();
        var r = await AsAsync(w.Admin, s => s.ResetResultsAsync(w.StudentA, [w.Exam1, w.Exam2], "Start over"));
        Assert.That(r.Ok, Is.True, r.Message);
        await using var db = SystemDb();
        Assert.That(await db.ExamSessions.Where(s => s.Id == w.A1 || s.Id == w.A2).CountAsync(), Is.Zero);
        Assert.That(await db.ExamSessions.AnyAsync(s => s.Id == w.B1), Is.True);
        Assert.That(await db.AuditEvents.CountAsync(e => e.OrganizationId == w.Org && e.Action == StudentAdminService.ResultsResetAction), Is.EqualTo(1));
    }

    [Test]
    public async Task Reset_RefusesAnInstructor_AndNeedsAReason()
    {
        var w = await WorldAsync();
        var instructor = Member(w.Org, await Seed.MemberAsync(Services, w.Org, OrgRoles.Instructor), OrgRoles.Instructor);

        var denied = await AsAsync(instructor, s => s.ResetResultsAsync(w.StudentA, [w.Exam1], "Not mine to do"));
        Assert.That(denied.Ok, Is.False);
        Assert.That(denied.Message, Does.Contain("owners and admins"));
        var noReason = await AsAsync(w.Admin, s => s.ResetResultsAsync(w.StudentA, [w.Exam1], " "));
        Assert.That(noReason.Ok, Is.False);

        await using var db = SystemDb();
        Assert.That(await db.ExamSessions.AnyAsync(s => s.Id == w.A1), Is.True, "nothing was erased");
        Assert.That(await db.AuditEvents.AnyAsync(e => e.OrganizationId == w.Org && e.Action == "access.denied" && e.EntityId == w.StudentA.ToString()), Is.True);
        Assert.That(await db.AuditEvents.AnyAsync(e => e.OrganizationId == w.Org && e.Action == StudentAdminService.ResultsResetAction), Is.False);
    }

    [Test]
    public async Task Reset_StaysInsideTheOrganization()
    {
        var w = await WorldAsync();
        var other = await WorldAsync();
        var r = await AsAsync(w.Admin, s => s.ResetResultsAsync(other.StudentA, [other.Exam1], "Not our student"));
        Assert.That(r.Ok, Is.False);
        await using var db = SystemDb();
        Assert.That(await db.ExamSessions.AnyAsync(s => s.Id == other.A1), Is.True);
    }
}
