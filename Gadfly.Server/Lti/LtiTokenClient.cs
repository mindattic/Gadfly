using System.Text.Json;
using Gadfly.Server.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gadfly.Server.Lti;

/// <summary>
/// Gets an OAuth2 access token from a platform's token endpoint for the Names and Roles (GAD-T382) and Assignment and
/// Grade Services (GAD-T383) calls, using the JWT-bearer client-credentials grant LTI Advantage specifies (IMS Security
/// Framework section 4.1, RFC 7523): Gadfly signs a short-lived assertion naming its client id as both issuer and
/// subject with its own RS256 key (never a shared secret), and the platform exchanges it for a bearer token scoped to
/// what was asked for. The assertion's audience is the token URL, or the registration's own
/// <see cref="LtiRegistration.TokenAudience"/> where the platform asks for something else (Brightspace). Tokens are
/// cached per (registration, scope) until shortly before they expire.
/// </summary>
public sealed class LtiTokenClient(LtiKeys keys, IHttpClientFactory http, IMemoryCache cache, TimeProvider clock)
{
    private sealed record TokenResponse(string? access_token, int? expires_in);

    public async Task<string> AccessTokenAsync(LtiRegistration registration, string scope, CancellationToken ct)
    {
        var cacheKey = $"lti-token:{registration.Id:N}:{scope}";
        if (cache.TryGetValue<string>(cacheKey, out var cached) && cached is not null) return cached;

        var assertion = await ClientAssertionAsync(keys, registration, clock.GetUtcNow(), ct);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = assertion,
            ["scope"] = scope,
        };
        using var response = await http.CreateClient(LtiSetup.HttpClientName).PostAsync(registration.AuthTokenUrl, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The platform's token endpoint refused the request ({(int)response.StatusCode}): {Truncate(body)}");
        TokenResponse? parsed;
        try { parsed = JsonSerializer.Deserialize<TokenResponse>(body); }
        catch (JsonException e) { throw new InvalidOperationException("The platform's token endpoint did not return JSON.", e); }
        if (parsed?.access_token is not { Length: > 0 } token)
            throw new InvalidOperationException("The platform's token endpoint did not return an access token.");

        cache.Set(cacheKey, token, TimeSpan.FromSeconds(Math.Clamp((parsed.expires_in ?? 300) - 30, 30, 3600)));
        return token;
    }

    /// <summary>The signed client assertion (public so the tests can check its shape).</summary>
    public static async Task<string> ClientAssertionAsync(LtiKeys keys, LtiRegistration registration, DateTimeOffset now, CancellationToken ct)
    {
        var (credentials, _) = await keys.SigningCredentialsAsync(ct);
        return ClientAssertion(credentials, registration, now);
    }

    public static string ClientAssertion(SigningCredentials credentials, LtiRegistration registration, DateTimeOffset now) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = registration.ClientId,
            Subject = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", registration.ClientId)]),
            Audience = string.IsNullOrWhiteSpace(registration.TokenAudience) ? registration.AuthTokenUrl : registration.TokenAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(5).UtcDateTime,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object> { ["jti"] = Guid.NewGuid().ToString("N") },
        });

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "...";
}
