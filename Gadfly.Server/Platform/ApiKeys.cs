using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gadfly.Server.Platform;

/// <summary>
/// Per-organization API keys for apps that integrate Gadfly (GAD-T46). A key looks like
/// <c>gfk_&lt;prefix&gt;_&lt;secret&gt;</c>; only its SHA-256 is stored and it is shown once at creation.
/// </summary>
public sealed class ApiKeyService(GadflyDbContext db, TimeProvider clock)
{
    public const string KeyPrefix = "gfk_";

    /// <summary>Creates a key for the caller's organization and returns the full key (shown once).</summary>
    public async Task<(ApiKey Row, string Key)> CreateAsync(Guid organizationId, Guid createdBy, string name, CancellationToken ct = default)
    {
        var prefix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var key = $"{KeyPrefix}{prefix}_{secret}";
        var row = new ApiKey
        {
            OrganizationId = organizationId,
            Name = string.IsNullOrWhiteSpace(name) ? "API key" : name.Trim()[..Math.Min(120, name.Trim().Length)],
            Prefix = prefix,
            KeyHash = Hash(key),
            Scopes = "exams",
            CreatedByUserId = createdBy,
            CreatedAt = clock.GetUtcNow(),
        };
        db.ApiKeys.Add(row);
        await db.SaveChangesAsync(ct);
        return (row, key);
    }

    public async Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken ct = default) =>
        await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(ct);

    public async Task<bool> RevokeAsync(Guid id, CancellationToken ct = default) =>
        await db.ApiKeys.Where(k => k.Id == id && k.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(k => k.RevokedAt, clock.GetUtcNow()), ct) > 0;

    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Authentication scheme for the platform API: <c>Authorization: Bearer gfk_...</c>. The principal is the
/// organization itself (role <see cref="OrgRoles.Api"/>), so every query is scoped to it by the tenant filter.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IServiceScopeFactory scopes, TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "GadflyApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer " + ApiKeyService.KeyPrefix, StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        var key = header["Bearer ".Length..].Trim();
        var hash = ApiKeyService.Hash(key);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        // System lookup (no tenant yet): ApiKeys is tenant-owned, so bypass explicitly.
        var row = await db.ApiKeys.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.RevokedAt == null, Context.RequestAborted);
        if (row is null) return AuthenticateResult.Fail("Unknown or revoked API key.");
        if (!await db.Organizations.AnyAsync(o => o.Id == row.OrganizationId && o.IsActive, Context.RequestAborted))
            return AuthenticateResult.Fail("The organization is not active.");

        await db.ApiKeys.IgnoreQueryFilters().Where(k => k.Id == row.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(k => k.LastUsedAt, clock.GetUtcNow()), Context.RequestAborted);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, $"api:{row.Name}"),
            new Claim(GadflyClaims.ApiKeyId, row.Id.ToString("N")),
            new Claim(GadflyClaims.Membership, $"{row.OrganizationId:N}|{OrgRoles.Api}"),
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
