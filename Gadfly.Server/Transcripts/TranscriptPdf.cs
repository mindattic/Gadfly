using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Gadfly.Server.Transcripts;

/// <summary>
/// The PDF download (GAD-T295), written directly as PDF 1.4 with no third-party library: A4 pages, the standard
/// Helvetica and Courier fonts (every PDF reader has them, nothing is embedded), text wrapped with Helvetica's own
/// character widths, a footer with the verification code and page number on every page, and the document title and
/// subject in the file's properties.
/// <para>The standard fonts cover Western European text (Windows-1252). Letters outside it lose their accents where that
/// keeps them readable (e.g. Polish ł becomes l) and otherwise print as "?"; the Word, web page, text and JSON downloads
/// keep every character. A future improvement is an embedded Unicode font.</para>
/// </summary>
public static class TranscriptPdf
{
    private const double PageWidth = 595.28, PageHeight = 841.89, Margin = 56, FooterY = 30;
    private const double ContentWidth = PageWidth - 2 * Margin;
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    private static readonly Encoding Cp1252 = Windows1252.Instance;

    private enum Font { Regular, Bold, Italic, BoldItalic, Mono }

    private sealed record Style(double Size, double Leading, double Before, double After, Font Base, double Gray = 0);

    private static Style StyleOf(BlockKind kind) => kind switch
    {
        BlockKind.Title => new(18, 22, 0, 8, Font.Bold),
        BlockKind.Meta => new(10, 13.5, 0, 1, Font.Regular),
        BlockKind.Heading => new(12.5, 16, 14, 3, Font.Bold),
        BlockKind.Subheading => new(9.5, 12.5, 5, 1, Font.Bold, 0.25),
        BlockKind.Speaker => new(9.5, 12.5, 6, 1, Font.Regular, 0.2),
        BlockKind.Quote => new(10.5, 14, 1, 3, Font.Regular),
        BlockKind.Small => new(8.5, 11, 0, 1, Font.Regular, 0.33),
        BlockKind.Notice => new(9, 12, 4, 4, Font.Regular, 0.15),
        BlockKind.Mono => new(8, 10.5, 0, 2, Font.Mono, 0.25),
        _ => new(10, 13.5, 2, 2, Font.Regular),
    };

    private sealed record Piece(string Text, Font Font, bool Mark);
    private sealed record Line(List<Piece> Pieces, double Size, double Gray, double X, bool Shade);

    public static byte[] Render(TranscriptDocument d, TranscriptStamp stamp)
    {
        var pages = new List<List<string>>();   // content-stream operators per page
        var current = new List<string>();
        var y = PageHeight - Margin;
        pages.Add(current);

        void NewPage()
        {
            current = [];
            pages.Add(current);
            y = PageHeight - Margin;
        }

        foreach (var block in TranscriptLayout.Blocks(d, stamp))
        {
            var style = StyleOf(block.Kind);
            var indent = block.Indent * 18.0;
            var notice = block.Kind == BlockKind.Notice;
            var x = Margin + indent + (notice ? 8 : 0);
            var width = ContentWidth - indent - (notice ? 16 : 0) - (block.Kind == BlockKind.Quote ? 10 : 0);
            if (block.Kind == BlockKind.Quote) x += 10;
            var lines = Wrap(block.Runs, style, width);
            var height = style.Before + lines.Count * style.Leading + style.After + (notice ? 6 : 0);
            // Keep a heading with what follows it, and short blocks whole.
            var keep = block.Kind is BlockKind.Heading or BlockKind.Subheading or BlockKind.Speaker ? 4 * style.Leading : 0;
            if (y - Math.Min(height, 3 * style.Leading) - keep < Margin + 12 && y < PageHeight - Margin - 1) NewPage();
            y -= style.Before;

            if (notice)
            {
                var boxTop = y + 2;
                var boxHeight = lines.Count * style.Leading + 6;
                if (boxTop - boxHeight < Margin + 12) { NewPage(); boxTop = y + 2; }
                current.Add($"0.97 0.955 0.92 rg {N(Margin + indent)} {N(boxTop - boxHeight)} {N(ContentWidth - indent)} {N(boxHeight)} re f");
                y -= 3;
            }
            if (block.Kind == BlockKind.Quote && lines.Count > 0)
            {
                var top = y;
                var span = Math.Min(lines.Count * style.Leading, y - Margin - 12);
                current.Add($"0.79 0.77 0.71 rg {N(Margin + indent + 2)} {N(top - span)} 2 {N(span)} re f");
            }

            for (var i = 0; i < lines.Count; i++)
            {
                if (y - style.Leading < Margin + 12)
                {
                    NewPage();
                    if (block.Kind == BlockKind.Quote)
                        current.Add($"0.79 0.77 0.71 rg {N(Margin + indent + 2)} {N(y - Math.Min((lines.Count - i) * style.Leading, y - Margin - 12))} 2 {N(Math.Min((lines.Count - i) * style.Leading, y - Margin - 12))} re f");
                }
                y -= style.Leading;
                DrawLine(current, lines[i], x, y + (style.Leading - style.Size) * 0.35, style);
            }
            y -= style.After + (notice ? 3 : 0);
            if (block.Kind == BlockKind.Title)
            {
                current.Add($"0.75 0.73 0.68 RG 0.8 w {N(Margin)} {N(y + 2)} m {N(PageWidth - Margin)} {N(y + 2)} l S");
                y -= 4;
            }
        }

        // Footer on every page: copy, code and page n of m.
        for (var p = 0; p < pages.Count; p++)
        {
            var footer = $"Gadfly transcript · {TranscriptLayout.CopyLabel(d.Audience)} · verification code {stamp.Code} · page {p + 1} of {pages.Count}";
            var w = Measure(footer, Font.Regular, 7.5);
            pages[p].Add($"0.75 0.73 0.68 RG 0.5 w {N(Margin)} {N(FooterY + 12)} m {N(PageWidth - Margin)} {N(FooterY + 12)} l S");
            pages[p].Add($"BT 0.4 g /F1 7.5 Tf {N((PageWidth - w) / 2)} {N(FooterY)} Td {Str(footer)} Tj ET");
        }

        return Write(pages, $"Transcript: {d.ExamTitle}", $"{d.StudentName}, attempt {d.Attempt} ({TranscriptLayout.CopyLabel(d.Audience)}). Verification code {stamp.Code}.",
            stamp.GeneratedAt);
    }

    private static void DrawLine(List<string> ops, Line line, double x, double y, Style style)
    {
        var cx = x;
        foreach (var p in line.Pieces)
        {
            var w = Measure(p.Text, p.Font, line.Size);
            if (p.Mark)
            {
                ops.Add($"1 0.91 0.64 rg {N(cx - 0.5)} {N(y - line.Size * 0.22)} {N(w + 1)} {N(line.Size * 1.12)} re f");
                ops.Add($"0.55 0.35 0 RG 0.4 w {N(cx)} {N(y - line.Size * 0.18)} m {N(cx + w)} {N(y - line.Size * 0.18)} l S");
            }
            ops.Add($"BT {N(line.Gray)} g /{FontName(p.Font)} {N(line.Size)} Tf {N(cx)} {N(y)} Td {Str(p.Text)} Tj ET");
            cx += w;
        }
    }

    // ------------------------------------------------------------------ wrapping

    private static List<Line> Wrap(IReadOnlyList<Run> runs, Style style, double width)
    {
        // Split every run into words (keeping the spaces with the word before them), each with its font.
        var tokens = new List<Piece>();
        foreach (var r in runs)
        {
            var font = r.Mono ? Font.Mono : (r.Bold || style.Base == Font.Bold, r.Italic) switch
            {
                (true, true) => Font.BoldItalic,
                (true, false) => Font.Bold,
                (false, true) => Font.Italic,
                _ => style.Base,
            };
            var text = Pdfable(r.Mark ? "[" + r.Text + "?]" : r.Text).Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
            var start = 0;
            for (var i = 0; i <= text.Length; i++)
            {
                if (i < text.Length && text[i] != ' ') continue;
                var end = Math.Min(text.Length, i + 1);   // include the space
                if (end > start) tokens.Add(new Piece(text[start..end], font, r.Mark));
                start = end;
            }
        }

        var lines = new List<Line>();
        var pieces = new List<Piece>();
        var used = 0.0;
        void Flush()
        {
            if (pieces.Count > 0 && pieces[^1].Text.EndsWith(' ')) pieces[^1] = pieces[^1] with { Text = pieces[^1].Text.TrimEnd() };
            lines.Add(new Line([.. pieces], style.Size, style.Gray, 0, false));
            pieces.Clear();
            used = 0;
        }
        foreach (var tok in tokens)
        {
            var w = Measure(tok.Text.TrimEnd(), tok.Font, style.Size);
            if (used > 0 && used + w > width * 0.985) Flush();
            // A single word wider than the line (a hash, a long URL): break it by characters.
            var text = tok.Text;
            while (Measure(text.TrimEnd(), tok.Font, style.Size) > width * 0.985 && text.Length > 1)
            {
                var n = text.Length - 1;
                while (n > 1 && Measure(text[..n], tok.Font, style.Size) > width * 0.985 - used) n--;
                pieces.Add(tok with { Text = text[..n] });
                Flush();
                text = text[n..];
            }
            pieces.Add(tok with { Text = text });
            used += Measure(text, tok.Font, style.Size);
        }
        if (pieces.Count > 0 || lines.Count == 0) Flush();
        return lines;
    }

    private static double Measure(string text, Font font, double size)
    {
        var total = 0.0;
        foreach (var b in Cp1252.GetBytes(text)) total += Width(b, font);
        return total * size / 1000.0;
    }

    // Helvetica and Helvetica-Bold advance widths (Adobe AFM) for 32..126; the oblique faces share them. Courier is 600.
    private static readonly short[] Regular =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
        278, 278, 584, 584, 584, 556, 1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667,
        611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, 333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833,
        556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];
    private static readonly short[] Bold =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
        333, 333, 584, 584, 584, 611, 975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778, 667, 778, 722, 667,
        611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556, 333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889,
        611, 611, 611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    private static double Width(byte b, Font font)
    {
        if (font == Font.Mono) return 600;
        var bold = font is Font.Bold or Font.BoldItalic;
        if (b is >= 32 and <= 126) return (bold ? Bold : Regular)[b - 32];
        return b switch
        {
            0x85 => 1000, 0x96 => 556, 0x97 => 1000, 0x95 => 350, 0x91 or 0x92 => bold ? 278 : 222, 0x93 or 0x94 => bold ? 500 : 333,
            0xA0 => 278, 0xB7 => 278, 0xA9 or 0xAE => 737, 0xB0 => 400, 0xD7 => 584,
            >= 0xC0 and <= 0xDE => 722, >= 0xDF => bold ? 611 : 556,
            _ => 556,
        };
    }

    /// <summary>Text the standard fonts can show: Windows-1252, accents dropped where that keeps a letter readable, else "?".</summary>
    internal static string Pdfable(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch < 0x80 || Fits(ch)) { sb.Append(ch); continue; }
            var replaced = ch switch
            {
                'Ł' => "L", 'ł' => "l", 'Đ' => "D", 'đ' => "d", '−' => "-", '‐' or '‑' => "-",
                '→' => "->", '←' => "<-", '≥' => ">=", '≤' => "<=", '×' => "x", '′' => "'", '″' => "\"",
                _ => null,
            };
            if (replaced is null)
            {
                var stripped = new string(ch.ToString().Normalize(NormalizationForm.FormD)
                    .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
                replaced = stripped.Length > 0 && stripped.All(c => c < 0x80 || Fits(c)) ? stripped : "?";
            }
            sb.Append(replaced);
        }
        return sb.ToString();

        static bool Fits(char c) => Windows1252.Map(c) is not null;
    }

    // ------------------------------------------------------------------ the file

    private static string FontName(Font f) => f switch
    {
        Font.Bold => "F2", Font.Italic => "F3", Font.BoldItalic => "F4", Font.Mono => "F5", _ => "F1",
    };

    private static string N(double v) => Math.Round(v, 2).ToString("0.##", Ci);

    /// <summary>A PDF string literal in Windows-1252, with (, ) and \ escaped and every other byte outside printable ASCII as an octal escape.</summary>
    private static string Str(string text)
    {
        var sb = new StringBuilder("(");
        foreach (var b in Cp1252.GetBytes(Pdfable(text)))
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\') sb.Append('\\').Append((char)b);
            else if (b is < 32 or > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append((char)b);
        }
        return sb.Append(')').ToString();
    }

    private static byte[] Write(List<List<string>> pages, string title, string subject, DateTimeOffset created)
    {
        using var ms = new MemoryStream();
        var offsets = new List<long> { 0 };
        void Raw(string s) { var bytes = Encoding.ASCII.GetBytes(s); ms.Write(bytes); }
        void Obj(int id, string body) { offsets.Add(ms.Position); Raw($"{id} 0 obj\n{body}\nendobj\n"); }

        ms.Write("%PDF-1.4\n%âãÏÓ\n"u8);
        // Object ids: 1 catalog, 2 pages, 3..7 fonts, 8 info, then a page and its content per page.
        const int firstPage = 9;
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{firstPage + i * 2} 0 R"));
        Obj(1, "<< /Type /Catalog /Pages 2 0 R /ViewerPreferences << /DisplayDocTitle true >> >>");
        Obj(2, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");
        string[] fonts = ["Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique", "Courier"];
        for (var f = 0; f < fonts.Length; f++)
            Obj(3 + f, $"<< /Type /Font /Subtype /Type1 /BaseFont /{fonts[f]} /Encoding /WinAnsiEncoding >>");
        var date = "D:" + created.UtcDateTime.ToString("yyyyMMddHHmmss", Ci) + "Z";
        Obj(8, $"<< /Title {Str(title)} /Subject {Str(subject)} /Creator (Gadfly) /Producer (Gadfly transcript maker) /CreationDate ({date}) >>");

        for (var p = 0; p < pages.Count; p++)
        {
            var pageId = firstPage + p * 2;
            var contentId = pageId + 1;
            Obj(pageId, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(PageWidth)} {N(PageHeight)}] " +
                "/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R /F4 6 0 R /F5 7 0 R >> >> " +
                $"/Contents {contentId} 0 R >>");
            var content = Encoding.ASCII.GetBytes(string.Join('\n', pages[p]));
            byte[] packed;
            using (var zipped = new MemoryStream())
            {
                using (var z = new ZLibStream(zipped, CompressionLevel.Optimal, leaveOpen: true)) z.Write(content);
                packed = zipped.ToArray();
            }
            offsets.Add(ms.Position);
            Raw($"{contentId} 0 obj\n<< /Length {packed.Length} /Filter /FlateDecode >>\nstream\n");
            ms.Write(packed);
            Raw("\nendstream\nendobj\n");
        }

        var xref = ms.Position;
        var count = offsets.Count;
        var sb = new StringBuilder($"xref\n0 {count}\n0000000000 65535 f \n");
        for (var i = 1; i < count; i++) sb.Append(offsets[i].ToString("D10", Ci)).Append(" 00000 n \n");
        sb.Append($"trailer\n<< /Size {count} /Root 1 0 R /Info 8 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Raw(sb.ToString());
        return ms.ToArray();
    }
}

/// <summary>Windows-1252 without the code-pages provider: Latin-1 plus the 0x80-0x9F punctuation block.</summary>
internal sealed class Windows1252 : Encoding
{
    public static readonly Windows1252 Instance = new();

    private static readonly Dictionary<char, byte> High = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87,
        ['ˆ'] = 0x88, ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91,
        ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98,
        ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
    };

    public static byte? Map(char c) => c < 0x80 || c is >= ' ' and <= 'ÿ' ? (byte)c : High.TryGetValue(c, out var b) ? b : null;

    public override int GetByteCount(char[] chars, int index, int count) => count;

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        for (var i = 0; i < charCount; i++) bytes[byteIndex + i] = Map(chars[charIndex + i]) ?? (byte)'?';
        return charCount;
    }

    public override int GetCharCount(byte[] bytes, int index, int count) => count;

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        for (var i = 0; i < byteCount; i++)
        {
            var b = bytes[byteIndex + i];
            chars[charIndex + i] = b is >= 0x80 and <= 0x9F ? High.FirstOrDefault(kv => kv.Value == b).Key is var k && k != default ? k : '?' : (char)b;
        }
        return byteCount;
    }

    public override int GetMaxByteCount(int charCount) => charCount;
    public override int GetMaxCharCount(int byteCount) => byteCount;
}
