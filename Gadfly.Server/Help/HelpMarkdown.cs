using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Gadfly.Server.Help;

/// <summary>One heading of a rendered help topic: its level (1 to 6), its text and the anchor id it carries.</summary>
public sealed record HelpHeading(int Level, string Text, string Anchor);

/// <summary>One searchable stretch of a topic: the heading it sits under (empty before the first) and its plain text.</summary>
public sealed record HelpSection(string Heading, string Anchor, string Text);

/// <summary>A help topic after rendering: the page title, the HTML body, its headings and its plain-text sections.</summary>
public sealed record HelpDocument(string Title, string Html, IReadOnlyList<HelpHeading> Headings, IReadOnlyList<HelpSection> Sections);

/// <summary>How links and images inside a guide are turned into addresses inside the app.</summary>
public interface IHelpLinkResolver
{
    /// <summary>The in-app address for a link target as written in the Markdown, or null to show the text without a link.</summary>
    string? ResolveLink(string target);
    /// <summary>The in-app address of an image as written in the Markdown, or null to show only its description.</summary>
    string? ResolveImage(string target);
    /// <summary>The address a same-page anchor is appended to (the app sets <c>&lt;base href="/"&gt;</c>, so "#x" alone would leave the page).</summary>
    string PagePath { get; }
}

/// <summary>
/// Renders the Markdown the guides in docs/guides are written in (GAD-T292): headings with anchors, paragraphs,
/// nested bullet and numbered lists, check-list items, pipe tables with a header row, fenced and inline code,
/// block quotes, rules, links, images, bold and italic. Every piece of text is HTML-encoded before any markup is
/// added, and raw HTML in a guide is shown as text, so a guide can never inject markup into the page.
/// The session report keeps its own smaller renderer (<see cref="Review.SimpleMarkdown"/>).
/// </summary>
public static partial class HelpMarkdown
{
    public static HelpDocument Render(string? markdown, IHelpLinkResolver? resolver = null)
    {
        var r = new Renderer(resolver ?? PlainResolver.Instance);
        return r.Run(markdown ?? "");
    }

    /// <summary>The anchor id a heading gets: lower-case letters, digits and hyphens (the same shape GitHub uses).</summary>
    public static string Slug(string headingText)
    {
        var sb = new StringBuilder();
        foreach (var ch in StripInline(headingText).Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('-');
        }
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "section" : s;
    }

    /// <summary>The words of a line with its Markdown marks removed (for the table of contents and the search index).</summary>
    public static string StripInline(string text)
    {
        var s = Image().Replace(text, "$1");
        s = Link().Replace(s, "$1");
        s = s.Replace("**", "").Replace("`", "");
        s = Italic().Replace(s, "$1");
        return s;
    }

    private sealed class PlainResolver : IHelpLinkResolver
    {
        public static readonly PlainResolver Instance = new();
        public string? ResolveLink(string target) =>
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? target : null;
        public string? ResolveImage(string target) => null;
        public string PagePath => "";
    }

    private sealed class Renderer(IHelpLinkResolver resolver)
    {
        private readonly StringBuilder _html = new();
        private readonly List<HelpHeading> _headings = [];
        private readonly List<HelpSection> _sections = [];
        private readonly Dictionary<string, int> _anchors = new(StringComparer.Ordinal);
        private readonly StringBuilder _sectionText = new();
        private string _sectionHeading = "", _sectionAnchor = "";
        private string? _title;

        public HelpDocument Run(string markdown)
        {
            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            Blocks(lines, _html, topLevel: true);
            CloseSection();
            return new HelpDocument(_title ?? "", _html.ToString(), _headings, _sections);
        }

        private void CloseSection()
        {
            var text = Whitespace().Replace(_sectionText.ToString(), " ").Trim();
            if (text.Length > 0 || _sectionHeading.Length > 0) _sections.Add(new HelpSection(_sectionHeading, _sectionAnchor, text));
            _sectionText.Clear();
        }

        private void Say(string text) => _sectionText.Append(StripInline(text)).Append(' ');

        private string Anchor(string text)
        {
            var slug = Slug(text);
            if (_anchors.TryGetValue(slug, out var n)) { _anchors[slug] = n + 1; return $"{slug}-{n}"; }
            _anchors[slug] = 1;
            return slug;
        }

        /// <summary>Renders a run of lines as blocks. Nested calls (list items, quotes) never open sections or headings' anchors.</summary>
        private void Blocks(IReadOnlyList<string> lines, StringBuilder sb, bool topLevel)
        {
            var para = new List<string>();
            void FlushPara()
            {
                if (para.Count == 0) return;
                var text = string.Join(" ", para.Select(p => p.Trim()));
                sb.Append("<p>").Append(Inline(text)).Append("</p>\n");
                Say(text);
                para.Clear();
            }

            for (var i = 0; i < lines.Count; i++)
            {
                var raw = lines[i].TrimEnd();
                var line = raw.TrimStart();
                if (line.Length == 0) { FlushPara(); continue; }

                // Fenced code: everything up to the closing fence, exactly as written.
                if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
                {
                    FlushPara();
                    var fence = line[..3];
                    var indent = raw.Length - line.Length;
                    var code = new StringBuilder();
                    for (i++; i < lines.Count && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal); i++)
                    {
                        var l = lines[i];
                        code.Append(l.Length >= indent && string.IsNullOrWhiteSpace(l[..indent]) ? l[indent..] : l.TrimStart()).Append('\n');
                    }
                    sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(code.ToString().TrimEnd('\n'))).Append("</code></pre>\n");
                    _sectionText.Append(code).Append(' ');
                    continue;
                }
                if (line.StartsWith("<!--", StringComparison.Ordinal))
                {
                    FlushPara();
                    for (; i < lines.Count && !lines[i].Contains("-->", StringComparison.Ordinal); i++) { }
                    continue;
                }

                var h = Heading().Match(line);
                if (h.Success)
                {
                    FlushPara();
                    var level = h.Groups[1].Length;
                    var text = h.Groups[2].Value.Trim().TrimEnd('#').Trim();
                    if (!topLevel) { sb.Append("<p><strong>").Append(Inline(text)).Append("</strong></p>\n"); Say(text); continue; }
                    var plain = StripInline(text);
                    if (level == 1 && _title is null)
                    {
                        // The page shows the title itself; the body starts under it.
                        _title = plain;
                        continue;
                    }
                    CloseSection();
                    var anchor = Anchor(text);
                    var shown = Math.Clamp(level, 2, 6);
                    _headings.Add(new HelpHeading(shown, plain, anchor));
                    _sectionHeading = plain; _sectionAnchor = anchor;
                    sb.Append($"<h{shown} id=\"{anchor}\">").Append(Inline(text))
                      .Append($" <a class=\"help-anchor\" href=\"{Attr(resolver.PagePath + "#" + anchor)}\" aria-label=\"Link to this section\">#</a></h{shown}>\n");
                    continue;
                }
                if (Rule().IsMatch(line)) { FlushPara(); sb.Append("<hr />\n"); continue; }

                if (line.StartsWith('|') && i + 1 < lines.Count && TableRule().IsMatch(lines[i + 1].Trim()))
                {
                    FlushPara();
                    var rows = new List<string>();
                    for (; i < lines.Count && lines[i].TrimStart().StartsWith('|'); i++) rows.Add(lines[i].Trim());
                    i--;
                    Table(rows, sb);
                    continue;
                }

                if (line.StartsWith('>'))
                {
                    FlushPara();
                    var inner = new List<string>();
                    for (; i < lines.Count && lines[i].TrimStart().StartsWith('>'); i++)
                    {
                        var q = lines[i].TrimStart()[1..];
                        inner.Add(q.StartsWith(' ') ? q[1..] : q);
                    }
                    i--;
                    // "> **Note.** ..." style call-outs get a class from their first bold word, so the page can colour them.
                    var kind = Callout().Match(inner.FirstOrDefault(l => l.Trim().Length > 0) ?? "");
                    var css = kind.Success ? $" class=\"help-callout help-{kind.Groups[1].Value.ToLowerInvariant()}\"" : "";
                    sb.Append($"<blockquote{css}>\n");
                    Blocks(inner, sb, topLevel: false);
                    sb.Append("</blockquote>\n");
                    continue;
                }

                var item = ListItem().Match(raw);
                if (item.Success)
                {
                    FlushPara();
                    i = List(lines, i, sb) - 1;
                    continue;
                }

                para.Add(line);
            }
            FlushPara();
        }

        /// <summary>Renders the list that starts at <paramref name="start"/>; returns the index of the first line after it.</summary>
        private int List(IReadOnlyList<string> lines, int start, StringBuilder sb)
        {
            var first = ListItem().Match(lines[start]);
            var baseIndent = first.Groups[1].Length;
            var ordered = char.IsDigit(first.Groups[2].Value[0]);
            var tag = ordered ? "ol" : "ul";
            var startAt = ordered && int.TryParse(first.Groups[2].Value.TrimEnd('.', ')'), out var n) && n != 1 ? $" start=\"{n}\"" : "";
            var tasks = false;
            var items = new List<List<string>>();
            var i = start;
            while (i < lines.Count)
            {
                var m = ListItem().Match(lines[i]);
                if (!m.Success || m.Groups[1].Length != baseIndent || char.IsDigit(m.Groups[2].Value[0]) != ordered) break;
                var contentIndent = m.Groups[1].Length + m.Groups[2].Length + m.Groups[3].Length;
                var body = new List<string> { m.Groups[4].Value };
                i++;
                // The item owns every following line that is indented under it, and blank lines between such lines.
                while (i < lines.Count)
                {
                    var l = lines[i];
                    if (l.Trim().Length == 0)
                    {
                        var next = i + 1;
                        while (next < lines.Count && lines[next].Trim().Length == 0) next++;
                        if (next < lines.Count && Indent(lines[next]) > baseIndent && !IsSiblingStart(lines[next], baseIndent)) { body.Add(""); i++; continue; }
                        break;
                    }
                    if (Indent(l) <= baseIndent) break;
                    body.Add(Indent(l) >= contentIndent ? l[contentIndent..] : l.TrimStart());
                    i++;
                }
                items.Add(body);
            }

            var rendered = new List<string>();
            foreach (var body in items)
            {
                var isb = new StringBuilder();
                var task = Task().Match(body[0]);
                if (task.Success)
                {
                    tasks = true;
                    body[0] = task.Groups[2].Value;
                    var done = task.Groups[1].Value is "x" or "X" ? " checked" : "";
                    // A simple item is one label so the whole line ticks the box; a longer one keeps the box on its own.
                    if (body.Count == 1)
                    {
                        isb.Append($"<label class=\"help-check\"><input type=\"checkbox\"{done} /> <span>").Append(Inline(body[0])).Append("</span></label>");
                        Say(body[0]);
                        rendered.Add("<li class=\"help-task\">" + isb + "</li>\n");
                        continue;
                    }
                    isb.Append($"<input type=\"checkbox\"{done} aria-label=\"Done\" /> ");
                }
                // A one-paragraph item stays inline (no <p>), the way a plain list reads best.
                var simple = body.All(l => l.Trim().Length > 0 && !ListItem().IsMatch(l) && !l.TrimStart().StartsWith("```", StringComparison.Ordinal)
                                           && !l.TrimStart().StartsWith('>') && !l.TrimStart().StartsWith('|'));
                if (simple)
                {
                    var text = string.Join(" ", body.Select(l => l.Trim()));
                    isb.Append(Inline(text));
                    Say(text);
                }
                else
                {
                    // Lead text stays inline; the rest (a nested list, a second paragraph, code) renders as blocks.
                    var lead = body.TakeWhile(l => l.Trim().Length > 0 && !ListItem().IsMatch(l) && !l.TrimStart().StartsWith("```", StringComparison.Ordinal)
                                                   && !l.TrimStart().StartsWith('>') && !l.TrimStart().StartsWith('|')).ToList();
                    if (lead.Count > 0)
                    {
                        var text = string.Join(" ", lead.Select(l => l.Trim()));
                        isb.Append(Inline(text)).Append('\n');
                        Say(text);
                    }
                    Blocks(body.Skip(lead.Count).ToList(), isb, topLevel: false);
                }
                rendered.Add((task.Success ? "<li class=\"help-task\">" : "<li>") + isb + "</li>\n");
            }
            sb.Append(tasks ? $"<{tag}{startAt} class=\"help-checklist\">\n" : $"<{tag}{startAt}>\n");
            foreach (var li in rendered) sb.Append(li);
            sb.Append($"</{tag}>\n");
            return i;
        }

        private static int Indent(string line) => line.Length - line.TrimStart().Length;

        private static bool IsSiblingStart(string line, int baseIndent)
        {
            var m = ListItem().Match(line);
            return m.Success && m.Groups[1].Length == baseIndent;
        }

        private void Table(List<string> rows, StringBuilder sb)
        {
            static List<string> Cells(string row)
            {
                var s = row.Trim();
                if (s.StartsWith('|')) s = s[1..];
                if (s.EndsWith('|') && !s.EndsWith("\\|", StringComparison.Ordinal)) s = s[..^1];
                return CellSplit().Split(s).Select(c => c.Trim().Replace("\\|", "|")).ToList();
            }
            var head = Cells(rows[0]);
            var align = Cells(rows[1]).Select(c => c.StartsWith(':') && c.EndsWith(':') ? "center" : c.EndsWith(':') ? "right" : "").ToList();
            string AlignOf(int col) => col < align.Count && align[col].Length > 0 ? $" class=\"help-{align[col]}\"" : "";

            sb.Append("<div class=\"help-table\"><table class=\"grid\">\n");
            if (head.Any(c => c.Length > 0))
            {
                sb.Append("<thead><tr>");
                for (var c = 0; c < head.Count; c++) sb.Append($"<th scope=\"col\"{AlignOf(c)}>").Append(Inline(head[c])).Append("</th>");
                sb.Append("</tr></thead>\n");
                Say(string.Join(" ", head));
            }
            sb.Append("<tbody>\n");
            foreach (var row in rows.Skip(2))
            {
                var cells = Cells(row);
                if (cells.All(c => c.Length == 0)) continue;
                sb.Append("<tr>");
                for (var c = 0; c < Math.Max(cells.Count, head.Count); c++)
                    sb.Append($"<td{AlignOf(c)}>").Append(Inline(c < cells.Count ? cells[c] : "")).Append("</td>");
                sb.Append("</tr>\n");
                Say(string.Join(" ", cells));
            }
            sb.Append("</tbody></table></div>\n");
        }

        /// <summary>Inline marks. Code spans, images and links are lifted out first so nothing inside them is re-read as markup.</summary>
        private string Inline(string text)
        {
            var held = new List<string>();
            string Hold(string html) { held.Add(html); return $"\u0001{held.Count - 1}\u0002"; }

            var s = text.Replace("\u0001", "").Replace("\u0002", "");
            s = Code().Replace(s, m => Hold("<code>" + WebUtility.HtmlEncode(m.Groups[1].Value.Trim()) + "</code>"));
            s = Image().Replace(s, m =>
            {
                var alt = m.Groups[1].Value;
                var src = resolver.ResolveImage(m.Groups[2].Value.Trim());
                return Hold(src is null
                    ? "<span class=\"help-missing-image\">" + WebUtility.HtmlEncode(alt) + "</span>"
                    : $"<img src=\"{Attr(src)}\" alt=\"{Attr(alt)}\" loading=\"lazy\" />");
            });
            s = Link().Replace(s, m =>
            {
                var label = InlineMarks(WebUtility.HtmlEncode(m.Groups[1].Value));
                var target = m.Groups[2].Value.Trim();
                if (target.StartsWith('#')) return Hold($"<a href=\"{Attr(resolver.PagePath + target)}\">{label}</a>");
                var href = resolver.ResolveLink(target);
                if (href is null) return Hold(label);
                var outside = href.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                return Hold($"<a href=\"{Attr(href)}\"{(outside ? " target=\"_blank\" rel=\"noopener noreferrer\"" : "")}>{label}</a>");
            });
            s = AutoLink().Replace(s, m => Hold($"<a href=\"{Attr(m.Value)}\" target=\"_blank\" rel=\"noopener noreferrer\">{WebUtility.HtmlEncode(m.Value)}</a>"));
            s = InlineMarks(WebUtility.HtmlEncode(s));
            return Placeholder().Replace(s, m => held[int.Parse(m.Groups[1].Value)]);
        }

        private static string InlineMarks(string encoded)
        {
            var s = Bold().Replace(encoded, "<strong>$1</strong>");
            s = Italic().Replace(s, "<em>$1</em>");
            return Kbd().Replace(s, "<kbd>$1</kbd>");
        }

        private static string Attr(string value) => WebUtility.HtmlEncode(value);
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")] private static partial Regex Heading();
    [GeneratedRegex(@"^(\s*)([-*+]|\d{1,3}[.)])(\s+)(.*)$")] private static partial Regex ListItem();
    [GeneratedRegex(@"^\[([ xX])\]\s+(.*)$")] private static partial Regex Task();
    [GeneratedRegex(@"^(?:-{3,}|\*{3,}|_{3,})$")] private static partial Regex Rule();
    [GeneratedRegex(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$")] private static partial Regex TableRule();
    [GeneratedRegex(@"(?<!\\)\|")] private static partial Regex CellSplit();
    [GeneratedRegex(@"^\*\*(Note|Tip|Important|Warning|Coming)\b", RegexOptions.IgnoreCase)] private static partial Regex Callout();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex Code();
    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)")] private static partial Regex Image();
    [GeneratedRegex(@"(?<!!)\[([^\]]+)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)")] private static partial Regex Link();
    [GeneratedRegex(@"(?<![\w""'(=/])https?://[^\s<>)\u0001]+[^\s<>).,;:!?\u0001]")] private static partial Regex AutoLink();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Bold();
    [GeneratedRegex(@"(?<![\*\w])\*(?!\s)(.+?)(?<!\s)\*(?![\*\w])")] private static partial Regex Italic();
    [GeneratedRegex(@"&lt;kbd&gt;(.+?)&lt;/kbd&gt;")] private static partial Regex Kbd();
    [GeneratedRegex(@"\u0001(\d+)\u0002")] private static partial Regex Placeholder();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}
