using Gadfly.Prompts;
using MindAttic.Legion;

namespace Gadfly.Socrates;

/// <summary>Runs Prompt 2 (The Final Reflection) one-shot, against either the corpus
/// digest alone or a prior interrogation transcript plus the digest.</summary>
public class ReflectionRunner
{
    private readonly ISocraticVotingClient client;

    public ReflectionRunner(ISocraticVotingClient client) => this.client = client;

    public async Task<string> RunAsync(
        string corpusDigest,
        IReadOnlyList<InterrogationTurn>? priorInterrogation = null,
        CancellationToken ct = default)
    {
        var context = corpusDigest;
        if (priorInterrogation is { Count: > 0 })
        {
            context += "\n\nINTERROGATION TRANSCRIPT:\n" +
                string.Join("\n", priorInterrogation.Select(t => $"{t.Speaker}: {t.Text}"));
        }

        var result = await client.AskAsync(CanonicalPrompts.FinalReflection, context, Quorum.Plurality, ct);
        return !string.IsNullOrWhiteSpace(result.Consensus) ? result.Consensus : result.NarrativeSummary;
    }
}
