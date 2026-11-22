using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Lti;

/// <summary>
/// Queues one "lti.roster-refresh" job every hour when none is waiting (GAD-T458), the same shape as the reminder and retention
/// schedulers: the job queue does the work, so several app instances never refresh the same roster twice at once.
/// </summary>
public sealed class LtiRosterRefreshScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<LtiRosterRefreshScheduler> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken); }   // let start-up (migrations) finish first
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateSystemScope();
                var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                var waiting = await db.Jobs.AnyAsync(j => j.Kind == LtiRosterRefreshJob.JobKind && (j.State == JobState.Queued || j.State == JobState.Running), stoppingToken);
                var due = !waiting && await LtiRosterRefreshJob.DueQuery(db, clock.GetUtcNow()).AnyAsync(stoppingToken);
                if (due) await scope.ServiceProvider.GetRequiredService<JobQueue>().EnqueueAsync(LtiRosterRefreshJob.JobKind, new { }, null, maxAttempts: 3, ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Could not queue the LTI roster refresh job."); }
            try { await Task.Delay(Interval, clock, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}

/// <summary>
/// The "lti.roster-refresh" job (GAD-T458): syncs every placement whose roster is due, that is whose connection is on, refreshes
/// on a schedule (<see cref="LtiRegistration.RosterRefreshHours"/> above 0, 24 by default) and was last synced longer ago than
/// that (or never, once a launch has told Gadfly the roster's address). Each sync runs in a scope acting for its own
/// organization, exactly as the admin's "Sync roster" button does, and is audited the same way (with no person as the actor).
/// One placement failing (the LMS refusing, unreachable) is logged and audited as <see cref="FailedAction"/> with the reason,
/// which the admin page shows beside the placement, and it waits for the next interval instead of being retried every hour.
/// </summary>
public sealed class LtiRosterRefreshJob(GadflyDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<LtiRosterRefreshJob> log) : IJobHandler
{
    public const string JobKind = "lti.roster-refresh";
    /// <summary>The audit action of a scheduled refresh that failed; a later successful sync ("lti.roster-synced") supersedes it.</summary>
    public const string FailedAction = "lti.roster-refresh-failed";
    public string Kind => JobKind;

    /// <summary>Most placements refreshed by one run; the rest wait for the next hourly run.</summary>
    public const int MaxPerRun = 100;

    /// <summary>The placements due a refresh at <paramref name="now"/> (system scope: every organization).</summary>
    public static IQueryable<LtiResourceLink> DueQuery(GadflyDbContext db, DateTimeOffset now) =>
        from l in db.LtiResourceLinks.IgnoreQueryFilters()
        join r in db.LtiRegistrations.IgnoreQueryFilters() on l.RegistrationId equals r.Id
        where r.Enabled && r.RosterRefreshHours > 0 && r.OrganizationId == l.OrganizationId
              && l.ContextMembershipsUrl != null && l.ContextMembershipsUrl != ""
              && (l.LastRosterSyncAt == null || l.LastRosterSyncAt <= now.AddHours(-r.RosterRefreshHours))
        select l;

    /// <summary>True when a placement last synced at <paramref name="lastSync"/> is due under an interval of <paramref name="hours"/>.</summary>
    public static bool IsDue(DateTimeOffset? lastSync, int hours, DateTimeOffset now) =>
        hours > 0 && (lastSync is not { } at || now - at >= TimeSpan.FromHours(hours));

    public async Task RunAsync(Job job, IJobProgress progress, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await DueQuery(db, now).AsNoTracking().OrderBy(l => l.LastRosterSyncAt).Take(MaxPerRun)
            .Select(l => new { l.Id, l.OrganizationId }).ToListAsync(ct);
        int synced = 0, failed = 0;
        for (var i = 0; i < due.Count; i++)
        {
            var p = due[i];
            using (var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(p.OrganizationId)))
            {
                var roster = scope.ServiceProvider.GetRequiredService<LtiRosterService>();
                try
                {
                    var result = await roster.SyncAsync(p.OrganizationId, p.Id, actorUserId: null, ct);
                    if (result.Ok) synced++;
                    else
                    {
                        failed++;
                        log.LogWarning("Scheduled LTI roster refresh of placement {PlacementId} failed: {Message}", p.Id, result.Message);
                        await MarkFailedAsync(scope.ServiceProvider.GetRequiredService<GadflyDbContext>(), p.OrganizationId, p.Id, result.Message, now, ct);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception e)
                {
                    failed++;
                    log.LogWarning(e, "Scheduled LTI roster refresh of placement {PlacementId} failed.", p.Id);
                    await MarkFailedAsync(scope.ServiceProvider.GetRequiredService<GadflyDbContext>(), p.OrganizationId, p.Id,
                        "Gadfly could not reach the LMS roster service.", now, ct);
                }
            }
            await progress.ReportAsync((i + 1) / (double)due.Count, $"{synced} synced, {failed} failed of {due.Count}", ct);
        }
        if (due.Count == 0) await progress.ReportAsync(1, "no roster due", ct);
    }

    // A failed refresh still counts as this interval's try (so a course whose LMS refuses isn't asked again every hour), and the
    // audit row says it failed and why, so the page never reports it as synced.
    private static async Task MarkFailedAsync(GadflyDbContext db, Guid organizationId, Guid placementId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await db.LtiResourceLinks.Where(l => l.Id == placementId && l.OrganizationId == organizationId)
            .ExecuteUpdateAsync(u => u.SetProperty(l => l.LastRosterSyncAt, now), ct);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId, ActorRole = "LTI", Action = FailedAction, EntityType = "LtiResourceLink", EntityId = placementId.ToString(),
            Reason = reason.Length <= 1000 ? reason : reason[..1000], At = now,
        });
        await db.SaveChangesAsync(ct);
    }
}
