namespace Gadfly.Socrates;

/// <summary>
/// One panel provider's call inside a fan-out, as <see cref="LegionVotingClient"/> saw it (GAD-T413): which provider, when it
/// started, how long it took and whether it answered. A fan-out waits for its slowest provider, so this is what says which one.
/// </summary>
public sealed record ProviderCallTiming(string ProviderId, DateTimeOffset StartedAt, long DurationMs, bool Succeeded, string? Error);

/// <summary>
/// Where per-provider call timings go (GAD-T413). The host sets <see cref="Observer"/> once at startup; with none set, timing
/// costs nothing. The observer runs on the calling thread and must not throw or block.
/// </summary>
public static class PanelTimings
{
    public static Action<ProviderCallTiming>? Observer { get; set; }

    internal static void Report(ProviderCallTiming timing)
    {
        try { Observer?.Invoke(timing); }
        catch { /* timing must never fail a panel call */ }
    }
}
