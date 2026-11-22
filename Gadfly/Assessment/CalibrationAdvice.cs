namespace Gadfly.Assessment;

/// <summary>One answer's group for the by-group check: any label the institution pastes (never stored).</summary>
public sealed record GroupSmd(string Group, int Items, double? Smd, bool Flagged);

/// <summary>How well the combined grade agrees with the independent second marker at one AI weight.</summary>
public sealed record WeightOption(decimal AiWeight, double? Qwk);

/// <summary>How a boundary margin would have routed the calibration set.</summary>
public sealed record MarginOption(decimal MarginPoints, int ContestedCaught, int Contested, int Routed, int Items);

/// <summary>How a panel-spread limit would have routed the calibration set.</summary>
public sealed record SpreadOption(int MaxSpreadLevels, int Routed, int Items, double? MeanGapRouted, double? MeanGapKept);

/// <summary>
/// What a calibration set suggests for the exam's grading policy (GAD-T427). Suggestions only: an approver still changes the
/// policy through the usual audited request, and a panel that was not accepted is never given weight.
/// </summary>
public sealed record CalibrationAdviceReport(
    decimal RecommendedAiWeight,
    IReadOnlyList<WeightOption> Weights,
    decimal RecommendedAdditionalReviewAt,
    decimal RecommendedMandatoryReconciliationAt,
    decimal? RecommendedBoundaryMargin,
    IReadOnlyList<MarginOption> Margins,
    int? RecommendedMaxPanelSpread,
    IReadOnlyList<SpreadOption> Spreads,
    IReadOnlyList<string> Notes);

/// <summary>
/// Turns a blind double-marked calibration set into the numbers ElenchusAtScale Section 3.8 says to choose from pilot data
/// (GAD-T427): the AI weight (alpha), the disagreement thresholds (tau), and the single-answer triggers (boundary margin and
/// panel spread, Section 5.4.1, correction 41). Also the standardized mean difference by group (Section 5.4.1, E4): an
/// aggregate SMD can hide a gap that one group of students carries. All of it is arithmetic on the levels; nothing calls a model.
/// </summary>
public static class CalibrationAdvice
{
    /// <summary>The weights tried, from human-only to an even split. The human always keeps at least half (Part 8.1).</summary>
    public static readonly IReadOnlyList<decimal> WeightGrid = [0m, 0.1m, 0.2m, 0.3m, 0.4m, 0.5m];

    /// <summary>The boundary margins tried, in percentage points.</summary>
    public static readonly IReadOnlyList<decimal> MarginGrid = [0m, 2.5m, 5m, 7.5m, 10m, 15m];

    /// <summary>Share of answers where the two markers fell on opposite sides of a boundary that the margin should catch.</summary>
    public const double ContestedCoverage = 0.8;

    /// <summary>Most answers a spread limit should route to reconciliation (about 20% in Wang2024; a little room above it).</summary>
    public const double MaxRoutedShare = 0.25;

    /// <summary>The smallest group the by-group SMD is reported for: fewer answers say too little.</summary>
    public const int MinGroupItems = 10;

    /// <summary>A level as a percentage of the objective's maximum (level 5 = 100%).</summary>
    public static decimal Percent(int level) => level * 20m;

    /// <summary>
    /// The advice for a calibration set. <paramref name="boundaries"/> are the policy's score boundaries (percent);
    /// <paramref name="memberLevels"/>, when known, are each answer's panel-member levels by item id (for the spread trigger).
    /// </summary>
    public static CalibrationAdviceReport Advise(IReadOnlyList<CalibrationItem> items, CalibrationReport report, IReadOnlyList<decimal> boundaries,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? memberLevels = null)
    {
        var notes = new List<string>();

        // alpha: the combined grade (panel with one marker) against the other, independent marker, both ways round.
        var weights = WeightGrid.Select(w => new WeightOption(w, CombinedAgreement(items, w))).ToList();
        decimal alpha = 0m;
        if (!report.Accepted)
            notes.Add("The panel did not meet the calibration thresholds, so its recommended weight is 0: grade on the human alone until a calibration is accepted.");
        else
        {
            var best = weights.Where(w => w.Qwk is not null).OrderByDescending(w => w.Qwk!.Value).ThenBy(w => w.AiWeight).FirstOrDefault();
            alpha = best?.AiWeight ?? 0m;
            var humanOnly = weights[0].Qwk;
            notes.Add(alpha == 0m
                ? "Adding the panel did not bring the grade closer to the independent second marker at any weight tried, so the recommended weight is 0."
                : $"With the panel at {alpha:0.#} the combined grade agrees best with the independent second marker (QWK {best!.Qwk:0.00}, human alone {humanOnly:0.00}).");
        }

        // tau: how far two humans already differ. A panel-human gap the two humans rarely show is worth a second look.
        var humanGaps = items.Select(i => (double)Math.Abs(Percent(i.HumanALevel) - Percent(i.HumanBLevel))).OrderBy(x => x).ToList();
        var review = (decimal)Math.Max(10, Quantile(humanGaps, 0.75));
        var mandatory = (decimal)Math.Max((double)review + 10, Quantile(humanGaps, 0.95));
        review = Math.Min(review, 100m);
        mandatory = Math.Min(Math.Max(mandatory, review), 100m);
        notes.Add($"The two markers differed by {Quantile(humanGaps, 0.75):0} points or less on three answers in four and {Quantile(humanGaps, 0.95):0} or less on nineteen in twenty; " +
                  $"the suggested thresholds are {review:0.#} points for additional review and {mandatory:0.#} for mandatory reconciliation (never under 10 and 20 apart).");

        // Boundary margin: catch most answers where the two markers fell on opposite sides of a boundary.
        var margins = new List<MarginOption>();
        decimal? margin = null;
        var bounds = (boundaries ?? []).Where(b => b > 0 && b < 100).ToList();
        var contested = items.Where(i => bounds.Any(b => Percent(i.HumanALevel) < b != Percent(i.HumanBLevel) < b)).ToList();
        if (bounds.Count == 0) notes.Add("The policy has no score boundaries, so the boundary trigger is off and no margin is suggested.");
        else
        {
            var weight = alpha > 0 ? alpha : 0.4m;
            foreach (var m in MarginGrid)
            {
                var rules = new GradingPolicyRules { ScoreBoundaries = bounds, BoundaryMarginPoints = m };
                bool Routed(CalibrationItem i)
                {
                    var ai = Percent(i.PanelLevel);
                    var human = Percent(i.HumanALevel);
                    return ReconciliationTriggers.OnBoundary(ai, human, ai * weight + human * (1 - weight), rules);
                }
                margins.Add(new MarginOption(m, contested.Count(Routed), contested.Count, items.Count(Routed), items.Count));
            }
            if (contested.Count == 0)
                notes.Add("The two markers never fell on opposite sides of a boundary in this set, so it cannot tune the boundary margin; keep the current one.");
            else
            {
                var pick = margins.FirstOrDefault(o => o.ContestedCaught >= ContestedCoverage * o.Contested) ?? margins[^1];
                margin = pick.MarginPoints;
                notes.Add($"A margin of {pick.MarginPoints:0.#} points would have sent {pick.ContestedCaught} of the {pick.Contested} answers the markers split on a boundary " +
                          $"to reconciliation, routing {pick.Routed} of {pick.Items} answers in all.");
            }
        }

        // Panel spread: the smallest limit that routes no more than about a quarter of answers.
        var spreads = new List<SpreadOption>();
        int? spread = null;
        var withMembers = memberLevels is null ? [] : items.Where(i => memberLevels.TryGetValue(i.ItemId, out var l) && l.Count >= 2).ToList();
        if (withMembers.Count == 0) notes.Add("The panel members' own levels are not in this set, so the panel-spread limit cannot be tuned from it.");
        else
        {
            for (var s = 0; s <= 3; s++)
            {
                var limit = s;
                var routed = withMembers.Where(i => memberLevels![i.ItemId].Max() - memberLevels[i.ItemId].Min() > limit).ToList();
                var kept = withMembers.Except(routed).ToList();
                static double? Gap(List<CalibrationItem> xs) => xs.Count == 0 ? null : xs.Average(i => Math.Abs(i.PanelLevel - (i.HumanALevel + i.HumanBLevel) / 2.0));
                spreads.Add(new SpreadOption(s, routed.Count, withMembers.Count, Gap(routed), Gap(kept)));
            }
            var pick = spreads.FirstOrDefault(o => o.Routed <= MaxRoutedShare * o.Items) ?? spreads[^1];
            spread = pick.MaxSpreadLevels;
            notes.Add($"A panel-spread limit of {pick.MaxSpreadLevels} level(s) would route {pick.Routed} of {pick.Items} answers" +
                      (pick.MeanGapRouted is { } gr && pick.MeanGapKept is { } gk ? $"; on those the panel was {gr:0.00} levels from the markers on average, against {gk:0.00} on the rest." : "."));
        }

        return new CalibrationAdviceReport(alpha, weights, review, mandatory, margin, margins, spread, spreads, notes);
    }

    /// <summary>
    /// QWK of the combined grade, round(w·panel + (1−w)·marker), against the other marker, averaged over both directions.
    /// At w = 0 this is the human-human agreement; a weight that beats it means the panel adds information.
    /// </summary>
    public static double? CombinedAgreement(IReadOnlyList<CalibrationItem> items, decimal aiWeight)
    {
        if (items.Count == 0) return null;
        int Mix(int panel, int human) => (int)Math.Round(aiWeight * panel + (1 - aiWeight) * human, MidpointRounding.AwayFromZero);
        var ab = RaterAgreement.QuadraticWeightedKappa(items.Select(i => (Mix(i.PanelLevel, i.HumanALevel), i.HumanBLevel)).ToList(), PanelCalibration.MinLevel, PanelCalibration.MaxLevel);
        var ba = RaterAgreement.QuadraticWeightedKappa(items.Select(i => (Mix(i.PanelLevel, i.HumanBLevel), i.HumanALevel)).ToList(), PanelCalibration.MinLevel, PanelCalibration.MaxLevel);
        return ab is { } x && ba is { } y ? Math.Round((x + y) / 2, 4) : ab ?? ba;
    }

    /// <summary>
    /// The panel's SMD against the markers' mean, per group (E4). <paramref name="groupOf"/> maps item ids to a group label;
    /// items without one are left out. Groups under <see cref="MinGroupItems"/> answers are listed without a figure.
    /// </summary>
    public static IReadOnlyList<GroupSmd> SmdByGroup(IReadOnlyList<CalibrationItem> items, IReadOnlyDictionary<string, string> groupOf, double maxAbsSmd = 0.15)
    {
        var result = new List<GroupSmd>();
        foreach (var g in items.Where(i => groupOf.ContainsKey(i.ItemId)).GroupBy(i => groupOf[i.ItemId], StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var list = g.ToList();
            if (list.Count < MinGroupItems) { result.Add(new GroupSmd(g.Key, list.Count, null, false)); continue; }
            var smd = RaterAgreement.StandardizedMeanDifference(list.Select(i => (double)i.PanelLevel).ToList(),
                list.Select(i => (i.HumanALevel + i.HumanBLevel) / 2.0).ToList());
            result.Add(new GroupSmd(g.Key, list.Count, smd is { } s ? Math.Round(s, 4) : null, smd is { } v && Math.Abs(v) >= maxAbsSmd));
        }
        return result;
    }

    /// <summary>
    /// Reads a pasted grouping: one "identifier, group" per line (comma, tab or semicolon). Blank lines and a header line
    /// are skipped. The identifiers are matched by the caller (e.g. to student emails or session codes).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseGrouping(string? text, out IReadOnlyList<string> problems)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();
        var lines = (text ?? "").Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n].Trim();
            if (line.Length == 0) continue;
            var parts = line.Split([',', '\t', ';'], 2);
            if (parts.Length < 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
            {
                if (n > 0 || map.Count > 0) issues.Add($"Line {n + 1}: expected \"identifier, group\".");
                continue;
            }
            var id = parts[0].Trim();
            var group = parts[1].Trim();
            if (group.Length > 60) group = group[..60];
            if (map.Count == 0 && issues.Count == 0 && id.ToLowerInvariant() is "identifier" or "email" or "student" or "session") continue;   // a header
            map[id] = group;
        }
        problems = issues;
        return map;
    }

    /// <summary>Linear-interpolated quantile of sorted values (0 when empty).</summary>
    internal static double Quantile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0) return 0;
        var pos = (sorted.Count - 1) * q;
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }
}
