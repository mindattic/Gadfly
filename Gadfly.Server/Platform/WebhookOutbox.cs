using System.Text.Json;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Platform;

/// <summary>
/// Transactional outbox for webhooks (GAD-T71): stages the deliveries and their delivery jobs on the
/// caller's DbContext without saving, so they commit in the same SaveChanges as the change they
/// announce (e.g. the session report). If that save fails, no webhook goes out; if it succeeds, the
/// deliveries are guaranteed to be queued.
/// </summary>
public static class WebhookOutbox
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<int> StageAsync(GadflyDbContext db, Guid organizationId, string eventType, object data, DateTimeOffset now, CancellationToken ct)
    {
        var hooks = await db.Webhooks.IgnoreQueryFilters().AsNoTracking()
            .Where(w => w.OrganizationId == organizationId && w.IsActive).ToListAsync(ct);
        var payload = JsonSerializer.Serialize(new { type = eventType, createdAt = now, data }, Camel);
        var staged = 0;
        foreach (var hook in hooks.Where(h => h.Events.Split(',', StringSplitOptions.TrimEntries).Contains(eventType)))
        {
            var delivery = new WebhookDelivery
            {
                OrganizationId = organizationId, WebhookId = hook.Id, EventType = eventType, PayloadJson = payload,
                Status = DeliveryStatus.Pending, CreatedAt = now,
            };
            db.WebhookDeliveries.Add(delivery);
            db.Jobs.Add(new Job
            {
                OrganizationId = organizationId,
                Kind = WebhookDeliveryJob.JobKind,
                PayloadJson = JsonSerializer.Serialize(new WebhookDeliveryRequest(delivery.Id)),
                State = JobState.Queued,
                MaxAttempts = 8,
                RunAfter = now,
                CreatedAt = now,
            });
            staged++;
        }
        return staged;
    }
}
