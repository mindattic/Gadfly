using System.Text.RegularExpressions;

namespace Gadfly.Socrates.Mock;

/// <summary>
/// Reads the fields the mock panel needs back out of a filled exam prompt (docs: Gadfly/Engine/ExamPrompts.cs and the
/// grading/verification prompts). Purely textual and forgiving: a field that can't be found comes back empty.
/// </summary>
internal static partial class MockPromptReader
{
    private const string Open = "<student_text>", Close = "</student_text>";

    /// <summary>Every student-text block in the prompt, in order, unwrapped.</summary>
    public static List<string> StudentTexts(string prompt) =>
        StudentBlock().Matches(prompt).Select(m => m.Groups[1].Value.Trim()).ToList();

    /// <summary>The first student-text block after <paramref name="label"/> (or "" when none).</summary>
    public static string StudentAfter(string prompt, string label)
    {
        var at = prompt.LastIndexOf(label, StringComparison.Ordinal);
        if (at < 0) return "";
        var m = StudentBlock().Match(prompt, at);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    /// <summary>The rest of the first line that starts with <paramref name="label"/> (after trimming indentation).</summary>
    public static string Line(string prompt, string label)
    {
        foreach (var raw in prompt.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith(label, StringComparison.Ordinal)) return line[label.Length..].Trim();
        }
        return "";
    }

    /// <summary>The text between <paramref name="start"/> and the first of <paramref name="ends"/> after it.</summary>
    public static string Section(string prompt, string start, params string[] ends)
    {
        var at = prompt.IndexOf(start, StringComparison.Ordinal);
        if (at < 0) return "";
        at += start.Length;
        var stop = prompt.Length;
        foreach (var end in ends)
        {
            var e = prompt.IndexOf(end, at, StringComparison.Ordinal);
            if (e >= 0 && e < stop) stop = e;
        }
        return prompt[at..stop].Trim();
    }

    /// <summary>Numbered passages ("[1] (location) text") as (number, location, text).</summary>
    public static List<(int Number, string? Location, string Text)> Passages(string section)
    {
        var result = new List<(int, string?, string)>();
        var starts = PassageStart().Matches(section).ToList();
        for (var i = 0; i < starts.Count; i++)
        {
            var m = starts[i];
            var end = i + 1 < starts.Count ? starts[i + 1].Index : section.Length;
            var body = section[(m.Index + m.Length)..end].Trim();
            string? location = null;
            if (body.StartsWith('('))
            {
                var close = body.IndexOf(')');
                if (close > 0)
                {
                    location = body[1..close].Trim();
                    body = body[(close + 1)..].Trim();
                }
            }
            result.Add((int.Parse(m.Groups[1].Value), location, body));
        }
        return result;
    }

    /// <summary>Sentences of a text (simple split on terminal punctuation).</summary>
    public static List<string> Sentences(string text) =>
        SentenceSplit().Split(text.Replace('\n', ' ')).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "is", "are", "was", "were", "be", "been", "it", "its", "that", "this",
        "for", "on", "with", "as", "by", "at", "from", "not", "no", "can", "does", "do", "you", "your", "they", "them", "their",
        "what", "which", "when", "where", "how", "why", "who", "has", "have", "had", "will", "would", "could", "should", "than",
        "then", "there", "these", "those", "into", "about", "each", "also", "more", "most", "some", "such", "only", "same", "other",
        "student", "students", "explain", "understand", "understanding", "concept", "point", "points", "weight", "answer",
        "means", "mean", "very", "just", "like", "much", "many", "one", "two", "both", "between", "because", "if", "but", "so",
        "mock", "think", "know", "really", "well", "get", "gets", "got",
    };

    /// <summary>Content words (lower case, at least three letters, no stop words), crudely singularized.</summary>
    public static HashSet<string> Keywords(string text) =>
        Word().Matches(text.ToLowerInvariant()).Select(m => Stem(m.Value)).Where(w => w.Length >= 3 && !Stop.Contains(w)).ToHashSet();

    private static string Stem(string w) =>
        w.Length > 4 && w.EndsWith("ies") ? w[..^3] + "y"
        : w.Length > 4 && w.EndsWith("es") && !w.EndsWith("ses") ? w[..^1]
        : w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss") ? w[..^1]
        : w;

    /// <summary>Share of <paramref name="expected"/> keywords present in <paramref name="actual"/> (0 when nothing is expected).</summary>
    public static double Coverage(HashSet<string> expected, HashSet<string> actual) =>
        expected.Count == 0 ? 0 : (double)expected.Count(actual.Contains) / expected.Count;

    public static double Jaccard(string a, string b)
    {
        var x = Keywords(a);
        var y = Keywords(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        var inter = x.Count(y.Contains);
        return (double)inter / (x.Count + y.Count - inter);
    }

    /// <summary>The student said they don't know (in a few common wordings).</summary>
    public static bool IsAporia(string answer) =>
        answer.Trim().Length == 0 || AporiaWords().IsMatch(answer) && answer.Length <= 200;

    /// <summary>Words that signal explaining why or how, not only what.</summary>
    public static int ReasoningMarkers(string answer) => Reasoning().Matches(answer).Count;

    public static bool HasNegation(string text) => Negation().IsMatch(text);

    public static string WithoutNegation(string text) => Negation().Replace(text, " ");

    [GeneratedRegex(@"<student_text>\s*\n?(.*?)\n?\s*</student_text>", RegexOptions.Singleline)]
    private static partial Regex StudentBlock();

    [GeneratedRegex(@"(?m)^\s*\[(\d+)\]")]
    private static partial Regex PassageStart();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Word();

    [GeneratedRegex(@"\b(i\s+(really\s+)?(do\s*n[o']?t|dont|don't)\s+know|no\s+idea|not\s+sure|can'?t\s+answer|cannot\s+answer|i\s+dunno|aporia|i\s+have\s+no\s+clue)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AporiaWords();

    [GeneratedRegex(@"\b(because|since|so|therefore|which means|that means|so that|due to|as a result|if|then|depends|whereas|while)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Reasoning();

    [GeneratedRegex(@"\b(not|never|no|isn't|aren't|doesn't|don't|can't|cannot|won't|wasn't|weren't)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Negation();
}
