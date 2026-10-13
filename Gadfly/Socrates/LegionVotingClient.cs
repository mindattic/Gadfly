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

    public LegionVotingClient(VotingConfiguration config)
    {
        this.config = config;
        http = new HttpClient { Timeout = config.ProviderTimeout };
        var provider = new LlmVotingProvider(http, config);
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

    public void Dispose() => http.Dispose();
}
