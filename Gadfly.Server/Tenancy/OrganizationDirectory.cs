using System.Security.Claims;
using System.Text.RegularExpressions;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication;
using MindAttic.Authentication.Services;
using MindAttic.Authentication.Web;

namespace Gadfly.Server.Tenancy;

/// <summary>The outcome of an organization or membership change, in words an admin can read.</summary>
public sealed record DirectoryResult(bool Ok, string Message, Guid? Id = null);

/// <summary>
/// Customer organizations and their members (GAD-T41). Interim home in Gadfly's tables until
/// MindAttic.Authentication ships organizations (M.AUTH-T34); callers only see this interface.
/// </summary>
public interface IOrganizationDirectory
{
    Task<IReadOnlyList<Organization>> ListOrganizationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<(Organization Org, string Role)>> MembershipsOfAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<(OrganizationMember Member, string UserName, string? Email)>> MembersAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>A MindAttic SuperAdmin creates a customer organization and its owner account.</summary>
    Task<DirectoryResult> CreateOrganizationAsync(string name, string ownerUserName, string ownerEmail, string ownerTemporaryPassword,
        Guid createdByUserId, CancellationToken ct = default);

    /// <summary>Adds a member, creating their account when the user name is new (temporary password, must change it).</summary>
    Task<DirectoryResult> AddMemberAsync(Guid organizationId, string userName, string? email, string role, string temporaryPassword,
        CancellationToken ct = default);

    Task<DirectoryResult> SetMemberActiveAsync(Guid organizationId, Guid memberId, bool active, CancellationToken ct = default);
}

public sealed partial class OrganizationDirectory(GadflyDbContext db, IUserAdminService users, TimeProvider clock) : IOrganizationDirectory
{
    /// <summary>Role every Gadfly account holds in MindAttic.Authentication; organization roles live in memberships.</summary>
    public const string AccountRole = "User";

    public async Task<IReadOnlyList<Organization>> ListOrganizationsAsync(CancellationToken ct = default) =>
        await db.Organizations.AsNoTracking().OrderBy(o => o.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<(Organization Org, string Role)>> MembershipsOfAsync(Guid userId, CancellationToken ct = default) =>
        (await db.OrganizationMembers.AsNoTracking().Include(m => m.Organization)
            .Where(m => m.UserId == userId && m.IsActive && m.Organization!.IsActive)
            .OrderBy(m => m.Organization!.Name).ToListAsync(ct))
        .Select(m => (m.Organization!, m.Role)).ToList();

    public async Task<IReadOnlyList<(OrganizationMember Member, string UserName, string? Email)>> MembersAsync(Guid organizationId, CancellationToken ct = default)
    {
        var members = await db.OrganizationMembers.AsNoTracking().Where(m => m.OrganizationId == organizationId).ToListAsync(ct);
        var ids = members.Select(m => m.UserId).ToList();
        var accounts = await db.AuthUsers.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.UserName, u.Email), ct);
        return members.Select(m => (m, accounts.TryGetValue(m.UserId, out var a) ? a.UserName : "(missing account)",
            accounts.TryGetValue(m.UserId, out var b) ? b.Email : null))
            .OrderBy(x => x.Item1.Role).ThenBy(x => x.Item2).ToList();
    }

    public async Task<DirectoryResult> CreateOrganizationAsync(string name, string ownerUserName, string ownerEmail, string ownerTemporaryPassword,
        Guid createdByUserId, CancellationToken ct = default)
    {
        name = name.Trim();
        if (name.Length is < 2 or > 200) return new(false, "Give the organization a name (2 to 200 characters).");
        var slug = Slug(name);
        if (await db.Organizations.AnyAsync(o => o.Slug == slug, ct)) return new(false, $"An organization named like '{name}' already exists.");

        var org = new Organization { Name = name, Slug = slug, CreatedByUserId = createdByUserId, CreatedAt = clock.GetUtcNow() };
        db.Organizations.Add(org);
        db.OrganizationSettings.Add(new OrganizationSettings { OrganizationId = org.Id, UpdatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);

        var owner = await AddMemberAsync(org.Id, ownerUserName, ownerEmail, OrgRoles.Owner, ownerTemporaryPassword, ct);
        if (!owner.Ok)
        {
            db.OrganizationSettings.RemoveRange(db.OrganizationSettings.Where(s => s.OrganizationId == org.Id));
            db.Organizations.Remove(org);
            await db.SaveChangesAsync(ct);
            return new(false, "The organization was not created: " + owner.Message);
        }
        return new(true, $"Created {name}. The owner signs in as '{ownerUserName.Trim()}' with the temporary password, then must change it and set up MFA.", org.Id);
    }

    public async Task<DirectoryResult> AddMemberAsync(Guid organizationId, string userName, string? email, string role, string temporaryPassword,
        CancellationToken ct = default)
    {
        if (!OrgRoles.IsValid(role)) return new(false, $"'{role}' is not a role.");
        if (!await db.Organizations.AnyAsync(o => o.Id == organizationId && o.IsActive, ct)) return new(false, "That organization does not exist.");
        userName = userName.Trim();
        if (userName.Length == 0) return new(false, "Give a user name.");

        var normalized = MindAttic.Authentication.Services.IUserStore.Normalize(userName);
        var account = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
        Guid userId;
        if (account is null)
        {
            var created = await users.CreateAsync(userName, string.IsNullOrWhiteSpace(email) ? null : email.Trim(), AccountRole,
                temporaryPassword, mustChangePassword: true, ct: ct);
            if (!created.Ok || created.UserId is null) return new(false, created.Error ?? "The account could not be created.");
            userId = created.UserId.Value;
            // Staff enroll an authenticator at first sign-in (not in local development, where MFA is off).
            if (OrgRoles.RequiresMfa(role) && OrgRoleHandler.MfaRequired)
                await db.AuthUsers.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.MustEnrollMfa, true), ct);
        }
        else
        {
            userId = account.Id;
        }

        var existing = await db.OrganizationMembers.FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct);
        if (existing is not null)
        {
            existing.Role = role;
            existing.IsActive = true;
        }
        else
        {
            db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organizationId, UserId = userId, Role = role, CreatedAt = clock.GetUtcNow(),
            });
        }
        await db.SaveChangesAsync(ct);
        return new(true, account is null ? $"Created {userName} as {role}." : $"{userName} is now {role}.", userId);
    }

    public async Task<DirectoryResult> SetMemberActiveAsync(Guid organizationId, Guid memberId, bool active, CancellationToken ct = default)
    {
        var m = await db.OrganizationMembers.FirstOrDefaultAsync(x => x.Id == memberId && x.OrganizationId == organizationId, ct);
        if (m is null) return new(false, "That member was not found.");
        if (!active && m.Role == OrgRoles.Owner
            && await db.OrganizationMembers.CountAsync(x => x.OrganizationId == organizationId && x.Role == OrgRoles.Owner && x.IsActive, ct) <= 1)
            return new(false, "An organization keeps at least one active owner.");
        m.IsActive = active;
        await db.SaveChangesAsync(ct);
        return new(true, active ? "Member restored." : "Member removed. They lose access at their next sign-in.");
    }

    internal static string Slug(string name)
    {
        var s = SlugChars().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return s.Length == 0 ? "org" : s.Length > 80 ? s[..80].TrimEnd('-') : s;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugChars();
}

/// <summary>Stamps the user's organization memberships onto the sign-in cookie (GAD-T41).</summary>
public sealed class OrganizationClaimsAugmentor : IMaClaimsAugmentor
{
    public async ValueTask AugmentAsync(ClaimsIdentity identity, IServiceProvider services, CancellationToken ct)
    {
        if (!Guid.TryParse(identity.FindFirst(MaClaims.UserId)?.Value, out var userId)) return;
        var directory = services.GetRequiredService<IOrganizationDirectory>();
        foreach (var (org, role) in await directory.MembershipsOfAsync(userId, ct))
            identity.AddClaim(new Claim(GadflyClaims.Membership, $"{org.Id:N}|{role}"));
    }
}
