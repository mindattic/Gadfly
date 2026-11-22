using System.Net;
using System.Security.Cryptography;
using System.Text;
using Gadfly.Server.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Card checkout (GAD-T372, under GAD-T399), pure: without payment keys, or for a plan with no price, nothing ever reaches Stripe
/// (the HTTP client factory is never even asked for a client); with keys the Checkout Session request carries the plan and the
/// organization; and a webhook is accepted only with a fresh, correct signature for a real plan. No test talks to Stripe: the
/// HTTP handler is a recording fake.
/// </summary>
[TestFixture]
public class CheckoutServiceTests
{
    private const string WebhookSecret = "whsec_test_secret";

    private static PaymentSettings Settings(params (string Key, string Value)[] values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => "MindAttic:Vault:Payments:stripe:" + v.Key, v => (string?)v.Value)).Build());

    private static PaymentSettings Configured() =>
        Settings(("secretKey", "sk_test_x"), ("webhookSecret", WebhookSecret), ("priceStandard", "price_std"), ("publicBaseUrl", "https://gadfly.example/"));

    private static (CheckoutService Service, RecordingFactory Factory) Service(PaymentSettings settings, HttpStatusCode status = HttpStatusCode.OK,
        string body = """{"id":"cs_test_1","url":"https://checkout.stripe.com/c/pay/cs_test_1"}""")
    {
        var factory = new RecordingFactory(status, body);
        return (new CheckoutService(factory, settings, NullLogger<CheckoutService>.Instance), factory);
    }

    // ------------------------------------------------------------------ starting a checkout

    [Test]
    public async Task Without_a_secret_key_no_call_is_made_and_the_owner_is_told_checkout_is_off()
    {
        var (service, factory) = Service(Settings(("priceStandard", "price_std")));
        var start = await service.StartAsync(Guid.NewGuid(), PlanCatalog.Standard, "https://localhost", CancellationToken.None);
        Assert.That(start.Ok, Is.False);
        Assert.That(start.Url, Is.Null);
        Assert.That(start.Error, Does.Contain("not configured"));
        Assert.That(factory.ClientsCreated, Is.Zero, "a server without payment keys asked for an HTTP client");
        Assert.That(factory.Requests, Is.Empty);
    }

    [Test]
    public async Task A_plan_with_no_price_is_refused_without_a_call()
    {
        var (service, factory) = Service(Configured());
        var start = await service.StartAsync(Guid.NewGuid(), PlanCatalog.Institution, "https://localhost", CancellationToken.None);
        Assert.That(start.Ok, Is.False);
        Assert.That(start.Error, Does.Contain("not set up for card checkout"));
        Assert.That(factory.Requests, Is.Empty);
    }

    [Test]
    public async Task With_keys_the_session_request_names_the_price_the_organization_and_the_return_pages()
    {
        var org = Guid.NewGuid();
        var (service, factory) = Service(Configured());
        var start = await service.StartAsync(org, PlanCatalog.Standard, "https://ignored.example", CancellationToken.None);
        Assert.That(start.Ok, Is.True, start.Error);
        Assert.That(start.Url, Is.EqualTo("https://checkout.stripe.com/c/pay/cs_test_1"));

        var request = factory.Requests.Single();
        Assert.That(request.Method, Is.EqualTo("POST"));
        Assert.That(request.Uri, Is.EqualTo("https://api.stripe.com/v1/checkout/sessions"));
        Assert.That(request.Authorization, Is.EqualTo("Bearer sk_test_x"));
        var form = request.Form;
        Assert.That(form["mode"], Is.EqualTo("subscription"));
        Assert.That(form["line_items[0][price]"], Is.EqualTo("price_std"));
        Assert.That(form["client_reference_id"], Is.EqualTo(org.ToString("D")));
        Assert.That(form["metadata[organizationId]"], Is.EqualTo(org.ToString("D")));
        Assert.That(form["metadata[plan]"], Is.EqualTo(PlanCatalog.Standard));
        Assert.That(form["success_url"], Is.EqualTo("https://gadfly.example/org/plan?checkout=success"), "the configured public address wins over the request's");
        Assert.That(form["cancel_url"], Is.EqualTo("https://gadfly.example/org/plan?checkout=cancelled"));
    }

    [Test]
    public async Task Without_a_configured_public_address_the_return_pages_use_the_request_address()
    {
        var (service, factory) = Service(Settings(("secretKey", "sk_test_x"), ("priceStandard", "price_std")));
        await service.StartAsync(Guid.NewGuid(), PlanCatalog.Standard, "https://school.example/", CancellationToken.None);
        Assert.That(factory.Requests.Single().Form["success_url"], Is.EqualTo("https://school.example/org/plan?checkout=success"));
    }

    [Test]
    public async Task A_Stripe_error_becomes_a_plain_try_again_message()
    {
        var (service, _) = Service(Configured(), HttpStatusCode.BadRequest, """{"error":{"message":"No such price"}}""");
        var start = await service.StartAsync(Guid.NewGuid(), PlanCatalog.Standard, "https://localhost", CancellationToken.None);
        Assert.That(start.Ok, Is.False);
        Assert.That(start.Error, Does.Contain("try again"));
        Assert.That(start.Error, Does.Not.Contain("No such price"), "Stripe's own error text is logged, not shown");
    }

    [Test]
    public async Task A_reply_without_a_redirect_address_is_refused()
    {
        var (service, _) = Service(Configured(), body: """{"id":"cs_test_1"}""");
        var start = await service.StartAsync(Guid.NewGuid(), PlanCatalog.Standard, "https://localhost", CancellationToken.None);
        Assert.That(start.Ok, Is.False);
        Assert.That(start.Error, Does.Contain("no redirect URL"));
    }

    // ------------------------------------------------------------------ the webhook

    private static string Completed(Guid org, string plan = PlanCatalog.Standard, string type = "checkout.session.completed") =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type,
            data = new { @object = new { client_reference_id = org.ToString("D"), metadata = new { organizationId = org.ToString("D"), plan } } },
        });

    private static string Sign(string payload, long? at = null, string secret = WebhookSecret)
    {
        var t = (at ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();
        var v1 = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{t}.{payload}")));
        return $"t={t},v1={v1}";
    }

    [Test]
    public void A_fresh_correctly_signed_completion_names_the_organization_and_plan()
    {
        var org = Guid.NewGuid();
        var payload = Completed(org);
        var (service, factory) = Service(Configured());
        var result = service.VerifyAndParse(payload, Sign(payload));
        Assert.That(result, Is.EqualTo(new CheckoutCompleted(org, PlanCatalog.Standard)));
        Assert.That(factory.Requests, Is.Empty, "verifying a webhook calls nobody");
    }

    [Test]
    public void A_wrong_signature_is_rejected()
    {
        var payload = Completed(Guid.NewGuid());
        var (service, _) = Service(Configured());
        Assert.That(service.VerifyAndParse(payload, Sign(payload, secret: "whsec_someone_else")), Is.Null);
    }

    [Test]
    public void A_payload_edited_after_signing_is_rejected()
    {
        var org = Guid.NewGuid();
        var signature = Sign(Completed(org, PlanCatalog.Standard));
        var (service, _) = Service(Configured());
        Assert.That(service.VerifyAndParse(Completed(org, PlanCatalog.Institution), signature), Is.Null);
    }

    [Test]
    public void A_replayed_webhook_older_than_five_minutes_is_rejected()
    {
        var payload = Completed(Guid.NewGuid());
        var (service, _) = Service(Configured());
        Assert.That(service.VerifyAndParse(payload, Sign(payload, DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds())), Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("v1=abc")]
    [TestCase("t=123")]
    public void A_missing_or_malformed_signature_header_is_rejected(string? header)
    {
        var (service, _) = Service(Configured());
        Assert.That(service.VerifyAndParse(Completed(Guid.NewGuid()), header), Is.Null);
    }

    [Test]
    public void Other_event_types_and_unknown_plans_are_ignored()
    {
        var (service, _) = Service(Configured());
        var other = Completed(Guid.NewGuid(), type: "invoice.paid");
        Assert.That(service.VerifyAndParse(other, Sign(other)), Is.Null);
        var gold = Completed(Guid.NewGuid(), plan: "Gold");
        Assert.That(service.VerifyAndParse(gold, Sign(gold)), Is.Null);
    }

    [Test]
    public void Without_payment_keys_or_a_webhook_secret_every_webhook_is_rejected()
    {
        var payload = Completed(Guid.NewGuid());
        var (noKeys, _) = Service(Settings(("webhookSecret", WebhookSecret)));
        Assert.That(noKeys.VerifyAndParse(payload, Sign(payload)), Is.Null);
        var (noSecret, _) = Service(Settings(("secretKey", "sk_test_x")));
        Assert.That(noSecret.VerifyAndParse(payload, Sign(payload)), Is.Null);
    }

    // ------------------------------------------------------------------ fakes

    private sealed record Recorded(string Method, string Uri, string? Authorization, Dictionary<string, string> Form);

    /// <summary>Hands out clients whose handler records each request and answers with a canned reply; never touches the network.</summary>
    private sealed class RecordingFactory(HttpStatusCode status, string body) : IHttpClientFactory
    {
        public HttpStatusCode Status { get; } = status;
        public string Body { get; } = body;
        public int ClientsCreated { get; private set; }
        public List<Recorded> Requests { get; } = [];

        public HttpClient CreateClient(string name)
        {
            ClientsCreated++;
            return new HttpClient(new Handler(this), disposeHandler: true);
        }

        private sealed class Handler(RecordingFactory owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var text = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
                var form = text.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2))
                    .ToDictionary(kv => WebUtility.UrlDecode(kv[0]), kv => kv.Length > 1 ? WebUtility.UrlDecode(kv[1]) : "");
                owner.Requests.Add(new Recorded(request.Method.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), form));
                return new HttpResponseMessage(owner.Status) { Content = new StringContent(owner.Body, Encoding.UTF8, "application/json") };
            }
        }
    }
}
