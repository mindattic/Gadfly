namespace Gadfly.Server.Grading;

/// <summary>One objective's class-wide average, from the released results only (GAD-T311/GAD-T378).</summary>
public sealed record ClassSummaryObjective(string Key, string Title, decimal MaxScore, decimal? Average, int Count);

/// <summary>One 10-point-wide bucket of the score distribution, e.g. "80-89%".</summary>
public sealed record ClassSummaryBucket(string Range, int Count);

/// <summary>A student the instructor still needs to follow up with.</summary>
public sealed record ClassSummaryOutstanding(string StudentName, string Status);

/// <summary>
/// The class summary for one assessment (GAD-T311/GAD-T378): an aggregate across every student assigned to one exam,
/// built only from results released to the student (<see cref="Gadfly.Server.Data.ExamAssignment.ResultsReleasedAt"/>),
/// using the same per-session content model as the individual assessment report (<see cref="AssessmentReport"/>) so the
/// class figures and a student's own report never disagree. Never names which student holds which score.
/// </summary>
public sealed record ClassSummaryReport
{
    public required Guid ExamId { get; init; }
    public required string ExamTitle { get; init; }
    public string? OrganizationName { get; init; }
    public required int TotalAssigned { get; init; }
    /// <summary>Released results: counted in the distribution and the per-objective averages.</summary>
    public required int Released { get; init; }
    /// <summary>Attempted and ended, but not yet released to the student (awaiting grading or release).</summary>
    public required int CompletedNotReleased { get; init; }
    /// <summary>Students with no ended attempt yet, by name (never more than a few hundred; this is a class, not a cohort export).</summary>
    public required IReadOnlyList<string> NotStarted { get; init; }
    /// <summary>The released final percentages, for the mean/median/distribution.</summary>
    public required IReadOnlyList<decimal> Scores { get; init; }
    public required IReadOnlyList<ClassSummaryObjective> Objectives { get; init; }

    public decimal? Mean => Scores.Count == 0 ? null : Math.Round(Scores.Average(), 1);

    public decimal? Median
    {
        get
        {
            if (Scores.Count == 0) return null;
            var sorted = Scores.OrderBy(s => s).ToList();
            var mid = sorted.Count / 2;
            return Math.Round(sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m, 1);
        }
    }

    /// <summary>Ten-point buckets from 0-9% up to 90-100%, highest first (for printing top to bottom).</summary>
    public IReadOnlyList<ClassSummaryBucket> Distribution()
    {
        var buckets = new int[10];
        foreach (var s in Scores)
        {
            var i = (int)Math.Clamp(Math.Floor(s / 10m), 0, 9);
            buckets[(int)i]++;
        }
        var labels = new[] { "0-9%", "10-19%", "20-29%", "30-39%", "40-49%", "50-59%", "60-69%", "70-79%", "80-89%", "90-100%" };
        return Enumerable.Range(0, 10).Reverse().Select(i => new ClassSummaryBucket(labels[i], buckets[i])).ToList();
    }

    public string FileStem()
    {
        var raw = $"{ExamTitle}-class-summary";
        var clean = new string(raw.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        while (clean.Contains("--")) clean = clean.Replace("--", "-");
        return clean.Length == 0 ? ExamId.ToString("N") : clean[..Math.Min(clean.Length, 90)];
    }
}
