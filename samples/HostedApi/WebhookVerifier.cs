using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Verifies a Gadfly webhook (docs/API.md, "Verifying the signature"). The header is
/// <c>t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256&gt;</c>; the HMAC is over <c>"{t}.{rawBody}"</c>, keyed with the webhook's
/// whole <c>whsec_...</c> secret as UTF-8. Always verify the RAW body, before any JSON parsing.
/// </summary>
static class WebhookVerifier
{
    public static bool Verify(string secret, string header, string rawBody, DateTimeOffset now, int toleranceSeconds = 300)
    {
        var fields = header.Split(',')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        if (!fields.TryGetValue("t", out var t) || !fields.TryGetValue("v1", out var v1)) return false;
        if (!long.TryParse(t, out var ts) || Math.Abs(now.ToUnixTimeSeconds() - ts) > toleranceSeconds) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{rawBody}"))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(v1));
    }
}
