using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gadfly.Server.Platform;

/// <summary>One outcome of starting a checkout: either a URL to send the owner to, or why not.</summary>
public sealed record CheckoutStart(bool Ok, string? Url, string? Error);

/// <summary>What a verified <c>checkout.session.completed</c> webhook said.</summary>
public sealed record CheckoutCompleted(Guid OrganizationId, string Plan);

/// <summary>
/// Card checkout for self-serve plan upgrades (GAD-T372), talking to Stripe's REST API directly over
/// <see cref="HttpClient"/> (no SDK dependency, mirroring how Gadfly already calls ElevenLabs). Every call first
/// checks <see cref="PaymentSettings.Available"/>; the caller (CheckoutEndpoints) never reaches this service
/// otherwise, so a server with no payment keys configured never makes an outbound call here.
/// </summary>
public sealed class CheckoutService(IHttpClientFactory httpClientFactory, PaymentSettings settings, ILogger<CheckoutService> log)
{
    private const string ApiBase = "https://api.stripe.com/v1";

    /// <summary>Starts a Checkout Session for one organization upgrading to <paramref name="plan"/>. The owner is redirected to the returned URL.</summary>
    public async Task<CheckoutStart> StartAsync(Guid organizationId, string plan, string returnBaseUrl, CancellationToken ct)
    {
        if (!settings.Available) return new(false, null, "Card checkout is not configured on this server.");
        var price = settings.PriceFor(plan);
        if (price is null) return new(false, null, $"The {plan} plan is not set up for card checkout. Contact us to upgrade.");

        var baseUrl = settings.PublicBaseUrl is { Length: > 0 } configured ? configured.TrimEnd('/') : returnBaseUrl.TrimEnd('/');
        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["line_items[0][price]"] = price,
            ["line_items[0][quantity]"] = "1",
            ["success_url"] = $"{baseUrl}/org/plan?checkout=success",
            ["cancel_url"] = $"{baseUrl}/org/plan?checkout=cancelled",
            ["client_reference_id"] = organizationId.ToString("D"),
            ["metadata[organizationId]"] = organizationId.ToString("D"),
            ["metadata[plan]"] = plan,
        };

        var client = httpClientFactory.CreateClient("StripeCheckout");
        client.BaseAddress ??= new Uri(ApiBase + "/");
        using var request = new HttpRequestMessage(HttpMethod.Post, "checkout/sessions") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.SecretKey);
        try
        {
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("Stripe checkout session creation failed ({Status}): {Body}", (int)response.StatusCode, body);
                return new(false, null, "Checkout could not be started. Please try again in a moment.");
            }
            using var doc = JsonDocument.Parse(body);
            var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
            return url is { Length: > 0 } ? new(true, url, null) : new(false, null, "Checkout could not be started: no redirect URL was returned.");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Stripe checkout session creation threw.");
            return new(false, null, "Checkout could not be started. Please try again in a moment.");
        }
    }

    /// <summary>
    /// Verifies a Stripe webhook's signature (the <c>Stripe-Signature</c> header: <c>t=&lt;unix seconds&gt;,v1=&lt;hex hmac&gt;</c>,
    /// over <c>"{t}.{payload}"</c> with the webhook secret) and, for <c>checkout.session.completed</c>, returns what to apply.
    /// Rejects a timestamp more than 5 minutes old (replay protection) and any signature mismatch.
    /// </summary>
    public CheckoutCompleted? VerifyAndParse(string payload, string? signatureHeader)
    {
        if (!settings.Available || string.IsNullOrWhiteSpace(settings.WebhookSecret)) return null;
        if (string.IsNullOrWhiteSpace(signatureHeader)) return null;

        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t") timestamp = kv[1];
            else if (kv[0] == "v1") signatures.Add(kv[1]);
        }
        if (timestamp is null || signatures.Count == 0) return null;
        if (!long.TryParse(timestamp, out var t) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - t) > 300) return null;

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}")));
        if (!signatures.Any(s => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(s), Encoding.UTF8.GetBytes(expected)))) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.GetProperty("type").GetString() != "checkout.session.completed") return null;
            var dataObject = root.GetProperty("data").GetProperty("object");
            var metadata = dataObject.TryGetProperty("metadata", out var m) ? m : default;
            var orgText = metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("organizationId", out var o) ? o.GetString()
                : dataObject.TryGetProperty("client_reference_id", out var cr) ? cr.GetString() : null;
            var plan = metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("plan", out var p) ? p.GetString() : null;
            if (!Guid.TryParse(orgText, out var orgId) || string.IsNullOrWhiteSpace(plan) || !PlanCatalog.IsValid(plan)) return null;
            return new CheckoutCompleted(orgId, plan);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
