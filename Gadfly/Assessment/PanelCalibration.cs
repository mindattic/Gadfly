namespace Gadfly.Assessment;

/// <summary>
/// Agreement statistics between two raters on the same items (GAD-T270; research note 05). Quadratic weighted
/// kappa (QWK) on ordinal categories and the standardized mean difference (SMD) of one rater against another:
/// the statistics behind the Williamson, Xi &amp; Breyer (2012) acceptance rules for machine scores, as reported
/// by Casabianca et al. (2023).
/// </summary>
public static class RaterAgreement
{
    /// <summary>
    /// Quadratic weighted kappa of paired ratings on the categories <paramref name="min"/>..<paramref name="max"/>:
    /// 1 - Σ w·O / Σ w·E with w = (i - j)² / (k - 1)². Null when it is undefined (no pairs, a single category, or
    /// both raters giving every item the same category, so chance disagreement is zero).
    /// </summary>
    public static double? QuadraticWeightedKappa(IReadOnlyList<(int A, int B)> pairs, int min, int max)
    {
        if (pairs.Count == 0 || max <= min) return null;
        var k = max - min + 1;
        var observed = new double[k, k];
        var rowTotals = new double[k];
        var colTotals = new double[k];
        foreach (var (a, b) in pairs)
        {
            if (a < min || a > max || b < min || b > max) throw new ArgumentOutOfRangeException(nameof(pairs), $"A rating is outside {min}..{max}.");
            observed[a - min, b - min]++;
            rowTotals[a - min]++;
            colTotals[b - min]++;
        }
        double n = pairs.Count, num = 0, den = 0;
        for (var i = 0; i < k; i++)
        for (var j = 0; j < k; j++)
        {
            var w = (double)(i - j) * (i - j) / ((double)(k - 1) * (k - 1));
            num += w * observed[i, j];
            den += w * rowTotals[i] * colTotals[j] / n;
        }
        return den == 0 ? null : 1 - num / den;
    }

    /// <summary>
    /// Standardized mean difference of <paramref name="rater"/> against <paramref name="reference"/> on the same items:
    /// (mean_rater - mean_reference) / sqrt((sd_rater² + sd_reference²) / 2), sample SDs. Positive means the rater is
    /// more lenient. Null when undefined (fewer than two items or no spread in either rater).
    /// </summary>
    public static double? StandardizedMeanDifference(IReadOnlyList<double> rater, IReadOnlyList<double> reference)
    {
        if (rater.Count != reference.Count) throw new ArgumentException("Both raters must score the same items.");
        if (rater.Count < 2) return null;
        var pooled = Math.Sqrt((Variance(rater) + Variance(reference)) / 2);
        return pooled == 0 ? null : (rater.Average() - reference.Average()) / pooled;
    }

    private static double Variance(IReadOnlyList<double> xs)
    {
        var mean = xs.Average();
        return xs.Sum(x => (x - mean) * (x - mean)) / (xs.Count - 1);
    }
}

/// <summary>
/// When a grading panel is accepted for an exam (GAD-T270). Defaults are the operational rules for automated
/// scores in large-scale testing (Williamson et al. 2012, as reported by Casabianca2023): panel-human QWK at least
/// .70, no more than .10 below the human-human QWK, and |SMD| under .15. They are rules of thumb from practice,
/// not derived optima.
/// </summary>
public sealed record CalibrationThresholds
{
    public double MinQwk { get; init; } = 0.70;
    public double MaxDegradation { get; init; } = 0.10;
    public double MaxAbsSmd { get; init; } = 0.15;
    /// <summary>The smallest calibration set that is evaluated at all (kappa on a handful of items means little).</summary>
    public int MinItems { get; init; } = 30;

    public static CalibrationThresholds Default { get; } = new();
}

/// <summary>
/// One answer of a calibration set, rated on the rubric's mastery levels (0..5): the panel's level and two blind,
/// independent human levels (double marking). Human B is what measures the human-human baseline.
/// </summary>
public sealed record CalibrationItem(string ItemId, int PanelLevel, int HumanALevel, int HumanBLevel);

/// <summary>The outcome of checking a calibration set against <see cref="CalibrationThresholds"/>.</summary>
public sealed record CalibrationReport(
    int Items,
    double? HumanHumanQwk,
    double? PanelHumanQwk,
    double? Degradation,
    double? Smd,
    bool Accepted,
    IReadOnlyList<string> Failures,
    CalibrationThresholds Thresholds);

/// <summary>
/// Evaluates a blind double-marked calibration set (GAD-T270). The panel is compared with each human marker and
/// the weaker comparison counts (the lower QWK and the larger |SMD|), so a panel is not accepted by matching only
/// the more lenient or harsher of the two. The panel is accepted only when every rule holds.
/// </summary>
public static class PanelCalibration
{
    public const int MinLevel = (int)MasteryLevel.InsufficientEvidence;
    public const int MaxLevel = (int)MasteryLevel.TransferAndCriticalReasoning;

    public static CalibrationReport Evaluate(IReadOnlyList<CalibrationItem> items, CalibrationThresholds? thresholds = null)
    {
        var t = thresholds ?? CalibrationThresholds.Default;
        foreach (var i in items)
            foreach (var level in new[] { i.PanelLevel, i.HumanALevel, i.HumanBLevel })
                if (level is < MinLevel or > MaxLevel)
                    throw new ArgumentOutOfRangeException(nameof(items), $"Item '{i.ItemId}' has a level outside {MinLevel}..{MaxLevel}.");

        var failures = new List<string>();
        if (items.Count < t.MinItems)
            failures.Add($"The calibration set has {items.Count} double-marked answers; at least {t.MinItems} are needed.");

        var humanHuman = RaterAgreement.QuadraticWeightedKappa(items.Select(i => (i.HumanALevel, i.HumanBLevel)).ToList(), MinLevel, MaxLevel);
        var panelA = RaterAgreement.QuadraticWeightedKappa(items.Select(i => (i.PanelLevel, i.HumanALevel)).ToList(), MinLevel, MaxLevel);
        var panelB = RaterAgreement.QuadraticWeightedKappa(items.Select(i => (i.PanelLevel, i.HumanBLevel)).ToList(), MinLevel, MaxLevel);
        double? panelHuman = panelA is { } a && panelB is { } b ? Math.Min(a, b) : null;

        var panel = items.Select(i => (double)i.PanelLevel).ToList();
        var smdA = RaterAgreement.StandardizedMeanDifference(panel, items.Select(i => (double)i.HumanALevel).ToList());
        var smdB = RaterAgreement.StandardizedMeanDifference(panel, items.Select(i => (double)i.HumanBLevel).ToList());
        double? smd = smdA is { } sa && smdB is { } sb ? (Math.Abs(sa) >= Math.Abs(sb) ? sa : sb) : null;
        double? degradation = humanHuman is { } hh && panelHuman is { } ph ? hh - ph : null;

        if (humanHuman is null) failures.Add("The human-human agreement could not be measured (the two markers need varied levels on the same answers).");
        if (panelHuman is null) failures.Add("The panel-human agreement could not be measured (the panel and the markers need varied levels).");
        else if (panelHuman < t.MinQwk) failures.Add($"Panel-human QWK is {panelHuman:0.00}; at least {t.MinQwk:0.00} is required.");
        if (degradation is { } d && d > t.MaxDegradation)
            failures.Add($"Panel-human QWK is {d:0.00} below the human-human QWK ({humanHuman:0.00}); at most {t.MaxDegradation:0.00} is allowed.");
        if (smd is null) failures.Add("The standardized mean difference could not be measured (no spread in the levels).");
        else if (Math.Abs(smd.Value) >= t.MaxAbsSmd)
            failures.Add($"The panel is systematically {(smd > 0 ? "more lenient" : "harsher")} than the markers (SMD {smd:0.00}); |SMD| must be under {t.MaxAbsSmd:0.00}.");

        return new CalibrationReport(items.Count, Round(humanHuman), Round(panelHuman), Round(degradation), Round(smd), failures.Count == 0, failures, t);
    }

    private static double? Round(double? x) => x is { } v ? Math.Round(v, 4, MidpointRounding.AwayFromZero) : null;
}
