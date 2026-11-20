using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Gadfly.Server.Review;

/// <summary>
/// Renders the subset of Markdown the session report uses (headings, paragraphs, bullet lists, pipe
/// tables, blockquotes, **bold**, *italic*, `code`) as HTML. Every piece of text is HTML-encoded
/// first, so student answers quoted in the report can't inject markup.
/// </summary>
public static partial class SimpleMarkdown
{
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var para = new List<string>();
        void FlushPara()
        {
            if (para.Count == 0) return;
            sb.Append("<p>").Append(string.Join("<br />", para.Select(Inline))).Append("</p>");
            para.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.Length == 0) { FlushPara(); continue; }
            if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal)) { FlushPara(); continue; }

            var h = Heading().Match(line);
            if (h.Success)
            {
                FlushPara();
                var level = Math.Min(6, h.Groups[1].Length + 1);   // the page owns h1
                sb.Append($"<h{level}>").Append(Inline(h.Groups[2].Value)).Append($"</h{level}>");
                continue;
            }
            if (line.StartsWith('|'))
            {
                FlushPara();
                var rows = new List<string>();
                for (; i < lines.Length && lines[i].TrimEnd().StartsWith('|'); i++) rows.Add(lines[i].TrimEnd());
                i--;
                sb.Append("<table class=\"grid\">");
                foreach (var row in rows)
                {
                    var cells = row.Trim('|').Split('|').Select(c => c.Trim()).ToList();
                    if (cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':'))) continue;
                    if (cells.All(c => c.Length == 0)) continue;
                    sb.Append("<tr>");
                    foreach (var c in cells) sb.Append("<td>").Append(Inline(c)).Append("</td>");
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
                continue;
            }
            if (Bullet().IsMatch(line))
            {
                FlushPara();
                sb.Append("<ul>");
                for (; i < lines.Length && Bullet().IsMatch(lines[i]); i++)
                    sb.Append("<li>").Append(Inline(Bullet().Replace(lines[i].TrimEnd(), "", 1))).Append("</li>");
                i--;
                sb.Append("</ul>");
                continue;
            }
            if (line.StartsWith('>'))
            {
                FlushPara();
                sb.Append("<blockquote>").Append(Inline(line.TrimStart('>', ' '))).Append("</blockquote>");
                continue;
            }
            para.Add(line);
        }
        FlushPara();
        return sb.ToString();
    }

    private static string Inline(string text)
    {
        var s = WebUtility.HtmlEncode(text);
        s = Code().Replace(s, "<code>$1</code>");
        s = Bold().Replace(s, "<strong>$1</strong>");
        s = Italic().Replace(s, "<em>$1</em>");
        return s;
    }

    [GeneratedRegex(@"^(#{1,5})\s+(.*)$")] private static partial Regex Heading();
    [GeneratedRegex(@"^\s*[-*]\s+")] private static partial Regex Bullet();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex Code();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Bold();
    [GeneratedRegex(@"(?<![\*\w])\*(?!\s)(.+?)(?<!\s)\*(?![\*\w])")] private static partial Regex Italic();
}
