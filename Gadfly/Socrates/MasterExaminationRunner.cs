using Gadfly.Prompts;
using MindAttic.Legion;

namespace Gadfly.Socrates;

public class PerspectiveResult
{
    public string Perspective { get; init; } = "";
    public string ProviderId { get; init; } = "";
    public string Text { get; init; } = "";
}

/// <summary>
/// Runs Prompt 3 (The Master Prompt) one-shot across exactly three named
/// perspectives — Socrates, Confucius, Buddha — reading each individual vote
/// directly rather than a synthesized consensus, since the point is three
/// distinct voices, not one merged answer.
/// </summary>
public class MasterExaminationRunner
{
    private static readonly string[] DefaultProviders = { "claude", "openai", "gemini" };

    private readonly ISocraticVotingClient client;

    public MasterExaminationRunner(ISocraticVotingClient client) => this.client = client;

    /// <summary>Runs Prompt 3. <paramref name="providerOverride"/>, if given, must have exactly 3
    /// provider ids for Socrates, Confucius, Buddha in that order; otherwise the default
    /// claude/openai/gemini assignment is used.</summary>
    public async Task<IReadOnlyList<PerspectiveResult>> RunAsync(
        string corpusDigest,
        IReadOnlyList<string>? providerOverride = null,
        CancellationToken ct = default)
    {
        var providers = providerOverride is { Count: 3 } ? providerOverride : DefaultProviders;

        var voters = new List<VoterProfile>
        {
            new() { Name = "Socrates", ProviderId = providers[0], PersonalityMarkdown = Personas.Socrates },
            new() { Name = "Confucius", ProviderId = providers[1], PersonalityMarkdown = Personas.Confucius },
            new() { Name = "Buddha", ProviderId = providers[2], PersonalityMarkdown = Personas.Buddha },
        };

        var result = await client.AskWithProfilesAsync(CanonicalPrompts.MasterPrompt, corpusDigest, voters, ct);

        return result.IndividualVotes.Select(v => new PerspectiveResult
        {
            Perspective = v.VoterName,
            ProviderId = v.ProviderId,
            Text = !string.IsNullOrWhiteSpace(v.Decision) ? v.Decision : v.Reasoning,
        }).ToList();
    }
}
