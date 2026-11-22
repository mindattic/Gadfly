using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The system status page's organization data (GAD-T302, GAD-T354, GAD-T355, GAD-T413; under GAD-T399): who may load it, the job
/// queue and its 7-day failed list, webhook problems, storage and outside-call timings, all scoped to the organization; and Retry
/// and Cancel, which act only on the organization's own jobs in the right state and are audited.
/// </summary>
[TestFixture]
public class AdminStatusServiceTests : ServerTest
{
    private static AdminStatusService Service(GadflyDbContext db, TenantSnapshot tenant) =>
        new(db, new FixedTenant(tenant), Services.GetRequiredService<HealthCheckService>(), Clock, new JobQueue(db, Clock));

    private static async Task<SystemStatusReport> LoadAsync(Guid org, string role = OrgRoles.Admin)
    {
        var me = Member(org, Guid.NewGuid(), role);
        await using var db = Db(me);
        return await Service(db, me).LoadAsync();
    }

    private static Job NewJob(Guid org, JobState state, DateTimeOffset created, string kind = "test.kind", DateTimeOffset? completed = null) => new()
    {
        OrganizationId = org, Kind = kind, State = state, CreatedAt = created, RunAfter = created, CompletedAt = completed,
        Attempts = state == JobState.Failed ? 5 : 0, Error = state == JobState.Failed ? "it broke" : null,
    };

    [TestCase(OrgRoles.Student)]
    [TestCase(OrgRoles.Instructor)]
    public async Task Anyone_but_an_owner_or_admin_is_refused(string role)
    {
        var org = await Seed.OrganizationAsync();
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => LoadAsync(org, role));
    }

    [Test]
    public async Task A_new_organization_has_an_empty_queue_no_webhooks_and_a_healthy_database()
    {
        var org = await Seed.OrganizationAsync();
        var r = await LoadAsync(org, OrgRoles.Owner);
        Assert.That(r.DatabaseHealthy, Is.True, r.DatabaseDetail);
        Assert.That(r.Jobs.PendingOrRunning, Is.Zero);
        Assert.That(r.Jobs.OldestPendingSince, Is.Null);
        Assert.That(r.Jobs.FailedInWindow, Is.Zero);
        Assert.That(r.JobList, Is.Empty);
        Assert.That(r.Webhooks!.ActiveEndpoints, Is.Zero);
        Assert.That(r.Webhooks.Problems, Is.Empty);
        Assert.That(r.CallTimings, Is.Empty);
        Assert.That(r.Storage!.Sessions, Is.Zero);
    }

    [Test]
    public async Task The_queue_counts_waiting_work_and_failures_over_seven_days_and_ignores_other_organizations()
    {
        var now = Clock.GetUtcNow();
        var org = await Seed.OrganizationAsync();
        var other = await Seed.OrganizationAsync();
        await using (var db = SystemDb())
        {
            db.Jobs.AddRange(
                NewJob(org, JobState.Queued, now.AddMinutes(-30)),
                NewJob(org, JobState.Queued, now.AddMinutes(-5)),
                NewJob(org, JobState.Running, now.AddMinutes(-1)),
                NewJob(org, JobState.Failed, now.AddDays(-3), completed: now.AddDays(-3)),
                NewJob(org, JobState.Failed, now.AddDays(-AdminStatusService.FailedJobDays - 2), completed: now.AddDays(-AdminStatusService.FailedJobDays - 2)),
                NewJob(org, JobState.Succeeded, now.AddHours(-2), completed: now.AddHours(-2)),
                NewJob(other, JobState.Queued, now.AddHours(-5)),
                NewJob(other, JobState.Failed, now.AddHours(-1), completed: now.AddHours(-1)));
            await db.SaveChangesAsync();
        }
        var r = await LoadAsync(org);
        Assert.That(r.Jobs.PendingOrRunning, Is.EqualTo(3));
        Assert.That(r.Jobs.OldestPendingSince!.Value, Is.EqualTo(now.AddMinutes(-30)).Within(TimeSpan.FromSeconds(1)));
        Assert.That(r.Jobs.FailedInWindow, Is.EqualTo(1), "only the failure inside the 7-day window counts");
        Assert.That(r.JobList, Has.Count.EqualTo(4), "waiting, running and the recent failure; not the old failure or the success");
        Assert.That(r.JobList.Select(j => j.CreatedAt), Is.Ordered.Descending);
        var failed = r.JobList.Single(j => j.State == JobState.Failed);
        Assert.That(failed.CanRetry, Is.True);
        Assert.That(failed.CanCancel, Is.False);
        Assert.That(failed.Error, Is.EqualTo("it broke"));
    }

    [Test]
    public async Task Webhook_problems_list_retrying_and_recently_given_up_deliveries_with_their_address()
    {
        var now = Clock.GetUtcNow();
        var org = await Seed.OrganizationAsync();
        await using (var db = SystemDb())
        {
            var hook = new Webhook { OrganizationId = org, Url = "https://hooks.example/gadfly", Events = "session.completed", ProtectedSecret = "x", CreatedAt = now };
            db.Webhooks.Add(hook);
            db.Webhooks.Add(new Webhook { OrganizationId = org, Url = "https://off.example", Events = "session.completed", ProtectedSecret = "x", CreatedAt = now, IsActive = false });
            WebhookDelivery D(DeliveryStatus s, DateTimeOffset at) => new() { OrganizationId = org, WebhookId = hook.Id, EventType = "session.completed", Status = s, CreatedAt = at, Attempt = 2, ResponseCode = 500 };
            db.WebhookDeliveries.AddRange(
                D(DeliveryStatus.Pending, now), D(DeliveryStatus.Delivered, now.AddHours(-1)), D(DeliveryStatus.Delivered, now.AddDays(-3)),
                D(DeliveryStatus.Failed, now.AddMinutes(-10)), D(DeliveryStatus.GaveUp, now.AddHours(-2)), D(DeliveryStatus.GaveUp, now.AddDays(-3)));
            await db.SaveChangesAsync();
        }
        var w = (await LoadAsync(org)).Webhooks!;
        Assert.That(w.ActiveEndpoints, Is.EqualTo(1));
        Assert.That(w.Pending, Is.EqualTo(1));
        Assert.That(w.Retrying, Is.EqualTo(1));
        Assert.That(w.DeliveredInWindow, Is.EqualTo(1));
        Assert.That(w.GaveUpInWindow, Is.EqualTo(1));
        Assert.That(w.Problems.Select(p => p.Status), Is.EquivalentTo(new[] { DeliveryStatus.Failed, DeliveryStatus.GaveUp }));
        Assert.That(w.Problems.All(p => p.Url == "https://hooks.example/gadfly"), Is.True);
    }

    [Test]
    public async Task Outside_call_timings_are_grouped_by_call_over_the_last_day_slowest_first()
    {
        var now = Clock.GetUtcNow();
        var org = await Seed.OrganizationAsync();
        await using (var db = SystemDb())
        {
            ApiCallTiming T(string name, int ms, bool ok, DateTimeOffset at) => new() { Id = Guid.NewGuid(), OrganizationId = org, CallName = name, DurationMs = ms, Succeeded = ok, StartedAtUtc = at };
            db.ApiCallTimings.AddRange(
                T("panel.judge", 1000, true, now.AddMinutes(-5)), T("panel.judge", 3000, false, now.AddMinutes(-4)),
                T("tts.elevenlabs", 400, true, now.AddMinutes(-3)),
                T("panel.judge", 90_000, true, now.AddDays(-2)));   // outside the window
            await db.SaveChangesAsync();
        }
        var r = await LoadAsync(org);
        Assert.That(r.CallSampleSize, Is.EqualTo(3));
        Assert.That(r.CallTimings.Select(c => c.CallName), Is.EqualTo(new[] { "panel.judge", "tts.elevenlabs" }));
        var judge = r.CallTimings[0];
        Assert.That(judge.Count, Is.EqualTo(2));
        Assert.That(judge.AvgMs, Is.EqualTo(2000));
        Assert.That(judge.MaxMs, Is.EqualTo(3000));
        Assert.That(judge.SuccessRate, Is.EqualTo(0.5));
    }

    [Test]
    public async Task Storage_counts_the_organizations_documents_sessions_and_transcript_text()
    {
        var (w, _, _) = await Seed.EndedSessionAsync("Plants use light.");
        var s = (await LoadAsync(w.Org)).Storage!;
        Assert.That(s.Documents, Is.EqualTo(1));
        Assert.That(s.Sessions, Is.EqualTo(1));
        Assert.That(s.TranscriptCharacters, Is.GreaterThan(0));
    }

    // ------------------------------------------------------------------ Retry and Cancel

    [Test]
    public async Task Retry_requeues_a_failed_job_and_is_audited()
    {
        var org = await Seed.OrganizationAsync();
        var job = NewJob(org, JobState.Failed, Clock.GetUtcNow().AddHours(-1), completed: Clock.GetUtcNow());
        await using (var db = SystemDb()) { db.Jobs.Add(job); await db.SaveChangesAsync(); }

        var me = Member(org, Guid.NewGuid(), OrgRoles.Admin);
        await using (var db = Db(me))
            Assert.That(await Service(db, me).RetryJobAsync(job.Id), Is.EqualTo("Queued to run again."));

        await using var check = SystemDb();
        var stored = await check.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.That(stored.State, Is.EqualTo(JobState.Queued));
        Assert.That(stored.Attempts, Is.Zero);
        Assert.That(stored.Error, Is.Null);
        Assert.That(await check.AuditEvents.CountAsync(a => a.OrganizationId == org && a.Action == "admin.job.retried"), Is.EqualTo(1));
    }

    [Test]
    public async Task Cancel_stops_a_waiting_job_and_is_audited()
    {
        var org = await Seed.OrganizationAsync();
        var job = NewJob(org, JobState.Queued, Clock.GetUtcNow());
        await using (var db = SystemDb()) { db.Jobs.Add(job); await db.SaveChangesAsync(); }

        var me = Member(org, Guid.NewGuid(), OrgRoles.Owner);
        await using (var db = Db(me))
            Assert.That(await Service(db, me).CancelJobAsync(job.Id), Does.StartWith("Cancelled."));

        await using var check = SystemDb();
        Assert.That((await check.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).State, Is.EqualTo(JobState.Cancelled));
        Assert.That(await check.AuditEvents.CountAsync(a => a.OrganizationId == org && a.Action == "admin.job.cancelled"), Is.EqualTo(1));
    }

    [Test]
    public async Task A_job_in_the_wrong_state_is_refused_and_nothing_is_audited()
    {
        var org = await Seed.OrganizationAsync();
        var queued = NewJob(org, JobState.Queued, Clock.GetUtcNow());
        var succeeded = NewJob(org, JobState.Succeeded, Clock.GetUtcNow(), completed: Clock.GetUtcNow());
        await using (var db = SystemDb()) { db.Jobs.AddRange(queued, succeeded); await db.SaveChangesAsync(); }

        var me = Member(org, Guid.NewGuid(), OrgRoles.Admin);
        await using (var db = Db(me))
        {
            var service = Service(db, me);
            Assert.That(await service.RetryJobAsync(queued.Id), Does.StartWith("Only a failed or cancelled job"));
            Assert.That(await service.CancelJobAsync(succeeded.Id), Does.StartWith("Only a waiting or running job"));
        }
        await using var check = SystemDb();
        Assert.That((await check.Jobs.AsNoTracking().SingleAsync(j => j.Id == queued.Id)).State, Is.EqualTo(JobState.Queued));
        Assert.That(await check.AuditEvents.AnyAsync(a => a.OrganizationId == org && a.Action.StartsWith("admin.job.")), Is.False);
    }

    [Test]
    public async Task Another_organizations_job_is_not_found_and_left_alone()
    {
        var org = await Seed.OrganizationAsync();
        var other = await Seed.OrganizationAsync();
        var theirs = NewJob(other, JobState.Queued, Clock.GetUtcNow());
        await using (var db = SystemDb()) { db.Jobs.Add(theirs); await db.SaveChangesAsync(); }

        var me = Member(org, Guid.NewGuid(), OrgRoles.Admin);
        await using (var db = Db(me))
            Assert.That(await Service(db, me).CancelJobAsync(theirs.Id), Is.EqualTo("That job was not found in this organization."));
        await using var check = SystemDb();
        Assert.That((await check.Jobs.AsNoTracking().SingleAsync(j => j.Id == theirs.Id)).State, Is.EqualTo(JobState.Queued));
    }

    [Test]
    public async Task A_student_cannot_retry_or_cancel()
    {
        var org = await Seed.OrganizationAsync();
        var job = NewJob(org, JobState.Queued, Clock.GetUtcNow());
        await using (var db = SystemDb()) { db.Jobs.Add(job); await db.SaveChangesAsync(); }
        var me = Member(org, Guid.NewGuid(), OrgRoles.Student);
        await using var mine = Db(me);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(mine, me).CancelJobAsync(job.Id));
    }
}
