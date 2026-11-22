using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Lti;

/// <summary>
/// Names and Roles Provisioning Service 2.0 (GAD-T312/GAD-T382): pulls a course's roster from the platform and resolves
/// every active member exactly the way a launch would (<see cref="LtiUserProvisioner"/>), so a student who never
/// personally clicks into Gadfly is still a recognized organization member, with the right role, once someone asks
/// for a sync. Follows the platform's <c>Link: rel="next"</c> paging. Members whose status is Inactive or Deleted are
/// skipped (never removed from Gadfly automatically: removal stays an admin's decision).
/// <para><b>Stated simplification</b>: sync is on demand (a button per placement on the LMS connection page,
/// GAD-T384), not a scheduled refresh; a student's own first launch provisions them immediately regardless.</para>
/// </summary>
public sealed partial class LtiRosterService(GadflyDbContext db, LtiTokenClient tokens, IHttpClientFactory http, LtiUserProvisioner provisioner,
    TimeProvider clock, ILogger<LtiRosterService> logger)
{
    private const int MaxPages = 50;

    private sealed record Member(string? user_id, string[]? roles, string? status, string? name, string? given_name, string? family_name, string? email);
    private sealed record MembershipContainer(Member[]? members);

    public sealed record SyncResult(bool Ok, string Message, int Found = 0, int Provisioned = 0, int Skipped = 0);

    /// <summary>Syncs one placement's course roster. The caller (the admin page) has already checked the caller may.</summary>
    public async Task<SyncResult> SyncAsync(Guid organizationId, Guid resourceLinkId, Guid? actorUserId, CancellationToken ct)
    {
        var link = await db.LtiResourceLinks.FirstOrDefaultAsync(r => r.Id == resourceLinkId && r.OrganizationId == organizationId, ct);
        if (link is null) return new(false, "That placement no longer exists.");
        if (link.ContextMembershipsUrl is not { Length: > 0 } url)
            return new(false, "Gadfly doesn't know this course's roster address yet. It learns it from a launch: open the exam from the course once (as anyone), then sync. If it still doesn't appear, the LMS hasn't granted Gadfly the Names and Roles service.");
        var reg = await db.LtiRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == link.RegistrationId && r.OrganizationId == organizationId, ct);
        if (reg is null || !reg.Enabled) return new(false, "This LMS connection no longer exists or is turned off.");

        string token;
        try { token = await tokens.AccessTokenAsync(reg, LtiClaims.ScopeNrps, ct); }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(e, "LTI roster: token request failed for registration {RegistrationId}", reg.Id);
            return new(false, $"The LMS did not give Gadfly permission to read the roster: {e.Message}");
        }

        var members = new List<Member>();
        string? next = url;
        for (var page = 0; next is not null && page < MaxPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.ims.lti-nrps.v2.membershipcontainer+json"));
            HttpResponseMessage response;
            try { response = await http.CreateClient(LtiSetup.HttpClientName).SendAsync(request, ct); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                return new(false, $"Gadfly could not reach the LMS roster service: {e.Message}");
            }
            using (response)
            {
                if (!response.IsSuccessStatusCode) return new(false, $"The LMS refused the roster request ({(int)response.StatusCode}).");
                MembershipContainer? parsed;
                try { parsed = JsonSerializer.Deserialize<MembershipContainer>(await response.Content.ReadAsStringAsync(ct)); }
                catch (JsonException) { return new(false, "The LMS's roster was not in the expected format."); }
                members.AddRange(parsed?.members ?? []);
                next = NextLink(response.Headers.TryGetValues("Link", out var values) ? values : []);
            }
        }

        int provisioned = 0, skipped = 0;
        var problems = new HashSet<string>();
        foreach (var m in members)
        {
            if (m.user_id is not { Length: > 0 } subject || m.status is "Inactive" or "Deleted") { skipped++; continue; }
            var claims = new LtiLaunchClaims(subject, m.email, m.given_name, m.family_name, m.name, m.roles ?? [],
                link.ContextId, null, null, link.ResourceLinkId, null, null, null, null, null, null);
            var result = await provisioner.ProvisionAsync(reg, claims, ct);
            if (result.Ok) provisioned++;
            else { skipped++; if (problems.Count < 3) problems.Add(result.Error!); }
        }
        link.LastRosterSyncAt = clock.GetUtcNow();
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId, ActorUserId = actorUserId, ActorRole = actorUserId is null ? "LTI" : null, Action = "lti.roster-synced",
            EntityType = "LtiResourceLink", EntityId = link.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { found = members.Count, provisioned, skipped }), At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);

        var message = $"Read {members.Count} roster member{(members.Count == 1 ? "" : "s")}: {provisioned} matched to Gadfly accounts in this organization"
            + (skipped > 0 ? $", {skipped} skipped (inactive in the course, a role Gadfly doesn't use, or refused)" : "") + "."
            + (problems.Count > 0 ? " " + string.Join(" ", problems) : "")
            + (next is not null ? $" The roster is longer than {MaxPages} pages; only the first {MaxPages} were read." : "");
        return new SyncResult(true, message, members.Count, provisioned, skipped);
    }

    /// <summary>The <c>rel="next"</c> URL of an RFC 8288 <c>Link</c> header, or null.</summary>
    public static string? NextLink(IEnumerable<string> linkHeaders)
    {
        foreach (var header in linkHeaders)
            foreach (Match m in LinkPart().Matches(header))
                if (m.Groups["rel"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("next", StringComparer.OrdinalIgnoreCase)
                    && Uri.TryCreate(m.Groups["url"].Value, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                    return u.ToString();
        return null;
    }

    [GeneratedRegex("""<(?<url>[^>]+)>\s*;\s*rel\s*=\s*"?(?<rel>[^";,]+)"?""")]
    private static partial Regex LinkPart();
}
