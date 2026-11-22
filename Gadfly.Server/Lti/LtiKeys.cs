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

/// <summary>Where one signing key stands (GAD-T478): the one signing now, an older one still published until
/// <see cref="PublishedUntil"/> so platforms that cached the key set keep verifying, or past that and about to go.</summary>
public sealed record LtiKeyStatus(string Kid, DateTimeOffset CreatedAt, bool Signing, DateTimeOffset? ReplacedAt, DateTimeOffset? PublishedUntil);

/// <summary>The result of a rotation: the new key's id and when the previous one stops being published.</summary>
public sealed record LtiKeyRotation(string NewKid, string? PreviousKid, DateTimeOffset? PreviousRetiresAt);

/// <summary>
/// Gadfly's own RSA signing key for LTI (GAD-T312/GAD-T380): generated on first use (2048-bit RSA), the private half
/// stored only in its <see cref="IDataProtector"/>-protected form (the same discipline
/// <see cref="Platform.Webhook.ProtectedSecret"/> uses), the public half served at <c>/lti/jwks.json</c> so every
/// connected platform can verify a JWT Gadfly signed (the deep-linking response, the client assertion for AGS/NRPS).
/// One key serves every organization: LTI's JWKS is a tool-wide identity, not a per-tenant one.
/// <para>
/// Rotation (GAD-T478): the newest key signs. A platform admin's <see cref="RotateAsync"/> adds a new key, which signs
/// from then on; the key it replaced stays in the key set for a grace period (<c>Gadfly:Lti:KeyGraceDays</c>, 7 days by
/// default) so platforms that cached the old key set, or still hold a JWT signed a moment ago, keep verifying. After the
/// grace period the old key is left out of the key set and its row (with the protected private key) is deleted, audited
/// as <c>lti.key.retired</c>. No schema change: a key's "replaced at" is the next newer key's <see cref="LtiToolKey.CreatedAt"/>.
/// </para>
/// </summary>
public sealed class LtiKeys(GadflyDbContext db, IDataProtectionProvider protection, TimeProvider clock, IConfiguration config)
{
    public const int DefaultGraceDays = 7;

    private IDataProtector Protector => protection.CreateProtector("Gadfly.Lti.ToolKey");

    /// <summary>How long a replaced key stays in <c>/lti/jwks.json</c> (<c>Gadfly:Lti:KeyGraceDays</c>, 1 to 90, default 7).</summary>
    public TimeSpan GracePeriod => TimeSpan.FromDays(Math.Clamp(config.GetValue("Gadfly:Lti:KeyGraceDays", DefaultGraceDays), 1, 90));

    /// <summary>The signing key (the newest), creating one the first time it is needed. Two first callers at once can each add
    /// a row; the newer one signs from then on and the older one is published through the grace period like any replaced
    /// key, so a signature made with either verifies.</summary>
    public async Task<LtiToolKey> ActiveAsync(CancellationToken ct = default)
    {
        var existing = await db.LtiToolKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;
        return await AddKeyAsync(ct);
    }

    /// <summary>The signing key's RSA parameters, decrypted for this call only (never persisted outside <see cref="LtiToolKey.ProtectedPrivateKey"/>).</summary>
    public async Task<RSA> PrivateKeyAsync(CancellationToken ct = default) => Decrypt(await ActiveAsync(ct));

    /// <summary>Signing credentials for Gadfly's own JWTs (the deep-linking response, the AGS/NRPS client assertion).</summary>
    public async Task<(SigningCredentials Credentials, string Kid)> SigningCredentialsAsync(CancellationToken ct = default)
    {
        var key = await ActiveAsync(ct);
        var securityKey = new RsaSecurityKey(Decrypt(key)) { KeyId = key.Kid };
        return (new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256), key.Kid);
    }

    /// <summary>The JWKS document served at <c>/lti/jwks.json</c>: the signing key plus every replaced key still inside its
    /// grace period, newest first. Keys past their grace period are retired first.</summary>
    public async Task<string> JwksJsonAsync(CancellationToken ct = default)
    {
        _ = await ActiveAsync(ct);   // ensure at least one exists
        await RetireExpiredAsync(ct);
        var now = clock.GetUtcNow();
        var published = (await StatusAsync(ct)).Where(s => s.PublishedUntil is null || s.PublishedUntil > now).Select(s => s.Kid).ToHashSet();
        var keys = await db.LtiToolKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt)
            .Select(k => new { k.Kid, k.PublicJwkJson }).ToListAsync(ct);
        var sb = new System.Text.StringBuilder("{\"keys\":[");
        sb.AppendJoin(',', keys.Where(k => published.Contains(k.Kid)).Select(k => k.PublicJwkJson));
        return sb.Append("]}").ToString();
    }

    /// <summary>Every stored key, newest first, with when it was replaced and until when it is published.</summary>
    public async Task<IReadOnlyList<LtiKeyStatus>> StatusAsync(CancellationToken ct = default)
    {
        var rows = await db.LtiToolKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt)
            .Select(k => new { k.Kid, k.CreatedAt }).ToListAsync(ct);
        var list = new List<LtiKeyStatus>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            DateTimeOffset? replacedAt = i == 0 ? null : rows[i - 1].CreatedAt;
            list.Add(new LtiKeyStatus(rows[i].Kid, rows[i].CreatedAt, Signing: i == 0, replacedAt, replacedAt + GracePeriod));
        }
        return list;
    }

    /// <summary>
    /// Platform admin action: a new key signs from now on; the one it replaces stays published for <see cref="GracePeriod"/>,
    /// then is retired. Audited as <c>lti.key.rotated</c> (key ids only, never key material).
    /// </summary>
    public async Task<LtiKeyRotation> RotateAsync(Guid? actorUserId, CancellationToken ct = default)
    {
        await RetireExpiredAsync(ct);
        var previous = await db.LtiToolKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).FirstOrDefaultAsync(ct);
        // Strictly newer than the key it replaces, even within one clock tick, so "newest signs" is never ambiguous.
        var added = await AddKeyAsync(ct, notBefore: previous?.CreatedAt.AddMilliseconds(1));
        DateTimeOffset? retiresAt = previous is null ? null : added.CreatedAt + GracePeriod;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = null, ActorUserId = actorUserId, ActorRole = "PlatformAdmin", Action = "lti.key.rotated",
            EntityType = "LtiToolKey", EntityId = added.Kid,
            BeforeJson = previous is null ? null : JsonSerializer.Serialize(new { signingKid = previous.Kid }),
            AfterJson = JsonSerializer.Serialize(new { signingKid = added.Kid, previousKid = previous?.Kid, previousPublishedUntil = retiresAt, graceDays = GracePeriod.TotalDays }),
            At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return new LtiKeyRotation(added.Kid, previous?.Kid, retiresAt);
    }

    /// <summary>Deletes every replaced key whose grace period is over (never the signing key), audited as <c>lti.key.retired</c>.
    /// Safe to call from several requests at once: a row another request already deleted is skipped.</summary>
    public async Task<int> RetireExpiredAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var expired = (await StatusAsync(ct)).Where(s => !s.Signing && s.PublishedUntil <= now).Select(s => s.Kid).ToList();
        if (expired.Count == 0) return 0;
        var retired = 0;
        foreach (var kid in expired)
        {
            var deleted = await db.LtiToolKeys.Where(k => k.Kid == kid).ExecuteDeleteAsync(ct);
            if (deleted == 0) continue;   // another request retired it first
            retired++;
            db.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = null, ActorRole = "System", Action = "lti.key.retired", EntityType = "LtiToolKey", EntityId = kid,
                Reason = $"Replaced more than {GracePeriod.TotalDays:0} days ago; no longer published in /lti/jwks.json.", At = now,
            });
        }
        if (retired > 0) await db.SaveChangesAsync(ct);
        return retired;
    }

    private async Task<LtiToolKey> AddKeyAsync(CancellationToken ct, DateTimeOffset? notBefore = null)
    {
        using var rsa = RSA.Create(2048);
        var kid = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var p = rsa.ExportParameters(false);
        var jwk = new JwkEntry("RSA", kid, "sig", "RS256", Base64Url(p.Modulus!), Base64Url(p.Exponent!));
        var row = new LtiToolKey
        {
            Kid = kid,
            ProtectedPrivateKey = Protector.Protect(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey())),
            PublicJwkJson = JsonSerializer.Serialize(jwk),
            CreatedAt = notBefore is { } floor && floor > clock.GetUtcNow() ? floor : clock.GetUtcNow(),
        };
        db.LtiToolKeys.Add(row);
        await db.SaveChangesAsync(ct);
        db.Entry(row).State = EntityState.Detached;
        return row;
    }

    private RSA Decrypt(LtiToolKey key)
    {
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(Protector.Unprotect(key.ProtectedPrivateKey)), out _);
        return rsa;
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
