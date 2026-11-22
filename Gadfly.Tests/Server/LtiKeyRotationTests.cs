using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Lti;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// GAD-T478: rotating Gadfly's LTI signing key. The new key signs at once; the old one stays in /lti/jwks.json through the
/// grace period (7 days by default), then is left out and deleted; both steps are audited.
/// </summary>
[TestFixture]
public class LtiKeyRotationTests : ServerTest
{
    private static async Task<T> WithKeysAsync<T>(Func<LtiKeys, Task<T>> act)
    {
        await using var scope = Scope(TenantSnapshot.System);
        return await act(scope.ServiceProvider.GetRequiredService<LtiKeys>());
    }

    private static async Task<List<string>> PublishedKidsAsync()
    {
        var json = await WithKeysAsync(k => k.JwksJsonAsync());
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("keys").EnumerateArray().Select(e => e.GetProperty("kid").GetString()!).ToList();
    }

    [SetUp]
    public async Task NoKeysYet()
    {
        await using var db = SystemDb();
        await db.LtiToolKeys.ExecuteDeleteAsync();
    }

    [Test]
    public async Task Rotation_signs_with_the_new_key_and_publishes_both_through_the_grace_period()
    {
        var first = (await WithKeysAsync(k => k.ActiveAsync())).Kid;
        var admin = Guid.NewGuid();
        var r = await WithKeysAsync(k => k.RotateAsync(admin));

        Assert.That(r.PreviousKid, Is.EqualTo(first));
        Assert.That(r.PreviousRetiresAt, Is.EqualTo(Clock.GetUtcNow() + TimeSpan.FromDays(LtiKeys.DefaultGraceDays)).Within(TimeSpan.FromMinutes(1)));
        var signing = await WithKeysAsync(async k => (await k.SigningCredentialsAsync()).Kid);
        Assert.That(signing, Is.EqualTo(r.NewKid));
        Assert.That(await PublishedKidsAsync(), Is.EqualTo(new[] { r.NewKid, first }), "newest first, old key still published");

        Clock.Now = Clock.GetUtcNow().AddDays(LtiKeys.DefaultGraceDays - 1);
        Assert.That(await PublishedKidsAsync(), Does.Contain(first), "inside the grace period");

        await using var db = SystemDb();
        var rotated = await db.AuditEvents.AsNoTracking().Where(a => a.Action == "lti.key.rotated" && a.EntityId == r.NewKid).SingleAsync();
        Assert.That(rotated.ActorUserId, Is.EqualTo(admin));
        Assert.That(rotated.AfterJson, Does.Not.Contain("PRIVATE").IgnoreCase.And.Not.Contain("\"n\""), "key material is never logged");
    }

    [Test]
    public async Task After_the_grace_period_the_old_key_is_retired_and_deleted()
    {
        var first = (await WithKeysAsync(k => k.ActiveAsync())).Kid;
        var r = await WithKeysAsync(k => k.RotateAsync(Guid.NewGuid()));

        Clock.Now = Clock.GetUtcNow().AddDays(LtiKeys.DefaultGraceDays).AddMinutes(1);
        Assert.That(await PublishedKidsAsync(), Is.EqualTo(new[] { r.NewKid }));

        await using var db = SystemDb();
        Assert.That(await db.LtiToolKeys.AnyAsync(k => k.Kid == first), Is.False, "the retired row, private key included, is gone");
        Assert.That(await db.AuditEvents.AnyAsync(a => a.Action == "lti.key.retired" && a.EntityId == first), Is.True);
        Assert.That(await WithKeysAsync(k => k.RetireExpiredAsync()), Is.EqualTo(0), "retiring twice does nothing");
    }

    [Test]
    public async Task The_signing_key_is_never_retired_however_old()
    {
        var only = (await WithKeysAsync(k => k.ActiveAsync())).Kid;
        Clock.Now = Clock.GetUtcNow().AddDays(400);
        Assert.That(await PublishedKidsAsync(), Is.EqualTo(new[] { only }));
    }
}
