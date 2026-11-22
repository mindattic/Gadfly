using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Lti;

public sealed record LtiRegistrationRow(Guid Id, string Name, string Issuer, string ClientId, string DeploymentId, string AuthLoginUrl,
    string AuthTokenUrl, string KeySetUrl, string? TokenAudience, bool Enabled, DateTimeOffset CreatedAt, int Placements, int People);

public sealed record LtiResourceLinkRow(Guid Id, string RegistrationName, string ContextId, string ResourceLinkId, Guid ExamId, string ExamTitle,
    bool HasLineItem, bool HasRoster, DateTimeOffset? LastRosterSyncAt, DateTimeOffset CreatedAt, int Students, int GradesSent);

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
}

/// <summary>
/// The LMS connection page's service (GAD-T312/GAD-T384): owners and admins register a platform connection, correct its
/// addresses, turn it off or remove it, see the placements deep linking created, and sync a placement's roster. Owner
/// and Admin only, like every other organization-wide integration (API keys, webhooks); every change is audited.
/// </summary>
public sealed class LtiAdminService(GadflyDbContext db, ITenantContext tenant, LtiRosterService roster, TimeProvider clock)
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
            r.TokenAudience, r.Enabled, r.CreatedAt, placements.GetValueOrDefault(r.Id), people.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<IReadOnlyList<LtiResourceLinkRow>> PlacementsAsync(CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var org = Gate.Org;
        var links = await db.LtiResourceLinks.AsNoTracking().Where(l => l.OrganizationId == org).OrderByDescending(l => l.CreatedAt).Take(500).ToListAsync(ct);
        var regNames = await db.LtiRegistrations.AsNoTracking().Where(r => r.OrganizationId == org).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var examIds = links.Select(l => l.ExamId).Distinct().ToList();
        var examTitles = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && examIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Title, ct);
        var counts = await db.LtiAssignmentLinks.AsNoTracking().Where(a => a.OrganizationId == org)
            .GroupBy(a => a.ResourceLinkId)
            .Select(g => new { g.Key, Students = g.Count(), Sent = g.Count(a => a.PassbackSentAt != null) })
            .ToDictionaryAsync(x => x.Key, ct);
        return links.Select(l => new LtiResourceLinkRow(l.Id, regNames.GetValueOrDefault(l.RegistrationId, "(removed connection)"), l.ContextId, l.ResourceLinkId,
            l.ExamId, examTitles.GetValueOrDefault(l.ExamId, "(exam no longer available)"), l.LineItemUrl is { Length: > 0 } || l.LineItemsUrl is { Length: > 0 },
            l.ContextMembershipsUrl is { Length: > 0 }, l.LastRosterSyncAt, l.CreatedAt,
            counts.TryGetValue(l.Id, out var c) ? c.Students : 0, counts.TryGetValue(l.Id, out var c2) ? c2.Sent : 0)).ToList();
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
        return null;
    }

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
            Enabled = true, CreatedByUserId = Gate.User, CreatedAt = clock.GetUtcNow(),
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

    private static object Snapshot(LtiRegistration r) => new { r.Name, r.Issuer, r.ClientId, r.DeploymentId, r.AuthLoginUrl, r.AuthTokenUrl, r.KeySetUrl, r.TokenAudience };
}
