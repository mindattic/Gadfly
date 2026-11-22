namespace Gadfly.Socrates;

/// <summary>One LLM call made for the panel, as far as Gadfly can see it.</summary>
/// <param name="Estimated">True when the sizes are character counts converted to tokens (chars/4), not provider-reported.</param>
public sealed record PanelCall(string ProviderId, string Model, long InputTokens, long OutputTokens, string Kind, bool Estimated = true);

/// <summary>
/// Collects the panel calls made inside a unit of work (an exam step, a processing job) so the host can
/// meter them per organization and session (GAD-T47). Ambient (AsyncLocal), so engine code needs no
/// plumbing: <c>using (PanelUsage.Track(out var calls)) { ... }</c>. Calls made outside a tracking scope
/// are not recorded. Token counts are the provider-reported figures from MindAttic.Legion (<see cref="CaptureReal"/>, Estimated=false) when
/// the provider returned usage, and character estimates otherwise (GAD-T154).
/// </summary>
public static class PanelUsage
{
    private static readonly AsyncLocal<List<PanelCall>?> Current = new();

    public static IDisposable Track(out IReadOnlyList<PanelCall> calls)
    {
        var list = new List<PanelCall>();
        var previous = Current.Value;
        Current.Value = list;
        calls = list;
        return new Scope(previous);
    }

    private static readonly AsyncLocal<string?> LiveKind = new();

    /// <summary>
    /// The kind a single live-model call is metered as (GAD-T508): "live-turn" (the question and its leak guard) unless a caller
    /// names another, e.g. "live-judge" for the provisional judgment, so the two are told apart in usage and audit. Applied in
    /// <see cref="Record"/> to any call recorded as "live-turn", so every live-model client is covered.
    /// </summary>
    public static string LiveCallKind => LiveKind.Value ?? "live-turn";

    /// <summary>Meters the live-model calls made until disposal as <paramref name="kind"/> (see <see cref="LiveCallKind"/>).</summary>
    public static IDisposable UseLiveKind(string kind)
    {
        var previous = LiveKind.Value;
        LiveKind.Value = kind;
        return new KindScope(previous);
    }

    private sealed class KindScope(string? previous) : IDisposable
    {
        public void Dispose() => LiveKind.Value = previous;
    }

    /// <summary>Records a call into the current tracking scope, if any. Thread-safe within one scope.</summary>
    public static void Record(PanelCall call)
    {
        var list = Current.Value;
        if (list is null) return;
        // A live-model call made under UseLiveKind (the provisional judgment, GAD-T508) is metered as that kind, whichever client made it.
        if (LiveKind.Value is { } kind && call.Kind == "live-turn") call = call with { Kind = kind };
        lock (list) list.Add(call);
    }

    /// <summary>
    /// Captures Legion's provider-reported usage for the calls made until the result is disposed, recording each as a
    /// <see cref="PanelCall"/> with Estimated=false in the current tracking scope. <see cref="RealUsageCapture.Count"/>
    /// says whether anything real arrived, so the caller can fall back to a character estimate when it did not (GAD-T154).
    /// Input tokens include any cached input, so cost is a slight overestimate when the provider cached.
    /// </summary>
    public static RealUsageCapture CaptureReal(string kind) => new(kind);

    /// <summary>Rough token estimate from characters (about four characters per token in English).</summary>
    public static long Tokens(long chars) => (chars + 3) / 4;

    /// <summary>A scope that turns Legion usage reports into non-estimated panel calls.</summary>
    public sealed class RealUsageCapture : IDisposable
    {
        private readonly IDisposable legion;
        private int count;

        internal RealUsageCapture(string kind)
        {
            legion = MindAttic.Legion.LegionUsage.Track(new MindAttic.Legion.DelegateUsageSink(u =>
            {
                Interlocked.Increment(ref count);
                Record(new PanelCall(u.Provider, u.Model, u.InputTokens + u.CacheReadTokens + u.CacheWriteTokens, u.OutputTokens, kind, Estimated: false));
            }));
        }

        /// <summary>How many provider calls reported real usage so far.</summary>
        public int Count => Volatile.Read(ref count);

        public void Dispose() => legion.Dispose();
    }

    private sealed class Scope(List<PanelCall>? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
