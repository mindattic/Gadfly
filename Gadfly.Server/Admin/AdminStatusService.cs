using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gadfly.Server.Admin;

/// <summary>One outside-call name's timing over the window: how many, how long on average and at worst, and how often it succeeded.</summary>
public sealed record CallTimingStat(string CallName, int Count, double AvgMs, int MaxMs, double SuccessRate);

/// <summary>The organization's background job queue right now: how many are waiting or running, how long the oldest waiting
/// job has been queued, and how many failed in the window.</summary>
public sealed record JobQueueHealth(int PendingOrRunning, DateTimeOffset? OldestPendingSince, int FailedInWindow);

/// <summary>One of the organization's background jobs on the status page (GAD-T354).</summary>
public sealed record JobLine(Guid Id, string Kind, JobState State, int Attempts, int MaxAttempts, double Progress, string? ProgressMessage,
    string? Error, DateTimeOffset CreatedAt, DateTimeOffset RunAfter, DateTimeOffset? CompletedAt)
{
    public bool CanRetry => State is JobState.Failed or JobState.Cancelled;
    public bool CanCancel => State is JobState.Queued or JobState.Running;
}

/// <summary>One webhook delivery that has not gone through (GAD-T354).</summary>
public sealed record DeliveryLine(Guid Id, string Url, string EventType, DeliveryStatus Status, int Attempt, int? ResponseCode,
    DateTimeOffset CreatedAt, DateTimeOffset? NextAttemptAt);

/// <summary>The organization's webhook outbox: active endpoints, what is waiting, and what went through or gave up in the window.</summary>
public sealed record WebhookHealth(int ActiveEndpoints, int Pending, int Retrying, int DeliveredInWindow, int GaveUpInWindow, IReadOnlyList<DeliveryLine> Problems);

/// <summary>What the organization stores (GAD-T355): source documents and their size, exam sessions and transcript text.</summary>
public sealed record StorageUse(int Documents, long DocumentBytes, int Chunks, int Sessions, long TranscriptCharacters, int AuditEvents);

/// <summary>Everything the system status page shows for the organization (GAD-T302, GAD-T392): whether the database answers,
/// the background job queue, webhooks, storage, and the outside calls a live exam makes (the panel, the examiner's voice, the
/// recognizer), timed since GAD-T413.</summary>
public sealed record SystemStatusReport(
    DateTimeOffset GeneratedAt,
    TimeSpan Window,
    bool DatabaseHealthy,
    string? DatabaseDetail,
    JobQueueHealth Jobs,
    IReadOnlyList<CallTimingStat> CallTimings,
    int CallSampleSize)
{
    public IReadOnlyList<JobLine> JobList { get; init; } = [];
    public WebhookHealth? Webhooks { get; init; }
    public StorageUse? Storage { get; init; }
}

/// <summary>
/// The system status page's organization data (GAD-T302, GAD-T392; jobs and webhooks GAD-T354, storage GAD-T355). Read-only
/// apart from retrying or cancelling one of the organization's own jobs, each audited. Cheap enough for an admin to refresh at
/// will: the health check the app already runs for /healthz, a few aggregate queries, and one grouped query over the most recent
/// outside-call timings (GAD-T413) &#8212; no per-row queries, matching AdminDashboardService's style.
/// <para>
/// This does not (yet) show the full "student submits an answer" to "next question appears" span: that gap spans the session
/// engine itself (<c>Sessions/SessionOrchestrator.cs</c>), which a separate, live performance ticket (GAD-T404) is actively
/// changing. What's shown here &#8212; the panel's own steps (judge, analysis, question generation, aporia, verify) and the
/// examiner's voice (first byte and full audio) &#8212; is the outside-call portion of that same pause, already timed and
/// already the biggest part of it; the small remainder spent inside the engine is not yet broken out as its own metric.
/// </para>
/// </summary>
public sealed class AdminStatusService(GadflyDbContext db, ITenantContext tenant, HealthCheckService health, TimeProvider clock, JobQueue queue)
{
    /// <summary>Outside calls are grouped over the most recent of this many rows, so a busy organization stays fast.</summary>
    public const int CallSampleSize = 2000;

    /// <summary>Background job activity is summarized over this many hours back.</summary>
    public const int WindowHours = 24;

    /// <summary>At most this many jobs and deliveries are listed.</summary>
    public const int ListSize = 50;

    private Guid Org => tenant.OrganizationId ?? throw new InvalidOperationException("System status needs an active organization.");

    private void Authorize()
    {
        if (!AdminArea.IsOrgAdmin(tenant.Role) && !tenant.IsSystem && !tenant.IsSuperAdmin)
            throw new UnauthorizedAccessException("System status is for the organization's owners and admins.");
    }

    public async Task<SystemStatusReport> LoadAsync(CancellationToken ct = default)
    {
        Authorize();
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

        return new SystemStatusReport(now, window, dbEntry?.Status == HealthStatus.Healthy, dbEntry?.Description, jobs, timings, rows.Count)
        {
            JobList = await JobListAsync(org, since, ct),
            Webhooks = await WebhooksAsync(org, since, ct),
            Storage = await StorageAsync(org, ct),
        };
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

    /// <summary>The organization's waiting and running jobs, and those that failed or were cancelled in the window, newest first.</summary>
    private async Task<IReadOnlyList<JobLine>> JobListAsync(Guid org, DateTimeOffset since, CancellationToken ct) =>
        await db.Jobs.AsNoTracking()
            .Where(j => j.OrganizationId == org && (j.State == JobState.Queued || j.State == JobState.Running
                || ((j.State == JobState.Failed || j.State == JobState.Cancelled) && (j.CompletedAt ?? j.CreatedAt) >= since)))
            .OrderByDescending(j => j.CreatedAt).Take(ListSize)
            .Select(j => new JobLine(j.Id, j.Kind, j.State, j.Attempts, j.MaxAttempts, j.Progress, j.ProgressMessage, j.Error, j.CreatedAt, j.RunAfter, j.CompletedAt))
            .ToListAsync(ct);

    private async Task<WebhookHealth> WebhooksAsync(Guid org, DateTimeOffset since, CancellationToken ct)
    {
        var active = await db.Webhooks.AsNoTracking().CountAsync(w => w.OrganizationId == org && w.IsActive, ct);
        var deliveries = db.WebhookDeliveries.AsNoTracking().Where(d => d.OrganizationId == org);
        var pending = await deliveries.CountAsync(d => d.Status == DeliveryStatus.Pending, ct);
        var retrying = await deliveries.CountAsync(d => d.Status == DeliveryStatus.Failed, ct);
        var delivered = await deliveries.CountAsync(d => d.Status == DeliveryStatus.Delivered && d.CreatedAt >= since, ct);
        var gaveUp = await deliveries.CountAsync(d => d.Status == DeliveryStatus.GaveUp && d.CreatedAt >= since, ct);
        var problems = await (from d in deliveries
                              join w in db.Webhooks.AsNoTracking() on d.WebhookId equals w.Id
                              where d.Status == DeliveryStatus.Failed || (d.Status == DeliveryStatus.GaveUp && d.CreatedAt >= since)
                              orderby d.CreatedAt descending
                              select new DeliveryLine(d.Id, w.Url, d.EventType, d.Status, d.Attempt, d.ResponseCode, d.CreatedAt, d.NextAttemptAt))
            .Take(ListSize).ToListAsync(ct);
        return new WebhookHealth(active, pending, retrying, delivered, gaveUp, problems);
    }

    private async Task<StorageUse> StorageAsync(Guid org, CancellationToken ct)
    {
        var documents = await db.SourceDocuments.AsNoTracking().Where(d => d.OrganizationId == org)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Bytes = g.Sum(d => d.SizeBytes) }).FirstOrDefaultAsync(ct);
        var chunks = await db.SourceChunks.AsNoTracking().CountAsync(c => c.OrganizationId == org, ct);
        var sessions = await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Chars = g.Sum(s => (long)(s.TranscriptJson == null ? 0 : s.TranscriptJson.Length)) }).FirstOrDefaultAsync(ct);
        var audit = await db.AuditEvents.AsNoTracking().CountAsync(a => a.OrganizationId == org, ct);
        return new StorageUse(documents?.Count ?? 0, documents?.Bytes ?? 0, chunks, sessions?.Count ?? 0, sessions?.Chars ?? 0, audit);
    }

    /// <summary>Retries one of the organization's failed or cancelled jobs (GAD-T354), audited.</summary>
    public Task<string> RetryJobAsync(Guid jobId, CancellationToken ct = default) =>
        JobActionAsync(jobId, "admin.job.retried", j => j.State is JobState.Failed or JobState.Cancelled, queue.RetryAsync,
            "Queued to run again.", "Only a failed or cancelled job can be retried.", ct);

    /// <summary>Cancels one of the organization's waiting or running jobs (GAD-T354), audited.</summary>
    public Task<string> CancelJobAsync(Guid jobId, CancellationToken ct = default) =>
        JobActionAsync(jobId, "admin.job.cancelled", j => j.State is JobState.Queued or JobState.Running, queue.CancelAsync,
            "Cancelled. A running job stops at its next progress report.", "Only a waiting or running job can be cancelled.", ct);

    private async Task<string> JobActionAsync(Guid jobId, string action, Func<Job, bool> allowed, Func<Guid, CancellationToken, Task<bool>> act,
        string done, string refused, CancellationToken ct)
    {
        Authorize();
        var org = Org;
        // The job table is not tenant-filtered: the organization is named explicitly, so one org can never act on another's job.
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId && j.OrganizationId == org, ct);
        if (job is null) return "That job was not found in this organization.";
        if (!allowed(job) || !await act(jobId, ct)) return refused;
        new AssessmentGuard(db, tenant, clock).Audit(action, "Job", jobId.ToString(),
            new { job.Kind, State = job.State.ToString(), job.Attempts, job.Error }, null, $"From the system status page ({job.Kind}).");
        await db.SaveChangesAsync(ct);
        return done;
    }
}
