using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace Gadfly.Server.Lti;

/// <summary>
/// Fetches and caches a platform's own JWKS (GAD-T312/GAD-T380), so the launch JWT's signature can be checked against
/// the key named by its <c>kid</c> header. Cached 10 minutes per key-set URL (platforms rotate keys rarely and publish
/// both old and new while they do); the launch evicts and re-fetches once when a <c>kid</c> isn't found, in case the
/// platform just rotated. A fetch failure raises so the caller refuses the launch rather than accepting an unverifiable
/// one. Only signing keys are kept (<see cref="JsonWebKeySet.GetSigningKeys"/>: an encryption key is never used to verify).
/// </summary>
public sealed class LtiPlatformKeys(IHttpClientFactory http, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private const int MaxBytes = 256 * 1024;

    public async Task<IReadOnlyList<SecurityKey>> KeysAsync(string keySetUrl, CancellationToken ct)
    {
        if (cache.TryGetValue<IReadOnlyList<SecurityKey>>(CacheKey(keySetUrl), out var cached) && cached is not null) return cached;
        var client = http.CreateClient(LtiSetup.HttpClientName);
        using var response = await client.GetAsync(keySetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The platform's key set answered {(int)response.StatusCode}.");
        if (response.Content.Headers.ContentLength is > MaxBytes)
            throw new InvalidOperationException("The platform's key set is unreasonably large.");
        var json = await response.Content.ReadAsStringAsync(ct);
        if (json.Length > MaxBytes) throw new InvalidOperationException("The platform's key set is unreasonably large.");
        var set = new JsonWebKeySet(json) { SkipUnresolvedJsonWebKeys = true };
        IReadOnlyList<SecurityKey> keys = [.. set.GetSigningKeys()];
        if (keys.Count == 0) throw new InvalidOperationException("The platform's key set lists no signing keys.");
        cache.Set(CacheKey(keySetUrl), keys, CacheFor);
        return keys;
    }

    /// <summary>Forces the next call to re-fetch (used once if a <c>kid</c> isn't found, in case the platform just rotated).</summary>
    public void Evict(string keySetUrl) => cache.Remove(CacheKey(keySetUrl));

    private static string CacheKey(string url) => "lti-platform-jwks:" + url;
}
