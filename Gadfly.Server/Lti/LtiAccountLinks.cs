using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Notifications;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Lti;

/// <summary>One LMS identity waiting to be linked, as the LMS connection page lists it.</summary>
public sealed record LtiLinkRequestRow(Guid Id, string Connection, string LmsName, string? LmsEmail, string Role, string Source,
    Guid CandidateUserId, string CandidateName, string CandidateUserName, string? CandidateEmail, string CandidateRole,
    bool EmailSent, DateTimeOffset CreatedAt);

/// <summary>
/// Linking an LMS identity to a Gadfly account the person already has (GAD-T458). A launch or roster row whose LMS email
/// matches exactly one active member of the organization is not given a second account, and is not linked either: Gadfly
/// never links by a matching email alone, because an LMS can send any address. It records an <see cref="LtiLinkRequest"/>
/// and waits for one of two confirmations:
/// <list type="bullet">
/// <item>an organization admin confirms it on <c>/org/lti</c> (or keeps the two apart, which gives the person a new account);</item>
/// <item>the person proves they own the existing account's <b>verified</b> email: Gadfly sends a single-use link to that
/// address, and the link only works in the same browser that asked for it (a cookie whose hash is on the request), so
/// someone who merely knows the address can't have its owner's account linked to their LMS sign-in by phishing the owner
/// into clicking.</item>
/// </list>
/// Every step is audited. Runs in a scope pinned to the request's organization, except <see cref="ConfirmByEmailAsync"/>,
/// which the anonymous link page calls in a system scope and which then acts only on the one request the token names.
/// </summary>
public sealed class LtiAccountLinks(GadflyDbContext db, TimeProvider clock, ILogger<LtiAccountLinks> logger)
{
    /// <summary>How long an emailed link works.</summary>
    public static readonly TimeSpan EmailLinkLifetime = TimeSpan.FromHours(24);
    /// <summary>The least time between two emails for the same request.</summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromMinutes(2);
    /// <summary>The cookie (per request) that binds an emailed link to the browser that asked for it.</summary>
    public static string BrowserCookieName(Guid requestId) => "gadfly-lti-link-" + requestId.ToString("N")[..12];

    public sealed record Candidate(Guid UserId, bool EmailVerified);

    /// <summary>
    /// The one active member of the organization whose account email equals <paramref name="lmsEmail"/>, or null when none or
    /// more than one does, or when that account is already the one another LMS identity on this connection reaches (a
    /// different person sharing the address: never offered).
    /// </summary>
    public async Task<Candidate?> CandidateAsync(LtiRegistration reg, string? lmsEmail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(lmsEmail) || !Tenancy.OrganizationDirectory.IsValidEmail(lmsEmail.Trim())) return null;
        var normalized = IUserStore.Normalize(lmsEmail.Trim());
        var found = await (from u in db.AuthUsers.AsNoTracking()
                           join m in db.OrganizationMembers.AsNoTracking() on u.Id equals m.UserId
                           where u.NormalizedEmail == normalized && u.IsActive && m.OrganizationId == reg.OrganizationId && m.IsActive
                           select new { u.Id, u.EmailVerified }).Distinct().Take(2).ToListAsync(ct);
        if (found.Count != 1) return null;
        if (await db.LtiUserMappings.AsNoTracking().AnyAsync(m => m.RegistrationId == reg.Id && m.GadflyUserId == found[0].Id && m.OrganizationId == reg.OrganizationId, ct))
            return null;
        return new Candidate(found[0].Id, found[0].EmailVerified);
    }

    /// <summary>Records (or returns the existing) pending request for this LMS identity. Saves.</summary>
    public async Task<LtiLinkRequest> RequestAsync(LtiRegistration reg, LtiLaunchClaims claims, string role, Candidate candidate, string source, CancellationToken ct)
    {
        var existing = await db.LtiLinkRequests.FirstOrDefaultAsync(r => r.RegistrationId == reg.Id && r.LtiSubject == claims.Subject && r.OrganizationId == reg.OrganizationId, ct);
        if (existing is not null) return existing;
        var now = clock.GetUtcNow();
        var request = new LtiLinkRequest
        {
            OrganizationId = reg.OrganizationId, RegistrationId = reg.Id, LtiSubject = Cap(claims.Subject, 255),
            LmsEmail = claims.Email is { } e ? Cap(e.Trim(), 320) : null, LmsName = Cap(claims.DisplayName, 200), Role = role,
            CandidateUserId = candidate.UserId, Source = source, Status = LtiLinkRequest.Pending, CreatedAt = now,
        };
        db.LtiLinkRequests.Add(request);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = reg.OrganizationId, ActorRole = "LTI", Action = "lti.link-requested", EntityType = "User", EntityId = candidate.UserId.ToString(),
            Reason = $"The LMS connection '{reg.Name}' sent the email address of this existing member ({source}); waiting for an admin or the person to confirm before linking.",
            AfterJson = JsonSerializer.Serialize(new { request = request.Id, connection = reg.Name, role }), At = now,
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();   // the same person's two first launches at once: use the row the other one made
            return await db.LtiLinkRequests.FirstAsync(r => r.RegistrationId == reg.Id && r.LtiSubject == claims.Subject, ct);
        }
        return request;
    }

    public sealed record EmailOffer(bool Ok, string Message, string? BrowserSecret = null);

    /// <summary>
    /// Emails a single-use link to the existing account's verified address. Returns the browser secret the caller sets as a
    /// cookie (<see cref="BrowserCookieName"/>); the link works only where that cookie is. Refused when the account's email isn't
    /// verified (then only an admin can link), or when one was sent moments ago.
    /// </summary>
    public async Task<EmailOffer> SendEmailAsync(Guid organizationId, Guid requestId, string baseUrl, CancellationToken ct)
    {
        var request = await db.LtiLinkRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.OrganizationId == organizationId, ct);
        if (request is null || request.Status != LtiLinkRequest.Pending)
            return new(false, "This request has already been settled. Go back to your course and open the assignment again.");
        var account = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.CandidateUserId && u.IsActive, ct);
        if (account is null || !account.EmailVerified || string.IsNullOrWhiteSpace(account.Email))
            return new(false, "Your Gadfly account's email address hasn't been verified, so Gadfly can't use it to confirm it's you. Ask the organization's Gadfly admin to connect your LMS sign-in to your account.");
        var now = clock.GetUtcNow();
        if (request.EmailSentAt is { } sent && now - sent < ResendAfter)
            return new(false, $"A link was emailed to {Mask(account.Email)} a moment ago. Check that inbox (and its spam folder); you can ask for another in a couple of minutes.");

        var token = LtiLaunchService.RandomToken(40);
        var browser = LtiLaunchService.RandomToken(32);
        request.EmailTokenHash = Hash(token);
        request.BrowserTokenHash = Hash(browser);
        request.EmailSentAt = now;
        request.EmailTokenExpiresAt = now + EmailLinkLifetime;

        var orgName = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "your organization";
        var data = new EmailData(orgName, account.DisplayName ?? account.UserName, ActionUrl: $"{baseUrl.TrimEnd('/')}{LtiEndpoints.LinkPath}?t={Uri.EscapeDataString(token)}",
            ActionLabel: "Connect my LMS sign-in");
        // Straight onto the queue, like a test send: a confirmation the person asked for is not a notification the
        // organization's email switch governs.
        var log = new EmailLog { OrganizationId = organizationId, Kind = EmailKinds.LmsLink, ToUserId = account.Id, RelatedId = request.Id, Status = EmailLogStatus.Queued, CreatedAt = now };
        db.Set<EmailLog>().Add(log);
        db.Jobs.Add(new Job
        {
            OrganizationId = organizationId, Kind = EmailDeliveryJob.JobKind, State = JobState.Queued, MaxAttempts = 5, RunAfter = now, CreatedAt = now,
            PayloadJson = JsonSerializer.Serialize(new EmailJobPayload(log.Id, EmailKinds.LmsLink, organizationId, account.Id, null, JsonSerializer.Serialize(data))),
        });
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId, ActorRole = "LTI", Action = "lti.link-email-sent", EntityType = "User", EntityId = account.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { request = request.Id }), At = now,
        });
        await db.SaveChangesAsync(ct);
        return new(true, $"We emailed a link to {Mask(account.Email)}. Open it in this browser within 24 hours to connect your LMS sign-in to that Gadfly account, then open the assignment from your course again.", browser);
    }

    public sealed record Confirmation(bool Ok, string Message, Guid? OrganizationId = null);

    /// <summary>The pending request a still-valid emailed token names, or null. For the confirm page's GET (which changes nothing).</summary>
    public async Task<LtiLinkRequest?> FindByTokenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = Hash(token);
        var now = clock.GetUtcNow();
        return await db.LtiLinkRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmailTokenHash == hash && r.Status == LtiLinkRequest.Pending && r.EmailTokenExpiresAt >= now, ct);
    }

    /// <summary>Links the LMS identity to the account, when the token is valid and <paramref name="browserSecret"/> is the one
    /// set in the browser that asked for the email. Single use.</summary>
    public async Task<Confirmation> ConfirmByEmailAsync(string? token, string? browserSecret, CancellationToken ct)
    {
        var found = await FindByTokenAsync(token, ct);
        if (found is null) return new(false, "This link has expired or was already used. Open the assignment from your course again to get a new one.");
        if (string.IsNullOrEmpty(browserSecret) || found.BrowserTokenHash is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(browserSecret)), Encoding.ASCII.GetBytes(found.BrowserTokenHash)))
            return new(false, "Open this link in the same browser where you asked for it (the one you opened the assignment in). For your safety, it doesn't work anywhere else.");
        return await LinkAsync(found.OrganizationId, found.Id, null, "the person confirmed by email", ct);
    }

    /// <summary>An admin's (or the emailed link's) decision to link. <paramref name="actorUserId"/> is null for the emailed link.</summary>
    public async Task<Confirmation> LinkAsync(Guid organizationId, Guid requestId, Guid? actorUserId, string how, CancellationToken ct)
    {
        var request = await db.LtiLinkRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.OrganizationId == organizationId, ct);
        if (request is null || request.Status != LtiLinkRequest.Pending) return new(false, "This request has already been settled.");
        var account = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.CandidateUserId && u.IsActive, ct);
        var member = await db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == organizationId && m.UserId == request.CandidateUserId && m.IsActive, ct);
        if (account is null || !member) return new(false, "That Gadfly account is no longer an active member of this organization, so it can't be linked. Keep them separate instead.");

        var now = clock.GetUtcNow();
        // Single use, across instances: exactly one request flips Pending to Linked.
        var claimed = await db.LtiLinkRequests.Where(r => r.Id == requestId && r.OrganizationId == organizationId && r.Status == LtiLinkRequest.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, LtiLinkRequest.Linked).SetProperty(r => r.ResolvedAt, now)
                .SetProperty(r => r.ResolvedByUserId, actorUserId).SetProperty(r => r.EmailTokenHash, (string?)null).SetProperty(r => r.BrowserTokenHash, (string?)null), ct);
        if (claimed != 1) return new(false, "This request has already been settled.");

        db.LtiUserMappings.Add(new LtiUserMapping
        {
            OrganizationId = organizationId, RegistrationId = request.RegistrationId, LtiSubject = request.LtiSubject, GadflyUserId = request.CandidateUserId, CreatedAt = now,
        });
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId, ActorUserId = actorUserId, ActorRole = actorUserId is null ? "LTI" : null, Action = "lti.identity-linked",
            EntityType = "User", EntityId = request.CandidateUserId.ToString(), Reason = $"LMS sign-in linked to this account: {how}.",
            AfterJson = JsonSerializer.Serialize(new { request = request.Id, lmsName = request.LmsName, lmsEmail = request.LmsEmail }), At = now,
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e)
        {
            // The LMS identity got a mapping some other way in the meantime: leave that one alone.
            logger.LogWarning(e, "LTI link request {RequestId}: the identity was already mapped", requestId);
            return new(false, "This LMS sign-in is already connected to a Gadfly account.");
        }
        return new(true, $"Linked. {request.LmsName} now opens Gadfly from the LMS as {account.DisplayName ?? account.UserName}.", organizationId);
    }

    /// <summary>An admin's decision that they are different people: the next launch or roster sync gives the LMS identity a new account.</summary>
    public async Task<Confirmation> KeepSeparateAsync(Guid organizationId, Guid requestId, Guid actorUserId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var request = await db.LtiLinkRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId && r.OrganizationId == organizationId, ct);
        var claimed = await db.LtiLinkRequests.Where(r => r.Id == requestId && r.OrganizationId == organizationId && r.Status == LtiLinkRequest.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, LtiLinkRequest.Separate).SetProperty(r => r.ResolvedAt, now)
                .SetProperty(r => r.ResolvedByUserId, actorUserId).SetProperty(r => r.EmailTokenHash, (string?)null).SetProperty(r => r.BrowserTokenHash, (string?)null), ct);
        if (claimed != 1 || request is null) return new(false, "This request has already been settled.");
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId, ActorUserId = actorUserId, Action = "lti.identity-kept-separate", EntityType = "User", EntityId = request.CandidateUserId.ToString(),
            Reason = "An admin decided the LMS person and this account are different people; the LMS person gets their own account.",
            AfterJson = JsonSerializer.Serialize(new { request = request.Id, lmsName = request.LmsName, lmsEmail = request.LmsEmail }), At = now,
        });
        await db.SaveChangesAsync(ct);
        return new(true, $"Kept separate. {request.LmsName} gets a Gadfly account of their own the next time they open an exam or the roster is synced.", organizationId);
    }

    /// <summary>The organization's pending requests, newest first, for the LMS connection page.</summary>
    public async Task<IReadOnlyList<LtiLinkRequestRow>> PendingAsync(Guid organizationId, CancellationToken ct)
    {
        var rows = await db.LtiLinkRequests.AsNoTracking().Where(r => r.OrganizationId == organizationId && r.Status == LtiLinkRequest.Pending)
            .OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var userIds = rows.Select(r => r.CandidateUserId).Distinct().ToList();
        var users = await db.AuthUsers.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, u.DisplayName, u.Email }).ToDictionaryAsync(u => u.Id, ct);
        var roles = await db.OrganizationMembers.AsNoTracking().Where(m => m.OrganizationId == organizationId && userIds.Contains(m.UserId))
            .ToDictionaryAsync(m => m.UserId, m => m.Role, ct);
        var regIds = rows.Select(r => r.RegistrationId).Distinct().ToList();
        var regs = await db.LtiRegistrations.AsNoTracking().Where(r => r.OrganizationId == organizationId && regIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        return rows.Select(r =>
        {
            var u = users.GetValueOrDefault(r.CandidateUserId);
            return new LtiLinkRequestRow(r.Id, regs.GetValueOrDefault(r.RegistrationId, "(removed connection)"), r.LmsName, r.LmsEmail, r.Role, r.Source,
                r.CandidateUserId, u?.DisplayName ?? u?.UserName ?? "(unknown)", u?.UserName ?? "", u?.Email, roles.GetValueOrDefault(r.CandidateUserId, "(not a member)"),
                r.EmailSentAt is not null, r.CreatedAt);
        }).ToList();
    }

    /// <summary>"j•••@example.edu": enough for the person to recognize their own address, not enough to read someone else's.</summary>
    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.IndexOf('@') is var at && at < 1) return "your account's email address";
        return email[0] + new string('•', Math.Min(6, Math.Max(1, at - 1))) + email[at..];
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max];
}
