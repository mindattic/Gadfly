using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Webhook targets (GAD-T75): what a webhook may and may not point at. Pure, no database.</summary>
[TestFixture]
public class WebhookTargetTests
{
    [SetUp]
    public void Strict() => WebhookTargets.AllowPrivate = false;

    [TestCase("0.0.0.1")]
    [TestCase("10.1.2.3")]
    [TestCase("100.64.0.1")]
    [TestCase("100.127.255.255")]
    [TestCase("127.0.0.1")]
    [TestCase("169.254.169.254")]
    [TestCase("172.16.0.1")]
    [TestCase("172.31.255.255")]
    [TestCase("192.0.0.8")]
    [TestCase("192.0.2.1")]
    [TestCase("192.88.99.1")]
    [TestCase("192.168.1.1")]
    [TestCase("198.18.0.1")]
    [TestCase("198.19.255.1")]
    [TestCase("198.51.100.7")]
    [TestCase("203.0.113.9")]
    [TestCase("224.0.0.1")]
    [TestCase("255.255.255.255")]
    [TestCase("::1")]
    [TestCase("::")]
    [TestCase("fe80::1")]
    [TestCase("fc00::1")]
    [TestCase("fd12:3456::1")]
    [TestCase("ff02::1")]
    [TestCase("2001:db8::1")]
    [TestCase("::ffff:10.0.0.1")]
    [TestCase("::ffff:169.254.169.254")]
    [TestCase("2002:0a00:0001::1")]
    [TestCase("64:ff9b::7f00:1")]
    public void NonPublicAddresses(string ip) => Assert.That(WebhookTargets.IsPublic(IPAddress.Parse(ip)), Is.False, ip);

    [TestCase("8.8.8.8")]
    [TestCase("1.1.1.1")]
    [TestCase("100.63.255.255")]
    [TestCase("100.128.0.1")]
    [TestCase("172.15.0.1")]
    [TestCase("172.32.0.1")]
    [TestCase("192.169.0.1")]
    [TestCase("223.255.255.254")]
    [TestCase("2606:4700:4700::1111")]
    [TestCase("::ffff:8.8.8.8")]
    [TestCase("2002:0808:0808::1")]
    [TestCase("64:ff9b::808:808")]
    public void PublicAddresses(string ip) => Assert.That(WebhookTargets.IsPublic(IPAddress.Parse(ip)), Is.True, ip);

    [TestCase("https://localhost/hook")]
    [TestCase("https://LOCALHOST./hook")]
    [TestCase("https://api.localhost/hook")]
    [TestCase("https://printer.local/hook")]
    [TestCase("https://metadata.google.internal/computeMetadata")]
    [TestCase("https://intranet/hook")]
    [TestCase("https://127.0.0.1/hook")]
    [TestCase("https://169.254.169.254/latest/meta-data")]
    [TestCase("https://[::1]/hook")]
    [TestCase("https://[fd00::1]:8443/hook")]
    [TestCase("https://10.0.0.5:8443/hook")]
    public void RefusedUrls(string url) => Assert.That(WebhookTargets.Problem(new Uri(url)), Is.Not.Null, url);

    [TestCase("https://hooks.example.com/gadfly")]
    [TestCase("https://8.8.8.8/hook")]
    [TestCase("https://[2606:4700:4700::1111]/hook")]
    public void AcceptedUrls(string url) => Assert.That(WebhookTargets.Problem(new Uri(url)), Is.Null, url);

    [Test]
    public void DevelopmentOverride_AllowsPrivateTargets()
    {
        try
        {
            WebhookTargets.AllowPrivate = true;
            Assert.That(WebhookTargets.Problem(new Uri("https://localhost/hook")), Is.Null);
        }
        finally
        {
            WebhookTargets.AllowPrivate = false;
        }
    }

    [Test]
    public void DeliveryHandler_FollowsNoRedirects_UsesNoProxy()
    {
        using var handler = WebhookTargets.CreateHandler();
        Assert.That(handler.AllowAutoRedirect, Is.False);
        Assert.That(handler.UseProxy, Is.False);
        Assert.That(handler.ConnectCallback, Is.Not.Null);
    }

    [Test]
    public void Signature_IsHmacSha256OfTimestampDotBody()
    {
        const string secret = "whsec_test";
        const string body = "{\"type\":\"session.completed\"}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes("1700000000." + body)));
        Assert.That(WebhookService.Sign(secret, 1700000000, body), Is.EqualTo(expected));
        Assert.That(WebhookService.Sign(secret, 1700000001, body), Is.Not.EqualTo(expected), "the timestamp is signed");
        Assert.That(WebhookService.Sign("other", 1700000000, body), Is.Not.EqualTo(expected));
        Assert.That(WebhookService.Sign(secret, 1700000000, body + " "), Is.Not.EqualTo(expected));
    }

    [TestCase(null, new string[0])]
    [TestCase("", new string[0])]
    [TestCase("session.completed", new[] { "session.completed" })]
    [TestCase(" a , ,b ", new[] { "a", "b" })]
    public void EventList(string? stored, string[] expected) => Assert.That(WebhookService.EventList(stored), Is.EqualTo(expected));

    [Test]
    public void ApiKeyHash_IsLowercaseSha256Hex()
    {
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("gfk_abc")));
        Assert.That(ApiKeyService.Hash("gfk_abc"), Is.EqualTo(expected));
    }
}

[TestFixture]
public class WebhookServiceTests : ServerTest
{
    [SetUp]
    public void Strict() => WebhookTargets.AllowPrivate = false;

    private static async Task<T> With<T>(Guid org, Func<WebhookService, GadflyDbContext, Task<T>> act)
    {
        await using var scope = Scope(TenantSnapshot.ForOrganization(org));
        return await act(scope.ServiceProvider.GetRequiredService<WebhookService>(), scope.ServiceProvider.GetRequiredService<GadflyDbContext>());
    }

    [TestCase("http://hooks.example.com/x", "absolute https")]
    [TestCase("not a url", "absolute https")]
    [TestCase("", "absolute https")]
    [TestCase("ftp://hooks.example.com/x", "absolute https")]
    [TestCase("https://localhost/x", "public host")]
    [TestCase("https://192.168.0.10/x", "private")]
    public async Task Create_RefusesBadUrls(string url, string why)
    {
        var org = await Seed.OrganizationAsync();
        var ex = Assert.ThrowsAsync<ArgumentException>(() => With(org, (w, _) => w.CreateAsync(org, url, ["session.completed"])));
        Assert.That(ex!.Message, Does.Contain(why));
    }

    [Test]
    public async Task Create_RefusesOverlongUrls()
    {
        var org = await Seed.OrganizationAsync();
        var url = "https://hooks.example.com/" + new string('a', 1000);
        Assert.ThrowsAsync<ArgumentException>(() => With(org, (w, _) => w.CreateAsync(org, url, ["session.completed"])));
    }

    [Test]
    public async Task Create_NeedsAKnownEvent()
    {
        var org = await Seed.OrganizationAsync();
        Assert.ThrowsAsync<ArgumentException>(() => With(org, (w, _) => w.CreateAsync(org, "https://hooks.example.com/x", ["session.started", null])));
        Assert.ThrowsAsync<ArgumentException>(() => With(org, (w, _) => w.CreateAsync(org, "https://hooks.example.com/x", null)));
    }

    [Test]
    public async Task Create_StoresTheSecretProtected_AndReturnsItOnce()
    {
        var org = await Seed.OrganizationAsync();
        var (row, secret) = await With(org, (w, _) => w.CreateAsync(org, "https://hooks.example.com/x", [" session.completed ", "session.completed", "bogus"]));
        Assert.That(secret, Does.StartWith("whsec_"));
        Assert.That(row.Events, Is.EqualTo("session.completed"));
        await using var db = SystemDb();
        var stored = await db.Webhooks.AsNoTracking().SingleAsync(h => h.Id == row.Id);
        Assert.That(stored.ProtectedSecret, Does.Not.Contain(secret));
        Assert.That(stored.ProtectedSecret, Is.Not.Empty);
    }

    private static async Task<Guid> Hook(Guid org, string events = "session.completed", bool active = true)
    {
        await using var db = SystemDb();
        var h = new Webhook { OrganizationId = org, Url = "https://hooks.example.com/x", Events = events, IsActive = active, ProtectedSecret = "x", CreatedAt = DateTimeOffset.UtcNow };
        db.Webhooks.Add(h);
        await db.SaveChangesAsync();
        return h.Id;
    }

    [Test]
    public async Task Outbox_StagesOnlySubscribedActiveHooksOfTheOrganization_WithoutSaving()
    {
        var org = await Seed.OrganizationAsync();
        var other = await Seed.OrganizationAsync();
        var subscribed = await Hook(org);
        await Hook(org, events: "something.else");
        await Hook(org, active: false);
        await Hook(other);

        await using var db = OrgDb(org);
        var n = await WebhookOutbox.StageAsync(db, org, WebhookService.SessionCompleted, new { sessionId = Guid.Empty, recommendation = "partial" },
            DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.That(n, Is.EqualTo(1));
        var staged = db.ChangeTracker.Entries<WebhookDelivery>().Single().Entity;
        Assert.That(staged.WebhookId, Is.EqualTo(subscribed));
        Assert.That(staged.Status, Is.EqualTo(DeliveryStatus.Pending));
        using var payload = JsonDocument.Parse(staged.PayloadJson);
        Assert.That(payload.RootElement.GetProperty("type").GetString(), Is.EqualTo("session.completed"));
        Assert.That(payload.RootElement.GetProperty("data").GetProperty("recommendation").GetString(), Is.EqualTo("partial"));
        var job = db.ChangeTracker.Entries<Job>().Single().Entity;
        Assert.That(job.Kind, Is.EqualTo(WebhookDeliveryJob.JobKind));
        Assert.That(job.PayloadJson, Does.Contain(staged.Id.ToString()));

        await using (var check = SystemDb())
            Assert.That(await check.WebhookDeliveries.AnyAsync(d => d.WebhookId == subscribed), Is.False, "nothing is written until the caller saves");
        await db.SaveChangesAsync();
        await using (var check = SystemDb())
            Assert.That(await check.WebhookDeliveries.CountAsync(d => d.WebhookId == subscribed), Is.EqualTo(1));
    }

    [Test]
    public async Task Outbox_DiscardedWithTheCallersChanges()
    {
        var org = await Seed.OrganizationAsync();
        var hook = await Hook(org);
        await using (var db = OrgDb(org))
            await WebhookOutbox.StageAsync(db, org, WebhookService.SessionCompleted, new { }, DateTimeOffset.UtcNow, CancellationToken.None);
        await using var check = SystemDb();
        Assert.That(await check.WebhookDeliveries.AnyAsync(d => d.WebhookId == hook), Is.False);
    }

    [Test]
    public async Task Delivery_ToAHookThatNoLongerPassesTheRules_GivesUpWithoutCalling()
    {
        var org = await Seed.OrganizationAsync();
        Guid deliveryId;
        await using (var db = SystemDb())
        {
            var hook = new Webhook { OrganizationId = org, Url = "https://169.254.169.254/latest", Events = "session.completed", ProtectedSecret = "x", CreatedAt = DateTimeOffset.UtcNow };
            db.Webhooks.Add(hook);
            var d = new WebhookDelivery { OrganizationId = org, WebhookId = hook.Id, EventType = "session.completed", Status = DeliveryStatus.Pending, CreatedAt = DateTimeOffset.UtcNow };
            db.WebhookDeliveries.Add(d);
            await db.SaveChangesAsync();
            deliveryId = d.Id;
        }
        await using (var scope = Scope(TenantSnapshot.System))
        {
            var job = ActivatorUtilities.CreateInstance<WebhookDeliveryJob>(scope.ServiceProvider);
            await job.RunAsync(new Job
            {
                Kind = WebhookDeliveryJob.JobKind, PayloadJson = JsonSerializer.Serialize(new WebhookDeliveryRequest(deliveryId)), Attempts = 1,
            }, new NoProgress(), CancellationToken.None);
        }
        await using var check = SystemDb();
        var stored = await check.WebhookDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId);
        Assert.That(stored.Status, Is.EqualTo(DeliveryStatus.GaveUp));
        Assert.That(stored.ResponseCode, Is.Null, "no request was made");
    }

    private sealed class NoProgress : IJobProgress
    {
        public Task ReportAsync(double progress, string? message, CancellationToken ct) => Task.CompletedTask;
    }
}

[TestFixture]
public class ApiKeyAndUsageTests : ServerTest
{
    [Test]
    public async Task CreatedKey_HasTheDocumentedShape_AndOnlyItsHashIsStored()
    {
        var org = await Seed.OrganizationAsync();
        await using var scope = Scope(TenantSnapshot.ForOrganization(org));
        var (row, key) = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync(org, Guid.Empty, "  My integration  ");
        Assert.That(key, Does.Match("^gfk_[0-9a-f]{8}_[A-Za-z0-9_-]{43}$"));
        Assert.That(row.Prefix, Is.EqualTo(key.Split('_')[1]));
        Assert.That(row.Name, Is.EqualTo("My integration"));
        await using var db = SystemDb();
        var stored = await db.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == row.Id);
        Assert.That(stored.KeyHash, Is.EqualTo(ApiKeyService.Hash(key)));
        Assert.That(stored.KeyHash, Does.Not.Contain(key));
    }

    [Test]
    public async Task KeyName_DefaultsAndIsCapped()
    {
        var org = await Seed.OrganizationAsync();
        await using var scope = Scope(TenantSnapshot.ForOrganization(org));
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        Assert.That((await keys.CreateAsync(org, Guid.Empty, " ")).Row.Name, Is.EqualTo("API key"));
        Assert.That((await keys.CreateAsync(org, Guid.Empty, new string('n', 300))).Row.Name.Length, Is.EqualTo(120));
    }

    [Test]
    public async Task Revoke_IsOnceOnly_AndScopedToTheOrganization()
    {
        var org = await Seed.OrganizationAsync();
        var other = await Seed.OrganizationAsync();
        Guid id;
        await using (var scope = Scope(TenantSnapshot.ForOrganization(org)))
            id = (await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync(org, Guid.Empty, "k")).Row.Id;
        await using (var scope = Scope(TenantSnapshot.ForOrganization(other)))
            Assert.That(await scope.ServiceProvider.GetRequiredService<ApiKeyService>().RevokeAsync(id), Is.False, "another organization can't revoke it");
        await using (var scope = Scope(TenantSnapshot.ForOrganization(org)))
        {
            var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
            Assert.That(await keys.RevokeAsync(id), Is.True);
            Assert.That(await keys.RevokeAsync(id), Is.False);
        }
    }

    private static UsageMeter Meter(Dictionary<string, string?>? settings = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build(), Clock);

    [Test]
    public void Prices_PerModelBeatsPerProviderBeatsFallback()
    {
        var m = Meter(new Dictionary<string, string?>
        {
            ["Gadfly:Prices:openai:InputPerMillion"] = "1.5",
            ["Gadfly:Prices:openai:OutputPerMillion"] = "6",
            ["Gadfly:Prices:openai:gpt-x:InputPerMillion"] = "0.1",
            ["Gadfly:Prices:openai:gpt-x:OutputPerMillion"] = "0.2",
            ["Gadfly:Prices:gemini:InputPerMillion"] = "not a number",
            ["Gadfly:Prices:gemini:OutputPerMillion"] = "3",
        });
        Assert.That(m.PriceOf("openai", "gpt-x"), Is.EqualTo(new TokenPrice(0.1m, 0.2m)));
        Assert.That(m.PriceOf("openai", "other"), Is.EqualTo(new TokenPrice(1.5m, 6m)));
        Assert.That(m.PriceOf("gemini", "g"), Is.EqualTo(new TokenPrice(1.25m, 5m)), "an unreadable price falls back");
        Assert.That(m.PriceOf("claude", "c"), Is.EqualTo(new TokenPrice(3m, 15m)));
        Assert.That(m.PriceOf("deepseek", "d"), Is.EqualTo(new TokenPrice(0.3m, 1.2m)));
        Assert.That(m.PriceOf("unknown-provider", "u"), Is.EqualTo(new TokenPrice(1m, 4m)));
    }

    [Test]
    public async Task Stage_GroupsByProviderModelAndKind_WithExactCosts()
    {
        var org = await Seed.OrganizationAsync();
        await using var db = OrgDb(org);
        IReadOnlyList<PanelCall> calls =
        [
            new("claude", "c1", 1_000_000, 0, "judge"),
            new("claude", "c1", 0, 100_000, "judge"),
            new("claude", "c1", 10, 10, "question"),
            new("openai", "o1", 500_000, 500_000, "judge"),
        ];
        Meter().Stage(db, org, null, calls, "exam-step");
        var rows = db.ChangeTracker.Entries<UsageEntry>().Select(e => e.Entity).ToList();
        Assert.That(rows, Has.Count.EqualTo(3));
        var claudeJudge = rows.Single(r => r.Provider == "claude" && r.Purpose == "exam-step:judge");
        Assert.That(claudeJudge.InputUnits, Is.EqualTo(1_000_000));
        Assert.That(claudeJudge.OutputUnits, Is.EqualTo(100_000));
        Assert.That(claudeJudge.CostUsd, Is.EqualTo(3m + 1.5m));
        Assert.That(rows.Single(r => r.Provider == "openai").CostUsd, Is.EqualTo(1.25m + 5m));
        Assert.That(rows.Single(r => r.Purpose == "exam-step:question").CostUsd, Is.EqualTo(0.00018m));
        Assert.That(rows.All(r => r.Kind == UsageKind.Llm && r.OrganizationId == org), Is.True);
    }

    [Test]
    public async Task Stage_TruncatesLongPurposeAndModel()
    {
        var org = await Seed.OrganizationAsync();
        await using var db = OrgDb(org);
        Meter().Stage(db, org, null, [new PanelCall("claude", new string('m', 200), 1, 1, "judge")], new string('p', 100));
        var row = db.ChangeTracker.Entries<UsageEntry>().Single().Entity;
        Assert.That(row.Purpose!.Length, Is.EqualTo(60));
        Assert.That(row.Model!.Length, Is.EqualTo(120));
        Assert.DoesNotThrowAsync(() => db.SaveChangesAsync(), "fits the columns");
    }

    [Test]
    public async Task Month_SumsThisMonthOfThisOrganizationOnly()
    {
        var org = await Seed.OrganizationAsync(quota: 10m);
        var other = await Seed.OrganizationAsync();
        Clock.Now = new DateTimeOffset(2031, 3, 15, 12, 0, 0, TimeSpan.Zero);
        await using (var db = SystemDb())
        {
            db.UsageEntries.AddRange(
                new UsageEntry { OrganizationId = org, Provider = "a", CostUsd = 1.25m, CreatedAt = new DateTimeOffset(2031, 3, 1, 0, 0, 0, TimeSpan.Zero) },
                new UsageEntry { OrganizationId = org, Provider = "a", CostUsd = 2m, CreatedAt = new DateTimeOffset(2031, 3, 14, 0, 0, 0, TimeSpan.Zero) },
                new UsageEntry { OrganizationId = org, Provider = "a", CostUsd = 100m, CreatedAt = new DateTimeOffset(2031, 2, 28, 23, 59, 59, TimeSpan.Zero) },
                new UsageEntry { OrganizationId = other, Provider = "a", CostUsd = 50m, CreatedAt = new DateTimeOffset(2031, 3, 10, 0, 0, 0, TimeSpan.Zero) });
            await db.SaveChangesAsync();
        }
        await using var orgDb = OrgDb(org);
        var (spent, quota) = await Meter().MonthAsync(orgDb, org, CancellationToken.None);
        Assert.That(spent, Is.EqualTo(3.25m));
        Assert.That(quota, Is.EqualTo(10m));
        var (none, noQuota) = await Meter().MonthAsync(orgDb, await Seed.OrganizationAsync(), CancellationToken.None);
        Assert.That(none, Is.Zero);
        Assert.That(noQuota, Is.Null);
    }
}
