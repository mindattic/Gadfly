using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Review;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>
/// Decision D25, default A (GAD-T218): while grading is blind, the exam-time judge's per-answer stance, notes and
/// judgments reach no surface; once the AI is visible they show as before. Written, not run.
/// </summary>
[TestFixture]
public class BlindEvidenceTests
{
    private static GradingWorkspace Workspace(bool aiVisible, Guid responseId)
    {
        var objective = new LearningObjective { Key = "obj", Title = "Photosynthesis", MaxScore = 100, Weight = 1 };
        var evidence = new AssessmentEvidence { ObjectiveId = objective.Id, ResponseId = responseId, Stance = EvidenceStance.Misconception, Note = "SECRETJUDGENOTE" };
        // Mirrors GradingService.LoadAsync: evidence is only attached to the view while the AI evaluation is visible.
        var view = new ObjectiveView(objective, null, null, [], aiVisible ? [evidence] : [], null, false, null, []);
        return new GradingWorkspace
        {
            Session = new ExamSession(), ExamTitle = "Exam", StudentName = "Student", Grade = new FinalGrade { Status = GradeStatus.HumanReviewPending },
            Policy = new PolicySnapshot(), AiVisible = aiVisible, Responses = [], Objectives = [view], Combined = null,
            Approvals = [], Appeals = [], Audit = [], Names = new Dictionary<Guid, string>(), SecondReviewers = [],
            RequestedSecondReviews = new Dictionary<Guid, Guid>(), Viewer = GradingActor.System,
        };
    }

    [Test]
    public void WhileBlind_TheAiVisibleRuleHidesEvidenceForBlindPolicyBeforeHumanSubmission()
    {
        var blind = new PolicySnapshot { Rules = new GradingPolicyRules { BlindHumanGrading = true } };
        Assert.That(GradingService.AiVisible(blind, GradeStatus.HumanReviewPending), Is.False);
        Assert.That(GradingService.AiVisible(blind, GradeStatus.AiGradingPending), Is.False);
        Assert.That(GradingService.AiVisible(blind, GradeStatus.Finalized), Is.True);
    }

    [Test]
    public void WhileBlind_TheReportCarriesNoEvidenceChosenByTheJudge()
    {
        var report = AssessmentReport.Build(Workspace(aiVisible: false, Guid.NewGuid()), forStudent: false);
        Assert.That(report.Objectives.Single().Evidence, Is.Empty);
        Assert.That(report.ToMarkdown(), Does.Not.Contain("SECRETJUDGENOTE").And.Not.Contain("Misconception"));
        Assert.That(report.ToCsv(), Does.Not.Contain("SECRETJUDGENOTE"));
    }

    [Test]
    public void BlindRuleAppliesToRolesThatCanEnterScores_NotToOwnersOrAdmins()
    {
        Assert.That(BlindGrading.AppliesToRole(AssessmentRoles.HumanExaminer), Is.True);
        Assert.That(BlindGrading.AppliesToRole(AssessmentRoles.Teacher), Is.True);
        Assert.That(BlindGrading.AppliesToRole(AssessmentRoles.Owner), Is.False);
        Assert.That(BlindGrading.AppliesToRole(AssessmentRoles.InstitutionAdmin), Is.False);
        Assert.That(BlindGrading.AppliesToRole(null), Is.False);
    }

    [Test]
    public void BlindedReviewExports_ShowNoJudgmentScoresSummariesVotesOrOverrideLine()
    {
        var turn = new TurnView(Guid.NewGuid(), 1, "k", "Concept", "probe", "Q?", null, DateTimeOffset.UtcNow, "answer", null, null, null, false, null,
            new Dictionary<string, double>(), null, []);
        var session = new ExamSession { State = SessionState.Ended };
        var grade = new GradeDecision { Grade = "B", OverridesRecommendation = true, DecidedAt = DateTimeOffset.UtcNow };
        var view = new SessionReviewView(session, "Exam", Guid.Empty, "Student", null, [turn], [], [], [], null, [(grade, "Teacher")]) { Blinded = true };
        var md = ReviewService.ToMarkdown(view);
        Assert.That(md, Does.Contain("hidden until the human evaluation"));
        Assert.That(md, Does.Not.Contain("Overrides"));
        Assert.That(ReviewService.ToCsv(view), Does.Contain("answer"));
    }
}
