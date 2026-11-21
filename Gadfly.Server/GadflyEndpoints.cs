using Gadfly.Server.Components;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Web;

namespace Gadfly.Server;

/// <summary>
/// Every endpoint, default-deny (GAD-T41): the fallback policy requires a signed-in user, and only the
/// routes in <see cref="AnonymousRoutes"/> opt out. The Blazor hub gets no exemption.
/// </summary>
public static class GadflyEndpoints
{
    /// <summary>The library's anonymous auth posts (change-password stays signed-in only).</summary>
    public static readonly IReadOnlyList<string> AnonymousAuthPosts = ["login", "mfa-challenge", "logout", "reset/request", "reset/confirm"];

    /// <summary>Every route a signed-out visitor may reach.</summary>
    public static readonly IReadOnlyList<string> AnonymousRoutes =
        ["login", "mfa", "forgot-password", "account/reset", "healthz", "launch", "openapi/{documentName}.json", .. AnonymousAuthPosts.Select(p => "_ma-auth/" + p)];

    private static readonly IReadOnlyList<string> AnonymousPrefixes = ["_framework/"];

    public static void Map(WebApplication app)
    {
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .Add(endpoint =>
            {
                if (endpoint is RouteEndpointBuilder route
                    && AnonymousPrefixes.Any(p => (route.RoutePattern.RawText ?? "").TrimStart('/').StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    endpoint.Metadata.Add(new AllowAnonymousAttribute());
            });

        app.MapMindAtticAuthEndpoints(group =>
            ((IEndpointConventionBuilder)group).Finally(endpoint =>
            {
                if (endpoint is RouteEndpointBuilder route && IsAnonymousAuthPost(route.RoutePattern))
                {
                    endpoint.Metadata.Add(new AllowAnonymousAttribute());
                    endpoint.Metadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(Platform.RateLimits.Anonymous));
                }
            }));

        // Health: anonymous and data-free. Provider health is staff-only (it names providers and errors).
        app.MapHealthChecks("/healthz").AllowAnonymous();
        app.MapGet("/healthz/providers", (Gadfly.Socrates.LegionVotingClient panel) => Results.Json(panel.Health.Snapshot()))
            .RequireAuthorization(GadflyPolicies.SuperAdmin);

        MapPrivacy(app);
        app.MapHub<Sessions.SessionHub>(Sessions.SessionHub.Path).RequireRateLimiting(Platform.RateLimits.Hub);
        Platform.ApiEndpoints.Map(app);
        Review.ReviewExportEndpoints.Map(app);
        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

        // Switch the active organization: only to one the caller is a member of (checked against the
        // sign-in claims by HttpTenantContext on every request, so a forged cookie just falls back).
        app.MapPost("/org/switch", (HttpContext http, [Microsoft.AspNetCore.Mvc.FromForm] Guid organizationId) =>
        {
            if (HttpTenantContext.Parse(http.User).All(m => m.OrganizationId != organizationId)) return Results.Forbid();
            http.Response.Cookies.Append(ITenantContext.ActiveOrgCookie, organizationId.ToString("N"), new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromDays(30),
            });
            return Results.LocalRedirect("/");
        });
    }

    /// <summary>Data-subject export and erasure (GAD-T61).</summary>
    private static void MapPrivacy(WebApplication app)
    {
        // A student downloads their own data; an org admin downloads any member's.
        app.MapGet("/privacy/export/{userId:guid}", async (Guid userId, HttpContext http, ITenantContext tenant, IAuthorizationService authz,
            Privacy.PrivacyService privacy, CancellationToken ct) =>
        {
            if (tenant.OrganizationId is not { } org) return Results.Forbid();
            // Your own data, or (as an org admin, through the same MFA-aware policy as every admin page) a member's.
            var isSelf = tenant.UserId == userId;
            if (!isSelf && !(await authz.AuthorizeAsync(http.User, GadflyPolicies.OrgAdmin)).Succeeded) return Results.Forbid();
            var json = await privacy.ExportStudentAsync(org, userId, ct);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"gadfly-data-{userId:N}.json");
        });

        // Erasure is an org-admin decision, confirmed by re-typing the word DELETE.
        app.MapPost("/privacy/erase/{userId:guid}", async (Guid userId, [Microsoft.AspNetCore.Mvc.FromForm] string confirm,
            ITenantContext tenant, Privacy.PrivacyService privacy, GadflyDbContext db, CancellationToken ct) =>
        {
            if (confirm != "DELETE") return Results.BadRequest("Type DELETE to confirm.");
            var org = tenant.OrganizationId!.Value;
            // Erasing deactivates the membership: an owner is only erased by an owner, never the last one (GAD-T76).
            var target = await db.OrganizationMembers.AsNoTracking().FirstOrDefaultAsync(m => m.OrganizationId == org && m.UserId == userId, ct);
            if (target?.Role == OrgRoles.Owner && (tenant.Role != OrgRoles.Owner
                || await db.OrganizationMembers.CountAsync(m => m.OrganizationId == org && m.Role == OrgRoles.Owner && m.IsActive, ct) <= 1))
                return Results.Forbid();
            var n = await privacy.DeleteStudentDataAsync(org, userId, ct);
            if (target is not null) await OrganizationDirectory.RevokeSessionsAsync(db, userId, ct);
            return Results.LocalRedirect($"/org/members?erased={n}");
        }).RequireAuthorization(GadflyPolicies.OrgAdmin);
    }

    private static bool IsAnonymousAuthPost(RoutePattern pattern)
    {
        var raw = (pattern.RawText ?? "").Trim('/');
        return AnonymousAuthPosts.Any(p => raw.Equals(p, StringComparison.OrdinalIgnoreCase)
                                        || raw.Equals("_ma-auth/" + p, StringComparison.OrdinalIgnoreCase));
    }
}
