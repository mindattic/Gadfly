using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Transcripts;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Lti;

/// <summary>
/// Assignment and Grade Services 2.0 (GAD-T312/GAD-T383): sends a released result back to its LTI placement's line item.
/// "Released" is exactly what a student sees as released in Gadfly (<see cref="TranscriptAccess.PublishedToStudentAsync"/>:
/// finalized and published with approval), so an LMS never learns a score before the student could. Integrity notes are
/// never part of it: only the session's final percentage is sent.
/// <para><b>Stated simplification</b>: one score out of 100 per placement (the latest attempt's final percentage), not a
/// line item per learning objective: most gradebooks have no notion of Gadfly's objectives, and the objective-by-objective
/// report stays in Gadfly (the result page and the report PDF).</para>
/// </summary>
public sealed class LtiGradePassbackService(GadflyDbContext db, LtiTokenClient tokens, IHttpClientFactory http, TimeProvider clock, ILogger<LtiGradePassbackService> logger)
{
    private sealed record LineItem(string? id);

    public const string ScoreContentType = "application/vnd.ims.lis.v1.score+json";
    public const string LineItemContentType = "application/vnd.ims.lis.v2.lineitem+json";

    /// <summary>Sends (or re-sends, after an amendment) one assignment's released score. False, quietly, when the result
    /// isn't released, the assignment was never LTI-originated, or the score hasn't changed since the last send.</summary>
    public async Task<bool> SendIfDueAsync(Guid organizationId, Guid assignmentId, CancellationToken ct)
    {
        var link = await db.LtiAssignmentLinks.FirstOrDefaultAsync(l => l.AssignmentId == assignmentId && l.OrganizationId == organizationId, ct);
        if (link is null) return false;
        var session = await db.ExamSessions.AsNoTracking().Where(s => s.AssignmentId == assignmentId && s.OrganizationId == organizationId)
            .OrderByDescending(s => s.Attempt).FirstOrDefaultAsync(ct);
        if (session is null) return false;
        if (!await TranscriptAccess.PublishedToStudentAsync(db, organizationId, session.Id, ct)) return false;
        var grade = await db.FinalGrades.AsNoTracking().FirstOrDefaultAsync(g => g.SessionId == session.Id && g.OrganizationId == organizationId, ct);
        if (grade?.FinalPercent is not { } finalPercent) return false;
        // Remember the grade as stored (so the sweeper's comparison matches exactly); send it rounded to two places.
        if (link.PassbackSentAt is not null && link.LastPassbackPercent == finalPercent) return false;   // already sent, unchanged
        var percent = Math.Clamp(Math.Round(finalPercent, 2), 0m, 100m);

        var resourceLink = await db.LtiResourceLinks.FirstOrDefaultAsync(r => r.Id == link.ResourceLinkId && r.OrganizationId == organizationId, ct);
        var registration = resourceLink is null ? null
            : await db.LtiRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resourceLink.RegistrationId && r.OrganizationId == organizationId, ct);
        if (resourceLink is null || registration is null || !registration.Enabled) return false;

        var lineItemUrl = await LineItemUrlAsync(registration, resourceLink, ct);
        if (lineItemUrl is null)
        {
            logger.LogWarning("LTI passback: no line item for placement {ResourceLinkId} (assignment {AssignmentId}); the platform may not offer grade services.", resourceLink.Id, assignmentId);
            return false;
        }

        var token = await tokens.AccessTokenAsync(registration, LtiClaims.ScopeAgsScore, ct);
        var body = ScoreJson(link.LtiSubject, percent, clock.GetUtcNow());
        using var request = new HttpRequestMessage(HttpMethod.Post, ScoresUrl(lineItemUrl))
        {
            Content = new StringContent(body, Encoding.UTF8, ScoreContentType),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ScoreContentType);   // no charset parameter: some platforms match the type exactly
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.CreateClient(LtiSetup.HttpClientName).SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("LTI passback failed for assignment {AssignmentId}: {Status} {Body}", assignmentId, (int)response.StatusCode, Truncate(text));
            return false;
        }

        link.PassbackSentAt = clock.GetUtcNow();
        link.LastPassbackPercent = finalPercent;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId, ActorRole = "LTI", Action = "lti.grade-sent", EntityType = "ExamAssignment", EntityId = assignmentId.ToString(),
            SessionId = session.Id, AfterJson = JsonSerializer.Serialize(new { percent, placement = resourceLink.Id }), At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The AGS score body (AGS 2.0 section 3.4): the LTI user id, the score out of 100, and "completed and fully graded".</summary>
    public static string ScoreJson(string ltiSubject, decimal percent, DateTimeOffset at) => JsonSerializer.Serialize(new
    {
        userId = ltiSubject,
        scoreGiven = (double)percent,
        scoreMaximum = 100.0,
        activityProgress = "Completed",
        gradingProgress = "FullyGraded",
        timestamp = at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
    });

    /// <summary>The line item's <c>/scores</c> endpoint: the path gets <c>/scores</c>, any query string (Moodle's
    /// <c>?type_id=</c>) is kept where it was, after the path.</summary>
    public static string ScoresUrl(string lineItemUrl)
    {
        var u = new UriBuilder(lineItemUrl);
        u.Path = u.Path.TrimEnd('/') + "/scores";
        return u.Uri.AbsoluteUri;
    }

    /// <summary>The placement's own line item: already known, else created now against the <c>lineitems</c> collection
    /// (and remembered on the placement).</summary>
    private async Task<string?> LineItemUrlAsync(LtiRegistration reg, LtiResourceLink link, CancellationToken ct)
    {
        if (link.LineItemUrl is { Length: > 0 }) return link.LineItemUrl;
        if (link.LineItemsUrl is not { Length: > 0 } collectionUrl) return null;

        var examTitle = await db.Exams.AsNoTracking().Where(e => e.Id == link.ExamId && e.OrganizationId == link.OrganizationId)
            .Select(e => e.Title).FirstOrDefaultAsync(ct) ?? "Gadfly exam";
        var token = await tokens.AccessTokenAsync(reg, LtiClaims.ScopeAgsLineItem, ct);
        var body = JsonSerializer.Serialize(new { scoreMaximum = 100.0, label = Truncate(examTitle, 200), resourceLinkId = link.ResourceLinkId, tag = "gadfly" });
        using var request = new HttpRequestMessage(HttpMethod.Post, collectionUrl) { Content = new StringContent(body, Encoding.UTF8) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(LineItemContentType);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.CreateClient(LtiSetup.HttpClientName).SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("LTI passback: the platform refused to create a line item for placement {ResourceLinkId}: {Status}", link.Id, (int)response.StatusCode);
            return null;
        }
        LineItem? created;
        try { created = JsonSerializer.Deserialize<LineItem>(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { return null; }
        if (created?.id is not { Length: > 0 } url || !Uri.TryCreate(url, UriKind.Absolute, out _)) return null;

        link.LineItemUrl = url;
        await db.SaveChangesAsync(ct);
        return url;
    }

    private static string Truncate(string s, int max = 500) => s.Length <= max ? s : s[..max];
}

/// <summary>Finds released results whose LTI placement hasn't been told yet, every two minutes (GAD-T312/GAD-T383: a polling
/// sweep rather than a hook in grading's publish step, so grading's own code stays untouched; the stated cost is a delay of
/// up to a couple of minutes between release and the LMS gradebook updating). A failed send is retried on the next sweep.</summary>
public sealed class LtiPassbackSweeper(IServiceScopeFactory scopes, TimeProvider clock, ILogger<LtiPassbackSweeper> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken); }   // let startup (migrations) finish first
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogWarning(e, "LTI passback sweep failed; trying again next time."); }
            try { await Task.Delay(Interval, clock, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();

        // Every LTI-linked assignment with a released final grade not yet matching what was last sent. SendIfDueAsync makes
        // the authoritative check (latest attempt, approval records); this only narrows the candidates.
        var candidates = await (
            from link in db.LtiAssignmentLinks.AsNoTracking()
            join s in db.ExamSessions.AsNoTracking() on link.AssignmentId equals s.AssignmentId
            join g in db.FinalGrades.AsNoTracking() on s.Id equals g.SessionId
            where g.FinalPercent != null
                  && (g.Status == GradeStatus.Published || g.Status == GradeStatus.UnderAppeal || g.Status == GradeStatus.Amended)
                  && (link.PassbackSentAt == null || link.LastPassbackPercent != g.FinalPercent)
            select new { link.AssignmentId, link.OrganizationId }
        ).Distinct().Take(200).ToListAsync(ct);

        foreach (var c in candidates)
        {
            // Each send in its own scope acting for that organization, so one organization's work is guarded to it.
            using var orgScope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(c.OrganizationId));
            var passback = orgScope.ServiceProvider.GetRequiredService<LtiGradePassbackService>();
            try { await passback.SendIfDueAsync(c.OrganizationId, c.AssignmentId, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { logger.LogWarning(e, "LTI passback failed for assignment {AssignmentId}", c.AssignmentId); }
        }
    }
}
