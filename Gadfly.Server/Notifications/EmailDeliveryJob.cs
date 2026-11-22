using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Notifications;

/// <summary>
/// The "notify.email" job: renders the template and sends it over <see cref="IAuthMailTransport"/> — the same SMTP
/// transport MindAttic.Authentication's own invitation and password-reset email uses (no second mailer, GAD-T310).
/// Stays silently off, like the rest of Gadfly's optional providers, when SMTP isn't configured
/// (<see cref="AuthEmailStatus"/>): the email is marked Failed with a plain reason rather than throwing, so one
/// unconfigured environment never blocks the job queue's other work.
/// </summary>
public sealed class EmailDeliveryJob(GadflyDbContext db, AuthEmailStatus emailStatus, IServiceProvider services, TimeProvider clock, ILogger<EmailDeliveryJob> log) : IJobHandler
{
    public const string JobKind = "notify.email";
    public string Kind => JobKind;

    public async Task RunAsync(Job job, IJobProgress progress, CancellationToken ct)
    {
        var payload = JobQueue.ReadPayload<EmailJobPayload>(job);
        var data = JsonSerializer.Deserialize<EmailData>(payload.DataJson) ?? throw new InvalidOperationException("Empty email template data.");
        var now = clock.GetUtcNow();

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

        if (emailStatus.Settings is null)
        {
            // Same rule as every other optional provider (ElevenLabs, Stripe...): absent configuration means the
            // feature stays off, never a broken send retried forever.
            await MarkAsync(EmailLogStatus.Failed, "Outgoing email is not configured for this server (MindAttic:Vault:Notifications:email).");
            log.LogWarning("Gadfly notification email ({Kind}) not sent: SMTP is not configured.", payload.Kind);
            await progress.ReportAsync(1, "email not configured", ct);
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
