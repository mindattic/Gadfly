using Gadfly.Server.Platform;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Card checkout settings (GAD-T372): without a secret key checkout is unavailable (no button that leads to an error), and
/// only plans with a configured price can be checked out; Trial and Custom never self-serve.
/// </summary>
[TestFixture]
public class PaymentSettingsTests
{
    private static PaymentSettings Settings(params (string Key, string Value)[] values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => "MindAttic:Vault:Payments:stripe:" + v.Key, v => (string?)v.Value)).Build());

    [Test]
    public void NoSecretKey_IsUnavailable()
    {
        var s = Settings(("priceStandard", "price_1"));
        Assert.That(s.Available, Is.False);
    }

    [Test]
    public void BlankSecretKey_IsUnavailable()
    {
        Assert.That(Settings(("secretKey", "   ")).Available, Is.False);
    }

    [Test]
    public void SecretKey_MakesCheckoutAvailable()
    {
        var s = Settings(("secretKey", "sk_test_x"), ("webhookSecret", "whsec_y"), ("publicBaseUrl", "https://gadfly.example"));
        Assert.That(s.Available, Is.True);
        Assert.That(s.WebhookSecret, Is.EqualTo("whsec_y"));
        Assert.That(s.PublicBaseUrl, Is.EqualTo("https://gadfly.example"));
    }

    [Test]
    public void OnlyPlansWithAPrice_AreCheckoutable()
    {
        var s = Settings(("secretKey", "sk_test_x"), ("priceStandard", "price_std"), ("priceInstitution", ""));
        Assert.That(s.PriceFor(PlanCatalog.Standard), Is.EqualTo("price_std"));
        Assert.That(s.PriceFor(PlanCatalog.Institution), Is.Null);
        Assert.That(s.CheckoutablePlans, Is.EqualTo(new[] { PlanCatalog.Standard }));
    }

    [Test]
    public void TrialAndUnknownPlans_HaveNoPrice()
    {
        var s = Settings(("secretKey", "sk_test_x"), ("priceStandard", "price_std"), ("priceInstitution", "price_inst"));
        Assert.That(s.PriceFor("Trial"), Is.Null);
        Assert.That(s.PriceFor("nonsense"), Is.Null);
        Assert.That(s.CheckoutablePlans, Is.EquivalentTo(new[] { PlanCatalog.Standard, PlanCatalog.Institution }));
    }
}
