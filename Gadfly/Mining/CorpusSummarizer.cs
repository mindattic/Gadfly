using System.Text.Json;
using Gadfly.Ingestion;
using Gadfly.Socrates;
using MindAttic.Legion;

namespace Gadfly.Mining;

/// <summary>
/// Reduces "our entire conversation history" down to a short corpus digest that
/// fits in a single prompt's context budget. Classic map-reduce, no RAG: chunk the
/// human-authored corpus into character-bounded windows, summarize each chunk with
/// one LLM call, then merge chunk digests (plus <see cref="ClaimExtractor"/>'s hint
/// phrases) into one final digest. Cached by <c>CorpusRepository.CorpusHash()</c> so
/// repeat <c>interrogate</c>/<c>reflect</c>/<c>examine</c> runs on an unchanged corpus
/// skip re-summarizing entirely.
/// </summary>
public class CorpusSummarizer
{
    private const int ChunkCharBudget = 7_000;

    private readonly ISocraticVotingClient client;
    private readonly string cachePath;

    public CorpusSummarizer(ISocraticVotingClient client, string? cachePath = null)
    {
        this.client = client;
        this.cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MindAttic", "Gadfly", "corpus", "digest-cache.json");
    }

    public async Task<string> SummarizeAsync(
        IReadOnlyList<CorpusMessage> humanMessages,
        string corpusHash,
        IReadOnlyList<string> hintPhrases,
        CancellationToken ct = default)
    {
        var cache = LoadCache();
        if (cache.TryGetValue(corpusHash, out var cached))
            return cached;

        var chunkDigests = new List<string>();
        foreach (var chunk in ChunkByCharBudget(humanMessages, ChunkCharBudget))
        {
            var result = await client.AskAsync(
                question:
                    "Summarize the recurring topics, stated opinions, and self-descriptions in this excerpt " +
                    "of someone's own messages, in 5-8 bullet points. Be concrete — quote short phrases " +
                    "where useful. Do not psychoanalyze; just extract what was actually said.",
                context: string.Join("\n---\n", chunk.Select(m => m.Text)),
                quorum: Quorum.Plurality,
                ct: ct);
            chunkDigests.Add(PickText(result));
        }

        var hintsBlock = hintPhrases.Count > 0
            ? "Phrases that recur verbatim across multiple distinct sessions/days:\n" + string.Join("\n", hintPhrases.Select(p => $"- {p}"))
            : "";

        var reduceResult = await client.AskAsync(
            question:
                "Merge these chunk summaries of someone's own messages into one corpus digest of a few hundred " +
                "words: their most repeated opinions, self-descriptions, and stated beliefs, with the phrases " +
                "that recur most. This digest will be handed to a Socratic interrogator as 'everything we've " +
                "discussed so far' — make it dense and concrete, not vague.",
            context: string.Join("\n\n", chunkDigests) + "\n\n" + hintsBlock,
            quorum: Quorum.Plurality,
            ct: ct);

        var digest = PickText(reduceResult);
        cache[corpusHash] = digest;
        SaveCache(cache);
        return digest;
    }

    private static string PickText(VotingResult result) =>
        !string.IsNullOrWhiteSpace(result.Consensus) ? result.Consensus : result.NarrativeSummary;

    internal static List<List<CorpusMessage>> ChunkByCharBudget(IReadOnlyList<CorpusMessage> messages, int charBudget)
    {
        var chunks = new List<List<CorpusMessage>>();
        var current = new List<CorpusMessage>();
        var currentChars = 0;

        foreach (var message in messages)
        {
            if (currentChars > 0 && currentChars + message.Text.Length > charBudget)
            {
                chunks.Add(current);
                current = new List<CorpusMessage>();
                currentChars = 0;
            }
            current.Add(message);
            currentChars += message.Text.Length;
        }
        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }

    private Dictionary<string, string> LoadCache()
    {
        if (!File.Exists(cachePath)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(cachePath)) ?? new();
        }
        catch { return new(); }
    }

    private void SaveCache(Dictionary<string, string> cache)
    {
        var dir = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(cachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
    }
}
