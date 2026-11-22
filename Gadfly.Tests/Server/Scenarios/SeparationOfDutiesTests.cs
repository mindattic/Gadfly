using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Jobs;
using Gadfly.Server.Review;
using Gadfly.Server.Tenancy;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server.Scenarios;

/// <summary>
/// Separation of duties in grading (GAD-T285): the person who entered a grade's human score cannot reconcile or approve
/// it, unless the organization allows a single grader, and then each such act is audited as a waiver. On LocalDB through
/// the real GradingService, which the grading page and the grading endpoints both call.
/// </summary>
[TestFixture]
[Category("Scenario")]
public class SeparationOfDutiesTests : ServerTest
{
    private sealed record World(Guid Org, Guid SessionId, Guid ObjectiveId);

    private async Task<World> GradedByAi(decimal aiScore)
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync();
        var objective = new LearningObjective { OrganizationId = w.Org, ExamId = w.ExamId, Key = "understanding", Title = "Understanding", MaxScore = 100, Weight = 1 };
        await using var db = OrgDb(w.Org);
        db.LearningObjectives.Add(objective);
        db.AiObjectiveGrades.Add(new AiObjectiveGrade
        {
            OrganizationId = w.Org, SessionId = sessionId, ObjectiveId = objective.Id, Level = MasteryLevel.Application, Score = aiScore, MaxScore = 100,
            Explanation = "AI rationale", Confidence = "high", CreatedAt = Clock.GetUtcNow(),
        });
        db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = sessionId, Status = GradeStatus.HumanReviewPending, UpdatedAt = Clock.GetUtcNow() });
        await db.SaveChangesAsync();
        return new World(w.Org, sessionId, objective.Id);
    }

    private GradingService As(Guid org, string role, Guid user, out GadflyDbContext db)
    {
        var tenant = Member(org, user, role);
        db = Db(tenant);
        return new GradingService(db, new FixedTenant(tenant), Clock, new JobQueue(db, Clock));
    }

    private static async Task AllowSingleGrader(Guid org)
    {
        await using var sys = SystemDb();
        await sys.OrganizationSettings.Where(s => s.OrganizationId == org).ExecuteUpdateAsync(u => u.SetProperty(s => s.AllowSingleGrader, true));
    }

    private static async Task<GradeStatus> Status(Guid sessionId)
    {
        await using var db = SystemDb();
        return await db.FinalGrades.AsNoTracking().Where(g => g.SessionId == sessionId).Select(g => g.Status).SingleAsync();
    }

    private static async Task<int> Audited(Guid sessionId, string action, Guid actor)
    {
        await using var db = SystemDb();
        return await db.AuditEvents.AsNoTracking().CountAsync(a => a.SessionId == sessionId && a.Action == action && a.ActorUserId == actor);
    }

    private async Task Score(World w, Guid user, decimal score)
    {
        var scorer = As(w.Org, OrgRoles.Instructor, user, out var db);
        await using (db)
        {
            var r = await scorer.SubmitHumanGradesAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, MasteryLevel.Application, score, null, null)]);
            Assert.That(r.Ok, Is.True, r.Message);
        }
    }

    [Test]
    public async Task TheScorer_CannotApproveTheirOwnGrade_AndSomeoneElseCan()
    {
        var w = await GradedByAi(85);
        var scorerId = Guid.NewGuid();
        await Score(w, scorerId, 88);
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.AwaitingFinalApproval));

        var scorer = As(w.Org, OrgRoles.Instructor, scorerId, out var sdb);
        await using (sdb)
        {
            var refused = await scorer.ApproveAsync(w.SessionId, "B+", null);
            Assert.That(refused.Ok, Is.False);
            Assert.That(refused.Message, Does.StartWith("Refused:"), "the endpoints answer 409 for this");
            Assert.That(refused.Message, Does.Contain("separation of duties"));
            var view = await scorer.WorkspaceAsync(w.SessionId);
            Assert.That(view!.ViewerScoredHuman, Is.True);
            Assert.That(view.SeparationBlocksViewer, Is.True);
        }
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.AwaitingFinalApproval), "nothing moved");
        Assert.That(await Audited(w.SessionId, "grade.separation-of-duties.refused", scorerId), Is.EqualTo(1));

        var otherId = Guid.NewGuid();
        var other = As(w.Org, OrgRoles.Instructor, otherId, out var odb);
        await using (odb)
        {
            Assert.That((await other.WorkspaceAsync(w.SessionId))!.SeparationBlocksViewer, Is.False);
            var approved = await other.ApproveAsync(w.SessionId, "B+", null);
            Assert.That(approved.Ok, Is.True, approved.Message);
        }
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.Finalized));
        Assert.That(await Audited(w.SessionId, GradingService.SeparationWaivedAction, otherId), Is.Zero, "no waiver was needed");
    }

    [Test]
    public async Task TheScorer_CannotReconcileTheirOwnGrade_AndASecondReviewerCan()
    {
        var w = await GradedByAi(95);
        var scorerId = Guid.NewGuid();
        await Score(w, scorerId, 60);
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.ReconciliationRequired));
        Guid disagreementId;
        await using (var db = SystemDb())
            disagreementId = await db.GradeDisagreements.AsNoTracking().Where(x => x.SessionId == w.SessionId && x.ResolvedAt == null).Select(x => x.Id).SingleAsync();

        var scorer = As(w.Org, OrgRoles.Instructor, scorerId, out var sdb);
        await using (sdb)
        {
            var refused = await scorer.ReconcileAsync(w.SessionId, disagreementId, ReconciliationCauses.All[0].Code, "I was right.", null, 60, null);
            Assert.That(refused.Ok, Is.False);
            Assert.That(refused.Message, Does.StartWith("Refused:"));
        }
        await using (var db = SystemDb())
        {
            Assert.That(await db.GradeReconciliations.AnyAsync(r => r.DisagreementId == disagreementId), Is.False);
            Assert.That(await db.GradeDisagreements.AsNoTracking().Where(x => x.Id == disagreementId).Select(x => x.ResolvedAt).SingleAsync(), Is.Null);
        }
        Assert.That(await Audited(w.SessionId, "grade.separation-of-duties.refused", scorerId), Is.EqualTo(1));

        var reviewer = As(w.Org, OrgRoles.SecondReviewer, Guid.NewGuid(), out var rdb);
        await using (rdb)
        {
            var r = await reviewer.ReconcileAsync(w.SessionId, disagreementId, ReconciliationCauses.All[0].Code, "The transcript shows no application.", null, 62, null);
            Assert.That(r.Ok, Is.True, r.Message);
        }
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.AwaitingFinalApproval));
    }

    [Test]
    public async Task ASingleGraderOrganization_MayDoAllThree_AndEachWaiverIsAudited()
    {
        var w = await GradedByAi(95);
        await AllowSingleGrader(w.Org);
        var soloId = Guid.NewGuid();
        await Score(w, soloId, 60);
        Guid disagreementId;
        await using (var db = SystemDb())
            disagreementId = await db.GradeDisagreements.AsNoTracking().Where(x => x.SessionId == w.SessionId && x.ResolvedAt == null).Select(x => x.Id).SingleAsync();

        var solo = As(w.Org, OrgRoles.Instructor, soloId, out var sdb);
        await using (sdb)
        {
            var view = await solo.WorkspaceAsync(w.SessionId);
            Assert.That(view!.ViewerScoredHuman, Is.True);
            Assert.That(view.SeparationBlocksViewer, Is.False);
            var reconciled = await solo.ReconcileAsync(w.SessionId, disagreementId, ReconciliationCauses.All[0].Code, "Re-read the transcript.", null, 62, null);
            Assert.That(reconciled.Ok, Is.True, reconciled.Message);
        }
        var again = As(w.Org, OrgRoles.Instructor, soloId, out var adb);
        await using (adb)
        {
            var approved = await again.ApproveAsync(w.SessionId, null, null);
            Assert.That(approved.Ok, Is.True, approved.Message);
        }
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.Finalized));
        Assert.That(await Audited(w.SessionId, GradingService.SeparationWaivedAction, soloId), Is.EqualTo(2), "one for the reconciliation, one for the approval");
        Assert.That(await Audited(w.SessionId, "grade.separation-of-duties.refused", soloId), Is.Zero);
    }

    [Test]
    public async Task ARefusedApproval_ForAnotherReason_RecordsNoWaiver()
    {
        var w = await GradedByAi(95);
        await AllowSingleGrader(w.Org);
        var soloId = Guid.NewGuid();
        await Score(w, soloId, 60);   // a mandatory disagreement is open, so approval is refused whoever asks
        var solo = As(w.Org, OrgRoles.Instructor, soloId, out var sdb);
        await using (sdb) Assert.That((await solo.ApproveAsync(w.SessionId, null, null)).Ok, Is.False);
        Assert.That(await Audited(w.SessionId, GradingService.SeparationWaivedAction, soloId), Is.Zero);
    }
}

/// <summary>
/// Integrity flags wait for the blind score (GAD-T285, GAD-T284): while a session's grading is blind, a grader gets no
/// integrity event (gaze, recitation, text style or any other) from the review service, which the review page, its
/// exports and the review list all read.
/// </summary>
[TestFixture]
[Category("Scenario")]
public class BlindIntegrityTests : ServerTest
{
    private static async Task<(Guid Org, Guid SessionId)> SessionWithFlags()
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync();
        await using var db = OrgDb(w.Org);
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);
        db.IntegrityEvents.Add(new IntegrityEvent
        {
            OrganizationId = w.Org, SessionId = sessionId, TurnId = turnId, Source = "text", Type = IntegrityExplanations.TextStyleEventType,
            StartedAt = at, Confidence = 0.8, Severity = IntegritySeverity.Low, Summary = "Unvalidated estimate.",
        });
        db.IntegrityEvents.Add(new IntegrityEvent
        {
            OrganizationId = w.Org, SessionId = sessionId, TurnId = turnId, Source = "webcam", Type = "second-face",
            StartedAt = at, Confidence = 0.9, Severity = IntegritySeverity.Medium, Summary = "A second face.",
        });
        await db.SaveChangesAsync();
        return (w.Org, sessionId);
    }

    private static async Task<(SessionReviewView View, ReviewListRow Row)> ReadAs(Guid org, Guid sessionId, string role)
    {
        await using var db = Db(Member(org, Guid.NewGuid(), role));
        var review = new ReviewService(db, org, viewerRole: role);
        var view = await review.GetAsync(sessionId);
        var row = (await review.ListAsync(new ReviewListFilter())).Single(r => r.SessionId == sessionId);
        return (view!, row);
    }

    [Test]
    public async Task ABlindGrader_SeesNoIntegrityFlags_UntilTheHumanScoreIsIn()
    {
        var (org, sessionId) = await SessionWithFlags();

        var (blind, blindRow) = await ReadAs(org, sessionId, OrgRoles.Examiner);
        Assert.That(blind.Blinded, Is.True, "an ended session under the default (blind) policy");
        Assert.That(blind.Integrity, Is.Empty);
        Assert.That(blind.Turns.All(t => t.Recitation is null && t.Judgment is null), Is.True);
        Assert.That(blindRow.FlagsHidden, Is.True);
        Assert.That(blindRow.IntegrityFlags, Is.Zero);
        var csv = ReviewService.ToCsv(blind);
        Assert.That(csv, Does.Not.Contain(IntegrityExplanations.TextStyleEventType));
        Assert.That(csv, Does.Not.Contain("second-face"));

        // An admin does not enter scores, so nothing is hidden from them.
        var (admin, adminRow) = await ReadAs(org, sessionId, OrgRoles.Admin);
        Assert.That(admin.Blinded, Is.False);
        Assert.That(admin.Integrity.Select(e => e.Type), Does.Contain(IntegrityExplanations.TextStyleEventType).And.Contain("second-face"));
        Assert.That(adminRow.FlagsHidden, Is.False);
        Assert.That(adminRow.IntegrityFlags, Is.GreaterThanOrEqualTo(1));

        // Once the human evaluation is submitted the grade leaves Human Review Pending, and the grader sees them.
        await using (var db = OrgDb(org))
        {
            db.FinalGrades.Add(new FinalGrade { OrganizationId = org, SessionId = sessionId, Status = GradeStatus.AwaitingFinalApproval, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var (after, afterRow) = await ReadAs(org, sessionId, OrgRoles.Examiner);
        Assert.That(after.Blinded, Is.False);
        Assert.That(after.Integrity.Select(e => e.Type), Does.Contain(IntegrityExplanations.TextStyleEventType).And.Contain("second-face"));
        Assert.That(afterRow.FlagsHidden, Is.False);
    }
}

/// <summary>The text-style estimate's firing rate over recorded sessions (GAD-T284): what gadfly textstyle-rate prints.</summary>
[TestFixture]
public class TextStyleRateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private static SocraticSession Session(params double?[] estimates)
    {
        var s = new SocraticSession("subject", new SessionPolicy(), ExamPrompts.Set, T0);
        for (var i = 0; i < estimates.Length; i++)
        {
            s.Ask("a", MoveKind.Elicit, $"Question {i}?", T0.AddMinutes(i));
            s.RecordAnswer(new Answer { Text = $"Answer {i}.", CompletedAt = T0.AddMinutes(i).AddSeconds(30) });
            s.RecordJudgment(new AnswerJudgment
            {
                Criteria = [], OverallScore = 0.6, Summary = "ok",
                TextStyle = estimates[i] is { } e ? new TextStyleAssessment(e, "reason", 2) : null,
            }, 0.5);
        }
        return s;
    }

    [Test]
    public void Measure_CountsAnswersAtOrAboveTheReviewerThreshold()
    {
        // Session one: no estimate for the first two answers (no baseline yet), then low, middling, and exactly at the threshold.
        // Session two: nothing reaches it. Session three: no estimates at all.
        var rate = TextStyleAssessor.Measure([
            Session(null, null, 0.1, 0.5, TextStyleAssessor.ReviewerThreshold),
            Session(null, null, 0.2, 0.69),
            Session(null, null),
        ]);
        Assert.Multiple(() =>
        {
            Assert.That(rate.Sessions, Is.EqualTo(3));
            Assert.That(rate.Answers, Is.EqualTo(11));
            Assert.That(rate.AssessedAnswers, Is.EqualTo(5));
            Assert.That(rate.Flagged, Is.EqualTo(1));
            Assert.That(rate.Middling, Is.EqualTo(2));
            Assert.That(rate.SessionsAssessed, Is.EqualTo(2));
            Assert.That(rate.SessionsFlagged, Is.EqualTo(1));
            Assert.That(rate.FlagRate, Is.EqualTo(0.2).Within(1e-9));
            Assert.That(rate.SessionRate, Is.EqualTo(0.5).Within(1e-9));
        });
    }

    [Test]
    public void Measure_WithNoEstimates_HasNoRate()
    {
        var rate = TextStyleAssessor.Measure([Session(null, null)]);
        Assert.That(rate.FlagRate, Is.Null);
        Assert.That(rate.SessionRate, Is.Null);
        Assert.That(TextStyleAssessor.Measure([]).Sessions, Is.Zero);
    }

    [Test]
    public void EveryReading_SaysTheEstimateIsUnvalidated()
    {
        foreach (var score in new[] { 0.0, 0.39, 0.4, 0.69, 0.7, 1.0 })
            Assert.That(IntegrityExplanations.TextStyle(score), Does.StartWith("Unvalidated estimate"), score.ToString());
        var (label, explanation, normal) = IntegrityExplanations.For(IntegrityExplanations.TextStyleEventType, "text");
        Assert.That(label, Does.Contain("unvalidated"));
        Assert.That(explanation, Does.Contain("never a verdict"));
        Assert.That(normal, Is.False);
    }
}
