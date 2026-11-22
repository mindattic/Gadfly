using System.Globalization;
using Gadfly.Server.Transcripts;

namespace Gadfly.Server.Grading;

/// <summary>
/// The assessment report as a PDF (GAD-T311), beside its existing Markdown and CSV downloads
/// (<see cref="AssessmentReport.ToMarkdown"/>, <see cref="AssessmentReport.ToCsv"/>). Says the same thing in the same
/// order as the Markdown, laid out with the same hand-written PDF engine the transcript downloads use
/// (<see cref="TranscriptPdf.RenderBlocks"/>; GAD-T295), so no new PDF library is needed. Per objective: the AI and
/// examiner levels, what was missing and what the next level asks for, just as the Markdown shows them.
/// </summary>
public static class GradingReportPdf
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static byte[] Render(AssessmentReport report, DateTimeOffset generatedAt)
    {
        var blocks = Blocks(report);
        return TranscriptPdf.RenderBlocks(blocks,
            (page, total) => $"Gadfly assessment report · {report.StudentName} · page {page} of {total}",
            $"Assessment report: {report.ExamTitle}", $"{report.StudentName}, attempt {report.Attempt}. Status: {report.Status}.", generatedAt);
    }

    private static List<Block> Blocks(AssessmentReport r)
    {
        var b = new List<Block> { new(BlockKind.Title, "Assessment report") };
        if (r.IsMock) b.Add(new(BlockKind.Notice, "MOCK: the AI scores in this report come from the mock panel, not a genuine AI evaluation."));
        if (!r.IsApproved) b.Add(new(BlockKind.Notice, "Preliminary: this grade has not been approved. It is not a final grade."));
        b.Add(new(BlockKind.Meta, [new Run(r.ExamTitle, Bold: true)]));
        b.Add(new(BlockKind.Meta, $"Student: {r.StudentName} · Attempt {r.Attempt}" + (r.EndedAt is { } ended ? $" · ended {Stamp(ended)}" : "")));
        b.Add(new(BlockKind.Meta, [new Run("Status: "), new Run(r.Status, Bold: true)]));
        b.Add(new(BlockKind.Meta,
        [
            new Run("Final grade: "),
            new Run(r.FinalPercent is { } f ? AssessmentReport.Pct(f) : "not available", Bold: true),
            new Run(r.Label is { } l ? $" ({l})" : ""),
        ]));
        b.Add(new(BlockKind.Small, "Grading policy: " + r.Policy));

        b.Add(new(BlockKind.Heading, "Learning objectives"));
        if (r.IsFormative) b.Add(new(BlockKind.Notice, "Formative scores: " + r.ScoreUseReason));

        foreach (var o in r.Objectives)
        {
            b.Add(new(BlockKind.Subheading, o.Title));
            var combined = $"Combined {N(o.Combined)} of {N(o.MaxScore)}" + (o.Band is { } band ? $" ({band})" : "");
            b.Add(new(BlockKind.Small, combined + $" · weight {N(o.Weight)}"));
            if (o.HumanLevel is not null) b.Add(new(BlockKind.Small, $"Examiner: {o.HumanLevel}" + (o.HumanComment is { } c ? $": {c}" : "")));
            if (o.AiLevel is not null)
                b.Add(new(BlockKind.Small, $"AI (preliminary{(o.AiIsMock ? ", MOCK" : "")}): {o.AiLevel}, confidence {o.AiConfidence}"
                    + (o.AiExplanation is { Length: > 0 } x ? $". {x}" : "")));
            if (o.Reconciliation is not null) b.Add(new(BlockKind.Small, "Reconciliation: " + o.Reconciliation));
            if (r.ChangeLine(o) is { } change) b.Add(new(BlockKind.Small, change));
            if (o.WhatWasMissing.Count > 0)
            {
                b.Add(new(BlockKind.Small, [new Run("What was missing:", Italic: true)], 1));
                foreach (var m in o.WhatWasMissing) b.Add(new(BlockKind.Quote, m, 1));
            }
            foreach (var e in o.Evidence)
                b.Add(new(BlockKind.Quote, [new Run($"Response {e.Sequence}{(e.Corrected ? ", verified correction" : "")}: ", Italic: true), new Run("\"" + e.VerifiedText + "\"")], 1));
        }

        b.Add(new(BlockKind.Heading, "Knowledge gaps"));
        if (r.KnowledgeGaps.Count == 0) b.Add(new(BlockKind.Body, "None identified."));
        foreach (var g in r.KnowledgeGaps) b.Add(new(BlockKind.Body, g, 1));

        b.Add(new(BlockKind.Heading, "Approval"));
        if (r.Approvals.Count == 0) b.Add(new(BlockKind.Body, "Not approved yet."));
        foreach (var a in r.Approvals) b.Add(new(BlockKind.Small, a));

        if (r.Appeals.Count > 0)
        {
            b.Add(new(BlockKind.Heading, "Appeals"));
            foreach (var a in r.Appeals) b.Add(new(BlockKind.Small, a));
        }
        return b;
    }

    private static string Stamp(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd HH:mm", Ci) + " UTC";
    private static string N(decimal? v) => v is { } x ? x.ToString("0.##", Ci) : "—";
}
