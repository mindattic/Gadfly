using System.Runtime.CompilerServices;
using MindAttic.Legion;

namespace Gadfly.Socrates.Mock;

/// <summary>
/// Holds one exam session to the AI mode it started in (GAD-T802), so a switch between mock and live made while it runs never
/// leaves it half one and half the other. <see cref="SelectedPanel"/> and the live-turn model follow <see cref="Mock"/> when it is
/// set, the server-wide mode otherwise. It is set only by <see cref="PinnedPanel"/> and <see cref="PinnedLiveTurnModel"/> for the
/// length of one call (and the work that call starts), then restored.
/// </summary>
public static class PanelPin
{
    private static readonly AsyncLocal<bool?> Pinned = new();

    /// <summary>The mode the current call is held to (true = mock), or null when it follows the server-wide mode.</summary>
    public static bool? Mock => Pinned.Value;

    /// <summary>Sets the pin and returns the one it replaced, for the caller to put back.</summary>
    internal static bool? Swap(bool? mock)
    {
        var before = Pinned.Value;
        Pinned.Value = mock;
        return before;
    }

    /// <summary>
    /// <paramref name="panel"/> held to a session's recorded mode (<see cref="PanelModes.Live"/> or <see cref="PanelModes.Mock"/>);
    /// <paramref name="panel"/> itself when the session recorded none (it then follows the server-wide mode).
    /// </summary>
    public static ISocraticVotingClient For(ISocraticVotingClient panel, string? mode) =>
        IsMockMode(mode) is { } mock ? new PinnedPanel(panel, mock) : panel;

    /// <summary>The live-turn model held to a session's recorded mode, as <see cref="For(ISocraticVotingClient, string?)"/>.</summary>
    public static ILiveTurnModel? For(ILiveTurnModel? model, string? mode) =>
        model is not null && IsMockMode(mode) is { } mock ? new PinnedLiveTurnModel(model, mock) : model;

    /// <summary>True for "mock", false for "live", null for anything else (no recorded mode).</summary>
    public static bool? IsMockMode(string? mode) => mode switch
    {
        PanelModes.Mock => true,
        PanelModes.Live => false,
        _ => null,
    };
}

/// <summary>A panel held to one mode (GAD-T802): every call runs pinned, and it reports that mode as its own.</summary>
public sealed class PinnedPanel(ISocraticVotingClient inner, bool mock) : ISocraticVotingClient, IPanelInfo, IPanelModels
{
    public ISocraticVotingClient Inner => inner;
    public bool IsMock => mock;
    public string Mode => mock ? PanelModes.Mock : PanelModes.Live;
    public string Reason => $"This exam session started in {Mode} mode and keeps it.";

    public IReadOnlyDictionary<string, string> ConfiguredModels
    {
        get
        {
            var before = PanelPin.Swap(mock);
            try { return inner is IPanelModels m ? m.ConfiguredModels : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
            finally { PanelPin.Swap(before); }
        }
    }

    // Async methods: the pin set here is visible to the inner call and restored when each returns.
    public async Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default)
    {
        PanelPin.Swap(mock);
        return await inner.AskAsync(question, context, quorum, ct);
    }

    public async Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default)
    {
        PanelPin.Swap(mock);
        return await inner.AskWithProfilesAsync(question, context, voters, ct);
    }

    public async Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default)
    {
        PanelPin.Swap(mock);
        return await inner.DecideAsync(question, options, context, ct);
    }

    public async Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096,
        double temperature = 0.2, CancellationToken ct = default)
    {
        PanelPin.Swap(mock);
        return await inner.FanOutAsync(systemPrompt, userMessage, maxTokens, temperature, ct);
    }
}

/// <summary>A live-turn model held to one mode (GAD-T802), like <see cref="PinnedPanel"/>.</summary>
public sealed class PinnedLiveTurnModel(ILiveTurnModel inner, bool mock) : ILiveTurnModel
{
    public string ProviderId => Pinned(() => inner.ProviderId);
    public string Model => Pinned(() => inner.Model);

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Pinned again before every step: an iterator resumes on its caller's context, not the one it left.
        PanelPin.Swap(mock);
        await using var e = inner.StreamAsync(systemPrompt, userMessage, maxTokens, temperature, ct).GetAsyncEnumerator(ct);
        while (true)
        {
            PanelPin.Swap(mock);
            if (!await e.MoveNextAsync()) yield break;
            yield return e.Current;
        }
    }

    private T Pinned<T>(Func<T> read)
    {
        var before = PanelPin.Swap(mock);
        try { return read(); }
        finally { PanelPin.Swap(before); }
    }
}
