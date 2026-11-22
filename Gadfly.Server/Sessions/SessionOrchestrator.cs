using System.Collections.Concurrent;
using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Verification;
using Gadfly.Socrates;
using Gadfly.Sources;
using Gadfly.Sources.Verification;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Sessions;

/// <summary>What the student's client sees after a step. Never contains scores (results are released by the instructor).</summary>
public sealed record SessionStep
{
    public required Guid SessionId { get; init; }
    public required SessionState State { get; init; }
    /// <summary>The question waiting for an answer, or null once the session has ended.</summary>
    public string? Question { get; init; }
    public int? TurnIndex { get; init; }
    public string? ConceptTitle { get; init; }
    public string? Move { get; init; }
    /// <summary>Server-authoritative time left.</summary>
    public TimeSpan Remaining { get; init; }
    public string? EndReason { get; init; }
    public int ConceptsTotal { get; init; }
    public int ConceptsClosed { get; init; }
    /// <summary>
    /// A request about the answer just given, waiting for the student before it is judged (GAD-T87/T88): a neutral
    /// meaning clarification of a typed answer. A spoken answer is taken as heard and never gets one (GAD-T405); a
    /// transcript confirmation can only be one left over from before that. One at a time; while it is set,
    /// <see cref="Question"/> is still the question that answer belongs to.
    /// </summary>
    public PendingVerification? Verification { get; init; }
    /// <summary>The last judged answer, for "correct a typo" and "change my answer" on a typed answer (null before the first one).</summary>
    public LastAnswer? LastAnswer { get; init; }
    /// <summary>A one-line confirmation of what the student's last action did, e.g. "Your correction was saved."</summary>
    public string? Notice { get; init; }
    /// <summary>False while the exam clock waits for the examiner to begin the first question (GAD-T239); the student's time isn't counting yet.</summary>
    public bool ClockStarted { get; init; } = true;
    /// <summary>
    /// True while the exam is paused for a lost microphone, camera or examiner connection (GAD-T263): <see cref="Remaining"/> stands
    /// still for up to <see cref="PauseAllowanceLeft"/> more, then counts down again.
    /// </summary>
    public bool ClockPaused { get; init; }
    /// <summary>How much more paused time can still be added back to this exam (GAD-T263).</summary>
    public TimeSpan PauseAllowanceLeft { get; init; }
    /// <summary>True while staff have paused the exam from the live monitor (GAD-T299): the clock stands still and answers wait.</summary>
    public bool HeldByStaff { get; init; }
}

/// <summary>Why a session could not be started or advanced, in words a student can read.</summary>
public sealed class SessionRefusedException(string message) : Exception(message);

/// <summary>
/// Runs live exam sessions on the server (GAD-T44). Each session's <see cref="SourceExamEngine"/> is
/// kept in memory while active and rebuilt from SQL (transcript + claim ledger) on demand, so a
/// session survives a client reconnect or a server restart and resumes at the same turn. Steps on
/// one session are serialized; every step is persisted (the transcript for resuming, and
/// SessionTurn / Judgment / JudgeVote / ConceptMastery / SessionClaim / SessionReport rows for
/// review), guarded by the session's row version so two servers cannot both advance it.
/// </summary>
public sealed class SessionOrchestrator(
    IServiceScopeFactory scopes,
    ISocraticVotingClient panel,
    TimeProvider clock,
    ILogger<SessionOrchestrator> log,
    Platform.UsageMeter meter)
{
    private sealed class Live(SourceExamEngine engine, Guid organizationId, Guid studentUserId, AssessmentMode mode)
    {
        public AssessmentMode Mode { get; } = mode;
        /// <summary>Set by a step for the view it returns (cleared on every step).</summary>
        public string? Notice { get; set; }
        public SourceExamEngine Engine { get; set; } = engine;
        public Guid OrganizationId { get; } = organizationId;
        /// <summary>The session's owner: a cached engine is only handed to them (GAD-T74).</summary>
        public Guid StudentUserId { get; } = studentUserId;
        public bool IsFor(Guid org, Guid student) => OrganizationId == org && StudentUserId == student;
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private sealed record SubjectSnapshot(ConceptMap Map, IReadOnlyList<ConceptRubric> Rubrics);

    private readonly ConcurrentDictionary<Guid, Live> _live = new();
    private readonly ResponseVerifier _verifier = new(panel);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _loadGates = new();
    private readonly ConcurrentDictionary<(Guid Collection, int Version), SourceMaterialSubject> _subjects = new();

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>How late an answer may arrive after time runs out and still be judged (network and typing lag).</summary>
    public static readonly TimeSpan AnswerGrace = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Starts the student's next attempt at an assignment, or returns the attempt already in
    /// progress (one active session per assignment).
    /// </summary>
    public async Task<Guid> CreateOrResumeAsync(Guid organizationId, Guid assignmentId, Guid studentUserId, string? clientInfo, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var assignment = await db.ExamAssignments.Include(a => a.Exam)
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.OrganizationId == organizationId && a.StudentUserId == studentUserId, ct)
            ?? throw new SessionRefusedException("That exam is not assigned to you.");
        var exam = assignment.Exam!;
        // An attempt already in progress can always be resumed, even if the exam was archived meanwhile.
        var inProgress = await db.ExamSessions.Where(s => s.AssignmentId == assignmentId && s.OrganizationId == organizationId
                && s.State != SessionState.Ended && s.State != SessionState.Failed)
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (inProgress is { } resume) return resume;
        if (exam.Status != ExamStatus.Published) throw new SessionRefusedException(
            exam.Status == ExamStatus.Archived ? "That exam is closed to new attempts." : "That exam is not open yet.");
        var now = clock.GetUtcNow();
        if (assignment.OpensAt is { } opens && now < opens) throw new SessionRefusedException($"That exam opens at {opens:u}.");
        if (assignment.ClosesAt is { } closes && now > closes) throw new SessionRefusedException("That exam has closed.");

        var sessions = await db.ExamSessions.Where(s => s.AssignmentId == assignmentId && s.OrganizationId == organizationId)
            .OrderBy(s => s.Attempt).ToListAsync(ct);
        // A session ended because the prompt set changed under it is not a used attempt (GAD-T235).
        var usedAttempts = sessions.Count(s => s.EndReason != nameof(EndReason.PromptSetChanged));
        var active = sessions.FirstOrDefault(s => s.State is not (SessionState.Ended or SessionState.Failed));
        if (active is not null) return active.Id;
        if (usedAttempts >= assignment.AttemptsAllowed) throw new SessionRefusedException("You have used every attempt for this exam.");
        var (spent, quota) = await meter.MonthAsync(db, organizationId, ct);
        if (quota is { } q && spent >= q)
            throw new SessionRefusedException("Your organization has reached its monthly usage limit. Please contact your instructor.");

        var collection = await db.SourceCollections.AsNoTracking()
            .FirstAsync(c => c.Id == exam.CollectionId && c.OrganizationId == organizationId, ct);
        // The session records the policy it runs under: objectives, mode and this student's extra time (GAD-T120).
        var policy = await SessionObjectives.ForSessionAsync(db, ReadPolicy(exam.PolicyJson), exam, assignment.TimeMultiplier, ct);
        var session = new ExamSession
        {
            OrganizationId = organizationId,
            AssignmentId = assignmentId,
            ExamId = exam.Id,
            StudentUserId = studentUserId,
            Attempt = (sessions.Count == 0 ? 0 : sessions.Max(s => s.Attempt)) + 1,
            State = SessionState.Created,
            CreatedAt = now,
            PolicyJson = JsonSerializer.Serialize(policy, Json),
            PromptSetSha256 = ExamPrompts.Set.Sha256,
            ConceptMapVersion = collection.ConceptMapVersion,
            ClientInfo = clientInfo is { Length: > 500 } ci ? ci[..500] : clientInfo,
        };
        db.ExamSessions.Add(session);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two starts at once (a double click, a second tab) both insert attempt N and the unique (AssignmentId, Attempt) index
            // refuses the second: that attempt is the winner's, so resume it instead of failing (GAD-T207).
            db.Entry(session).State = EntityState.Detached;
            var winner = await db.ExamSessions.AsNoTracking().Where(s => s.AssignmentId == assignmentId && s.OrganizationId == organizationId
                    && s.State != SessionState.Ended && s.State != SessionState.Failed)
                .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (winner is { } id) return id;
            throw;
        }
        return session.Id;
    }

    /// <summary>Records the student's consent (hash of the exact text shown). Required before the first question.</summary>
    public async Task RecordConsentAsync(Guid organizationId, Guid sessionId, Guid studentUserId, string consentTextSha256, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var row = await Owned(db, organizationId, sessionId, studentUserId, ct);
        if (row.State != SessionState.Created) return;
        row.ConsentRecordedAt = clock.GetUtcNow();
        row.ConsentTextSha256 = consentTextSha256;
        row.State = SessionState.ConsentGiven;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The current question (asking it again after a reconnect), planning a new one if needed.</summary>
    public Task<SessionStep> CurrentAsync(Guid organizationId, Guid sessionId, Guid studentUserId, CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, live => NextAsync(live, ct), ct);

    /// <summary>
    /// The next question, first finishing the judgment of an answer that was recorded but not judged (a step failed between
    /// the two), as <see cref="AnswerAsync"/> recovers: otherwise the engine refuses to plan on and the session is stuck.
    /// </summary>
    private static async Task<Turn?> NextAsync(Live live, CancellationToken ct)
    {
        if (!live.Engine.Session.IsEnded && live.Engine.Session.AwaitingJudgment is not null)
            await live.Engine.SubmitAnswerAsync(null, ct);
        return await live.Engine.NextQuestionAsync(ct);
    }

    /// <summary>
    /// The examiner has begun speaking the first question (GAD-T239): the held exam clock starts now, so the student's time begins
    /// with the talking examiner, not with the click on Begin exam. Once only; a later call (a reload) changes nothing.
    /// </summary>
    public Task<SessionStep> StartClockAsync(Guid organizationId, Guid sessionId, Guid studentUserId, CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, live =>
        {
            var s = live.Engine.Session;
            if (s.StartClock(clock.GetUtcNow())) log.LogInformation("Exam clock started with the examiner's first question.");
            return Task.FromResult(s.IsEnded ? null : s.OpenTurn);
        }, ct);

    /// <summary>
    /// The exam room lost the microphone, the camera or the examiner's connection after the clock started (GAD-T263): the clock stops
    /// until <see cref="ResumeClockAsync"/>, so the pause doesn't cost exam time. The paused time added back is capped per exam
    /// (<see cref="SocraticSession.PauseAllowance"/>), and any answer from the student ends the pause, so a pause reported by the
    /// browser can't buy unlimited time.
    /// </summary>
    public Task<SessionStep> PauseClockAsync(Guid organizationId, Guid sessionId, Guid studentUserId, CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, live =>
        {
            var s = live.Engine.Session;
            if (s.PauseClock(clock.GetUtcNow()))
                log.LogInformation("Exam clock paused for a lost device; {Left} of pause allowance left.", s.PauseAllowanceLeft(clock.GetUtcNow()));
            return Task.FromResult(s.IsEnded ? null : s.OpenTurn);
        }, ct);

    /// <summary>The microphone, camera and examiner are working again (GAD-T263): a paused clock runs again, with the paused time added back.</summary>
    public Task<SessionStep> ResumeClockAsync(Guid organizationId, Guid sessionId, Guid studentUserId, CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, live =>
        {
            var s = live.Engine.Session;
            if (!s.IsHeld) ResumeOnActivity(live);
            return Task.FromResult(s.IsEnded ? null : s.OpenTurn);
        }, ct);

    /// <summary>What the student is told when they answer while staff have paused the exam (GAD-T299).</summary>
    public const string HeldByStaffMessage = "Your exam is paused by your instructor. Your time is stopped; you can answer again once they resume it.";

    /// <summary>
    /// A staff action from the live monitor (GAD-T299): runs <paramref name="act"/> on the running session under its step gate and saves it
    /// like any step. <paramref name="studentUserId"/> is the session's own student (the caller looked it up in their organization).
    /// </summary>
    public Task<SessionStep> StaffStepAsync(Guid organizationId, Guid sessionId, Guid studentUserId, Action<SocraticSession, DateTimeOffset> act,
        CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, live =>
        {
            var s = live.Engine.Session;
            if (s.IsEnded) throw new SessionRefusedException("This exam has already ended.");
            act(s, clock.GetUtcNow());
            return Task.FromResult(s.IsEnded ? null : s.OpenTurn);
        }, ct);

    /// <summary>The student did something only a working exam room can do: an open pause ends now, by the server's clock (GAD-T263).</summary>
    private void ResumeOnActivity(Live live)
    {
        var s = live.Engine.Session;
        if (s.IsHeld) throw new SessionRefusedException(HeldByStaffMessage);
        if (s.ResumeClock(clock.GetUtcNow()))
            log.LogInformation("Exam clock resumed; {Credit} of paused time added back in all.", s.PausedTotal);
    }

    /// <summary>Meaning clarifications per session: beyond this, an unclear answer goes to a human instead of interrupting again.</summary>
    public const int MaxMeaningClarificationsPerSession = 3;

    /// <summary>With less time than this left, the student is not interrupted; an unclear answer goes to a human.</summary>
    public static readonly TimeSpan MinTimeForClarification = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Submits the answer to the waiting question. It is recorded as an immutable response (GAD-T86) and
    /// verified (GAD-T87): a voice answer is taken as heard and judged at once (GAD-T405); an unclear typed
    /// one may get a neutral clarification question (the step then carries <see cref="SessionStep.Verification"/>
    /// and the same question). Otherwise the verified text is judged and the next question returned.
    /// </summary>
    public Task<SessionStep> AnswerAsync(Guid organizationId, Guid sessionId, Guid studentUserId, Answer answer, CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, async live =>
        {
            var s = live.Engine.Session;
            if (s.IsEnded)
                throw new SessionRefusedException("This exam has ended; your answers were submitted.");
            ResumeOnActivity(live);
            if (s.OpenTurn is null && s.AwaitingJudgment is not null)
            {
                // A crash between recording and judging: finish judging what was recorded.
                await live.Engine.SubmitAnswerAsync(null, ct);
                return await live.Engine.NextQuestionAsync(ct);
            }
            var open = s.OpenTurn ?? throw new SessionRefusedException("There is no question waiting for an answer.");
            // The server's clock is the authority: an answer arriving well after time ran out is not judged.
            if (TooLate(live))
            {
                live.Engine.End(EndReason.TimeUp);
                return null;
            }

            using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(live.OrganizationId));
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var ledger = new ResponseLedger(db, clock);
            var turnId = await TurnIdAsync(db, s.Id, open.Index, ct)
                ?? throw new SessionRefusedException("Your question is still being saved; please submit again.");
            if (await ledger.PendingAsync(s.Id, turnId, ct) is not null)
                throw new SessionRefusedException("Please answer the request about your last answer first.");

            // An answer given while the clock is still held (the examiner-speaks signal never arrived) releases it where it stands,
            // counting from when the session started: a later start can't hand out a fresh time budget (GAD-T239).
            if (s.ClockHeld) s.StartClock(s.StartedAt);

            // A resend after a step failed past the capture reuses the original already recorded for this turn (one original per turn);
            // one that already got past verification is judged as it stands.
            var response = await ledger.OriginalForTurnAsync(s.Id, turnId, ct);
            var alreadyVerified = response is not null && response.VerificationState != VerificationState.Captured;
            response ??= await ledger.CaptureAsync(live.OrganizationId, s.Id, turnId, studentUserId, answer, ct: ct);
            // GAD-T58: a spoken answer's speech metrics become reviewable audio annotations (never a score), saved with the answer.
            await Gadfly.Server.Integrity.AudioIntegritySignals.RecordAsync(db, live.OrganizationId, s.Id, turnId, answer, ct);
            await db.SaveChangesAsync(ct);
            if (!alreadyVerified && await VerifyAsync(live, db, ledger, response, open, answer, ct))
            {
                await db.SaveChangesAsync(ct);
                return open;   // the same question, now with a pending request
            }
            await db.SaveChangesAsync(ct);
            await live.Engine.SubmitAnswerAsync(answer with { Text = response.VerifiedText }, ct);
            return await live.Engine.NextQuestionAsync(ct);
        }, ct);

    /// <summary>
    /// The student's reply to a meaning clarification (Part 4.3 Type B). The reply is stored beside the
    /// answer (never over it) and judged with it; an empty reply declines, which leaves the ambiguity
    /// unresolved and sends the answer to a human reviewer.
    /// </summary>
    public Task<SessionStep> ClarifyAsync(Guid organizationId, Guid sessionId, Guid studentUserId, Guid responseId, string? reply, CancellationToken ct = default) =>
        ResolveAsync(organizationId, sessionId, studentUserId, responseId, ClarificationType.Meaning, (live, ledger, p) =>
        {
            var text = (reply ?? "").Trim();
            if (text.Length > 5000) throw new SessionRefusedException("That clarification is too long.");
            if (text.Length == 0 || ResponseVerifier.IsAporiaWord(text))
            {
                ledger.Answer(p.Request!, p.Response, text.Length == 0 ? null : text, studentUserId, "The student did not clarify.");
                ledger.MovePath(p.Response, "The student chose not to clarify; the ambiguity goes to a human reviewer.", studentUserId, OrgRoles.Student,
                    VerificationState.Unresolved, VerificationState.FlaggedForHumanReview);
                live.Notice = "No problem. Your answer was kept exactly as you wrote it.";
                return Task.FromResult(Compose(p.Response.VerifiedText, null, null));
            }
            ledger.Answer(p.Request!, p.Response, text, studentUserId, "The student clarified their meaning.");
            ledger.AddVersion(p.Response, text, CorrectionKind.MeaningClarification, "student", studentUserId);
            ledger.Move(p.Response, VerificationState.Clarified, "The student clarified their meaning.", studentUserId, OrgRoles.Student);
            live.Notice = "Thanks. Your clarification was saved alongside your answer.";
            return Task.FromResult(Compose(p.Response.VerifiedText, p.Request!.Question, text));
        }, ct);

    /// <summary>
    /// "Yes, this is what I said" (Part 4.3 Type A). Confirms the wording only, never that the answer is correct. No read-back request
    /// is created any more (GAD-T405, spoken answers are taken as heard); this only resolves one left over from before that change,
    /// which the exam room does on its own, so an exam already in progress is never stranded.
    /// </summary>
    public Task<SessionStep> ConfirmTranscriptAsync(Guid organizationId, Guid sessionId, Guid studentUserId, Guid responseId, CancellationToken ct = default) =>
        ResolveAsync(organizationId, sessionId, studentUserId, responseId, ClarificationType.Transcription, (live, ledger, p) =>
        {
            ledger.Answer(p.Request!, p.Response, "confirmed", studentUserId, "The student confirmed the recording matches what they said.");
            ledger.Move(p.Response, VerificationState.Confirmed,
                "The student confirmed the transcript. This confirms the wording only, not that the answer is correct.", studentUserId, OrgRoles.Student);
            return Task.FromResult(Compose(p.Response.VerifiedText, null, null));
        }, ct);

    /// <summary>
    /// A typo fix to a typed answer (Part 4.7, GAD-T260): while a meaning clarification is pending (fix a typo
    /// instead of clarifying), or afterwards on the last judged answer ("fix a typo in my last answer"). Adds a
    /// transcript version and updates the verified text; the original is kept and the score is never reduced
    /// for it. A spoken answer is kept as it was heard (GAD-T405): it is never corrected or said again.
    /// </summary>
    public Task<SessionStep> CorrectAnswerAsync(Guid organizationId, Guid sessionId, Guid studentUserId, string corrected,
        CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, async live =>
        {
            var s = live.Engine.Session;
            if (s.IsEnded) throw new SessionRefusedException("This exam has ended; your answers were submitted.");
            ResumeOnActivity(live);
            var text = (corrected ?? "").Trim();
            if (text.Length == 0 || text.Length > 20_000) throw new SessionRefusedException("The corrected answer needs text.");
            // A step failed between recording and judging: finish judging what was recorded first (it is the answer on record).
            if (s.OpenTurn is null && s.AwaitingJudgment is not null) return await NextAsync(live, ct);
            using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(live.OrganizationId));
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var ledger = new ResponseLedger(db, clock);

            // 1. A request is pending on the open question: the correction resolves it, then the answer is judged.
            if (s.OpenTurn is { } open && await TurnIdAsync(db, s.Id, open.Index, ct) is { } openId
                && await ledger.PendingAsync(s.Id, openId, ct) is { } p)
            {
                // A request left over from before GAD-T405 on a spoken answer: it is taken as heard, never corrected.
                if (p.Response.Modality == "voice")
                    throw new SessionRefusedException("A spoken answer is kept as it was heard; it can't be corrected.");
                if (CorrectionRules.Check(p.Response.VerifiedText, text, voice: false) is { Allowed: false } refused)
                    throw new SessionRefusedException(refused.Reason);
                if (TooLate(live)) return await ExpireAsync(live, db, ledger, p, open, ct);
                if (p.Request is { } request)
                    ledger.Answer(request, p.Response, request.Type == ClarificationType.Transcription ? text : null, studentUserId,
                        request.Type == ClarificationType.Transcription
                            ? "The student corrected the transcript."
                            : "The student fixed a typo instead of clarifying.");
                ledger.AddVersion(p.Response, text, CorrectionKind.Transcription, "student", studentUserId);
                ledger.Move(p.Response, VerificationState.Corrected, "Transcription correction by the student.", studentUserId, OrgRoles.Student);
                await db.SaveChangesAsync(ct);
                live.Notice = "Your correction was saved. Your original answer is kept on record too.";
                await JudgePendingAsync(live, ledger, p.Response, Compose(p.Response.VerifiedText, null, null), ct);
                return await live.Engine.NextQuestionAsync(ct);
            }

            // 2. The last judged answer: a typo fix after the fact (verified text for grading; the live judgment stays on version 1).
            var (response, _) = await LastJudgedAsync(live, db, ledger, ct);
            if (response.Modality == "voice")
                throw new SessionRefusedException("A spoken answer is kept as it was heard; it can't be corrected afterwards.");
            if (response.VerificationState != VerificationState.Captured)
                throw new SessionRefusedException("That answer was already confirmed or clarified, so it can't be edited. Use \"I want to change my answer\" instead.");
            if (CorrectionRules.Check(response.VerifiedText, text, voice: response.Modality == "voice") is { Allowed: false } no)
                throw new SessionRefusedException(no.Reason);
            ledger.MovePath(response, "The student asked to correct a typo in their answer.", studentUserId, OrgRoles.Student,
                VerificationState.VerificationRequired, VerificationState.AwaitingStudentConfirmation);
            ledger.AddVersion(response, text, CorrectionKind.Transcription, "student", studentUserId);
            ledger.Move(response, VerificationState.Corrected,
                "Transcription correction after judging: graders use the corrected text; the live examiner saw version 1. Never a score penalty.",
                studentUserId, OrgRoles.Student);
            await db.SaveChangesAsync(ct);
            live.Notice = "Your typo fix was saved. Your original answer is kept on record too.";
            return await live.Engine.NextQuestionAsync(ct);
        }, ct);

    /// <summary>
    /// "I want to change my answer" (Part 4.7, Scenario E): a substantive revision of the last judged answer,
    /// recorded as a new response event that points at what it revises. The original and its judgment are
    /// untouched; the reviewer sees both and applies the exam's rule (<see cref="ResponseLedger.RevisionRule"/>).
    /// </summary>
    public Task<SessionStep> ReviseAnswerAsync(Guid organizationId, Guid sessionId, Guid studentUserId, string revised, CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, async live =>
        {
            var s = live.Engine.Session;
            if (s.IsEnded) throw new SessionRefusedException("This exam has ended; your answers were submitted.");
            ResumeOnActivity(live);
            var text = (revised ?? "").Trim();
            if (text.Length == 0 || text.Length > 20_000) throw new SessionRefusedException("Your changed answer needs text.");
            // A step failed between recording and judging: finish judging that answer before anything can be changed.
            if (s.OpenTurn is null && s.AwaitingJudgment is not null) return await NextAsync(live, ct);
            if (TooLate(live)) throw new SessionRefusedException("Time is up, so answers can no longer be changed.");
            using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(live.OrganizationId));
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var ledger = new ResponseLedger(db, clock);
            if (s.OpenTurn is { } open && await TurnIdAsync(db, s.Id, open.Index, ct) is { } openId && await ledger.PendingAsync(s.Id, openId, ct) is not null)
                throw new SessionRefusedException("Please answer the request about your last answer first.");
            var (original, turnId) = await LastJudgedAsync(live, db, ledger, ct);
            // No do-over for a spoken answer (GAD-T407, owner decision 2026-11-22): it was taken as heard, and a second, considered pass
            // would change the Socratic exchange. A typed answer (GAD-T260) can still be changed.
            if (original.Modality == "voice")
                throw new SessionRefusedException("A spoken answer is kept as it was heard; it can't be changed afterwards.");
            var revisions = await db.StudentResponses.Where(r => r.TurnId == turnId && r.Kind == ResponseKind.SubstantiveRevision)
                .OrderBy(r => r.Sequence).ToListAsync(ct);
            if (revisions.Count >= ResponseLedger.MaxRevisionsPerTurn)
                throw new SessionRefusedException($"You've already changed this answer {revisions.Count} times.");
            var latest = revisions.LastOrDefault() ?? original;
            if (string.Equals(latest.OriginalText.Trim(), text, StringComparison.Ordinal))
                throw new SessionRefusedException("That is the same as your answer on record.");
            await ledger.CaptureAsync(live.OrganizationId, s.Id, turnId, studentUserId,
                new Answer { Text = text, Mode = "text", CompletedAt = clock.GetUtcNow() }, ResponseKind.SubstantiveRevision, latest.Id, ct);
            await db.SaveChangesAsync(ct);
            live.Notice = "Your changed answer was recorded as a new answer. Your original stays on record and your instructor sees both.";
            return await live.Engine.NextQuestionAsync(ct);
        }, ct);

    /// <summary>Ends a session early (student finished early, time ran out, or the host stopped it).</summary>
    public Task<SessionStep> EndAsync(Guid organizationId, Guid sessionId, Guid studentUserId, EndReason reason, CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, async live =>
        {
            // The server's clock decides when time is up: an exam room counting down an older deadline (staff added time or paused the
            // exam since its last step, GAD-T299) gets the open question back with the real time left instead of an ended exam.
            if (reason == EndReason.TimeUp && !live.Engine.Session.IsEnded && live.Engine.Session.OpenTurn is { } stillOpen
                && live.Engine.Session.Remaining(clock.GetUtcNow()) > TimeSpan.FromSeconds(5)) return stillOpen;
            // An answer waiting on a clarification was given in time: it is judged as recorded and flagged for a human.
            if (!live.Engine.Session.IsEnded && live.Engine.Session.OpenTurn is { } open)
            {
                using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(live.OrganizationId));
                var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                var ledger = new ResponseLedger(db, clock);
                if (await TurnIdAsync(db, live.Engine.Session.Id, open.Index, ct) is { } openId)
                {
                    if (await ledger.PendingAsync(live.Engine.Session.Id, openId, ct) is { } p)
                        await ExpireAsync(live, db, ledger, p, open, ct, ended: true);
                    // An answer saved for the open question whose step then failed (verification or judging threw) and was never resent:
                    // it was given in time, so it is attached to its turn and judged as recorded, flagged for a human because it was never
                    // checked with the student (GAD-T323). A panel still failing never stops the student finishing.
                    else if (await ledger.OriginalForTurnAsync(live.Engine.Session.Id, openId, ct) is { } orphan)
                        await JudgeOrphanOnEndAsync(live, db, ledger, orphan, ct);
                }
            }
            // An answer recorded but not judged (a step failed between the two) was given in time too: judge it before ending, as every
            // other step recovers it, so it doesn't stay in the report unjudged. A panel still failing never stops the student finishing.
            else if (!live.Engine.Session.IsEnded && live.Engine.Session.AwaitingJudgment is not null)
            {
                try { await live.Engine.SubmitAnswerAsync(null, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "The last answer could not be judged before the exam ended; it is kept for a human to grade.");
                }
            }
            live.Engine.End(reason);
            return null;
        }, ct);

    /// <summary>The exam ends with an answer saved for the open question but never judged (GAD-T323): judged as recorded, for a human to review.</summary>
    private async Task JudgeOrphanOnEndAsync(Live live, GadflyDbContext db, ResponseLedger ledger, StudentResponse orphan, CancellationToken ct)
    {
        if (orphan.VerificationState == VerificationState.Captured)
        {
            ledger.MovePath(orphan, "The exam ended before this answer could be checked with the student; a human reviews it as recorded.", null, "System",
                VerificationState.VerificationRequired, VerificationState.FlaggedForHumanReview);
            await db.SaveChangesAsync(ct);
        }
        try { await JudgePendingAsync(live, ledger, orphan, Compose(orphan.VerifiedText, null, null), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "The last answer could not be judged before the exam ended; it is kept for a human to grade.");
        }
    }

    // ------------------------------------------------------------------ verification flow (GAD-T86..T88)

    private bool TooLate(Live live)
    {
        var s = live.Engine.Session;
        var now = clock.GetUtcNow();
        // The deadline moves with the paused time added back (GAD-T263) and with staff extra time and pauses (GAD-T299).
        return s.Remaining(now) <= TimeSpan.Zero && now - s.Deadline(now) > AnswerGrace;
    }

    private static Task<Guid?> TurnIdAsync(GadflyDbContext db, Guid sessionId, int index, CancellationToken ct) =>
        db.SessionTurns.Where(t => t.SessionId == sessionId && t.Index == index).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);

    /// <summary>The original response of the most recent judged turn (the one "my last answer" means).</summary>
    private static async Task<(StudentResponse Original, Guid TurnId)> LastJudgedAsync(Live live, GadflyDbContext db, ResponseLedger ledger, CancellationToken ct)
    {
        var s = live.Engine.Session;
        var judged = s.Turns.LastOrDefault(t => t.Judgment is not null) ?? throw new SessionRefusedException("You haven't answered a question yet.");
        var turnId = await TurnIdAsync(db, s.Id, judged.Index, ct) ?? throw new SessionRefusedException("Your last answer is still being saved; try again.");
        var original = await ledger.OriginalForTurnAsync(s.Id, turnId, ct) ?? throw new SessionRefusedException("Your last answer can't be changed here.");
        return (original, turnId);
    }

    /// <summary>
    /// Decides whether the answer needs the student before judging. Returns true when a request is now
    /// pending (the response is AwaitingStudentConfirmation with an open ClarificationEvent).
    /// </summary>
    private async Task<bool> VerifyAsync(Live live, GadflyDbContext db, ResponseLedger ledger, StudentResponse response, Turn open, Answer answer, CancellationToken ct)
    {
        var s = live.Engine.Session;
        var text = response.VerifiedText;
        if (text.Length == 0 || ResponseVerifier.IsAporiaWord(text)) return false;
        var concept = live.Engine.Subject.Concept(open.ConceptId);
        var input = new VerificationInput
        {
            Question = open.Question, AnswerText = text, Modality = response.Modality, SpeechConfidence = response.SpeechRecognitionConfidence,
            Words = answer.Words, ConceptTitle = concept.Title,
        };
        var hints = ResponseVerifier.Hints(input);

        // A spoken answer is taken as heard and judged at once (GAD-T405, owner decision 2026-11-22): no read-back, no confirm,
        // correct or clarify round trip, since a second considered pass changes the Socratic exchange. The recognizer's doubts
        // (low word confidence, its own advice to check) are recorded for reviewers; the uncertain words stay on the capture (GAD-T265).
        if (response.Modality == "voice")
        {
            var low = hints.FirstOrDefault(h => h.Trigger == ClarificationTrigger.LowSpeechConfidence);
            var advised = answer.SpeechMetrics.TryGetValue("verification.recommended", out var rec) && rec == 1;
            ledger.Audit(live.OrganizationId, null, "response.verification.checked", response, s.Id, null,
                JsonSerializer.Serialize(new { takenAsHeard = true, lowSpeechConfidence = low is not null, recognizerAdvisedCheck = advised }),
                "Spoken answer taken as heard; not read back to the student." + (low is null ? "" : " " + low.Reason)
                    + (advised ? " The recognizer advised checking this transcript." : ""), "System");
            return false;
        }

        // Part 4.5: no interruption when there is no time for it; an unclear answer then goes to a human.
        var shortOnTime = s.Remaining(clock.GetUtcNow()) < MinTimeForClarification;
        var budgetSpent = await ledger.MeaningClarificationsAsync(s.Id, ct) >= MaxMeaningClarificationsPerSession;
        var decision = await _verifier.DecideAsync(input with { Grounding = (await live.Engine.Subject.GetGroundingAsync(concept.Key, text, ct)).Text }, ct);
        ledger.Audit(live.OrganizationId, null, "response.verification.checked", response, s.Id, null,
            JsonSerializer.Serialize(new
            {
                decision.NeedsClarification, trigger = ResponseVerifier.TriggerName(decision.Trigger), decision.Phrase, decision.Voters,
                hints = hints.Select(h => ResponseVerifier.TriggerName(h.Trigger)),
            }), decision.Reason, "System");
        if (!decision.NeedsClarification) return false;
        if (shortOnTime || budgetSpent)
        {
            ledger.MovePath(response, $"{ResponseVerifier.TriggerName(decision.Trigger)}: {decision.Reason} Not asked ({(shortOnTime ? "too little time left" : "clarification limit for this exam reached")}); a human reviews it.",
                null, "System", VerificationState.VerificationRequired, VerificationState.FlaggedForHumanReview);
            return false;
        }
        ledger.MovePath(response, $"{ResponseVerifier.TriggerName(decision.Trigger)}: {decision.Reason}", null, "System",
            VerificationState.VerificationRequired, VerificationState.AwaitingStudentConfirmation);
        var question = await _verifier.WordAsync(input, decision, live.Engine.Subject.Rubric(concept.Key).Describe(), Dialogue(s, open, text), ct);
        ledger.Ask(response, ClarificationType.Meaning, question.Text, question.NonLeadingChecked, $"{question.Outcome}; {decision.Reason}");
        await CheckClarificationLeakAsync(live, open, question, ct);
        return true;
    }

    /// <summary>
    /// A clarification shown to the student may itself give the answer away (Part 4.4, GAD-T92): a drafted one goes
    /// through the engine's leak guard, which flags the turn when it leaks or cannot decide, so answers on that
    /// objective from then on count as contaminated, never as independent evidence. One the verification guard
    /// did not clear is flagged outright. The fixed template only quotes the student's own words, so it is not checked.
    /// </summary>
    private async Task CheckClarificationLeakAsync(Live live, Turn open, ClarificationQuestion question, CancellationToken ct)
    {
        if (!question.NonLeadingChecked)
        {
            live.Engine.FlagLeak(open.Index, LeakSource.GuardFailedOpen, $"The clarification was shown without passing the leak checks ({question.Outcome}).");
            return;
        }
        if (question.Outcome != "drafted") return;
        try
        {
            await live.Engine.CheckClarificationAsync(open.Index, question.Text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "The clarification leak check failed; the turn is flagged as possibly leaked.");
            live.Engine.FlagLeak(open.Index, LeakSource.GuardFailedOpen, "The leak guard could not run on a clarification already shown to the student.");
        }
    }

    /// <summary>Loads and checks the pending request, applies <paramref name="resolve"/> (which returns the text to judge), then judges and moves on.</summary>
    private Task<SessionStep> ResolveAsync(Guid organizationId, Guid sessionId, Guid studentUserId, Guid responseId, ClarificationType type,
        Func<Live, ResponseLedger, (StudentResponse Response, ClarificationEvent? Request), Task<string>> resolve, CancellationToken ct) =>
        StepAsync(organizationId, sessionId, studentUserId, async live =>
        {
            var s = live.Engine.Session;
            if (s.IsEnded) throw new SessionRefusedException("This exam has ended; your answers were submitted.");
            ResumeOnActivity(live);
            // A step failed between recording and judging: the request was already resolved, so finish judging and move on.
            if (s.OpenTurn is null && s.AwaitingJudgment is not null) return await NextAsync(live, ct);
            var open = s.OpenTurn ?? throw new SessionRefusedException("There is nothing waiting for you to confirm.");
            using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(live.OrganizationId));
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var ledger = new ResponseLedger(db, clock);
            var p = await TurnIdAsync(db, s.Id, open.Index, ct) is { } openId ? await ledger.PendingAsync(s.Id, openId, ct) : null;
            if (p is not { Request: { } request } pending || pending.Response.Id != responseId || request.Type != type)
                throw new SessionRefusedException("That request has already been answered.");
            if (TooLate(live)) return await ExpireAsync(live, db, ledger, pending, open, ct);
            var judgeText = await resolve(live, ledger, pending);
            await db.SaveChangesAsync(ct);
            await JudgePendingAsync(live, ledger, pending.Response, judgeText, ct);
            return await live.Engine.NextQuestionAsync(ct);
        }, ct);

    /// <summary>Time ran out (or the exam ended) with a request open: the answer is judged as recorded and flagged for a human.</summary>
    private async Task<Turn?> ExpireAsync(Live live, GadflyDbContext db, ResponseLedger ledger, (StudentResponse Response, ClarificationEvent? Request) p,
        Turn open, CancellationToken ct, bool ended = false)
    {
        if (p.Request is { } request) ledger.Answer(request, p.Response, null, live.StudentUserId, "The exam ended before the student replied.");
        ledger.MovePath(p.Response, "The exam ended before the student replied; a human reviews the answer as recorded.", null, "System",
            VerificationState.Unresolved, VerificationState.FlaggedForHumanReview);
        await db.SaveChangesAsync(ct);
        await JudgePendingAsync(live, ledger, p.Response, Compose(p.Response.VerifiedText, null, null), ct);
        if (!ended) live.Engine.End(EndReason.TimeUp);
        return null;
    }

    /// <summary>Records the pending answer in the engine (with its capture timings) and judges it.</summary>
    private async Task JudgePendingAsync(Live live, ResponseLedger ledger, StudentResponse response, string text, CancellationToken ct)
    {
        var capture = await ledger.CaptureOfAsync(response.Id, ct);
        await live.Engine.SubmitAnswerAsync(new Answer
        {
            Text = text,
            Mode = response.Modality,
            StartedAt = capture?.StartedAt,
            CompletedAt = capture?.CompletedAt ?? response.SubmittedAt,
            Words = capture?.Words ?? [],
            SpeechMetrics = capture?.SpeechMetrics ?? new Dictionary<string, double>(),
        }, ct);
    }

    /// <summary>The text the judge sees: the verified answer, plus the clarification exchange when there was one.</summary>
    private static string Compose(string verified, string? clarificationQuestion, string? reply) =>
        reply is null ? verified
            : $"{verified}\n\n(The examiner asked a neutral clarification question: \"{clarificationQuestion}\" The student's clarification: {reply})";

    private static string Dialogue(SocraticSession s, Turn open, string answer) =>
        string.Join("\n", s.Turns.TakeLast(6).SelectMany(t => t.Answer is null
            ? new[] { $"Examiner: {t.Question}", t.Index == open.Index ? $"Student: {ExamPrompts.Student(answer)}" : "" }
            : new[] { $"Examiner: {t.Question}", $"Student: {ExamPrompts.Student(t.Answer.Text)}" }).Where(l => l.Length > 0));

    private async Task<SessionStep> StepAsync(Guid organizationId, Guid sessionId, Guid studentUserId, Func<Live, Task<Turn?>> step, CancellationToken ct)
    {
        // Every log line inside a step carries the session and organization (GAD-T62).
        using var scope = log.BeginScope(new Dictionary<string, object> { ["SessionId"] = sessionId, ["OrganizationId"] = organizationId });
        var started = clock.GetTimestamp();
        var live = await GetLiveAsync(organizationId, sessionId, studentUserId, ct);
        await live.Gate.WaitAsync(ct);
        try
        {
            Turn? question;
            live.Notice = null;
            using var usage = Gadfly.Socrates.PanelUsage.Track(out var calls);
            try
            {
                question = await step(live);
            }
            finally
            {
                // Persist whatever happened, even if a panel call failed midway: the transcript is the resume point.
                // Not the caller's token: an aborted connection must not skip saving what the step already did.
                await PersistAsync(live, studentUserId, calls, CancellationToken.None);
            }
            var view = await ViewAsync(live, question, ct);
            Admin.LiveSessionFeed.Shared.Stepped(live.OrganizationId, live.StudentUserId, view, clock.GetUtcNow());   // live monitor (GAD-T299)
            log.LogInformation("Session step: turn {Turn}, {Move}, state {State}, {Elapsed} ms.",
                view.TurnIndex, view.Move ?? "-", view.State, (long)clock.GetElapsedTime(started).TotalMilliseconds);
            return view;
        }
        catch (DbUpdateConcurrencyException)
        {
            _live.TryRemove(sessionId, out _);
            throw new SessionRefusedException("This exam was advanced from another window; reload to continue.");
        }
        finally
        {
            live.Gate.Release();
        }
    }

    private async Task<SessionStep> ViewAsync(Live live, Turn? question, CancellationToken ct)
    {
        var s = live.Engine.Session;
        var objectives = live.Engine.Assessments();
        PendingVerification? pending = null;
        LastAnswer? last = null;
        using (var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(live.OrganizationId)))
        {
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var ledger = new ResponseLedger(db, clock);
            if (!s.IsEnded && question is not null && s.OpenTurn is { } open && open.Index == question.Index
                && await TurnIdAsync(db, s.Id, open.Index, ct) is { } openId
                && await ledger.PendingAsync(s.Id, openId, ct) is { Request: { } request } p)
            {
                pending = new PendingVerification(p.Response.Id,
                    request.Type == ClarificationType.Transcription ? ResponseLedger.TranscriptKind : ResponseLedger.MeaningKind,
                    request.Question, p.Response.VerifiedText);
            }
            if (!s.IsEnded && s.Turns.LastOrDefault(t => t.Judgment is not null) is { } judged
                && await TurnIdAsync(db, s.Id, judged.Index, ct) is { } judgedId
                && await ledger.OriginalForTurnAsync(s.Id, judgedId, ct) is { } original)
            {
                var revisions = await db.StudentResponses.Where(r => r.TurnId == judgedId && r.Kind == ResponseKind.SubstantiveRevision)
                    .OrderBy(r => r.Sequence).Select(r => r.OriginalText).ToListAsync(ct);
                last = new LastAnswer(original.Id, judged.Index, judged.Question, revisions.LastOrDefault() ?? original.VerifiedText,
                    // A spoken answer is taken as heard (GAD-T405): no after-the-fact transcript fix is offered for it.
                    original.Versions.Count, original.VerificationState == VerificationState.Captured && pending is null && original.Modality != "voice",
                    // Nor a changed answer (GAD-T407): no do-over for what was said.
                    pending is null && revisions.Count < ResponseLedger.MaxRevisionsPerTurn && original.Modality != "voice", revisions.Count,
                    ResponseLedger.RevisionRule(live.Mode));
            }
        }
        return new SessionStep
        {
            SessionId = s.Id,
            State = s.IsEnded ? SessionState.Ended : SessionState.Running,
            Question = question?.Question,
            TurnIndex = question?.Index,
            ConceptTitle = question is null ? null : live.Engine.Subject.Concept(question.ConceptId).Title,
            Move = question?.Move.ToString(),
            Remaining = s.Remaining(clock.GetUtcNow()),
            EndReason = s.IsEnded ? s.EndReason.ToString() : null,
            // Progress is per learning objective (one per concept when the exam defines none).
            ConceptsTotal = objectives.Count,
            ConceptsClosed = objectives.Count(o => o.IsClosed),
            Verification = pending,
            LastAnswer = last,
            Notice = live.Notice,
            ClockStarted = !s.ClockHeld,
            ClockPaused = s.IsPaused && !s.IsEnded,
            PauseAllowanceLeft = s.PauseAllowanceLeft(clock.GetUtcNow()),
            HeldByStaff = s.IsHeld && !s.IsEnded,
        };
    }

    private async Task<Live> GetLiveAsync(Guid organizationId, Guid sessionId, Guid studentUserId, CancellationToken ct)
    {
        if (_live.TryGetValue(sessionId, out var cached) && cached.IsFor(organizationId, studentUserId)) return cached;
        var gate = _loadGates.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_live.TryGetValue(sessionId, out cached) && cached.IsFor(organizationId, studentUserId)) return cached;
            using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(organizationId));
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var row = await Owned(db, organizationId, sessionId, studentUserId, ct);
            if (row.State == SessionState.Created) throw new SessionRefusedException("Please read and accept the exam notice first.");
            if (row.State == SessionState.Failed) throw new SessionRefusedException("This exam session could not continue; ask your instructor to reset it.");

            var exam = await db.Exams.AsNoTracking().FirstAsync(e => e.Id == row.ExamId && e.OrganizationId == organizationId, ct);
            // Pinned subject (GAD-T72): a session runs on the concept map and rubrics it started with.
            SourceMaterialSubject subject;
            if (row.SubjectSnapshotJson is { Length: > 0 } snapshotJson && JsonSerializer.Deserialize<SubjectSnapshot>(snapshotJson, Json) is { } snapshot)
            {
                var passages = await new SqlSourceStore(db, organizationId, Guid.Empty, clock).GetPassagesAsync(exam.CollectionId.ToString(), ct);
                subject = new SourceMaterialSubject(exam.Title, snapshot.Map, snapshot.Rubrics, new GroundingRetriever(passages));
            }
            else
            {
                subject = await SubjectAsync(db, organizationId, exam.CollectionId, exam.Title, ct);
                var rubrics = subject.Map.Concepts.Select(c => subject.Rubric(c.Key)).ToList();
                await db.ExamSessions.Where(x => x.Id == sessionId)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.SubjectSnapshotJson, JsonSerializer.Serialize(new SubjectSnapshot(subject.Map, rubrics), Json)), ct);
            }

            SourceExamEngine engine;
            if (row.TranscriptJson is { Length: > 0 } transcript)
            {
                var session = SessionTranscriptSerializer.Deserialize(transcript);
                if (session.PromptSetSha256 != subject.PromptSet.Sha256)
                {
                    // An attempt that already ended keeps its own end reason and end time: it can't resume, and it isn't rewritten.
                    if (row.State == SessionState.Ended)
                        throw new SessionRefusedException("This exam has ended; your answers were submitted.");
                    // Started under older wording: it can never resume. End it cleanly (not a used attempt) so the student can start afresh (GAD-T235).
                    var endedAt = clock.GetUtcNow();
                    await db.ExamSessions.Where(x => x.Id == sessionId && x.OrganizationId == organizationId
                            && x.State != SessionState.Ended && x.State != SessionState.Failed)
                        .ExecuteUpdateAsync(u => u.SetProperty(x => x.State, SessionState.Ended)
                            .SetProperty(x => x.EndReason, nameof(EndReason.PromptSetChanged))
                            .SetProperty(x => x.EndedAt, endedAt), ct);
                    log.LogWarning("Ended exam session {SessionId}: it ran prompt set {Old}, this build has {New}.",
                        sessionId, session.PromptSetSha256[..12], subject.PromptSet.Sha256[..12]);
                    throw new SessionRefusedException("The exam was updated while your attempt was open, so that attempt could not continue and does not count against your attempts. Please start the exam again.");
                }
                engine = await SourceExamEngine.ResumeAsync(panel, subject, session, ClaimLedger.FromJson(row.LedgerJson), clock, ct);
                log.LogInformation("Resumed exam session {SessionId} at turn {Turn}.", sessionId, session.Turns.Count);
            }
            else
            {
                // Objectives, mode and extra time (GAD-T120); a session created before they were recorded gets them now.
                var multiplier = await db.ExamAssignments.Where(a => a.Id == row.AssignmentId && a.OrganizationId == organizationId)
                    .Select(a => (decimal?)a.TimeMultiplier).FirstOrDefaultAsync(ct);
                var policy = await SessionObjectives.ForSessionAsync(db, ReadPolicy(row.PolicyJson), exam, multiplier, ct);
                if (policy.ConceptIds.Count == 0)
                    policy = policy with { ConceptIds = subject.Map.Concepts.Select(c => c.Key).ToList() };
                engine = await SourceExamEngine.StartAsync(panel, subject, policy, clock, sessionId, ct);
                // Voice and camera are mandatory (GAD-T239): the clock waits for the examiner to begin the first question.
                engine.Session.HoldClock();
                var recorded = JsonSerializer.Serialize(policy, Json);
                if (recorded != row.PolicyJson)
                    await db.ExamSessions.Where(x => x.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(x => x.PolicyJson, recorded), ct);
            }
            // The answer-text signal is an integrity signal: off when the exam's integrity level is Off.
            engine.AssessTextStyle = exam.IntegrityLevel != IntegrityLevel.Off;
            var live = new Live(engine, organizationId, row.StudentUserId, exam.AssessmentMode);
            _live[sessionId] = live;
            return live;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SourceMaterialSubject> SubjectAsync(GadflyDbContext db, Guid organizationId, Guid collectionId, string title, CancellationToken ct)
    {
        var version = await db.SourceCollections.Where(c => c.Id == collectionId && c.OrganizationId == organizationId)
            .Select(c => c.ConceptMapVersion).FirstAsync(ct);
        if (_subjects.TryGetValue((collectionId, version), out var cached)) return cached;
        var id = collectionId.ToString();
        var maps = new SqlConceptMapStore(db, organizationId);
        var map = await maps.LoadMapAsync(id, ct) ?? throw new SessionRefusedException("This exam's source material is still being prepared.");
        var rubrics = await maps.LoadRubricsAsync(id, ct);
        var passages = await new SqlSourceStore(db, organizationId, Guid.Empty, clock).GetPassagesAsync(id, ct);
        var subject = new SourceMaterialSubject(title, map, rubrics, new GroundingRetriever(passages));
        _subjects[(collectionId, version)] = subject;
        return subject;
    }

    private static SessionPolicy ReadPolicy(string? json) => Exams.ExamService.ReadPolicy(json);

    private static async Task<ExamSession> Owned(GadflyDbContext db, Guid organizationId, Guid sessionId, Guid studentUserId, CancellationToken ct) =>
        await db.ExamSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId, ct)
        ?? throw new SessionRefusedException("That exam session was not found.");

    /// <summary>Writes the engine's state to SQL: the resume point plus the review rows for every turn not yet stored.</summary>
    private async Task PersistAsync(Live live, Guid studentUserId, IReadOnlyList<Gadfly.Socrates.PanelCall> calls, CancellationToken ct)
    {
        var org = live.OrganizationId;
        using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(org));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var s = live.Engine.Session;
        var row = await Owned(db, org, s.Id, studentUserId, ct);
        var now = clock.GetUtcNow();

        meter.Stage(db, org, s.Id, calls, "exam-step");   // GAD-T47: estimated LLM usage of this step
        row.TranscriptJson = SessionTranscriptSerializer.Serialize(s);
        row.LedgerJson = live.Engine.Ledger.ToJson();
        row.StartedAt = s.StartedAt;   // moves once, when a held clock starts (GAD-T239)
        row.State = s.IsEnded ? SessionState.Ended : s.IsHeld ? SessionState.Paused : SessionState.Running;   // Paused: by staff (GAD-T299)
        row.EndedAt = s.EndedAt;
        row.EndReason = s.IsEnded ? s.EndReason.ToString() : null;

        var stored = await db.SessionTurns.Where(t => t.SessionId == s.Id).ToDictionaryAsync(t => t.Index, ct);
        var judgedIds = (await db.Judgments.Where(j => stored.Values.Select(t => t.Id).Contains(j.TurnId)).Select(j => j.TurnId).ToListAsync(ct)).ToHashSet();
        foreach (var turn in s.Turns)
        {
            if (!stored.TryGetValue(turn.Index, out var t))
            {
                t = new SessionTurn { OrganizationId = org, SessionId = s.Id, Index = turn.Index };
                db.SessionTurns.Add(t);
                stored[turn.Index] = t;
            }
            t.ConceptKey = turn.ConceptId;
            t.Move = turn.Move.ToString();
            t.Question = turn.Question;
            t.AskedAt = turn.AskedAt;
            t.TargetClaimIdsJson = JsonSerializer.Serialize(turn.TargetClaimIds);
            t.PlannerReason = string.IsNullOrEmpty(turn.Reason) ? null : turn.Reason;
            if (turn.Answer is { } a)
            {
                t.AnswerText = a.Text;
                t.AnswerMode = a.Mode;
                t.AnswerStartedAt = a.StartedAt;
                t.AnswerCompletedAt = a.CompletedAt;
                t.WordsJson = a.Words.Count == 0 ? null : JsonSerializer.Serialize(a.Words, Json);
                t.SpeechMetricsJson = a.SpeechMetrics.Count == 0 ? null : JsonSerializer.Serialize(a.SpeechMetrics, Json);
            }
            t.MasteryAfter = turn.MasteryAfter?.Estimate;
            if (turn.Judgment is { } j && !judgedIds.Contains(t.Id))
            {
                db.Judgments.Add(new Judgment
                {
                    OrganizationId = org,
                    TurnId = t.Id,
                    OverallScore = j.OverallScore,
                    Agreement = j.Agreement,
                    NeedsHumanReview = j.NeedsHumanReview,
                    RubricVersion = j.RubricVersion,
                    Summary = j.Summary,
                    CriteriaJson = JsonSerializer.Serialize(j.Criteria, Json),
                    TextStyleScore = j.TextStyle?.Score,
                    CreatedAt = now,
                    Votes = j.Votes.Select(v => new JudgeVoteRecord
                    {
                        OrganizationId = org, ProviderId = v.ProviderId, Model = v.Model, OverallScore = v.OverallScore,
                        Verdict = v.Verdict.Length > 2000 ? v.Verdict[..2000] : v.Verdict,
                    }).ToList(),
                });
                judgedIds.Add(t.Id);
            }
        }

        var mastery = await db.ConceptMastery.Where(m => m.SessionId == s.Id).ToDictionaryAsync(m => m.ConceptKey, ct);
        foreach (var conceptKey in s.Policy.ConceptIds)
        {
            if (!mastery.TryGetValue(conceptKey, out var m))
            {
                m = new ConceptMasteryRecord { OrganizationId = org, SessionId = s.Id, ConceptKey = conceptKey };
                db.ConceptMastery.Add(m);
            }
            m.Estimate = s.Mastery.TryGetValue(conceptKey, out var snap) ? snap.Estimate : 0;
            m.TurnsOnConcept = s.TurnsOn(conceptKey);
            m.Status = QuestionPlanner.StatusOf(s, conceptKey).ToString();
            m.UpdatedAt = now;
        }

        // Answer-text integrity: a high text-style estimate becomes reviewer-only context (never a score, never a verdict). It is an
        // unvalidated, detector-like estimate (GAD-T284): ReviewService hides it from a blind grader until their grade is submitted.
        foreach (var turn in s.Turns.Where(t => t.Judgment?.TextStyle is { Score: >= Gadfly.Sources.TextStyleAssessor.ReviewerThreshold }))
        {
            var turnRow = stored[turn.Index];
            if (await db.IntegrityEvents.AnyAsync(e => e.SessionId == s.Id && e.TurnId == turnRow.Id && e.Type == Review.IntegrityExplanations.TextStyleEventType, ct)) continue;
            var style = turn.Judgment!.TextStyle!;
            db.IntegrityEvents.Add(new IntegrityEvent
            {
                OrganizationId = org, SessionId = s.Id, TurnId = turnRow.Id, Source = "text", Type = Review.IntegrityExplanations.TextStyleEventType,
                StartedAt = turn.Answer!.StartedAt ?? turn.Answer.CompletedAt, EndedAt = turn.Answer.CompletedAt,
                Confidence = style.Score, Severity = IntegritySeverity.Low,
                Summary = $"Unvalidated estimate: this answer reads more like written text than the student's other answers ({style.Reason}). False-flag rate not yet measured; weak evidence on its own, never a verdict.",
            });
        }

        // Recitation check (GAD-T242): a possibly recited answer is recorded for the reviewer with its overlap, copied stretch and
        // which pattern fired (GAD-T248). Evidence the examiner acted on (she asked for it differently), never a verdict.
        foreach (var turn in s.Turns.Where(t => t.Judgment?.Recitation is not null && t.Answer is not null))
        {
            var turnRow = stored[turn.Index];
            if (await db.IntegrityEvents.AnyAsync(e => e.SessionId == s.Id && e.TurnId == turnRow.Id && e.Type == Review.RecitationView.EventType, ct)) continue;
            var flag = turn.Judgment!.Recitation!;
            db.IntegrityEvents.Add(new IntegrityEvent
            {
                OrganizationId = org, SessionId = s.Id, TurnId = turnRow.Id, Source = "answer", Type = Review.RecitationView.EventType,
                StartedAt = turn.Answer!.StartedAt ?? turn.Answer.CompletedAt, EndedAt = turn.Answer.CompletedAt,
                Confidence = flag.MatchesSource ? Math.Clamp(flag.SourceOverlap, 0, 1) : 0.5, Severity = IntegritySeverity.Low,
                Summary = $"Possibly recited: {flag.Reason}. Not counted as the student's own understanding; the examiner asked for it differently. Not a verdict.",
                DataJson = Review.RecitationView.From(flag).ToJson(),
            });
        }

        var knownClaims = (await db.SessionClaims.Where(c => c.SessionId == s.Id).Select(c => c.Normalized).ToListAsync(ct)).ToHashSet();
        foreach (var claim in live.Engine.Ledger.Claims.Where(c => !knownClaims.Contains(c.Id + "|" + c.Text)))
        {
            if (!stored.TryGetValue(claim.TurnIndex, out var turnRow)) continue;
            db.SessionClaims.Add(new SessionClaim
            {
                OrganizationId = org, SessionId = s.Id, TurnId = turnRow.Id, ConceptKey = claim.ConceptId,
                Quote = claim.Quote, Normalized = claim.Id + "|" + claim.Text,
            });
        }

        if (s.IsEnded && !await db.SessionReports.AnyAsync(r => r.SessionId == s.Id, ct))
        {
            var report = live.Engine.Report();
            db.SessionReports.Add(new Data.SessionReport
            {
                OrganizationId = org,
                SessionId = s.Id,
                Recommendation = report.Recommendation,
                OverallScore = report.OverallScore,
                ReportJson = report.ToJson(),
                Markdown = report.ToMarkdown(),
                CreatedAt = now,
            });
            // Integrating apps hear about it (GAD-T46): the deliveries commit with the report (outbox, GAD-T71).
            await Platform.WebhookOutbox.StageAsync(db, org, Platform.WebhookService.SessionCompleted, new
            {
                sessionId = s.Id, assignmentId = row.AssignmentId, studentUserId = row.StudentUserId, examId = row.ExamId,
                endReason = s.EndReason.ToString(), recommendation = report.Recommendation, overallScore = report.OverallScore,
            }, now, ct);
        }
        await db.SaveChangesAsync(ct);
        // An ended session leaves the cache only once its end is saved: dropped earlier, a reload in that window rebuilt the engine
        // from the not-yet-ended row and could ask on. Every ended session leaves it, also one only reloaded to be viewed (GAD-T314).
        if (s.IsEnded) _live.TryRemove(new KeyValuePair<Guid, Live>(s.Id, live));
        await PersistEvidenceAsync(live, row.ExamId, now, ct);
    }

    /// <summary>
    /// Per-objective evidence (GAD-T91, GAD-T120): AssessmentEvidence rows linked to the exam's objectives and the
    /// verified responses, in their own save after the transcript is stored, so a problem here never costs the
    /// resume point. Idempotent: items not written now (a turn or response not stored yet) are written by a later step.
    /// </summary>
    private async Task PersistEvidenceAsync(Live live, Guid examId, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(live.OrganizationId));
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            await Evidence.EvidenceWriter.SyncAsync(db, live.OrganizationId, live.Engine.Session.Id, examId, live.Engine.CollectEvidence(), now, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Evidence for session {SessionId} was not stored this step; the next step retries.", live.Engine.Session.Id);
        }
    }
}
