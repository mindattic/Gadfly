using Gadfly.Engine;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Sessions;

/// <summary>
/// Closes an exam session a student abandoned outright (closed the tab or lost power and never came back: no pause, no
/// reload, no staff action) once it has sat well past its own deadline (GAD-T306). Without this, such a session stays
/// "in progress" forever: nothing else ever calls <see cref="SessionOrchestrator.EndAsync"/> for it, since every other
/// path to an end runs from the student's own browser or from a staff action on the live monitor (GAD-T299).
/// </summary>
/// <remarks>
/// Closing (rather than only flagging it for an admin) is the less disruptive choice here: it runs through the very
/// same end path the client's own "time's up" check already uses, so the attempt is graded exactly as it would have
/// been had the student's browser stayed open to report it, counts fairly as a used attempt (not thrown away), and the
/// student's record is never left blocking a retake while nobody has noticed. A session staff are actively holding
/// (<see cref="SocraticSession.IsHeld"/>, GAD-T299) is left alone: that is a deliberate, visible staff action with its
/// own generous allowance, not an abandonment, and staff already have the live monitor to resolve it.
/// </remarks>
public sealed class StaleSessionSweep(IServiceScopeFactory scopes, SessionOrchestrator orchestrator, TimeProvider clock, ILogger<StaleSessionSweep> log)
{
    /// <summary>
    /// How long past a session's own deadline (its time budget, any staff extra time, and pause credit already
    /// included) it must sit before counting as abandoned rather than a student who merely stepped away, or is
    /// between a dropped connection and reconnecting (GAD-T306). Generous on purpose: the exam's own clock has
    /// already run out by this point, so nothing here cuts a student's time shorter than the exam itself allowed.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);

    private static readonly SessionState[] Candidates = [SessionState.Running, SessionState.Paused];

    /// <summary>Finds and closes every session abandoned past its deadline plus <see cref="Grace"/>. Returns how many were closed.</summary>
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        using var systemScope = scopes.CreateSystemScope();
        var systemDb = systemScope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var now = clock.GetUtcNow();
        // Cross-organization on purpose (a system scope): abandonment isn't an organization-scoped question. Only a
        // session that has actually taken a step has a transcript to read; one that never got that far (still at
        // Created or ConsentGiven) isn't Running or Paused, so it is already outside this query.
        var rows = await systemDb.ExamSessions.AsNoTracking()
            .Where(s => Candidates.Contains(s.State) && s.TranscriptJson != null)
            .Select(s => new { s.Id, s.OrganizationId, s.StudentUserId, s.State, s.TranscriptJson })
            .ToListAsync(ct);
        var closed = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            SocraticSession session;
            try { session = SessionTranscriptSerializer.Deserialize(row.TranscriptJson!); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
            {
                log.LogWarning(ex, "Session {SessionId} could not be read for the stale-session sweep; left alone.", row.Id);
                continue;
            }
            if (session.IsEnded || session.IsHeld) continue;   // already over, or staff are actively managing it (GAD-T299)
            var overdueBy = now - session.Deadline(now);
            if (overdueBy < Grace) continue;
            try
            {
                var step = await orchestrator.EndAsync(row.OrganizationId, row.Id, row.StudentUserId, EndReason.TimeUp, ct);
                await AuditAsync(row.OrganizationId, row.Id, row.State, step, overdueBy, ct);
                closed++;
                log.LogInformation("Closed abandoned exam session {SessionId} (organization {OrganizationId}): {Overdue} past its deadline.",
                    row.Id, row.OrganizationId, Humanize(overdueBy));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let one stuck session stop the rest of the sweep: left for the next sweep, or for staff to resolve by hand.
                log.LogWarning(ex, "Could not close abandoned exam session {SessionId}; left for the next sweep or for staff.", row.Id);
            }
        }
        return closed;
    }

    /// <summary>The audit note on the session's review timeline (<see cref="Admin.LiveSessionService.AutoClosedAction"/>), with why.</summary>
    private async Task AuditAsync(Guid organizationId, Guid sessionId, SessionState before, SessionStep after, TimeSpan overdueBy, CancellationToken ct)
    {
        using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var auditor = new AssessmentGuard(db, scope.ServiceProvider.GetRequiredService<ITenantContext>(), clock);
        auditor.Audit(Admin.LiveSessionService.AutoClosedAction, "ExamSession", sessionId.ToString(),
            new { state = before.ToString() }, new { state = after.State.ToString(), endReason = after.EndReason },
            $"closed: abandoned past time limit (no activity, {Humanize(overdueBy)} past its deadline)", sessionId);
        await db.SaveChangesAsync(ct);
    }

    private static string Humanize(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{span.TotalHours:0.#} hours" : $"{Math.Round(span.TotalMinutes):0} minutes";
}

/// <summary>The "session.stale-sweep" job: runs <see cref="StaleSessionSweep.SweepAsync"/>.</summary>
public sealed class StaleSessionJob(StaleSessionSweep sweep) : IJobHandler
{
    public const string JobKind = "session.stale-sweep";
    public string Kind => JobKind;

    public async Task RunAsync(Job job, IJobProgress progress, CancellationToken ct)
    {
        var n = await sweep.SweepAsync(ct);
        await progress.ReportAsync(1, $"{n} abandoned session(s) closed", ct);
    }
}

/// <summary>Queues the stale-session sweep periodically (if one isn't already waiting).</summary>
public sealed class StaleSessionScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<StaleSessionScheduler> log) : BackgroundService
{
    /// <summary>How often the sweep is queued: often enough that an abandoned session is closed within about this long of its grace running out.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateSystemScope();
                var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                var waiting = await db.Jobs.AnyAsync(j => j.Kind == StaleSessionJob.JobKind && (j.State == JobState.Queued || j.State == JobState.Running), stoppingToken);
                if (!waiting)
                    await scope.ServiceProvider.GetRequiredService<JobQueue>().EnqueueAsync(StaleSessionJob.JobKind, new { }, null, ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Could not queue the stale-session sweep.");
            }
            try { await Task.Delay(Interval, clock, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
