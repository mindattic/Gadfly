using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>
/// The assessment report and exports must not show anything AI-derived while blind grading hides the AI's
/// evaluation (Part 8.3), including the AI's "insufficient evidence" outcome (GAD-T213).
/// </summary>
[TestFixture]
public class BlindReportTests
{
    private static GradingWorkspace Workspace(bool aiVisible, MasteryLevel humanLevel = MasteryLevel.Application)
    {
        var objective = new LearningObjective { Key = "obj", Title = "Photosynthesis", MaxScore = 100, Weight = 1 };
        var ai = new AiObjectiveGrade
        {
            ObjectiveId = objective.Id, Level = MasteryLevel.InsufficientEvidence, Score = 0, MaxScore = 100, IsMock = true,
            Confidence = "SECRETCONF", Explanation = "SECRETEXPLANATION", CriteriaJson = "{}", EvidenceRefsJson = "[]",
            MisconceptionsJson = "[]", AmbiguitiesJson = "[]", ReviewFlagsJson = "[]",
        };
        var human = new HumanObjectiveGrade { ObjectiveId = objective.Id, Level = humanLevel, Score = 50, MaxScore = 100 };
        var disagreement = new GradeDisagreement { ObjectiveId = objective.Id };
        var rec = new GradeReconciliation { DisagreementId = disagreement.Id, Cause = "both-defensible", Resolution = "SECRETRESOLUTION" };
        var view = new ObjectiveView(objective, AiGradeView.From(ai), human, [human], [], null, AiExcluded: true, disagreement, [rec]);
        return new GradingWorkspace
        {
            Session = new ExamSession(), ExamTitle = "Exam", StudentName = "Student",
            Grade = new FinalGrade { Status = GradeStatus.HumanReviewPending, FinalPercent = 77, LetterOrLabel = "SECRETLABEL" },
            Policy = new PolicySnapshot(), AiVisible = aiVisible, Responses = [], Objectives = [view], Combined = null,
            Approvals = [], Appeals = [], Audit = [], Names = new Dictionary<Guid, string>(), SecondReviewers = [],
            RequestedSecondReviews = new Dictionary<Guid, Guid>(), Viewer = GradingActor.System,
        };
    }

    private static readonly string[] Secrets =
        ["insufficient evidence", "SECRETCONF", "SECRETEXPLANATION", "SECRETRESOLUTION", "SECRETLABEL", "MOCK", "not enough evidence was gathered"];

    [Test]
    public void WhileBlind_NoAiDerivedOutcomeAppearsInTheReportOrItsExports()
    {
        var report = AssessmentReport.Build(Workspace(aiVisible: false), forStudent: false);
        var md = report.ToMarkdown();
        var csv = report.ToCsv();
        Assert.That(report.Objectives.Single().AiExcluded, Is.False);
        Assert.That(report.Objectives.Single().AiIsMock, Is.False);
        Assert.That(report.Label, Is.Null);
        Assert.That(report.IsMock, Is.False);
        Assert.That(report.KnowledgeGaps, Is.Empty);
        foreach (var secret in Secrets)
        {
            Assert.That(md, Does.Not.Contain(secret).IgnoreCase, "markdown: " + secret);
            Assert.That(csv, Does.Not.Contain(secret).IgnoreCase, "csv: " + secret);
        }
        Assert.That(csv, Does.Contain(",no,no,"), "ai_excluded and ai_mock columns say no, not the AI's outcome");
    }

    [Test]
    public void WhileBlind_AnInsufficientEvidenceHumanGradeDoesNotRevealTheAisInconclusiveOutcomeInTheGaps()
    {
        var blind = AssessmentReport.Build(Workspace(aiVisible: false, MasteryLevel.InsufficientEvidence), forStudent: false);
        Assert.That(blind.KnowledgeGaps, Is.Empty);
        var open = AssessmentReport.Build(Workspace(aiVisible: true, MasteryLevel.InsufficientEvidence), forStudent: false);
        Assert.That(open.KnowledgeGaps, Has.Some.Contain("not enough evidence was gathered"));
    }

    [Test]
    public void OnceTheAiIsVisible_TheInsufficientEvidenceOutcomeIsReported()
    {
        var report = AssessmentReport.Build(Workspace(aiVisible: true), forStudent: false);
        Assert.That(report.Objectives.Single().AiExcluded, Is.True);
        Assert.That(report.ToMarkdown(), Does.Contain("insufficient evidence"));
        Assert.That(report.ToCsv(), Does.Contain(",yes,yes,"));
    }
}
