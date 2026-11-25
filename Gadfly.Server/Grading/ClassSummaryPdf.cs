using System.Globalization;
using Gadfly.Server.Transcripts;

namespace Gadfly.Server.Grading;

/// <summary>
/// The class summary as a PDF for instructors (GAD-T311/GAD-T378): score distribution, per-objective average and who
/// hasn't completed, over one exam's released results. Laid out with the same hand-written PDF engine the transcript
/// and assessment-report downloads use (<see cref="TranscriptPdf.RenderBlocks"/>; GAD-T295/GAD-T311).
/// </summary>
public static class ClassSummaryPdf
{
    public static byte[] Render(ClassSummaryReport r, DateTimeOffset generatedAt)
    {
        var blocks = Blocks(r, generatedAt);
        return TranscriptPdf.RenderBlocks(blocks, (page, total) => $"Gadfly class summary · {r.ExamTitle} · page {page} of {total}",
            $"Class summary: {r.ExamTitle}", $"Released results for {r.ExamTitle}, generated {generatedAt.UtcDateTime:yyyy-MM-dd}.", generatedAt);
    }

    private static List<Block> Blocks(ClassSummaryReport r, DateTimeOffset generatedAt)
    {
        var b = new List<Block> { new(BlockKind.Title, "Class summary") };
        b.Add(new(BlockKind.Meta, [new Run(r.ExamTitle, Bold: true)]));
        if (r.OrganizationName is { Length: > 0 } org) b.Add(new(BlockKind.Meta, $"Organization: {org}"));
        b.Add(new(BlockKind.Meta, $"Assigned: {r.TotalAssigned} · released: {r.Released} · completed, awaiting release: {r.CompletedNotReleased} · "
            + $"not yet attempted: {r.NotStarted.Count}"));
        b.Add(new(BlockKind.Small, $"File made {generatedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC. Only results released to the student are counted below; "
            + "an in-progress or ungraded attempt never changes these figures."));

        b.Add(new(BlockKind.Heading, "Score distribution"));
        if (r.Released == 0)
        {
            b.Add(new(BlockKind.Body, "No results have been released to students yet for this assessment."));
        }
        else
        {
            b.Add(new(BlockKind.Small, [new Run("Mean: ", Bold: true), new Run(Pct(r.Mean)), new Run("   Median: ", Bold: true), new Run(Pct(r.Median))]));
            foreach (var bucket in r.Distribution())
                b.Add(new(BlockKind.Body, $"{bucket.Range,-8} {Bar(bucket.Count, r.Released)} {bucket.Count}", 1));
        }

        b.Add(new(BlockKind.Heading, "Per-objective average"));
        if (r.Objectives.Count == 0)
        {
            b.Add(new(BlockKind.Body, "No per-objective scores are available yet."));
        }
        else
        {
            foreach (var o in r.Objectives)
                b.Add(new(BlockKind.Body, $"{o.Title}: {(o.Average is { } a ? a.ToString("0.##", CultureInfo.InvariantCulture) : "—")} of {o.MaxScore.ToString("0.##", CultureInfo.InvariantCulture)} "
                    + $"(from {o.Count} released result{(o.Count == 1 ? "" : "s")})", 1));
        }

        b.Add(new(BlockKind.Heading, "Who hasn't completed"));
        if (r.NotStarted.Count == 0)
        {
            b.Add(new(BlockKind.Body, "Every assigned student has at least one ended attempt."));
        }
        else
        {
            b.Add(new(BlockKind.Small, $"{r.NotStarted.Count} student{(r.NotStarted.Count == 1 ? " has" : "s have")} no ended attempt yet:"));
            foreach (var name in r.NotStarted) b.Add(new(BlockKind.Body, name, 1));
        }
        if (r.CompletedNotReleased > 0)
            b.Add(new(BlockKind.Notice, $"{r.CompletedNotReleased} attempt{(r.CompletedNotReleased == 1 ? " has" : "s have")} ended but "
                + "its result is not yet released to the student (grading, reconciliation or approval may still be in progress); grade it in Grading."));
        return b;
    }

    private static string Pct(decimal? v) => v is { } x ? x.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "—";

    /// <summary>A plain-text bar, scaled to the largest share this distribution has: readable without colour or a chart library.</summary>
    private static string Bar(int count, int total)
    {
        if (total == 0) return "";
        var width = (int)Math.Round(count * 24.0 / total);
        return new string('#', Math.Max(count > 0 ? 1 : 0, width));
    }
}
