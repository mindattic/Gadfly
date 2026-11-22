using System.Runtime.CompilerServices;
using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Tests.Fakes;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>The live turn's one fast model words the question and guards it, off the panel vote (GAD-T494).</summary>
[TestFixture]
public class LiveTurnModelTests
{
    /// <summary>A scripted single model: the guard prompt gets <see cref="Guard"/>, anything else <see cref="Question"/>.</summary>
    private sealed class ScriptedLiveModel : ILiveTurnModel
    {
        public Func<string, string> Question { get; init; } = _ => "What does a plant do with the light it absorbs?";
        public Func<string, string> Guard { get; init; } = _ => "SAFE";
        public bool Fail { get; init; }
        public List<string> Prompts { get; } = [];
        public string ProviderId => "claude";
        public string Model => "claude-sonnet-5";

        public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            Prompts.Add(userMessage);
            if (Fail) throw new HttpRequestException("provider down");
            var reply = userMessage.Contains("LEAKS or SAFE", StringComparison.Ordinal) ? Guard(userMessage) : Question(userMessage);
            // Two deltas, as a streaming provider would send them.
            var half = reply.Length / 2;
            yield return reply[..half];
            yield return reply[half..];
        }
    }

    private static QuestionRequest Request(string rubric = "- Light becomes chemical energy") => new()
    {
        Session = Session(),
        Decision = new PlanDecision { ConceptId = "photosynthesis", Move = MoveKind.Probe, TargetClaimIds = [] },
        ConceptTitle = "Photosynthesis",
        Grounding = new GroundingContext("[1] Photosynthesis makes glucose.", []),
        Ledger = new ClaimLedger(),
        RubricForGuard = rubric,
    };

    [Test]
    public async Task TheLiveModelWordsAndGuardsTheQuestion_WithNoPanelCall()
    {
        var panel = new ScriptedPanel();
        var live = new ScriptedLiveModel();
        var gen = new QuestionGenerator(panel) { LiveModel = live };
        var q = await gen.GenerateAsync(Request());
        Assert.That(q, Is.EqualTo("What does a plant do with the light it absorbs?"));
        Assert.That(gen.LastGuard, Is.EqualTo(LeakGuardResult.Safe));
        Assert.That(gen.LastAuthor!.How, Is.EqualTo(QuestionAuthorship.LiveModelDraft));
        Assert.That(gen.LastAuthor.Providers, Is.EqualTo(new[] { "claude" }));
        Assert.That(gen.LastAuthor.Models, Is.EqualTo(new[] { "claude-sonnet-5" }));
        Assert.That(live.Prompts, Has.Count.EqualTo(2));
        Assert.That(panel.Calls, Is.Empty);
    }

    [Test]
    public async Task ALeakingQuestion_IsNeverReturned_ItIsRedraftedThenReplaced()
    {
        var live = new ScriptedLiveModel { Guard = _ => "LEAKS" };
        var gen = new QuestionGenerator(new ScriptedPanel()) { LiveModel = live };
        var q = await gen.GenerateAsync(Request());
        Assert.That(q, Is.EqualTo(QuestionGenerator.Fallback(MoveKind.Probe, "Photosynthesis", null)));
        Assert.That(gen.LastGuard, Is.EqualTo(LeakGuardResult.LeakedReplaced));
        Assert.That(live.Prompts.Count(p => p.Contains("Your previous draft revealed expected answer points")), Is.EqualTo(1));
    }

    [Test]
    public async Task AnUndecidedGuard_FailsClosed()
    {
        var gen = new QuestionGenerator(new ScriptedPanel()) { LiveModel = new ScriptedLiveModel { Guard = _ => "Hard to say." } };
        var q = await gen.GenerateAsync(Request());
        Assert.That(q, Is.EqualTo(QuestionGenerator.Fallback(MoveKind.Probe, "Photosynthesis", null)));
        Assert.That(gen.LastGuard, Is.EqualTo(LeakGuardResult.UndecidedReplaced));
    }

    [Test]
    public async Task AFailingLiveModel_FallsBackToThePanel()
    {
        var panel = new ScriptedPanel { QuestionText = _ => "Why do leaves need light at all?" };
        var gen = new QuestionGenerator(panel) { LiveModel = new ScriptedLiveModel { Fail = true } };
        var q = await gen.GenerateAsync(Request());
        Assert.That(q, Is.EqualTo("Why do leaves need light at all?"));
        Assert.That(panel.Count(ScriptedPanel.Kind.Question), Is.EqualTo(1));
        Assert.That(panel.Count(ScriptedPanel.Kind.Guard), Is.EqualTo(1));
    }

    [TestCase("SAFE", false)]
    [TestCase(" leaks.", true)]
    [TestCase("\"SAFE\"", false)]
    [TestCase("**LEAKS**", true)]
    [TestCase("It is safe", null)]
    [TestCase("", null)]
    public void TheGuardReplyIsOneWord(string reply, bool? expected) =>
        Assert.That(QuestionGenerator.ParseGuard(reply), Is.EqualTo(expected));

    [Test]
    public void TheLiveModelIsSonnetUnlessConfigured_AndNeverOpus()
    {
        Assert.That(LiveTurnSettings.Provider(null), Is.EqualTo("claude"));
        Assert.That(LiveTurnSettings.Provider("evil"), Is.EqualTo("claude"));
        Assert.That(LiveTurnSettings.Provider("OpenAI"), Is.EqualTo("openai"));
        Assert.That(LiveTurnSettings.Model("claude", null), Is.EqualTo("claude-sonnet-5"));
        Assert.That(LiveTurnSettings.Model("claude", "claude-opus-5"), Is.EqualTo("claude-sonnet-5"));
        Assert.That(LiveTurnSettings.Model("claude", "claude-haiku-5"), Is.EqualTo("claude-haiku-5"));
        Assert.That(LiveTurnSettings.Model("openai", null), Is.EqualTo(""));
    }

    [Test]
    public async Task TheMockModel_AnswersTheGuardSafe_AndWordsATemplateQuestion()
    {
        var mock = new MockLiveTurnModel();
        Assert.That(await mock.CompleteAsync("", "Is this LEAKS or SAFE?"), Is.EqualTo("SAFE"));
        var gen = new QuestionGenerator(new ScriptedPanel()) { LiveModel = mock };
        var q = await gen.GenerateAsync(Request());
        Assert.That(QuestionGenerator.Usable(q), Is.Not.Empty);
        Assert.That(gen.LastGuard, Is.EqualTo(LeakGuardResult.Safe));
    }
}
