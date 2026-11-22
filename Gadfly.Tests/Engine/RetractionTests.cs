using Gadfly.Engine;
using Gadfly.Sources;
using Gadfly.Tests.Fakes;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>GAD-T441: a claim the student withdraws stays on record but is no longer a premise.</summary>
[TestFixture]
public class RetractionTests
{
    [Test]
    public void Retract_MarksTheBestMatchingEarlierClaim()
    {
        var ledger = new ClaimLedger();
        ledger.Add(0, "p", "plants eat soil", "Plants get their mass from the soil.");
        ledger.Add(1, "p", "light is food", "Light is the food of the plant.");
        var r = ledger.Retract(2, "I was wrong earlier", "Plants get their mass from soil.");
        Assert.That(r.ClaimId, Is.EqualTo("c1"));
        Assert.That(ledger.IsWithdrawn("c1"), Is.True);
        Assert.That(ledger.IsWithdrawn("c2"), Is.False);
        Assert.That(ledger.Describe(), Does.Contain("(withdrawn by the student at turn 3)"));
    }

    [Test]
    public void Retract_WithNoMatch_IsKeptUnmatched()
    {
        var ledger = new ClaimLedger();
        ledger.Add(0, "p", "plants eat soil", "Plants get their mass from the soil.");
        var r = ledger.Retract(1, "scratch that", "Volcanoes erupt every spring.");
        Assert.That(r.ClaimId, Is.Null);
        Assert.That(ledger.Retractions, Has.Count.EqualTo(1));
        Assert.That(ledger.IsWithdrawn("c1"), Is.False);
    }

    [Test]
    public void Ledger_RoundTrips_Retractions_AndReadsOldJson()
    {
        var ledger = new ClaimLedger();
        ledger.Add(0, "p", "plants eat soil", "Plants get their mass from the soil.");
        ledger.Retract(1, "I was wrong", "Plants get their mass from the soil.");
        var back = ClaimLedger.FromJson(ledger.ToJson());
        Assert.That(back.IsWithdrawn("c1"), Is.True);
        var old = ClaimLedger.FromJson("""{"claims":[{"id":"c1","turnIndex":0,"conceptId":"p","quote":"q","text":"t"}],"contradictions":[]}""");
        Assert.That(old.Retractions, Is.Empty);
        Assert.That(old.Claims, Has.Count.EqualTo(1));
    }

    [Test]
    public void WithdrawnContradiction_IsNotConfronted_AndNotPending()
    {
        var ledger = new ClaimLedger();
        ledger.Add(0, "a", "x", "X raises Y.");
        ledger.Add(1, "a", "not x", "X does not raise Y.");
        ledger.AddContradiction(new ClaimContradiction("c1", "c2", "a", 1));
        ledger.Retract(2, "I was wrong", "X raises Y.");
        var candidates = ledger.ForPlanner();
        Assert.That(candidates[0].WithdrawnAtTurn, Is.EqualTo(2));

        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.9, 0.6);
        var planner = new QuestionPlanner([new PlannerConcept("a")]);
        Assert.That(planner.Next(s, T0, candidates).Move, Is.Not.EqualTo(MoveKind.Contradiction));
        var a = planner.Assess(s, candidates).Single();
        Assert.That(a.UnconfrontedContradictions, Is.EqualTo(0));
        Assert.That(a.WithdrawnContradictions, Is.EqualTo(1));
    }

    [Test]
    public async Task WithdrawnClaim_IsNeverCheckedAgain()
    {
        var panel = new ScriptedPanel { ContradictionVerdict = _ => "CONTRADICT" };
        var ledger = new ClaimLedger();
        ledger.Add(0, "a", "x raises y", "X raises Y in every case.");
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.9, 0.6);
        s.Judged("a", MoveKind.Probe, 0.9, 0.6);
        var turn = s.Turns[1];
        var found = await new ClaimAnalyzer(panel, ExamPrompts.Set).AddClaimsAsync(ledger, turn,
            [("x lowers y", "X lowers Y in every case.")], retractions: [("I was wrong earlier", "X raises Y in every case.")]);
        Assert.That(found, Is.Empty);
        Assert.That(panel.Count(ScriptedPanel.Kind.Contradiction), Is.EqualTo(0));
    }

    [Test]
    public void ClaimsPrompt_Items_SeparateWithdrawals()
    {
        var items = ClaimAnalyzer.ParseItems("""[{"quote":"q1","claim":"A is B."},{"quote":"I was wrong","withdraws":"A is C."}]""")!;
        Assert.That(items.Count(i => i.Withdraws), Is.EqualTo(1));
        Assert.That(ClaimAnalyzer.ParseClaims("""[{"quote":"q1","claim":"A is B."},{"quote":"I was wrong","withdraws":"A is C."}]"""), Has.Count.EqualTo(1));
    }

    [Test]
    public void Analysis_AcceptsOptionalRetractions()
    {
        const string answer = "I was wrong earlier. Plants build mass from carbon dioxide.";
        var json = """
            {"claims": [{"quote": "Plants build mass from carbon dioxide", "claim": "Plant mass comes from carbon dioxide."}],
             "retractions": [{"quote": "I was wrong earlier", "claim": "Plants get their mass from the soil."}],
             "misconceptions": [], "uncertainties": [], "evidence": [], "questionRevealedAnswer": false}
            """;
        var parsed = ResponseAnalyzer.Parse(json, answer, new HashSet<string> { "o1" });
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Retractions, Has.Count.EqualTo(1));
        var without = ResponseAnalyzer.Parse("""{"claims": [], "misconceptions": [], "uncertainties": [], "evidence": [], "questionRevealedAnswer": false}""",
            answer, new HashSet<string> { "o1" });
        Assert.That(without, Is.Not.Null);
    }
}
