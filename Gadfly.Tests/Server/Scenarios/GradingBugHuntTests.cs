using Gadfly.Assessment;
using Gadfly.Server.Analytics;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Jobs;
using Gadfly.Server.Privacy;
using Gadfly.Server.Review;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server.Scenarios;

/// <summary>
/// Regressions for the grading, review, analytics and privacy defects found by the GAD-T315 bug hunt: a blind grader
/// reading the AI's levels through analytics or the voice measurements through the review service, an erasure leaving
/// the student's webhook deliveries behind, and reconciled scores rounded differently from submitted ones.
/// </summary>
[TestFixture]
[Category("Scenario")]
public class GradingBugHuntTests : ServerTest
{
    /// <summary>An ended session with one objective the AI graded at Application, grade record at Human Review Pending (blind).</summary>
    private async Task<(Guid Org, Guid ExamId, Guid SessionId, Guid TurnId, Guid ObjectiveId, Guid StudentId)> BlindSessionAsync(decimal aiScore = 85)
    {
        var (w, sessionId, turnId) = await Seed.EndedSessionAsync();
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
        return (w.Org, w.ExamId, sessionId, turnId, objective.Id, w.StudentId);
    }

    private static async Task<AnalyticsReport> AnalyticsAs(Guid org, Guid examId, string role)
    {
        await using var db = Db(Member(org, Guid.NewGuid(), role));
        return await new AnalyticsService(db, org, viewerRole: role).BuildAsync(new AnalyticsScope(ExamId: examId));
    }

    [Test]
    public async Task Analytics_HidesTheAiLevelsOfABlindSession_FromAGrader()
    {
        var s = await BlindSessionAsync();

        var grader = await AnalyticsAs(s.Org, s.ExamId, OrgRoles.Instructor);
        var aiRow = grader.ObjectiveMastery.Single(m => m.ObjectiveId == s.ObjectiveId && m.Source == LevelSource.Ai);
        Assert.That(aiRow.Total, Is.Zero, "the only session is still blind, so its AI level would be read straight off the chart");
        Assert.That(grader.Reliability.AiGrades, Is.Zero);
        Assert.That(grader.Sessions, Is.EqualTo(1), "the session still counts for completion");

        var admin = await AnalyticsAs(s.Org, s.ExamId, OrgRoles.Admin);
        Assert.That(admin.ObjectiveMastery.Single(m => m.ObjectiveId == s.ObjectiveId && m.Source == LevelSource.Ai).Counts[(int)MasteryLevel.Application],
            Is.EqualTo(1), "an admin enters no scores, so nothing is hidden");

        await using (var db = SystemDb())
            await db.FinalGrades.Where(g => g.SessionId == s.SessionId).ExecuteUpdateAsync(u => u.SetProperty(g => g.Status, GradeStatus.AwaitingFinalApproval));
        var after = await AnalyticsAs(s.Org, s.ExamId, OrgRoles.Instructor);
        Assert.That(after.ObjectiveMastery.Single(m => m.ObjectiveId == s.ObjectiveId && m.Source == LevelSource.Ai).Total, Is.EqualTo(1),
            "once the human evaluation is in, the grader sees it");
    }

    [Test]
    public async Task Review_GivesABlindGrader_NoVoiceMeasurements()
    {
        var s = await BlindSessionAsync();
        await using (var db = SystemDb())
            await db.SessionTurns.Where(t => t.Id == s.TurnId)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.SpeechMetricsJson, (string?)"{\"pitchHz\":182.5,\"jitter\":0.012}"));

        await using (var db = Db(Member(s.Org, Guid.NewGuid(), OrgRoles.Examiner)))
        {
            var view = await new ReviewService(db, s.Org, viewerRole: OrgRoles.Examiner).GetAsync(s.SessionId);
            Assert.That(view!.Blinded, Is.True);
            Assert.That(view.Turns.Single(t => t.TurnId == s.TurnId).SpeechMetrics, Is.Empty);
        }
        await using (var db = Db(Member(s.Org, Guid.NewGuid(), OrgRoles.Admin)))
        {
            var view = await new ReviewService(db, s.Org, viewerRole: OrgRoles.Admin).GetAsync(s.SessionId);
            Assert.That(view!.Turns.Single(t => t.TurnId == s.TurnId).SpeechMetrics, Does.ContainKey("pitchHz"));
        }
    }

    [Test]
    public async Task Erasure_RemovesTheStudentsWebhookDeliveries_AndKeepsOthers()
    {
        var s = await BlindSessionAsync();
        var hook = new Webhook { OrganizationId = s.Org, Url = "https://example.com/hook", Events = "session.completed", ProtectedSecret = "x", CreatedAt = Clock.GetUtcNow() };
        var mine = new WebhookDelivery
        {
            OrganizationId = s.Org, WebhookId = hook.Id, EventType = "session.completed", Status = DeliveryStatus.Pending, CreatedAt = Clock.GetUtcNow(),
            PayloadJson = $"{{\"type\":\"session.completed\",\"data\":{{\"sessionId\":\"{s.SessionId}\",\"studentUserId\":\"{s.StudentId}\",\"overallScore\":0.8}}}}",
        };
        var theirs = new WebhookDelivery
        {
            OrganizationId = s.Org, WebhookId = hook.Id, EventType = "session.completed", Status = DeliveryStatus.Delivered, CreatedAt = Clock.GetUtcNow(),
            PayloadJson = $"{{\"type\":\"session.completed\",\"data\":{{\"sessionId\":\"{Guid.NewGuid()}\",\"studentUserId\":\"{Guid.NewGuid()}\"}}}}",
        };
        await using (var db = OrgDb(s.Org))
        {
            db.Webhooks.Add(hook);
            db.WebhookDeliveries.AddRange(mine, theirs);
            await db.SaveChangesAsync();
        }

        await using (var scope = Scope(TenantSnapshot.ForOrganization(s.Org)))
            await scope.ServiceProvider.GetRequiredService<PrivacyService>().DeleteStudentDataAsync(s.Org, s.StudentId);

        await using var check = SystemDb();
        Assert.That(await check.WebhookDeliveries.AnyAsync(d => d.Id == mine.Id), Is.False, "the payload names the erased student and their score");
        Assert.That(await check.WebhookDeliveries.AnyAsync(d => d.Id == theirs.Id), Is.True);
    }

    [Test]
    public async Task AReconciledScore_IsRoundedHalfAwayFromZero_LikeASubmittedOne()
    {
        var s = await BlindSessionAsync(aiScore: 95);
        var scorer = Member(s.Org, Guid.NewGuid(), OrgRoles.Instructor);
        await using (var db = Db(scorer))
        {
            var r = await new GradingService(db, new FixedTenant(scorer), Clock, new JobQueue(db, Clock))
                .SubmitHumanGradesAsync(s.SessionId, [new HumanGradeInput(s.ObjectiveId, MasteryLevel.BasicExplanation, 60, null, null)]);
            Assert.That(r.Ok, Is.True, r.Message);
        }
        Guid disagreementId;
        await using (var db = SystemDb())
            disagreementId = await db.GradeDisagreements.AsNoTracking().Where(d => d.SessionId == s.SessionId && d.ResolvedAt == null).Select(d => d.Id).SingleAsync();

        var reviewer = Member(s.Org, Guid.NewGuid(), OrgRoles.SecondReviewer);
        await using (var db = Db(reviewer))
        {
            var r = await new GradingService(db, new FixedTenant(reviewer), Clock, new JobQueue(db, Clock))
                .ReconcileAsync(s.SessionId, disagreementId, ReconciliationCauses.All[0].Code, "Re-read the transcript.", 62.345m, 62.345m, null);
            Assert.That(r.Ok, Is.True, r.Message);
        }
        await using var check = SystemDb();
        var rec = await check.GradeReconciliations.AsNoTracking().SingleAsync(x => x.DisagreementId == disagreementId);
        Assert.That(rec.ResolvedHumanScore, Is.EqualTo(62.35m));
        Assert.That(rec.ResolvedAiScore, Is.EqualTo(62.35m));
    }
}
