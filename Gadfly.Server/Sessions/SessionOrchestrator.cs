using System.Collections.Concurrent;
using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Sources;
using Gadfly.Socrates;
using Gadfly.Sources;
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
    private sealed class Live(SourceExamEngine engine, Guid organizationId)
    {
        public SourceExamEngine Engine { get; set; } = engine;
        public Guid OrganizationId { get; } = organizationId;
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private sealed record SubjectSnapshot(ConceptMap Map, IReadOnlyList<ConceptRubric> Rubrics);

    private readonly ConcurrentDictionary<Guid, Live> _live = new();
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
        using var scope = scopes.CreateScope();
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
        var active = sessions.FirstOrDefault(s => s.State is not (SessionState.Ended or SessionState.Failed));
        if (active is not null) return active.Id;
        if (sessions.Count >= assignment.AttemptsAllowed) throw new SessionRefusedException("You have used every attempt for this exam.");
        var (spent, quota) = await meter.MonthAsync(db, organizationId, ct);
        if (quota is { } q && spent >= q)
            throw new SessionRefusedException("Your organization has reached its monthly usage limit. Please contact your instructor.");

        var collection = await db.SourceCollections.AsNoTracking()
            .FirstAsync(c => c.Id == exam.CollectionId && c.OrganizationId == organizationId, ct);
        var session = new ExamSession
        {
            OrganizationId = organizationId,
            AssignmentId = assignmentId,
            ExamId = exam.Id,
            StudentUserId = studentUserId,
            Attempt = sessions.Count + 1,
            State = SessionState.Created,
            CreatedAt = now,
            PolicyJson = exam.PolicyJson,
            PromptSetSha256 = ExamPrompts.Set.Sha256,
            ConceptMapVersion = collection.ConceptMapVersion,
            ClientInfo = clientInfo is { Length: > 500 } ci ? ci[..500] : clientInfo,
        };
        db.ExamSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session.Id;
    }

    /// <summary>Records the student's consent (hash of the exact text shown). Required before the first question.</summary>
    public async Task RecordConsentAsync(Guid organizationId, Guid sessionId, Guid studentUserId, string consentTextSha256, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
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
        StepAsync(organizationId, sessionId, studentUserId, async live => await live.Engine.NextQuestionAsync(ct), ct);

    /// <summary>Submits the answer to the waiting question, judges it, and returns the next question.</summary>
    public Task<SessionStep> AnswerAsync(Guid organizationId, Guid sessionId, Guid studentUserId, Answer answer, CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, async live =>
        {
            if (live.Engine.Session.IsEnded)
                throw new SessionRefusedException("This exam has ended; your answers were submitted.");
            if (live.Engine.Session.OpenTurn is null && live.Engine.Session.AwaitingJudgment is null)
                throw new SessionRefusedException("There is no question waiting for an answer.");
            // The server's clock is the authority: an answer arriving well after time ran out is not judged.
            if (live.Engine.Session.Remaining(clock.GetUtcNow()) <= TimeSpan.Zero
                && clock.GetUtcNow() - (live.Engine.Session.StartedAt + live.Engine.Session.Policy.TimeBudget) > AnswerGrace)
            {
                live.Engine.End(EndReason.TimeUp);
                return null;
            }
            await live.Engine.SubmitAnswerAsync(answer, ct);
            return await live.Engine.NextQuestionAsync(ct);
        }, ct);

    /// <summary>Ends a session early (student finished early, or the host stopped it).</summary>
    public Task<SessionStep> EndAsync(Guid organizationId, Guid sessionId, Guid studentUserId, EndReason reason, CancellationToken ct = default) =>
        StepAsync(organizationId, sessionId, studentUserId, live =>
        {
            live.Engine.End(reason);
            return Task.FromResult<Turn?>(null);
        }, ct);

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
            using var usage = Gadfly.Socrates.PanelUsage.Track(out var calls);
            try
            {
                question = await step(live);
            }
            finally
            {
                // Persist whatever happened, even if a panel call failed midway: the transcript is the resume point.
                await PersistAsync(live, studentUserId, calls, ct);
            }
            var view = View(live, question);
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

    private SessionStep View(Live live, Turn? question)
    {
        var s = live.Engine.Session;
        var concepts = s.Policy.ConceptIds;
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
            ConceptsTotal = concepts.Count,
            ConceptsClosed = concepts.Count(c => QuestionPlanner.StatusOf(s, c) is ConceptStatus.Mastered or ConceptStatus.Aporia or ConceptStatus.Exhausted),
        };
    }

    private async Task<Live> GetLiveAsync(Guid organizationId, Guid sessionId, Guid studentUserId, CancellationToken ct)
    {
        if (_live.TryGetValue(sessionId, out var cached) && cached.OrganizationId == organizationId) return cached;
        var gate = _loadGates.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_live.TryGetValue(sessionId, out cached) && cached.OrganizationId == organizationId) return cached;
            using var scope = scopes.CreateScope();
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
                engine = await SourceExamEngine.ResumeAsync(panel, subject, session, ClaimLedger.FromJson(row.LedgerJson), clock, ct);
                log.LogInformation("Resumed exam session {SessionId} at turn {Turn}.", sessionId, session.Turns.Count);
            }
            else
            {
                var policy = JsonSerializer.Deserialize<SessionPolicy>(row.PolicyJson, Json) ?? new SessionPolicy();
                if (policy.ConceptIds.Count == 0)
                    policy = policy with { ConceptIds = subject.Map.Concepts.Select(c => c.Key).ToList() };
                engine = await SourceExamEngine.StartAsync(panel, subject, policy, clock, sessionId, ct);
            }
            // The answer-text signal is an integrity signal: off when the exam's integrity level is Off.
            engine.AssessTextStyle = exam.IntegrityLevel != IntegrityLevel.Off;
            var live = new Live(engine, organizationId);
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

    private static async Task<ExamSession> Owned(GadflyDbContext db, Guid organizationId, Guid sessionId, Guid studentUserId, CancellationToken ct) =>
        await db.ExamSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId, ct)
        ?? throw new SessionRefusedException("That exam session was not found.");

    /// <summary>Writes the engine's state to SQL: the resume point plus the review rows for every turn not yet stored.</summary>
    private async Task PersistAsync(Live live, Guid studentUserId, IReadOnlyList<Gadfly.Socrates.PanelCall> calls, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var org = live.OrganizationId;
        var s = live.Engine.Session;
        var row = await Owned(db, org, s.Id, studentUserId, ct);
        var now = clock.GetUtcNow();

        meter.Stage(db, org, s.Id, calls, "exam-step");   // GAD-T47: estimated LLM usage of this step
        row.TranscriptJson = SessionTranscriptSerializer.Serialize(s);
        row.LedgerJson = live.Engine.Ledger.ToJson();
        row.StartedAt ??= s.StartedAt;
        row.State = s.IsEnded ? SessionState.Ended : SessionState.Running;
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

        // Answer-text integrity: a strong "read aloud" signal becomes reviewable evidence (never a score).
        foreach (var turn in s.Turns.Where(t => t.Judgment?.TextStyle is { Score: >= 0.7 }))
        {
            var turnRow = stored[turn.Index];
            if (await db.IntegrityEvents.AnyAsync(e => e.SessionId == s.Id && e.TurnId == turnRow.Id && e.Type == "text-reads-as-written", ct)) continue;
            var style = turn.Judgment!.TextStyle!;
            db.IntegrityEvents.Add(new IntegrityEvent
            {
                OrganizationId = org, SessionId = s.Id, TurnId = turnRow.Id, Source = "text", Type = "text-reads-as-written",
                StartedAt = turn.Answer!.StartedAt ?? turn.Answer.CompletedAt, EndedAt = turn.Answer.CompletedAt,
                Confidence = style.Score, Severity = IntegritySeverity.Low,
                Summary = $"This answer reads more like written text than the student's other answers ({style.Reason}). Weak evidence on its own.",
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
            _live.TryRemove(s.Id, out _);
            // Integrating apps hear about it (GAD-T46): the deliveries commit with the report (outbox, GAD-T71).
            await Platform.WebhookOutbox.StageAsync(db, org, Platform.WebhookService.SessionCompleted, new
            {
                sessionId = s.Id, assignmentId = row.AssignmentId, studentUserId = row.StudentUserId, examId = row.ExamId,
                endReason = s.EndReason.ToString(), recommendation = report.Recommendation, overallScore = report.OverallScore,
            }, now, ct);
        }
        await db.SaveChangesAsync(ct);
    }
}
