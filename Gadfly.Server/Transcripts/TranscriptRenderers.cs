using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Gadfly.Server.Transcripts;

/// <summary>The download formats of a transcript (GAD-T295).</summary>
public enum TranscriptFormat { Html, Pdf, Docx, Txt, Vtt, Srt, Json }

public static class TranscriptFormats
{
    public static TranscriptFormat? Parse(string? value) => value?.Trim().TrimStart('.').ToLowerInvariant() switch
    {
        "html" or "htm" or "print" => TranscriptFormat.Html,
        "pdf" => TranscriptFormat.Pdf,
        "docx" or "word" => TranscriptFormat.Docx,
        "txt" or "text" => TranscriptFormat.Txt,
        "vtt" or "webvtt" => TranscriptFormat.Vtt,
        "srt" => TranscriptFormat.Srt,
        "json" => TranscriptFormat.Json,
        _ => null,
    };

    public static string Extension(TranscriptFormat f) => f.ToString().ToLowerInvariant();

    public static string ContentType(TranscriptFormat f) => f switch
    {
        TranscriptFormat.Html => "text/html; charset=utf-8",
        TranscriptFormat.Pdf => "application/pdf",
        TranscriptFormat.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        TranscriptFormat.Txt => "text/plain; charset=utf-8",
        TranscriptFormat.Vtt => "text/vtt; charset=utf-8",
        TranscriptFormat.Srt => "application/x-subrip; charset=utf-8",
        _ => "application/json; charset=utf-8",
    };

    public static string Label(TranscriptFormat f) => f switch
    {
        TranscriptFormat.Html => "Print-ready page",
        TranscriptFormat.Pdf => "PDF",
        TranscriptFormat.Docx => "Word",
        TranscriptFormat.Txt => "Plain text",
        TranscriptFormat.Vtt => "WebVTT captions",
        TranscriptFormat.Srt => "SRT captions",
        _ => "JSON",
    };

    public static readonly IReadOnlyList<TranscriptFormat> All =
        [TranscriptFormat.Pdf, TranscriptFormat.Docx, TranscriptFormat.Html, TranscriptFormat.Txt, TranscriptFormat.Vtt, TranscriptFormat.Srt, TranscriptFormat.Json];

    /// <summary>The download link for a session's transcript; <paramref name="copy"/> asks for a lesser copy than the caller's own.</summary>
    public static string Path(Guid sessionId, TranscriptFormat format, TranscriptAudience? copy = null) =>
        $"/transcripts/{sessionId:D}/{Extension(format)}" + (copy is { } c ? "?copy=" + TranscriptAudiences.Key(c) : "");

    /// <summary>A file-name-safe stem: student, exam, attempt and copy.</summary>
    public static string FileStem(TranscriptDocument d)
    {
        var raw = $"transcript-{d.StudentName}-{d.ExamTitle}-attempt{d.Attempt}-{d.Audience}";
        var clean = new string(raw.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        while (clean.Contains("--")) clean = clean.Replace("--", "-");
        return clean.Length == 0 ? "transcript-" + d.SessionId.ToString("N") : clean[..Math.Min(clean.Length, 120)];
    }

    /// <summary>Renders a transcript in one format.</summary>
    public static byte[] Render(TranscriptFormat format, TranscriptDocument d, TranscriptStamp stamp) => format switch
    {
        TranscriptFormat.Html => Encoding.UTF8.GetBytes(TranscriptHtml.Render(d, stamp)),
        TranscriptFormat.Pdf => TranscriptPdf.Render(d, stamp),
        TranscriptFormat.Docx => TranscriptDocx.Render(d, stamp),
        TranscriptFormat.Txt => Utf8(TranscriptText.Render(d, stamp)),
        TranscriptFormat.Vtt => Encoding.UTF8.GetBytes(TranscriptCaptions.Vtt(d, stamp)),
        TranscriptFormat.Srt => Utf8(TranscriptCaptions.Srt(d)),
        _ => Encoding.UTF8.GetBytes(TranscriptJson.Render(d, stamp)),
    };

    private static byte[] Utf8(string text) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];
}

/// <summary>The JSON download: the transcript (exactly what the hash covers) and its verification stamp beside it.</summary>
public static class TranscriptJson
{
    private static readonly JsonSerializerOptions Indented = new(TranscriptHash.Json) { WriteIndented = true };

    public static string Render(TranscriptDocument d, TranscriptStamp stamp) => JsonSerializer.Serialize(new
    {
        transcript = d,
        verification = new
        {
            stamp.Sha256,
            stamp.Code,
            stamp.GeneratedAt,
            stamp.VerifyUrl,
            method = "SHA-256 of the canonical JSON of \"transcript\" (keys in ordinal order, nulls left out, no whitespace, UTF-8). "
                + "The code is the first 80 bits of the hash in Crockford base 32.",
        },
    }, Indented);
}

/// <summary>Plain text, wrapped at 100 columns.</summary>
public static class TranscriptText
{
    private const int Width = 100;

    public static string Render(TranscriptDocument d, TranscriptStamp stamp)
    {
        var sb = new StringBuilder();
        foreach (var block in TranscriptLayout.Blocks(d, stamp))
        {
            var text = Plain(block.Runs);
            var pad = new string(' ', block.Indent * 4);
            switch (block.Kind)
            {
                case BlockKind.Title:
                    sb.AppendLine(text.ToUpperInvariant()).AppendLine(new string('=', Math.Min(Width, text.Length)));
                    break;
                case BlockKind.Heading:
                    sb.AppendLine().AppendLine(text).AppendLine(new string('-', Math.Min(Width, text.Length)));
                    break;
                case BlockKind.Notice:
                    foreach (var line in Wrap(text, Width - pad.Length - 2)) sb.Append(pad).Append("| ").AppendLine(line);
                    break;
                case BlockKind.Mono:
                    sb.Append(pad).AppendLine(text);
                    break;
                default:
                    foreach (var line in Wrap(text, Width - pad.Length)) sb.Append(pad).AppendLine(line);
                    break;
            }
            if (block.Kind is BlockKind.Quote or BlockKind.Notice) sb.AppendLine();
        }
        return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    internal static string Plain(IReadOnlyList<Run> runs) => string.Concat(runs.Select(r => r.Mark ? "[" + r.Text + "?]" : r.Text));

    internal static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            yield return line.ToString();
        }
    }
}

/// <summary>The print-ready web page: one self-contained HTML file with a print stylesheet (no scripts, no external files).</summary>
public static class TranscriptHtml
{
    /// <summary>The page's own Content-Security-Policy: no scripts, no outside requests.</summary>
    public const string ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public static string Render(TranscriptDocument d, TranscriptStamp stamp)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />")
          .Append("<meta name=\"robots\" content=\"noindex\" />")
          .Append("<title>").Append(E($"Transcript · {d.ExamTitle} · {d.StudentName}")).Append("</title><style>").Append(Css).Append("</style></head><body><main>");
        var inNotice = false;
        foreach (var block in TranscriptLayout.Blocks(d, stamp))
        {
            var runs = Runs(block.Runs);
            var indent = block.Indent > 0 ? $" style=\"margin-left:{block.Indent * 1.5:0.#}rem\"" : "";
            if (inNotice && block.Kind != BlockKind.Notice) inNotice = false;
            sb.Append(block.Kind switch
            {
                BlockKind.Title => $"<h1>{runs}</h1>",
                BlockKind.Meta => $"<p class=\"meta\">{runs}</p>",
                BlockKind.Heading => $"<h2>{runs}</h2>",
                BlockKind.Subheading => $"<h3{indent}>{runs}</h3>",
                BlockKind.Speaker => $"<p class=\"speaker\"{indent}>{runs}</p>",
                BlockKind.Quote => $"<blockquote{indent}>{runs}</blockquote>",
                BlockKind.Small => $"<p class=\"small\"{indent}>{runs}</p>",
                BlockKind.Notice => $"<p class=\"notice\"{indent}>{runs}</p>",
                BlockKind.Mono => $"<p class=\"mono\">{runs}</p>",
                _ => $"<p{indent}>{runs}</p>",
            });
        }
        sb.Append("</main><footer>Gadfly transcript · ").Append(E(TranscriptLayout.CopyLabel(d.Audience))).Append(" · verification code ")
          .Append(E(stamp.Code)).Append("</footer></body></html>");
        return sb.ToString();
    }

    private static string Runs(IReadOnlyList<Run> runs) => string.Concat(runs.Select(r =>
    {
        var t = E(r.Mark ? "[" + r.Text + "?]" : r.Text);
        if (r.Mono) t = $"<code>{t}</code>";
        if (r.Bold) t = $"<strong>{t}</strong>";
        if (r.Italic) t = $"<em>{t}</em>";
        if (r.Mark) t = $"<mark title=\"The speech recognizer was unsure of these words\">{t}</mark>";
        return t;
    }));

    internal static string E(string s) => WebUtility.HtmlEncode(s);

    internal const string Css = """
        :root{color-scheme:light}
        body{margin:0;background:#f4f3ef;color:#1d1d1f;font:15px/1.5 Georgia,"Times New Roman",serif}
        main{max-width:780px;margin:24px auto;background:#fff;padding:40px 48px;box-shadow:0 1px 4px rgba(0,0,0,.12)}
        h1{font:600 26px/1.2 system-ui,-apple-system,"Segoe UI",sans-serif;margin:0 0 12px}
        h2{font:600 17px/1.3 system-ui,-apple-system,"Segoe UI",sans-serif;margin:28px 0 6px;padding-top:12px;border-top:1px solid #d8d6cf}
        h3{font:600 14px/1.3 system-ui,-apple-system,"Segoe UI",sans-serif;margin:10px 0 4px;color:#444}
        .meta{margin:2px 0;font-family:system-ui,-apple-system,"Segoe UI",sans-serif;font-size:14px}
        .speaker{margin:10px 0 2px;font-family:system-ui,-apple-system,"Segoe UI",sans-serif;font-size:13px;color:#333}
        blockquote{margin:2px 0 4px 1.5rem;padding:0 0 0 12px;border-left:3px solid #c9c4b5;white-space:pre-wrap}
        .small{margin:2px 0;font:12.5px/1.45 system-ui,-apple-system,"Segoe UI",sans-serif;color:#555}
        .notice{margin:8px 0;padding:8px 12px;background:#f7f4ea;border:1px solid #e3dcc5;font:13px/1.45 system-ui,-apple-system,"Segoe UI",sans-serif}
        .mono,code{font-family:ui-monospace,Consolas,"Courier New",monospace;font-size:12px;word-break:break-all}
        mark{background:#ffe8a3;color:inherit;padding:0 1px}
        footer{max-width:780px;margin:0 auto 24px;font:12px system-ui,-apple-system,"Segoe UI",sans-serif;color:#666;text-align:center}
        @page{size:auto;margin:18mm 16mm 20mm}
        @media print{
          body{background:#fff;font-size:11pt}
          main{box-shadow:none;margin:0;max-width:none;padding:0}
          h2{break-after:avoid}
          .speaker,h3{break-after:avoid}
          blockquote,.notice{break-inside:avoid}
          footer{position:fixed;bottom:0;left:0;right:0;margin:0;font-size:8pt}
          mark{background:none;text-decoration:underline dotted}
        }
        """;
}

/// <summary>
/// Captions (WebVTT and SRT). Each examiner question is a cue from when it was asked; each answer is cut into cues from the
/// stored word timings when the capture kept them, else spread evenly over the answer's start and end times (or the turn's
/// times for older records). Read-backs, corrections and clarifications appear as cues at their own times.
/// </summary>
public static class TranscriptCaptions
{
    public sealed record Cue(double Start, double End, string Speaker, string Text);

    private const int MaxWordsPerCue = 12;
    private const double MaxCueSeconds = 6, MinCueSeconds = 1.2, WordsPerSecondSpoken = 2.6;

    public static string Vtt(TranscriptDocument d, TranscriptStamp stamp)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        sb.Append("NOTE\n").Append(Clean($"Gadfly transcript captions · {d.ExamTitle} · {d.StudentName} · attempt {d.Attempt} · {TranscriptLayout.CopyLabel(d.Audience)}"))
          .Append('\n').Append(Clean($"Verification code {stamp.Code} · {stamp.VerifyUrl}")).Append("\n\n");
        var n = 0;
        foreach (var c in Cues(d))
            sb.Append(++n).Append('\n').Append(Ts(c.Start, '.')).Append(" --> ").Append(Ts(c.End, '.')).Append('\n')
              .Append("<v ").Append(c.Speaker).Append('>').Append(VttEscape(c.Text)).Append("\n\n");
        return sb.ToString();
    }

    public static string Srt(TranscriptDocument d)
    {
        var sb = new StringBuilder();
        var n = 0;
        foreach (var c in Cues(d))
            sb.Append(++n).Append("\r\n").Append(Ts(c.Start, ',')).Append(" --> ").Append(Ts(c.End, ',')).Append("\r\n")
              .Append(c.Speaker).Append(": ").Append(Clean(c.Text)).Append("\r\n\r\n");
        return sb.ToString();
    }

    /// <summary>Every cue in time order, no two overlapping, each at least <see cref="MinCueSeconds"/> long where room allows.</summary>
    public static IReadOnlyList<Cue> Cues(TranscriptDocument d)
    {
        var cues = new List<Cue>();
        var origin = d.StartedAt ?? (d.Turns.Count > 0 ? d.Turns[0].AskedAt : (DateTimeOffset?)null);
        double Off(DateTimeOffset at) => origin is { } o ? Math.Max(0, (at - o).TotalSeconds) : 0;

        foreach (var t in d.Turns)
        {
            var first = t.Responses.FirstOrDefault();
            var answerStart = first?.StartOffsetSeconds ?? first?.EndOffsetSeconds;
            var said = Estimate(t.Question);
            var qEnd = answerStart is { } a && a > t.OffsetSeconds ? Math.Min(t.OffsetSeconds + said, a) : t.OffsetSeconds + said;
            cues.Add(new Cue(t.OffsetSeconds, qEnd, "Examiner", t.Question));

            foreach (var r in t.Responses)
            {
                if (r.Words.Count > 0 && r.Verified is null) cues.AddRange(FromWords(r));
                else cues.AddRange(Spread(r.Verified ?? r.AsRecorded, r.StartOffsetSeconds ?? Math.Max(r.EndOffsetSeconds - Estimate(r.Verified ?? r.AsRecorded), t.OffsetSeconds), r.EndOffsetSeconds, "Student"));

                if (r.ReadBack is { } rb)
                {
                    var at = Off(rb.AskedAt);
                    cues.Add(new Cue(at, at + Estimate(rb.Prompt), "Examiner", rb.Prompt));
                    if (rb.AnsweredAt is { } ans) cues.Add(new Cue(Off(ans), Off(ans) + 2, "Student", "[" + rb.Outcome.TrimEnd('.') + "]"));
                }
                foreach (var c in r.Corrections)
                    cues.Add(new Cue(Off(c.At), Off(c.At) + Math.Max(3, Estimate(c.Corrected)), "Student", $"[Correction] {c.Corrected}"));
                foreach (var c in r.Clarifications)
                {
                    cues.Add(new Cue(Off(c.AskedAt), Off(c.AskedAt) + Estimate(c.Question), "Examiner", c.Question));
                    if (c.Reply is { Length: > 0 } reply && c.AnsweredAt is { } at2)
                        cues.AddRange(Spread(reply, Off(at2) - Estimate(reply) is var s && s > Off(c.AskedAt) ? s : Off(c.AskedAt) + 1, Off(at2), "Student"));
                }
            }
        }

        // Time order, then trim each cue so it ends before the next begins.
        var ordered = cues.Where(c => !string.IsNullOrWhiteSpace(c.Text)).OrderBy(c => c.Start).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var c = ordered[i];
            var end = Math.Max(c.End, c.Start + MinCueSeconds);
            if (i + 1 < ordered.Count && end > ordered[i + 1].Start) end = Math.Max(c.Start + 0.05, ordered[i + 1].Start);
            ordered[i] = c with { End = end };
        }
        return ordered;
    }

    private static IEnumerable<Cue> FromWords(TranscriptResponse r)
    {
        // Word times are relative to the start of the answer.
        var start = r.StartOffsetSeconds ?? Math.Max(0, r.EndOffsetSeconds - r.Words[^1].EndSeconds);
        var group = new List<TranscriptWord>();
        for (var i = 0; i < r.Words.Count; i++)
        {
            var w = r.Words[i];
            if (group.Count > 0 && (group.Count >= MaxWordsPerCue || w.EndSeconds - group[0].StartSeconds > MaxCueSeconds || w.StartSeconds - group[^1].EndSeconds > 1.0))
            {
                yield return Group(group, start);
                group.Clear();
            }
            group.Add(w);
        }
        if (group.Count > 0) yield return Group(group, start);

        static Cue Group(List<TranscriptWord> g, double start) =>
            new(start + g[0].StartSeconds, start + g[^1].EndSeconds, "Student", string.Join(' ', g.Select(w => w.Text)));
    }

    private static IEnumerable<Cue> Spread(string text, double start, double end, string speaker)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) yield break;
        if (end <= start) end = start + Estimate(text);
        var chunks = (int)Math.Ceiling(words.Length / (double)MaxWordsPerCue);
        var per = (end - start) / words.Length;
        for (var c = 0; c < chunks; c++)
        {
            var from = c * MaxWordsPerCue;
            var count = Math.Min(MaxWordsPerCue, words.Length - from);
            yield return new Cue(start + from * per, start + (from + count) * per, speaker, string.Join(' ', words, from, count));
        }
    }

    private static double Estimate(string text) => Math.Clamp(text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length / WordsPerSecondSpoken, MinCueSeconds, 30);

    public static string Ts(double seconds, char fraction)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, Math.Round(seconds * 1000)));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{fraction}{t.Milliseconds:000}";
    }

    private static string Clean(string s) => s.Replace("\r", " ").Replace("\n", " ").Replace("-->", "->");
    private static string VttEscape(string s) => Clean(s).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
