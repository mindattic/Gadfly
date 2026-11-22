using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gadfly.Server.Admin;

/// <summary>One outside-call name's timing over the window: how many, how long on average and at worst, and how often it succeeded.</summary>
public sealed record CallTimingStat(string CallName, int Count, double AvgMs, int MaxMs, double SuccessRate);

/// <summary>The organization's background job queue right now: how many are waiting or running, how long the oldest waiting
/// job has been queued, and how many failed in the window.</summary>
public sealed record JobQueueHealth(int PendingOrRunning, DateTimeOffset? OldestPendingSince, int FailedInWindow);

/// <summary>Everything the system status page shows (GAD-T302, GAD-T392): whether the database answers, the background job
/// queue, and the outside calls a live exam makes (the panel, the examiner's voice, the recognizer), timed since GAD-T413.</summary>
public sealed record SystemStatusReport(
    DateTimeOffset GeneratedAt,
    TimeSpan Window,
    bool DatabaseHealthy,
    string? DatabaseDetail,
    JobQueueHealth Jobs,
    IReadOnlyList<CallTimingStat> CallTimings,
    int CallSampleSize);

/// <summary>
/// The system status page's data (GAD-T302, GAD-T392). Read-only, and cheap enough for an admin to refresh at will: the health
/// check the app already runs for /healthz, one pair of aggregate queries over the job table, and one grouped query over the
/// most recent outside-call timings (GAD-T413) &#8212; no per-row queries, matching AdminDashboardService's style.
/// <para>
/// This does not (yet) show the full "student submits an answer" to "next question appears" span: that gap spans the session
/// engine itself (<c>Sessions/SessionOrchestrator.cs</c>), which a separate, live performance ticket (GAD-T404) is actively
/// changing. What's shown here &#8212; the panel's own steps (judge, analysis, question generation, aporia, verify) and the
/// examiner's voice (first byte and full audio) &#8212; is the outside-call portion of that same pause, already timed and
/// already the biggest part of it; the small remainder spent inside the engine is not yet broken out as its own metric.
/// </para>
/// </summary>
public sealed class AdminStatusService(GadflyDbContext db, ITenantContext tenant, HealthCheckService health, TimeProvider clock)
{
    /// <summary>Outside calls are grouped over the most recent of this many rows, so a busy organization stays fast.</summary>
    public const int CallSampleSize = 2000;

    /// <summary>Background job activity is summarized over this many hours back.</summary>
    public const int WindowHours = 24;

    private Guid Org => tenant.OrganizationId ?? throw new InvalidOperationException("System status needs an active organization.");

    public async Task<SystemStatusReport> LoadAsync(CancellationToken ct = default)
    {
        if (!AdminArea.IsOrgAdmin(tenant.Role) && !tenant.IsSystem)
            throw new UnauthorizedAccessException("System status is for the organization's owners and admins.");
        var org = Org;
        var now = clock.GetUtcNow();
        var window = TimeSpan.FromHours(WindowHours);
        var since = now - window;

        var healthReport = await health.CheckHealthAsync(ct);
        var dbEntry = healthReport.Entries.TryGetValue("database", out var e) ? (HealthReportEntry?)e : null;

        var jobs = await JobsAsync(org, since, ct);

        var rows = await db.ApiCallTimings.AsNoTracking()
            .Where(t => t.OrganizationId == org && t.StartedAtUtc >= since)
            .OrderByDescending(t => t.StartedAtUtc)
            .Take(CallSampleSize)
            .Select(t => new { t.CallName, t.DurationMs, t.Succeeded })
            .ToListAsync(ct);
        var timings = rows.GroupBy(r => r.CallName)
            .Select(g => new CallTimingStat(g.Key, g.Count(), g.Average(r => r.DurationMs), g.Max(r => r.DurationMs), g.Count(r => r.Succeeded) / (double)g.Count()))
            .OrderByDescending(s => s.AvgMs)
            .ToList();

        return new SystemStatusReport(now, window, dbEntry?.Status == HealthStatus.Healthy, dbEntry?.Description, jobs, timings, rows.Count);
    }

    private async Task<JobQueueHealth> JobsAsync(Guid org, DateTimeOffset since, CancellationToken ct)
    {
        // The job table is not tenant-filtered (GAD-T43): every query here names the organization explicitly.
        var pending = await db.Jobs.AsNoTracking()
            .CountAsync(j => j.OrganizationId == org && (j.State == JobState.Queued || j.State == JobState.Running), ct);
        var oldest = await db.Jobs.AsNoTracking()
            .Where(j => j.OrganizationId == org && j.State == JobState.Queued)
            .OrderBy(j => j.CreatedAt)
            .Select(j => (DateTimeOffset?)j.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var failed = await db.Jobs.AsNoTracking()
            .CountAsync(j => j.OrganizationId == org && j.State == JobState.Failed && (j.CompletedAt ?? j.CreatedAt) >= since, ct);
        return new JobQueueHealth(pending, oldest, failed);
    }
}
