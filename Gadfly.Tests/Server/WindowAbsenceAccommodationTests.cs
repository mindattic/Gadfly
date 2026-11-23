using Gadfly.Server.Data;
using Gadfly.Server.Exams;
using Gadfly.Server.Integrity;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The window-absence accommodation (GAD-T604): granted by staff per student and assignment, audited, it raises the thresholds at
/// which leaving the exam window marks the attempt for review, or turns the mark off; it never lowers them. It replaced GAD-T511's
/// stopgap, so the typed-answer and eye-contact accommodations no longer exempt a student from the mark.
/// </summary>
[TestFixture]
public class WindowAbsenceAccommodationTests : ServerTest
{
    private sealed record World(Guid Org, Guid AssignmentId, Guid StudentId, Guid SessionId);

    private async Task<World> RunningAsync(Action<ExamAssignment>? accommodate = null)
    {
        Clock.Now = DateTimeOffset.UtcNow;
        var w = await Seed.ExamAsync();
        await using var db = OrgDb(w.Org);
        if (accommodate is not null)
        {
            var a = await db.ExamAssignments.FirstAsync(x => x.Id == w.AssignmentId);
            accommodate(a);
        }
        var session = new ExamSession
        {
            OrganizationId = w.Org, AssignmentId = w.AssignmentId, ExamId = w.ExamId, StudentUserId = w.StudentId, State = SessionState.Running,
            CreatedAt = Clock.Now.AddMinutes(-20), ConsentRecordedAt = Clock.Now.AddMinutes(-15), StartedAt = Clock.Now.AddMinutes(-15),
            TranscriptJson = "{}", LedgerJson = "{}",
        };
        db.ExamSessions.Add(session);
        await db.SaveChangesAsync();
        return new World(w.Org, w.AssignmentId, w.StudentId, session.Id);
    }

    private static async Task<WindowAbsence?> AwayAsync(World w, int seconds)
    {
        await using var db = OrgDb(w.Org);
        return await new WindowFocusRecorder(db, Clock, NullLogger<WindowFocusRecorder>.Instance)
            .RecordAbsenceAsync(w.Org, w.StudentId, w.SessionId, TimeSpan.FromSeconds(seconds));
    }

    private static async Task<bool> FlaggedAsync(World w)
    {
        await using var db = SystemDb();
        return await db.ExamSessions.Where(s => s.Id == w.SessionId).Select(s => s.NeedsInstructorReview).SingleAsync();
    }

    [Test]
    public void The_rule_never_goes_below_the_standard_thresholds()
    {
        Assert.That(WindowAbsenceRule.For(false, null, null), Is.EqualTo(WindowAbsenceRule.Standard));
        Assert.That(WindowAbsenceRule.For(false, 1, 5), Is.EqualTo(WindowAbsenceRule.Standard), "lower values fall back to the standard");
        Assert.That(WindowAbsenceRule.Standard.Flags(3, TimeSpan.Zero) && WindowAbsenceRule.Standard.Flags(1, TimeSpan.FromSeconds(60)), Is.True);
        var raised = WindowAbsenceRule.For(false, 6, 300);
        Assert.That(raised.Flags(5, TimeSpan.FromSeconds(299)), Is.False);
        Assert.That(raised.Flags(6, TimeSpan.Zero) && raised.Flags(1, TimeSpan.FromSeconds(300)), Is.True);
        Assert.That(WindowAbsenceRule.For(true, null, null).Flags(100, TimeSpan.FromHours(1)), Is.False);
        Assert.That(raised.Describe(), Does.Contain("6 absences").And.Contain("5 min"));
    }

    [Test]
    public async Task Typed_answer_and_eye_contact_accommodations_no_longer_keep_absences_from_flagging()
    {
        var w = await RunningAsync(a => { a.TypedAnswerAccommodation = true; a.GazeAccommodation = true; });
        var r = await AwayAsync(w, 61);
        Assert.That(r!.Exempt, Is.False);
        Assert.That(r.NewlyFlagged, Is.True);
        Assert.That(await FlaggedAsync(w), Is.True);
    }

    [Test]
    public async Task The_accommodation_turned_off_never_flags_but_still_notes_each_absence()
    {
        var w = await RunningAsync(a => a.WindowAbsenceFlagOff = true);
        for (var i = 0; i < 4; i++) Assert.That((await AwayAsync(w, 90))!.Exempt, Is.True);
        Assert.That(await FlaggedAsync(w), Is.False);
        await using var db = SystemDb();
        var notes = await db.IntegrityEvents.Where(e => e.SessionId == w.SessionId && e.Type == WindowFocusRecorder.AbsenceType).ToListAsync();
        Assert.That(notes, Has.Count.EqualTo(4));
        Assert.That(notes.All(n => n.Severity == IntegritySeverity.Info && n.Summary.Contains("accommodation")), Is.True);
    }

    [Test]
    public async Task Raised_thresholds_flag_only_once_they_are_reached()
    {
        var w = await RunningAsync(a => { a.WindowAbsenceReviewAfterCount = 5; a.WindowAbsenceReviewAfterSeconds = 300; });
        await AwayAsync(w, 120);   // longer than the standard minute, shorter than the raised five
        for (var i = 0; i < 3; i++) await AwayAsync(w, 5);
        Assert.That(await FlaggedAsync(w), Is.False, "four absences, none of five minutes");
        Assert.That((await AwayAsync(w, 5))!.NewlyFlagged, Is.True, "the fifth absence");
    }

    [Test]
    public async Task Staff_set_it_through_the_exam_service_only_upward_and_every_change_is_audited()
    {
        var w = await RunningAsync();
        var admin = await Seed.MemberAsync(Services, w.Org, OrgRoles.Admin);
        await using (var scope = Scope(Member(w.Org, admin, OrgRoles.Admin)))
        {
            var exams = scope.ServiceProvider.GetRequiredService<ExamService>();
            Assert.That((await exams.SetWindowAbsenceAccommodationAsync(w.AssignmentId, false, 2, null)).Ok, Is.False, "can't lower the count");
            Assert.That((await exams.SetWindowAbsenceAccommodationAsync(w.AssignmentId, false, null, 30)).Ok, Is.False, "can't shorten the length");
            Assert.That((await exams.SetWindowAbsenceAccommodationAsync(w.AssignmentId, false, 6, 300)).Ok, Is.True);
            Assert.That((await exams.SetWindowAbsenceAccommodationAsync(w.AssignmentId, true, null, null)).Ok, Is.True);
            Assert.That((await exams.SetWindowAbsenceAccommodationAsync(w.AssignmentId, false, null, null)).Ok, Is.True);
        }
        await using var db = SystemDb();
        var a = await db.ExamAssignments.SingleAsync(x => x.Id == w.AssignmentId);
        Assert.That(a.WindowAbsenceFlagOff || a.WindowAbsenceReviewAfterCount is not null || a.WindowAbsenceReviewAfterSeconds is not null, Is.False);
        var audits = await db.AuditEvents.Where(e => e.EntityId == w.AssignmentId.ToString() && e.Action.StartsWith("assessment.accommodation.window-absence"))
            .Select(e => e.Action).ToListAsync();
        Assert.That(audits, Has.Count.EqualTo(3));
        Assert.That(audits.Count(x => x.EndsWith(".withdrawn")), Is.EqualTo(1));
    }

    [Test]
    public async Task A_student_cannot_grant_it_to_themselves()
    {
        var w = await RunningAsync();
        await using var scope = Scope(Member(w.Org, w.StudentId, OrgRoles.Student));
        var r = await scope.ServiceProvider.GetRequiredService<ExamService>().SetWindowAbsenceAccommodationAsync(w.AssignmentId, true, null, null);
        Assert.That(r.Ok, Is.False);
        await using var db = SystemDb();
        Assert.That(await db.ExamAssignments.Where(x => x.Id == w.AssignmentId).Select(x => x.WindowAbsenceFlagOff).SingleAsync(), Is.False);
    }
}
