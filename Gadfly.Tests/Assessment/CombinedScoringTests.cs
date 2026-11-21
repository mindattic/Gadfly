using Gadfly.Assessment;
using NUnit.Framework;

namespace Gadfly.Tests.Assessment;

/// <summary>Part 9 weighted scoring, Part 9.3 policy validation and Part 10 disagreement bands, as exact decimals.</summary>
[TestFixture]
public class GradingPolicyRulesTests
{
    [Test]
    public void DefaultPolicy_IsTheMasterPlanExample()
    {
        var p = new GradingPolicyRules();
        Assert.Multiple(() =>
        {
            Assert.That(p.AiWeight, Is.EqualTo(0.4m));
            Assert.That(p.HumanWeight, Is.EqualTo(0.6m));
            Assert.That(p.AdditionalReviewAt, Is.EqualTo(10m));
            Assert.That(p.MandatoryReconciliationAt, Is.EqualTo(20m));
            Assert.That(p.BlindHumanGrading, Is.True);
        });
        Assert.DoesNotThrow(p.Validate);
    }

    [TestCase("0.2", "0.8")]
    [TestCase("0.3", "0.7")]
    [TestCase("0.4", "0.6")]
    [TestCase("0.5", "0.5")]
    [TestCase("0", "1")]
    public void Part93Weightings_AreValid(string ai, string human) =>
        Assert.DoesNotThrow(new GradingPolicyRules { AiWeight = decimal.Parse(ai), HumanWeight = decimal.Parse(human) }.Validate);

    [TestCase("0.5", "0.6", "sum to 1")]
    [TestCase("0.3", "0.6", "sum to 1")]
    [TestCase("0.33", "0.66", "sum to 1")]
    [TestCase("1", "0", "human weight cannot be 0")]
    [TestCase("-0.1", "1.1", "between 0 and 1")]
    [TestCase("1.2", "-0.2", "between 0 and 1")]
    [TestCase("0", "0", "sum to 1")]
    public void InvalidWeights_AreRefused(string ai, string human, string why)
    {
        var p = new GradingPolicyRules { AiWeight = decimal.Parse(ai), HumanWeight = decimal.Parse(human) };
        var ex = Assert.Throws<ArgumentException>(p.Validate);
        Assert.That(ex!.Message, Does.Contain(why).IgnoreCase);
    }

    [TestCase("-1", "20")]
    [TestCase("10", "0")]
    [TestCase("10", "-5")]
    public void NegativeOrZeroThresholds_AreRefused(string review, string reconcile)
    {
        var p = new GradingPolicyRules { AdditionalReviewAt = decimal.Parse(review), MandatoryReconciliationAt = decimal.Parse(reconcile) };
        var ex = Assert.Throws<ArgumentException>(p.Validate);
        Assert.That(ex!.Message, Does.Contain("positive"));
    }

    [Test]
    public void InvertedThresholds_AreRefused()
    {
        var p = new GradingPolicyRules { AdditionalReviewAt = 25, MandatoryReconciliationAt = 20 };
        var ex = Assert.Throws<ArgumentException>(p.Validate);
        Assert.That(ex!.Message, Does.Contain("must not exceed"));
    }

    [Test]
    public void EqualThresholds_AreAllowed() =>
        Assert.DoesNotThrow(new GradingPolicyRules { AdditionalReviewAt = 15, MandatoryReconciliationAt = 15 }.Validate);

    [Test]
    public void ZeroAdditionalReviewThreshold_IsAllowed() =>
        Assert.DoesNotThrow(new GradingPolicyRules { AdditionalReviewAt = 0, MandatoryReconciliationAt = 20 }.Validate);
}

[TestFixture]
public class CombinedScoringTests
{
    private static readonly GradingPolicyRules Default = new();

    private static ObjectiveScores O(string key, decimal? ai, decimal? human, decimal max = 100, decimal weight = 1) =>
        new(key, max, weight, ai, human);

    [Test]
    public void Part92_WorkedExample_85And90_Is88()
    {
        Assert.That(CombinedScoring.Combine(85, 90, Default), Is.EqualTo(88m));
        var r = CombinedScoring.Calculate([O("overall", 85, 90)], Default);
        Assert.That(r.FinalPercent, Is.EqualTo(88.00m));
        Assert.That(r.Objectives[0].Combined, Is.EqualTo(88m));
        Assert.That(r.Objectives[0].DifferencePoints, Is.EqualTo(5m));
        Assert.That(r.Objectives[0].Band, Is.EqualTo(DisagreementBand.Standard));
        Assert.That(r.RequiresReconciliation, Is.False);
        Assert.That(r.HasAdditionalReviewFlags, Is.False);
        Assert.That(r.MissingScores, Is.Empty);
    }

    [TestCase("Conceptual Understanding", 80, 90, 86)]
    [TestCase("Logical Reasoning", 75, 85, 81)]
    [TestCase("Application", 90, 80, 84)]
    [TestCase("Knowledge Transfer", 70, 80, 76)]
    public void Part94_Table_PerObjective(string objective, int ai, int human, int combined)
    {
        var r = CombinedScoring.Calculate([O(objective, ai, human)], Default);
        Assert.That(r.Objectives[0].Combined, Is.EqualTo((decimal)combined));
        Assert.That(r.Objectives[0].ObjectiveKey, Is.EqualTo(objective));
    }

    [Test]
    public void Part94_Table_Aggregated_WithEqualObjectiveWeights()
    {
        var r = CombinedScoring.Calculate(
        [
            O("Conceptual Understanding", 80, 90), O("Logical Reasoning", 75, 85),
            O("Application", 90, 80), O("Knowledge Transfer", 70, 80),
        ], Default);
        Assert.That(r.Objectives.Select(o => o.Combined), Is.EqualTo(new decimal?[] { 86, 81, 84, 76 }));
        Assert.That(r.FinalPercent, Is.EqualTo(81.75m));
        // Each pair differs by exactly 10 points: the additional-review boundary is inclusive.
        Assert.That(r.Objectives.All(o => o.DifferencePoints == 10m && o.Band == DisagreementBand.AdditionalReview), Is.True);
        Assert.That(r.HasAdditionalReviewFlags, Is.True);
        Assert.That(r.RequiresReconciliation, Is.False);
    }

    [TestCase("0.2", "0.8", "89")]
    [TestCase("0.3", "0.7", "88.5")]
    [TestCase("0.4", "0.6", "88")]
    [TestCase("0.5", "0.5", "87.5")]
    public void Part93_Weightings_Combine85And90(string ai, string human, string expected)
    {
        var p = new GradingPolicyRules { AiWeight = decimal.Parse(ai), HumanWeight = decimal.Parse(human) };
        Assert.That(CombinedScoring.Combine(85, 90, p), Is.EqualTo(decimal.Parse(expected)));
    }

    [Test]
    public void ScenarioG_95And60_RequiresReconciliation()
    {
        var r = CombinedScoring.Calculate([O("overall", 95, 60)], Default);
        Assert.That(r.Objectives[0].DifferencePoints, Is.EqualTo(35m));
        Assert.That(r.Objectives[0].Band, Is.EqualTo(DisagreementBand.MandatoryReconciliation));
        Assert.That(r.RequiresReconciliation, Is.True);
        Assert.That(r.Objectives[0].Combined, Is.EqualTo(74m));
    }

    [Test]
    public void Part101_90And65_IsFlagged()
    {
        var r = CombinedScoring.Calculate([O("x", 90, 65)], Default);
        Assert.That(r.Objectives[0].DifferencePoints, Is.EqualTo(25m));
        Assert.That(r.RequiresReconciliation, Is.True);
    }

    [TestCase("0", DisagreementBand.Standard)]
    [TestCase("9.9999", DisagreementBand.Standard)]
    [TestCase("10", DisagreementBand.AdditionalReview)]
    [TestCase("10.0001", DisagreementBand.AdditionalReview)]
    [TestCase("19.9999", DisagreementBand.AdditionalReview)]
    [TestCase("20", DisagreementBand.MandatoryReconciliation)]
    [TestCase("100", DisagreementBand.MandatoryReconciliation)]
    public void Band_Boundaries(string diff, DisagreementBand band) =>
        Assert.That(CombinedScoring.Band(decimal.Parse(diff), Default), Is.EqualTo(band));

    [Test]
    public void Band_UsesTheInstitutionsThresholds()
    {
        var p = new GradingPolicyRules { AdditionalReviewAt = 5, MandatoryReconciliationAt = 15 };
        Assert.That(CombinedScoring.Band(4.99m, p), Is.EqualTo(DisagreementBand.Standard));
        Assert.That(CombinedScoring.Band(5m, p), Is.EqualTo(DisagreementBand.AdditionalReview));
        Assert.That(CombinedScoring.Band(15m, p), Is.EqualTo(DisagreementBand.MandatoryReconciliation));
    }

    [Test]
    public void ExactBoundariesThroughCalculate_10And20Points()
    {
        var r = CombinedScoring.Calculate([O("a", 60, 70), O("b", 50, 70), O("c", 50, 59)], Default);
        Assert.That(r.Objectives.Select(o => o.Band), Is.EqualTo(new DisagreementBand?[]
        {
            DisagreementBand.AdditionalReview, DisagreementBand.MandatoryReconciliation, DisagreementBand.Standard,
        }));
        Assert.That(r.RequiresReconciliation, Is.True);
        Assert.That(r.HasAdditionalReviewFlags, Is.True);
    }

    [Test]
    public void DifferentMaxScores_CompareInPercentagePoints()
    {
        // 8 vs 9 out of 10 is a 10-point gap; 40 vs 30 out of 50 is 20 points.
        var r = CombinedScoring.Calculate([O("ten", 8, 9, max: 10), O("fifty", 40, 30, max: 50)], Default);
        var ten = r.Objectives[0];
        var fifty = r.Objectives[1];
        Assert.That(ten.Combined, Is.EqualTo(8.6m));
        Assert.That(ten.DifferencePoints, Is.EqualTo(10m));
        Assert.That(ten.Band, Is.EqualTo(DisagreementBand.AdditionalReview));
        Assert.That(fifty.Combined, Is.EqualTo(34m));
        Assert.That(fifty.DifferencePoints, Is.EqualTo(20m));
        Assert.That(fifty.Band, Is.EqualTo(DisagreementBand.MandatoryReconciliation));
        // 86% and 68%, equally weighted.
        Assert.That(r.FinalPercent, Is.EqualTo(77m));
    }

    [Test]
    public void ObjectiveWeights_WeightTheFinalPercent()
    {
        var r = CombinedScoring.Calculate([O("heavy", 90, 90, weight: 3), O("light", 60, 60, weight: 1)], Default);
        Assert.That(r.FinalPercent, Is.EqualTo(82.5m));
    }

    [Test]
    public void ZeroWeightObjective_IsCombinedButDoesNotCount()
    {
        var r = CombinedScoring.Calculate([O("counted", 80, 80), O("ignored", 0, 0, weight: 0)], Default);
        Assert.That(r.FinalPercent, Is.EqualTo(80m));
        Assert.That(r.Objectives[1].Combined, Is.EqualTo(0m));
    }

    [Test]
    public void AllZeroWeights_AreRefused() =>
        Assert.Throws<ArgumentException>(() => CombinedScoring.Calculate([O("a", 50, 50, weight: 0), O("b", 50, 50, weight: 0)], Default));

    [Test]
    public void MissingHumanScore_LeavesNoFinal_AndNamesTheObjective()
    {
        var r = CombinedScoring.Calculate([O("done", 80, 90), O("pending", 70, null)], Default);
        Assert.That(r.FinalPercent, Is.Null);
        Assert.That(r.MissingScores, Is.EqualTo(new[] { "pending" }));
        var pending = r.Objectives[1];
        Assert.That(pending.Combined, Is.Null);
        Assert.That(pending.DifferencePoints, Is.Null);
        Assert.That(pending.Band, Is.Null);
        Assert.That(pending.AiScore, Is.EqualTo(70m));
        Assert.That(r.Objectives[0].Combined, Is.EqualTo(86m));
    }

    [Test]
    public void MissingAiScore_AlsoBlocksTheFinal()
    {
        var r = CombinedScoring.Calculate([O("a", null, 90)], Default);
        Assert.That(r.FinalPercent, Is.Null);
        Assert.That(r.MissingScores, Is.EqualTo(new[] { "a" }));
        Assert.That(r.RequiresReconciliation, Is.False);
    }

    [Test]
    public void BothScoresMissing_StillListedOnce()
    {
        var r = CombinedScoring.Calculate([O("a", null, null), O("b", null, null)], Default);
        Assert.That(r.MissingScores, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void MissingScores_DoNotHideAReconciliationElsewhere()
    {
        var r = CombinedScoring.Calculate([O("split", 95, 60), O("pending", null, 50)], Default);
        Assert.That(r.RequiresReconciliation, Is.True);
        Assert.That(r.FinalPercent, Is.Null);
    }

    [TestCase("101", "50")]
    [TestCase("50", "100.01")]
    [TestCase("-1", "50")]
    [TestCase("50", "-0.5")]
    public void OutOfRangeScores_AreRefused(string ai, string human)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CombinedScoring.Calculate([O("x", decimal.Parse(ai), decimal.Parse(human))], Default));
        Assert.That(ex!.Message, Does.Contain("outside 0..100"));
    }

    [Test]
    public void ScoresAtTheLimits_AreAccepted()
    {
        var r = CombinedScoring.Calculate([O("x", 0, 100)], Default);
        Assert.That(r.Objectives[0].Combined, Is.EqualTo(60m));
        Assert.That(r.Objectives[0].DifferencePoints, Is.EqualTo(100m));
    }

    [Test]
    public void OutOfRangeOnSmallScale_IsRefused() =>
        Assert.Throws<ArgumentException>(() => CombinedScoring.Calculate([O("x", 11, 5, max: 10)], Default));

    [TestCase("0")]
    [TestCase("-10")]
    public void NonPositiveMax_IsRefused(string max) =>
        Assert.Throws<ArgumentException>(() => CombinedScoring.Calculate([O("x", 0, 0, max: decimal.Parse(max))], Default));

    [Test]
    public void NegativeObjectiveWeight_IsRefused() =>
        Assert.Throws<ArgumentException>(() => CombinedScoring.Calculate([O("x", 50, 50, weight: -1)], Default));

    [Test]
    public void NoObjectives_IsRefused() =>
        Assert.Throws<ArgumentException>(() => CombinedScoring.Calculate([], Default));

    [Test]
    public void InvalidPolicy_IsRefusedBeforeScoring() =>
        Assert.Throws<ArgumentException>(() => CombinedScoring.Calculate([O("x", 50, 50)],
            new GradingPolicyRules { AiWeight = 0.5m, HumanWeight = 0.6m }));

    [Test]
    public void Combine_RoundsToFourPlaces_AwayFromZero()
    {
        var half = new GradingPolicyRules { AiWeight = 0.5m, HumanWeight = 0.5m };
        Assert.That(CombinedScoring.Combine(0.0001m, 0m, half), Is.EqualTo(0.0001m));   // 0.00005 rounds up
        Assert.That(CombinedScoring.Combine(0.00001m, 0m, half), Is.EqualTo(0m));       // 0.000005 rounds down
    }

    [Test]
    public void FinalPercent_RoundsToTwoPlaces()
    {
        var r = CombinedScoring.Calculate([O("a", 100, 100), O("b", 100, 100), O("c", 0, 0)], Default);
        Assert.That(r.FinalPercent, Is.EqualTo(66.67m));
    }

    [Test]
    public void DifferencePoints_RoundToFourPlaces()
    {
        // 1 point of 3 is 33.3333... percentage points.
        var r = CombinedScoring.Calculate([O("x", 2, 1, max: 3)], Default);
        Assert.That(r.Objectives[0].DifferencePoints, Is.EqualTo(33.3333m));
    }

    [Test]
    public void ArithmeticIsExact_NoBinaryFloatDrift()
    {
        // 0.1 + 0.2 style sums must come out exact in decimal.
        var p = new GradingPolicyRules { AiWeight = 0.3m, HumanWeight = 0.7m };
        Assert.That(CombinedScoring.Combine(0.1m, 0.2m, p), Is.EqualTo(0.17m));
    }

    [Test]
    public void DifferenceIsSymmetric()
    {
        var a = CombinedScoring.Calculate([O("x", 90, 70)], Default).Objectives[0];
        var b = CombinedScoring.Calculate([O("x", 70, 90)], Default).Objectives[0];
        Assert.That(a.DifferencePoints, Is.EqualTo(b.DifferencePoints));
        Assert.That(a.Band, Is.EqualTo(b.Band));
        Assert.That(a.Combined, Is.Not.EqualTo(b.Combined), "weights are not symmetric");
    }

    [Test]
    public void HumanOnlyPolicy_ReproducesTheHumanScore()
    {
        var p = new GradingPolicyRules { AiWeight = 0, HumanWeight = 1 };
        var r = CombinedScoring.Calculate([O("x", 10, 73)], p);
        Assert.That(r.FinalPercent, Is.EqualTo(73m));
    }

    [Test]
    public void ResultKeepsInputOrderAndValues()
    {
        var r = CombinedScoring.Calculate([O("z", 1, 2, max: 5, weight: 2), O("a", 3, 4, max: 5, weight: 1)], Default);
        Assert.That(r.Objectives.Select(o => o.ObjectiveKey), Is.EqualTo(new[] { "z", "a" }));
        Assert.That(r.Objectives[0].MaxScore, Is.EqualTo(5m));
        Assert.That(r.Objectives[0].Weight, Is.EqualTo(2m));
        Assert.That(r.Objectives[0].AiScore, Is.EqualTo(1m));
        Assert.That(r.Objectives[0].HumanScore, Is.EqualTo(2m));
    }
}
