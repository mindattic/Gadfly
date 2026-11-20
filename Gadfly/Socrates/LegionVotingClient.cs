using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;
using MindAttic.Legion.Providers;

namespace Gadfly.Socrates;

/// <summary>Real <see cref="ISocraticVotingClient"/> backed by a live <see cref="LlmVotingService"/>.</summary>
public class LegionVotingClient : ISocraticVotingClient, IDisposable
{
    private readonly HttpClient http;
    private readonly VotingConfiguration config;
    private readonly LlmVotingService service;
    private readonly LlmVotingProvider provider;

    /// <summary>Per-provider failure tracking for the raw fan-out calls (GAD-T62).</summary>
    public ProviderHealth Health { get; } = new();

    public LegionVotingClient(VotingConfiguration config)
    {
        this.config = config;
        http = new HttpClient { Timeout = config.ProviderTimeout };
        provider = new LlmVotingProvider(http, config);
        service = new LlmVotingService(provider, config, NullLogger<LlmVotingService>.Instance);
    }

    public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default) =>
        service.VoteAsync(new VoteRequest { Question = question, Context = context, SynthesizeNarrative = true }, quorum, ct);

    public Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default) =>
        service.VoteWithProfilesAsync(
            new VoteRequest { Question = question, Context = context, SynthesizeNarrative = false },
            Quorum.Plurality,
            voters,
            ct);

    public Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default) =>
        service.DecideAsync(question, options, context, Quorum.Plurality, ct: ct);

    public async Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096,
        double temperature = 0.2, CancellationToken ct = default)
    {
        var ids = config.ActiveProviderIds.ToList();
        if (ids.Count == 0) return [];
        var calls = ids.Select(async id =>
        {
            try
            {
                var text = await provider.CallAsync(id, systemPrompt, userMessage, maxTokens, temperature, null, ct);
                Health.RecordSuccess(id, DateTimeOffset.UtcNow);
                return new PanelReply(id, text ?? "");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Health.RecordFailure(id, ex.Message, DateTimeOffset.UtcNow);
                return new PanelReply(id, "", ex.Message);
            }
        });
        return await Task.WhenAll(calls);
    }

    public void Dispose() => http.Dispose();
}
