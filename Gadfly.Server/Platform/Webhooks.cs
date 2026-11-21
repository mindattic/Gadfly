using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Platform;

/// <summary>
/// Outgoing webhooks (GAD-T46). Each delivery is a background job; the body is signed with the
/// webhook's secret: header <c>X-Gadfly-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256 of "t.body"&gt;</c>.
/// Receivers should reject signatures older than five minutes.
/// </summary>
public sealed class WebhookService(GadflyDbContext db, JobQueue jobs, IDataProtectionProvider protection, TimeProvider clock)
{
    public const string SessionCompleted = "session.completed";
    public static readonly IReadOnlyList<string> Events = [SessionCompleted];

    /// <summary>The longest URL the webhook table stores.</summary>
    public const int MaxUrlLength = 1000;

    private IDataProtector Protector => protection.CreateProtector("Gadfly.Webhooks.Secret");

    /// <summary>A webhook's subscribed events as a list (stored comma-separated); the API always returns this array.</summary>
    public static IReadOnlyList<string> EventList(string? stored) =>
        (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Registers a webhook for the caller's organization and returns its signing secret (shown once).</summary>
    public async Task<(Webhook Row, string Secret)> CreateAsync(Guid organizationId, string? url, IEnumerable<string?>? events, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Webhook URLs must be absolute https URLs.");
        if (uri.ToString().Length > MaxUrlLength)
            throw new ArgumentException($"Webhook URLs must be at most {MaxUrlLength} characters.");
        if (WebhookTargets.Problem(uri) is { } problem) throw new ArgumentException(problem);
        var wanted = (events ?? []).OfType<string>().Select(e => e.Trim()).Where(e => Events.Contains(e)).Distinct().ToList();
        if (wanted.Count == 0) throw new ArgumentException($"Subscribe to at least one of: {string.Join(", ", Events)}.");
        var secret = "whsec_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var row = new Webhook
        {
            OrganizationId = organizationId, Url = uri.ToString(), Events = string.Join(",", wanted),
            ProtectedSecret = Protector.Protect(secret), CreatedAt = clock.GetUtcNow(),
        };
        db.Webhooks.Add(row);
        await db.SaveChangesAsync(ct);
        return (row, secret);
    }

    /// <summary>Queues a delivery to every active webhook of the organization subscribed to <paramref name="eventType"/>.</summary>
    public async Task PublishAsync(Guid organizationId, string eventType, object data, CancellationToken ct = default)
    {
        var hooks = await db.Webhooks.IgnoreQueryFilters().AsNoTracking()
            .Where(w => w.OrganizationId == organizationId && w.IsActive).ToListAsync(ct);
        var payload = JsonSerializer.Serialize(new { type = eventType, createdAt = clock.GetUtcNow(), data },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        foreach (var hook in hooks.Where(h => EventList(h.Events).Contains(eventType)))
        {
            var delivery = new WebhookDelivery
            {
                OrganizationId = organizationId, WebhookId = hook.Id, EventType = eventType, PayloadJson = payload,
                Status = DeliveryStatus.Pending, CreatedAt = clock.GetUtcNow(),
            };
            db.WebhookDeliveries.Add(delivery);
            await db.SaveChangesAsync(ct);
            await jobs.EnqueueAsync(WebhookDeliveryJob.JobKind, new WebhookDeliveryRequest(delivery.Id), organizationId, maxAttempts: 8, ct: ct);
        }
    }

    internal string Unprotect(string protectedSecret) => Protector.Unprotect(protectedSecret);

    public static string Sign(string secret, long timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
    }
}

public sealed record WebhookDeliveryRequest(Guid DeliveryId);

/// <summary>The "webhook.deliver" job: POSTs one delivery; a non-2xx answer throws so the queue retries with backoff.</summary>
public sealed class WebhookDeliveryJob(GadflyDbContext db, WebhookService webhooks, IHttpClientFactory http, TimeProvider clock) : IJobHandler
{
    public const string JobKind = "webhook.deliver";
    public string Kind => JobKind;

    public async Task RunAsync(Job job, IJobProgress progress, CancellationToken ct)
    {
        var request = JobQueue.ReadPayload<WebhookDeliveryRequest>(job);
        var delivery = await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == request.DeliveryId, ct);
        if (delivery is null || delivery.Status == DeliveryStatus.Delivered) return;
        var hook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == delivery.WebhookId, ct);
        // A hook registered before the target rules (or whose URL no longer passes them) is never called (GAD-T75).
        if (hook is null || !hook.IsActive || !Uri.TryCreate(hook.Url, UriKind.Absolute, out var target)
            || target.Scheme != Uri.UriSchemeHttps || WebhookTargets.Problem(target) is not null)
        {
            delivery.Status = DeliveryStatus.GaveUp;
            await db.SaveChangesAsync(ct);
            return;
        }

        var ts = clock.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookService.Sign(webhooks.Unprotect(hook.ProtectedSecret), ts, delivery.PayloadJson);
        using var message = new HttpRequestMessage(HttpMethod.Post, hook.Url)
        {
            Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("X-Gadfly-Signature", $"t={ts},v1={signature}");
        message.Headers.Add("X-Gadfly-Event", delivery.EventType);
        message.Headers.Add("X-Gadfly-Delivery", delivery.Id.ToString("N"));
        message.Headers.UserAgent.Add(new ProductInfoHeaderValue("Gadfly-Webhooks", "1"));

        delivery.Attempt = job.Attempts;
        HttpResponseMessage? response = null;
        try
        {
            response = await http.CreateClient("webhooks").SendAsync(message, ct);
            delivery.ResponseCode = (int)response.StatusCode;
            delivery.Status = response.IsSuccessStatusCode ? DeliveryStatus.Delivered
                : job.Attempts >= job.MaxAttempts ? DeliveryStatus.GaveUp : DeliveryStatus.Failed;
        }
        catch (Exception) when (job.Attempts >= job.MaxAttempts)
        {
            delivery.Status = DeliveryStatus.GaveUp;
        }
        finally
        {
            await db.SaveChangesAsync(CancellationToken.None);
            response?.Dispose();
        }
        if (delivery.Status != DeliveryStatus.Delivered && delivery.Status != DeliveryStatus.GaveUp)
            throw new HttpRequestException($"Webhook answered {delivery.ResponseCode}; will retry.");
    }
}
