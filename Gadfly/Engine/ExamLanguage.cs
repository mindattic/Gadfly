namespace Gadfly.Engine;

/// <summary>
/// The language an exam attempt is conducted in (GAD-T526, GAD-T534): a BCP-47 primary-language tag, lower case ("en", "ko").
/// The source material, concept map and rubric stay in their own (English) language; the examiner asks, and the student answers,
/// in the exam language. English is the default and the only language whose behavior predates localization: an English attempt
/// by a student whose primary language is English behaves exactly as it always has.
/// </summary>
public static class ExamLanguage
{
    public const string English = "en";
    public const string Korean = "ko";

    /// <summary>The languages Gadfly can run an exam in, in display order.</summary>
    public static readonly IReadOnlyList<string> Supported = [English, Korean];

    /// <summary>The offered languages a new exam starts with (Exam.Languages).</summary>
    public const string DefaultOffered = "en,ko";

    /// <summary>
    /// The primary language subtag of <paramref name="tag"/>, lower case ("ko-KR" becomes "ko", "EN" becomes "en"); English when
    /// the tag is empty.
    /// </summary>
    public static string Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return English;
        var t = tag.Trim().Replace('_', '-');
        var dash = t.IndexOf('-');
        if (dash > 0) t = t[..dash];
        return t.ToLowerInvariant();
    }

    public static bool IsSupported(string? tag) => Supported.Contains(Normalize(tag));

    public static bool IsEnglish(string? tag) => Normalize(tag) == English;

    public static bool IsKorean(string? tag) => Normalize(tag) == Korean;

    /// <summary>Parses a comma-separated language list ("en,ko") into normalized, distinct, supported tags; English when empty.</summary>
    public static IReadOnlyList<string> ParseList(string? list)
    {
        var tags = (list ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).Where(Supported.Contains).Distinct().ToList();
        return tags.Count == 0 ? [English] : tags;
    }

    /// <summary>The comma-separated form of a language list, as stored.</summary>
    public static string FormatList(IEnumerable<string> tags) =>
        string.Join(",", tags.Select(Normalize).Where(t => t.Length > 0).Distinct());

    /// <summary>The language's own name for itself, for prompts and pickers.</summary>
    public static string NativeName(string? tag) => Normalize(tag) switch
    {
        Korean => "한국어",
        _ => "English",
    };

    /// <summary>The language's English name, for prompts written in English.</summary>
    public static string EnglishName(string? tag) => Normalize(tag) switch
    {
        Korean => "Korean",
        English => "English",
        var other => other,
    };
}
