using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Integrity;
using Gadfly.Server.Jobs;
using Gadfly.Server.Review;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Transcripts;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Attempts held for an instructor's review (GAD-T603): reviewers see why (the reason, each absence with time and length, the
/// student's explanation, the calibration reason), can filter the queue by it, and clear it only with a note, audited; until a
/// person decides (clearing, or approving with a note) the result is never published to the student or passed back to the LMS.
/// </summary>
[TestFixture]
public class InstructorReviewHoldTests : ServerTest
{
    private sealed record World(Guid Org, Guid SessionId, Guid ObjectiveId, Guid StudentId);

    private const string WindowReason = "The exam window was left 3 times during the attempt (longest 20 s).";

    private async Task<World> GradedByExaminerAsync()
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync();
        var objective = new LearningObjective { OrganizationId = w.Org, ExamId = w.ExamId, Key = "understanding", Title = "Understanding", MaxScore = 100, Weight = 1 };
        await using (var db = OrgDb(w.Org))
        {
            db.LearningObjectives.Add(objective);
            db.AiObjectiveGrades.Add(new AiObjectiveGrade
            {
                OrganizationId = w.Org, SessionId = sessionId, ObjectiveId = objective.Id, Level = MasteryLevel.Application, Score = 85, MaxScore = 100,
                Explanation = "AI rationale", Confidence = "high", CreatedAt = Clock.GetUtcNow(),
            });
            db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = sessionId, Status = GradeStatus.HumanReviewPending, UpdatedAt = Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        var examiner = As(w.Org, OrgRoles.Examiner, null, out var edb);
        await using (edb)
            Assert.That((await examiner.SubmitHumanGradesAsync(sessionId, [new HumanGradeInput(objective.Id, MasteryLevel.Application, 88, "Solid.", null)])).Ok, Is.True);
        return new World(w.Org, sessionId, objective.Id, w.StudentId);
    }

    private GradingService As(Guid org, string role, Guid? user, out GadflyDbContext db)
    {
        var tenant = Member(org, user ?? Guid.NewGuid(), role);
        db = Db(tenant);
        return new GradingService(db, new FixedTenant(tenant), Clock, new JobQueue(db, Clock));
    }

    private static async Task HoldAsync(Guid sessionId, string reason = WindowReason)
    {
        await using var db = SystemDb();
        await db.ExamSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.NeedsInstructorReview, true).SetProperty(s => s.ReviewReason, reason).SetProperty(s => s.ReviewFlaggedAt, DateTimeOffset.UtcNow));
    }

    private static async Task<bool> HeldAsync(Guid sessionId)
    {
        await using var db = SystemDb();
        return await db.ExamSessions.Where(s => s.Id == sessionId).Select(s => s.NeedsInstructorReview).SingleAsync();
    }

    private static async Task<bool> PublishedAsync(World w)
    {
        await using var db = SystemDb();
        return await TranscriptAccess.PublishedToStudentAsync(db, w.Org, w.SessionId, CancellationToken.None);
    }

    [Test]
    public async Task Approving_a_held_attempt_needs_a_note_and_that_approval_decides_it()
    {
        var w = await GradedByExaminerAsync();
        await HoldAsync(w.SessionId);
        var teacher = As(w.Org, OrgRoles.Instructor, null, out var tdb);
        await using (tdb)
        {
            var bare = await teacher.ApproveAsync(w.SessionId, null, null);
            Assert.That(bare.Ok, Is.False);
            Assert.That(bare.Message, Does.Contain(InstructorReview.Badge));
            Assert.That(await HeldAsync(w.SessionId), Is.True);
            var approved = await teacher.ApproveAsync(w.SessionId, null, "Read the absences and the explanation (a fire alarm); the answers stand.");
            Assert.That(approved.Ok, Is.True, approved.Message);
            Assert.That((await teacher.PublishAsync(w.SessionId, null)).Ok, Is.True);
        }
        Assert.That(await HeldAsync(w.SessionId), Is.False);
        Assert.That(await PublishedAsync(w), Is.True);
        await using var check = SystemDb();
        var audit = await check.AuditEvents.SingleAsync(a => a.SessionId == w.SessionId && a.Action == InstructorReview.DecidedByApprovalAction);
        Assert.That(audit.Reason, Does.Contain("fire alarm"));
        Assert.That(audit.BeforeJson, Does.Contain("exam window"));
    }

    [Test]
    public async Task A_held_result_is_not_published_and_clearing_needs_the_role_and_a_note()
    {
        var w = await GradedByExaminerAsync();
        var teacherId = await Seed.MemberAsync(Services, w.Org, OrgRoles.Instructor);
        var teacher = As(w.Org, OrgRoles.Instructor, teacherId, out var tdb);
        await using (tdb) Assert.That((await teacher.ApproveAsync(w.SessionId, null, null)).Ok, Is.True);
        await HoldAsync(w.SessionId);   // e.g. a late absence note marked it after approval

        var again = As(w.Org, OrgRoles.Instructor, teacherId, out var adb);
        await using (adb)
        {
            var refused = await again.PublishAsync(w.SessionId, null);
            Assert.That(refused.Ok, Is.False);
            Assert.That(refused.Message, Does.Contain(InstructorReview.Badge));
        }

        await using (var db = OrgDb(w.Org))
        {
            Assert.That((await InstructorReview.ClearAsync(db, w.Org, Guid.NewGuid(), OrgRoles.Examiner, w.SessionId, "Fine.", Clock.GetUtcNow(), default)).Ok, Is.False,
                "an examiner doesn't approve final grades, so can't clear the mark");
            Assert.That((await InstructorReview.ClearAsync(db, w.Org, teacherId, OrgRoles.Instructor, w.SessionId, "  ", Clock.GetUtcNow(), default)).Ok, Is.False,
                "a note is required");
        }
        Assert.That(await HeldAsync(w.SessionId), Is.True);
        await using (var db = OrgDb(w.Org))
            Assert.That((await InstructorReview.ClearAsync(db, w.Org, teacherId, OrgRoles.Instructor, w.SessionId, "Checked the timeline: notifications.", Clock.GetUtcNow(), default)).Ok, Is.True);
        Assert.That(await HeldAsync(w.SessionId), Is.False);

        var publisher = As(w.Org, OrgRoles.Instructor, teacherId, out var pdb);
        await using (pdb) Assert.That((await publisher.PublishAsync(w.SessionId, null)).Ok, Is.True);
        Assert.That(await PublishedAsync(w), Is.True);

        await using var check = SystemDb();
        var cleared = await check.AuditEvents.SingleAsync(a => a.SessionId == w.SessionId && a.Action == InstructorReview.ClearedAction);
        Assert.That(cleared.ActorUserId, Is.EqualTo(teacherId));
        Assert.That(cleared.Reason, Is.EqualTo("Checked the timeline: notifications."));
        Assert.That(await check.AuditEvents.AnyAsync(a => a.SessionId == w.SessionId && a.Action == "access.denied"), Is.True, "the examiner's attempt is audited");
        await using var view = OrgDb(w.Org);
        var v = await InstructorReview.LoadAsync(view, w.Org, w.SessionId, false, default);
        Assert.That(v!.Held, Is.False);
        Assert.That(v.LastDecision!.Note, Is.EqualTo("Checked the timeline: notifications."));
        Assert.That(v.Reason, Is.EqualTo(WindowReason), "the reason stays on the attempt for the record");
    }

    [Test]
    public async Task A_published_result_marked_later_is_withdrawn_from_the_student_and_the_lms()
    {
        var w = await GradedByExaminerAsync();
        var teacher = As(w.Org, OrgRoles.Instructor, null, out var tdb);
        await using (tdb)
        {
            Assert.That((await teacher.ApproveAsync(w.SessionId, null, null)).Ok, Is.True);
            Assert.That((await teacher.PublishAsync(w.SessionId, null)).Ok, Is.True);
        }
        Assert.That(await PublishedAsync(w), Is.True);
        await HoldAsync(w.SessionId);
        Assert.That(await PublishedAsync(w), Is.False, "the passback, transcript and certificate gate");
        var student = As(w.Org, OrgRoles.Student, w.StudentId, out var sdb);
        await using (sdb) Assert.That((await student.MyResultsAsync()).Any(r => r.SessionId == w.SessionId), Is.False);
    }

    [Test]
    public async Task The_review_view_shows_each_absence_the_explanation_and_the_calibration_reason_and_blind_grading_hides_the_details()
    {
        var (world, sessionId, turnId) = await Seed.EndedSessionAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);
        await using (var db = OrgDb(world.Org))
        {
            var s = await db.ExamSessions.SingleAsync(x => x.Id == sessionId);
            s.Language = "ko";
            s.NeedsInstructorReview = true;
            s.ReviewReason = Gadfly.Server.Sessions.SessionOrchestrator.CrossLingualReviewReason;
            db.IntegrityEvents.Add(new IntegrityEvent
            {
                OrganizationId = world.Org, SessionId = sessionId, Source = WindowFocusRecorder.Source, Type = WindowFocusRecorder.AbsenceType,
                StartedAt = at, EndedAt = at.AddSeconds(75), TurnId = turnId, Confidence = 0.9, Severity = IntegritySeverity.Low, Summary = "left",
            });
            db.IntegrityEvents.Add(new IntegrityEvent
            {
                OrganizationId = world.Org, SessionId = sessionId, Source = WindowFocusRecorder.NoteSource, Type = WindowFocusRecorder.NoteType,
                StartedAt = at.AddMinutes(2), Confidence = 1, Severity = IntegritySeverity.Info,
                Summary = "The student's explanation of leaving the exam window: \"My screen reader took the focus.\"",
            });
            await db.SaveChangesAsync();
        }
        await using (var db = OrgDb(world.Org))
        {
            var v = (await InstructorReview.LoadAsync(db, world.Org, sessionId, hideIntegrity: false, default))!;
            Assert.That(v.Held, Is.True);
            Assert.That(v.CalibrationReason, Is.EqualTo(Gadfly.Server.Sessions.SessionOrchestrator.CrossLingualReviewReason));
            var absence = v.Absences.Single();
            Assert.That(absence.Length, Is.EqualTo(TimeSpan.FromSeconds(75)).Within(TimeSpan.FromMilliseconds(10)));
            Assert.That(absence.Question, Is.EqualTo(1));
            Assert.That(InstructorReview.Length(absence.Length), Is.EqualTo("1 min 15 s"));
            Assert.That(v.Explanations.Single().Text, Is.EqualTo("My screen reader took the focus."));
            Assert.That(v.WindowRule, Does.Contain("3 absences"));

            var blind = (await InstructorReview.LoadAsync(db, world.Org, sessionId, hideIntegrity: true, default))!;
            Assert.That(blind.Held && blind.DetailsHidden, Is.True);
            Assert.That(blind.Absences.Count + blind.Explanations.Count, Is.Zero);
            Assert.That(blind.CalibrationReason, Is.Not.Null, "the language reason isn't an integrity flag");
            Assert.That(InstructorReview.ShortReason(v.Reason, "ko"), Does.Contain("Korean"));
        }
    }

    [Test]
    public async Task The_review_list_marks_held_attempts_and_filters_by_them()
    {
        var (world, held, _) = await Seed.EndedSessionAsync();
        await HoldAsync(held);
        await using var db = OrgDb(world.Org);
        var service = new ReviewService(db, world.Org, viewerRole: OrgRoles.Instructor);
        var all = await service.ListAsync(new ReviewListFilter());
        Assert.That(all.Single(r => r.SessionId == held).NeedsInstructorReview, Is.True);
        Assert.That(all.Single(r => r.SessionId == held).ReviewReason, Is.EqualTo(WindowReason));
        var only = await service.ListAsync(new ReviewListFilter(NeedsInstructorReviewOnly: true));
        Assert.That(only.Select(r => r.SessionId), Is.EqualTo(new[] { held }));
        await db.ExamSessions.Where(s => s.Id == held).ExecuteUpdateAsync(u => u.SetProperty(s => s.NeedsInstructorReview, false));
        Assert.That(await service.ListAsync(new ReviewListFilter(NeedsInstructorReviewOnly: true)), Is.Empty);
    }
}
