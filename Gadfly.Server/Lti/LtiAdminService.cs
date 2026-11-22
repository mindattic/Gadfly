using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Lti;

public sealed record LtiRegistrationRow(Guid Id, string Name, string Issuer, string ClientId, string DeploymentId, string AuthLoginUrl,
    string AuthTokenUrl, string KeySetUrl, string? TokenAudience, bool Enabled, DateTimeOffset CreatedAt, int Placements, int People,
    int RosterRefreshHours = LtiRegistration.DefaultRosterRefreshHours);

public sealed record LtiResourceLinkRow(Guid Id, string RegistrationName, string ContextId, string ResourceLinkId, Guid ExamId, string ExamTitle,
    bool HasLineItem, bool HasRoster, DateTimeOffset? LastRosterSyncAt, DateTimeOffset CreatedAt, int Students, int GradesSent)
{
    /// <summary>GAD-T458: grades whose send ran out of tries and wait for an admin's Retry.</summary>
    public int GradesFailed { get; init; }
    /// <summary>Grades that failed at least once and will be tried again after their backoff.</summary>
    public int GradesRetrying { get; init; }
    /// <summary>The connection's scheduled refresh interval in hours (0 = only on request).</summary>
    public int RosterRefreshHours { get; init; }
    /// <summary>When the latest roster refresh failed, and why; null when the latest one worked (or none ran).</summary>
    public string? RosterProblem { get; init; }
}

/// <summary>A grade the LMS hasn't accepted (GAD-T458): still retrying, or given up and waiting for an admin.</summary>
public sealed record LtiPassbackProblemRow(Guid AssignmentId, Guid ResourceLinkId, string StudentName, string ExamTitle, string Course,
    decimal? Percent, int Attempts, string? Error, DateTimeOffset? RetryAt, DateTimeOffset? FailedAt)
{
    public bool GaveUp => FailedAt is not null;
}

public sealed record LtiActionResult(bool Ok, string Message, Guid? Id = null);

/// <summary>What an admin types in for one platform connection (the values the platform's own registration screen shows).</summary>
public sealed class LtiRegistrationInput
{
    public string Name { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string DeploymentId { get; set; } = "";
    public string AuthLoginUrl { get; set; } = "";
    public string AuthTokenUrl { get; set; } = "";
    public string KeySetUrl { get; set; } = "";
    public string? TokenAudience { get; set; }
    /// <summary>GAD-T458: hours between scheduled roster refreshes; 0 refreshes only when an admin asks.</summary>
    public int RosterRefreshHours { get; set; } = LtiRegistration.DefaultRosterRefreshHours;
}

/// <summary>
/// The LMS connection page's service (GAD-T312/GAD-T384): owners and admins register a platform connection, correct its
/// addresses, turn it off or remove it, see the placements deep linking created, and sync a placement's roster. Owner
/// and Admin only, like every other organization-wide integration (API keys, webhooks); every change is audited.
/// </summary>
public sealed class LtiAdminService(GadflyDbContext db, ITenantContext tenant, LtiRosterService roster, LtiGradePassbackService passback,
    LtiAccountLinks links, TimeProvider clock)
{
    private AdminGate Gate => field ??= new AdminGate(db, tenant, clock);

    public async Task<IReadOnlyList<LtiRegistrationRow>> ListAsync(CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var org = Gate.Org;
        var regs = await db.LtiRegistrations.AsNoTracking().Where(r => r.OrganizationId == org).OrderBy(r => r.Name).ToListAsync(ct);
        var placements = await db.LtiResourceLinks.AsNoTracking().Where(l => l.OrganizationId == org)
            .GroupBy(l => l.RegistrationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var people = await db.LtiUserMappings.AsNoTracking().Where(m => m.OrganizationId == org)
            .GroupBy(m => m.RegistrationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return regs.Select(r => new LtiRegistrationRow(r.Id, r.Name, r.Issuer, r.ClientId, r.DeploymentId, r.AuthLoginUrl, r.AuthTokenUrl, r.KeySetUrl,
            r.TokenAudience, r.Enabled, r.CreatedAt, placements.GetValueOrDefault(r.Id), people.GetValueOrDefault(r.Id), r.RosterRefreshHours)).ToList();
    }

    public async Task<IReadOnlyList<LtiResourceLinkRow>> PlacementsAsync(CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var org = Gate.Org;
        var links = await db.LtiResourceLinks.AsNoTracking().Where(l => l.OrganizationId == org).OrderByDescending(l => l.CreatedAt).Take(500).ToListAsync(ct);
        var regs = await db.LtiRegistrations.AsNoTracking().Where(r => r.OrganizationId == org)
            .ToDictionaryAsync(r => r.Id, r => new { r.Name, r.RosterRefreshHours }, ct);
        var examIds = links.Select(l => l.ExamId).Distinct().ToList();
        var examTitles = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && examIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Title, ct);
        var counts = await db.LtiAssignmentLinks.AsNoTracking().Where(a => a.OrganizationId == org)
            .GroupBy(a => a.ResourceLinkId)
            .Select(g => new
            {
                g.Key, Students = g.Count(), Sent = g.Count(a => a.PassbackSentAt != null),
                Failed = g.Count(a => a.PassbackFailedAt != null), Retrying = g.Count(a => a.PassbackFailedAt == null && a.PassbackAttempts > 0),
            })
            .ToDictionaryAsync(x => x.Key, ct);
        // The latest roster outcome per placement (GAD-T458): a scheduled refresh that failed is audited with its reason.
        var linkKeys = links.Select(l => l.Id.ToString()).ToList();
        var rosterEvents = await db.AuditEvents.AsNoTracking()
            .Where(a => a.OrganizationId == org && a.EntityType == "LtiResourceLink" && a.EntityId != null && linkKeys.Contains(a.EntityId)
                        && (a.Action == "lti.roster-synced" || a.Action == LtiRosterRefreshJob.FailedAction))
            .Select(a => new { a.EntityId, a.Action, a.Reason, a.At }).ToListAsync(ct);
        var latestRoster = rosterEvents.GroupBy(a => a.EntityId!).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.At).First());
        return links.Select(l =>
        {
            var reg = regs.GetValueOrDefault(l.RegistrationId);
            counts.TryGetValue(l.Id, out var c);
            var roster = latestRoster.GetValueOrDefault(l.Id.ToString());
            return new LtiResourceLinkRow(l.Id, reg?.Name ?? "(removed connection)", l.ContextId, l.ResourceLinkId,
                l.ExamId, examTitles.GetValueOrDefault(l.ExamId, "(exam no longer available)"), l.LineItemUrl is { Length: > 0 } || l.LineItemsUrl is { Length: > 0 },
                l.ContextMembershipsUrl is { Length: > 0 }, l.LastRosterSyncAt, l.CreatedAt, c?.Students ?? 0, c?.Sent ?? 0)
            {
                GradesFailed = c?.Failed ?? 0,
                GradesRetrying = c?.Retrying ?? 0,
                RosterRefreshHours = reg?.RosterRefreshHours ?? 0,
                RosterProblem = roster?.Action == LtiRosterRefreshJob.FailedAction ? $"refresh failed {roster.At.UtcDateTime:yyyy-MM-dd HH:mm} UTC: {roster.Reason}" : null,
            };
        }).ToList();
    }

    /// <summary>The reason <paramref name="input"/> can't be saved, or null. Trims it in place.</summary>
    public static string? Problem(LtiRegistrationInput input)
    {
        input.Name = (input.Name ?? "").Trim();
        input.Issuer = (input.Issuer ?? "").Trim();
        input.ClientId = (input.ClientId ?? "").Trim();
        input.DeploymentId = (input.DeploymentId ?? "").Trim();
        input.AuthLoginUrl = (input.AuthLoginUrl ?? "").Trim();
        input.AuthTokenUrl = (input.AuthTokenUrl ?? "").Trim();
        input.KeySetUrl = (input.KeySetUrl ?? "").Trim();
        input.TokenAudience = string.IsNullOrWhiteSpace(input.TokenAudience) ? null : input.TokenAudience.Trim();
        if (input.Name.Length is < 1 or > 200) return "Give this connection a name (1 to 200 characters), for example \"Canvas - spring term\".";
        if (input.Issuer.Length is 0 or > 500) return "The issuer is required (up to 500 characters). Your LMS's registration screen shows it; the guide below says where.";
        if (input.ClientId.Length is 0 or > 200) return "The client id is required (up to 200 characters). Your LMS gives it to you when you register Gadfly.";
        if (input.DeploymentId.Length is 0 or > 200) return "The deployment id is required (up to 200 characters). Your LMS gives it to you when you install or deploy Gadfly.";
        foreach (var (label, url) in new[] { ("OIDC login (authorization) URL", input.AuthLoginUrl), ("access token URL", input.AuthTokenUrl), ("key set (JWKS) URL", input.KeySetUrl) })
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || url.Length > 1000)
                return $"The {label} must be a full https:// address (up to 1000 characters).";
            if (Platform.WebhookTargets.Problem(u) is not null) return $"The {label} must point at a public host, not a private or local address.";
        }
        if (input.TokenAudience is { Length: > 1000 }) return "The token audience can be at most 1000 characters.";
        if (input.RosterRefreshHours is < 0 or > MaxRosterRefreshHours)
            return $"Refresh rosters every 1 to {MaxRosterRefreshHours} hours, or 0 to refresh them only when you press Sync roster.";
        return null;
    }

    /// <summary>The longest scheduled roster refresh interval (30 days).</summary>
    public const int MaxRosterRefreshHours = 720;

    public async Task<LtiActionResult> CreateAsync(LtiRegistrationInput input, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Connecting an LMS", "LtiRegistration", null, ct)) return new(false, AdminGate.DeniedMessage);
        var org = Gate.Org;
        if (Problem(input) is { } problem) return new(false, problem);
        // The (issuer, client id, deployment id) triple names one connection across every organization: a launch has to resolve
        // to exactly one organization. The message doesn't say whose it is.
        if (await db.LtiRegistrations.IgnoreQueryFilters().AnyAsync(r => r.Issuer == input.Issuer && r.ClientId == input.ClientId && r.DeploymentId == input.DeploymentId, ct))
            return new(false, "A connection with this issuer, client id and deployment id already exists in Gadfly. If it isn't one of yours, check you copied the deployment id for your own installation.");

        var row = new LtiRegistration
        {
            OrganizationId = org, Name = input.Name, Issuer = input.Issuer, ClientId = input.ClientId, DeploymentId = input.DeploymentId,
            AuthLoginUrl = input.AuthLoginUrl, AuthTokenUrl = input.AuthTokenUrl, KeySetUrl = input.KeySetUrl, TokenAudience = input.TokenAudience,
            RosterRefreshHours = input.RosterRefreshHours, Enabled = true, CreatedByUserId = Gate.User, CreatedAt = clock.GetUtcNow(),
        };
        db.LtiRegistrations.Add(row);
        Gate.Audit("lti.registration-created", "LtiRegistration", row.Id.ToString(), after: Snapshot(row));
        await db.SaveChangesAsync(ct);
        return new(true, $"{input.Name} is connected. If you haven't yet, give your LMS Gadfly's addresses (step 1 of the guide), then add a Gadfly exam to a course.", row.Id);
    }

    /// <summary>Corrects a connection's name and platform addresses. The issuer, client id and deployment id are its identity:
    /// to change those, add a new connection (existing placements and accounts stay with the old one).</summary>
    public async Task<LtiActionResult> UpdateAsync(Guid id, LtiRegistrationInput input, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Changing an LMS connection", "LtiRegistration", id.ToString(), ct)) return new(false, AdminGate.DeniedMessage);
        var row = await db.LtiRegistrations.FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == Gate.Org, ct);
        if (row is null) return new(false, "That connection no longer exists.");
        input.Issuer = row.Issuer; input.ClientId = row.ClientId; input.DeploymentId = row.DeploymentId;
        if (Problem(input) is { } problem) return new(false, problem);
        var before = Snapshot(row);
        row.Name = input.Name; row.AuthLoginUrl = input.AuthLoginUrl; row.AuthTokenUrl = input.AuthTokenUrl; row.KeySetUrl = input.KeySetUrl; row.TokenAudience = input.TokenAudience;
        row.RosterRefreshHours = input.RosterRefreshHours;
        Gate.Audit("lti.registration-changed", "LtiRegistration", id.ToString(), before: before, after: Snapshot(row));
        await db.SaveChangesAsync(ct);
        return new(true, "Saved.", id);
    }

    public async Task<LtiActionResult> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Changing an LMS connection", "LtiRegistration", id.ToString(), ct)) return new(false, AdminGate.DeniedMessage);
        var row = await db.LtiRegistrations.FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == Gate.Org, ct);
        if (row is null) return new(false, "That connection no longer exists.");
        if (row.Enabled == enabled) return new(true, enabled ? "It is already on." : "It is already off.");
        row.Enabled = enabled;
        Gate.Audit(enabled ? "lti.registration-enabled" : "lti.registration-disabled", "LtiRegistration", id.ToString());
        await db.SaveChangesAsync(ct);
        return new(true, enabled
            ? "Turned on. Launches from this LMS work again, and released grades it is owed will be sent within a few minutes."
            : "Turned off. Placements and accounts stay, but nobody can launch through this connection and no grades are sent until it is turned back on.");
    }

    public async Task<LtiActionResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Removing an LMS connection", "LtiRegistration", id.ToString(), ct)) return new(false, AdminGate.DeniedMessage);
        var org = Gate.Org;
        var row = await db.LtiRegistrations.FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == org, ct);
        if (row is null) return new(false, "That connection no longer exists.");
        if (await db.LtiResourceLinks.AnyAsync(l => l.RegistrationId == id && l.OrganizationId == org, ct)
            || await db.LtiUserMappings.AnyAsync(m => m.RegistrationId == id && m.OrganizationId == org, ct))
            return new(false, "This connection has already been used (courses added Gadfly exams through it, or people launched through it), so it is kept for the record. Turn it off instead.");
        db.LtiRegistrations.Remove(row);
        Gate.Audit("lti.registration-removed", "LtiRegistration", id.ToString(), before: Snapshot(row));
        await db.SaveChangesAsync(ct);
        return new(true, "Removed.");
    }

    public async Task<LtiRosterService.SyncResult> SyncRosterAsync(Guid resourceLinkId, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Syncing a course roster from the LMS", "LtiResourceLink", resourceLinkId.ToString(), ct))
            return new(false, AdminGate.DeniedMessage);
        return await roster.SyncAsync(Gate.Org, resourceLinkId, Gate.User, ct);
    }

    // ================================================================== grade passback problems (GAD-T458)

    /// <summary>Grades the LMS hasn't accepted yet: those still retrying and those that ran out of tries, newest problem first.</summary>
    public async Task<IReadOnlyList<LtiPassbackProblemRow>> PassbackProblemsAsync(CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var org = Gate.Org;
        var rows = await (from a in db.LtiAssignmentLinks.AsNoTracking()
                          join x in db.ExamAssignments.AsNoTracking() on a.AssignmentId equals x.Id
                          join r in db.LtiResourceLinks.AsNoTracking() on a.ResourceLinkId equals r.Id
                          where a.OrganizationId == org && x.OrganizationId == org && r.OrganizationId == org
                                && (a.PassbackFailedAt != null || a.PassbackAttempts > 0)
                          orderby a.PassbackFailedAt descending, a.PassbackRetryAt
                          select new { a.AssignmentId, a.ResourceLinkId, x.StudentUserId, r.ExamId, r.ContextId, a.PassbackPendingPercent, a.PassbackAttempts,
                              a.PassbackError, a.PassbackRetryAt, a.PassbackFailedAt })
            .Take(500).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var userIds = rows.Select(r => r.StudentUserId).Distinct().ToList();
        var names = await db.AuthUsers.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.UserName, ct);
        var examIds = rows.Select(r => r.ExamId).Distinct().ToList();
        var titles = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && examIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Title, ct);
        return rows.Select(r => new LtiPassbackProblemRow(r.AssignmentId, r.ResourceLinkId, names.GetValueOrDefault(r.StudentUserId, "(unknown student)"),
            titles.GetValueOrDefault(r.ExamId, "(exam no longer available)"), r.ContextId, r.PassbackPendingPercent, r.PassbackAttempts, r.PassbackError,
            r.PassbackRetryAt, r.PassbackFailedAt)).ToList();
    }

    /// <summary>
    /// Starts a grade's tries again now (one assignment, or every problem grade of one placement when <paramref name="assignmentId"/>
    /// is null): clears the failed state and backoff, audits the request, and sends straight away.
    /// </summary>
    public async Task<LtiActionResult> RetryPassbackAsync(Guid? assignmentId, Guid? resourceLinkId, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Retrying a grade sent to the LMS", "LtiAssignmentLink", (assignmentId ?? resourceLinkId)?.ToString(), ct))
            return new(false, AdminGate.DeniedMessage);
        var org = Gate.Org;
        var targets = await db.LtiAssignmentLinks
            .Where(a => a.OrganizationId == org && (a.PassbackFailedAt != null || a.PassbackAttempts > 0)
                        && (assignmentId == null || a.AssignmentId == assignmentId) && (resourceLinkId == null || a.ResourceLinkId == resourceLinkId))
            .ToListAsync(ct);
        if (targets.Count == 0) return new(true, "There is nothing to retry: those grades have already gone through.");
        foreach (var t in targets)
        {
            t.PassbackAttempts = 0;
            t.PassbackFailedAt = null;
            t.PassbackRetryAt = null;
        }
        Gate.Audit("lti.grade-retry", "LtiAssignmentLink", (assignmentId ?? resourceLinkId)?.ToString(), after: new { grades = targets.Count });
        await db.SaveChangesAsync(ct);

        int sent = 0, failed = 0;
        foreach (var t in targets)
        {
            var outcome = await passback.SendIfDueAsync(org, t.AssignmentId, ct);
            if (outcome == LtiGradePassbackService.Outcome.Sent) sent++;
            else if (outcome is LtiGradePassbackService.Outcome.Failed or LtiGradePassbackService.Outcome.GaveUp) failed++;
        }
        return failed == 0
            ? new(true, sent == 0 ? "Retry started. Nothing needed sending right now (the connection may be off, or the result is no longer released)."
                : $"Sent {sent} grade{(sent == 1 ? "" : "s")} to the LMS.")
            : new(false, $"Sent {sent}; {failed} still failed and will be tried again automatically (see the reason in the table). Check the connection's token URL and that the LMS still grants the gradebook services.");
    }

    // ================================================================== linking existing accounts (GAD-T458)

    public async Task<IReadOnlyList<LtiLinkRequestRow>> LinkRequestsAsync(CancellationToken ct = default)
    {
        Gate.EnsureReader();
        return await links.PendingAsync(Gate.Org, ct);
    }

    /// <summary>Confirms that the LMS person and the existing account are the same person, and links them.</summary>
    public async Task<LtiActionResult> LinkAsync(Guid requestId, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Linking an LMS sign-in to an existing account", "LtiLinkRequest", requestId.ToString(), ct))
            return new(false, AdminGate.DeniedMessage);
        var r = await links.LinkAsync(Gate.Org, requestId, Gate.User, "confirmed by an admin", ct);
        return new(r.Ok, r.Message);
    }

    /// <summary>Records that they are different people: the LMS person gets their own account next time.</summary>
    public async Task<LtiActionResult> KeepSeparateAsync(Guid requestId, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Keeping an LMS sign-in separate from an existing account", "LtiLinkRequest", requestId.ToString(), ct))
            return new(false, AdminGate.DeniedMessage);
        var r = await links.KeepSeparateAsync(Gate.Org, requestId, Gate.User, ct);
        return new(r.Ok, r.Message);
    }

    private static object Snapshot(LtiRegistration r) => new { r.Name, r.Issuer, r.ClientId, r.DeploymentId, r.AuthLoginUrl, r.AuthTokenUrl, r.KeySetUrl, r.TokenAudience, r.RosterRefreshHours };
}
