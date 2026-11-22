using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server.Scenarios;

/// <summary>
/// Scenarios F, G and J through the real grading workflow (GradingService, GAD-T95..T98) on LocalDB: an ended
/// session with one learning objective and an AI grade, then the human examiner, reconciliation, approval,
/// publication and unauthorized attempts, each checked against the stored grade and the audit log.
/// </summary>
[TestFixture]
[Category("Scenario")]
public class GradingWorkflowScenarioTests : ServerTest
{
    private sealed record World(Guid Org, Guid SessionId, Guid ObjectiveId, Guid StudentId);

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
        return new World(w.Org, sessionId, objective.Id, w.StudentId);
    }

    private GradingService As(Guid org, string role, Guid? user, out GadflyDbContext db)
    {
        var tenant = Member(org, user ?? Guid.NewGuid(), role);
        db = Db(tenant);
        return new GradingService(db, new FixedTenant(tenant), Clock, new JobQueue(db, Clock));
    }

    private static async Task<FinalGrade> Grade(Guid sessionId)
    {
        await using var db = SystemDb();
        return await db.FinalGrades.AsNoTracking().SingleAsync(g => g.SessionId == sessionId);
    }

    private static async Task<List<AuditEvent>> Denials(Guid sessionId)
    {
        await using var db = SystemDb();
        return await db.AuditEvents.AsNoTracking().Where(a => a.SessionId == sessionId && a.Action == "access.denied").ToListAsync();
    }

    [Test]
    public async Task Scenario_F_AiHumanScoringAgreement_FullWorkflow()
    {
        var w = await GradedByAi(85);
        var examiner = As(w.Org, OrgRoles.Examiner, null, out var edb);
        await using (edb)
        {
            var r = await examiner.SubmitHumanGradesAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, MasteryLevel.Application, 88, "Solid.", null)]);
            Assert.That(r.Ok, Is.True, r.Message);
        }
        var g = await Grade(w.SessionId);
        Assert.That(g.Status, Is.EqualTo(GradeStatus.AwaitingFinalApproval), "agreement requests final approval");
        Assert.That(g.FinalPercent, Is.EqualTo(86.8m), "85 x 0.4 + 88 x 0.6");

        // The examiner may not approve their own work; an instructor approves, then publishes.
        var again = As(w.Org, OrgRoles.Examiner, null, out var e2);
        await using (e2) Assert.That((await again.ApproveAsync(w.SessionId, "B+", null)).Ok, Is.False);
        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.AwaitingFinalApproval));

        var teacher = As(w.Org, OrgRoles.Instructor, null, out var tdb);
        await using (tdb)
        {
            var approved = await teacher.ApproveAsync(w.SessionId, "B+", "Agreed.");
            Assert.That(approved.Ok, Is.True, approved.Message);
        }
        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.Finalized));
        var publisher = As(w.Org, OrgRoles.Instructor, null, out var pdb);
        await using (pdb) Assert.That((await publisher.PublishAsync(w.SessionId, null)).Ok, Is.True);
        Assert.That(GradeStatusMachine.IsVisibleToStudent((await Grade(w.SessionId)).Status), Is.True);
        await using var check = SystemDb();
        Assert.That(await check.ApprovalRecords.CountAsync(a => a.SessionId == w.SessionId && a.Action == ApprovalAction.Finalize), Is.EqualTo(1));
        Assert.That(await check.HumanObjectiveGrades.AsNoTracking().Where(h => h.SessionId == w.SessionId).Select(h => h.WasBlind).SingleAsync(), Is.True,
            "the default policy grades blind");
    }

    [Test]
    public async Task HumanGradesResubmittedAfterReopen_AreNotRecordedAsBlind()
    {
        var w = await GradedByAi(85);
        var examiner = As(w.Org, OrgRoles.Examiner, null, out var edb);
        await using (edb)
        {
            Assert.That((await examiner.SubmitHumanGradesAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, MasteryLevel.Application, 88, null, null)])).Ok, Is.True);
            Assert.That((await examiner.ReopenHumanReviewAsync(w.SessionId, "Adjusting after seeing the AI's evaluation.")).Ok, Is.True);
            Assert.That((await examiner.SubmitHumanGradesAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, MasteryLevel.Application, 80, null, null)])).Ok, Is.True);
        }
        await using var check = SystemDb();
        var rows = await check.HumanObjectiveGrades.AsNoTracking().Where(h => h.SessionId == w.SessionId).Select(h => h.WasBlind).ToListAsync();
        Assert.That(rows.Count(b => b), Is.EqualTo(1), "only the first submission was blind");
        Assert.That(rows.Count(b => !b), Is.EqualTo(1), "the resubmission came after the AI scores were visible");
    }

    [Test]
    public async Task Amend_RefusesAnUndefinedLevel_AndTreatsASameScoreAmendmentAsNoChange()
    {
        var w = await GradedByAi(85);
        var examiner = As(w.Org, OrgRoles.Examiner, null, out var edb);
        await using (edb)
            Assert.That((await examiner.SubmitHumanGradesAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, MasteryLevel.Application, 88.125m, null, null)])).Ok, Is.True);
        var teacher = As(w.Org, OrgRoles.Instructor, null, out var tdb);
        await using (tdb)
        {
            Assert.That((await teacher.ApproveAsync(w.SessionId, null, null)).Ok, Is.True);
            Assert.That((await teacher.PublishAsync(w.SessionId, null)).Ok, Is.True);
            var bad = await teacher.AmendAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, (MasteryLevel)99, 70, null, null)], "Correcting the level.", null);
            Assert.That(bad.Ok, Is.False);
            Assert.That(bad.Message, Does.Contain("level"));
            var same = await teacher.AmendAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, MasteryLevel.Application, 88.125m, null, null)], "Nothing actually changes.", null);
            Assert.That(same.Ok, Is.False, "88.125 is stored as 88.13, so resubmitting it is no change");
            Assert.That(same.Message, Does.Contain("Change at least one"));
        }
    }

    [Test]
    public async Task Scenario_G_AiHumanScoringDisagreement_FullWorkflow()
    {
        var w = await GradedByAi(95);
        var examiner = As(w.Org, OrgRoles.Examiner, null, out var edb);
        await using (edb)
            Assert.That((await examiner.SubmitHumanGradesAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, MasteryLevel.BasicExplanation, 60, null, null)])).Ok, Is.True);

        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.ReconciliationRequired), "the disagreement is flagged and reconciliation starts");
        Guid disagreementId;
        await using (var db = SystemDb())
        {
            var d = await db.GradeDisagreements.AsNoTracking().SingleAsync(x => x.SessionId == w.SessionId && x.ResolvedAt == null);
            Assert.That(d.Band, Is.EqualTo(DisagreementBand.MandatoryReconciliation));
            Assert.That(d.DifferencePoints, Is.EqualTo(35m));
            disagreementId = d.Id;
        }

        // No automatic finalization, and no publication without approval.
        var teacher = As(w.Org, OrgRoles.Instructor, null, out var tdb);
        await using (tdb)
        {
            Assert.That((await teacher.ApproveAsync(w.SessionId, null, null)).Ok, Is.False);
            Assert.That((await teacher.PublishAsync(w.SessionId, null)).Ok, Is.False);
        }
        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.ReconciliationRequired));

        // An authorized reviewer documents the resolution; only then can the grade be approved.
        var reviewer = As(w.Org, OrgRoles.SecondReviewer, null, out var rdb);
        await using (rdb)
        {
            var r = await reviewer.ReconcileAsync(w.SessionId, disagreementId, ReconciliationCauses.All[0].Code,
                "The AI credited a memorized definition; the transcript shows no application.", null, 62, null);
            Assert.That(r.Ok, Is.True, r.Message);
        }
        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.AwaitingFinalApproval));
        var approver = As(w.Org, OrgRoles.Instructor, null, out var adb);
        await using (adb) Assert.That((await approver.ApproveAsync(w.SessionId, null, null)).Ok, Is.True);
        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.Finalized));
        await using var check = SystemDb();
        Assert.That(await check.GradeReconciliations.AnyAsync(x => x.DisagreementId == disagreementId), Is.True);
        Assert.That(await check.AuditEvents.AnyAsync(a => a.SessionId == w.SessionId && a.Action == "grade.reconciled"), Is.True);
    }

    [Test]
    public async Task Scenario_J_UnauthorizedGradeModification_FullWorkflow()
    {
        var w = await GradedByAi(80);
        var examiner = As(w.Org, OrgRoles.Examiner, null, out var edb);
        await using (edb) await examiner.SubmitHumanGradesAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, MasteryLevel.Application, 82, null, null)]);
        var teacher = As(w.Org, OrgRoles.Instructor, null, out var tdb);
        await using (tdb)
        {
            await teacher.ApproveAsync(w.SessionId, null, null);
            await teacher.PublishAsync(w.SessionId, null);
        }
        var published = await Grade(w.SessionId);
        Assert.That(published.Status, Is.EqualTo(GradeStatus.Published));

        var change = new[] { new HumanGradeInput(w.ObjectiveId, MasteryLevel.TransferAndCriticalReasoning, 100, null, null) };
        foreach (var (role, user) in new[] { (OrgRoles.Student, w.StudentId), (OrgRoles.Examiner, Guid.NewGuid()), (OrgRoles.SecondReviewer, Guid.NewGuid()) })
        {
            var svc = As(w.Org, role, user, out var db);
            await using (db)
            {
                var r = await svc.AmendAsync(w.SessionId, change, "I want a better grade", null);
                Assert.That(r.Ok, Is.False, role);
                Assert.That(r.Message, Does.Contain("recorded"), role);
            }
        }
        var after = await Grade(w.SessionId);
        Assert.That(after.FinalPercent, Is.EqualTo(published.FinalPercent), "the action is rejected");
        Assert.That(after.Status, Is.EqualTo(GradeStatus.Published));
        var denied = await Denials(w.SessionId);
        Assert.That(denied.Select(d => d.ActorRole), Is.SupersetOf(new[] { OrgRoles.Student, OrgRoles.Examiner, OrgRoles.SecondReviewer }), "and logged");

        // An authorized amendment goes through, with its reason.
        var amender = As(w.Org, OrgRoles.Instructor, null, out var adb);
        await using (adb) Assert.That((await amender.AmendAsync(w.SessionId, change, "Appeal upheld after re-reading turn 3.", null)).Ok, Is.True);
        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.Amended));
    }

    /// <summary>GAD-T324: amending a grade under appeal without naming the appeal is refused, so the appeal is never left open behind it.</summary>
    [Test]
    public async Task AmendingAGradeUnderAppeal_MustNameTheAppeal()
    {
        var w = await GradedByAi(80);
        var examiner = As(w.Org, OrgRoles.Examiner, null, out var edb);
        await using (edb) await examiner.SubmitHumanGradesAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, MasteryLevel.Application, 82, null, null)]);
        var teacher = As(w.Org, OrgRoles.Instructor, null, out var tdb);
        await using (tdb)
        {
            await teacher.ApproveAsync(w.SessionId, null, null);
            await teacher.PublishAsync(w.SessionId, null);
        }
        var student = As(w.Org, OrgRoles.Student, w.StudentId, out var sdb);
        await using (sdb) Assert.That((await student.AppealAsync(w.SessionId, "Turn 3 was misread.")).Ok, Is.True);
        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.UnderAppeal));

        var change = new[] { new HumanGradeInput(w.ObjectiveId, MasteryLevel.TransferAndCriticalReasoning, 95, null, null) };
        var amender = As(w.Org, OrgRoles.Instructor, null, out var adb);
        await using (adb)
        {
            var r = await amender.AmendAsync(w.SessionId, change, "Re-read turn 3.", null);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Message, Does.Contain("under appeal"));
        }
        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.UnderAppeal), "nothing changed");

        Guid appealId;
        await using (var check = SystemDb())
            appealId = await check.GradeAppeals.Where(a => a.SessionId == w.SessionId).Select(a => a.Id).SingleAsync();
        var amender2 = As(w.Org, OrgRoles.Instructor, null, out var adb2);
        await using (adb2) Assert.That((await amender2.AmendAsync(w.SessionId, change, "Re-read turn 3.", appealId)).Ok, Is.True);
        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.Amended));
        await using var after = SystemDb();
        Assert.That((await after.GradeAppeals.SingleAsync(a => a.Id == appealId)).State, Is.EqualTo(AppealState.Amended));
    }

    [Test]
    public async Task Scenario_J_AStudentCannotEnterScoresOrApprove()
    {
        var w = await GradedByAi(70);
        var student = As(w.Org, OrgRoles.Student, w.StudentId, out var db);
        await using (db)
        {
            Assert.That((await student.SubmitHumanGradesAsync(w.SessionId, [new HumanGradeInput(w.ObjectiveId, MasteryLevel.Application, 100, null, null)])).Ok, Is.False);
            Assert.That((await student.ApproveAsync(w.SessionId, "A", null)).Ok, Is.False);
            Assert.That((await student.PublishAsync(w.SessionId, null)).Ok, Is.False);
        }
        Assert.That((await Grade(w.SessionId)).Status, Is.EqualTo(GradeStatus.HumanReviewPending));
        await using var check = SystemDb();
        Assert.That(await check.HumanObjectiveGrades.AnyAsync(h => h.SessionId == w.SessionId), Is.False);
        Assert.That((await Denials(w.SessionId)).Count, Is.GreaterThanOrEqualTo(3));
    }
}

/// <summary>Blind human grading (Part 8.3): the AI's scores stay hidden from the examiner until their own scores are in.</summary>
[TestFixture]
public class BlindGradingRuleTests
{
    private static PolicySnapshot Policy(bool blind) => new() { Rules = new GradingPolicyRules { BlindHumanGrading = blind } };

    [TestCase(null)]
    [TestCase(GradeStatus.ExaminationInProgress)]
    [TestCase(GradeStatus.ExaminationCompleted)]
    [TestCase(GradeStatus.TranscriptVerificationPending)]
    [TestCase(GradeStatus.AiGradingPending)]
    [TestCase(GradeStatus.HumanReviewPending)]
    public void BlindPolicy_HidesTheAiUntilTheHumanHasScored(GradeStatus? status) =>
        Assert.That(GradingService.AiVisible(Policy(blind: true), status), Is.False);

    [TestCase(GradeStatus.ReconciliationRequired)]
    [TestCase(GradeStatus.AwaitingFinalApproval)]
    [TestCase(GradeStatus.Finalized)]
    [TestCase(GradeStatus.Published)]
    [TestCase(GradeStatus.UnderAppeal)]
    [TestCase(GradeStatus.Amended)]
    public void BlindPolicy_ShowsTheAiForComparisonAfterwards(GradeStatus status) =>
        Assert.That(GradingService.AiVisible(Policy(blind: true), status), Is.True);

    [Test]
    public void NonBlindPolicy_AlwaysShowsTheAi()
    {
        foreach (var s in Enum.GetValues<GradeStatus>())
            Assert.That(GradingService.AiVisible(Policy(blind: false), s), Is.True, s.ToString());
    }

    [Test]
    public void TheCurrentHumanGrade_IsTheNewestNotSuperseded()
    {
        var objective = Guid.NewGuid();
        var first = new HumanObjectiveGrade { ObjectiveId = objective, Score = 60, SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var second = new HumanObjectiveGrade { ObjectiveId = objective, Score = 70, SupersedesId = first.Id, SubmittedAt = DateTimeOffset.UtcNow };
        var other = new HumanObjectiveGrade { ObjectiveId = Guid.NewGuid(), Score = 10, SubmittedAt = DateTimeOffset.UtcNow };
        Assert.That(GradingService.CurrentHuman([first, second, other], objective), Is.SameAs(second));
        Assert.That(GradingService.CurrentHuman([other], objective), Is.Null);
    }

    [Test]
    public void InsufficientAiEvidence_IsLeftOut_NotScoredZero()
    {
        var o = new LearningObjective { Key = "k", MaxScore = 100, Weight = 1 };
        var ai = new AiObjectiveGrade { Level = MasteryLevel.InsufficientEvidence, Score = 0, MaxScore = 100 };
        var human = new HumanObjectiveGrade { Score = 70, MaxScore = 100 };
        var (result, excluded) = GradingService.TryCompute(new PolicySnapshot(), [(o, ai, human, null)]);
        Assert.That(excluded, Does.Contain("k"));
        Assert.That(result!.FinalPercent, Is.EqualTo(70m), "the human's score stands alone");
    }
}
