using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using Gadfly.Socrates;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Notifications;

/// <summary>
/// The "notify.email" job: renders the template and sends it over <see cref="IAuthMailTransport"/> — the same SMTP
/// transport MindAttic.Authentication's own invitation and password-reset email uses (no second mailer, GAD-T310).
/// Stays silently off, like the rest of Gadfly's optional providers, when SMTP isn't configured
/// (<see cref="AuthEmailStatus"/>): the email is marked Failed with a plain reason rather than throwing, so one
/// unconfigured environment never blocks the job queue's other work. A mock-mode server never sends either; its
/// emails are rendered and logged as Skipped (<see cref="EmailDeliveryGate"/>).
/// </summary>
public sealed class EmailDeliveryJob(GadflyDbContext db, AuthEmailStatus emailStatus, IServiceProvider services, TimeProvider clock, ILogger<EmailDeliveryJob> log) : IJobHandler
{
    public const string JobKind = "notify.email";
    public string Kind => JobKind;

    public async Task RunAsync(Job job, IJobProgress progress, CancellationToken ct)
    {
        var payload = JobQueue.ReadPayload<EmailJobPayload>(job);
        var data = JsonSerializer.Deserialize<EmailData>(payload.DataJson) ?? throw new InvalidOperationException("Empty email template data.");
        // GAD-T479: callers stage app paths ("/my-exams"); a mail client needs the full address, or no button at all.
        data = data with { ActionUrl = EmailTemplates.AbsoluteActionUrl(data.ActionUrl, services.GetRequiredService<IConfiguration>()) };
        var now = clock.GetUtcNow();

        // GAD-T479: an email whose log was settled before it went out (the student's data was erased meanwhile) is never sent.
        var logStatus = await db.Set<EmailLog>().AsNoTracking().Where(e => e.Id == payload.EmailLogId).Select(e => (EmailLogStatus?)e.Status).FirstOrDefaultAsync(ct);
        if (logStatus == EmailLogStatus.Skipped)
        {
            await progress.ReportAsync(1, "skipped before sending", ct);
            return;
        }

        string? toEmail = payload.ToEmailOverride;
        if (toEmail is null && payload.ToUserId is { } userId)
            toEmail = await db.AuthUsers.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);

        var rendered = EmailTemplates.Render(payload.Kind, data);

        async Task MarkAsync(EmailLogStatus status, string? error)
        {
            await db.Set<EmailLog>().Where(e => e.Id == payload.EmailLogId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, status).SetProperty(e => e.Error, error)
                    .SetProperty(e => e.Subject, rendered.Subject).SetProperty(e => e.ToEmail, toEmail)
                    .SetProperty(e => e.SentAt, status == EmailLogStatus.Sent ? now : (DateTimeOffset?)null), ct);
        }

        if (string.IsNullOrWhiteSpace(toEmail) || !MailboxAddress.TryParse(toEmail, out var to))
        {
            await MarkAsync(EmailLogStatus.Failed, "No valid email address for this recipient.");
            await progress.ReportAsync(1, "no address", ct);
            return;
        }

        // Same rule as every other optional provider (ElevenLabs, Stripe...): absent configuration means the feature
        // stays off, never a broken send retried forever. And a mock-mode server never emails real people.
        if (EmailDeliveryGate.BlockedReason(emailStatus, services.GetService<IPanelInfo>(), services.GetRequiredService<IConfiguration>()) is { } blocked)
        {
            var unconfigured = emailStatus.Settings is null;
            await MarkAsync(unconfigured ? EmailLogStatus.Failed : EmailLogStatus.Skipped, blocked);
            log.LogWarning("Gadfly notification email ({Kind}) not sent: {Reason}", payload.Kind, unconfigured ? "SMTP is not configured" : "mock mode");
            await progress.ReportAsync(1, unconfigured ? "email not configured" : "mock mode: not sent", ct);
            return;
        }

        var transport = services.GetRequiredService<IAuthMailTransport>();
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(emailStatus.Settings.From));
        message.To.Add(to);
        message.Subject = rendered.Subject;
        message.Body = new BodyBuilder { HtmlBody = rendered.Html, TextBody = rendered.Text }.ToMessageBody();

        try
        {
            await transport.SendAsync(message, ct);
            await MarkAsync(EmailLogStatus.Sent, null);
            await progress.ReportAsync(1, "sent", ct);
        }
        catch (Exception ex)
        {
            await MarkAsync(EmailLogStatus.Failed, ex.Message.Length > 400 ? ex.Message[..400] : ex.Message);
            log.LogWarning(ex, "Gadfly notification email ({Kind}) to a recipient failed.", payload.Kind);
            throw;   // let the job queue retry with backoff (GAD-T43); MarkAsync above already reflects the latest attempt
        }
    }
}
