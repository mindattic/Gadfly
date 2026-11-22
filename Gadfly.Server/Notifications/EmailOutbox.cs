using System.Text.Json;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Notifications;

/// <summary>One "notify.email" job's payload.</summary>
public sealed record EmailJobPayload(Guid EmailLogId, string Kind, Guid OrganizationId, Guid? ToUserId, string? ToEmailOverride, string DataJson);

/// <summary>
/// Transactional outbox for Gadfly's own notification emails (GAD-T310), the same shape as
/// <see cref="Gadfly.Server.Platform.WebhookOutbox"/>: stages the "notify.email" job and its EmailLog row on the
/// caller's own DbContext without saving, so they commit in the same SaveChanges as the event they are about (an
/// assignment created, a result published...). Gated here: the organization's on/off switch, and (for reminders
/// only) the student's own opt-out (GAD-T375); a skip is still recorded in the log, with a reason, so the admin
/// preview page can show why nothing went out.
/// </summary>
public static class EmailOutbox
{
    public static async Task<bool> StageAsync(GadflyDbContext db, Guid organizationId, string kind, Guid studentUserId, EmailData data,
        DateTimeOffset now, CancellationToken ct, Guid? relatedId = null)
    {
        var settings = await db.OrganizationSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.OrganizationId == organizationId).Select(s => (bool?)s.EmailNotificationsEnabled).FirstOrDefaultAsync(ct);
        if (settings == false)
        {
            db.Set<EmailLog>().Add(new EmailLog
            {
                OrganizationId = organizationId, Kind = kind, ToUserId = studentUserId, RelatedId = relatedId,
                Status = EmailLogStatus.Skipped, Error = "This organization has notification emails turned off.", CreatedAt = now,
            });
            return false;
        }
        if (kind == EmailKinds.Reminder)
        {
            var optedOut = await db.Set<StudentNotificationPreference>().IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.OrganizationId == organizationId && p.UserId == studentUserId)
                .Select(p => (bool?)p.RemindersOptedOut).FirstOrDefaultAsync(ct);
            if (optedOut == true)
            {
                db.Set<EmailLog>().Add(new EmailLog
                {
                    OrganizationId = organizationId, Kind = kind, ToUserId = studentUserId, RelatedId = relatedId,
                    Status = EmailLogStatus.Skipped, Error = "This student opted out of reminder emails.", CreatedAt = now,
                });
                return false;
            }
        }

        var log = new EmailLog { OrganizationId = organizationId, Kind = kind, ToUserId = studentUserId, RelatedId = relatedId, Status = EmailLogStatus.Queued, CreatedAt = now };
        db.Set<EmailLog>().Add(log);
        var payload = new EmailJobPayload(log.Id, kind, organizationId, studentUserId, null, JsonSerializer.Serialize(data));
        db.Jobs.Add(new Job
        {
            OrganizationId = organizationId,
            Kind = EmailDeliveryJob.JobKind,
            PayloadJson = JsonSerializer.Serialize(payload),
            State = JobState.Queued,
            MaxAttempts = 5,
            RunAfter = now,
            CreatedAt = now,
        });
        return true;
    }

    /// <summary>A test send from the admin preview page (GAD-T376): never gated, always to the address given, logged under <see cref="EmailKinds.Test"/> or the kind being previewed.</summary>
    public static void StageTest(GadflyDbContext db, Guid organizationId, string kind, string toEmail, EmailData data, DateTimeOffset now)
    {
        var log = new EmailLog { OrganizationId = organizationId, Kind = kind, ToEmail = toEmail, Status = EmailLogStatus.Queued, CreatedAt = now };
        db.Set<EmailLog>().Add(log);
        var payload = new EmailJobPayload(log.Id, kind, organizationId, null, toEmail, JsonSerializer.Serialize(data));
        db.Jobs.Add(new Job
        {
            OrganizationId = organizationId,
            Kind = EmailDeliveryJob.JobKind,
            PayloadJson = JsonSerializer.Serialize(payload),
            State = JobState.Queued,
            MaxAttempts = 2,
            RunAfter = now,
            CreatedAt = now,
        });
    }
}
