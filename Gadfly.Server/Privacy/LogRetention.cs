using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Privacy;

/// <summary>
/// How long Gadfly's logs are kept (GAD-T466), separately from the organization's transcript retention period.
/// <para>
/// The EU AI Act asks providers (Art. 19) and deployers (Art. 26(6)) of a high-risk system to keep its automatically
/// generated logs for at least six months. Gadfly's logs are the audit log (<c>gadfly.AuditEvents</c>) and the per-call
/// timings (<c>gadfly.ApiCallTimings</c>, GAD-T413). The organization's retention period (as short as 7 days) removes what
/// the student said, including the words copied into answer and clarification audit events, but never removes a log row:
/// </para>
/// <list type="bullet">
/// <item>Audit rows are never deleted by any job. The audit log is append-only; retention and erasure only clear the
/// student's words and grades from the detail they carry.</item>
/// <item>Call-timing rows are deleted only once they are older than <see cref="CallTimingDays"/>, which can never be set
/// below <see cref="MinimumDays"/>.</item>
/// </list>
/// Whether that reading of Arts. 12, 19 and 26(6) is right, and whether six months is enough where national law asks for
/// more, is for counsel (docs/COMPLIANCE.md).
/// </summary>
public static class LogRetentionPolicy
{
    /// <summary>The floor: six months, counted generously (184 days, the longest six calendar months), so no setting can make a log shorter.</summary>
    public const int MinimumDays = 184;

    /// <summary>How long call timings are kept when <c>Gadfly:Logs:CallTimingDays</c> is not set: a year.</summary>
    public const int DefaultCallTimingDays = 365;

    /// <summary>The configuration key for the call-timing period, in days.</summary>
    public const string CallTimingDaysKey = "Gadfly:Logs:CallTimingDays";

    /// <summary>The call-timing period actually used: the configured one, never below <see cref="MinimumDays"/>.</summary>
    public static int CallTimingDays(int? configured) => Math.Max(configured is > 0 ? configured.Value : DefaultCallTimingDays, MinimumDays);

    /// <summary>Call timings that started before this moment may be deleted.</summary>
    public static DateTimeOffset CallTimingCutoff(DateTimeOffset now, int? configured) => now.AddDays(-CallTimingDays(configured));

    /// <summary>
    /// Whether a log row of this age may be removed, whatever the organization's transcript retention period says. The
    /// transcript period is deliberately not an input: it never shortens a log.
    /// </summary>
    public static bool MayDelete(DateTimeOffset loggedAt, DateTimeOffset now, int? configured) => loggedAt < CallTimingCutoff(now, configured);
}

/// <summary>Applies <see cref="LogRetentionPolicy"/> (GAD-T466): run by the daily "privacy.retention" job.</summary>
public sealed class LogRetention(GadflyDbContext db, IConfiguration config, TimeProvider clock, ILogger<LogRetention> log)
{
    /// <summary>Deletes call-timing rows older than the call-timing period (never under six months). Returns rows deleted.</summary>
    public async Task<int> PruneCallTimingsAsync(CancellationToken ct = default)
    {
        var configured = config.GetValue<int?>(LogRetentionPolicy.CallTimingDaysKey);
        var cutoff = LogRetentionPolicy.CallTimingCutoff(clock.GetUtcNow(), configured);
        var n = await db.ApiCallTimings.Where(t => t.StartedAtUtc < cutoff).ExecuteDeleteAsync(ct);
        if (n > 0)
            log.LogInformation("Log retention: deleted {Count} call timing row(s) older than {Days} days.", n, LogRetentionPolicy.CallTimingDays(configured));
        return n;
    }
}
