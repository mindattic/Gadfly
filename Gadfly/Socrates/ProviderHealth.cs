using System.Collections.Concurrent;

namespace Gadfly.Socrates;

/// <summary>One provider's recent record.</summary>
public sealed record ProviderStatus(string ProviderId, int ConsecutiveFailures, int TotalCalls, int TotalFailures,
    DateTimeOffset? LastFailureAt, string? LastError, DateTimeOffset? LastSuccessAt)
{
    public bool Failing => ConsecutiveFailures >= ProviderHealth.FailingAfter;
}

/// <summary>
/// Tracks panel provider failures (GAD-T62), so an out-of-credit key or a retired model shows up as
/// a named, alertable condition instead of quietly shrinking the panel. Thread-safe.
/// </summary>
public sealed class ProviderHealth
{
    /// <summary>Consecutive failures after which a provider counts as failing.</summary>
    public const int FailingAfter = 3;

    private readonly ConcurrentDictionary<string, ProviderStatus> _status = new(StringComparer.Ordinal);

    /// <summary>Raised when a provider crosses into failing (once per streak).</summary>
    public event Action<ProviderStatus>? BecameFailing;

    public void RecordSuccess(string providerId, DateTimeOffset at) =>
        _status.AddOrUpdate(providerId,
            _ => new ProviderStatus(providerId, 0, 1, 0, null, null, at),
            (_, s) => s with { ConsecutiveFailures = 0, TotalCalls = s.TotalCalls + 1, LastSuccessAt = at });

    public void RecordFailure(string providerId, string error, DateTimeOffset at)
    {
        var updated = _status.AddOrUpdate(providerId,
            _ => new ProviderStatus(providerId, 1, 1, 1, at, error, null),
            (_, s) => s with
            {
                ConsecutiveFailures = s.ConsecutiveFailures + 1,
                TotalCalls = s.TotalCalls + 1,
                TotalFailures = s.TotalFailures + 1,
                LastFailureAt = at,
                LastError = error.Length > 500 ? error[..500] : error,
            });
        if (updated.ConsecutiveFailures == FailingAfter) BecameFailing?.Invoke(updated);
    }

    public IReadOnlyList<ProviderStatus> Snapshot() => _status.Values.OrderBy(s => s.ProviderId, StringComparer.Ordinal).ToList();
}
