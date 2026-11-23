using System.Text.RegularExpressions;

namespace Gadfly.Sources.Verification;

/// <summary>Whether an edit to a recorded answer may count as a transcription correction (Part 4.7).</summary>
/// <param name="Allowed">True when the edit is small enough to be a recording fix rather than a new answer.</param>
/// <param name="ChangedWords">Word-level edit distance between the two texts.</param>
/// <param name="Reason">Why it was refused, in words a student can read; empty when allowed.</param>
public sealed record CorrectionCheck(bool Allowed, int ChangedWords, string Reason)
{
    /// <summary>The refusal.correction.* string key of <see cref="Reason"/> (GAD-T628), so the student reads it in the attempt's language; null when allowed.</summary>
    public string? ReasonKey { get; init; }

    /// <summary>The values of <see cref="ReasonKey"/>'s <c>{0}</c> slots.</summary>
    public object[] ReasonArgs { get; init; } = [];
}

/// <summary>
/// Deterministic limits that keep "correct a typo" from becoming a way to rewrite an answer (Part 4.7:
/// a transcription correction never reduces the score, so it must not be able to raise it with new
/// substance either). Typed answers get a tight limit; a voice transcript confirmation, where whole
/// words may have been misheard, a looser one. Anything bigger must go through a substantive revision,
/// which is recorded as a new response event.
/// </summary>
public static partial class CorrectionRules
{
    /// <summary>A typed answer's typo fix: at most this many words changed, or this share of the answer, whichever is larger.</summary>
    public const int TypedMinWords = 2;
    public const double TypedShare = 0.10;

    /// <summary>A voice transcript correction: misheard words are common, so the allowance is wider.</summary>
    public const int VoiceMinWords = 3;
    public const double VoiceShare = 0.25;

    public static CorrectionCheck Check(string original, string corrected, bool voice)
    {
        var a = Words(original);
        var b = Words(corrected);
        if (b.Count == 0) return new(false, a.Count, "The corrected answer is empty.") { ReasonKey = "refusal.correction.empty" };
        if (string.Equals(Normalize(original), Normalize(corrected), StringComparison.Ordinal))
            return new(false, 0, "That is the same as the recorded answer.") { ReasonKey = "refusal.correction.same" };
        var changed = WordDistance(a, b);
        var allowance = Math.Max(voice ? VoiceMinWords : TypedMinWords, (int)Math.Floor(Math.Max(a.Count, b.Count) * (voice ? VoiceShare : TypedShare)));
        if (changed > allowance)
            return new(false, changed, voice
                ? $"That changes {changed} words, more than a recording correction can ({allowance}). If you want to change what you said, use \"I want to change my answer\"."
                : $"That changes {changed} words, more than a typo fix can ({allowance}). If you want to change what your answer says, use \"I want to change my answer\".")
            {
                ReasonKey = voice ? "refusal.correction.tooManyVoice" : "refusal.correction.tooManyTyped",
                ReasonArgs = [changed, allowance],
            };
        // Adding or removing a negation flips the meaning: never a recording fix.
        if (Negations(a) != Negations(b))
            return new(false, changed, "That adds or removes a \"not\" (or similar), which changes the meaning. Use \"I want to change my answer\" for that.")
                { ReasonKey = "refusal.correction.negation" };
        // Each replaced word should look like a misspelling of the old one (typed answers only).
        if (!voice)
        {
            foreach (var (from, to) in Replacements(a, b))
                if (CharDistance(from, to) > Math.Max(2, (int)Math.Ceiling(Math.Max(from.Length, to.Length) * 0.4)))
                    return new(false, changed, $"\"{from}\" → \"{to}\" is a different word, not a typo fix. Use \"I want to change my answer\" to change what you said.")
                        { ReasonKey = "refusal.correction.differentWord", ReasonArgs = [from, to] };
        }
        return new(true, changed, "");
    }

    private static readonly HashSet<string> NegationWords = new(StringComparer.Ordinal)
    {
        "not", "no", "never", "none", "nor", "neither", "nothing", "cannot", "without",
    };

    private static int Negations(IEnumerable<string> words) =>
        words.Count(w => NegationWords.Contains(w) || w.EndsWith("n't", StringComparison.Ordinal));

    private static List<string> Words(string? s) => WordRx().Matches((s ?? "").ToLowerInvariant()).Select(m => m.Value).ToList();

    private static string Normalize(string? s) => string.Join(" ", Words(s));

    /// <summary>Levenshtein distance over words.</summary>
    public static int WordDistance(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var d = new int[a.Count + 1, b.Count + 1];
        for (var i = 0; i <= a.Count; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Count; j++) d[0, j] = j;
        for (var i = 1; i <= a.Count; i++)
            for (var j = 1; j <= b.Count; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Count, b.Count];
    }

    public static int CharDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>The substituted word pairs of an optimal word alignment (insertions and deletions are not pairs).</summary>
    private static IEnumerable<(string From, string To)> Replacements(List<string> a, List<string> b)
    {
        var d = new int[a.Count + 1, b.Count + 1];
        for (var i = 0; i <= a.Count; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Count; j++) d[0, j] = j;
        for (var i = 1; i <= a.Count; i++)
            for (var j = 1; j <= b.Count; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        var pairs = new List<(string, string)>();
        int x = a.Count, y = b.Count;
        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1] && d[x, y] == d[x - 1, y - 1]) { x--; y--; }
            else if (d[x, y] == d[x - 1, y - 1] + 1) { pairs.Add((a[x - 1], b[y - 1])); x--; y--; }
            else if (d[x, y] == d[x - 1, y] + 1) x--;
            else y--;
        }
        return pairs;
    }

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex WordRx();
}
