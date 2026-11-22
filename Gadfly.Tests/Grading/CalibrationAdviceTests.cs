using Gadfly.Assessment;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>GAD-T427: what a calibration set suggests for the AI weight, thresholds and triggers, and the by-group SMD.</summary>
[TestFixture]
public class CalibrationAdviceTests
{
    /// <summary>Two markers who agree closely and a panel that tracks them, over every level.</summary>
    private static List<CalibrationItem> Agreeing(int n = 60) =>
        Enumerable.Range(0, n).Select(i =>
        {
            var level = i % 6;
            var b = i % 7 == 0 ? Math.Min(5, level + 1) : level;
            return new CalibrationItem($"i{i}", level, level, b);
        }).ToList();

    [Test]
    public void ANotAcceptedPanel_IsRecommendedNoWeight()
    {
        var items = Agreeing();
        var report = PanelCalibration.Evaluate(items) with { Accepted = false };
        var advice = CalibrationAdvice.Advise(items, report, [50m]);
        Assert.That(advice.RecommendedAiWeight, Is.EqualTo(0m));
        Assert.That(advice.Notes[0], Does.Contain("recommended weight is 0"));
    }

    [Test]
    public void AnAcceptedPanel_GetsTheWeightThatAgreesBestWithTheOtherMarker_NeverAboveHalf()
    {
        var items = Agreeing();
        var report = PanelCalibration.Evaluate(items);
        Assert.That(report.Accepted, Is.True, string.Join(" ", report.Failures));
        var advice = CalibrationAdvice.Advise(items, report, [50m]);
        Assert.That(advice.Weights.Select(w => w.AiWeight), Is.EqualTo(CalibrationAdvice.WeightGrid));
        Assert.That(advice.RecommendedAiWeight, Is.InRange(0m, 0.5m));
        var best = advice.Weights.Where(w => w.Qwk is not null).Max(w => w.Qwk!.Value);
        Assert.That(advice.Weights.First(w => w.AiWeight == advice.RecommendedAiWeight).Qwk, Is.EqualTo(best));
    }

    [Test]
    public void Thresholds_AreNeverUnderTenAndTwentyApart()
    {
        var items = Agreeing();
        var advice = CalibrationAdvice.Advise(items, PanelCalibration.Evaluate(items), [50m]);
        Assert.That(advice.RecommendedAdditionalReviewAt, Is.GreaterThanOrEqualTo(10m));
        Assert.That(advice.RecommendedMandatoryReconciliationAt, Is.GreaterThanOrEqualTo(advice.RecommendedAdditionalReviewAt + 10m));
        Assert.That(advice.RecommendedMandatoryReconciliationAt, Is.LessThanOrEqualTo(100m));
    }

    [Test]
    public void BoundaryMargin_CatchesMostAnswersTheMarkersSplitOnABoundary()
    {
        // Markers at 2 and 3 (40% / 60%) straddle the 50% boundary.
        var items = Enumerable.Range(0, 40).Select(i => new CalibrationItem($"s{i}", i % 2 == 0 ? 2 : 3, 2, 3)).ToList();
        var advice = CalibrationAdvice.Advise(items, PanelCalibration.Evaluate(items), [50m]);
        Assert.That(advice.RecommendedBoundaryMargin, Is.Not.Null);
        var pick = advice.Margins.First(m => m.MarginPoints == advice.RecommendedBoundaryMargin);
        Assert.That(pick.ContestedCaught, Is.GreaterThanOrEqualTo(CalibrationAdvice.ContestedCoverage * pick.Contested));
    }

    [Test]
    public void WithoutMemberLevels_NoSpreadIsSuggested()
    {
        var items = Agreeing();
        var advice = CalibrationAdvice.Advise(items, PanelCalibration.Evaluate(items), [50m]);
        Assert.That(advice.RecommendedMaxPanelSpread, Is.Null);
        var withMembers = CalibrationAdvice.Advise(items, PanelCalibration.Evaluate(items), [50m],
            items.ToDictionary(i => i.ItemId, i => (IReadOnlyList<int>)[i.PanelLevel, Math.Min(5, i.PanelLevel + (i.ItemId.EndsWith('3') ? 3 : 0))]));
        Assert.That(withMembers.RecommendedMaxPanelSpread, Is.Not.Null);
        var pick = withMembers.Spreads.First(s => s.MaxSpreadLevels == withMembers.RecommendedMaxPanelSpread);
        Assert.That(pick.Routed, Is.LessThanOrEqualTo(CalibrationAdvice.MaxRoutedShare * pick.Items));
    }

    [Test]
    public void SmdByGroup_FindsAGapTheAggregateHides()
    {
        // Group A: the panel is a level harsher; group B: a level more lenient. Overall the two cancel out.
        var items = new List<CalibrationItem>();
        var groups = new Dictionary<string, string>();
        for (var i = 0; i < 30; i++)
        {
            var level = 1 + i % 4;
            items.Add(new CalibrationItem($"a{i}", level - 1, level, level));
            groups[$"a{i}"] = "A";
            items.Add(new CalibrationItem($"b{i}", level + 1, level, level));
            groups[$"b{i}"] = "B";
        }
        var overall = PanelCalibration.Evaluate(items);
        Assert.That(Math.Abs(overall.Smd!.Value), Is.LessThan(0.15));
        var byGroup = CalibrationAdvice.SmdByGroup(items, groups);
        Assert.That(byGroup.Select(g => g.Group), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(byGroup.All(g => g.Flagged), Is.True);
        Assert.That(byGroup[0].Smd, Is.LessThan(0));
        Assert.That(byGroup[1].Smd, Is.GreaterThan(0));
    }

    [Test]
    public void SmallGroups_AreListedWithoutAFigure()
    {
        var items = Agreeing(12);
        var byGroup = CalibrationAdvice.SmdByGroup(items, new Dictionary<string, string> { ["i0"] = "tiny" });
        Assert.That(byGroup.Single().Smd, Is.Null);
        Assert.That(byGroup.Single().Flagged, Is.False);
    }

    [Test]
    public void ParseGrouping_SkipsAHeaderAndReportsBadLines()
    {
        var map = CalibrationAdvice.ParseGrouping("email,group\nann@x.edu, A\nbob@x.edu\tB\n\nnonsense\n", out var problems);
        Assert.That(map, Has.Count.EqualTo(2));
        Assert.That(map["ANN@x.edu"], Is.EqualTo("A"));
        Assert.That(problems, Has.Count.EqualTo(1));
    }
}
