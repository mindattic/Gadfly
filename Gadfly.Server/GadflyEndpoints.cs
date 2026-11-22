using Gadfly.Server.Components;
using Gadfly.Server.Analytics;
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
    /// <summary>The library's anonymous auth posts (change-password and invite/accept-existing stay signed-in only).</summary>
    public static readonly IReadOnlyList<string> AnonymousAuthPosts = ["login", "mfa-challenge", "logout", "reset/request", "reset/confirm", "invite/accept"];

    /// <summary>Every route a signed-out visitor may reach.</summary>
    public static readonly IReadOnlyList<string> AnonymousRoutes =
        ["login", "mfa", "forgot-password", "account/reset", "account/accept-invite", "healthz", "launch", "error", "openapi/{documentName}.json", "transcripts/verify", .. AnonymousAuthPosts.Select(p => "_ma-auth/" + p),
         .. Help.HelpEndpoints.AnonymousRoutes,   // the help center: a student with a broken microphone may never get past sign-in (GAD-T292)
         .. Site.PublicSite.AnonymousRoutes];   // the public site: landing, product and legal pages, robots, sitemap, the 404 catch-all (GAD-T308)

    /// <summary>The path <c>UseExceptionHandler</c> re-executes in production (Program.cs).</summary>
    public const string ErrorPath = "/error";

    private static readonly IReadOnlyList<string> AnonymousPrefixes = ["_framework/"];

    public static void Map(WebApplication app)
    {
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .WithStaticAssets()   // fingerprinted, long-cached asset URLs for any @Assets[...] reference (GAD-T317)
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
        // Outside-call durations on live exams (GAD-T413): /healthz/call-timings?session=<id>&limit=200, staff-only.
        Diagnostics.ApiCallTimingServices.MapCallTimings(app).RequireAuthorization(GadflyPolicies.SuperAdmin);
        // Recognizer latency and unsure-word rate, in memory (GAD-T361), staff-only.
        Diagnostics.VoiceRecognitionTimingsServices.MapVoiceRecognitionTimings(app).RequireAuthorization(GadflyPolicies.SuperAdmin);

        // Where Program.cs's production exception handler re-executes (GAD-T190): an anonymous, data-free page, whatever the verb of the
        // failed request. Without this endpoint the handler found nothing, the fallback policy would have redirected it to sign-in, and the
        // visitor got a bare 500 and an extra "handler produced a 404" exception in the log.
        app.MapMethods(ErrorPath, ["GET", "POST", "PUT", "PATCH", "DELETE"], (HttpContext http) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                var failed = http.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>() is not null;
                return Results.Content(Site.PublicSite.ErrorPageHtml, "text/html; charset=utf-8", System.Text.Encoding.UTF8,
                    failed ? StatusCodes.Status500InternalServerError : StatusCodes.Status404NotFound);
            })
            .AllowAnonymous().ExcludeFromDescription();

        MapPrivacy(app);
        app.MapHub<Sessions.SessionHub>(Sessions.SessionHub.Path).RequireRateLimiting(Platform.RateLimits.Hub);
        Platform.ApiEndpoints.Map(app);
        Review.ReviewExportEndpoints.Map(app);
        Transcripts.TranscriptEndpoints.Map(app);   // downloads and the anonymous /transcripts/verify page (GAD-T295)
        Reports.CertificateEndpoints.Map(app);   // completion certificate PDF, verified at /transcripts/verify too (GAD-T311)
        app.MapAnalytics();
        Admin.PeopleExportEndpoints.MapPeopleExports(app);   // students / assignments CSV (GAD-T296, GAD-T298)
        Admin.PlanExportEndpoints.MapPlanExport(app);   // invoice-ready usage CSV (GAD-T371)
        Platform.CheckoutEndpoints.MapCheckout(app);   // optional card checkout, off without Stripe keys (GAD-T372)
        Admin.ExamAuditEndpoints.Map(app);   // exam definition JSON (GAD-T297), audit log CSV/JSON (GAD-T301)
        Research.ResearchEndpoints.Map(app);   // de-identified research export (GAD-T451)
        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
        Help.HelpEndpoints.Map(app);   // images the help guides show (GAD-T292)
        Site.PublicSite.MapPublicSite(app);   // robots.txt and sitemap.xml (GAD-T308)
        Lti.LtiSetup.MapLti(app);   // LTI 1.3 login, launch, JWKS, deep linking (GAD-T312)

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
