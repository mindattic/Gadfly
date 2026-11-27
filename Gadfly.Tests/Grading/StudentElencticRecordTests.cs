using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Sessions;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>
/// The student's published result shows the contradictions put to them, in their own words with their answer and no verdict,
/// and their closing reflection; staff reports show neither (GAD-T821, ElenchusAtScale 3.4 Prop. 3.1, M7, 2.2.3, 2.6.1).
/// </summary>
[TestFixture]
public class StudentElencticRecordTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private static void Turn(SocraticSession s, MoveKind move, string question, string answer, IReadOnlyList<string>? targets = null)
    {
        s.Ask("c1", move, question, T0, targetClaimIds: targets, objectiveKey: "obj");
        s.RecordAnswer(new Answer { Text = answer, CompletedAt = T0 });
        s.RecordJudgment(new AnswerJudgment { Criteria = [new CriterionScore { Criterion = "accuracy", Score = 0.7 }], OverallScore = 0.7 }, 0.5);
    }

    /// <summary>A session with one contradiction put to the student and one the detector found but nobody asked about.</summary>
    private static ExamSession Session(bool confront = true, string? reflectionState = ClosingReflection.Answered)
    {
        var s = new SocraticSession("x", new SessionPolicy(), ExamPrompts.Set, T0);
        s.StartClock(T0);
        var ledger = new ClaimLedger();
        Turn(s, MoveKind.Elicit, "What drives the plant's growth?", "Plants get their mass mostly from the soil.");
        var a = ledger.Add(0, "c1", "Plants get their mass mostly from the soil", "Plant mass comes from soil.");
        Turn(s, MoveKind.Probe, "Where does the carbon come from?", "The carbon in a plant comes out of the air.");
        var b = ledger.Add(1, "c1", "The carbon in a plant comes out of the air", "Plant carbon comes from the air.");
        var c = ledger.Add(1, "c1", "UNASKEDCLAIM roots hold the water", "Roots hold water.");
        ledger.AddContradiction(new ClaimContradiction(a.Id, b.Id, "c1", 0.9));
        ledger.AddContradiction(new ClaimContradiction(a.Id, c.Id, "c1", 0.9));
        if (confront)
        {
            Turn(s, MoveKind.Contradiction, "You said the soil and later the air. Which do you hold now, and why?",
                "I take back the soil one: most of the mass is carbon from the air.", [a.Id, b.Id]);
            ledger.Retract(2, "I take back the soil one", "Plant mass comes from soil.");
        }
        // A one-sided Contradiction turn is no confrontation and is never listed.
        Turn(s, MoveKind.Contradiction, "ONESIDED question?", "ONESIDED answer.", [c.Id]);
        return new ExamSession
        {
            TranscriptJson = SessionTranscriptSerializer.Serialize(s), LedgerJson = ledger.ToJson(), Language = ExamLanguage.English,
            ClosingReflectionState = reflectionState,
            ClosingReflectionText = reflectionState == ClosingReflection.Answered ? "The soil claim, because I only half remembered it." : null,
        };
    }

    private static GradingWorkspace Workspace(ExamSession session)
    {
        var objective = new LearningObjective { Key = "obj", Title = "Photosynthesis", MaxScore = 100, Weight = 1 };
        var human = new HumanObjectiveGrade { ObjectiveId = objective.Id, Level = MasteryLevel.Application, Score = 70, MaxScore = 100 };
        return new GradingWorkspace
        {
            Session = session, ExamTitle = "Exam", StudentName = "Student",
            Grade = new FinalGrade { Status = GradeStatus.Published, FinalPercent = 70 },
            Policy = new PolicySnapshot(), AiVisible = true, Responses = [],
            Objectives = [new ObjectiveView(objective, null, human, [human], [], null, AiExcluded: false, null, [])], Combined = null,
            Approvals = [], Appeals = [], Audit = [], Names = new Dictionary<Guid, string>(), SecondReviewers = [],
            RequestedSecondReviews = new Dictionary<Guid, Guid>(), Viewer = GradingActor.System,
        };
    }

    [Test]
    public void TheStudentSeesEachConfrontedContradictionInTheirOwnWordsWithTheirAnswer()
    {
        var report = AssessmentReport.Build(Workspace(Session()), forStudent: true);

        var c = report.Contradictions.Single();
        Assert.That(c.FirstQuote, Is.EqualTo("Plants get their mass mostly from the soil"));
        Assert.That(c.FirstWithdrawn, Is.True, "the claim the student took back is marked as withdrawn by them");
        Assert.That(c.SecondQuote, Is.EqualTo("The carbon in a plant comes out of the air"));
        Assert.That(c.SecondWithdrawn, Is.False);
        Assert.That(c.Question, Does.StartWith("You said the soil"));
        Assert.That(c.Answer, Is.EqualTo("I take back the soil one: most of the mass is carbon from the air."));

        var md = report.ToMarkdown();
        Assert.That(md, Does.Contain(AssessmentReport.ContradictionsHeading));
        Assert.That(md, Does.Contain("Plants get their mass mostly from the soil\" (you withdrew this)"));
        Assert.That(md, Does.Not.Contain("UNASKEDCLAIM"), "an unconfronted detection is unconfirmed and never shown");
        Assert.That(md, Does.Not.Contain("ONESIDED"), "a Contradiction turn without both claims is not a confrontation");
        var section = md[md.IndexOf(AssessmentReport.ContradictionsHeading, StringComparison.Ordinal)..md.IndexOf("## Knowledge gaps", StringComparison.Ordinal)];
        Assert.That(section, Does.Not.Contain("correct").IgnoreCase.And.Not.Contain("wrong").IgnoreCase.And.Not.Contain("score:").IgnoreCase,
            "no verdict on which claim was right");
    }

    [Test]
    public void TheStudentSeesTheirClosingReflectionWhenTheyGaveOne()
    {
        var report = AssessmentReport.Build(Workspace(Session()), forStudent: true);
        Assert.That(report.Reflection, Is.EqualTo("The soil claim, because I only half remembered it."));
        Assert.That(report.ReflectionQuestion, Is.EqualTo(ClosingReflection.English));
        Assert.That(report.ToMarkdown(), Does.Contain("## Your closing reflection").And.Contain("half remembered"));

        var skipped = AssessmentReport.Build(Workspace(Session(reflectionState: ClosingReflection.Skipped)), forStudent: true);
        Assert.That(skipped.Reflection, Is.Null);
        Assert.That(skipped.ToMarkdown(), Does.Not.Contain("closing reflection"));
    }

    [Test]
    public void WithoutAConfrontedContradictionThereIsNoSection()
    {
        var report = AssessmentReport.Build(Workspace(Session(confront: false, reflectionState: null)), forStudent: true);
        Assert.That(report.Contradictions, Is.Empty);
        Assert.That(report.Reflection, Is.Null);
        Assert.That(report.ToMarkdown(), Does.Not.Contain(AssessmentReport.ContradictionsHeading));
    }

    [Test]
    public void StaffReportsCarryNeitherTheContradictionsNorTheReflection()
    {
        var report = AssessmentReport.Build(Workspace(Session()), forStudent: false);
        Assert.That(report.Contradictions, Is.Empty);
        Assert.That(report.Reflection, Is.Null);
        var md = report.ToMarkdown();
        Assert.That(md, Does.Not.Contain(AssessmentReport.ContradictionsHeading));
        Assert.That(md, Does.Not.Contain("half remembered"));
    }

    [Test]
    public void ARedactedSessionShowsNoReflection()
    {
        var session = Session();
        session.RedactedAt = T0;
        Assert.That(AssessmentReport.Build(Workspace(session), forStudent: true).Reflection, Is.Null);
    }
}
