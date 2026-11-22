using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Gadfly.Tests.Server;

/// <summary>
/// Reads back what Gadfly's hand-written PDF and Word writers put on the page, so tests can check content rather than only
/// the file signature: the PDF's Flate-compressed page streams are inflated and their <c>(...) Tj</c> strings decoded
/// (Windows-1252 is read as Latin-1, which agrees on every letter the tests use); the .docx's <c>w:t</c> runs are joined.
/// </summary>
internal static partial class PdfText
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>The text drawn on each page, in drawing order (words joined as drawn; a wrapped line's trailing space is gone).</summary>
    public static IReadOnlyList<string> Pages(byte[] pdf)
    {
        var raw = Latin1.GetString(pdf);
        var pages = new List<string>();
        foreach (Match m in StreamHeader().Matches(raw))
        {
            var length = int.Parse(m.Groups[1].Value);
            var start = m.Index + m.Length;
            using var z = new ZLibStream(new MemoryStream(pdf, start, length), CompressionMode.Decompress);
            using var reader = new StreamReader(z, Latin1);
            var content = reader.ReadToEnd();
            var sb = new StringBuilder();
            foreach (Match s in ShowText().Matches(content)) sb.Append(Unescape(s.Groups[1].Value));
            pages.Add(sb.ToString());
        }
        return pages;
    }

    /// <summary>Every page's text joined.</summary>
    public static string All(byte[] pdf) => string.Concat(Pages(pdf));

    /// <summary>The document properties dictionary (title, subject), which is not compressed.</summary>
    public static string Info(byte[] pdf)
    {
        var raw = Latin1.GetString(pdf);
        var m = InfoObject().Match(raw);
        return m.Success ? Unescape(m.Value) : "";
    }

    /// <summary>Whitespace removed, so a phrase can be found however the writer wrapped it.</summary>
    public static string Squash(string s) => Whitespace().Replace(s, "");

    /// <summary>The text of every <c>w:t</c> run in one part of a .docx, joined.</summary>
    public static string DocxText(byte[] docx, string part = "word/document.xml")
    {
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var reader = new StreamReader(zip.GetEntry(part)!.Open());
        var xml = XDocument.Parse(reader.ReadToEnd());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return string.Concat(xml.Descendants(w + "t").Select(t => t.Value));
    }

    private static string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            var next = s[i + 1];
            if (next is >= '0' and <= '7')
            {
                var j = i + 1;
                var digits = "";
                while (j < s.Length && digits.Length < 3 && s[j] is >= '0' and <= '7') digits += s[j++];
                sb.Append((char)Convert.ToInt32(digits, 8));
                i = j - 1;
            }
            else
            {
                sb.Append(next);
                i++;
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"<< /Length (\d+) /Filter /FlateDecode >>\nstream\n")]
    private static partial Regex StreamHeader();

    [GeneratedRegex(@"\(((?:\\.|[^\\)])*)\) Tj")]
    private static partial Regex ShowText();

    [GeneratedRegex(@"8 0 obj\n<<.*?>>\nendobj", RegexOptions.Singleline)]
    private static partial Regex InfoObject();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
