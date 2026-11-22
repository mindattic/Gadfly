using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Gadfly.Server.Platform;

/// <summary>
/// Which callers may tell Gadfly the client's real address (GAD-T476). The per-address rate limits on sign-in,
/// the contact form, <c>/transcripts/verify</c> and the other anonymous posts read
/// <c>HttpContext.Connection.RemoteIpAddress</c>, which the forwarded-headers middleware rewrites from
/// <c>X-Forwarded-For</c>. It used to trust that header from anyone, so a caller could pick a fresh made-up
/// address on every request and never hit a limit.
/// <para>
/// Now the header is believed only when the connection itself comes from the Azure App Service front end.
/// App Service's front ends reach the app from internal addresses that Microsoft does not publish as a fixed list
/// (and that change), so the documented approach is to trust the private, shared-address and link-local ranges the
/// platform uses for that hop, and nothing public. A caller on the internet always connects from a public address,
/// so its header is ignored. Only the last <c>X-Forwarded-For</c> entry is taken (<see cref="ForwardedHeadersOptions.ForwardLimit"/>
/// = 1): that is the one the front end appends itself, so anything a client typed in front of it is never read.
/// </para>
/// <para>
/// A deployment behind another proxy (Front Door, Application Gateway, a VNet) can name its own networks in
/// <c>Gadfly:ForwardedHeaders:KnownNetworks</c> (CIDR, comma separated) and <c>Gadfly:ForwardedHeaders:KnownProxies</c>
/// (addresses); when either is set it replaces the defaults below. Development needs nothing: loopback is trusted.
/// </para>
/// </summary>
public static class ForwardedHeadersSetup
{
    /// <summary>The ranges the App Service front end connects from, plus loopback: never a public address.</summary>
    public static readonly string[] DefaultNetworks =
    [
        "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16",   // RFC 1918 (App Service, VNet integration)
        "100.64.0.0/10",                                    // RFC 6598 shared address space (App Service on Linux)
        "169.254.0.0/16",                                   // link-local (App Service on Linux front-end hop)
        "127.0.0.0/8", "::1/128",                           // loopback (Development, a local reverse proxy)
        "fc00::/7", "fe80::/10",                            // IPv6 unique-local and link-local
    ];

    public static ForwardedHeadersOptions Create(IConfiguration config)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,   // only the entry the front end appended; anything earlier came from the client
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        var networks = Split(config["Gadfly:ForwardedHeaders:KnownNetworks"]);
        var proxies = Split(config["Gadfly:ForwardedHeaders:KnownProxies"]);
        if (networks.Length == 0 && proxies.Length == 0) networks = DefaultNetworks;

        foreach (var cidr in networks)
            if (System.Net.IPNetwork.TryParse(cidr, out var network)) options.KnownIPNetworks.Add(network);
            else throw new InvalidOperationException($"Gadfly:ForwardedHeaders:KnownNetworks has an entry that is not a network: '{cidr}'.");
        foreach (var address in proxies)
            if (IPAddress.TryParse(address, out var ip)) options.KnownProxies.Add(ip);
            else throw new InvalidOperationException($"Gadfly:ForwardedHeaders:KnownProxies has an entry that is not an address: '{address}'.");
        return options;
    }

    private static string[] Split(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
