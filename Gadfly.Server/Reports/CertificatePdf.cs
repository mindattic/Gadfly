using System.Globalization;
using Gadfly.Server.Transcripts;

namespace Gadfly.Server.Reports;

/// <summary>
/// The completion certificate as a PDF (GAD-T311/GAD-T379), laid out with the same hand-written PDF engine the
/// transcript downloads use (<see cref="TranscriptPdf.RenderBlocks"/>; GAD-T295) rather than a new PDF library.
/// Carries its verification code and SHA-256, checked at the same page as a transcript
/// (<see cref="TranscriptEndpoints.VerifyPath"/>; <see cref="CertificateRegistry"/>).
/// </summary>
public static class CertificatePdf
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static byte[] Render(CertificateDocument d, TranscriptStamp stamp)
    {
        var blocks = Blocks(d, stamp);
        return TranscriptPdf.RenderBlocks(blocks,
            (page, total) => $"Gadfly certificate · verification code {stamp.Code} · page {page} of {total}",
            $"Certificate of completion: {d.ExamTitle}", $"{d.StudentName}, attempt {d.Attempt}. Verification code {stamp.Code}.", stamp.GeneratedAt);
    }

    private static List<Block> Blocks(CertificateDocument d, TranscriptStamp stamp)
    {
        var b = new List<Block>
        {
            new(BlockKind.Title, "Certificate of completion"),
            new(BlockKind.Meta, "This certifies that"),
            new(BlockKind.Heading, d.StudentName),
            new(BlockKind.Meta, "has completed"),
            new(BlockKind.Heading, d.ExamTitle),
        };
        if (Join(" · ", d.Subject, d.Topic) is { Length: > 0 } subjectTopic) b.Add(new(BlockKind.Meta, subjectTopic));
        if (d.Organization is { Length: > 0 } org) b.Add(new(BlockKind.Meta, $"at {org}"));
        b.Add(new(BlockKind.Meta, $"Completed: {d.CompletedAt.UtcDateTime.ToString("yyyy-MM-dd", Ci)} · Attempt {d.Attempt}"));
        if (d.FinalPercent is { } f) b.Add(new(BlockKind.Meta, [new Run("Result: ", Bold: true), new Run(f.ToString("0.##", Ci) + "%" + (d.Label is { } l ? $" ({l})" : ""))]));
        b.Add(new(BlockKind.Small, $"Verification code: {stamp.Code} · check it at {stamp.VerifyUrl}"));
        b.Add(new(BlockKind.Mono, "SHA-256 " + stamp.Sha256));
        b.Add(new(BlockKind.Small, $"Issued {stamp.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", Ci)} UTC by Gadfly Exams."));
        b.Add(new(BlockKind.Notice, "Anyone holding this certificate can check it is genuine and unchanged at the address above, or by typing the code there. "
            + "The check never shows anything about the exam itself, only whether this copy matches Gadfly's record."));
        return b;
    }

    private static string Join(string sep, params string?[] parts) => string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
