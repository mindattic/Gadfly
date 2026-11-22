using System.Security.Claims;
using System.Security.Cryptography;
using Gadfly.Server.Data;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MindAttic.Authentication;
using MindAttic.Authentication.Options;
using MindAttic.Authentication.Services;
using MindAttic.Authentication.Web;

namespace Gadfly.Server.Lti;

/// <summary>A person resolved (or newly provisioned) for an LTI identity, or the plain-English reason they can't be.</summary>
public sealed record LtiProvisionResult(Guid? UserId, string? Error, bool Created = false, string? Role = null)
{
    public bool Ok => UserId is not null;
    /// <summary>GAD-T458: the LMS identity looks like an existing member and waits to be linked (see <see cref="LtiAccountLinks"/>).</summary>
    public Guid? LinkRequestId { get; init; }
    /// <summary>The existing account's email is verified, so the person can confirm by email instead of waiting for an admin.</summary>
    public bool CanConfirmByEmail { get; init; }
    public static LtiProvisionResult Fail(string error) => new(null, error);

    public static LtiProvisionResult WaitingForLink(LtiLinkRequest request, bool canConfirmByEmail) =>
        new(null, "This LMS sign-in uses the same email address as an existing Gadfly account in this organization. Gadfly links the two only once "
            + "that is confirmed: by the organization's Gadfly admin on the LMS connection page" + (canConfirmByEmail ? ", or by you through a link emailed to that account" : "") + ".")
        { LinkRequestId = request.Id, CanConfirmByEmail = canConfirmByEmail };
}

/// <summary>
/// Roster and role mapping (GAD-T312/GAD-T382): resolves an LTI identity (the platform's <c>sub</c>, stable per
/// registration) to the Gadfly account mapped to it, provisioning the account and the organization membership on first
/// sight. A launch and an NRPS roster row go through exactly this, so a person is the same Gadfly account whichever
/// way they arrive. Runs in a scope pinned to the registration's organization, so the tenant write guard holds.
/// <list type="bullet">
/// <item>Learner becomes Student; Instructor, Teaching Assistant, Content Developer and course Administrator become
/// Instructor (<see cref="LtiClaims.GadflyRoleOf"/>). Anything else is refused, never guessed.</item>
/// <item>Never links to an existing Gadfly account by a matching email alone: that would hand an LMS identity someone else's
/// account. When the LMS email equals exactly one active member's, nothing is created and an <see cref="LtiLinkRequest"/>
/// waits for an admin's confirmation or the person's own, through that account's verified email (GAD-T458,
/// <see cref="LtiAccountLinks"/>). Otherwise an LTI identity only ever reaches the account created for it.</item>
/// <item>A role only ever moves up from the LMS (a Student who becomes a TA becomes an Instructor); it is never moved
/// down automatically, and Owner, Admin, Examiner and Second Reviewer are never touched, so a change an admin made in
/// Gadfly sticks.</item>
/// <item>A member an admin removed, or an account an admin turned off, stays out: the LMS cannot restore them.</item>
/// <item>A new member spends a seat like any other (the plan's seat limit applies).</item>
/// </list>
/// </summary>
public sealed class LtiUserProvisioner(GadflyDbContext db, IUserAdminService users, IOrganizationService orgs, PlanEnforcement plans, LtiAccountLinks links, TimeProvider clock)
{
    public const string SourceLaunch = "launch";
    public const string SourceRoster = "roster";

    /// <param name="source"><see cref="SourceLaunch"/> or <see cref="SourceRoster"/>: how Gadfly first saw the person, for a link request.</param>
    public async Task<LtiProvisionResult> ProvisionAsync(LtiRegistration reg, LtiLaunchClaims claims, CancellationToken ct, string source = SourceLaunch)
    {
        if (claims.GadflyRole is not { } role)
            return LtiProvisionResult.Fail("Your role in this course (" + (claims.Roles.Count == 0 ? "none given" : string.Join(", ", claims.Roles.Select(ShortRole))) + ") "
                + "is not one Gadfly takes part in. Gadfly works with instructors, teaching assistants and students (learners).");

        var mapping = await db.LtiUserMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.RegistrationId == reg.Id && m.LtiSubject == claims.Subject && m.OrganizationId == reg.OrganizationId, ct);
        if (mapping is not null) return await ExistingAsync(reg, mapping.GadflyUserId, role, ct);

        // GAD-T458: someone who already has an account here is not given a second one, nor linked silently.
        var request = await db.LtiLinkRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RegistrationId == reg.Id && r.LtiSubject == claims.Subject && r.OrganizationId == reg.OrganizationId, ct);
        if (request?.Status == LtiLinkRequest.Pending)
            return LtiProvisionResult.WaitingForLink(request, await EmailVerifiedAsync(request.CandidateUserId, ct));
        if (request is null && await links.CandidateAsync(reg, claims.Email, ct) is { } match)
            return LtiProvisionResult.WaitingForLink(await links.RequestAsync(reg, claims, role, match, source, ct), match.EmailVerified);

        // The seat lock is held from the count until the membership is saved (GAD-T481): two first launches, or a launch and a
        // roster sync, can't both take the last free seat.
        await using var seats = await SeatLock.AcquireAsync(db, reg.OrganizationId, ct);
        if (!seats.Acquired) return LtiProvisionResult.Fail(SeatLock.BusyMessage);
        var (seatOk, seatMessage) = await plans.CheckSeatAsync(db, reg.OrganizationId, role, ct);
        if (!seatOk) return LtiProvisionResult.Fail(seatMessage!);

        var email =claims.Email is { } e && OrganizationDirectory.IsValidEmail(e.Trim()) ? e.Trim() : null;
        var baseName = Sanitize(email is not null ? email.Split('@')[0] : claims.Name ?? claims.GivenName ?? "lms-user");
        var candidate = baseName;
        for (var suffix = 2; await db.AuthUsers.AsNoTracking().AnyAsync(u => u.NormalizedUserName == IUserStore.Normalize(candidate), ct); suffix++)
            candidate = $"{baseName}{suffix}";

        // A random password nobody is told: the account is reached through the LMS (or, for staff who want the full app,
        // through "forgot password" to the email the LMS gave, then their own MFA set-up).
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        var displayName = claims.DisplayName.Length > 200 ? claims.DisplayName[..200] : claims.DisplayName;
        var created = await users.CreateAsync(candidate, email, OrganizationDirectory.AccountRole, password,
            mustChangePassword: false, displayName: displayName, ct: ct);
        if (!created.Ok || created.UserId is not { } userId)
            return LtiProvisionResult.Fail($"Gadfly could not create your account ({created.Error}). Ask your instructor to contact the organization's Gadfly admin.");

        var added = await orgs.AddMemberAsync(reg.OrganizationId, userId, role, actingRole: null, actorUserId: null, ct);
        if (!added.Ok)
        {
            await db.AuthUsers.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);   // never leave an orphan account behind
            return LtiProvisionResult.Fail($"Gadfly could not add you to the organization ({added.Message}).");
        }

        db.LtiUserMappings.Add(new LtiUserMapping
        {
            OrganizationId = reg.OrganizationId, RegistrationId = reg.Id, LtiSubject = claims.Subject, GadflyUserId = userId, CreatedAt = clock.GetUtcNow(),
        });
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = reg.OrganizationId, ActorUserId = null, ActorRole = "LTI", Action = "lti.user-provisioned",
            EntityType = "User", EntityId = userId.ToString(), Reason = $"First seen through the LMS connection '{reg.Name}' as {role}.", At = clock.GetUtcNow(),
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two first launches of the same person at once: the other request's mapping won. Use it and drop ours.
            db.ChangeTracker.Clear();
            var winner = await db.LtiUserMappings.AsNoTracking().FirstOrDefaultAsync(m => m.RegistrationId == reg.Id && m.LtiSubject == claims.Subject, ct);
            if (winner is null) throw;
            await db.OrganizationMembers.Where(m => m.OrganizationId == reg.OrganizationId && m.UserId == userId).ExecuteDeleteAsync(ct);
            await db.AuthUsers.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
            return await ExistingAsync(reg, winner.GadflyUserId, role, ct);
        }
        return new LtiProvisionResult(userId, null, Created: true, Role: role);
    }

    private async Task<LtiProvisionResult> ExistingAsync(LtiRegistration reg, Guid userId, string ltiRole, CancellationToken ct)
    {
        var account = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (account is null || !account.IsActive)
            return LtiProvisionResult.Fail("Your Gadfly account has been turned off. Ask your instructor to contact the organization's Gadfly admin.");
        var member = await db.OrganizationMembers.AsNoTracking().FirstOrDefaultAsync(m => m.OrganizationId == reg.OrganizationId && m.UserId == userId, ct);
        if (member is null)
        {
            await using var seats = await SeatLock.AcquireAsync(db, reg.OrganizationId, ct);
            if (!seats.Acquired) return LtiProvisionResult.Fail(SeatLock.BusyMessage);
            var (seatOk, seatMessage) = await plans.CheckSeatAsync(db, reg.OrganizationId, ltiRole, ct);
            if (!seatOk) return LtiProvisionResult.Fail(seatMessage!);
            var added = await orgs.AddMemberAsync(reg.OrganizationId, userId, ltiRole, actingRole: null, actorUserId: null, ct);
            return added.Ok ? new(userId, null, Role: ltiRole) : LtiProvisionResult.Fail($"Gadfly could not add you to the organization ({added.Message}).");
        }
        if (!member.IsActive)
            return LtiProvisionResult.Fail("Your access to this organization in Gadfly has been removed. Ask your instructor to contact the organization's Gadfly admin.");
        if (member.Role == OrgRoles.Student && ltiRole == OrgRoles.Instructor)
        {
            await using var seats = await SeatLock.AcquireAsync(db, reg.OrganizationId, ct);
            if (!seats.Acquired) return new(userId, null, Role: member.Role);   // stays a student this time; the next launch tries again
            var (seatOk, seatMessage) = await plans.CheckSeatAsync(db, reg.OrganizationId, ltiRole, ct);
            if (!seatOk) return new(userId, null, Role: member.Role);   // stays a student until a staff seat is free
            var changed = await orgs.AddMemberAsync(reg.OrganizationId, userId, ltiRole, actingRole: null, actorUserId: null, ct);
            return new(userId, null, Role: changed.Ok ? ltiRole : member.Role);
        }
        return new(userId, null, Role: member.Role);
    }

    private Task<bool> EmailVerifiedAsync(Guid userId, CancellationToken ct) =>
        db.AuthUsers.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive && u.EmailVerified && u.Email != null, ct);

    private static string ShortRole(string role) => LtiClaims.ContextRoleOf(role) ?? role[(role.LastIndexOfAny(['#', '/']) + 1)..];

    internal static string Sanitize(string s)
    {
        var cleaned = new string(s.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_').ToArray()).Trim('.', '-', '_');
        return cleaned.Length == 0 ? "lms-user" : cleaned[..Math.Min(cleaned.Length, 40)];
    }
}

/// <summary>
/// Signs an LTI-launched student in with the same cookie and claim shape a password sign-in produces (GAD-T312): user
/// id, security stamp, and every <see cref="IMaClaimsAugmentor"/> (which is how the organization memberships get
/// stamped on), then makes the launching organization the active one.
/// <para>Stated simplification: the session carries <c>amr=lti</c> and no tracked <c>ma:sid</c> session row, so it is not
/// listed under "sign out everywhere"; it still ends at the idle and absolute timeouts and whenever the account's security
/// stamp rotates (a role change, a deactivation). It never satisfies an MFA requirement, so it can't reach staff pages.</para>
/// </summary>
public static class LtiSignIn
{
    public static async Task SignInAsync(HttpContext http, GadflyDbContext db, Guid userId, Guid organizationId, TimeProvider clock, CancellationToken ct)
    {
        var user = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new InvalidOperationException("The account no longer exists or is turned off.");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(MaClaims.UserId, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.Role),
            new(MaClaims.SecurityStamp, user.SecurityStamp),
            new(MaClaims.Amr, "lti"),
        };
        var identity = new ClaimsIdentity(claims, MaSchemes.Cookie);
        foreach (var augmentor in http.RequestServices.GetServices<IMaClaimsAugmentor>())
            await augmentor.AugmentAsync(identity, http.RequestServices, ct);
        var now = clock.GetUtcNow();
        var session = http.RequestServices.GetRequiredService<IOptions<AuthSessionOptions>>().Value;
        var props = new AuthenticationProperties { IsPersistent = false, ExpiresUtc = now + session.AbsoluteTimeout };
        props.Items["la"] = now.ToString("O");
        props.Items["sc"] = now.ToString("O");
        await http.SignInAsync(MaSchemes.Cookie, new ClaimsPrincipal(identity), props);
        http.Response.Cookies.Append(ITenantContext.ActiveOrgCookie, organizationId.ToString("N"), new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromDays(30),
        });
    }
}
