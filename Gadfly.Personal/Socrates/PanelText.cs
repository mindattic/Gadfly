using MindAttic.Legion;

namespace Gadfly.Socrates;

/// <summary>The text a panel vote produced, or null when the panel failed (Legion reports a failure as status wording such as "All voters failed.", not as an error).</summary>
internal static class PanelText
{
    public static string? Of(VotingResult result)
    {
        if (result.IndividualVotes.Count > 0 && result.IndividualVotes.All(v => v.IsError)) return null;
        if (result.QuorumReached && !string.IsNullOrWhiteSpace(result.Consensus)) return result.Consensus;
        return string.IsNullOrWhiteSpace(result.NarrativeSummary) ? null : result.NarrativeSummary;
    }
}
