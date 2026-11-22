using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Gadfly.Server.Help;

/// <summary>A help topic ready to show: its catalogue entry and its rendered text (null when the file is missing from the build).</summary>
public sealed record LoadedHelpTopic(HelpTopic Topic, HelpDocument? Document);

/// <summary>One search hit: the topic, the section it was found in (null for the topic as a whole), and a short excerpt.</summary>
public sealed record HelpSearchResult(HelpTopic Topic, string? SectionHeading, string? SectionAnchor, string ExcerptHtml, double Score);

/// <summary>
/// The help center's content (GAD-T292). At startup it reads every guide in docs/guides, which the build embeds in the
/// app (Gadfly.Server.csproj, "Help center"), renders each one and builds a small in-memory search index. Nothing is
/// read from disk at run time, so the deployed app always shows the guides of the commit it was built from.
/// </summary>
public sealed partial class HelpLibrary
{
    /// <summary>The prefix the build gives the embedded guides: "Gadfly.Help/STUDENT_GUIDE.md", "Gadfly.Help/images/x.png".</summary>
    public const string ResourcePrefix = "Gadfly.Help/";

    private readonly Dictionary<string, LoadedHelpTopic> _topics;
    private readonly List<IndexEntry> _index = [];
    private readonly Func<string, Stream?> _open;
    private readonly HashSet<string> _resources;

    /// <summary>Reads the guides embedded in the Gadfly.Server assembly.</summary>
    public HelpLibrary() : this(typeof(HelpLibrary).Assembly) { }

    public HelpLibrary(Assembly assembly) : this(
        assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..].Replace('\\', '/')),
        path => assembly.GetManifestResourceStream(ResourcePrefix + path)
                ?? assembly.GetManifestResourceStream(ResourcePrefix + path.Replace('/', '\\')))
    { }

    /// <summary>For tests and tools: guides from any source, named by their path under docs/guides.</summary>
    public HelpLibrary(IEnumerable<string> files, Func<string, Stream?> open)
    {
        _open = open;
        _resources = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        _topics = new Dictionary<string, LoadedHelpTopic>(StringComparer.OrdinalIgnoreCase);
        foreach (var topic in HelpCatalog.Topics)
        {
            var doc = Load(topic);
            _topics[topic.Slug] = new LoadedHelpTopic(topic, doc);
            if (doc is not null) Index(topic, doc);
        }
    }

    /// <summary>Topics whose Markdown file was not in the build (a test keeps this empty).</summary>
    public IReadOnlyList<HelpTopic> Missing => _topics.Values.Where(t => t.Document is null).Select(t => t.Topic).ToList();

    public LoadedHelpTopic? Get(string? slug) =>
        HelpCatalog.Find(slug) is { } topic && _topics.TryGetValue(topic.Slug, out var loaded) ? loaded : null;

    /// <summary>
    /// A topic if this reader may open it. Null when it does not exist; <paramref name="forbidden"/> is true when it exists
    /// but is for another role, so the page can say "sign in" or "this topic is for staff" instead of "not found".
    /// </summary>
    public LoadedHelpTopic? GetFor(string? slug, HelpViewer viewer, out bool forbidden)
    {
        forbidden = false;
        var loaded = Get(slug);
        if (loaded is null) return null;
        if (viewer.CanRead(loaded.Topic)) return loaded;
        forbidden = true;
        return null;
    }

    /// <summary>An embedded image or other file a guide refers to (under docs/guides), for the /help/media endpoint.</summary>
    public Stream? OpenMedia(string path)
    {
        var clean = path.Replace('\\', '/').TrimStart('/');
        if (clean.Length == 0 || clean.Contains("..", StringComparison.Ordinal) || !_resources.Contains(clean)) return null;
        if (clean.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;   // guides are shown as pages, not files
        return _open(clean);
    }

    private HelpDocument? Load(HelpTopic topic)
    {
        if (!_resources.Contains(topic.File)) return null;
        using var stream = _open(topic.File);
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return HelpMarkdown.Render(reader.ReadToEnd(), new Resolver(topic, _resources));
    }

    /// <summary>Turns links between guides into help pages and images into /help/media addresses.</summary>
    private sealed class Resolver(HelpTopic topic, HashSet<string> resources) : IHelpLinkResolver
    {
        public string PagePath => HelpPaths.Topic(topic.Slug);

        public string? ResolveLink(string target)
        {
            if (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                return target;
            if (target.StartsWith('/')) return target.TrimStart('/').Length == 0 ? "/" : target.TrimStart('/');   // an app page, as written
            var hash = target.IndexOf('#');
            var file = hash >= 0 ? target[..hash] : target;
            var fragment = hash >= 0 ? target[hash..] : "";
            var name = file.Replace('\\', '/');
            if (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
            // Links out of docs/guides (../PRIVACY.md and the like) are repository documents the app does not serve.
            if (name.Contains("../", StringComparison.Ordinal)) return null;
            if (HelpCatalog.FindByFile(name) is { } other) return HelpPaths.Topic(other.Slug) + fragment;
            return resources.Contains(name) && !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? HelpPaths.Media(name) : null;
        }

        public string? ResolveImage(string target)
        {
            if (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return target;
            if (target.StartsWith('/')) return target.TrimStart('/');
            var name = target.Replace('\\', '/');
            if (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
            return resources.Contains(name) ? HelpPaths.Media(name) : null;
        }
    }

    // ---------------------------------------------------------------- search

    private sealed record IndexEntry(HelpTopic Topic, string? Heading, string? Anchor, string Text,
        HashSet<string> TitleTerms, HashSet<string> KeywordTerms, HashSet<string> HeadingTerms, Dictionary<string, int> BodyTerms);

    private void Index(HelpTopic topic, HelpDocument doc)
    {
        var titleTerms = Terms(topic.Title + " " + topic.Summary).ToHashSet();
        var keywordTerms = Terms(topic.Keywords).ToHashSet();
        // The whole topic is one entry (so a query matching across sections still finds it), and each section another.
        var all = string.Join(" ", doc.Sections.Select(s => s.Heading + " " + s.Text));
        _index.Add(new IndexEntry(topic, null, null, topic.Summary + " " + (doc.Sections.FirstOrDefault()?.Text ?? ""),
            titleTerms, keywordTerms, [], Count(Terms(all))));
        foreach (var s in doc.Sections.Where(s => s.Heading.Length > 0))
            _index.Add(new IndexEntry(topic, s.Heading, s.Anchor, s.Text, titleTerms, keywordTerms, Terms(s.Heading).ToHashSet(), Count(Terms(s.Text))));
    }

    private static Dictionary<string, int> Count(IEnumerable<string> terms)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in terms) d[t] = d.TryGetValue(t, out var n) ? n + 1 : 1;
        return d;
    }

    /// <summary>
    /// Finds topics and sections for a query, among the topics this reader may open. Every word of the query must appear
    /// (in the title, the keywords, a heading or the text); a word of three letters or more also matches words that start
    /// with it, so "micro" finds "microphone". Results are ranked title first, then headings, then text, and each topic
    /// appears at most <paramref name="perTopic"/> times.
    /// </summary>
    public IReadOnlyList<HelpSearchResult> Search(string? query, HelpViewer viewer, int max = 25, int perTopic = 3)
    {
        var terms = Terms(query ?? "").Distinct().ToList();
        if (terms.Count == 0) return [];
        var hits = new List<HelpSearchResult>();
        foreach (var e in _index)
        {
            if (!viewer.CanRead(e.Topic)) continue;
            double score = 0;
            var all = true;
            foreach (var term in terms)
            {
                double best = 0;
                if (Has(e.TitleTerms, term, out var exactT)) best = Math.Max(best, exactT ? 12 : 8);
                if (Has(e.KeywordTerms, term, out var exactK)) best = Math.Max(best, exactK ? 7 : 5);
                if (Has(e.HeadingTerms, term, out var exactH)) best = Math.Max(best, exactH ? 6 : 4);
                var body = BodyCount(e.BodyTerms, term);
                if (body > 0) best = Math.Max(best, 1 + Math.Log(1 + body));
                if (best == 0) { all = false; break; }
                score += best;
            }
            if (!all) continue;
            if (e.Heading is null) score *= 0.9;                 // prefer the exact section over the topic as a whole
            if (e.Topic.IsStub) score *= 0.5;                    // a "coming soon" page is rarely the answer
            hits.Add(new HelpSearchResult(e.Topic, e.Heading, e.Anchor, Excerpt(e.Text, terms), score));
        }
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Topic.Title, StringComparer.OrdinalIgnoreCase)
            .GroupBy(h => h.Topic.Slug).SelectMany(g => g.Take(perTopic))
            .OrderByDescending(h => h.Score).Take(max).ToList();
    }

    private static bool Has(HashSet<string> set, string term, out bool exact)
    {
        exact = set.Contains(term);
        if (exact) return true;
        return term.Length >= 3 && set.Any(s => s.StartsWith(term, StringComparison.Ordinal));
    }

    private static int BodyCount(Dictionary<string, int> body, string term)
    {
        if (body.TryGetValue(term, out var n)) return n;
        if (term.Length < 3) return 0;
        var sum = 0;
        foreach (var (k, v) in body) if (k.StartsWith(term, StringComparison.Ordinal)) sum += v;
        return sum;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "can", "do", "does", "for", "from", "how", "i", "if", "in", "is", "it",
        "me", "my", "of", "on", "or", "so", "the", "to", "what", "when", "where", "which", "who", "why", "will", "with", "you", "your",
    };

    /// <summary>Lower-case words of two letters or more, without common little words, with a trailing plural "s" removed.</summary>
    public static IEnumerable<string> Terms(string text)
    {
        foreach (Match m in Word().Matches(text.ToLowerInvariant()))
        {
            var w = m.Value.Replace("'s", "").Replace("'", "");
            if (w.Length < 2 || StopWords.Contains(w)) continue;
            yield return Stem(w);
        }
    }

    private static string Stem(string w) =>
        w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal) ? w[..^3] + "y"
        : w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) && !w.EndsWith("us", StringComparison.Ordinal) ? w[..^1]
        : w;

    /// <summary>About 200 characters around the first query word in the text, HTML-encoded, with the query words marked.</summary>
    private static string Excerpt(string text, IReadOnlyList<string> terms)
    {
        if (text.Length == 0) return "";
        var lower = text.ToLowerInvariant();
        var at = terms.Select(t => FindWord(lower, t)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, at - 70);
        if (start > 0) { var sp = text.IndexOf(' ', start); if (sp > 0 && sp < at) start = sp + 1; }
        var end = Math.Min(text.Length, start + 220);
        if (end < text.Length) { var sp = text.LastIndexOf(' ', end); if (sp > at) end = sp; }
        var slice = text[start..end];
        var html = WebUtility.HtmlEncode(slice);
        foreach (var t in terms.OrderByDescending(t => t.Length))
            html = Regex.Replace(html, $@"(?<![\w>])({Regex.Escape(t)}\w*)", "<mark>$1</mark>", RegexOptions.IgnoreCase);
        return (start > 0 ? "… " : "") + html + (end < text.Length ? " …" : "");
    }

    private static int FindWord(string lower, string term)
    {
        foreach (Match m in Regex.Matches(lower, $@"\b{Regex.Escape(term)}")) return m.Index;
        return -1;
    }

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}']*")] private static partial Regex Word();
}

/// <summary>The help center's addresses, relative to the app's base (the layout sets &lt;base href="/"&gt;).</summary>
public static class HelpPaths
{
    public const string Home = "help";
    public static string Topic(string slug) => "help/" + Uri.EscapeDataString(slug);
    public static string Topic(string slug, string? anchor) => Topic(slug) + (string.IsNullOrEmpty(anchor) ? "" : "#" + anchor);
    public static string Search(string? query) => "help/search" + (string.IsNullOrWhiteSpace(query) ? "" : "?q=" + Uri.EscapeDataString(query.Trim()));
    public static string Media(string path) => "help/media/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
