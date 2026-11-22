using Gadfly.Engine;
using Gadfly.Tests.Fakes;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>GAD-T445: the examiner never asks about the student's own mental process; it asks for reasons.</summary>
[TestFixture]
public class IntrospectionTests
{
    [TestCase("How did you arrive at that answer?")]
    [TestCase("How did you figure that out?")]
    [TestCase("What made you think that the leaf needs light?")]
    [TestCase("What were you thinking when you said the plant eats soil?")]
    [TestCase("What went through your mind just now?")]
    [TestCase("Can you describe your thought process for that step?")]
    [TestCase("How did you come up with that example?")]
    public void IntrospectiveQuestions_AreRecognized(string question) =>
        Assert.That(QuestionGenerator.IsIntrospective(question), Is.True);

    [TestCase("Why does the plant need light to make glucose?")]
    [TestCase("How do you know that the mass comes from the air?")]
    [TestCase("What shows that your explanation holds when the light is dim?")]
    [TestCase("What must be true for your explanation to hold?")]
    public void ReasonQuestions_AreAllowed(string question) =>
        Assert.That(QuestionGenerator.IsIntrospective(question), Is.False);

    [Test]
    public async Task IntrospectiveDraft_IsRedrafted_NeverAsked()
    {
        var drafts = 0;
        var panel = new ScriptedPanel
        {
            QuestionText = _ => ++drafts == 1 ? "How did you arrive at that idea about light?" : "Why does the plant need light to make glucose?",
        };
        var gen = new QuestionGenerator(panel);
        var q = await gen.GenerateAsync(new QuestionRequest
        {
            Session = Session(),
            Decision = new PlanDecision { ConceptId = "photosynthesis", Move = MoveKind.Probe },
            ConceptTitle = "Photosynthesis",
            Grounding = new GroundingContext("[1] Photosynthesis makes glucose.", []),
            Ledger = new ClaimLedger(),
            RubricForGuard = "- Light becomes chemical energy",
        });
        Assert.That(q, Is.EqualTo("Why does the plant need light to make glucose?"));
        Assert.That(gen.LastOutcome, Is.EqualTo("redrafted"));
        Assert.That(panel.Calls.Count(c => c.Kind == ScriptedPanel.Kind.Question), Is.EqualTo(2));
        Assert.That(panel.Calls[1].Prompt, Does.Contain("asked about the student's own thought process"));
    }

    [Test]
    public void PromptSet_ForbidsIntrospection_InQuestionGuardAndJudge()
    {
        Assert.That(ExamPrompts.Version, Is.GreaterThanOrEqualTo(9));
        Assert.That(ExamPrompts.Question, Does.Contain("never about the student's own mental process"));
        Assert.That(ExamPrompts.QuestionGuard, Does.Contain("own mental process"));
        Assert.That(ExamPrompts.Judge, Does.Contain("not a justification"));
    }
}
