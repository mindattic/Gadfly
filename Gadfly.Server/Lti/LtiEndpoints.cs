using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Gadfly.Server.Lti;

/// <summary>
/// LTI 1.3's own endpoints (GAD-T312): the OIDC third-party-initiated login (<c>/lti/login</c>), the launch it leads to
/// (<c>/lti/launch</c>), Gadfly's public key set (<c>/lti/jwks.json</c>), the deep-linking picker's submit
/// (<c>/lti/deep-link/submit</c>) and the exam hand-off for a launch shown inside an LMS frame (<c>/lti/continue</c>).
/// All anonymous by nature (a platform-initiated flow happens before Gadfly knows who is calling), each checked on its
/// own terms instead: a single-use state bound to the browser by a cookie, a verified id_token, a protected
/// short-lived token. The admin side (registrations, roster sync) is the Blazor page <c>/org/lti</c>.
/// </summary>
public static class LtiEndpoints
{
    public const string LoginPath = "/lti/login";
    public const string LaunchPath = "/lti/launch";
    public const string JwksPath = "/lti/jwks.json";
    public const string DeepLinkSubmitPath = "/lti/deep-link/submit";
    public const string ContinuePath = "/lti/continue";

    /// <summary>The cookie that binds a login's state to the browser that started it: one per login, so two launches in
    /// two tabs don't overwrite each other. SameSite=None (the launch is a cross-site form post from the LMS),
    /// Partitioned (so it still works inside an LMS frame where third-party cookies are otherwise blocked).</summary>
    public static string StateCookieName(string state) => "gadfly-lti-" + state[..Math.Min(12, state.Length)];

    private static readonly TimeSpan HandoffLifetime = TimeSpan.FromMinutes(2);

    private sealed record Handoff(Guid UserId, Guid OrganizationId, Guid AssignmentId, string Id);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapMethods(LoginPath, ["GET", "POST"], async (HttpContext http, IServiceScopeFactory scopes, CancellationToken ct) =>
            {
                var p = await ParamsAsync(http, ct);
                var req = new LtiLaunchService.LoginRequest(p.GetValueOrDefault("iss") ?? "", p.GetValueOrDefault("client_id"),
                    p.GetValueOrDefault("login_hint"), p.GetValueOrDefault("lti_message_hint"), p.GetValueOrDefault("target_link_uri"), p.GetValueOrDefault("lti_deployment_id"));
                await using var system = scopes.CreateAsyncScopeFor(TenantSnapshot.System);   // nobody is signed in yet: find the registration across organizations
                var start = await system.ServiceProvider.GetRequiredService<LtiLaunchService>().BeginLoginAsync(req, BaseUrl(http) + LaunchPath, ct);
                if (start.Error is not null) return LtiPages.Message(http, "Can't open this from your LMS", start.Error, status: StatusCodes.Status400BadRequest);
                http.Response.Cookies.Append(StateCookieName(start.State!), start.State!, StateCookieOptions());
                return Results.Redirect(start.RedirectUrl!);
            })
            .AllowAnonymous().DisableAntiforgery().RequireRateLimiting(RateLimits.Anonymous).ExcludeFromDescription();

        app.MapPost(LaunchPath, async (HttpContext http, IServiceScopeFactory scopes, TimeProvider clock, IDataProtectionProvider protection, CancellationToken ct) =>
            {
                var form = await http.Request.ReadFormAsync(ct);
                var state = form["state"].ToString();
                var cookieName = string.IsNullOrEmpty(state) ? null : StateCookieName(state);
                var browserState = cookieName is null ? null : http.Request.Cookies[cookieName];
                if (cookieName is not null) http.Response.Cookies.Delete(cookieName, StateCookieOptions());

                LtiLaunchResult result;
                await using (var system = scopes.CreateAsyncScopeFor(TenantSnapshot.System))
                    result = await system.ServiceProvider.GetRequiredService<LtiLaunchService>().ValidateLaunchAsync(form["id_token"], state, browserState, ct);
                if (!result.Ok) return LtiPages.Message(http, "Can't open this from your LMS", result.Error!, status: StatusCodes.Status400BadRequest);
                var reg = result.Registration!;
                var claims = result.Claims!;

                // Everything from here acts for the registration's organization only: filtered to it, and every write guarded to it.
                await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(reg.OrganizationId));
                var sp = scope.ServiceProvider;
                var db = sp.GetRequiredService<GadflyDbContext>();

                if (result.MessageType == LtiClaims.DeepLinkingRequest)
                {
                    var who = await sp.GetRequiredService<LtiUserProvisioner>().ProvisionAsync(reg, claims, ct);
                    if (!who.Ok) return LtiPages.Message(http, "Can't add a Gadfly exam", who.Error!, status: StatusCodes.Status403Forbidden);
                    return Render(http, await sp.GetRequiredService<LtiDeepLinking>().PickerAsync(reg, claims, who.UserId!.Value, ct));
                }
                if (result.MessageType != LtiClaims.ResourceLinkRequest)
                    return LtiPages.Message(http, "Can't open this from your LMS", $"Gadfly does not handle the LMS message \"{result.MessageType}\".", status: StatusCodes.Status400BadRequest);
                if (claims.ContextId is not { Length: > 0 } contextId || claims.ResourceLinkId is not { Length: > 0 } resourceLinkId)
                    return LtiPages.Message(http, "Can't open this from your LMS", "This launch named no course or no placement, so Gadfly can't tell which exam it is for.", status: StatusCodes.Status400BadRequest);

                var person = await sp.GetRequiredService<LtiUserProvisioner>().ProvisionAsync(reg, claims, ct);
                if (!person.Ok) return LtiPages.Message(http, "Can't open this exam", person.Error!, status: StatusCodes.Status403Forbidden);
                var userId = person.UserId!.Value;
                var now = clock.GetUtcNow();

                var link = await PlacementAsync(db, reg, claims, contextId, resourceLinkId, userId, now, ct);
                if (link is null)
                    return LtiPages.Message(http, "Not set up yet", "This assignment isn't linked to a Gadfly exam yet. An instructor adds one through the course's content picker (\"External tool\", \"Gadfly Exams\"); after that this link opens it.");

                var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == link.ExamId && e.OrganizationId == reg.OrganizationId, ct);
                if (exam is null) return LtiPages.Message(http, "Not available", "The Gadfly exam this assignment pointed to no longer exists. Ask your instructor.");

                if (person.Role != OrgRoles.Student)
                {
                    // Staff need Gadfly's own sign-in with MFA for grading and editing: an LMS launch never satisfies that.
                    return LtiPages.Message(http, "Linked to Gadfly",
                        $"This assignment is linked to the Gadfly exam \"{exam.Title}\". Students who open it go straight into the exam, and their grade comes back here once it's released. "
                        + "To see results, grade or change the exam, sign in to Gadfly directly with your multi-factor sign-in.",
                        $"<p><a href=\"{LtiPages.Encode(BaseUrl(http))}/login\" target=\"_blank\" rel=\"noopener\">Open Gadfly in a new window</a></p>");
                }
                if (exam.Status != ExamStatus.Published)
                    return LtiPages.Message(http, "Not available", "This exam isn't open for attempts right now. Ask your instructor.");

                var assignment = await AssignmentAsync(db, reg.OrganizationId, exam, link, userId, claims.Subject, now, ct);

                // Top-level window: sign in and go. Inside an LMS frame the exam can't run (camera, microphone, cookies),
                // so hand off to a new window with a short-lived single-use token.
                if (!InFrame(http))
                {
                    await LtiSignIn.SignInAsync(http, db, userId, reg.OrganizationId, clock, ct);
                    return Results.Redirect(StartPath(assignment.Id));
                }
                var token = protection.CreateProtector("Gadfly.Lti.Handoff").ToTimeLimitedDataProtector()
                    .Protect(JsonSerializer.Serialize(new Handoff(userId, reg.OrganizationId, assignment.Id, LtiLaunchService.RandomToken(24))), now + HandoffLifetime);
                var body = $"""
                    <h1>{LtiPages.Encode(exam.Title)}</h1>
                    <p>Your Gadfly exam opens in its own window, so it can use your camera and microphone.</p>
                    <form method="post" action="{ContinuePath}" target="_blank"><input type="hidden" name="t" value="{LtiPages.Encode(token)}" />
                    <button type="submit">Open the exam in a new window</button></form>
                    <p class="hint">This button works for two minutes. If it has expired, open the assignment from your course again.</p>
                    """;
                LtiPages.ApplyHeaders(http);
                return Results.Content(LtiPages.Document(exam.Title, body), "text/html; charset=utf-8");
            })
            .AllowAnonymous().DisableAntiforgery().RequireRateLimiting(RateLimits.Anonymous).ExcludeFromDescription();

        app.MapPost(ContinuePath, async (HttpContext http, IServiceScopeFactory scopes, IDataProtectionProvider protection, IMemoryCache used, TimeProvider clock, CancellationToken ct) =>
            {
                var form = await http.Request.ReadFormAsync(ct);
                Handoff? handoff;
                try { handoff = JsonSerializer.Deserialize<Handoff>(protection.CreateProtector("Gadfly.Lti.Handoff").ToTimeLimitedDataProtector().Unprotect(form["t"].ToString())); }
                catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException or FormatException) { handoff = null; }
                if (handoff is null)
                    return LtiPages.Message(http, "This link has expired", "Open the assignment from your course again.", status: StatusCodes.Status400BadRequest);
                // Single use (per server instance; the token also dies in two minutes).
                if (used.TryGetValue("lti-handoff:" + handoff.Id, out _))
                    return LtiPages.Message(http, "This link was already used", "Open the assignment from your course again.", status: StatusCodes.Status400BadRequest);
                used.Set("lti-handoff:" + handoff.Id, true, HandoffLifetime + TimeSpan.FromMinutes(1));

                await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(handoff.OrganizationId));
                var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                var member = await db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == handoff.OrganizationId && m.UserId == handoff.UserId && m.IsActive && m.Role == OrgRoles.Student, ct);
                if (!member) return LtiPages.Message(http, "Can't open this exam", "Your access to this organization in Gadfly has been removed.", status: StatusCodes.Status403Forbidden);
                try { await LtiSignIn.SignInAsync(http, db, handoff.UserId, handoff.OrganizationId, clock, ct); }
                catch (InvalidOperationException) { return LtiPages.Message(http, "Can't open this exam", "Your Gadfly account has been turned off.", status: StatusCodes.Status403Forbidden); }
                return Results.Redirect(StartPath(handoff.AssignmentId));
            })
            .AllowAnonymous().DisableAntiforgery().RequireRateLimiting(RateLimits.Anonymous).ExcludeFromDescription();

        app.MapGet(JwksPath, async (HttpContext http, LtiKeys keys, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "public, max-age=300";
                return Results.Text(await keys.JwksJsonAsync(ct), "application/json");
            })
            .AllowAnonymous().ExcludeFromDescription();

        app.MapPost(DeepLinkSubmitPath, async (HttpContext http, IServiceScopeFactory scopes, CancellationToken ct) =>
            {
                var form = await http.Request.ReadFormAsync(ct);
                await using var probe = scopes.CreateAsyncScope();
                var continuation = probe.ServiceProvider.GetRequiredService<LtiDeepLinking>().UnprotectContinuation(form["continuation"].ToString());
                if (continuation is null)
                    return LtiPages.Message(http, "Can't add this exam", "This page has expired. Go back to the course and open the content picker again.", status: StatusCodes.Status400BadRequest);
                var examIds = form["examId"].Select(v => Guid.TryParse(v, out var g) ? g : (Guid?)null).OfType<Guid>().Distinct().ToList();
                if (examIds.Count == 0)
                    return LtiPages.Message(http, "Can't add this exam", "Choose an exam first. Go back and pick one.", status: StatusCodes.Status400BadRequest);
                await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(continuation.OrganizationId));
                return Render(http, await scope.ServiceProvider.GetRequiredService<LtiDeepLinking>().ResponseAsync(continuation, examIds, BaseUrl(http) + LaunchPath, ct));
            })
            .AllowAnonymous().DisableAntiforgery().RequireRateLimiting(RateLimits.Anonymous).ExcludeFromDescription();
    }

    /// <summary>The placement for this course and link: the one already recorded, or (first launch after deep linking) a new one
    /// for the exam the platform's custom parameters name. Keeps its AGS and NRPS addresses current with what the platform sends.</summary>
    private static async Task<LtiResourceLink?> PlacementAsync(GadflyDbContext db, LtiRegistration reg, LtiLaunchClaims claims, string contextId, string resourceLinkId,
        Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var link = await db.LtiResourceLinks.FirstOrDefaultAsync(l => l.RegistrationId == reg.Id && l.ContextId == contextId && l.ResourceLinkId == resourceLinkId
            && l.OrganizationId == reg.OrganizationId, ct);
        if (link is null)
        {
            if (claims.CustomExamId is not { } examId || !await db.Exams.AnyAsync(e => e.Id == examId && e.OrganizationId == reg.OrganizationId, ct)) return null;
            link = new LtiResourceLink
            {
                OrganizationId = reg.OrganizationId, RegistrationId = reg.Id, ContextId = Cap(contextId, 255), ResourceLinkId = Cap(resourceLinkId, 255),
                ExamId = examId, LineItemsUrl = Url(claims.LineItemsUrl), LineItemUrl = Url(claims.LineItemUrl), ContextMembershipsUrl = Url(claims.ContextMembershipsUrl),
                CreatedByUserId = userId, CreatedAt = now,
            };
            db.LtiResourceLinks.Add(link);
            db.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = reg.OrganizationId, ActorUserId = userId, ActorRole = "LTI", Action = "lti.placement-linked", EntityType = "LtiResourceLink",
                EntityId = link.Id.ToString(), AfterJson = JsonSerializer.Serialize(new { connection = reg.Name, course = claims.ContextTitle ?? claims.ContextLabel ?? contextId, examId }), At = now,
            });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();   // two first launches at once: use the row the other one made
                return await db.LtiResourceLinks.FirstOrDefaultAsync(l => l.RegistrationId == reg.Id && l.ContextId == contextId && l.ResourceLinkId == resourceLinkId, ct);
            }
            return link;
        }
        var changed = false;
        if (Url(claims.LineItemsUrl) is { } items && items != link.LineItemsUrl) { link.LineItemsUrl = items; changed = true; }
        if (Url(claims.LineItemUrl) is { } item && item != link.LineItemUrl) { link.LineItemUrl = item; changed = true; }
        if (Url(claims.ContextMembershipsUrl) is { } roster && roster != link.ContextMembershipsUrl) { link.ContextMembershipsUrl = roster; changed = true; }
        if (changed) await db.SaveChangesAsync(ct);
        return link;
    }

    private static async Task<ExamAssignment> AssignmentAsync(GadflyDbContext db, Guid org, Exam exam, LtiResourceLink link, Guid userId, string subject,
        DateTimeOffset now, CancellationToken ct)
    {
        var assignment = await db.ExamAssignments.FirstOrDefaultAsync(a => a.ExamId == exam.Id && a.StudentUserId == userId && a.OrganizationId == org, ct);
        if (assignment is null)
        {
            assignment = new ExamAssignment
            {
                OrganizationId = org, ExamId = exam.Id, StudentUserId = userId, AttemptsAllowed = 1,
                AssignedByUserId = exam.CreatedByUserId != Guid.Empty ? exam.CreatedByUserId : link.CreatedByUserId, AssignedAt = now,
            };
            db.ExamAssignments.Add(assignment);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                assignment = await db.ExamAssignments.FirstAsync(a => a.ExamId == exam.Id && a.StudentUserId == userId && a.OrganizationId == org, ct);
            }
        }
        if (!await db.LtiAssignmentLinks.AnyAsync(l => l.AssignmentId == assignment.Id && l.OrganizationId == org, ct))
        {
            db.LtiAssignmentLinks.Add(new LtiAssignmentLink
            {
                AssignmentId = assignment.Id, OrganizationId = org, ResourceLinkId = link.Id, LtiSubject = Cap(subject, 255), CreatedAt = now,
            });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); }
        }
        return assignment;
    }

    private static IResult Render(HttpContext http, LtiPage page)
    {
        LtiPages.ApplyHeaders(http, page.FrameAncestors, page.FormAction, page.ScriptNonce);
        return Results.Content(page.Html, "text/html; charset=utf-8");
    }

    /// <summary>True when the browser says this navigation is inside a frame (Fetch Metadata). A browser that doesn't send
    /// the header is treated as top-level, which only means the student signs in where they are.</summary>
    public static bool InFrame(HttpContext http) =>
        http.Request.Headers["Sec-Fetch-Dest"].ToString() is "iframe" or "frame" or "embed" or "object";

    private static string StartPath(Guid assignmentId) => $"/my-exams/{assignmentId:D}/start";

    public static string BaseUrl(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}";

    private static CookieOptions StateCookieOptions()
    {
        var o = new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.None, IsEssential = true, Path = "/lti",
            MaxAge = LtiLaunchService.LoginLifetime,
        };
        o.Extensions.Add("Partitioned");
        return o;
    }

    private static string? Url(string? s) => s is { Length: > 0 and <= 1000 } && Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? s : null;

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max];

    private static async Task<IReadOnlyDictionary<string, string?>> ParamsAsync(HttpContext http, CancellationToken ct)
    {
        if (HttpMethods.IsPost(http.Request.Method) && http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync(ct);
            return form.Keys.ToDictionary(k => k, k => (string?)form[k].ToString());
        }
        return http.Request.Query.Keys.ToDictionary(k => k, k => (string?)http.Request.Query[k].ToString());
    }
}
