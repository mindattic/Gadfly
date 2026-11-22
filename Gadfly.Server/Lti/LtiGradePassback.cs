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

    /// <summary>What one passback try came to.</summary>
    public enum Outcome { NotDue, Sent, NoGradebook, Failed, GaveUp }

    /// <summary>Tries before a send is marked failed and waits for an admin's Retry (GAD-T458).</summary>
    public const int MaxAttempts = 8;

    /// <summary>
    /// The wait before the next try after <paramref name="failures"/> failed ones: two minutes, doubling each time, at most
    /// twelve hours (2, 4, 8, ... minutes: the eight tries span about four hours).
    /// </summary>
    public static TimeSpan Backoff(int failures) =>
        TimeSpan.FromMinutes(Math.Min(12 * 60, 2 * Math.Pow(2, Math.Max(0, failures - 1))));

    /// <summary>
    /// Sends (or re-sends, after an amendment) one assignment's released score. <see cref="Outcome.NotDue"/>, quietly, when the
    /// result isn't released, the assignment was never LTI-originated, the score hasn't changed since the last send, a retry
    /// isn't due yet, or the connection is off. <see cref="Outcome.GaveUp"/> when an earlier send of this same score ran out of
    /// tries (an admin's Retry, or a changed grade, starts it again). A failure (the LMS refusing or unreachable) is kept on the
    /// assignment's link with its reason and retried with exponential backoff (<see cref="Backoff"/>); after
    /// <see cref="MaxAttempts"/> it is marked failed, audited, and shown on /org/lti (GAD-T458).
    /// </summary>
    public async Task<Outcome> SendIfDueAsync(Guid organizationId, Guid assignmentId, CancellationToken ct)
    {
        var link = await db.LtiAssignmentLinks.FirstOrDefaultAsync(l => l.AssignmentId == assignmentId && l.OrganizationId == organizationId, ct);
        if (link is null) return Outcome.NotDue;
        var session = await db.ExamSessions.AsNoTracking().Where(s => s.AssignmentId == assignmentId && s.OrganizationId == organizationId)
            .OrderByDescending(s => s.Attempt).FirstOrDefaultAsync(ct);
        if (session is null) return Outcome.NotDue;
        if (!await TranscriptAccess.PublishedToStudentAsync(db, organizationId, session.Id, ct)) return Outcome.NotDue;
        var grade = await db.FinalGrades.AsNoTracking().FirstOrDefaultAsync(g => g.SessionId == session.Id && g.OrganizationId == organizationId, ct);
        if (grade?.FinalPercent is not { } finalPercent) return Outcome.NotDue;
        // Remember the grade as stored (so the sweeper's comparison matches exactly); send it rounded to two places.
        if (link.PassbackSentAt is not null && link.LastPassbackPercent == finalPercent) return Outcome.NotDue;   // already sent, unchanged
        var percent = Math.Clamp(Math.Round(finalPercent, 2), 0m, 100m);

        var now = clock.GetUtcNow();
        if (link.PassbackPendingPercent != finalPercent)
        {
            // A new score to send (the first, or an amendment): its own fresh set of tries.
            link.PassbackPendingPercent = finalPercent;
            link.PassbackAttempts = 0;
            link.PassbackRetryAt = null;
            link.PassbackFailedAt = null;
            link.PassbackError = null;
        }
        if (link.PassbackFailedAt is not null) return Outcome.GaveUp;
        if (link.PassbackRetryAt is { } retryAt && retryAt > now) return Outcome.NotDue;

        var resourceLink = await db.LtiResourceLinks.FirstOrDefaultAsync(r => r.Id == link.ResourceLinkId && r.OrganizationId == organizationId, ct);
        var registration = resourceLink is null ? null
            : await db.LtiRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resourceLink.RegistrationId && r.OrganizationId == organizationId, ct);
        if (resourceLink is null || registration is null || !registration.Enabled) return Outcome.NotDue;   // sent once the connection is back on
        if (resourceLink.LineItemUrl is not { Length: > 0 } && resourceLink.LineItemsUrl is not { Length: > 0 })
        {
            // The platform offered no gradebook service for this placement: nothing to retry; the page says so.
            logger.LogInformation("LTI passback: placement {ResourceLinkId} has no gradebook service (assignment {AssignmentId}).", resourceLink.Id, assignmentId);
            return Outcome.NoGradebook;
        }

        string? failure;
        try { failure = await SendAsync(registration, resourceLink, link, percent, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(e, "LTI passback failed for assignment {AssignmentId}", assignmentId);
            failure = e is TaskCanceledException ? "The LMS did not answer in time." : $"Gadfly could not reach the LMS: {e.Message}";
        }

        if (failure is not null)
        {
            link.PassbackAttempts++;
            link.PassbackError = Truncate(failure);
            var gaveUp = link.PassbackAttempts >= MaxAttempts;
            if (gaveUp)
            {
                link.PassbackFailedAt = now;
                link.PassbackRetryAt = null;
                db.AuditEvents.Add(new AuditEvent
                {
                    OrganizationId = organizationId, ActorRole = "LTI", Action = "lti.grade-failed", EntityType = "ExamAssignment", EntityId = assignmentId.ToString(),
                    SessionId = session.Id, Reason = link.PassbackError,
                    AfterJson = JsonSerializer.Serialize(new { percent, attempts = link.PassbackAttempts, placement = resourceLink.Id }), At = now,
                });
            }
            else link.PassbackRetryAt = now + Backoff(link.PassbackAttempts);
            await db.SaveChangesAsync(ct);
            return gaveUp ? Outcome.GaveUp : Outcome.Failed;
        }

        link.PassbackSentAt = clock.GetUtcNow();
        link.LastPassbackPercent = finalPercent;
        link.PassbackPendingPercent = null;
        link.PassbackAttempts = 0;
        link.PassbackRetryAt = null;
        link.PassbackFailedAt = null;
        link.PassbackError = null;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId, ActorRole = "LTI", Action = "lti.grade-sent", EntityType = "ExamAssignment", EntityId = assignmentId.ToString(),
            SessionId = session.Id, AfterJson = JsonSerializer.Serialize(new { percent, placement = resourceLink.Id }), At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return Outcome.Sent;
    }

    /// <summary>Posts the score; null when the LMS accepted it, else why not, in words an admin can act on.</summary>
    private async Task<string?> SendAsync(LtiRegistration registration, LtiResourceLink resourceLink, LtiAssignmentLink link, decimal percent, CancellationToken ct)
    {
        var (lineItemUrl, lineItemProblem) = await LineItemUrlAsync(registration, resourceLink, ct);
        if (lineItemUrl is null) return lineItemProblem;

        var token = await tokens.AccessTokenAsync(registration, LtiClaims.ScopeAgsScore, ct);
        var body = ScoreJson(link.LtiSubject, percent, clock.GetUtcNow());
        using var request = new HttpRequestMessage(HttpMethod.Post, ScoresUrl(lineItemUrl))
        {
            Content = new StringContent(body, Encoding.UTF8, ScoreContentType),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ScoreContentType);   // no charset parameter: some platforms match the type exactly
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.CreateClient(LtiSetup.HttpClientName).SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return null;
        var text = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("LTI passback failed for assignment {AssignmentId}: {Status} {Body}", link.AssignmentId, (int)response.StatusCode, Truncate(text));
        return $"The LMS refused the grade ({(int)response.StatusCode} {response.ReasonPhrase}).";
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
    private async Task<(string? Url, string? Problem)> LineItemUrlAsync(LtiRegistration reg, LtiResourceLink link, CancellationToken ct)
    {
        if (link.LineItemUrl is { Length: > 0 }) return (link.LineItemUrl, null);
        if (link.LineItemsUrl is not { Length: > 0 } collectionUrl) return (null, "The LMS offered no gradebook service for this placement.");

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
            return (null, $"The LMS refused to create the gradebook column ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }
        LineItem? created;
        try { created = JsonSerializer.Deserialize<LineItem>(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { return (null, "The LMS's answer to creating the gradebook column was not in the expected format."); }
        if (created?.id is not { Length: > 0 } url || !Uri.TryCreate(url, UriKind.Absolute, out _))
            return (null, "The LMS created the gradebook column but did not say where it is.");

        link.LineItemUrl = url;
        await db.SaveChangesAsync(ct);
        return (url, null);
    }

    private static string Truncate(string s, int max = 500) => s.Length <= max ? s : s[..max];
}

/// <summary>Finds released results whose LTI placement hasn't been told yet, every two minutes (GAD-T312/GAD-T383: a polling
/// sweep rather than a hook in grading's publish step, so grading's own code stays untouched; the stated cost is a delay of
/// up to a couple of minutes between release and the LMS gradebook updating). A failed send is retried once its backoff has
/// passed (GAD-T458, <see cref="LtiGradePassbackService.Backoff"/>); one that ran out of tries waits for an admin's Retry.</summary>
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

        // Every LTI-linked assignment with a released final grade not yet matching what was last sent, whose retry (if any) is
        // due and which hasn't run out of tries on that same grade. SendIfDueAsync makes the authoritative check (latest attempt,
        // approval records); this only narrows the candidates.
        var now = clock.GetUtcNow();
        var candidates = await (
            from link in db.LtiAssignmentLinks.AsNoTracking()
            join s in db.ExamSessions.AsNoTracking() on link.AssignmentId equals s.AssignmentId
            join g in db.FinalGrades.AsNoTracking() on s.Id equals g.SessionId
            where g.FinalPercent != null
                  && (g.Status == GradeStatus.Published || g.Status == GradeStatus.UnderAppeal || g.Status == GradeStatus.Amended)
                  && (link.PassbackSentAt == null || link.LastPassbackPercent != g.FinalPercent)
                  && (link.PassbackPendingPercent == null || link.PassbackPendingPercent != g.FinalPercent
                      || (link.PassbackFailedAt == null && (link.PassbackRetryAt == null || link.PassbackRetryAt <= now)))
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
