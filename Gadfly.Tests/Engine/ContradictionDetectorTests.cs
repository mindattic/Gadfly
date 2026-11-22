using Gadfly.Engine;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>
/// Contradiction detector honesty (GAD-T440): pair selection is wider but bounded, a split panel's finding is never
/// confronted, silence reads "no contradiction detected", and the audit turns human labels into false-flag and miss rates.
/// </summary>
[TestFixture]
public class ContradictionDetectorTests
{
    private static StudentClaim Claim(string id, int turn, string concept, string text) => new(id, turn, concept, text, text);

    private static ClaimAnalyzer Analyzer() => new(null!, ExamPrompts.Set);

    [Test]
    public void SelectPairs_TakesSameConceptAndCloseCrossConceptPairs()
    {
        var earlier = new[]
        {
            Claim("c1", 0, "a", "photosynthesis makes sugar from light energy"),
            Claim("c2", 1, "b", "photosynthesis makes sugar from light energy in leaves"),
            Claim("c3", 1, "b", "rivers flow downhill toward the sea"),
        };
        var added = new[] { Claim("c4", 2, "a", "photosynthesis does not make sugar from light") };
        var pairs = Analyzer().SelectPairs(earlier, added, "a");
        Assert.That(pairs.Select(p => p.Prior.Id), Is.EquivalentTo(new[] { "c1", "c2" }));
    }

    [Test]
    public void SelectPairs_IsCappedPerAnswer()
    {
        var earlier = Enumerable.Range(0, 20).Select(i => Claim($"e{i}", 0, "a", $"the cell wall is rigid number {i}")).ToList();
        var added = Enumerable.Range(0, 5).Select(i => Claim($"n{i}", 1, "a", $"the cell wall is not rigid case {i}")).ToList();
        var analyzer = Analyzer();
        Assert.That(analyzer.SelectPairs(earlier, added, "a"), Has.Count.EqualTo(analyzer.MaxChecksPerAnswer));
    }

    [Test]
    public void SplitPanelFinding_IsNotPutToTheStudent()
    {
        var ledger = new ClaimLedger();
        var a = ledger.Add(0, "a", "x is y", "x is y");
        var b = ledger.Add(1, "a", "x is not y", "x is not y");
        ledger.AddContradiction(new ClaimContradiction(a.Id, b.Id, "a", 0.5));
        Assert.That(ledger.ForPlanner(), Is.Empty);
        var c = ledger.Add(2, "a", "x was never y", "x was never y");
        ledger.AddContradiction(new ClaimContradiction(a.Id, c.Id, "a", 0.67));
        Assert.That(ledger.ForPlanner(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Silence_IsNoContradictionDetected()
    {
        Assert.That(ObjectiveAssessor.ConsistencyOf(0, 0, 0, 0, 0), Is.EqualTo(ObjectiveAssessor.NoContradictionDetected));
        Assert.That(ObjectiveAssessor.ConsistencyOf(1, 1, 0, 0, 0), Is.EqualTo("contradiction pending"));
        Assert.That(ObjectiveAssessor.ConsistencyOf(1, 0, 0, 1, 0), Is.EqualTo("contradiction not resolved"));
    }

    [Test]
    public void Checks_SurviveTheJsonRoundTrip()
    {
        var ledger = new ClaimLedger();
        ledger.AddCheck(new ContradictionCheck(1, "c1", "c2", 0.3, false, "consistent", 1));
        Assert.That(ClaimLedger.FromJson(ledger.ToJson()).Checks, Has.Count.EqualTo(1));
    }

    [Test]
    public void Rates_WeightEachStratumBySize()
    {
        ClaimPairRow Row(ContradictionStratum s, bool? human) =>
            new() { Session = "s", FirstClaimId = "a", SecondClaimId = "b", Stratum = s, HumanSaysContradiction = human };
        var all = new List<ClaimPairRow>();
        all.AddRange(Enumerable.Repeat(Row(ContradictionStratum.Flagged, null), 4));
        all.AddRange(Enumerable.Repeat(Row(ContradictionStratum.NotChecked, null), 100));
        var labelled = new List<ClaimPairRow>
        {
            Row(ContradictionStratum.Flagged, true), Row(ContradictionStratum.Flagged, true),
            Row(ContradictionStratum.Flagged, true), Row(ContradictionStratum.Flagged, false),
        };
        labelled.AddRange(Enumerable.Repeat(Row(ContradictionStratum.NotChecked, false), 9));
        labelled.Add(Row(ContradictionStratum.NotChecked, true));

        var rates = ContradictionAudit.Rates(all, labelled);
        Assert.That(rates.FalseFlagRate, Is.EqualTo(0.25).Within(1e-9));
        // Caught 3 of 4 flagged; about 10 of 100 never checked: 10 / 13 missed.
        Assert.That(rates.MissRate, Is.EqualTo(10.0 / 13).Within(1e-9));
    }

    [Test]
    public void Csv_RoundTripsLabels()
    {
        var row = new ClaimPairRow
        {
            Session = "s1", FirstClaimId = "c1", SecondClaimId = "c2", FirstText = "a, \"quoted\"", SecondText = "b",
            Stratum = ContradictionStratum.CheckedClear, HumanSaysContradiction = true,
        };
        var back = ContradictionAudit.FromCsv(ContradictionAudit.ToCsv([row]), out var problems);
        Assert.That(problems, Is.Empty);
        Assert.That(back.Single().FirstText, Is.EqualTo(row.FirstText));
        Assert.That(back.Single().HumanSaysContradiction, Is.True);
    }
}
