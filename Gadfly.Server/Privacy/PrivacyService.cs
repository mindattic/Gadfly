using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Privacy;

/// <summary>
/// Retention, export and deletion of student data (GAD-T61, decisions D6/D7). What a student said is
/// kept only for the organization's retention period (default 90 days); after that the words are
/// redacted while the outcome stays: judgments, mastery, the report and the instructor's grade.
/// Video is never stored anywhere in the platform. Every operation is explicit and logged; nothing
/// relies on cascades.
/// </summary>
public sealed class PrivacyService(GadflyDbContext db, TimeProvider clock, ILogger<PrivacyService> log)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Redacts every ended session older than its organization's retention period. Returns sessions redacted.</summary>
    public async Task<int> ApplyRetentionAsync(CancellationToken ct = default) => await ApplyRetentionAsync(null, ct);

    /// <summary>As above; <paramref name="progress"/> is told after each redacted session (the job renews its lease with it).</summary>
    public async Task<int> ApplyRetentionAsync(IJobProgress? progress, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var settings = await db.OrganizationSettings.AsNoTracking().ToDictionaryAsync(s => s.OrganizationId, s => s.RetentionDays, ct);
        // A session the student walked away from (closed laptop, lost connection) is never ended: it counts from when it started,
        // with a day's margin so a paused attempt that is still being worked on is not pulled out from under the student (GAD-T192).
        var candidates = await db.ExamSessions.AsNoTracking()
            .Where(s => s.RedactedAt == null && (s.EndedAt != null || s.State == SessionState.Running || s.State == SessionState.Paused))
            .Select(s => new { s.Id, s.OrganizationId, s.EndedAt, s.State, Started = s.StartedAt ?? s.CreatedAt })
            .ToListAsync(ct);
        var due = candidates.Where(s => (s.EndedAt ?? s.Started.AddDays(1)).AddDays(settings.GetValueOrDefault(s.OrganizationId, 90)) <= now).ToList();
        var redacted = 0;
        foreach (var s in due)
        {
            await RedactSessionAsync(s.Id, ct);
            if (s.EndedAt is null)
                await db.ExamSessions.Where(x => x.Id == s.Id && (x.State == SessionState.Running || x.State == SessionState.Paused))
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.State, SessionState.Failed).SetProperty(x => x.EndReason, "Abandoned").SetProperty(x => x.EndedAt, now), ct);
            log.LogInformation("Retention: redacted session {SessionId} (organization {OrganizationId}).", s.Id, s.OrganizationId);
            redacted++;
            if (progress is not null)
                await progress.ReportAsync((double)redacted / due.Count, $"Redacted {redacted} of {due.Count} session(s)", ct);
        }
        return due.Count;
    }

    /// <summary>Removes what the student said (answers, word timings, speech metrics, audio, quotes) but keeps the outcome.</summary>
    public async Task RedactSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SessionTurns.Where(t => t.SessionId == sessionId && t.AnswerText != null).ExecuteUpdateAsync(u => u
                .SetProperty(t => t.AnswerText, (string?)"[redacted after the retention period]")
                .SetProperty(t => t.WordsJson, (string?)null)
                .SetProperty(t => t.SpeechMetricsJson, (string?)null)
                .SetProperty(t => t.AudioRef, (string?)null), ct);
            await db.SessionClaims.Where(c => c.SessionId == sessionId).ExecuteDeleteAsync(ct);
            // The student's words also live in judge text and the report's evidence quotes: keep the scores, drop the words.
            var turnIds = db.SessionTurns.Where(t => t.SessionId == sessionId).Select(t => t.Id);
            var judgmentIds = db.Judgments.Where(j => turnIds.Contains(j.TurnId)).Select(j => j.Id);
            await db.JudgeVotes.Where(v => judgmentIds.Contains(v.JudgmentId))
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.Verdict, "[redacted after the retention period]"), ct);
            foreach (var j in await db.Judgments.Where(j => turnIds.Contains(j.TurnId)).ToListAsync(ct))
            {
                j.Summary = "[redacted after the retention period]";
                j.CriteriaJson = RedactCriteria(j.CriteriaJson);
            }
            foreach (var r in await db.SessionReports.Where(r => r.SessionId == sessionId).ToListAsync(ct))
            {
                if (Gadfly.Engine.SessionReport.FromJson(r.ReportJson) is { } report)
                {
                    var redacted = report.WithoutStudentWords();
                    r.ReportJson = redacted.ToJson();
                    r.Markdown = redacted.ToMarkdown();
                }
            }
            await db.SaveChangesAsync(ct);
            await RedactResponseLedgerAsync([sessionId], allAuditJson: false, ct);
            // The AI's per-objective grades quote the answers verbatim (criterion quotes, rationales, noted misconceptions): keep levels and scores (GAD-T182).
            foreach (var g in await db.AiObjectiveGrades.Where(g => g.SessionId == sessionId).ToListAsync(ct))
            {
                g.Explanation = Redacted;
                g.CriteriaJson = RedactAiDetail(g.CriteriaJson);
                g.MisconceptionsJson = "[]";
                g.AmbiguitiesJson = "[]";
            }
            await db.SaveChangesAsync(ct);
            await db.IntegrityEvents.Where(e => e.SessionId == sessionId).ExecuteUpdateAsync(u => u.SetProperty(e => e.DataJson, (string?)null), ct);
            await db.ExamSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.TranscriptJson, (string?)null)
                .SetProperty(s => s.LedgerJson, (string?)null)
                .SetProperty(s => s.ClientInfo, (string?)null)
                .SetProperty(s => s.RedactedAt, now), ct);
            await tx.CommitAsync(ct);
        });
    }

    private const string Redacted = "[redacted after the retention period]";

    /// <summary>
    /// Removes the student's words from the response ledger (GAD-T86) of the given sessions: original and verified
    /// text, transcript versions, clarification replies, evidence notes, and the before/after JSON of the
    /// "response.*", "clarification.*" and "desk.scores-archived" audit events (or of every audit event of those sessions, when
    /// <paramref name="allAuditJson"/>).
    /// This is the one deliberate exception to the ledger's immutability and the audit log's append-only rule: it
    /// runs as set-based updates that bypass the SaveChanges guards, and audit rows keep who did what and when (GAD-T110).
    /// </summary>
    private async Task RedactResponseLedgerAsync(List<Guid> sessionIds, bool allAuditJson, CancellationToken ct)
    {
        var responseIds = db.StudentResponses.Where(r => sessionIds.Contains(r.SessionId)).Select(r => r.Id);
        await db.TranscriptVersions.Where(v => responseIds.Contains(v.ResponseId)).ExecuteUpdateAsync(u => u.SetProperty(v => v.Text, Redacted), ct);
        await db.ClarificationEvents.Where(c => sessionIds.Contains(c.SessionId) && c.StudentReply != null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.StudentReply, (string?)Redacted), ct);
        // The question quotes the student ("I recorded your answer as: ...") or paraphrases the answer (GAD-T179).
        await db.ClarificationEvents.Where(c => sessionIds.Contains(c.SessionId))
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.Question, Redacted), ct);
        await db.AssessmentEvidence.Where(e => sessionIds.Contains(e.SessionId)).ExecuteUpdateAsync(u => u.SetProperty(e => e.Note, Redacted), ct);
        await db.StudentResponses.Where(r => sessionIds.Contains(r.SessionId)).ExecuteUpdateAsync(u => u
            .SetProperty(r => r.OriginalText, Redacted)
            .SetProperty(r => r.VerifiedText, Redacted)
            .SetProperty(r => r.SpeechRecognitionText, (string?)null), ct);
        await db.AuditEvents.Where(a => a.SessionId != null && sessionIds.Contains(a.SessionId.Value) && (allAuditJson || a.Action.StartsWith("response.") || a.Action.StartsWith("clarification.")
                        // Clear scores' archived copy holds the AI grades and reports, which quote the answers (GAD-T262).
                        || a.Action == "desk.scores-archived"))
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.BeforeJson, (string?)null).SetProperty(a => a.AfterJson, (string?)null), ct);
        // The student's own appeal text is the audit event's reason; an erasure takes it too (GAD-T185).
        if (allAuditJson)
            await db.AuditEvents.Where(a => a.SessionId != null && sessionIds.Contains(a.SessionId.Value) && a.Action == "grade.appeal.opened")
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Reason, (string?)Redacted), ct);
    }

    /// <summary>The AI objective grade's stored detail without the words: criterion levels and scores stay, quotes and rationales go.</summary>
    internal static string RedactAiDetail(string criteriaJson)
    {
        // The legacy format is a bare criteria array (AiGradeView.From still reads it): keep its levels and scores too.
        if (criteriaJson.TrimStart().StartsWith('['))
            return Gadfly.Server.Grading.GradingJson.Write(Gadfly.Server.Grading.GradingJson.ReadList<Gadfly.Sources.Grading.CriterionGrade>(criteriaJson)
                .Select(c => c with { Rationale = "", Quotes = [] }).ToList());
        var detail = Gadfly.Server.Grading.GradingJson.Read<Gadfly.Server.Grading.StoredAiDetail>(criteriaJson);
        if (detail is null) return "[]";
        return Gadfly.Server.Grading.GradingJson.Write(new Gadfly.Server.Grading.StoredAiDetail(
            [.. detail.Criteria.Select(c => c with { Rationale = "", Quotes = [] })],
            [.. detail.Voters.Select(v => v with { Explanation = "" })]));
    }

    /// <summary>Keeps each criterion's name and score; drops rationales and quoted passages tied to the answer.</summary>
    internal static string RedactCriteria(string criteriaJson)
    {
        try
        {
            var criteria = JsonSerializer.Deserialize<List<Gadfly.Engine.CriterionScore>>(criteriaJson, Json) ?? [];
            return JsonSerializer.Serialize(criteria.Select(c => c with { Rationale = "", Citations = c.Citations.Select(x => x with { Quote = null }).ToList() }), Json);
        }
        catch (JsonException)
        {
            return "[]";
        }
    }

    /// <summary>Everything the platform holds about one student in one organization, as JSON (a data-subject export).</summary>
    public async Task<string> ExportStudentAsync(Guid organizationId, Guid studentUserId, CancellationToken ct = default)
    {
        var sessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.StudentUserId == studentUserId).ToListAsync(ct);
        var ids = sessions.Select(s => s.Id).ToList();
        var turns = await db.SessionTurns.AsNoTracking().Where(t => ids.Contains(t.SessionId)).OrderBy(t => t.Index).ToListAsync(ct);
        var turnIds = turns.Select(t => t.Id).ToList();
        var judgments = await db.Judgments.AsNoTracking().Include(j => j.Votes).Where(j => turnIds.Contains(j.TurnId)).ToListAsync(ct);
        var responses = await db.StudentResponses.AsNoTracking().Where(r => ids.Contains(r.SessionId)).OrderBy(r => r.SessionId).ThenBy(r => r.Sequence).ToListAsync(ct);
        var responseIds = responses.Select(r => r.Id).ToList();
        var versions = await db.TranscriptVersions.AsNoTracking().Where(v => responseIds.Contains(v.ResponseId)).ToListAsync(ct);
        var export = new
        {
            exportedAt = clock.GetUtcNow(),
            organizationId,
            studentUserId,
            assignments = await db.ExamAssignments.AsNoTracking()
                .Where(a => a.OrganizationId == organizationId && a.StudentUserId == studentUserId)
                .Select(a => new { a.Id, a.ExamId, examTitle = a.Exam!.Title, a.OpensAt, a.ClosesAt, a.AttemptsAllowed, a.ResultsReleasedAt })
                .ToListAsync(ct),
            sessions = sessions.Select(s => new
            {
                s.Id, s.AssignmentId, s.ExamId, s.Attempt, state = s.State.ToString(), s.CreatedAt, s.StartedAt, s.EndedAt, s.EndReason,
                s.ConsentRecordedAt, s.ConsentTextSha256, s.RedactedAt,
                turns = turns.Where(t => t.SessionId == s.Id).Select(t => new
                {
                    t.Index, t.ConceptKey, t.Move, t.Question, t.AskedAt, t.AnswerText, t.AnswerMode, t.AnswerStartedAt, t.AnswerCompletedAt,
                    t.MasteryAfter,
                    judgment = judgments.Where(j => j.TurnId == t.Id).Select(j => new
                    {
                        j.OverallScore, j.Agreement, j.NeedsHumanReview, j.Summary, j.CriteriaJson, j.TextStyleScore,
                        votes = j.Votes.Select(v => new { v.ProviderId, v.OverallScore, v.Verdict }),
                    }).FirstOrDefault(),
                }),
            }),
            integrityEvents = await db.IntegrityEvents.AsNoTracking().Where(e => ids.Contains(e.SessionId))
                .Select(e => new { e.SessionId, e.Source, e.Type, e.StartedAt, e.EndedAt, e.Confidence, severity = e.Severity.ToString(), e.Summary })
                .ToListAsync(ct),
            reports = await db.SessionReports.AsNoTracking().Where(r => ids.Contains(r.SessionId))
                .Select(r => new { r.SessionId, r.Recommendation, r.OverallScore, r.Markdown, r.CreatedAt }).ToListAsync(ct),
            grades = await db.GradeDecisions.AsNoTracking().Where(g => ids.Contains(g.SessionId))
                .Select(g => new { g.SessionId, g.Grade, g.Score, g.Comment, g.DecidedAt }).ToListAsync(ct),
            // The response ledger and the grading record (GAD-T193): what the student said and how it was graded.
            responses = responses.Select(r => new
            {
                r.SessionId, r.Sequence, kind = r.Kind.ToString(), r.Modality, r.OriginalText, r.VerifiedText, verificationState = r.VerificationState.ToString(),
                r.SubmittedAt,
                versions = versions.Where(v => v.ResponseId == r.Id).OrderBy(v => v.Version).Select(v => new { v.Version, v.Text, v.Source, v.CreatedAt }),
            }),
            clarifications = await db.ClarificationEvents.AsNoTracking().Where(c => ids.Contains(c.SessionId)).OrderBy(c => c.AskedAt)
                .Select(c => new { c.SessionId, type = c.Type.ToString(), c.Question, c.StudentReply, c.AskedAt, c.AnsweredAt }).ToListAsync(ct),
            humanGrades = await db.HumanObjectiveGrades.AsNoTracking().Where(g => ids.Contains(g.SessionId)).OrderBy(g => g.SubmittedAt)
                .Select(g => new { g.SessionId, g.ObjectiveId, level = g.Level.ToString(), g.Score, g.MaxScore, g.Comment, g.EvidenceNote, g.SubmittedAt }).ToListAsync(ct),
            aiGrades = await db.AiObjectiveGrades.AsNoTracking().Where(g => ids.Contains(g.SessionId) && !g.IsMock).OrderBy(g => g.CreatedAt)
                .Select(g => new { g.SessionId, g.ObjectiveId, level = g.Level.ToString(), g.Score, g.MaxScore, g.Explanation, g.CreatedAt }).ToListAsync(ct),
            finalGrades = await db.FinalGrades.AsNoTracking().Where(g => ids.Contains(g.SessionId))
                .Select(g => new { g.SessionId, status = g.Status.ToString(), g.FinalPercent, g.LetterOrLabel, g.UpdatedAt }).ToListAsync(ct),
            appeals = await db.GradeAppeals.AsNoTracking().Where(a => ids.Contains(a.SessionId)).OrderBy(a => a.OpenedAt)
                .Select(a => new { a.SessionId, a.Reason, state = a.State.ToString(), a.Resolution, a.OpenedAt, a.ResolvedAt }).ToListAsync(ct),
        };
        return JsonSerializer.Serialize(export, Json);
    }

    /// <summary>
    /// Deletes every session-level record of one student in one organization and removes their
    /// membership (a data-subject erasure). The sign-in account itself is left to MindAttic.Authentication's
    /// administration, since the person may belong to other organizations.
    /// </summary>
    public async Task<int> DeleteStudentDataAsync(Guid organizationId, Guid studentUserId, CancellationToken ct = default)
    {
        var ids = await db.ExamSessions.Where(s => s.OrganizationId == organizationId && s.StudentUserId == studentUserId)
            .Select(s => s.Id).ToListAsync(ct);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Audit rows stay (append-only) but lose the words and grades they carried for these sessions.
            await RedactResponseLedgerAsync(ids, allAuditJson: true, ct);
            // The response ledger and grading records (GAD-T86, GAD-T94..T98) reference sessions, turns and each other:
            // children first, since nothing cascades (GAD-T111).
            var responseIds = db.StudentResponses.Where(r => ids.Contains(r.SessionId)).Select(r => r.Id);
            var disagreementIds = db.GradeDisagreements.Where(d => ids.Contains(d.SessionId)).Select(d => d.Id);
            await db.AssessmentEvidence.Where(e => ids.Contains(e.SessionId)).ExecuteDeleteAsync(ct);
            await db.ClarificationEvents.Where(c => ids.Contains(c.SessionId)).ExecuteDeleteAsync(ct);
            await db.ResponseVerifications.Where(v => responseIds.Contains(v.ResponseId)).ExecuteDeleteAsync(ct);
            await db.TranscriptVersions.Where(v => responseIds.Contains(v.ResponseId)).ExecuteDeleteAsync(ct);
            await db.StudentResponses.Where(r => ids.Contains(r.SessionId)).ExecuteDeleteAsync(ct);
            await db.GradeReconciliations.Where(r => disagreementIds.Contains(r.DisagreementId)).ExecuteDeleteAsync(ct);
            await db.GradeDisagreements.Where(d => ids.Contains(d.SessionId)).ExecuteDeleteAsync(ct);
            await db.AiObjectiveGrades.Where(g => ids.Contains(g.SessionId)).ExecuteDeleteAsync(ct);
            await db.HumanObjectiveGrades.Where(g => ids.Contains(g.SessionId)).ExecuteDeleteAsync(ct);
            await db.ApprovalRecords.Where(a => ids.Contains(a.SessionId)).ExecuteDeleteAsync(ct);
            await db.GradeAppeals.Where(a => ids.Contains(a.SessionId)).ExecuteDeleteAsync(ct);
            await db.FinalGrades.Where(g => ids.Contains(g.SessionId)).ExecuteDeleteAsync(ct);
            var turnIds = db.SessionTurns.Where(t => ids.Contains(t.SessionId)).Select(t => t.Id);
            var judgmentIds = db.Judgments.Where(j => turnIds.Contains(j.TurnId)).Select(j => j.Id);
            await db.JudgeVotes.Where(v => judgmentIds.Contains(v.JudgmentId)).ExecuteDeleteAsync(ct);
            await db.Judgments.Where(j => turnIds.Contains(j.TurnId)).ExecuteDeleteAsync(ct);
            await db.SessionClaims.Where(c => ids.Contains(c.SessionId)).ExecuteDeleteAsync(ct);
            await db.IntegrityEvents.Where(e => ids.Contains(e.SessionId)).ExecuteDeleteAsync(ct);
            await db.ConceptMastery.Where(m => ids.Contains(m.SessionId)).ExecuteDeleteAsync(ct);
            await db.SessionReports.Where(r => ids.Contains(r.SessionId)).ExecuteDeleteAsync(ct);
            await db.GradeDecisions.Where(g => ids.Contains(g.SessionId)).ExecuteDeleteAsync(ct);
            await db.UsageEntries.Where(u => u.SessionId != null && ids.Contains(u.SessionId.Value)).ExecuteUpdateAsync(u => u.SetProperty(x => x.SessionId, (Guid?)null), ct);
            // Webhook deliveries copy the session's outcome and the student's id into their payload (session.completed): erase them
            // too, so nothing of the student is left in the organization and a delivery still waiting is never sent (GAD-T315). Their
            // queued jobs find no delivery and stop.
            var studentKey = studentUserId.ToString();
            await db.WebhookDeliveries.Where(d => d.OrganizationId == organizationId && d.PayloadJson.Contains(studentKey)).ExecuteDeleteAsync(ct);
            await db.SessionTurns.Where(t => ids.Contains(t.SessionId)).ExecuteDeleteAsync(ct);
            await db.ExamSessions.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(ct);
            await db.ExamAssignments.Where(a => a.OrganizationId == organizationId && a.StudentUserId == studentUserId).ExecuteDeleteAsync(ct);
            await db.OrganizationMembers.Where(m => m.OrganizationId == organizationId && m.UserId == studentUserId)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.IsActive, false), ct);
            await tx.CommitAsync(ct);
        });
        log.LogWarning("Erased {Count} session(s) of user {UserId} in organization {OrganizationId}.", ids.Count, studentUserId, organizationId);
        return ids.Count;
    }
}

/// <summary>The "privacy.retention" job: runs <see cref="PrivacyService.ApplyRetentionAsync"/>.</summary>
public sealed class RetentionJob(PrivacyService privacy) : IJobHandler
{
    public const string JobKind = "privacy.retention";
    public string Kind => JobKind;

    public async Task RunAsync(Job job, IJobProgress progress, CancellationToken ct)
    {
        var n = await privacy.ApplyRetentionAsync(progress, ct);
        await progress.ReportAsync(1, $"{n} session(s) redacted", ct);
    }
}

/// <summary>Queues the retention job once a day (if one isn't already waiting).</summary>
public sealed class RetentionScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<RetentionScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateSystemScope();
                var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                var waiting = await db.Jobs.AnyAsync(j => j.Kind == RetentionJob.JobKind && (j.State == JobState.Queued || j.State == JobState.Running), stoppingToken);
                if (!waiting)
                    await scope.ServiceProvider.GetRequiredService<JobQueue>().EnqueueAsync(RetentionJob.JobKind, new { }, null, ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Could not queue the retention job.");
            }
            try { await Task.Delay(TimeSpan.FromHours(24), clock, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
