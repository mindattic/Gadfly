using Gadfly.Engine;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Gadfly.Server.Sessions;

/// <summary>An answer as a client sends it.</summary>
/// <param name="ExpectedTurn">The step's TurnIndex this answers (GAD-T321): with it, a double submit or late resend is dropped, never
/// taken as the answer to the next question. Optional for older clients.</param>
public sealed record HubAnswer(string Text, string Mode = "text", DateTimeOffset? StartedAt = null,
    IReadOnlyList<TimedWord>? Words = null, IReadOnlyDictionary<string, double>? SpeechMetrics = null, int? ExpectedTurn = null);

/// <summary>A batch of client-side integrity signals (browser, webcam, audio modules).</summary>
public sealed record HubIntegrityEvent(string Source, string Type, DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    double Confidence, string Summary, string? EpisodeId = null, bool Ongoing = false);

/// <summary>
/// The live exam channel for clients outside the Blazor app (GAD-T45): embedded exam rooms in other
/// apps, and the voice pipeline (MindAttic.SpeechRecognition sends final transcripts with word timings
/// and speech metrics as <see cref="HubAnswer"/>; MindAttic.Avatar speaks each question it receives).
/// Every call is the signed-in student acting on their own session; the orchestrator checks ownership,
/// state and the server-side time limit. Server → client messages: <c>Step</c> (the new state and
/// question, and any pending <see cref="SessionStep.Verification"/> request), <c>Thinking</c> (judging has
/// started), <c>Refused</c> (a readable reason). A step with Verification set asks the student about a typed answer
/// just given (Clarify, or CorrectAnswer for a typo) before it is judged. A spoken answer is taken as heard and judged
/// at once (GAD-T405): it is never read back, corrected, said again or changed.
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
    public Task<Guid> Start(Guid assignmentId, string consentTextSha256) => StartIn(assignmentId, consentTextSha256, null);

    /// <summary>
    /// <see cref="Start"/> in an exam language chosen on the get-ready screen (GAD-T534, GAD-T546): one the exam offers, English
    /// when null. An attempt already in progress keeps its language.
    /// </summary>
    public async Task<Guid> StartIn(Guid assignmentId, string consentTextSha256, string? language)
    {
        var (org, user) = Caller();
        if (language is { Length: > 16 }) throw new HubException("Choose a language.");
        try
        {
            var id = await orchestrator.CreateOrResumeAsync(org, assignmentId, user, Context.GetHttpContext()?.Request.Headers.UserAgent.ToString(),
                language, Context.ConnectionAborted);
            if (string.IsNullOrWhiteSpace(consentTextSha256) || consentTextSha256.Length != 64)
                throw new SessionRefusedException("Consent is required before the exam starts.");
            await orchestrator.RecordConsentAsync(org, id, user, consentTextSha256, Context.ConnectionAborted);
            await JoinAsync(id);
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
        // A hub client has no "the examiner began speaking" signal: the question it is handed now is the question asked, so a held
        // exam clock starts here rather than giving the question out while the student's time isn't counting (GAD-T239).
        if (!step.ClockStarted && step.Question is not null)
            step = await Guard(() => orchestrator.StartClockAsync(org, sessionId, user, Context.ConnectionAborted));
        await JoinAsync(sessionId);
        return step;
    }

    /// <summary>Submits the answer; judging takes a few seconds (a Thinking message is sent first), then returns the next step.</summary>
    public async Task<SessionStep> Answer(Guid sessionId, HubAnswer answer)
    {
        var (org, user) = Caller();
        if (answer is null || answer.Text is null) throw new HubException("An answer needs text (\"aporia\" if you don't know).");
        if (answer.Text.Length > 20_000) throw new HubException("That answer is too long.");
        if (answer.Words is { Count: > MaxWords } || answer.SpeechMetrics is { Count: > MaxSpeechMetrics })
            throw new HubException("That answer carries too much timing data.");
        // Answers are spoken (GAD-T239): a typed answer is taken only from a student granted the typed-answer accommodation on this
        // assignment by an instructor or admin (GAD-T260). "I don't know" is a button, not typing, so it is always allowed.
        if (answer.Mode is not "voice" && !Gadfly.Sources.Verification.ResponseVerifier.IsAporiaWord(answer.Text)
            && !await TypedAnswersAllowedAsync(org, user, sessionId))
            throw new HubException("This exam is answered out loud. Typed answers are an accommodation only your instructor can grant.");
        // Only a connection that already proved ownership (Start/Current) may signal the session's channel (GAD-T74).
        if (Joined().Contains(sessionId)) await Clients.Group(Group(sessionId)).SendAsync("Thinking", sessionId);
        var sanitized = await ToAnswerAsync(org, sessionId, answer, DateTimeOffset.UtcNow);
        var step = await Guard(() => orchestrator.AnswerAsync(org, sessionId, user, sanitized, Context.ConnectionAborted, answer.ExpectedTurn));
        await Clients.OthersInGroup(Group(sessionId)).SendAsync("Step", step);
        return step;
    }

    /// <summary>
    /// The client's answer as the server can stand behind it, the way <see cref="Voice.VoiceAnswerBuilder"/> treats the exam room's:
    /// the start time and word confidences are bounded, keys only the server may set (strain.*, verification.*, time to first word)
    /// are dropped, the word-derived speech metrics and transcript advice are recomputed from the words, and the voice strain
    /// comparison is computed here against the student's own earlier answers.
    /// </summary>
    private async Task<Answer> ToAnswerAsync(Guid org, Guid sessionId, HubAnswer answer, DateTimeOffset now)
    {
        var voice = answer.Mode is "voice";
        var text = answer.Text.Trim();
        var startedAt = answer.StartedAt is { } s && s <= now && s >= now - TimeSpan.FromHours(6) ? s : (DateTimeOffset?)null;
        var words = (answer.Words ?? []).Where(w => w is not null && !string.IsNullOrWhiteSpace(w.Text)
                && w.Start >= TimeSpan.Zero && w.End >= w.Start && !double.IsNaN(w.Confidence))
            .Select(w => w with { Text = w.Text.Trim(), Confidence = Math.Clamp(w.Confidence, 0, 1) }).ToList();
        var metrics = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, value) in answer.SpeechMetrics ?? new Dictionary<string, double>())
            if (!string.IsNullOrEmpty(key) && double.IsFinite(value) && !ServerDerived(key)) metrics[key] = value;
        if (voice && words.Count > 0)
        {
            List<MindAttic.SpeechRecognition.TranscriptSegment> segments =
            [
                new()
                {
                    Index = 0, IsFinal = true, Text = text,
                    Words = words.Select(w => new MindAttic.SpeechRecognition.TranscriptWord(w.Text, w.Start, w.End, w.Confidence, null, false)).ToList(),
                },
            ];
            var computed = Voice.SpeechMetricKeys.Flatten(
                MindAttic.SpeechRecognition.Metrics.SpeechMetricsCalculator.Compute(segments, new MindAttic.SpeechRecognition.Metrics.SpeechMetricsContext()),
                MindAttic.SpeechRecognition.TranscriptVerification.Assess(segments));
            // The words carry no speaker labels, so the client's diarization (speakers.*) is the only source for those.
            foreach (var (key, value) in computed)
                if (!key.StartsWith("speakers.", StringComparison.Ordinal)) metrics[key] = value;
        }
        var result = new Answer
        {
            Text = text,
            Mode = voice ? "voice" : "text",
            StartedAt = startedAt,
            CompletedAt = now,
            Words = words,
            SpeechMetrics = metrics,
        };
        if (!voice || metrics.Count == 0) return result;
        using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(org));
        return await Voice.VoiceStrain.ApplyAsync(scope.ServiceProvider.GetRequiredService<Data.GadflyDbContext>(), sessionId, result, Context.ConnectionAborted);
    }

    private static bool ServerDerived(string key) =>
        key.StartsWith("strain.", StringComparison.Ordinal) || key.StartsWith("verification.", StringComparison.Ordinal)
        || key == Voice.SpeechMetricKeys.TimeToFirstWordSeconds;

    /// <summary>
    /// Replies to a pending meaning clarification (step.Verification.Kind "meaning"). The reply is stored beside
    /// the answer, never over it; an empty reply declines (the answer then goes to a human reviewer as recorded).
    /// </summary>
    public Task<SessionStep> Clarify(Guid sessionId, Guid responseId, string? reply)
    {
        var (org, user) = Caller();
        if (reply is { Length: > 5000 }) throw new HubException("That clarification is too long.");
        return SignalAndRun(sessionId, () => orchestrator.ClarifyAsync(org, sessionId, user, responseId, reply, Context.ConnectionAborted));
    }

    /// <summary>
    /// Takes a pending transcript request (Kind "transcript") as heard. None is created any more (GAD-T405); this only lets a client
    /// resolve one left over from before that change, so an exam already in progress isn't stranded. Confirms wording only, not correctness.
    /// </summary>
    public Task<SessionStep> ConfirmTranscript(Guid sessionId, Guid responseId)
    {
        var (org, user) = Caller();
        return SignalAndRun(sessionId, () => orchestrator.ConfirmTranscriptAsync(org, sessionId, user, responseId, Context.ConnectionAborted));
    }

    /// <summary>
    /// A typo fix to a typed answer (GAD-T260): resolves a pending meaning clarification with the fixed text instead, or with nothing
    /// pending fixes a typo in the last judged answer. Small edits only; the original is kept. A spoken answer is kept as heard
    /// (GAD-T405): it can't be corrected, and <paramref name="repeat"/> (saying it again) is refused; the parameter stays only so an
    /// older client's call still binds.
    /// </summary>
    public Task<SessionStep> CorrectAnswer(Guid sessionId, string text, bool repeat = false)
    {
        var (org, user) = Caller();
        if (repeat) throw new HubException("A spoken answer is kept as it was heard; it can't be said again.");
        if (text is null || text.Length > 20_000) throw new HubException("The corrected answer needs text.");
        return SignalAndRun(sessionId, () => orchestrator.CorrectAnswerAsync(org, sessionId, user, text, Context.ConnectionAborted));
    }

    /// <summary>
    /// "I want to change my answer": a substantive revision of the last judged typed answer, recorded as a new response event. A
    /// spoken answer has no do-over (GAD-T407): the orchestrator refuses it.
    /// </summary>
    public async Task<SessionStep> ReviseAnswer(Guid sessionId, string text)
    {
        var (org, user) = Caller();
        if (text is null || text.Length > 20_000) throw new HubException("Your changed answer needs text.");
        // A changed answer is typed: only under the typed-answer accommodation (GAD-T239, GAD-T260).
        if (!await TypedAnswersAllowedAsync(org, user, sessionId))
            throw new HubException("This exam is answered out loud. Typed answers are an accommodation only your instructor can grant.");
        return await Guard(() => orchestrator.ReviseAnswerAsync(org, sessionId, user, text, Context.ConnectionAborted));
    }

    /// <summary>The student's typed-answer accommodation on this session's assignment (GAD-T260); false when it isn't theirs.</summary>
    private async Task<bool> TypedAnswersAllowedAsync(Guid org, Guid user, Guid sessionId)
    {
        using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(org));
        return await Student.StudentExams.TypedAnswersAsync(scope.ServiceProvider.GetRequiredService<Data.GadflyDbContext>(), org, user, sessionId, Context.ConnectionAborted);
    }

    private async Task<SessionStep> SignalAndRun(Guid sessionId, Func<Task<SessionStep>> run)
    {
        if (Joined().Contains(sessionId)) await Clients.Group(Group(sessionId)).SendAsync("Thinking", sessionId);
        var step = await Guard(run);
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

    private const int MaxWords = 10_000;
    private const int MaxSpeechMetrics = 64;

    private static string Group(Guid sessionId) => "session:" + sessionId.ToString("N");

    /// <summary>Sessions this connection joined after the orchestrator confirmed the caller owns them.</summary>
    private HashSet<Guid> Joined() =>
        (HashSet<Guid>)(Context.Items.TryGetValue("gadfly:sessions", out var v) && v is HashSet<Guid> s ? s : Context.Items["gadfly:sessions"] = new HashSet<Guid>());

    private async Task JoinAsync(Guid sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(sessionId));
        lock (Joined()) Joined().Add(sessionId);
    }

    private static async Task<T> Guard<T>(Func<Task<T>> step)
    {
        try { return await step(); }
        catch (SessionRefusedException ex) { throw new HubException(ex.Message); }
    }

    /// <summary>
    /// "Say it another way" (GAD-T555): the open question reworded through the leak guard, in <see cref="SessionStep.Rephrased"/>,
    /// for the examiner to say. Free and outside scoring; it never changes the question on record.
    /// </summary>
    public Task<SessionStep> RephraseQuestion(Guid sessionId)
    {
        var (org, user) = Caller();
        return Guard(() => orchestrator.RephraseQuestionAsync(org, sessionId, user, Context.ConnectionAborted));
    }

    /// <summary>
    /// Chooses the exam language before the first question (GAD-T534, GAD-T546); refused once the exam has begun or when the exam
    /// does not offer the language. Returns the language set.
    /// </summary>
    public Task<string> ChooseLanguage(Guid sessionId, string language)
    {
        var (org, user) = Caller();
        if (string.IsNullOrWhiteSpace(language) || language.Length > 16) throw new HubException("Choose a language.");
        return Guard(() => orchestrator.ChooseLanguageAsync(org, sessionId, user, language, Context.ConnectionAborted));
    }
}

/// <summary>Stores hub-delivered integrity events after checking the session is the caller's and still running.</summary>
public sealed class HubIntegrityRecorder(Data.GadflyDbContext db, TimeProvider clock)
{
    public const int MaxEventsPerSession = 500;

    /// <summary>
    /// The browser and audio rows a hub client may add to one session, well under the browser recorder's own cap
    /// (<see cref="Integrity.IntegrityRecorder.MaxEventsPerSession"/>), so a client can never use up the room real signals need.
    /// </summary>
    public const int MaxHubEventsPerSession = 100;

    /// <summary>At most this many browser and audio events a session may send through the hub per rate window.</summary>
    public const int MaxPerWindow = 20;
    public static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The types a client may report, per source. Audio signals the server derives from an answer's speech metrics
    /// (<see cref="Integrity.AudioIntegritySignals"/>) are never taken from a client: a client-sent one would pre-empt the real one.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlySet<string>> Sources = new(StringComparer.Ordinal)
    {
        ["browser"] = Integrity.IntegrityRecorder.BrowserTypes,
        ["audio"] = new HashSet<string>(StringComparer.Ordinal) { "whispering" },
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Queue<DateTimeOffset>> Recent = new();

    public async Task<int> RecordAsync(Guid org, Guid student, Guid sessionId, IReadOnlyList<HubIntegrityEvent> events, CancellationToken ct)
    {
        // Webcam events (GAD-T57) get their own allow-list, rate limit and severity rules, whichever channel carried them.
        var webcamStored = 0;
        if (events.Any(e => e is not null && e.Source == Integrity.WebcamIntegrityRecorder.Source))
            webcamStored = (await new Integrity.WebcamIntegrityRecorder(db, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<Integrity.WebcamIntegrityRecorder>.Instance)
                .RecordAsync(org, student, sessionId, events.Where(e => e is not null && e.Source == Integrity.WebcamIntegrityRecorder.Source).ToList(), ct)).Stored;
        var session = db.ExamSessions.FirstOrDefault(s => s.Id == sessionId && s.OrganizationId == org && s.StudentUserId == student);
        if (session is null || session.State != Data.SessionState.Running) return webcamStored;
        var existing = db.IntegrityEvents.Count(e => e.SessionId == sessionId);
        var existingHub = db.IntegrityEvents.Count(e => e.SessionId == sessionId && (e.Source == "browser" || e.Source == "audio"));
        var room = Math.Max(0, Math.Min(MaxEventsPerSession - existing, MaxHubEventsPerSession - existingHub));
        var allowed = events.Where(e => e is not null && e.Source is { } src && Sources.TryGetValue(src, out var types)
            && types.Contains((e.Type ?? "").Trim()) && double.IsFinite(e.Confidence)).Take(room).ToList();
        var now = clock.GetUtcNow();
        allowed = allowed.Take(TakeRate(sessionId, now, allowed.Count)).ToList();
        if (allowed.Count == 0) return webcamStored;
        var openTurn = db.SessionTurns.Where(t => t.SessionId == sessionId && t.AnswerText == null).OrderByDescending(t => t.Index)
            .Select(t => (Guid?)t.Id).FirstOrDefault();
        var added = 0;
        foreach (var e in allowed)
        {
            db.IntegrityEvents.Add(new Data.IntegrityEvent
            {
                OrganizationId = org,
                SessionId = sessionId,
                TurnId = openTurn,
                Source = e.Source,
                Type = Clip(e.Type.Trim(), 60),
                StartedAt = e.StartedAt > now ? now : e.StartedAt,
                EndedAt = e.EndedAt is { } end ? (end > now ? now : end < e.StartedAt ? (e.StartedAt > now ? now : e.StartedAt) : end) : null,
                Confidence = Math.Clamp(e.Confidence, 0, 1),
                // Client-reported signals are never more than Medium on their own.
                Severity = e.Confidence >= 0.8 ? Data.IntegritySeverity.Medium : e.Confidence >= 0.4 ? Data.IntegritySeverity.Low : Data.IntegritySeverity.Info,
                Summary = Clip(e.Summary, 500),
            });
            added++;
        }
        await db.SaveChangesAsync(ct);
        Admin.LiveSessionFeed.Shared.IntegrityRecorded(org, sessionId, now);   // live monitor (GAD-T299)
        return added + webcamStored;
    }

    private static string Clip(string? s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];

    /// <summary>How many of <paramref name="want"/> events this session may still store in the current rate window.</summary>
    private static int TakeRate(Guid sessionId, DateTimeOffset now, int want)
    {
        if (Recent.Count > 2000)
            foreach (var (id, other) in Recent)
                lock (other)
                    if (other.Count == 0 || now - other.Peek() > RateWindow) Recent.TryRemove(id, out _);
        var q = Recent.GetOrAdd(sessionId, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > RateWindow) q.Dequeue();
            var take = Math.Min(Math.Max(0, MaxPerWindow - q.Count), want);
            for (var i = 0; i < take; i++) q.Enqueue(now);
            return take;
        }
    }
}
