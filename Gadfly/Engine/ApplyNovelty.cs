using System.Text.RegularExpressions;

namespace Gadfly.Engine;

/// <summary>
/// The result of checking whether an Apply question's situation is new to the student (GAD-T442). <see cref="Novel"/> false
/// means the question reuses an example from the source passages the question was grounded in, or a situation the student
/// already discussed: an answer to it is recorded as extrapolation (credited as application), never as transfer.
/// </summary>
/// <param name="Novel">True when no reuse was found (or the question was too short to check, see <see cref="Checked"/>).</param>
/// <param name="Overlap">The largest share of the question's situation words found in one source sentence or earlier answer.</param>
/// <param name="SharedWords">How many situation words that closest match shared.</param>
/// <param name="ReusedFrom">"source" or "answer" when not novel; null otherwise.</param>
/// <param name="Match">The source sentence or the student's words the situation reuses (clipped); null when novel.</param>
/// <param name="MatchTurn">The 1-based turn of the reused answer, when <paramref name="ReusedFrom"/> is "answer".</param>
/// <param name="Checked">False when the question named too few situation words to compare; it then counts as novel.</param>
public sealed record ApplyNoveltyCheck(bool Novel, double Overlap, int SharedWords, string? ReusedFrom = null, string? Match = null,
    int? MatchTurn = null, bool Checked = true)
{
    /// <summary>A plain sentence for the reviewer.</summary>
    public string Describe() => Novel
        ? Checked ? $"Novelty check: the situation shares at most {Overlap:P0} of its words with the source or the student's earlier answers; credited as transfer."
                  : "Novelty check: the question named too few situation words to compare; credited as transfer."
        : $"Novelty check: the situation reuses {(ReusedFrom == "answer" ? $"one the student already discussed (turn {MatchTurn})" : "an example from the source")} " +
          $"({SharedWords} of its words, {Overlap:P0}: \"{Match}\"), so the answer is recorded as extrapolation, not transfer.";
}

/// <summary>
/// A deterministic check that an Apply question puts the concept in a situation new to the student (GAD-T442; ElenchusAtScale
/// M5, 5.2.2). The prompt asks for a new situation; this checks the question it produced. The question's situation words
/// (its content words, less the concept's and objective's own terms and the usual question wording) are compared with each
/// sentence of the source passages the question was grounded in and with each of the student's earlier answers: when one of
/// them holds at least <see cref="ReuseAt"/> of the situation words (and at least <see cref="MinShared"/> of them), the
/// situation is not new. Word overlap only, no model or embedding call, so it adds no wait between questions; a question is
/// never redrafted for it (a redraft is a panel round trip), the answer is recorded as extrapolation instead.
/// </summary>
public static partial class ApplyNovelty
{
    /// <summary>Share of the situation words one sentence or answer must hold for the situation to count as reused.</summary>
    public const double ReuseAt = 0.5;

    /// <summary>Fewest shared situation words for a reuse: two common words are a coincidence, not a reused example.</summary>
    public const int MinShared = 3;

    /// <summary>Fewest situation words a question must name to be checked at all.</summary>
    public const int MinSituationWords = 3;

    /// <summary>
    /// Checks <paramref name="question"/> against the source passages (<paramref name="sourceText"/>) and the student's earlier
    /// answers in <paramref name="session"/>. <paramref name="conceptTerms"/> (concept and objective titles) are not counted as
    /// situation words: a new situation still names the concept.
    /// </summary>
    public static ApplyNoveltyCheck Check(string question, string? sourceText, SocraticSession? session, params string?[] conceptTerms)
    {
        var answers = session?.Turns.Where(t => t.Answer is { Text.Length: > 0 }).Select(t => (Turn: t.Index + 1, t.Answer!.Text)).ToList() ?? [];
        return Check(question, sourceText, answers, conceptTerms);
    }

    /// <summary>As <see cref="Check(string, string?, SocraticSession?, string?[])"/>, with the earlier answers given directly.</summary>
    public static ApplyNoveltyCheck Check(string question, string? sourceText, IReadOnlyList<(int Turn, string Text)> answers,
        params string?[] conceptTerms)
    {
        var excluded = new HashSet<string>(conceptTerms.Where(t => t is not null).SelectMany(t => Words(t!)), StringComparer.Ordinal);
        excluded.UnionWith(QuestionWording);
        var situation = Words(question).Where(w => !excluded.Contains(w)).ToHashSet(StringComparer.Ordinal);
        if (situation.Count < MinSituationWords) return new ApplyNoveltyCheck(true, 0, 0, Checked: false);

        var best = (Overlap: 0.0, Shared: 0, From: (string?)null, Text: (string?)null, Turn: (int?)null);
        void Consider(string text, string from, int? turn)
        {
            var shared = situation.Count(Words(text).ToHashSet(StringComparer.Ordinal).Contains);
            var overlap = (double)shared / situation.Count;
            if (overlap > best.Overlap || (overlap == best.Overlap && shared > best.Shared)) best = (overlap, shared, from, text, turn);
        }
        foreach (var sentence in Sentences(sourceText ?? "")) Consider(sentence, "source", null);
        foreach (var (turn, text) in answers) Consider(text, "answer", turn);

        var reused = best.Overlap >= ReuseAt && best.Shared >= MinShared;
        return reused
            ? new ApplyNoveltyCheck(false, Math.Round(best.Overlap, 3), best.Shared, best.From, Clip(best.Text!.Trim(), 160), best.Turn)
            : new ApplyNoveltyCheck(true, Math.Round(best.Overlap, 3), best.Shared);
    }

    /// <summary>The content words of <paramref name="text"/>: lower case, stop words and short words dropped, plurals folded.</summary>
    public static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(text.ToLowerInvariant())
            .Select(m => m.Value.EndsWith("'s", StringComparison.Ordinal) ? m.Value[..^2] : m.Value.Trim('\''))
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Select(Fold);

    private static string Fold(string w) =>
        w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal) ? w[..^3] + "y"
        : w.Length > 4 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) ? w[..^1]
        : w;

    private static IEnumerable<string> Sentences(string text) =>
        SentenceSplit().Split(text).Select(s => s.Trim()).Where(s => s.Length > 0);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    [GeneratedRegex(@"[a-z0-9][a-z0-9']*")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"(?<=[.!?])\s+|\n+")]
    private static partial Regex SentenceSplit();

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "but", "not", "you", "your", "yours", "all", "any", "can", "had", "has", "have", "her", "his",
        "him", "she", "they", "them", "their", "there", "then", "than", "that", "this", "these", "those", "with", "from", "into",
        "onto", "out", "over", "under", "was", "were", "been", "being", "will", "would", "could", "should", "might", "may", "must",
        "what", "when", "where", "which", "who", "whom", "whose", "why", "how", "its", "it's", "our", "ours", "one", "ones", "some",
        "such", "each", "other", "more", "most", "very", "also", "just", "only", "about", "after", "before", "because", "does",
        "did", "doing", "done", "got", "get", "gets", "let", "lets", "say", "said", "says", "use", "used", "using", "way", "ways",
        "like", "well", "now", "new", "own", "same", "both", "either", "neither", "while", "through", "between", "against",
        "during", "without", "within", "upon", "off", "too", "own", "yet", "here", "you'd", "you're", "i'm", "don't", "doesn't",
    };

    /// <summary>Words any Apply question uses whatever its situation (they say nothing about which situation it is).</summary>
    private static readonly HashSet<string> QuestionWording = new(StringComparer.Ordinal)
    {
        "imagine", "suppose", "situation", "scenario", "case", "example", "happen", "happens", "expect", "explain", "think",
        "reason", "reasoning", "apply", "applied", "applying", "concept", "idea", "predict", "describe", "tell",
        "seen", "before", "haven't", "never", "matter", "matters", "consider", "picture", "someone", "something", "person",
        "people", "help", "need", "want", "work", "result", "change", "changes", "probably", "likely",
    };
}
