using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MindAttic.Authentication;

namespace Gadfly.Server.Tenancy;

/// <summary>Roles a member holds inside one customer organization (GAD-T41).</summary>
public static class OrgRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Instructor = "Instructor";
    public const string Student = "Student";

    public static readonly IReadOnlyList<string> All = [Owner, Admin, Instructor, Student];

    /// <summary>Roles that must use MFA (staff): everyone but students.</summary>
    public static bool RequiresMfa(string role) => role is Owner or Admin or Instructor;

    public static bool IsValid(string role) => All.Contains(role, StringComparer.Ordinal);
}

/// <summary>Gadfly's claim types.</summary>
public static class GadflyClaims
{
    /// <summary>One per membership, "{organizationId:N}|{role}", stamped at sign-in.</summary>
    public const string Membership = "gadfly:org";
}

/// <summary>Gadfly's authorization policy names.</summary>
public static class GadflyPolicies
{
    /// <summary>MindAttic platform staff (the library's Admin role with MFA).</summary>
    public const string SuperAdmin = MaPolicies.Admin;
    /// <summary>Owner or Admin of the active organization.</summary>
    public const string OrgAdmin = "gadfly:org-admin";
    /// <summary>Owner, Admin or Instructor of the active organization.</summary>
    public const string Instructor = "gadfly:instructor";
    /// <summary>Student of the active organization.</summary>
    public const string Student = "gadfly:student";
    /// <summary>Any member of the active organization.</summary>
    public const string Member = "gadfly:member";
}

/// <summary>One membership, as read from the sign-in claims.</summary>
public sealed record Membership(Guid OrganizationId, string Role);

/// <summary>
/// Who is calling and in which organization (GAD-T41/T42). The active organization is the one named by
/// the <see cref="ActiveOrgCookie"/> cookie when the user holds a membership in it, else their only
/// membership. Everything tenant-owned is scoped to it by <see cref="Data.GadflyDbContext"/>'s filter.
/// </summary>
public interface ITenantContext
{
    /// <summary>True for work with no signed-in caller (background jobs, startup): tenant filters are off.</summary>
    bool IsSystem { get; }
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    string? Role { get; }
    IReadOnlyList<Membership> Memberships { get; }
    bool IsSuperAdmin { get; }

    public const string ActiveOrgCookie = "gadfly-org";
}

/// <summary>Reads the tenant from the request's signed-in user; no request means system work.</summary>
public sealed class HttpTenantContext : ITenantContext
{
    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        var http = accessor.HttpContext;
        if (http is null)
        {
            IsSystem = true;
            return;
        }
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true) return;
        UserId = Guid.TryParse(user.FindFirst(MaClaims.UserId)?.Value, out var uid) ? uid : null;
        IsSuperAdmin = user.IsInRole(MaRoles.Admin);
        Memberships = Parse(user);
        var wanted = Guid.TryParse(http.Request.Cookies[ITenantContext.ActiveOrgCookie], out var w) ? w : (Guid?)null;
        var active = Memberships.FirstOrDefault(m => m.OrganizationId == wanted)
                     ?? (Memberships.Count == 1 ? Memberships[0] : null);
        OrganizationId = active?.OrganizationId;
        Role = active?.Role;
    }

    public bool IsSystem { get; }
    public Guid? UserId { get; }
    public Guid? OrganizationId { get; }
    public string? Role { get; }
    public IReadOnlyList<Membership> Memberships { get; } = [];
    public bool IsSuperAdmin { get; }

    public static IReadOnlyList<Membership> Parse(ClaimsPrincipal user) =>
        user.FindAll(GadflyClaims.Membership).Select(c => c.Value.Split('|'))
            .Where(p => p.Length == 2 && Guid.TryParse(p[0], out _) && OrgRoles.IsValid(p[1]))
            .Select(p => new Membership(Guid.Parse(p[0]), p[1]))
            .ToList();
}

/// <summary>Requires the caller's role in the active organization to be one of <see cref="Roles"/>.</summary>
public sealed class OrgRoleRequirement(params string[] roles) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Roles { get; } = roles;
}

public sealed class OrgRoleHandler(ITenantContext tenant) : AuthorizationHandler<OrgRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OrgRoleRequirement requirement)
    {
        if (tenant.OrganizationId is not null && tenant.Role is { } role && requirement.Roles.Contains(role))
        {
            // Staff roles need an MFA sign-in, like the library's Admin policy.
            if (!OrgRoles.RequiresMfa(role) || context.User.HasClaim(MaClaims.Amr, "mfa") || !MfaRequired)
                context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }

    /// <summary>Off only for local development without an authenticator (Development environment).</summary>
    public static bool MfaRequired { get; set; } = true;
}

public static class TenancyAuthorization
{
    /// <summary>Gadfly's policies: signed-in fallback plus the organization-role policies.</summary>
    public static void Configure(AuthorizationBuilder authz)
    {
        authz.SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        authz.AddPolicy(GadflyPolicies.OrgAdmin, p => p.RequireAuthenticatedUser().AddRequirements(new OrgRoleRequirement(OrgRoles.Owner, OrgRoles.Admin)));
        authz.AddPolicy(GadflyPolicies.Instructor, p => p.RequireAuthenticatedUser().AddRequirements(new OrgRoleRequirement(OrgRoles.Owner, OrgRoles.Admin, OrgRoles.Instructor)));
        authz.AddPolicy(GadflyPolicies.Student, p => p.RequireAuthenticatedUser().AddRequirements(new OrgRoleRequirement(OrgRoles.Student)));
        authz.AddPolicy(GadflyPolicies.Member, p => p.RequireAuthenticatedUser().AddRequirements(new OrgRoleRequirement([.. OrgRoles.All])));
    }
}
