using Gadfly.Engine;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>
/// Fixed opening questions (GAD-T436): the instructor-approved opening is asked word for word, without a panel call, as the
/// first question on its objective; replacements, later questions and other objectives are worded as before.
/// </summary>
[TestFixture]
public class FixedOpeningTests
{
    private const string Opening = "In your own words, what does a cell membrane do, and why does a cell need one?";

    private static SessionPolicy Policy() => new()
    {
        Objectives = [new ExamObjective { Key = "obj-1", Title = "Membranes", ConceptKeys = ["membrane"] }],
        OpeningQuestions = new Dictionary<string, string> { ["obj-1"] = Opening },
    };

    private static QuestionRequest Request(SocraticSession s, MoveKind move = MoveKind.Elicit, string key = "obj-1", bool replacement = false) => new()
    {
        Session = s,
        Decision = new PlanDecision { ConceptId = "membrane", ObjectiveKey = key, Move = move, IsReplacement = replacement },
        ConceptTitle = "Cell membrane",
        Grounding = new GroundingContext("", []),
        Ledger = new ClaimLedger(),
    };

    [Test]
    public void FirstElicit_IsTheFixedQuestion()
    {
        Assert.That(QuestionGenerator.FixedOpening(Request(Session(Policy()))), Is.EqualTo(Opening));
    }

    [Test]
    public async Task Generator_AsksItWordForWord_WithoutThePanel()
    {
        var generator = new QuestionGenerator(null!);   // a panel call would throw
        var question = await generator.GenerateAsync(Request(Session(Policy())));
        Assert.That(question, Is.EqualTo(Opening));
        Assert.That(generator.LastOutcome, Is.EqualTo(QuestionGenerator.FixedOpeningOutcome));
    }

    [Test]
    public void OtherMovesReplacementsAndLaterQuestions_AreNotFixed()
    {
        var s = Session(Policy());
        Assert.That(QuestionGenerator.FixedOpening(Request(s, MoveKind.Probe)), Is.Null);
        Assert.That(QuestionGenerator.FixedOpening(Request(s, replacement: true)), Is.Null);
        Assert.That(QuestionGenerator.FixedOpening(Request(s, key: "obj-2")), Is.Null);
        s.Ask("membrane", MoveKind.Elicit, Opening, T0, objectiveKey: "obj-1");
        Assert.That(QuestionGenerator.FixedOpening(Request(s)), Is.Null, "only the first question on the objective is fixed");
    }

    [Test]
    public void Policy_RejectsAnOverlongOpening()
    {
        var p = Policy() with { OpeningQuestions = new Dictionary<string, string> { ["obj-1"] = new string('a', SessionPolicy.MaxOpeningQuestionLength + 1) } };
        Assert.Throws<ArgumentException>(p.Validate);
    }
}
