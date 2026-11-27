using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Tests.Fakes;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>The fixed fallback questions obey the elenchus rules (GAD-T814; ElenchusAtScale 2.2.2, 2.3.1, 2.3.2, 3.3, 3.4, M3, M5, M7).</summary>
[TestFixture]
public class FallbackQuestionTests
{
    private static readonly string[] Languages = ["en", ExamLanguage.Spanish, ExamLanguage.Korean];

    private static readonly string[] YesNoOpenings =
    [
        "could you", "can you", "do you", "does ", "is ", "are ", "would you", "will you", "have you", "did you",
        "¿podría", "¿puede", "¿se le ocurre", "¿hay ",
    ];

    private static QuestionRequest Request(MoveKind move, ClaimLedger? ledger = null, IReadOnlyList<string>? targets = null) => new()
    {
        Session = Session(),
        Decision = new PlanDecision { ConceptId = "photosynthesis", Move = move, TargetClaimIds = targets ?? [] },
        ConceptTitle = "Photosynthesis",
        Grounding = new GroundingContext("[1] Photosynthesis makes glucose.", []),
        Ledger = ledger ?? new ClaimLedger(),
        RubricForGuard = "- Light becomes chemical energy",
    };

    private static ScriptedPanel AlwaysLeaking() => new() { GuardVerdict = _ => "LEAKS", QuestionText = _ => "Light energy becomes chemical energy, right?" };

    [Test]
    public void EveryFallback_InEveryLanguage_IsAUsableOpenQuestion_OfAtMost40Words()
    {
        foreach (var language in Languages)
            foreach (var move in Enum.GetValues<MoveKind>())
            {
                var q = QuestionGenerator.Fallback(move, "Photosynthesis", "Respiration", language, ("plants eat soil", "mass comes from air"));
                var label = $"{language} {move}: {q}";
                Assert.That(QuestionGenerator.Usable(q), Is.EqualTo(q), label);
                Assert.That(q.Count(c => c is '?' or '？'), Is.EqualTo(1), $"one question per turn (GAD-T815): {label}");
                Assert.That(QuestionGenerator.IsIntrospective(q), Is.False, label);
                Assert.That(LanguageText.WordCount(q), Is.LessThanOrEqualTo(40), label);
                var lower = q.ToLowerInvariant();
                foreach (var sentence in lower.Split(['.', '?', '!'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()))
                    Assert.That(YesNoOpenings.Any(o => sentence.StartsWith(o, StringComparison.Ordinal)), Is.False, label);
                Assert.That(lower, Does.Not.Contain("walk me through").And.Not.Contain("cause").And.Not.Contain("주시겠어요")
                    .And.Not.Contain("razonamiento paso a paso").And.Not.Contain("causas"), label);
            }
    }

    [Test]
    public async Task ContradictionFallback_QuotesBothTargetClaims()
    {
        var ledger = new ClaimLedger();
        ledger.Add(0, "photosynthesis", "plants eat soil", "Plants get their mass from soil.");
        ledger.Add(1, "photosynthesis", "mass comes from air", "Plant mass comes from carbon dioxide.");
        var gen = new QuestionGenerator(AlwaysLeaking());
        var q = await gen.GenerateAsync(Request(MoveKind.Contradiction, ledger, ["c1", "c2"]));
        Assert.That(gen.LastOutcome, Does.StartWith("fallback: "));
        Assert.That(q, Does.Contain("\"plants eat soil\"").And.Contain("\"mass comes from air\""));
        Assert.That(gen.LastMove, Is.EqualTo(MoveKind.Contradiction));
    }

    [Test]
    public async Task ContradictionFallback_WithoutBothQuotes_IsNotRecordedAsAConfrontation()
    {
        var ledger = new ClaimLedger();
        ledger.Add(0, "photosynthesis", "plants eat soil", "Plants get their mass from soil.");
        var gen = new QuestionGenerator(AlwaysLeaking());
        var q = await gen.GenerateAsync(Request(MoveKind.Contradiction, ledger, ["c1", "c99"]));
        Assert.That(gen.LastMove, Is.EqualTo(MoveKind.AssumptionTesting));
        Assert.That(q, Is.EqualTo(QuestionGenerator.Fallback(MoveKind.AssumptionTesting, "Photosynthesis", null)));
    }

    [TestCase(MoveKind.Counterexample, MoveKind.AssumptionTesting)]
    [TestCase(MoveKind.Apply, MoveKind.IndependentExplanation)]
    public async Task CounterexampleAndApplyFallbacks_AreRecordedAsTheMoveAsked_AndNeverCreditTransfer(MoveKind planned, MoveKind asked)
    {
        var gen = new QuestionGenerator(AlwaysLeaking());
        var q = await gen.GenerateAsync(Request(planned));
        Assert.That(gen.LastMove, Is.EqualTo(asked));
        Assert.That(q, Is.EqualTo(QuestionGenerator.Fallback(asked, "Photosynthesis", null)));
        Assert.That(MoveProfiles.CreditsTransfer(gen.LastMove), Is.False);
        foreach (var language in Languages)
            Assert.That(QuestionGenerator.Fallback(planned, "Photosynthesis", null, language),
                Is.EqualTo(QuestionGenerator.Fallback(asked, "Photosynthesis", null, language)), language);
    }

    [Test]
    public async Task ADraftedQuestion_KeepsThePlannedMove()
    {
        var gen = new QuestionGenerator(new ScriptedPanel { QuestionText = _ => "Suppose a plant were kept in a sealed jar; what would happen to its mass?" });
        await gen.GenerateAsync(Request(MoveKind.Apply));
        Assert.That(gen.LastOutcome, Is.EqualTo("drafted"));
        Assert.That(gen.LastMove, Is.EqualTo(MoveKind.Apply));
    }
}
