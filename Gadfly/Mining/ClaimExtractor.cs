using Gadfly.Ingestion;

namespace Gadfly.Mining;

/// <summary>
/// Pure, local, no-LLM heuristic that finds phrases a person's own messages repeat
/// across distinct sessions or days. This is <em>not</em> the belief-detector itself —
/// Prompt 1 asks the LLM panel to do that job, deliberately — it only produces ranked
/// hint phrases that seed and bias <see cref="CorpusSummarizer"/>'s digest.
/// </summary>
public static class ClaimExtractor
{
    private const int MinWordsPerPhrase = 4;
    private const int MaxWordsPerPhrase = 8;

    /// <summary>Returns up to <paramref name="topN"/> word-sequences (4-8 words) that recur
    /// verbatim across at least two distinct session/day occurrences, most-recurring first.</summary>
    public static IReadOnlyList<string> ExtractRecurringPhrases(IReadOnlyList<CorpusMessage> humanMessages, int topN = 20)
    {
        var occurrences = new Dictionary<string, HashSet<string>>();

        foreach (var message in humanMessages)
        {
            var occurrenceKey = $"{message.SessionId}|{message.TimestampUtc:yyyy-MM-dd}";
            foreach (var phrase in ExtractPhrases(message.Text))
            {
                if (!occurrences.TryGetValue(phrase, out var keys))
                {
                    keys = new HashSet<string>();
                    occurrences[phrase] = keys;
                }
                keys.Add(occurrenceKey);
            }
        }

        return occurrences
            .Where(kv => kv.Value.Count >= 2)
            .OrderByDescending(kv => kv.Value.Count)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(topN)
            .Select(kv => kv.Key)
            .ToList();
    }

    internal static IEnumerable<string> ExtractPhrases(string text)
    {
        var words = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var len = MinWordsPerPhrase; len <= MaxWordsPerPhrase; len++)
        {
            for (var start = 0; start + len <= words.Length; start++)
                yield return string.Join(' ', words.Skip(start).Take(len));
        }
    }

    internal static string Normalize(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ');
        var collapsed = string.Concat(chars);
        return string.Join(' ', collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
