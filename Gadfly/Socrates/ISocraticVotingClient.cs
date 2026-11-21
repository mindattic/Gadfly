using MindAttic.Legion;

namespace Gadfly.Socrates;

/// <summary>
/// Thin seam over MindAttic.Legion's <see cref="LlmVotingService"/>. Everything in
/// Gadfly that needs an LLM call — <see cref="Gadfly.Mining.CorpusSummarizer"/>, the
/// interrogation loop, the reflection and master-examination runners — depends on
/// this interface, not directly on <see cref="LlmVotingService"/>, so the turn loop,
/// Aporia detection, and transcript persistence can be driven by a fake client in
/// tests instead of making live network calls.
/// </summary>
public interface ISocraticVotingClient
{
    /// <summary>Vote across the configured panel, free-form, with a judge-synthesized consensus.</summary>
    Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default);

    /// <summary>Vote using specific named voter profiles — used for Prompt 3's three perspectives,
    /// where each individual vote (not the synthesized consensus) is the point.</summary>
    Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default);

    /// <summary>Panel picks exactly one of <paramref name="options"/> (Legion's DecideAsync). An empty
    /// <see cref="DecisionResult.Choice"/> means no quorum was reached.</summary>
    Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default);

    /// <summary>
    /// Sends the same prompt to every active panel provider in parallel and returns each raw reply,
    /// unparsed. For structured (JSON) work — concept maps, rubrics, judging — where Gadfly aggregates
    /// the panel itself instead of going through Legion's ballot format.
    /// </summary>
    Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096,
        double temperature = 0.2, CancellationToken ct = default);
}

/// <summary>One provider's raw reply from <see cref="ISocraticVotingClient.FanOutAsync"/>.</summary>
public sealed record PanelReply(string ProviderId, string Text, string? Error = null, string Model = "")
{
    public bool IsError => Error is not null;
}
