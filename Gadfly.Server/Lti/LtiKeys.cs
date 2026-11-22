using System.Security.Cryptography;
using System.Text.Json;
using Gadfly.Server.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Gadfly.Server.Lti;

/// <summary>One JWKS key entry, in the shape <c>/lti/jwks.json</c> and a platform's own key-set both use.</summary>
public sealed record JwkEntry(
    [property: System.Text.Json.Serialization.JsonPropertyName("kty")] string Kty,
    [property: System.Text.Json.Serialization.JsonPropertyName("kid")] string Kid,
    [property: System.Text.Json.Serialization.JsonPropertyName("use")] string Use,
    [property: System.Text.Json.Serialization.JsonPropertyName("alg")] string Alg,
    [property: System.Text.Json.Serialization.JsonPropertyName("n")] string N,
    [property: System.Text.Json.Serialization.JsonPropertyName("e")] string E);

/// <summary>
/// Gadfly's own RSA signing key for LTI (GAD-T312/GAD-T380): generated once (2048-bit RSA) and reused after that, the
/// private half stored only in its <see cref="IDataProtector"/>-protected form (the same discipline
/// <see cref="Platform.Webhook.ProtectedSecret"/> uses), the public half served at <c>/lti/jwks.json</c> so every
/// connected platform can verify a JWT Gadfly signed (the deep-linking response, the client assertion for AGS/NRPS).
/// One key serves every organization: LTI's JWKS is a tool-wide identity, not a per-tenant one.
/// </summary>
public sealed class LtiKeys(GadflyDbContext db, IDataProtectionProvider protection, TimeProvider clock)
{
    private IDataProtector Protector => protection.CreateProtector("Gadfly.Lti.ToolKey");

    /// <summary>The active key, creating one the first time it is needed. Two first callers at once can each add a row; both
    /// are published in the key set and every caller after that uses the oldest, so a signature made with either verifies.</summary>
    public async Task<LtiToolKey> ActiveAsync(CancellationToken ct = default)
    {
        var existing = await db.LtiToolKeys.AsNoTracking().OrderBy(k => k.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        using var rsa = RSA.Create(2048);
        var kid = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var p = rsa.ExportParameters(false);
        var jwk = new JwkEntry("RSA", kid, "sig", "RS256", Base64Url(p.Modulus!), Base64Url(p.Exponent!));
        var row = new LtiToolKey
        {
            Kid = kid,
            ProtectedPrivateKey = Protector.Protect(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey())),
            PublicJwkJson = JsonSerializer.Serialize(jwk),
            CreatedAt = clock.GetUtcNow(),
        };
        db.LtiToolKeys.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Lost a race with another request creating the first key: use whichever one landed.
            return await db.LtiToolKeys.AsNoTracking().OrderBy(k => k.CreatedAt).FirstAsync(ct);
        }
        return row;
    }

    /// <summary>The active key's RSA parameters, decrypted for this call only (never persisted outside <see cref="LtiToolKey.ProtectedPrivateKey"/>).</summary>
    public async Task<RSA> PrivateKeyAsync(CancellationToken ct = default)
    {
        var key = await ActiveAsync(ct);
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(Protector.Unprotect(key.ProtectedPrivateKey)), out _);
        return rsa;
    }

    /// <summary>Signing credentials for Gadfly's own JWTs (the deep-linking response, the AGS/NRPS client assertion).</summary>
    public async Task<(SigningCredentials Credentials, string Kid)> SigningCredentialsAsync(CancellationToken ct = default)
    {
        var key = await ActiveAsync(ct);
        var rsa = await PrivateKeyAsync(ct);
        var securityKey = new RsaSecurityKey(rsa) { KeyId = key.Kid };
        return (new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256), key.Kid);
    }

    /// <summary>The JWKS document served at <c>/lti/jwks.json</c>: every key ever issued (so a signature made with an older
    /// one, if a rotation is ever added, still verifies), newest first.</summary>
    public async Task<string> JwksJsonAsync(CancellationToken ct = default)
    {
        _ = await ActiveAsync(ct);   // ensure at least one exists
        var keys = await db.LtiToolKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt)
            .Select(k => k.PublicJwkJson).ToListAsync(ct);
        var sb = new System.Text.StringBuilder("{\"keys\":[");
        sb.AppendJoin(',', keys);
        return sb.Append("]}").ToString();
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
