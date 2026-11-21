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

    /// <summary>An integrating app authenticated by an organization API key (GAD-T46); never a person.</summary>
    public const string Api = "Api";

    /// <summary>The roles a person can hold.</summary>
    public static readonly IReadOnlyList<string> All = [Owner, Admin, Instructor, Student];

    /// <summary>Roles that must use MFA (staff): everyone but students.</summary>
    public static bool RequiresMfa(string role) => role is Owner or Admin or Instructor;

    public static bool IsValid(string role) => All.Contains(role, StringComparer.Ordinal) || role == Api;
}

/// <summary>Gadfly's claim types.</summary>
public static class GadflyClaims
{
    /// <summary>One per membership, "{organizationId:N}|{role}", stamped at sign-in.</summary>
    public const string Membership = "gadfly:org";

    /// <summary>On API-key principals: the key's id.</summary>
    public const string ApiKeyId = "gadfly:apikey";
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
    /// <summary>An integrating app with an organization API key (platform API, GAD-T46).</summary>
    public const string Api = "gadfly:api";
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

/// <summary>
/// Reads the tenant from the request's signed-in user; no request means system work. Re-evaluated
/// whenever the request's principal changes (an endpoint policy that authenticates with another scheme,
/// such as the API key, replaces <c>HttpContext.User</c> after this scoped object may already exist).
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private sealed record State(bool IsSystem, Guid? UserId, Guid? OrganizationId, string? Role, IReadOnlyList<Membership> Memberships, bool IsSuperAdmin);

    private ClaimsPrincipal? _seen;
    private State? _state;

    private State Current
    {
        get
        {
            var http = accessor.HttpContext;
            if (http is null) return _state ??= new State(true, null, null, null, [], false);
            if (_state is not null && ReferenceEquals(_seen, http.User)) return _state;
            _seen = http.User;
            return _state = Read(http);
        }
    }

    private static State Read(HttpContext http)
    {
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true) return new State(false, null, null, null, [], false);
        var userId = Guid.TryParse(user.FindFirst(MaClaims.UserId)?.Value, out var uid) ? uid : (Guid?)null;
        var memberships = Parse(user);
        var wanted = Guid.TryParse(http.Request.Cookies[ITenantContext.ActiveOrgCookie], out var w) ? w : (Guid?)null;
        var active = memberships.FirstOrDefault(m => m.OrganizationId == wanted) ?? (memberships.Count == 1 ? memberships[0] : null);
        return new State(false, userId, active?.OrganizationId, active?.Role, memberships, user.IsInRole(MaRoles.Admin));
    }

    public bool IsSystem => Current.IsSystem;
    public Guid? UserId => Current.UserId;
    public Guid? OrganizationId => Current.OrganizationId;
    public string? Role => Current.Role;
    public IReadOnlyList<Membership> Memberships => Current.Memberships;
    public bool IsSuperAdmin => Current.IsSuperAdmin;

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
        authz.AddPolicy(GadflyPolicies.Api, p => p
            .AddAuthenticationSchemes(Platform.ApiKeyAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .AddRequirements(new OrgRoleRequirement(OrgRoles.Api)));
    }
}
