using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Notifications;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using MindAttic.Authentication.Services;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Gadfly's notification emails end to end on the test database (GAD-T310, GAD-T374, GAD-T375, under GAD-T399): the outbox
/// stages a job and its log row only with the caller's save and records every skip with its reason; the reminder sweep stages
/// one reminder per assignment, ever; and the delivery job never hands anything to SMTP in mock mode or without SMTP settings.
/// </summary>
[TestFixture]
public class EmailOutboxTests : ServerTest
{
    private static readonly AuthEmailStatus Smtp = new(new AuthEmailSettings("smtp.example.org", 587, "u", "p", "noreply@example.org"), []);
    private static readonly AuthEmailStatus NoSmtp = new(null, []);

    private static EmailData Data(string title = "Biology check") => new("Acme", "Alex", ExamTitle: title);

    // ------------------------------------------------------------------ outbox

    [Test]
    public async Task Stage_writes_the_log_row_and_job_only_when_the_caller_saves()
    {
        var w = await Seed.ExamAsync();
        await using (var db = OrgDb(w.Org))
        {
            Assert.That(await EmailOutbox.StageAsync(db, w.Org, EmailKinds.ExamAssigned, w.StudentId, Data(), DateTimeOffset.UtcNow, CancellationToken.None, w.AssignmentId), Is.True);
            await using (var before = SystemDb())
                Assert.That(await before.Set<EmailLog>().AnyAsync(l => l.OrganizationId == w.Org), Is.False, "nothing is written until the caller saves");
            await db.SaveChangesAsync();
        }
        await using var check = SystemDb();
        var log = await check.Set<EmailLog>().AsNoTracking().SingleAsync(l => l.OrganizationId == w.Org);
        Assert.That(log.Status, Is.EqualTo(EmailLogStatus.Queued));
        Assert.That(log.Kind, Is.EqualTo(EmailKinds.ExamAssigned));
        Assert.That(log.RelatedId, Is.EqualTo(w.AssignmentId));
        var job = await check.Jobs.AsNoTracking().SingleAsync(j => j.OrganizationId == w.Org && j.Kind == EmailDeliveryJob.JobKind);
        Assert.That(job.State, Is.EqualTo(JobState.Queued));
        Assert.That(job.PayloadJson, Does.Contain(log.Id.ToString()));
    }

    [Test]
    public async Task Stage_discarded_with_the_callers_changes_writes_nothing()
    {
        var w = await Seed.ExamAsync();
        await using (var db = OrgDb(w.Org))
            await EmailOutbox.StageAsync(db, w.Org, EmailKinds.ExamAssigned, w.StudentId, Data(), DateTimeOffset.UtcNow, CancellationToken.None);
        await using var check = SystemDb();
        Assert.That(await check.Set<EmailLog>().AnyAsync(l => l.OrganizationId == w.Org), Is.False);
        Assert.That(await check.Jobs.AnyAsync(j => j.OrganizationId == w.Org && j.Kind == EmailDeliveryJob.JobKind), Is.False);
    }

    [Test]
    public async Task An_organization_with_emails_off_gets_a_skipped_log_row_and_no_job()
    {
        var w = await Seed.ExamAsync();
        await using (var db = SystemDb())
        {
            var s = await db.OrganizationSettings.SingleAsync(x => x.OrganizationId == w.Org);
            s.EmailNotificationsEnabled = false;
            await db.SaveChangesAsync();
        }
        await using (var db = OrgDb(w.Org))
        {
            Assert.That(await EmailOutbox.StageAsync(db, w.Org, EmailKinds.ResultReleased, w.StudentId, Data(), DateTimeOffset.UtcNow, CancellationToken.None), Is.False);
            await db.SaveChangesAsync();
        }
        await using var check = SystemDb();
        var log = await check.Set<EmailLog>().AsNoTracking().SingleAsync(l => l.OrganizationId == w.Org);
        Assert.That(log.Status, Is.EqualTo(EmailLogStatus.Skipped));
        Assert.That(log.Error, Does.Contain("turned off"));
        Assert.That(await check.Jobs.AnyAsync(j => j.OrganizationId == w.Org && j.Kind == EmailDeliveryJob.JobKind), Is.False);
    }

    [Test]
    public async Task A_student_who_opted_out_gets_no_reminder_but_still_gets_other_emails()
    {
        var w = await Seed.ExamAsync();
        await using (var db = OrgDb(w.Org))
        {
            db.Set<StudentNotificationPreference>().Add(new StudentNotificationPreference { OrganizationId = w.Org, UserId = w.StudentId, RemindersOptedOut = true, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await using (var db = OrgDb(w.Org))
        {
            Assert.That(await EmailOutbox.StageAsync(db, w.Org, EmailKinds.Reminder, w.StudentId, Data(), DateTimeOffset.UtcNow, CancellationToken.None), Is.False);
            Assert.That(await EmailOutbox.StageAsync(db, w.Org, EmailKinds.ResultReleased, w.StudentId, Data(), DateTimeOffset.UtcNow, CancellationToken.None), Is.True,
                "the opt-out covers reminders only");
            await db.SaveChangesAsync();
        }
        await using var check = SystemDb();
        var logs = await check.Set<EmailLog>().AsNoTracking().Where(l => l.OrganizationId == w.Org).ToListAsync();
        Assert.That(logs.Single(l => l.Kind == EmailKinds.Reminder).Status, Is.EqualTo(EmailLogStatus.Skipped));
        Assert.That(logs.Single(l => l.Kind == EmailKinds.Reminder).Error, Does.Contain("opted out"));
        Assert.That(logs.Single(l => l.Kind == EmailKinds.ResultReleased).Status, Is.EqualTo(EmailLogStatus.Queued));
    }

    [Test]
    public async Task A_test_send_is_never_gated_and_goes_to_the_address_given()
    {
        var w = await Seed.ExamAsync();
        await using (var db = SystemDb())
        {
            var s = await db.OrganizationSettings.SingleAsync(x => x.OrganizationId == w.Org);
            s.EmailNotificationsEnabled = false;
            await db.SaveChangesAsync();
        }
        await using (var db = OrgDb(w.Org))
        {
            EmailOutbox.StageTest(db, w.Org, EmailKinds.Test, "admin@example.org", Data(), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        await using var check = SystemDb();
        var log = await check.Set<EmailLog>().AsNoTracking().SingleAsync(l => l.OrganizationId == w.Org);
        Assert.That(log.Status, Is.EqualTo(EmailLogStatus.Queued));
        Assert.That(log.ToEmail, Is.EqualTo("admin@example.org"));
        Assert.That(await check.Jobs.CountAsync(j => j.OrganizationId == w.Org && j.Kind == EmailDeliveryJob.JobKind), Is.EqualTo(1));
    }

    // ------------------------------------------------------------------ reminder sweep

    [Test]
    public async Task The_sweep_stages_one_reminder_for_an_assignment_due_within_48_hours_and_never_a_second()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.Now = now;
        var due = await Seed.ExamAsync(closes: now.AddHours(24));
        await RunSweepAsync();
        await RunSweepAsync();
        await using var check = SystemDb();
        var reminders = await check.Set<EmailLog>().AsNoTracking()
            .Where(l => l.RelatedId == due.AssignmentId && l.Kind == EmailKinds.Reminder).ToListAsync();
        Assert.That(reminders, Has.Count.EqualTo(1), "one reminder per assignment, ever");
        Assert.That(reminders[0].ToUserId, Is.EqualTo(due.StudentId));
        Assert.That(reminders[0].Status, Is.EqualTo(EmailLogStatus.Queued));
    }

    [Test]
    public async Task The_sweep_skips_assignments_not_due_soon_already_closed_or_already_finished()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.Now = now;
        var later = await Seed.ExamAsync(closes: now.AddDays(5));
        var closed = await Seed.ExamAsync(closes: now.AddHours(-1));
        var open = await Seed.ExamAsync();   // no due date
        var finished = await Seed.ExamAsync(closes: now.AddHours(12));
        await using (var db = OrgDb(finished.Org))
        {
            db.ExamSessions.Add(new ExamSession
            {
                OrganizationId = finished.Org, AssignmentId = finished.AssignmentId, ExamId = finished.ExamId, StudentUserId = finished.StudentId,
                State = SessionState.Ended, CreatedAt = now.AddHours(-1), StartedAt = now.AddHours(-1), EndedAt = now, EndReason = "Mastery",
                TranscriptJson = "{}", LedgerJson = "{}", ClientInfo = "browser",
            });
            await db.SaveChangesAsync();
        }
        await RunSweepAsync();
        await using var check = SystemDb();
        Guid[] ids = [later.AssignmentId, closed.AssignmentId, open.AssignmentId, finished.AssignmentId];
        Assert.That(await check.Set<EmailLog>().AnyAsync(l => l.Kind == EmailKinds.Reminder && l.RelatedId != null && ids.Contains(l.RelatedId.Value)), Is.False);
    }

    [Test]
    public async Task The_sweep_records_an_opted_out_student_as_skipped_and_does_not_retry_them()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.Now = now;
        var w = await Seed.ExamAsync(closes: now.AddHours(10));
        await using (var db = OrgDb(w.Org))
        {
            db.Set<StudentNotificationPreference>().Add(new StudentNotificationPreference { OrganizationId = w.Org, UserId = w.StudentId, RemindersOptedOut = true, UpdatedAt = now });
            await db.SaveChangesAsync();
        }
        await RunSweepAsync();
        await RunSweepAsync();
        await using var check = SystemDb();
        var logs = await check.Set<EmailLog>().AsNoTracking().Where(l => l.RelatedId == w.AssignmentId).ToListAsync();
        Assert.That(logs, Has.Count.EqualTo(1));
        Assert.That(logs[0].Status, Is.EqualTo(EmailLogStatus.Skipped));
        Assert.That(await check.Jobs.AnyAsync(j => j.OrganizationId == w.Org && j.Kind == EmailDeliveryJob.JobKind), Is.False);
    }

    private static async Task RunSweepAsync()
    {
        await using var db = SystemDb();
        await new ReminderSweepJob(db, Clock).RunAsync(new Job { Kind = ReminderSweepJob.JobKind, PayloadJson = "{}" }, new NoProgress(), CancellationToken.None);
    }

    // ------------------------------------------------------------------ delivery job

    [Test]
    public async Task Mock_mode_never_hands_an_email_to_SMTP_even_when_SMTP_is_configured()
    {
        var (_, job, logId) = await StageTestSendAsync();
        var transport = new RecordingTransport();
        await RunDeliveryAsync(job, Smtp, transport, panel: null);   // the test host's own panel: mock
        Assert.That(transport.Sent, Is.Empty, "a mock-mode server sent a real email");
        var log = await LogAsync(logId);
        Assert.That(log.Status, Is.EqualTo(EmailLogStatus.Skipped));
        Assert.That(log.Error, Does.Contain("mock mode"));
        Assert.That(log.Subject, Is.Not.Empty, "the email is still rendered so the preview log shows what would have gone out");
        Assert.That(log.SentAt, Is.Null);
    }

    [Test]
    public async Task Without_SMTP_settings_nothing_is_sent_and_the_email_is_marked_failed_with_a_plain_reason()
    {
        var (_, job, logId) = await StageTestSendAsync();
        var transport = new RecordingTransport();
        await RunDeliveryAsync(job, NoSmtp, transport, new StubPanel(mock: false));
        Assert.That(transport.Sent, Is.Empty);
        var log = await LogAsync(logId);
        Assert.That(log.Status, Is.EqualTo(EmailLogStatus.Failed));
        Assert.That(log.Error, Does.Contain("not configured"));
    }

    [Test]
    public async Task A_recipient_without_an_email_address_is_marked_failed_without_a_send()
    {
        var w = await Seed.ExamAsync();   // the seeded student account has no email address
        Job job;
        await using (var db = OrgDb(w.Org))
        {
            await EmailOutbox.StageAsync(db, w.Org, EmailKinds.ExamAssigned, w.StudentId, Data(), DateTimeOffset.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
            job = await db.Jobs.AsNoTracking().SingleAsync(j => j.OrganizationId == w.Org && j.Kind == EmailDeliveryJob.JobKind);
        }
        var transport = new RecordingTransport();
        await RunDeliveryAsync(job, Smtp, transport, new StubPanel(mock: false));
        Assert.That(transport.Sent, Is.Empty);
        await using var check = SystemDb();
        var log = await check.Set<EmailLog>().AsNoTracking().SingleAsync(l => l.OrganizationId == w.Org);
        Assert.That(log.Status, Is.EqualTo(EmailLogStatus.Failed));
        Assert.That(log.Error, Does.Contain("No valid email address"));
    }

    [Test]
    public async Task Live_mode_with_SMTP_sends_once_and_marks_the_email_sent()
    {
        var (_, job, logId) = await StageTestSendAsync();
        var transport = new RecordingTransport();
        await RunDeliveryAsync(job, Smtp, transport, new StubPanel(mock: false));
        Assert.That(transport.Sent, Has.Count.EqualTo(1));
        Assert.That(transport.Sent[0].To.Mailboxes.Single().Address, Is.EqualTo("admin@example.org"));
        Assert.That(transport.Sent[0].From.Mailboxes.Single().Address, Is.EqualTo("noreply@example.org"));
        var log = await LogAsync(logId);
        Assert.That(log.Status, Is.EqualTo(EmailLogStatus.Sent));
        Assert.That(log.SentAt, Is.Not.Null);
    }

    [Test]
    public async Task A_failed_SMTP_send_is_marked_failed_and_rethrown_for_the_queue_to_retry()
    {
        var (_, job, logId) = await StageTestSendAsync();
        var transport = new RecordingTransport { Fail = true };
        Assert.ThrowsAsync<InvalidOperationException>(() => RunDeliveryAsync(job, Smtp, transport, new StubPanel(mock: false)));
        var log = await LogAsync(logId);
        Assert.That(log.Status, Is.EqualTo(EmailLogStatus.Failed));
        Assert.That(log.Error, Does.Contain("relay refused"));
    }

    private static async Task<(Guid Org, Job Job, Guid LogId)> StageTestSendAsync()
    {
        var org = await Seed.OrganizationAsync();
        await using var db = OrgDb(org);
        EmailOutbox.StageTest(db, org, EmailKinds.Test, "admin@example.org", Data(), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.OrganizationId == org && j.Kind == EmailDeliveryJob.JobKind);
        var logId = await db.Set<EmailLog>().Where(l => l.OrganizationId == org).Select(l => l.Id).SingleAsync();
        return (org, job, logId);
    }

    private static async Task RunDeliveryAsync(Job job, AuthEmailStatus status, IAuthMailTransport transport, IPanelInfo? panel)
    {
        await using var scope = Scope(TenantSnapshot.System);
        var services = new Overrides(scope.ServiceProvider, transport, panel);
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var handler = new EmailDeliveryJob(db, status, services, Clock, NullLogger<EmailDeliveryJob>.Instance);
        await handler.RunAsync(job, new NoProgress(), CancellationToken.None);
    }

    private static async Task<EmailLog> LogAsync(Guid id)
    {
        await using var db = SystemDb();
        return await db.Set<EmailLog>().AsNoTracking().SingleAsync(l => l.Id == id);
    }

    /// <summary>The app's services with the SMTP transport (and optionally the panel) replaced.</summary>
    private sealed class Overrides(IServiceProvider inner, IAuthMailTransport transport, IPanelInfo? panel) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IAuthMailTransport) ? transport
            : serviceType == typeof(IPanelInfo) && panel is not null ? panel
            : inner.GetService(serviceType);
    }

    private sealed class RecordingTransport : IAuthMailTransport
    {
        public List<MimeMessage> Sent { get; } = [];
        public bool Fail { get; init; }

        public Task SendAsync(MimeMessage message, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("relay refused");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class StubPanel(bool mock) : IPanelInfo
    {
        public bool IsMock => mock;
        public string Mode => mock ? PanelModes.Mock : PanelModes.Live;
        public string Reason => "test";
    }

    private sealed class NoProgress : IJobProgress
    {
        public Task ReportAsync(double fraction, string? message = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
