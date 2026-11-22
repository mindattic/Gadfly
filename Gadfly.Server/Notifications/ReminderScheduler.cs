using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Notifications;

/// <summary>
/// Queues one "notify.reminder-sweep" job every few hours (if one isn't already waiting), the same shape as
/// <see cref="Gadfly.Server.Privacy.RetentionJob"/>'s scheduler (GAD-T310).
/// </summary>
public sealed class ReminderScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ReminderScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateSystemScope();
                var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                var waiting = await db.Jobs.AnyAsync(j => j.Kind == ReminderSweepJob.JobKind && (j.State == JobState.Queued || j.State == JobState.Running), stoppingToken);
                if (!waiting)
                    await scope.ServiceProvider.GetRequiredService<JobQueue>().EnqueueAsync(ReminderSweepJob.JobKind, new { }, null, ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Could not queue the reminder sweep job.");
            }
            try { await Task.Delay(TimeSpan.FromHours(6), clock, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}

/// <summary>
/// The "notify.reminder-sweep" job (GAD-T310): finds assignments due within the next 48 hours that have no
/// finished attempt yet and have never had a reminder staged, and stages one each (gated by the organization's
/// switch and the student's own opt-out, in <see cref="EmailOutbox"/>). One reminder per assignment, ever — not
/// one per sweep — so a student is never emailed about the same exam over and over.
/// </summary>
public sealed class ReminderSweepJob(GadflyDbContext db, TimeProvider clock) : IJobHandler
{
    public const string JobKind = "notify.reminder-sweep";
    public string Kind => JobKind;
    private static readonly TimeSpan Window = TimeSpan.FromHours(48);

    public async Task RunAsync(Job job, IJobProgress progress, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var until = now + Window;

        var due = await (from a in db.ExamAssignments.IgnoreQueryFilters().AsNoTracking()
                          join e in db.Exams.IgnoreQueryFilters().AsNoTracking() on a.ExamId equals e.Id
                          where a.ClosesAt != null && a.ClosesAt > now && a.ClosesAt <= until
                                && !db.ExamSessions.IgnoreQueryFilters().Any(s => s.AssignmentId == a.Id && s.State == SessionState.Ended)
                                && !db.Set<EmailLog>().Any(l => l.Kind == EmailKinds.Reminder && l.RelatedId == a.Id)
                          select new { a.Id, a.OrganizationId, a.StudentUserId, a.ClosesAt, ExamTitle = e.Title })
            .Take(2000).ToListAsync(ct);

        var orgNames = await db.Organizations.AsNoTracking().Where(o => due.Select(d => d.OrganizationId).Distinct().Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var names = await db.AuthUsers.AsNoTracking().Where(u => due.Select(d => d.StudentUserId).Distinct().Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.UserName, ct);

        var staged = 0;
        foreach (var a in due)
        {
            var data = new EmailData(orgNames.GetValueOrDefault(a.OrganizationId, "your organization"), names.GetValueOrDefault(a.StudentUserId, "there"),
                ExamTitle: a.ExamTitle, DueAtUtc: a.ClosesAt, ActionUrl: "/my-exams", ActionLabel: "Open my exams");
            if (await EmailOutbox.StageAsync(db, a.OrganizationId, EmailKinds.Reminder, a.StudentUserId, data, now, ct, relatedId: a.Id))
                staged++;
        }
        await db.SaveChangesAsync(ct);
        await progress.ReportAsync(1, $"{staged} reminder(s) staged of {due.Count} due assignment(s)", ct);
    }
}
