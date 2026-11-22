using Gadfly.Assessment;
using NUnit.Framework;

namespace Gadfly.Tests.Assessment;

/// <summary>
/// The single-answer reconciliation triggers (GAD-T270): a split panel or a score on a pass/fail boundary sends one
/// objective to reconciliation even when the AI-human difference alone would not.
/// </summary>
[TestFixture]
public class ReconciliationTriggersTests
{
    private static readonly GradingPolicyRules Policy = new();   // spread 1 level, boundary 50%, margin 2.5 points

    private static ObjectiveResult Result(decimal? ai, decimal? human, decimal max = 10m)
    {
        decimal? combined = ai is { } a && human is { } h ? CombinedScoring.Combine(a, h, Policy) : null;
        return new ObjectiveResult("o1", max, 1m, ai, human, combined, null, null);
    }

    [Test]
    public void PanelSpreadAboveTheLimit_Fires()
    {
        var fired = ReconciliationTriggers.Evaluate(Result(9m, 9m), [2, 4], Policy);
        Assert.That(fired, Does.Contain(ReconciliationTriggers.PanelSpread));
    }

    [Test]
    public void PanelSpreadWithinTheLimit_DoesNotFire()
    {
        Assert.That(ReconciliationTriggers.Evaluate(Result(9m, 9m), [3, 4, 4], Policy), Is.Empty);
    }

    [Test]
    public void OneOrNoPanelLevels_NeverCountAsSpread()
    {
        Assert.That(ReconciliationTriggers.Evaluate(Result(9m, 9m), [0], Policy), Is.Empty);
        Assert.That(ReconciliationTriggers.Evaluate(Result(9m, 9m), [], Policy), Is.Empty);
    }

    [Test]
    public void SpreadLimitComesFromThePolicy()
    {
        var lenient = new GradingPolicyRules { MaxPanelSpreadLevels = 3 };
        Assert.That(ReconciliationTriggers.Evaluate(Result(9m, 9m), [1, 4], lenient), Is.Empty);
        Assert.That(ReconciliationTriggers.Evaluate(Result(9m, 9m), [0, 4], lenient), Is.EqualTo(new[] { ReconciliationTriggers.PanelSpread }));
    }

    [Test]
    public void RatersOnOppositeSidesOfABoundary_Fires()
    {
        var fired = ReconciliationTriggers.Evaluate(Result(4m, 6m), [], Policy);   // 40% and 60% either side of 50%
        Assert.That(fired, Is.EqualTo(new[] { ReconciliationTriggers.Boundary }));
    }

    [Test]
    public void CombinedScoreWithinTheMarginOfABoundary_Fires()
    {
        // Both 51%: same side, but the combined 51% is within 2.5 points of 50%.
        Assert.That(ReconciliationTriggers.Evaluate(Result(5.1m, 5.1m), [], Policy), Does.Contain(ReconciliationTriggers.Boundary));
    }

    [Test]
    public void ScoresClearOfEveryBoundary_DoNotFire()
    {
        Assert.That(ReconciliationTriggers.Evaluate(Result(8m, 9m), [4, 5], Policy), Is.Empty);
    }

    [Test]
    public void BothTriggersCanFireTogether()
    {
        var fired = ReconciliationTriggers.Evaluate(Result(4m, 6m), [0, 5], Policy);
        Assert.That(fired, Is.EquivalentTo(new[] { ReconciliationTriggers.PanelSpread, ReconciliationTriggers.Boundary }));
    }

    [Test]
    public void MissingAScore_FiresNoBoundary()
    {
        Assert.That(ReconciliationTriggers.Evaluate(Result(4m, null), [], Policy), Is.Empty);
        Assert.That(ReconciliationTriggers.Evaluate(Result(null, 6m), [], Policy), Is.Empty);
    }

    [Test]
    public void ZeroMaximum_FiresNoBoundary()
    {
        var r = new ObjectiveResult("o1", 0m, 1m, 0m, 0m, 0m, null, null);
        Assert.That(ReconciliationTriggers.Evaluate(r, [], Policy), Is.Empty);
    }

    [Test]
    public void NoBoundaries_TurnsTheBoundaryTriggerOff()
    {
        var none = new GradingPolicyRules { ScoreBoundaries = [] };
        Assert.That(ReconciliationTriggers.Evaluate(Result(4m, 6m), [], none), Is.Empty);
    }

    [Test]
    public void SeveralBoundaries_AnyOneFires()
    {
        var grades = new GradingPolicyRules { ScoreBoundaries = [50m, 70m, 90m] };
        Assert.That(ReconciliationTriggers.OnBoundary(65m, 75m, 71m, grades), Is.True);   // either side of 70
        Assert.That(ReconciliationTriggers.OnBoundary(60m, 62m, 61m, grades), Is.False);
        Assert.That(ReconciliationTriggers.OnBoundary(88m, 88m, 88m, grades), Is.True);   // within 2.5 of 90
    }

    [Test]
    public void MarginIsStrict()
    {
        var p = new GradingPolicyRules { ScoreBoundaries = [50m], BoundaryMarginPoints = 2.5m };
        Assert.That(ReconciliationTriggers.OnBoundary(52.5m, 52.5m, 52.5m, p), Is.False);
        Assert.That(ReconciliationTriggers.OnBoundary(52.4m, 52.4m, 52.4m, p), Is.True);
    }

    [Test]
    public void Describe_ExplainsKnownCodesAndEchoesUnknownOnes()
    {
        Assert.That(ReconciliationTriggers.Describe(ReconciliationTriggers.PanelSpread), Does.Contain("panel members disagree"));
        Assert.That(ReconciliationTriggers.Describe(ReconciliationTriggers.Boundary), Does.Contain("boundary"));
        Assert.That(ReconciliationTriggers.Describe("something-else"), Is.EqualTo("something-else"));
    }
}
