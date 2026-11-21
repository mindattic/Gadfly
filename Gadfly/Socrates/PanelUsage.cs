namespace Gadfly.Socrates;

/// <summary>One LLM call made for the panel, as far as Gadfly can see it.</summary>
/// <param name="Estimated">True when the sizes are character counts converted to tokens (chars/4), not provider-reported.</param>
public sealed record PanelCall(string ProviderId, string Model, long InputTokens, long OutputTokens, string Kind, bool Estimated = true);

/// <summary>
/// Collects the panel calls made inside a unit of work (an exam step, a processing job) so the host can
/// meter them per organization and session (GAD-T47). Ambient (AsyncLocal), so engine code needs no
/// plumbing: <c>using (PanelUsage.Track(out var calls)) { ... }</c>. Calls made outside a tracking scope
/// are not recorded. Token counts are estimates until MindAttic.Legion reports real usage (M.LEGN-T50).
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

    /// <summary>Records a call into the current tracking scope, if any. Thread-safe within one scope.</summary>
    public static void Record(PanelCall call)
    {
        var list = Current.Value;
        if (list is null) return;
        lock (list) list.Add(call);
    }

    /// <summary>Rough token estimate from characters (about four characters per token in English).</summary>
    public static long Tokens(long chars) => (chars + 3) / 4;

    private sealed class Scope(List<PanelCall>? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
