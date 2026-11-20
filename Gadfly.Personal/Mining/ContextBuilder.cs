using Gadfly.Corpus;

namespace Gadfly.Mining;

/// <summary>
/// Assembles the final <c>VoteRequest.Context</c> string handed to all three canonical
/// prompts: the corpus digest (<see cref="ClaimExtractor"/> + <see cref="CorpusSummarizer"/>,
/// cached), optionally folded together with a running interrogation-dialogue history for
/// long <c>gadfly interrogate</c> sessions.
/// </summary>
public class ContextBuilder
{
    private readonly CorpusRepository repository;
    private readonly CorpusSummarizer summarizer;

    public ContextBuilder(CorpusRepository repository, CorpusSummarizer summarizer)
    {
        this.repository = repository;
        this.summarizer = summarizer;
    }

    public async Task<string> BuildCorpusDigestAsync(CancellationToken ct = default)
    {
        var humanMessages = repository.GetHumanMessages();
        var hints = ClaimExtractor.ExtractRecurringPhrases(humanMessages);
        var hash = repository.CorpusHash();
        return await summarizer.SummarizeAsync(humanMessages, hash, hints, ct);
    }

    /// <summary>Folds the corpus digest together with the running interrogation dialogue (verbatim
    /// so far this session) into one context string for the next turn of <c>gadfly interrogate</c>.</summary>
    public static string BuildInterrogationContext(string corpusDigest, IReadOnlyList<(string Speaker, string Text)> dialogueSoFar)
    {
        var dialogueBlock = dialogueSoFar.Count > 0
            ? "\n\nDIALOGUE SO FAR THIS SESSION:\n" + string.Join("\n", dialogueSoFar.Select(t => $"{t.Speaker}: {t.Text}"))
            : "";
        return $"CORPUS DIGEST (recurring beliefs/opinions from the person's own conversation history):\n{corpusDigest}{dialogueBlock}";
    }
}
