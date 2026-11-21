using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using MindAttic.Authentication;

namespace Gadfly.Server.Tenancy;

/// <summary>Roles a member holds inside one customer organization (GAD-T41).</summary>
public static class OrgRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Instructor = "Instructor";
    public const string Student = "Student";
    /// <summary>Human Examiner: scores examinations, independently of the AI (master plan Part 8).</summary>
    public const string Examiner = "Examiner";
    /// <summary>Second Reviewer: resolves AI/human disagreements (master plan Part 10.3).</summary>
    public const string SecondReviewer = "SecondReviewer";

    /// <summary>An integrating app authenticated by an organization API key (GAD-T46); never a person.</summary>
    public const string Api = "Api";

    /// <summary>The roles a person can hold.</summary>
    public static readonly IReadOnlyList<string> All = [Owner, Admin, Instructor, Examiner, SecondReviewer, Student];

    /// <summary>Roles that must use MFA (staff): everyone but students.</summary>
    public static bool RequiresMfa(string role) => role is Owner or Admin or Instructor or Examiner or SecondReviewer;

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

/// <summary>An immutable view of who is calling and in which organization.</summary>
public sealed record TenantSnapshot(bool IsSystem, Guid? UserId, Guid? OrganizationId, string? Role,
    IReadOnlyList<Membership> Memberships, bool IsSuperAdmin)
{
    /// <summary>Explicit system work (background jobs, startup): tenant filters off. Never a fallback.</summary>
    public static readonly TenantSnapshot System = new(true, null, null, null, [], false);

    /// <summary>No caller: sees no tenant-owned rows and may write none.</summary>
    public static readonly TenantSnapshot Nobody = new(false, null, null, null, [], false);

    /// <summary>Server code acting for one organization (no person, no role): filtered and guarded to it.</summary>
    public static TenantSnapshot ForOrganization(Guid organizationId) => new(false, null, organizationId, null, [], false);

    /// <summary>A copy of <paramref name="tenant"/>, for a child scope that must act exactly as its parent.</summary>
    public static TenantSnapshot Of(ITenantContext tenant) => tenant.IsSystem ? System
        : new(false, tenant.UserId, tenant.OrganizationId, tenant.Role, tenant.Memberships, tenant.IsSuperAdmin);

    /// <summary>
    /// The tenant of a signed-in principal: the organization named by the active-org cookie value when the
    /// principal holds a membership in it, else its only membership. Unauthenticated means <see cref="Nobody"/>.
    /// </summary>
    public static TenantSnapshot FromPrincipal(ClaimsPrincipal? user, string? activeOrgCookie)
    {
        if (user?.Identity?.IsAuthenticated != true) return Nobody;
        var userId = Guid.TryParse(user.FindFirst(MaClaims.UserId)?.Value, out var uid) ? uid : (Guid?)null;
        var memberships = HttpTenantContext.Parse(user);
        var wanted = Guid.TryParse(activeOrgCookie, out var w) ? w : (Guid?)null;
        var active = memberships.FirstOrDefault(m => m.OrganizationId == wanted) ?? (memberships.Count == 1 ? memberships[0] : null);
        return new(false, userId, active?.OrganizationId, active?.Role, memberships, user.IsInRole(MaRoles.Admin));
    }
}

/// <summary>
/// Scoped holder for a tenant pinned to the current DI scope (GAD-T73). Set by whoever opens the scope:
/// <see cref="TenantScopes.CreateSystemScope"/> for background work, <see cref="TenantScopes.CreateScopeFor"/>
/// for a child scope acting for a known tenant, <see cref="TenantCircuitHandler"/> for a Blazor circuit.
/// </summary>
public sealed class TenantScope
{
    public TenantSnapshot? Pinned { get; private set; }

    public void Pin(TenantSnapshot tenant) => Pinned = tenant;
}

/// <summary>
/// Who is calling (GAD-T41/T42/T73). In order: the tenant pinned to this DI scope; else the request's
/// signed-in user (re-evaluated whenever the request's principal changes: an endpoint policy that
/// authenticates with another scheme, such as the API key, replaces <c>HttpContext.User</c> after this
/// scoped object may already exist); else <see cref="TenantSnapshot.Nobody"/>. A missing HttpContext is
/// never system work: inside a Blazor circuit it is routinely absent (ASP.NET Core documents that
/// IHttpContextAccessor must not be relied on in interactive components), and treating that as system
/// would switch the tenant filter and write guard off. System work opts in with
/// <see cref="TenantScopes.CreateSystemScope"/>.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor, TenantScope? scope = null) : ITenantContext
{
    private ClaimsPrincipal? _seen;
    private TenantSnapshot? _state;

    private TenantSnapshot Current
    {
        get
        {
            if (scope?.Pinned is { } pinned) return pinned;
            var http = accessor.HttpContext;
            if (http is null) return TenantSnapshot.Nobody;
            if (_state is not null && ReferenceEquals(_seen, http.User)) return _state;
            _seen = http.User;
            return _state = TenantSnapshot.FromPrincipal(http.User, http.Request.Cookies[ITenantContext.ActiveOrgCookie]);
        }
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

/// <summary>Opening DI scopes with an explicit tenant (GAD-T73).</summary>
public static class TenantScopes
{
    /// <summary>A scope for background/system work that must see every organization (jobs, startup).</summary>
    public static IServiceScope CreateSystemScope(this IServiceScopeFactory scopes) => CreateScopeFor(scopes, TenantSnapshot.System);

    /// <summary>A scope acting as <paramref name="tenant"/> (e.g. a component's own scope inheriting the circuit's tenant).</summary>
    public static IServiceScope CreateScopeFor(this IServiceScopeFactory scopes, TenantSnapshot tenant)
    {
        var scope = scopes.CreateScope();
        scope.ServiceProvider.GetService<TenantScope>()?.Pin(tenant);
        return scope;
    }

    /// <inheritdoc cref="CreateScopeFor"/>
    public static AsyncServiceScope CreateAsyncScopeFor(this IServiceScopeFactory scopes, TenantSnapshot tenant)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetService<TenantScope>()?.Pin(tenant);
        return scope;
    }
}

/// <summary>
/// Pins a Blazor Server circuit's tenant when the circuit opens (GAD-T73): the user from the circuit's
/// <see cref="AuthenticationStateProvider"/> (kept current by the auth library's revalidation) and the
/// active-org cookie as the hub connection presented it. From then on the circuit never consults
/// IHttpContextAccessor, so a missing or different HttpContext can't change or widen what it sees.
/// </summary>
public sealed class TenantCircuitHandler(TenantScope scope, AuthenticationStateProvider auth, IHttpContextAccessor accessor)
    : CircuitHandler, IDisposable
{
    private string? _activeOrgCookie;
    private bool _subscribed;

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        scope.Pin(TenantSnapshot.Nobody);   // until the user is known: never system, never the ambient HttpContext
        _activeOrgCookie = accessor.HttpContext?.Request.Cookies[ITenantContext.ActiveOrgCookie];
        var state = await auth.GetAuthenticationStateAsync();
        scope.Pin(TenantSnapshot.FromPrincipal(state.User, _activeOrgCookie));
        auth.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _subscribed = true;
    }

    private async void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        try { scope.Pin(TenantSnapshot.FromPrincipal((await task).User, _activeOrgCookie)); }
        catch { scope.Pin(TenantSnapshot.Nobody); }
    }

    public void Dispose()
    {
        if (_subscribed) auth.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }
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
