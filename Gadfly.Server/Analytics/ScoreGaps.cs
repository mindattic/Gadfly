using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Analytics;

// Score gaps by group (GAD-T433; ElenchusAtScale Sections 5.4.1, 5.4.3 and 3.9; docs/EVIDENCE_MAP.md mechanic 7). For every objective
// that has both the AI panel's latest level and the human examiner's latest level, the two are compared within each group of
// students. A difference in levels between groups is not, by itself, unfairness: groups can differ in what they learned. A
// difference that appears only in the panel's levels, or only in the human's, is: so each group's panel-minus-human gap is
// reported as a standardized mean difference and checked against the 0.15 used for automated scoring (Williamson et al. 2012,
// as cited in Casabianca et al. 2023), and the groups' gaps are checked against each other. Plain arithmetic over stored rows.

/// <summary>One objective graded by both the panel and a human, for one student in one group. Levels are 0 (insufficient evidence) to 5.</summary>
public sealed record LevelPair(string Group, Guid Student, int PanelLevel, int HumanLevel);

/// <summary>One group's panel and human levels and the gap between them.</summary>
/// <param name="Pairs">Objectives with both levels.</param>
/// <param name="Conclusive">Pairs where both levels are 1 to 5 (the means and the gap use only these).</param>
/// <param name="Smd">Mean panel-minus-human gap over the pooled standard deviation of the two sets of levels; null when undefined.</param>
public sealed record ScoreGapGroup(string Group, int Students, int Pairs, int Conclusive, double? MeanPanel, double? MeanHuman,
    double? MeanGap, double? Smd, double? SameLevel, double? WithinOne, double? PanelInconclusive, double? HumanInconclusive)
{
    public bool TooFew => Students < GroupFairness.MinStudents;
}

public sealed record ScoreGapsReport(IReadOnlyList<ScoreGapGroup> Groups, int Pairs, int Unplaced, IReadOnlyList<string> Warnings)
{
    /// <summary>The total of <see cref="Pairs"/> is not shown while any group is withheld (GAD-T486).</summary>
    public bool TotalWithheld => GroupFairness.WithholdsTotal(Groups, g => g.TooFew);
}

public sealed class ScoreGaps(GadflyDbContext db, Guid organizationId)
{
    /// <summary>A group whose panel-minus-human gap is at least this many pooled standard deviations is warned (the automated-scoring threshold).</summary>
    public const double SmdThreshold = 0.15;

    /// <summary>Groups whose mean panel-minus-human gaps differ by at least this many levels are warned.</summary>
    public const double GapSpreadThreshold = 0.25;

    public const string Caveat =
        "Each row compares the AI panel's level with the human examiner's level on the same objectives. A difference in levels between " +
        "groups is not by itself unfair, since groups can differ in what they have learned. A gap between the panel and the human that " +
        "appears for one group and not another is the warning sign: one of the two graders reads that group differently. Agreement " +
        "is still not validity (both graders could share a bias). Level 0, insufficient evidence, is counted apart and left out of the means.";

    /// <summary>The pairs for the given sessions: the latest real (not mock) panel level and the latest human level per objective.</summary>
    public async Task<ScoreGapsReport> BuildAsync(IReadOnlyList<FairSession> sessions, Func<FairSession, string?> groupOf, CancellationToken ct = default)
    {
        var byId = sessions.ToDictionary(s => s.Id);
        var ids = byId.Keys.ToList();
        var ai = new List<(Guid SessionId, Guid ObjectiveId, int Level, DateTimeOffset At)>();
        var human = new List<(Guid Id, Guid SessionId, Guid ObjectiveId, int Level, Guid? Supersedes, DateTimeOffset At)>();
        foreach (var chunk in ids.Chunk(500))
        {
            ai.AddRange((await db.AiObjectiveGrades.AsNoTracking()
                    .Where(g => g.OrganizationId == organizationId && chunk.Contains(g.SessionId) && !g.IsMock)
                    .Select(g => new { g.SessionId, g.ObjectiveId, g.Level, g.CreatedAt }).ToListAsync(ct))
                .Select(g => (g.SessionId, g.ObjectiveId, (int)g.Level, g.CreatedAt)));
            human.AddRange((await db.HumanObjectiveGrades.AsNoTracking()
                    .Where(g => g.OrganizationId == organizationId && chunk.Contains(g.SessionId))
                    .Select(g => new { g.Id, g.SessionId, g.ObjectiveId, g.Level, g.SupersedesId, g.SubmittedAt }).ToListAsync(ct))
                .Select(g => (g.Id, g.SessionId, g.ObjectiveId, (int)g.Level, g.SupersedesId, g.SubmittedAt)));
        }
        var aiLatest = ai.GroupBy(g => (g.SessionId, g.ObjectiveId)).ToDictionary(g => g.Key, g => g.MaxBy(x => x.At).Level);
        var superseded = human.Where(g => g.Supersedes is not null).Select(g => g.Supersedes!.Value).ToHashSet();
        var humanLatest = human.Where(g => !superseded.Contains(g.Id)).GroupBy(g => (g.SessionId, g.ObjectiveId))
            .ToDictionary(g => g.Key, g => g.MaxBy(x => x.At).Level);

        var pairs = new List<LevelPair>();
        var unplaced = new HashSet<Guid>();
        foreach (var (key, panel) in aiLatest)
        {
            if (!humanLatest.TryGetValue(key, out var h) || !byId.TryGetValue(key.SessionId, out var s)) continue;
            if (groupOf(s) is not { } group) { unplaced.Add(s.StudentUserId); continue; }
            pairs.Add(new LevelPair(group, s.StudentUserId, panel, h));
        }
        var groups = Summarize(pairs);
        return new ScoreGapsReport(groups, pairs.Count, unplaced.Count, Warnings(groups));
    }

    /// <summary>One row per group; groups under <see cref="GroupFairness.MinStudents"/> students keep only their name and size.</summary>
    public static IReadOnlyList<ScoreGapGroup> Summarize(IEnumerable<LevelPair> pairs) =>
        GroupFairness.Ordered(pairs.GroupBy(p => p.Group, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var students = g.Select(p => p.Student).Distinct().Count();
            if (students < GroupFairness.MinStudents)
                return new ScoreGapGroup(g.First().Group, students, 0, 0, null, null, null, null, null, null, null, null);
            var all = g.ToList();
            var conclusive = all.Where(p => p.PanelLevel is >= 1 and <= 5 && p.HumanLevel is >= 1 and <= 5).ToList();
            var panel = conclusive.Select(p => (double)p.PanelLevel).ToList();
            var humanLevels = conclusive.Select(p => (double)p.HumanLevel).ToList();
            var gap = GroupFairness.Mean(conclusive.Select(p => (double)(p.PanelLevel - p.HumanLevel)));
            return new ScoreGapGroup(g.First().Group, students, all.Count, conclusive.Count, GroupFairness.Mean(panel),
                GroupFairness.Mean(humanLevels), gap, Smd(panel, humanLevels),
                conclusive.Count == 0 ? null : conclusive.Count(p => p.PanelLevel == p.HumanLevel) / (double)conclusive.Count,
                conclusive.Count == 0 ? null : conclusive.Count(p => Math.Abs(p.PanelLevel - p.HumanLevel) <= 1) / (double)conclusive.Count,
                all.Count(p => p.PanelLevel == 0) / (double)all.Count, all.Count(p => p.HumanLevel == 0) / (double)all.Count);
        }), g => g.Group).ToList();

    /// <summary>
    /// Standardized mean difference of two sets of levels: (mean of <paramref name="panel"/> - mean of <paramref name="human"/>)
    /// over the pooled standard deviation sqrt((var1 + var2) / 2). Null with fewer than two values each; 0 when every level is
    /// the same in both; null when the means differ but neither set varies (the ratio is undefined).
    /// </summary>
    public static double? Smd(IReadOnlyCollection<double> panel, IReadOnlyCollection<double> human)
    {
        if (panel.Count < 2 || human.Count < 2) return null;
        var diff = panel.Average() - human.Average();
        var pooled = Math.Sqrt((GroupFairness.Variance(panel)!.Value + GroupFairness.Variance(human)!.Value) / 2);
        if (pooled < 1e-9) return Math.Abs(diff) < 1e-9 ? 0 : null;
        return diff / pooled;
    }

    /// <summary>Too little to compare, a group whose own gap passes <see cref="SmdThreshold"/>, or gaps that differ between groups.</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<ScoreGapGroup> groups)
    {
        var usable = groups.Where(g => !g.TooFew && g.Conclusive > 0).ToList();
        var w = new List<string>();
        if (usable.Count < 2)
        {
            w.Add($"Fewer than two groups have {GroupFairness.MinStudents} or more students with objectives graded by both the panel and a human, so the groups can't be compared yet.");
            return w;
        }
        foreach (var g in usable.Where(g => g.Smd is { } smd && Math.Abs(smd) >= SmdThreshold))
            w.Add($"For {g.Group} the panel's levels sit {(g.Smd > 0 ? "above" : "below")} the human examiner's by {Math.Abs(g.Smd!.Value):0.00} " +
                  $"standard deviations (the threshold is {SmdThreshold:0.00}). Read this group's panel levels with care and compare the evidence.");
        var gaps = usable.Where(g => g.MeanGap is not null).ToList();
        if (gaps.Count >= 2)
        {
            var hi = gaps.MaxBy(g => g.MeanGap!.Value)!;
            var lo = gaps.MinBy(g => g.MeanGap!.Value)!;
            if (hi.MeanGap!.Value - lo.MeanGap!.Value >= GapSpreadThreshold)
                w.Add($"The panel-minus-human gap differs between groups: {hi.Group} {Signed(hi.MeanGap.Value)} against {lo.Group} {Signed(lo.MeanGap.Value)} " +
                      $"levels (the threshold is {GapSpreadThreshold:0.00}). One of the two graders reads these groups differently; look at both before relying on either.");
        }
        return w;
    }

    public static string Signed(double v) => (v > 0 ? "+" : "") + v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
