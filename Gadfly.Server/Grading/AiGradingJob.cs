using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Sources.Grading;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>The "grading.ai" job's payload.</summary>
public sealed record AiGradingPayload(Guid SessionId);

/// <summary>
/// AI preliminary grading of an ended session (GAD-T94, Part 11.1 Stages 3-5). Status path:
/// Examination Completed → Transcript Verification Pending (a response needs a human; grading waits) →
/// AI Grading Pending → per-objective <see cref="AiObjectiveGrade"/> rows → Human Review Pending. Every
/// move is audited. Idempotent: objectives already graded for the session are not graded again, so a retry
/// continues where a failed attempt stopped. The AI grade is never final.
/// </summary>
public sealed class AiGradingJob(GadflyDbContext db, ISocraticVotingClient panel, TimeProvider clock, ILogger<AiGradingJob> log,
    PanelModelInventory models, IConfiguration config) : IJobHandler
{
    public const string JobKind = "grading.ai";
    public string Kind => JobKind;

    public async Task RunAsync(Job job, IJobProgress progress, CancellationToken ct)
    {
        var payload = JobQueue.ReadPayload<AiGradingPayload>(job);
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == payload.SessionId, ct);
        if (session is null || session.State != SessionState.Ended) return;
        var exam = await db.Exams.AsNoTracking().FirstAsync(e => e.Id == session.ExamId && e.OrganizationId == session.OrganizationId, ct);
        var now = clock.GetUtcNow();
        var grade = await GradeLifecycle.EnsureAsync(db, session, GradingActor.System, now, ct);
        if (grade.Status is not (GradeStatus.ExaminationCompleted or GradeStatus.TranscriptVerificationPending or GradeStatus.AiGradingPending)) return;

        // Stage 3: responses that need a human (unresolved transcript or meaning) hold AI grading back.
        var responses = await GradingInputs.EnsureResponsesAsync(db, session, now, ct);
        var waiting = responses.Count(r => VerificationMachine.NeedsHuman(r.VerificationState));
        if (grade.Status == GradeStatus.ExaminationCompleted)
        {
            if (waiting > 0)
            {
                GradeLifecycle.Move(db, grade, GradeStatus.TranscriptVerificationPending, GradingActor.System, now,
                    $"{waiting} response(s) need a human to resolve the transcript or meaning before AI grading.");
                await db.SaveChangesAsync(ct);
                return;
            }
            GradeLifecycle.Move(db, grade, GradeStatus.AiGradingPending, GradingActor.System, now, "Every response is usable or resolved.");
            await db.SaveChangesAsync(ct);
        }
        else if (grade.Status == GradeStatus.TranscriptVerificationPending)
        {
            if (waiting > 0) return;   // still waiting; a reviewer may release it explicitly (GradingService.ProceedWithUnresolvedAsync)
            GradeLifecycle.Move(db, grade, GradeStatus.AiGradingPending, GradingActor.System, now, "The responses that needed a human were resolved.");
            await db.SaveChangesAsync(ct);
        }

        // The policy in force, frozen for this session.
        var policyRow = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == exam.Id && p.OrganizationId == exam.OrganizationId, ct);
        var policy = GradingJson.Read<PolicySnapshot>(grade.PolicyJson);
        if (policy is null)
        {
            // GAD-T270 calibration, checked against the models the panel runs now (GAD-T429), and the exam's gate on it (GAD-T427).
            var calibration = await PanelCalibrationService.LatestAsync(db, exam.OrganizationId, exam.Id, models.Current, now,
                PanelCalibrationService.RecheckDays(config), ct);
            var settings = await PanelGovernanceService.LatestAsync(db, exam.OrganizationId, exam.Id, ct);
            policy = PanelGovernanceService.ApplyGate(PolicySnapshot.From(policyRow, now) with { Panel = calibration?.ToNote() }, settings, calibration);
            grade.PolicyJson = GradingJson.Write(policy);
            GradingAudit.Add(db, grade.OrganizationId, GradingActor.System, "grade.policy.applied", "FinalGrade", grade.SessionId.ToString(), grade.SessionId, now,
                after: policy, reason: (policy.Source == "exam-policy" ? "The exam's grading policy." : "The exam has no grading policy; the platform defaults apply.")
                    + (policy.Gate is { Applied: true } gate ? " " + gate.Reason : ""));
            await db.SaveChangesAsync(ct);
        }

        // Stage 5: grade each objective not graded yet.
        var objectives = await GradingInputs.EnsureObjectivesAsync(db, exam, now, ct);
        if (objectives.Count == 0) throw new InvalidOperationException("The exam has no learning objectives and no concepts to derive them from.");
        var done = (await db.AiObjectiveGrades.AsNoTracking().Where(g => g.SessionId == session.Id).Select(g => g.ObjectiveId).ToListAsync(ct)).ToHashSet();
        var grader = new AiObjectiveGrader(panel);
        var lastAttempt = job.Attempts >= job.MaxAttempts;
        var i = 0;
        foreach (var objective in objectives)
        {
            i++;
            if (done.Contains(objective.Id)) continue;
            await progress.ReportAsync((double)(i - 1) / objectives.Count, $"Grading {objective.Title}", ct);
            var input = await GradingInputs.BuildInputAsync(db, session, exam, objective, responses, policy.AllowAssistedEvidence, ct);
            var result = await grader.GradeAsync(input, ct);
            if (result.PanelFailed && !lastAttempt)
                throw new InvalidOperationException($"The AI panel returned no valid evaluation for '{objective.Title}'; retrying.");
            var row = new AiObjectiveGrade
            {
                OrganizationId = session.OrganizationId, SessionId = session.Id, ObjectiveId = objective.Id, Level = result.Level, Score = result.Score,
                MaxScore = result.MaxScore, Explanation = result.Explanation,
                CriteriaJson = GradingJson.Write(new StoredAiDetail(result.Criteria, result.Voters)),
                EvidenceRefsJson = GradingJson.Write(result.EvidenceRefs), MisconceptionsJson = GradingJson.Write(result.Misconceptions),
                AmbiguitiesJson = GradingJson.Write(result.Ambiguities), ReviewFlagsJson = GradingJson.Write(result.ReviewFlags),
                Confidence = result.Confidence, IsMock = result.IsMock, CreatedAt = clock.GetUtcNow(),
            };
            db.AiObjectiveGrades.Add(row);
            GradingAudit.Add(db, session.OrganizationId, GradingActor.System, "grade.ai.recorded", "AiObjectiveGrade", row.Id.ToString(), session.Id, row.CreatedAt,
                after: new { objective = objective.Key, level = result.Level.ToString(), score = result.Score, maxScore = result.MaxScore, confidence = result.Confidence,
                    flags = result.ReviewFlags, voters = result.Voters.Count, isMock = result.IsMock });
            await db.SaveChangesAsync(ct);
            log.LogInformation("AI graded objective {Objective} of session {SessionId}: level {Level}, {Score}/{Max}, {Confidence}.",
                objective.Key, session.Id, result.Level, result.Score, result.MaxScore, result.Confidence);
        }

        GradeLifecycle.Move(db, grade, GradeStatus.HumanReviewPending, GradingActor.System, clock.GetUtcNow(),
            "AI preliminary grades recorded; a human examiner grades next.");
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Starts AI grading for sessions that end (GAD-T94) without touching the live exam path: every poll it
/// creates the grade record of each recently ended session that has none and queues "grading.ai"; it also
/// re-queues sessions whose responses needing a human were resolved. Older sessions are started by hand
/// from the grading queue. Failed jobs are not retried automatically (a reviewer retries from the queue).
/// </summary>
public sealed class GradingSweeper(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration config, ILogger<GradingSweeper> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, config.GetValue("Gadfly:Grading:SweepSeconds", 15)));
        while (!stop.IsCancellationRequested)
        {
            try { await SweepAsync(stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Grading sweep failed; trying again next poll."); }
            try { await Task.Delay(interval, clock, stop); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
        var now = clock.GetUtcNow();
        var since = now - TimeSpan.FromDays(Math.Max(0, config.GetValue("Gadfly:Grading:AutoStartWithinDays", 3)));

        var fresh = await db.ExamSessions.AsNoTracking()
            .Where(s => s.State == SessionState.Ended && s.EndedAt >= since && !db.FinalGrades.Any(g => g.SessionId == s.Id))
            .OrderBy(s => s.EndedAt).Take(20).ToListAsync(ct);
        foreach (var s in fresh)
            await GradingStarter.StartAsync(db, queue, s, GradingActor.System, now, ct);

        // Waiting on transcript verification, now resolved; or stuck without any job (e.g. created by hand).
        // Rows the loop below would skip are filtered out here, so 50 stuck grades cannot starve the eligible ones (oldest first).
        var pending = await db.FinalGrades.AsNoTracking()
            .Where(g => g.Status == GradeStatus.TranscriptVerificationPending || g.Status == GradeStatus.ExaminationCompleted
                        || g.Status == GradeStatus.AiGradingPending)
            .Where(g => !db.Jobs.Any(j => j.Kind == AiGradingJob.JobKind && j.PayloadJson.Contains(g.SessionId.ToString().ToLower())
                        && (g.Status != GradeStatus.TranscriptVerificationPending || j.State == JobState.Queued || j.State == JobState.Running)))
            .Where(g => g.Status != GradeStatus.TranscriptVerificationPending
                        || !db.StudentResponses.Any(r => r.SessionId == g.SessionId
                            && (r.VerificationState == VerificationState.Unresolved || r.VerificationState == VerificationState.FlaggedForHumanReview)))
            .OrderBy(g => g.UpdatedAt)
            .Select(g => new { g.SessionId, g.OrganizationId, g.Status }).Take(50).ToListAsync(ct);
        foreach (var g in pending)
        {
            if (await GradingStarter.HasJobAsync(db, g.SessionId, activeOnly: g.Status == GradeStatus.TranscriptVerificationPending, ct)) continue;
            if (g.Status == GradeStatus.TranscriptVerificationPending
                && await db.StudentResponses.AnyAsync(r => r.SessionId == g.SessionId
                    && (r.VerificationState == VerificationState.Unresolved || r.VerificationState == VerificationState.FlaggedForHumanReview), ct))
                continue;
            await queue.EnqueueAsync(AiGradingJob.JobKind, new AiGradingPayload(g.SessionId), g.OrganizationId, maxAttempts: 4, ct: ct);
        }
    }
}

/// <summary>Creating a session's grade record and queueing its AI grading.</summary>
internal static class GradingStarter
{
    public static async Task StartAsync(GadflyDbContext db, JobQueue queue, ExamSession session, GradingActor actor, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await GradeLifecycle.EnsureAsync(db, session, actor, now, ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();   // another instance created it first
            return;
        }
        if (!await HasJobAsync(db, session.Id, activeOnly: true, ct))
            await queue.EnqueueAsync(AiGradingJob.JobKind, new AiGradingPayload(session.Id), session.OrganizationId, maxAttempts: 4, ct: ct);
    }

    /// <summary>Whether a grading job exists for the session (queued or running only, when <paramref name="activeOnly"/>).</summary>
    public static Task<bool> HasJobAsync(GadflyDbContext db, Guid sessionId, bool activeOnly, CancellationToken ct)
    {
        var id = sessionId.ToString();
        var q = db.Jobs.AsNoTracking().Where(j => j.Kind == AiGradingJob.JobKind && j.PayloadJson.Contains(id));
        if (activeOnly) q = q.Where(j => j.State == JobState.Queued || j.State == JobState.Running);
        return q.AnyAsync(ct);
    }
}
