using System.Net;
using System.Net.Sockets;

namespace Gadfly.Server.Platform;

/// <summary>
/// Where webhook deliveries may go (GAD-T75). A webhook URL is chosen by an organization admin or an API
/// key, so the server must not become a proxy into its own network: loopback, private, link-local (cloud
/// metadata at 169.254.169.254), CGNAT, multicast, reserved and documentation ranges are refused, both
/// when the URL is registered and again when each delivery connects (after DNS resolution, so a name that
/// later resolves to an internal address - DNS rebinding - is caught too). Redirects are never followed.
/// </summary>
public static class WebhookTargets
{
    /// <summary>Configuration switch for local development only: <c>Gadfly:Webhooks:AllowPrivateTargets=true</c>.</summary>
    public const string AllowPrivateSetting = "Gadfly:Webhooks:AllowPrivateTargets";

    /// <summary>Set at startup; only ever true in the Development environment.</summary>
    public static bool AllowPrivate { get; set; }

    /// <summary>The reason <paramref name="uri"/> can't be a webhook target, or null when it may be.</summary>
    public static string? Problem(Uri uri)
    {
        if (AllowPrivate) return null;
        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || !host.Contains('.') && uri.HostNameType == UriHostNameType.Dns)
            return "Webhook URLs must name a public host.";
        if (IPAddress.TryParse(uri.HostNameType == UriHostNameType.IPv6 ? host.Trim('[', ']') : host, out var ip) && !IsPublic(ip))
            return "Webhook URLs must not point at private, loopback or link-local addresses.";
        return null;
    }

    /// <summary>The delivery client's handler: no redirects, and every connection checked after DNS resolution.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = ConnectAsync,
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        var allowed = AllowPrivate ? addresses : addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
            throw new HttpRequestException($"Webhook host {host} does not resolve to a public address; delivery refused.");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            // Connect to the vetted addresses only (never re-resolve the name).
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>True for a globally routable unicast address.</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0                                   // "this" network
                || b[0] == 10                                    // private
                || b[0] == 100 && b[1] >= 64 && b[1] <= 127      // CGNAT
                || b[0] == 127                                   // loopback
                || b[0] == 169 && b[1] == 254                    // link-local, cloud metadata
                || b[0] == 172 && b[1] >= 16 && b[1] <= 31       // private
                || b[0] == 192 && b[1] == 0 && b[2] is 0 or 2    // IETF protocol assignments, TEST-NET-1
                || b[0] == 192 && b[1] == 88 && b[2] == 99       // 6to4 relay
                || b[0] == 192 && b[1] == 168                    // private
                || b[0] == 198 && b[1] is 18 or 19               // benchmarking
                || b[0] == 198 && b[1] == 51 && b[2] == 100      // TEST-NET-2
                || b[0] == 203 && b[1] == 0 && b[2] == 113       // TEST-NET-3
                || b[0] >= 224);                                 // multicast, reserved, broadcast
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IPv6Loopback.Equals(ip) || IPAddress.IPv6None.Equals(ip)) return false;
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.IsIPv6UniqueLocal || ip.IsIPv6Teredo) return false;
            var b = ip.GetAddressBytes();
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) return false;       // documentation
            if (b[0] == 0x20 && b[1] == 0x02) return IsPublic(new IPAddress(b[2..6]));          // 6to4: the embedded IPv4
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b) return IsPublic(new IPAddress(b[12..16]));   // NAT64
            return (b[0] & 0xe0) == 0x20;                                                      // global unicast 2000::/3 only
        }
        return false;
    }
}
