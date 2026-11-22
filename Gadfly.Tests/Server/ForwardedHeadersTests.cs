using System.Net;
using Gadfly.Server.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// GAD-T476: a forged X-Forwarded-For must not change the address the per-address rate limits see
/// (<c>Connection.RemoteIpAddress</c>), unless the connection comes from the App Service front end, and then only the
/// entry the front end appended counts.
/// </summary>
[TestFixture]
public class ForwardedHeadersTests
{
    private static async Task<IPAddress?> AddressSeenAsync(string connectionFrom, string? forwardedFor, IConfiguration? config = null)
    {
        var options = ForwardedHeadersSetup.Create(config ?? new ConfigurationBuilder().Build());
        IPAddress? seen = null;
        var middleware = new ForwardedHeadersMiddleware(
            ctx => { seen = ctx.Connection.RemoteIpAddress; return Task.CompletedTask; },
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            Options.Create(options));
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(connectionFrom);
        if (forwardedFor is not null) http.Request.Headers["X-Forwarded-For"] = forwardedFor;
        await middleware.Invoke(http);
        return seen;
    }

    [Test]
    public async Task Forged_header_from_a_public_caller_is_ignored()
    {
        var seen = await AddressSeenAsync("203.0.113.7", "198.51.100.99");
        Assert.That(seen, Is.EqualTo(IPAddress.Parse("203.0.113.7")));
    }

    [Test]
    public async Task Every_forged_value_from_one_public_caller_lands_in_the_same_rate_limit_partition()
    {
        var a = await AddressSeenAsync("203.0.113.7", "1.1.1.1");
        var b = await AddressSeenAsync("203.0.113.7", "2.2.2.2, 3.3.3.3");
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public async Task Front_end_hop_uses_only_the_entry_it_appended()
    {
        // The client typed 198.51.100.99; the App Service front end appended the real address (with a port, as it does).
        var seen = await AddressSeenAsync("169.254.129.1", "198.51.100.99, 203.0.113.7:51234");
        Assert.That(seen, Is.EqualTo(IPAddress.Parse("203.0.113.7")));
    }

    [Test]
    public async Task Development_loopback_still_forwards()
    {
        var seen = await AddressSeenAsync("127.0.0.1", "203.0.113.7");
        Assert.That(seen, Is.EqualTo(IPAddress.Parse("203.0.113.7")));
    }

    [Test]
    public async Task Configured_networks_replace_the_defaults()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gadfly:ForwardedHeaders:KnownNetworks"] = "192.0.2.0/24",
        }).Build();
        Assert.That(await AddressSeenAsync("10.0.0.4", "203.0.113.7", config), Is.EqualTo(IPAddress.Parse("10.0.0.4")));
        Assert.That(await AddressSeenAsync("192.0.2.10", "203.0.113.7", config), Is.EqualTo(IPAddress.Parse("203.0.113.7")));
    }

    [Test]
    public void A_bad_configured_network_fails_at_startup()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gadfly:ForwardedHeaders:KnownNetworks"] = "not-a-network",
        }).Build();
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Create(config));
    }
}
