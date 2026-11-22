using Microsoft.Extensions.Configuration;

namespace Gadfly.Server.Platform;

/// <summary>
/// Card checkout (GAD-T372), read from the MindAttic.Vault <c>Payments</c> credential bucket the same way
/// ElevenLabs and outgoing email read theirs (Voice/VoiceServices.cs, MindAttic.Authentication's AuthEmailSettings):
/// <code>
/// MindAttic:Vault:Payments:stripe:secretKey        # sk_live_... / sk_test_...
/// MindAttic:Vault:Payments:stripe:webhookSecret     # whsec_...
/// MindAttic:Vault:Payments:stripe:priceStandard     # a Stripe Price id
/// MindAttic:Vault:Payments:stripe:priceInstitution
/// MindAttic:Vault:Payments:stripe:publicBaseUrl     # absolute, for the checkout success/cancel redirect
/// </code>
/// Without a secret key, <see cref="Available"/> is false and the owner's plan page shows no checkout button at
/// all — never a button that leads to an error (the same rule voice and avatar providers follow).
/// </summary>
public sealed class PaymentSettings
{
    public bool Available { get; }
    public string? SecretKey { get; }
    public string? WebhookSecret { get; }
    public string? PublicBaseUrl { get; }
    private readonly Dictionary<string, string> _prices;

    public PaymentSettings(IConfiguration configuration)
    {
        var s = configuration.GetSection("MindAttic:Vault:Payments:stripe");
        SecretKey = s["secretKey"];
        WebhookSecret = s["webhookSecret"];
        PublicBaseUrl = s["publicBaseUrl"];
        _prices = new Dictionary<string, string>(StringComparer.Ordinal);
        if (s["priceStandard"] is { Length: > 0 } std) _prices[PlanCatalog.Standard] = std;
        if (s["priceInstitution"] is { Length: > 0 } inst) _prices[PlanCatalog.Institution] = inst;
        Available = !string.IsNullOrWhiteSpace(SecretKey);
    }

    /// <summary>The Stripe Price id for a plan an owner can self-serve checkout for, or null (Trial/Custom are never self-serve).</summary>
    public string? PriceFor(string plan) => _prices.GetValueOrDefault(plan);

    /// <summary>Plans an owner can start checkout for right now (a configured price exists).</summary>
    public IReadOnlyList<string> CheckoutablePlans => _prices.Keys.ToList();
}
