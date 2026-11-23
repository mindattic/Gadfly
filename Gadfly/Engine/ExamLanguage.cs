namespace Gadfly.Engine;

/// <summary>
/// The language an exam attempt is conducted in (GAD-T526, GAD-T534): a BCP-47 primary-language tag, lower case ("en", "ko", "es").
/// The source material, concept map and rubric stay in their own (English) language; the examiner asks, and the student answers,
/// in the exam language. English is the default and the only language whose behavior predates localization: an English attempt
/// by a student whose primary language is English behaves exactly as it always has.
/// </summary>
public static class ExamLanguage
{
    public const string English = "en";
    public const string Korean = "ko";
    /// <summary>Spanish (GAD-T596): neutral Latin American Spanish, the student addressed as usted.</summary>
    public const string Spanish = "es";

    /// <summary>The languages Gadfly can run an exam in, in display order.</summary>
    public static readonly IReadOnlyList<string> Supported = [English, Korean, Spanish];

    /// <summary>
    /// The offered languages a new exam starts with (Exam.Languages): English, Korean and Spanish (GAD-T611). Staff turn any of them
    /// off per exam. Exams created before Spanish that still offered exactly the old default, en,ko, were given es by the
    /// SpanishExamLanguage migration; an exam whose staff had changed its list keeps it as they left it.
    /// </summary>
    public const string DefaultOffered = "en,ko,es";

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

    public static bool IsSpanish(string? tag) => Normalize(tag) == Spanish;

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
        Spanish => "Español",
        _ => "English",
    };

    /// <summary>The language's English name, for prompts written in English.</summary>
    public static string EnglishName(string? tag) => Normalize(tag) switch
    {
        Korean => "Korean",
        Spanish => "Spanish",
        English => "English",
        var other => other,
    };
}

/// <summary>
/// The languages of one attempt (GAD-T534): the exam language it is conducted in, and the student's self-declared primary language
/// (null when they gave none, which counts as the exam language). <see cref="IsDefault"/> is today's exam: English, by a student
/// whose primary language is English or unknown; it runs on the unchanged English prompts and heuristics. The primary language
/// only switches adaptations on (plain questions, the judges' content view); it is never told to the judges (research note
/// esl-and-korean R5.1) and never inferred from the voice.
/// </summary>
public sealed record AttemptLanguage(string Language, string? PrimaryLanguage = null)
{
    public static readonly AttemptLanguage Default = new(ExamLanguage.English);

    /// <summary>The exam language, normalized.</summary>
    public string Exam { get; } = ExamLanguage.Normalize(Language);

    /// <summary>The primary language, normalized; null when not declared.</summary>
    public string? Primary { get; } = string.IsNullOrWhiteSpace(PrimaryLanguage) ? null : ExamLanguage.Normalize(PrimaryLanguage);

    /// <summary>The student declared a primary language other than the exam language.</summary>
    public bool IsNonNative => Primary is not null && Primary != Exam;

    /// <summary>An English exam by an English-primary (or undeclared) student: exactly the behavior before localization.</summary>
    public bool IsDefault => Exam == ExamLanguage.English && !IsNonNative;

    public bool IsKorean => Exam == ExamLanguage.Korean;

    public bool IsSpanish => Exam == ExamLanguage.Spanish;
}
