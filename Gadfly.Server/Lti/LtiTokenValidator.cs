using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gadfly.Server.Lti;

/// <summary>Everything one validated launch's JWT said that the rest of the integration needs (GAD-T312/GAD-T380).</summary>
public sealed record LtiLaunchClaims(string Subject, string? Email, string? GivenName, string? FamilyName, string? Name,
    IReadOnlyList<string> Roles, string? ContextId, string? ContextLabel, string? ContextTitle, string? ResourceLinkId, string? ResourceLinkTitle,
    string? LineItemsUrl, string? LineItemUrl, string? ContextMembershipsUrl, JsonElement? DeepLinkingSettings, Guid? CustomExamId,
    string? TargetLinkUri = null)
{
    /// <summary>The Gadfly organization role these LTI roles map to, or null when none does (see <see cref="LtiClaims.GadflyRoleOf"/>).</summary>
    public string? GadflyRole => LtiClaims.GadflyRoleOf(Roles);

    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name.Trim()
        : string.Join(' ', new[] { GivenName, FamilyName }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())) is { Length: > 0 } n ? n
        : Email ?? "LMS user";
}

/// <summary>What a launch must match: the registration's identity, and the nonce issued for this login.</summary>
public sealed record LtiExpectations(string Issuer, string ClientId, string DeploymentId, string Nonce);

/// <summary>A validated token's message type and claims, or the plain-English reason it was refused.</summary>
public sealed record LtiTokenValidation(bool Ok, string? Error, string? MessageType = null, LtiLaunchClaims? Claims = null, bool SigningKeyNotFound = false)
{
    public static LtiTokenValidation Fail(string error, bool keyNotFound = false) => new(false, error, SigningKeyNotFound: keyNotFound);
}

/// <summary>
/// Validates an LTI 1.3 launch <c>id_token</c> (GAD-T380) against every check the IMS Security Framework (section 5.1.3)
/// and LTI Core (section 5.3) require of a tool, with no database or network: the caller supplies the platform's keys
/// and what to expect. In order: the token is a compact JWS signed with RS256 (never <c>none</c>, never HMAC: a
/// platform's public key used as an HMAC secret is the classic downgrade), the signature verifies against one of the
/// platform's keys, <c>iss</c> is the registered issuer, <c>aud</c> contains the client id (and <c>azp</c>, when
/// present or when there are several audiences, is the client id), <c>exp</c>/<c>iat</c> are present and current
/// (60 seconds of clock skew), the <c>nonce</c> is the one issued for this login, the LTI version is 1.3.0, the
/// deployment id is the registered one, and a message type and subject are present. Pure, so the tests exercise
/// it directly.
/// </summary>
public static class LtiTokenValidator
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(60);

    public static async Task<LtiTokenValidation> ValidateAsync(string? idToken, LtiExpectations expect, IEnumerable<SecurityKey> keys)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return LtiTokenValidation.Fail("This is not a valid LTI launch (no id_token).");

        JsonWebToken jwt;
        try { jwt = new JsonWebToken(idToken); }
        catch (Exception e) when (e is ArgumentException or FormatException or SecurityTokenMalformedException) { return LtiTokenValidation.Fail("The id_token is not a readable JWT."); }
        if (jwt.IsEncrypted || !string.Equals(jwt.Alg, LtiClaims.Algorithm, StringComparison.Ordinal))
            return LtiTokenValidation.Fail($"The launch was not signed with {LtiClaims.Algorithm}, the only algorithm LTI 1.3 allows.");

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = expect.Issuer, ValidateIssuer = true,
            ValidAudience = expect.ClientId, ValidateAudience = true,
            IssuerSigningKeys = keys.ToList(), ValidateIssuerSigningKey = true, RequireSignedTokens = true,
            ValidAlgorithms = [LtiClaims.Algorithm],
            ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = ClockSkew,
            ValidateTokenReplay = false,   // replay is stopped by the single-use state and its nonce, checked by the caller and below
            TryAllIssuerSigningKeys = true,
        };
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(jwt, parameters);
        if (!result.IsValid)
        {
            var keyNotFound = result.Exception is SecurityTokenSignatureKeyNotFoundException;
            return LtiTokenValidation.Fail(Describe(result.Exception), keyNotFound);
        }

        // Read the (now verified) payload ourselves: object-valued claims come back as plain JSON.
        JsonElement payload;
        try
        {
            using var doc = JsonDocument.Parse(Base64UrlEncoder.Decode(jwt.EncodedPayload));
            payload = doc.RootElement.Clone();
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException) { return LtiTokenValidation.Fail("The launch's claims are not valid JSON."); }

        if (!payload.TryGetProperty("iat", out var iat) || iat.ValueKind != JsonValueKind.Number)
            return LtiTokenValidation.Fail("The launch carries no issued-at time.");
        var audiences = jwt.Audiences.ToList();
        var azp = Str(payload, "azp");
        if ((audiences.Count > 1 || azp is not null) && azp != expect.ClientId)
            return LtiTokenValidation.Fail("The launch was issued for a different tool (its authorized party is not this client id).");
        if (Str(payload, "nonce") is not { } nonce || !FixedTimeEquals(nonce, expect.Nonce))
            return LtiTokenValidation.Fail("The launch's nonce does not match: it may have been replayed. Open the exam from the LMS again.");
        if (Str(payload, LtiClaims.Version) != LtiClaims.SupportedVersion)
            return LtiTokenValidation.Fail($"Unsupported LTI version (Gadfly speaks LTI {LtiClaims.SupportedVersion}).");
        if (Str(payload, LtiClaims.DeploymentId) != expect.DeploymentId)
            return LtiTokenValidation.Fail("The launch's deployment id does not match this connection's. Check the deployment id on Gadfly's LMS connection page.");
        if (Str(payload, LtiClaims.MessageType) is not { Length: > 0 } messageType)
            return LtiTokenValidation.Fail("The launch names no message type.");
        if (Str(payload, "sub") is not { Length: > 0 } subject)
            return LtiTokenValidation.Fail("The launch names no user. Gadfly cannot run an anonymous launch: a spoken exam belongs to a person.");

        var roles = payload.TryGetProperty(LtiClaims.Roles, out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
        var context = Obj(payload, LtiClaims.Context);
        var resourceLink = Obj(payload, LtiClaims.ResourceLink);
        var endpoint = Obj(payload, LtiClaims.AgsEndpoint);
        var nrps = Obj(payload, LtiClaims.NamesRoleService);
        var custom = Obj(payload, LtiClaims.Custom);
        var customExamId = Str(custom, "examId") is { } ceid && Guid.TryParse(ceid, out var ceg) ? ceg : (Guid?)null;

        var claims = new LtiLaunchClaims(
            subject, Str(payload, "email"), Str(payload, "given_name"), Str(payload, "family_name"), Str(payload, "name"),
            roles,
            Str(context, "id"), Str(context, "label"), Str(context, "title"),
            Str(resourceLink, "id"), Str(resourceLink, "title"),
            Str(endpoint, "lineitems"), Str(endpoint, "lineitem"), Str(nrps, "context_memberships_url"),
            Obj(payload, LtiClaims.DeepLinkingSettings), customExamId, Str(payload, LtiClaims.TargetLinkUri));
        return new LtiTokenValidation(true, null, messageType, claims);
    }

    private static string Describe(Exception? e) => e switch
    {
        SecurityTokenSignatureKeyNotFoundException => "The launch was signed with a key the platform's key set does not list. Check the key-set (JWKS) URL on Gadfly's LMS connection page.",
        SecurityTokenInvalidSignatureException => "The launch's signature is not valid.",
        SecurityTokenInvalidIssuerException => "The launch came from a different issuer than this connection's. Check the issuer on Gadfly's LMS connection page.",
        SecurityTokenInvalidAudienceException => "The launch was meant for a different client id than this connection's. Check the client id on Gadfly's LMS connection page.",
        SecurityTokenExpiredException => "The launch has expired. Open the exam from the LMS again (and check this computer's clock).",
        SecurityTokenNotYetValidException => "The launch is not valid yet. Check this computer's clock and open the exam from the LMS again.",
        SecurityTokenNoExpirationException => "The launch carries no expiry time.",
        SecurityTokenInvalidAlgorithmException => $"The launch was not signed with {LtiClaims.Algorithm}, the only algorithm LTI 1.3 allows.",
        _ => "The launch could not be verified.",
    };

    private static JsonElement? Obj(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    internal static string? Str(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool FixedTimeEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}
