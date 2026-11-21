using System.Text.RegularExpressions;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Entities;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Tenancy;

/// <summary>The outcome of an organization or membership change, in words an admin can read.</summary>
public sealed record DirectoryResult(bool Ok, string Message, Guid? Id = null);

/// <summary>
/// Customer organizations and their members (GAD-T41). Since GAD-T156 the data lives in MindAttic.Authentication's
/// organization tables (M.AUTH-T34) and invitations (M.AUTH-T35); this interface stays the one door Gadfly's pages,
/// endpoints and seeders use, adding Gadfly's own rules (its role names, platform-staff versus organization callers).
/// </summary>
public interface IOrganizationDirectory
{
    Task<IReadOnlyList<AuthOrganization>> ListOrganizationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<(AuthOrganization Org, string Role)>> MembershipsOfAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<(AuthMembership Member, string UserName, string? Email)>> MembersAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>A MindAttic SuperAdmin creates a customer organization and its owner account.</summary>
    Task<DirectoryResult> CreateOrganizationAsync(string name, string ownerUserName, string ownerEmail, string ownerTemporaryPassword,
        Guid createdByUserId, CancellationToken ct = default);

    /// <summary>
    /// Adds a member, creating their account when the user name is new (temporary password, must change it),
    /// or changes an existing member's role. An organization caller (<paramref name="actingRole"/> set) can't
    /// add an existing account that isn't already a member of this organization (GAD-T106); that is what
    /// <see cref="InviteAsync"/> is for. <paramref name="actingRole"/> is the caller's role in the organization
    /// (null for platform staff and system callers): only an Owner may grant Owner or change an Owner's role (GAD-T76).
    /// </summary>
    Task<DirectoryResult> AddMemberAsync(Guid organizationId, string userName, string? email, string role, string temporaryPassword,
        CancellationToken ct = default, string? actingRole = null, Guid? actorUserId = null);

    /// <summary>Removes or restores a member; only an Owner may remove an Owner (GAD-T76).</summary>
    Task<DirectoryResult> SetMemberActiveAsync(Guid organizationId, Guid memberId, bool active, CancellationToken ct = default,
        string? actingRole = null, Guid? actorUserId = null);

    // ---- email invitations (M.AUTH-T35): the person accepts, sets a password, and (for staff roles) enrolls MFA ----

    /// <summary>Invites an email address into the organization with a role. The result never says whether the address already has an account.</summary>
    Task<DirectoryResult> InviteAsync(Guid organizationId, string email, string role, string? actingRole, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Invites every row of a CSV (<c>email[,role]</c>, header optional); rows without a role get <paramref name="defaultRole"/>.</summary>
    Task<BulkInviteResult> InviteBulkAsync(Guid organizationId, string csv, string defaultRole, string? actingRole, Guid? actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<InvitationSummary>> InvitationsAsync(Guid organizationId, CancellationToken ct = default);
    Task<DirectoryResult> ResendInvitationAsync(Guid organizationId, Guid invitationId, Guid? actorUserId, CancellationToken ct = default);
    Task<DirectoryResult> RevokeInvitationAsync(Guid organizationId, Guid invitationId, Guid? actorUserId, CancellationToken ct = default);
}

public sealed partial class OrganizationDirectory(GadflyDbContext db, IUserAdminService users, IOrganizationService orgs,
    IInvitationService invitations, TimeProvider clock) : IOrganizationDirectory
{
    /// <summary>Role every Gadfly account holds in MindAttic.Authentication; organization roles live in memberships.</summary>
    public const string AccountRole = "User";

    /// <summary>The account table's column limit for user names and emails (GAD-T212); longer values would crash with a SQL truncation.</summary>
    public const int MaxAccountTextLength = 256;

    /// <summary>Why <paramref name="userName"/> or <paramref name="email"/> can't be stored on an account, or null when they can.</summary>
    internal static string? AccountTextProblem(string userName, string? email)
    {
        if (userName.Length > MaxAccountTextLength) return $"The user name must be at most {MaxAccountTextLength} characters.";
        return email is { Length: > 0 } && !IsValidEmail(email) ? $"The email must be an email address of at most {MaxAccountTextLength} characters." : null;
    }

    public static bool IsValidEmail(string email) =>
        email.Length <= MaxAccountTextLength && System.Net.Mail.MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;

    public Task<IReadOnlyList<AuthOrganization>> ListOrganizationsAsync(CancellationToken ct = default) => orgs.ListOrganizationsAsync(ct);

    public async Task<IReadOnlyList<(AuthOrganization Org, string Role)>> MembershipsOfAsync(Guid userId, CancellationToken ct = default) =>
        (await orgs.MembershipsOfAsync(userId, ct)).Select(m => (m.Organization, m.Role)).ToList();

    public async Task<IReadOnlyList<(AuthMembership Member, string UserName, string? Email)>> MembersAsync(Guid organizationId, CancellationToken ct = default) =>
        (await orgs.MembersAsync(organizationId, ct)).Select(m => (m.Membership, m.UserName, m.Email)).ToList();

    public async Task<DirectoryResult> CreateOrganizationAsync(string name, string ownerUserName, string ownerEmail, string ownerTemporaryPassword,
        Guid createdByUserId, CancellationToken ct = default)
    {
        name = name.Trim();
        if (name.Length is < 2 or > 200) return new(false, "Give the organization a name (2 to 200 characters).");
        if (await db.Organizations.AnyAsync(o => o.Slug == Slug(name), ct)) return new(false, $"An organization named like '{name}' already exists.");
        ownerUserName = ownerUserName.Trim();
        if (ownerUserName.Length == 0) return new(false, "Give a user name.");

        var (owner, created) = await EnsureAccountAsync(ownerUserName, string.IsNullOrWhiteSpace(ownerEmail) ? null : ownerEmail.Trim(), ownerTemporaryPassword, ct);
        if (owner.Error is not null) return new(false, "The organization was not created: " + owner.Error);

        var org = await orgs.CreateAsync(name, createdByUserId == Guid.Empty ? null : createdByUserId, owner.UserId, ct);
        if (!org.Ok || org.Id is null)
        {
            if (created) await db.AuthUsers.Where(u => u.Id == owner.UserId).ExecuteDeleteAsync(ct);   // never leave an orphan account behind
            return new(false, "The organization was not created: " + org.Message);
        }
        db.OrganizationSettings.Add(new OrganizationSettings { OrganizationId = org.Id.Value, UpdatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return new(true, $"Created {name}. The owner signs in as '{ownerUserName}' with the temporary password, then must change it and set up MFA.", org.Id);
    }

    public async Task<DirectoryResult> AddMemberAsync(Guid organizationId, string userName, string? email, string role, string temporaryPassword,
        CancellationToken ct = default, string? actingRole = null, Guid? actorUserId = null)
    {
        if (!OrgRoles.IsValid(role)) return new(false, $"'{role}' is not a role.");
        if (role == OrgRoles.Owner && !ActsAsOwner(actingRole)) return new(false, "Only an owner can add another owner.");
        if (!await db.Organizations.AnyAsync(o => o.Id == organizationId && o.IsActive, ct)) return new(false, "That organization does not exist.");
        userName = userName.Trim();
        if (userName.Length == 0) return new(false, "Give a user name.");

        var normalized = IUserStore.Normalize(userName);
        var account = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
        if (account is not null && actingRole is not null
            && !await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == account.Id, ct))
        {
            // An organization's own admins never pull an outside account in: accounts are shared across
            // organizations, and adding one would put this organization in someone's switcher without
            // their consent (and confirm the name exists). Same rule as the API (decision D17 default); the
            // way in is an email invitation the person accepts (M.AUTH-T35). Platform staff and system callers are exempt.
            return new(false, $"The user name '{userName}' belongs to an existing Gadfly account that is not a member of this organization. " +
                "Choose a different user name for a new account, or invite the person by email: they accept the invitation themselves.");
        }

        Guid userId;
        if (account is null)
        {
            var (made, _) = await EnsureAccountAsync(userName, string.IsNullOrWhiteSpace(email) ? null : email.Trim(), temporaryPassword, ct);
            if (made.Error is not null) return new(false, made.Error);
            userId = made.UserId;
        }
        else userId = account.Id;

        // The library owns the invariants (owner protection, MFA flag for staff roles, sign-out on a role change, audit).
        var r = await orgs.AddMemberAsync(organizationId, userId, role, actingRole, actorUserId, ct);
        if (!r.Ok) return new(false, r.Message);
        return new(true, account is null ? $"Created {userName} as {role}." : $"{userName} is now {role}.", userId);
    }

    public async Task<DirectoryResult> SetMemberActiveAsync(Guid organizationId, Guid memberId, bool active, CancellationToken ct = default,
        string? actingRole = null, Guid? actorUserId = null)
    {
        var m = await db.OrganizationMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == memberId && x.OrganizationId == organizationId, ct);
        if (m is null) return new(false, "That member was not found.");
        var r = active
            ? await orgs.RestoreMemberAsync(organizationId, m.UserId, actingRole, actorUserId, ct)
            : await orgs.RemoveMemberAsync(organizationId, m.UserId, actingRole, actorUserId, ct);
        return new(r.Ok, r.Message);
    }

    public async Task<DirectoryResult> InviteAsync(Guid organizationId, string email, string role, string? actingRole, Guid? actorUserId, CancellationToken ct = default)
    {
        if (!OrgRoles.All.Contains(role, StringComparer.Ordinal)) return new(false, $"'{role}' is not a role.");
        email = email?.Trim() ?? "";
        if (!IsValidEmail(email)) return new(false, $"The email must be an email address of at most {MaxAccountTextLength} characters.");
        var r = await invitations.InviteAsync(organizationId, email, role, actorUserId, actingRole, ct);
        return new(r.Ok, r.Message, r.InvitationId);
    }

    public Task<BulkInviteResult> InviteBulkAsync(Guid organizationId, string csv, string defaultRole, string? actingRole, Guid? actorUserId, CancellationToken ct = default) =>
        invitations.InviteBulkCsvAsync(organizationId, csv, defaultRole, actorUserId, actingRole, ct);

    public Task<IReadOnlyList<InvitationSummary>> InvitationsAsync(Guid organizationId, CancellationToken ct = default) =>
        invitations.ListAsync(organizationId, ct);

    public async Task<DirectoryResult> ResendInvitationAsync(Guid organizationId, Guid invitationId, Guid? actorUserId, CancellationToken ct = default)
    {
        var r = await invitations.ResendAsync(organizationId, invitationId, actorUserId, ct);
        return new(r.Ok, r.Message, r.InvitationId);
    }

    public async Task<DirectoryResult> RevokeInvitationAsync(Guid organizationId, Guid invitationId, Guid? actorUserId, CancellationToken ct = default)
    {
        var r = await invitations.RevokeAsync(organizationId, invitationId, actorUserId, ct);
        return new(r.Ok, r.Message, r.InvitationId);
    }

    private readonly record struct Account(Guid UserId, string? Error);

    /// <summary>The account for <paramref name="userName"/>: an existing one, or a new one with the temporary password (must change it at first sign-in).</summary>
    private async Task<(Account Account, bool Created)> EnsureAccountAsync(string userName, string? email, string temporaryPassword, CancellationToken ct)
    {
        var normalized = IUserStore.Normalize(userName);
        var existing = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
        if (existing is not null) return (new(existing.Id, null), false);
        if (AccountTextProblem(userName, email) is { } problem) return (new(Guid.Empty, problem), false);
        var created = await users.CreateAsync(userName, email, AccountRole, temporaryPassword, mustChangePassword: true, ct: ct);
        return !created.Ok || created.UserId is null
            ? (new(Guid.Empty, created.Error ?? "The account could not be created."), false)
            : (new(created.UserId.Value, null), true);
    }

    private static bool ActsAsOwner(string? actingRole) => actingRole is null or OrgRoles.Owner;

    /// <summary>
    /// Memberships ride on the sign-in cookie (stamped once at sign-in), so a removal or role change only
    /// bites once the cookie is re-issued. Rotating the account's security stamp makes the auth library's
    /// revalidation (HTTP and Blazor circuits, about a minute) sign every session of that account out;
    /// the next sign-in carries the current memberships (GAD-T76). The library does this itself on a role change or
    /// removal; Gadfly's erasure endpoint calls this directly.
    /// </summary>
    public static Task RevokeSessionsAsync(GadflyDbContext db, Guid userId, CancellationToken ct) =>
        db.AuthUsers.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, Guid.NewGuid().ToString("N")), ct);

    internal static string Slug(string name)
    {
        var s = SlugChars().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return s.Length == 0 ? "org" : s.Length > 80 ? s[..80].TrimEnd('-') : s;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugChars();
}
