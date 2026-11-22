using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Gadfly.Server.Platform;

/// <summary>
/// Request rate limits (GAD-T78), on top of the auth library's persistent per-account and per-IP login
/// backoff. Partitions: the platform API by API key (the bearer header, hashed; unauthenticated calls by
/// IP), the exam hub by signed-in user (else IP), and the anonymous auth posts and launch links by IP.
/// A refused request gets <c>429 Too Many Requests</c> with <c>Retry-After</c>.
/// </summary>
public static class RateLimits
{
    public const string Api = "gadfly:api";
    public const string Hub = "gadfly:hub";
    public const string Anonymous = "gadfly:anonymous";
    public const string Verify = "gadfly:verify";

    public static void Configure(RateLimiterOptions o)
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.OnRejected = (context, _) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString();
            return ValueTask.CompletedTask;
        };

        // 120 calls a minute per key, bursts up to 60.
        o.AddPolicy(Api, http => RateLimitPartition.GetTokenBucketLimiter(ApiPartition(http), _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 60, TokensPerPeriod = 30, ReplenishmentPeriod = TimeSpan.FromSeconds(15), QueueLimit = 0, AutoReplenishment = true,
        }));

        // Negotiate, connect and (on long polling) every poll: generous, but bounds a reconnect storm.
        o.AddPolicy(Hub, http => RateLimitPartition.GetFixedWindowLimiter(UserOrIp(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true,
        }));

        // Sign-in, MFA, password-reset posts and launch links from one address.
        o.AddPolicy(Anonymous, http => RateLimitPartition.GetFixedWindowLimiter("ip:" + Ip(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true,
        }));

        // /transcripts/verify (GAD-T295, GAD-T313): its own, stricter limit, separate from the general anonymous
        // one above. It used to share that 30/min, which is far too generous for guessing a 16-character code or
        // probing with altered JSON files; this partitions by IP under its own key so it never borrows room from,
        // or eats into, sign-in/MFA/reset's budget.
        o.AddPolicy(Verify, http => RateLimitPartition.GetFixedWindowLimiter("verify-ip:" + Ip(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true,
        }));
    }

    private static string ApiPartition(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer " + ApiKeyService.KeyPrefix, StringComparison.Ordinal)
            ? "key:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(header)))[..32]
            : "ip:" + Ip(http);
    }

    private static string UserOrIp(HttpContext http) =>
        http.User.FindFirst(MindAttic.Authentication.MaClaims.UserId)?.Value is { Length: > 0 } uid ? "user:" + uid : "ip:" + Ip(http);

    private static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
