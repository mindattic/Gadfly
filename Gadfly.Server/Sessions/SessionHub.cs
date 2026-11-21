using Gadfly.Engine;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Gadfly.Server.Sessions;

/// <summary>An answer as a client sends it.</summary>
public sealed record HubAnswer(string Text, string Mode = "text", DateTimeOffset? StartedAt = null,
    IReadOnlyList<TimedWord>? Words = null, IReadOnlyDictionary<string, double>? SpeechMetrics = null);

/// <summary>A batch of client-side integrity signals (browser, webcam, audio modules).</summary>
public sealed record HubIntegrityEvent(string Source, string Type, DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    double Confidence, string Summary);

/// <summary>
/// The live exam channel for clients outside the Blazor app (GAD-T45): embedded exam rooms in other
/// apps, and the voice pipeline (MindAttic.SpeechRecognition sends final transcripts with word timings
/// and speech metrics as <see cref="HubAnswer"/>; MindAttic.Avatar speaks each question it receives).
/// Every call is the signed-in student acting on their own session; the orchestrator checks ownership,
/// state and the server-side time limit. Server → client messages: <c>Step</c> (the new state and
/// question), <c>Thinking</c> (judging has started), <c>Refused</c> (a readable reason).
/// </summary>
[Authorize(Policy = GadflyPolicies.Student)]
public sealed class SessionHub(SessionOrchestrator orchestrator, IServiceScopeFactory scopes) : Hub
{
    public const string Path = "/hubs/session";

    // The caller comes from the hub connection itself (its user and the active-org cookie it connected with),
    // never from IHttpContextAccessor, which is absent on some transports (GAD-T73).
    private (Guid Org, Guid User) Caller()
    {
        var tenant = TenantSnapshot.FromPrincipal(Context.User,
            Context.GetHttpContext()?.Request.Cookies[ITenantContext.ActiveOrgCookie]);
        return tenant is { OrganizationId: { } org, UserId: { } user, Role: OrgRoles.Student }
            ? (org, user)
            : throw new HubException("Sign in as a student of this organization.");
    }

    /// <summary>Starts (or resumes) the attempt for an assignment; returns the session id. Consent must be recorded first.</summary>
    public async Task<Guid> Start(Guid assignmentId, string consentTextSha256)
    {
        var (org, user) = Caller();
        try
        {
            var id = await orchestrator.CreateOrResumeAsync(org, assignmentId, user, Context.GetHttpContext()?.Request.Headers.UserAgent.ToString(), Context.ConnectionAborted);
            if (string.IsNullOrWhiteSpace(consentTextSha256) || consentTextSha256.Length != 64)
                throw new SessionRefusedException("Consent is required before the exam starts.");
            await orchestrator.RecordConsentAsync(org, id, user, consentTextSha256, Context.ConnectionAborted);
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(id));
            return id;
        }
        catch (SessionRefusedException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    /// <summary>Joins an existing session's channel and returns the question waiting for an answer.</summary>
    public async Task<SessionStep> Current(Guid sessionId)
    {
        var (org, user) = Caller();
        var step = await Guard(() => orchestrator.CurrentAsync(org, sessionId, user, Context.ConnectionAborted));
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(sessionId));
        return step;
    }

    /// <summary>Submits the answer; judging takes a few seconds (a Thinking message is sent first), then returns the next step.</summary>
    public async Task<SessionStep> Answer(Guid sessionId, HubAnswer answer)
    {
        var (org, user) = Caller();
        if (answer is null || answer.Text is null) throw new HubException("An answer needs text (\"aporia\" if you don't know).");
        if (answer.Text.Length > 20_000) throw new HubException("That answer is too long.");
        await Clients.Group(Group(sessionId)).SendAsync("Thinking", sessionId);
        var step = await Guard(() => orchestrator.AnswerAsync(org, sessionId, user, new Answer
        {
            Text = answer.Text.Trim(),
            Mode = answer.Mode is "voice" ? "voice" : "text",
            StartedAt = answer.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Words = answer.Words ?? [],
            SpeechMetrics = answer.SpeechMetrics ?? new Dictionary<string, double>(),
        }, Context.ConnectionAborted));
        await Clients.OthersInGroup(Group(sessionId)).SendAsync("Step", step);
        return step;
    }

    /// <summary>Ends the attempt early (the student chose to finish).</summary>
    public Task<SessionStep> Finish(Guid sessionId)
    {
        var (org, user) = Caller();
        return Guard(() => orchestrator.EndAsync(org, sessionId, user, EndReason.Abandoned, Context.ConnectionAborted));
    }

    /// <summary>Records client-side integrity signals for the session (capped, never shown to the student as accusations).</summary>
    public async Task<int> Integrity(Guid sessionId, IReadOnlyList<HubIntegrityEvent> events)
    {
        var (org, user) = Caller();
        if (events is null || events.Count == 0) return 0;
        using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(org));
        var recorder = scope.ServiceProvider.GetRequiredService<HubIntegrityRecorder>();
        return await recorder.RecordAsync(org, user, sessionId, events.Take(50).ToList(), Context.ConnectionAborted);
    }

    private static string Group(Guid sessionId) => "session:" + sessionId.ToString("N");

    private static async Task<SessionStep> Guard(Func<Task<SessionStep>> step)
    {
        try { return await step(); }
        catch (SessionRefusedException ex) { throw new HubException(ex.Message); }
    }
}

/// <summary>Stores hub-delivered integrity events after checking the session is the caller's and still running.</summary>
public sealed class HubIntegrityRecorder(Data.GadflyDbContext db, TimeProvider clock)
{
    public const int MaxEventsPerSession = 500;
    private static readonly HashSet<string> Sources = ["browser", "webcam", "audio"];

    public async Task<int> RecordAsync(Guid org, Guid student, Guid sessionId, IReadOnlyList<HubIntegrityEvent> events, CancellationToken ct)
    {
        var session = db.ExamSessions.FirstOrDefault(s => s.Id == sessionId && s.OrganizationId == org && s.StudentUserId == student);
        if (session is null || session.State != Data.SessionState.Running) return 0;
        var existing = db.IntegrityEvents.Count(e => e.SessionId == sessionId);
        var room = Math.Max(0, MaxEventsPerSession - existing);
        var openTurn = db.SessionTurns.Where(t => t.SessionId == sessionId && t.AnswerText == null).OrderByDescending(t => t.Index)
            .Select(t => (Guid?)t.Id).FirstOrDefault();
        var now = clock.GetUtcNow();
        var added = 0;
        foreach (var e in events.Where(e => Sources.Contains(e.Source)).Take(room))
        {
            db.IntegrityEvents.Add(new Data.IntegrityEvent
            {
                OrganizationId = org,
                SessionId = sessionId,
                TurnId = openTurn,
                Source = e.Source,
                Type = Clip(e.Type, 60),
                StartedAt = e.StartedAt > now ? now : e.StartedAt,
                EndedAt = e.EndedAt,
                Confidence = Math.Clamp(e.Confidence, 0, 1),
                // Client-reported signals are never more than Medium on their own.
                Severity = e.Confidence >= 0.8 ? Data.IntegritySeverity.Medium : e.Confidence >= 0.4 ? Data.IntegritySeverity.Low : Data.IntegritySeverity.Info,
                Summary = Clip(e.Summary, 500),
            });
            added++;
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    private static string Clip(string? s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
}
