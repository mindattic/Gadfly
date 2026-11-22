using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Gadfly.Server.Localization;

/// <summary>
/// The interface strings of every UI language Gadfly speaks (GAD-T543): one JSON file per language under Localization/Strings,
/// embedded in the app as "Gadfly.Ui/&lt;lang&gt;.json". Each file is a flat object of stable dotted keys ("getReady.title") to the
/// text; English is the source and the fallback, so a key another language has not translated yet shows in English, never as a
/// raw key. Loaded once at startup; immutable.
/// </summary>
public sealed class UiCatalog
{
    /// <summary>English: the source language and the fallback for every key.</summary>
    public const string English = "en";
    /// <summary>Korean (GAD-T526): the first translated language.</summary>
    public const string Korean = "ko";

    /// <summary>The UI languages, in the order a picker lists them.</summary>
    public static readonly IReadOnlyList<string> Languages = [English, Korean];

    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> byLanguage;

    public UiCatalog() : this(LoadEmbedded()) { }

    /// <summary>A catalog over the given strings (tests).</summary>
    public UiCatalog(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> strings) =>
        byLanguage = strings.ToFrozenDictionary(
            kv => kv.Key,
            kv => kv.Value.ToFrozenDictionary(StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The primary language subtag Gadfly has a UI for ("ko-KR" becomes "ko"); English for anything else.</summary>
    public static string Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return English;
        var t = tag.Trim().Replace('_', '-');
        var dash = t.IndexOf('-');
        if (dash > 0) t = t[..dash];
        t = t.ToLowerInvariant();
        return Languages.Contains(t) ? t : English;
    }

    /// <summary>The culture used for dates and numbers in a UI language.</summary>
    public static CultureInfo CultureFor(string? language) => Normalize(language) switch
    {
        Korean => CultureInfo.GetCultureInfo("ko-KR"),
        _ => CultureInfo.GetCultureInfo("en-US"),
    };

    /// <summary>The language's own name for itself, as a picker shows it.</summary>
    public static string NativeName(string? language) => Normalize(language) switch
    {
        Korean => "한국어",
        _ => "English",
    };

    /// <summary>Every key the English source defines.</summary>
    public IEnumerable<string> Keys => Table(English).Keys;

    /// <summary>The keys one language defines itself (no fallback).</summary>
    public IReadOnlyDictionary<string, string> Table(string language) =>
        byLanguage.TryGetValue(Normalize(language), out var t) ? t : FrozenDictionary<string, string>.Empty;

    /// <summary>The text of <paramref name="key"/> in <paramref name="language"/>, else in English, else null.</summary>
    public string? Find(string language, string key)
    {
        if (byLanguage.TryGetValue(Normalize(language), out var t) && t.TryGetValue(key, out var s)) return s;
        return byLanguage.TryGetValue(English, out var en) && en.TryGetValue(key, out var e) ? e : null;
    }

    /// <summary>
    /// Every key starting with <paramref name="prefix"/> in <paramref name="language"/>, English filling the gaps: the dictionary
    /// a script fetches or a component passes to its JS module (the exam room, GAD-T544).
    /// </summary>
    public IReadOnlyDictionary<string, string> Dictionary(string language, string prefix = "")
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in Table(English))
            if (k.StartsWith(prefix, StringComparison.Ordinal)) result[k] = v;
        foreach (var (k, v) in Table(language))
            if (k.StartsWith(prefix, StringComparison.Ordinal)) result[k] = v;
        return result;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadEmbedded()
    {
        // One main file per language ("ko.json") plus any area files ("examRoom.ko.json"), all merged; an area file may not
        // redefine a key another file defines (LocalizationTests checks), so the merge order never matters.
        var asm = typeof(UiCatalog).Assembly;
        var names = asm.GetManifestResourceNames().Where(n => n.StartsWith("Gadfly.Ui/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var lang in Languages)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in names.Where(n => n == $"Gadfly.Ui/{lang}.json" || n.EndsWith($".{lang}.json", StringComparison.Ordinal)))
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;
                foreach (var (k, v) in Parse(stream)) map[k] = v;
            }
            if (map.Count > 0) result[lang] = map;
        }
        return result;
    }

    /// <summary>Reads one language file: a flat JSON object of string values; keys starting with "_" are comments.</summary>
    public static IReadOnlyDictionary<string, string> Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Name.StartsWith('_') || p.Value.ValueKind != JsonValueKind.String) continue;
            map[p.Name] = p.Value.GetString()!;
        }
        return map;
    }
}
