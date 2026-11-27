using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Tests.Fakes;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>
/// One question per turn (GAD-T815; ElenchusAtScale 3.1, 3.2, 3.4, 4.3.4): a draft is cut after its first question, and a first
/// question well past the prompt's 40 words is redrafted like an empty draft. The streamed path is in <see cref="QuestionStreamingTests"/>.
/// </summary>
[TestFixture]
public class OneQuestionPerTurnTests
{
    private static QuestionRequest Request() => new()
    {
        Session = Session(),
        Decision = new PlanDecision { ConceptId = "photosynthesis", Move = MoveKind.Probe, TargetClaimIds = [] },
        ConceptTitle = "Photosynthesis",
        Grounding = new GroundingContext("[1] Photosynthesis makes glucose.", []),
        Ledger = new ClaimLedger(),
        RubricForGuard = "- Light becomes chemical energy",
    };

    [TestCase("You said plants store energy. Where does that energy come from first? And why does it matter?",
        "You said plants store energy. Where does that energy come from first?")]
    [TestCase("Usted dijo que las plantas almacenan energía. ¿De dónde viene esa energía primero? ¿Y por qué importa?",
        "Usted dijo que las plantas almacenan energía. ¿De dónde viene esa energía primero?")]
    [TestCase("식물이 에너지를 저장한다고 하셨어요. 그 에너지는 처음에 어디에서 오나요？ 그것이 왜 중요한가요?",
        "식물이 에너지를 저장한다고 하셨어요. 그 에너지는 처음에 어디에서 오나요？")]
    [TestCase("식물이 에너지를 저장한다고 하셨어요. 그 에너지는 처음에 어디에서 오나요? 그것이 왜 중요한가요?",
        "식물이 에너지를 저장한다고 하셨어요. 그 에너지는 처음에 어디에서 오나요?")]
    [TestCase("Why does a leaf need light?! What else does it need?", "Why does a leaf need light?!")]
    public void ADraft_IsCutAfterItsFirstQuestion(string draft, string asked)
    {
        Assert.That(QuestionGenerator.FirstQuestion(draft), Is.EqualTo(asked));
        Assert.That(QuestionGenerator.Usable(draft), Is.EqualTo(asked));
    }

    [TestCase("You said \"why would it not?\" a moment ago. What did you mean by that?")]
    [TestCase("You said “is it the soil?” and then “it is the air”. How do those two fit together, or which one would you change?")]
    [TestCase("Usted dijo «¿por qué no?». ¿Qué quiso decir con eso?")]
    [TestCase("Explain in your own words what a leaf does with light.")]
    public void AQuotedQuestionOrNoQuestion_LeavesTheDraftWhole(string draft) =>
        Assert.That(QuestionGenerator.Usable(draft), Is.EqualTo(draft));

    [Test]
    public void AFirstQuestionPastTheLimit_IsNotUsable()
    {
        var at = string.Join(" ", Enumerable.Repeat("leaf", QuestionGenerator.DefaultMaxQuestionWords - 1)) + " why?";
        var past = "Suppose " + at;
        Assert.That(QuestionGenerator.Usable(at), Is.EqualTo(at));
        Assert.That(QuestionGenerator.Usable(past), Is.Empty);
        Assert.That(QuestionGenerator.Usable(past, maxWords: 60), Is.EqualTo(past), "the limit is a setting");
        // Only the first question counts: a short question followed by a long second one is cut, not dropped.
        Assert.That(QuestionGenerator.Usable("Why does a leaf need light? " + past), Is.EqualTo("Why does a leaf need light?"));
    }

    [Test]
    public async Task ADraftWithTwoQuestions_IsAskedUpToTheEndOfTheFirst()
    {
        var gen = new QuestionGenerator(new ScriptedPanel { QuestionText = _ => "You said plants store energy. Where does it come from? Why?" });
        var q = await gen.GenerateAsync(Request());
        Assert.That(q, Is.EqualTo("You said plants store energy. Where does it come from?"));
        Assert.That(gen.LastOutcome, Is.EqualTo("drafted"));
    }

    [Test]
    public async Task AnOverlongDraft_IsRedrafted()
    {
        var drafts = 0;
        var overlong = "Suppose " + string.Join(" ", Enumerable.Repeat("leaf", 55)) + " why?";
        var gen = new QuestionGenerator(new ScriptedPanel { QuestionText = _ => ++drafts == 1 ? overlong : "What happens to the light a leaf absorbs?" });
        var q = await gen.GenerateAsync(Request());
        Assert.That(q, Is.EqualTo("What happens to the light a leaf absorbs?"));
        Assert.That(gen.LastOutcome, Is.EqualTo("redrafted"));
    }
}
