using Gadfly.Socrates;
using MindAttic.Legion;

namespace Gadfly.Tests.Fakes;

/// <summary>
/// Canned-response <see cref="ISocraticVotingClient"/> for driving
/// <see cref="InterrogationSession"/>, <see cref="Gadfly.Mining.CorpusSummarizer"/>,
/// and the one-shot runners without a live network call.
/// </summary>
public class FakeSocraticVotingClient : ISocraticVotingClient
{
    private readonly Queue<string> responses;

    public List<(string Question, string Context)> Calls { get; } = new();

    public FakeSocraticVotingClient(IEnumerable<string> responses) => this.responses = new Queue<string>(responses);

    public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default)
    {
        Calls.Add((question, context));
        var answer = responses.Count > 0 ? responses.Dequeue() : "";
        return Task.FromResult(new VotingResult { Question = question, QuorumType = quorum, QuorumReached = true, Consensus = answer });
    }

    public Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default)
    {
        Calls.Add((question, context));
        var votes = voters.Select(v => new VoteResult
        {
            VoterName = v.Name,
            ProviderId = v.ProviderId,
            Decision = responses.Count > 0 ? responses.Dequeue() : $"{v.Name} has nothing to say.",
        }).ToList();
        return Task.FromResult(new VotingResult { Question = question, QuorumReached = true, IndividualVotes = votes });
    }
}
