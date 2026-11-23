using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>
/// Human grading, combined scoring, reconciliation, approval, publication, appeals and amendments
/// (docs/MASTER-PLAN.md Parts 8-11; GAD-T95..T98). Every action checks <see cref="PermissionMatrix"/> with the
/// caller's role in the active organization and audits a refusal as <c>access.denied</c> (Scenario J); every
/// state change goes through <see cref="GradeStatusMachine"/> and is audited; nothing reaches a student
/// without a Finalize approval record and a Publish record. Scores are exact decimal arithmetic in
/// <see cref="CombinedScoring"/> under the policy frozen when AI grading began.
/// </summary>
public sealed class GradingService(GadflyDbContext db, ITenantContext tenant, TimeProvider clock, JobQueue queue)
{
    private Guid Org => tenant.OrganizationId ?? throw new InvalidOperationException("No active organization.");
    public GradingActor Actor => new(tenant.UserId, tenant.Role);
    private DateTimeOffset Now => clock.GetUtcNow();

    public bool Can(Permission p) => tenant.OrganizationId is not null && Actor.Has(p);

    /// <summary>
    /// Stages a notification email to the session's own student (GAD-T310: result released, appeal received/decided).
    /// Never throws and never blocks grading: a lookup miss or a disabled organization just means nothing is staged.
    /// </summary>
    private async Task NotifyStudentAsync(Guid sessionId, string kind, Func<string?, string, string, Notifications.EmailData> build, CancellationToken ct)
    {
        var info = await (from s in db.ExamSessions.AsNoTracking()
                           join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
                           where s.Id == sessionId && s.OrganizationId == Org
                           select new { s.StudentUserId, e.Title }).FirstOrDefaultAsync(ct);
        if (info is null) return;
        var orgName = await db.Organizations.AsNoTracking().Where(o => o.Id == Org).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "your organization";
        var studentName = await db.AuthUsers.AsNoTracking().Where(u => u.Id == info.StudentUserId).Select(u => u.DisplayName ?? u.UserName).FirstOrDefaultAsync(ct) ?? "there";
        await Notifications.EmailOutbox.StageAsync(db, Org, kind, info.StudentUserId, build(info.Title, orgName, studentName), Now, ct, relatedId: sessionId);
    }

    /// <summary>Checks a permission; a refusal is audited and saved at once.</summary>
    private async Task<bool> RequireAsync(Permission p, string attempted, Guid? sessionId, CancellationToken ct)
    {
        if (Can(p)) return true;
        if (tenant.OrganizationId is { } org) await GradingAudit.DeniedAsync(db, org, Actor, p, attempted, sessionId, Now, ct);
        return false;
    }

    /// <summary>
    /// True when the session is visible to the caller. A scoped instructor (decision D18, GAD-T170) never sees another
    /// instructor's session; the refused visit is audited as access.denied. Actions that load only the grade record
    /// (FinalGrade is organization-wide) call this first, so an id from elsewhere cannot drive another instructor's grade.
    /// </summary>
    private async Task<bool> SessionVisibleAsync(Guid sessionId, CancellationToken ct)
    {
        var org = Org;
        if (await db.ExamSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId && s.OrganizationId == org, ct)) return true;
        await db.RefuseForeignAsync(OwnershipScope.Kind.Session, sessionId, "Opening a session for grading", ct);
        return false;
    }

    private static string Denied(Permission p) => $"Your role in this organization does not allow this ({p}). The attempt was recorded.";

    // ================================================================== separation of duties (GAD-T285)

    /// <summary>The audit action recorded when the single-grader organization setting lets a grade's scorer reconcile or approve it.</summary>
    public const string SeparationWaivedAction = "grade.separation-of-duties.waived";

    /// <summary>
    /// True when the organization has turned on "allow a single grader" (OrganizationSettings.AllowSingleGrader, off by default):
    /// a one-person organization may then reconcile and approve grades it scored itself, each time audited.
    /// </summary>
    public async Task<bool> SingleGraderAllowedAsync(CancellationToken ct = default)
    {
        var org = Org;
        return await db.OrganizationSettings.AsNoTracking().Where(s => s.OrganizationId == org).Select(s => s.AllowSingleGrader).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// GAD-T285: reconciliation and final approval are done by someone other than the person who entered the grade's human
    /// score (any human objective grade of the session, superseded ones included). When the caller did score it, the act is
    /// refused and audited, unless the organization allows a single grader, in which case it goes ahead and a waiver is
    /// audited with it (saved with the act). Null means go ahead.
    /// </summary>
    private async Task<GradingResult?> SeparationOfDutiesAsync(Guid sessionId, string acting, CancellationToken ct)
    {
        if (Actor.UserId is not { } me) return null;
        var org = Org;
        if (!await db.HumanObjectiveGrades.AsNoTracking().AnyAsync(h => h.SessionId == sessionId && h.OrganizationId == org && h.ReviewerUserId == me, ct))
            return null;
        if (await SingleGraderAllowedAsync(ct))
        {
            GradingAudit.Add(db, org, Actor, SeparationWaivedAction, "FinalGrade", sessionId.ToString(), sessionId, Now,
                after: new { acting, setting = "allowSingleGrader" },
                reason: $"{acting} by the person who entered this grade's human score, allowed by the organization's single-grader setting.");
            return null;
        }
        db.ChangeTracker.Clear();
        GradingAudit.Add(db, org, Actor, "grade.separation-of-duties.refused", "FinalGrade", sessionId.ToString(), sessionId, Now,
            after: new { acting }, reason: $"{acting} needs someone other than the person who entered this grade's human score.");
        await db.SaveChangesAsync(ct);
        return GradingResult.Fail($"Refused: you entered this grade's human score, so {acting.ToLowerInvariant()} must be done by someone else "
            + "(separation of duties). Ask another instructor, admin or second reviewer. The attempt was recorded.");
    }

    /// <summary>
    /// GAD-T444: whether <see cref="ApproverSeparation"/> stops <paramref name="approver"/> approving this grade because they
    /// reconciled it (or were the second reviewer on its reconciliation). Reads only; the caller audits.
    /// </summary>
    private async Task<ApproverSeparationDecision> ApproverSeparationForAsync(Guid sessionId, Guid approver, CancellationToken ct)
    {
        var org = Org;
        var reconcilers = await (from r in db.GradeReconciliations.AsNoTracking()
                                 join d in db.GradeDisagreements.AsNoTracking() on r.DisagreementId equals d.Id
                                 where r.OrganizationId == org && d.OrganizationId == org && d.SessionId == sessionId
                                 select new { r.ReviewerUserId, r.SecondReviewerUserId }).ToListAsync(ct);
        if (!reconcilers.Any(r => r.ReviewerUserId == approver || r.SecondReviewerUserId == approver)) return ApproverSeparationDecision.NotApplicable;
        var settings = await db.OrganizationSettings.AsNoTracking().Where(s => s.OrganizationId == org)
            .Select(s => new { s.ApproverSeparation, s.AllowSingleGrader }).FirstOrDefaultAsync(ct);
        var setting = settings?.ApproverSeparation ?? ApproverSeparationSetting.Automatic;
        var singleGrader = settings?.AllowSingleGrader ?? false;
        var another = false;
        if (setting == ApproverSeparationSetting.Automatic && !singleGrader)
        {
            var excluded = reconcilers.Select(r => r.ReviewerUserId).Concat(reconcilers.Select(r => r.SecondReviewerUserId).OfType<Guid>()).ToHashSet();
            excluded.Add(approver);
            foreach (var scorer in await db.HumanObjectiveGrades.AsNoTracking().Where(h => h.SessionId == sessionId && h.OrganizationId == org)
                         .Select(h => h.ReviewerUserId).Distinct().ToListAsync(ct))
                excluded.Add(scorer);
            var members = await db.OrganizationMembers.AsNoTracking().Where(m => m.OrganizationId == org && m.IsActive)
                .Select(m => new { m.UserId, m.Role }).ToListAsync(ct);
            another = members.Any(m => !excluded.Contains(m.UserId) && PermissionMatrix.Has(m.Role, Permission.ApproveFinalGrade));
        }
        return ApproverSeparation.Decide(setting, true, singleGrader, another);
    }

    /// <summary>
    /// GAD-T444: refuses (and audits) an approval by the person who reconciled the grade when the organization's setting
    /// requires a separate approver; when it lets the approval through, the waiver is audited with it. Null means go ahead.
    /// </summary>
    private async Task<GradingResult?> ReconcilerApproverAsync(Guid sessionId, CancellationToken ct)
    {
        if (Actor.UserId is not { } me) return null;
        var decision = await ApproverSeparationForAsync(sessionId, me, ct);
        if (!decision.Applies) return null;
        var org = Org;
        if (!decision.Refused)
        {
            GradingAudit.Add(db, org, Actor, ApproverSeparation.WaivedAction, "FinalGrade", sessionId.ToString(), sessionId, Now,
                after: new { acting = "Approving a final grade" }, reason: $"Approved by the person who reconciled this grade. {decision.Waiver}");
            return null;
        }
        db.ChangeTracker.Clear();
        GradingAudit.Add(db, org, Actor, ApproverSeparation.RefusedAction, "FinalGrade", sessionId.ToString(), sessionId, Now,
            after: new { acting = "Approving a final grade" }, reason: "Approving a final grade needs someone other than the person who reconciled it.");
        await db.SaveChangesAsync(ct);
        return GradingResult.Fail("Refused: you reconciled this grade, so approving it must be done by someone else "
            + "(separation of duties). Ask another instructor or admin. The attempt was recorded.");
    }

    // ================================================================== queue

    /// <summary>Sessions with a grade record, plus ended sessions not yet started, newest first.</summary>
    public async Task<IReadOnlyList<GradingQueueRow>?> QueueAsync(GradeStatus? status, bool includeUnstarted, CancellationToken ct = default) =>
        await QueueAsync(status, includeUnstarted, needsInstructorReviewOnly: false, ct);

    /// <summary>The queue; <paramref name="needsInstructorReviewOnly"/> keeps only attempts held for instructor review (GAD-T603).</summary>
    public async Task<IReadOnlyList<GradingQueueRow>?> QueueAsync(GradeStatus? status, bool includeUnstarted, bool needsInstructorReviewOnly, CancellationToken ct = default)
    {
        if (!await RequireAsync(Permission.ViewTranscripts, "Opening the grading queue", null, ct)) return null;
        var org = Org;
        var grades = db.FinalGrades.AsNoTracking().Where(g => g.OrganizationId == org);
        if (status is { } st) grades = grades.Where(g => g.Status == st);
        var graded = await (from g in grades
                            join s in db.ExamSessions.AsNoTracking() on g.SessionId equals s.Id
                            where !needsInstructorReviewOnly || s.NeedsInstructorReview
                            orderby s.EndedAt descending
                            select new { g.SessionId, g.Status, g.FinalPercent, s.ExamId, s.StudentUserId, s.Attempt, s.EndedAt, s.NeedsInstructorReview, s.ReviewReason, s.Language })
            .Take(500).ToListAsync(ct);
        var unstarted = !includeUnstarted || status is not null ? []
            : await db.ExamSessions.AsNoTracking()
                .Where(s => s.OrganizationId == org && s.State == SessionState.Ended && !db.FinalGrades.Any(g => g.SessionId == s.Id))
                .Where(s => !needsInstructorReviewOnly || s.NeedsInstructorReview)
                .OrderByDescending(s => s.EndedAt).Take(200)
                .Select(s => new { s.Id, s.ExamId, s.StudentUserId, s.Attempt, s.EndedAt, s.NeedsInstructorReview, s.ReviewReason, s.Language }).ToListAsync(ct);

        var ids = graded.Select(g => g.SessionId).ToList();
        var examIds = graded.Select(g => g.ExamId).Concat(unstarted.Select(u => u.ExamId)).Distinct().ToList();
        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && examIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Title, ct);
        var names = await NamesAsync(graded.Select(g => g.StudentUserId).Concat(unstarted.Select(u => u.StudentUserId)), ct);
        var open = (await db.GradeDisagreements.AsNoTracking()
                .Where(d => d.OrganizationId == org && ids.Contains(d.SessionId) && d.ResolvedAt == null && d.Band == DisagreementBand.MandatoryReconciliation)
                .Select(d => d.SessionId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var appeals = (await db.GradeAppeals.AsNoTracking().Where(a => a.OrganizationId == org && ids.Contains(a.SessionId) && a.State == AppealState.Open)
            .Select(a => a.SessionId).ToListAsync(ct)).ToHashSet();
        var mock = (await db.AiObjectiveGrades.AsNoTracking().Where(a => a.OrganizationId == org && ids.Contains(a.SessionId) && a.IsMock)
            .Select(a => a.SessionId).Distinct().ToListAsync(ct)).ToHashSet();
        var jobs = await LatestJobsAsync(ids, ct);
        // GAD-T439: under anonymous marking the queue shows a session code until the human score is submitted.
        var anonymous = await AnonymousMarking.HiddenSessionsAsync(db, org, [.. ids, .. unstarted.Select(u => u.Id)], Now, ct);

        var rows = graded.Select(g => new GradingQueueRow(g.SessionId, AnonymousMarking.NameOrCode(anonymous.Contains(g.SessionId), g.SessionId, names.GetValueOrDefault(g.StudentUserId)),
                exams.GetValueOrDefault(g.ExamId) ?? "(deleted exam)", g.Attempt, g.EndedAt, g.Status, g.FinalPercent, open.GetValueOrDefault(g.SessionId),
                appeals.Contains(g.SessionId), jobs.GetValueOrDefault(g.SessionId).State, jobs.GetValueOrDefault(g.SessionId).Error, mock.Contains(g.SessionId))
                { NeedsInstructorReview = g.NeedsInstructorReview, ReviewReason = g.ReviewReason, Language = g.Language })
            .Concat(unstarted.Select(u => new GradingQueueRow(u.Id, AnonymousMarking.NameOrCode(anonymous.Contains(u.Id), u.Id, names.GetValueOrDefault(u.StudentUserId)),
                exams.GetValueOrDefault(u.ExamId) ?? "(deleted exam)", u.Attempt, u.EndedAt, null, null, 0, false, null, null, false)
                { NeedsInstructorReview = u.NeedsInstructorReview, ReviewReason = u.ReviewReason, Language = u.Language }))
            .OrderByDescending(r => r.EndedAt).ToList();
        return rows;
    }

    private async Task<Dictionary<Guid, (string? State, string? Error)>> LatestJobsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0) return [];
        var jobs = await db.Jobs.AsNoTracking().Where(j => j.Kind == AiGradingJob.JobKind && j.OrganizationId == Org)
            .OrderByDescending(j => j.CreatedAt).Take(2000).Select(j => new { j.PayloadJson, j.State, j.Error }).ToListAsync(ct);
        var result = new Dictionary<Guid, (string?, string?)>();
        foreach (var j in jobs)
        {
            AiGradingPayload? p;
            try { p = System.Text.Json.JsonSerializer.Deserialize<AiGradingPayload>(j.PayloadJson); }
            catch (System.Text.Json.JsonException) { continue; }
            if (p is null || !sessionIds.Contains(p.SessionId) || result.ContainsKey(p.SessionId)) continue;
            result[p.SessionId] = (j.State.ToString(), j.Error);
        }
        return result;
    }

    /// <summary>Starts (or retries) AI grading of an ended session.</summary>
    public async Task<GradingResult> StartAiGradingAsync(Guid sessionId, CancellationToken ct = default)
    {
        var p = Can(Permission.ApproveFinalGrade) ? Permission.ApproveFinalGrade : Permission.EnterHumanScore;   // examiners and approvers
        if (!await RequireAsync(p, "Starting AI grading", sessionId, ct)) return GradingResult.Fail(Denied(Permission.EnterHumanScore));
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == Org, ct);
        if (session is null) await db.RefuseForeignAsync(OwnershipScope.Kind.Session, sessionId, "Opening a session for grading", ct);
        if (session is null) return GradingResult.Fail("That session was not found in this organization.");
        if (session.State != SessionState.Ended) return GradingResult.Fail("The examination hasn't ended yet.");
        var grade = await db.FinalGrades.AsNoTracking().FirstOrDefaultAsync(g => g.SessionId == sessionId, ct);
        if (grade is not null && grade.Status is not (GradeStatus.ExaminationCompleted or GradeStatus.AiGradingPending or GradeStatus.TranscriptVerificationPending))
            return GradingResult.Fail("AI grading has already finished for this session.");
        if (await GradingStarter.HasJobAsync(db, sessionId, activeOnly: true, ct)) return GradingResult.Done("AI grading is already queued or running.");
        if (grade is null) await GradeLifecycle.EnsureAsync(db, session, Actor, Now, ct);
        GradingAudit.Add(db, Org, Actor, "grade.ai.requested", "FinalGrade", sessionId.ToString(), sessionId, Now);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(AiGradingJob.JobKind, new AiGradingPayload(sessionId), Org, maxAttempts: 4, ct: ct);
        return GradingResult.Done("AI grading queued. Refresh in a minute.");
    }

    /// <summary>
    /// Releases a session held at Transcript Verification Pending: AI grading proceeds, the unresolved
    /// responses are excluded from evidence and flagged as unresolved ambiguities (audited with the reason).
    /// </summary>
    public async Task<GradingResult> ProceedWithUnresolvedAsync(Guid sessionId, string reason, CancellationToken ct = default)
    {
        var p = Can(Permission.ApproveFinalGrade) ? Permission.ApproveFinalGrade : Permission.EnterHumanScore;
        if (!await RequireAsync(p, "Proceeding to AI grading with unresolved responses", sessionId, ct)) return GradingResult.Fail(Denied(Permission.EnterHumanScore));
        if (string.IsNullOrWhiteSpace(reason)) return GradingResult.Fail("Give a reason for proceeding while responses are unresolved.");
        if (!await SessionVisibleAsync(sessionId, ct)) return GradingResult.Fail("That session was not found in this organization.");
        var grade = await db.FinalGrades.FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == Org, ct);
        if (grade is null) return GradingResult.Fail("This session has no grade record yet.");
        var moved = await TryMoveAsync(grade, GradeStatus.AiGradingPending, "Proceed with unresolved responses flagged: " + reason.Trim(), ct);
        if (moved is not null) return moved;
        await db.SaveChangesAsync(ct);
        if (!await GradingStarter.HasJobAsync(db, sessionId, activeOnly: true, ct))
            await queue.EnqueueAsync(AiGradingJob.JobKind, new AiGradingPayload(sessionId), Org, maxAttempts: 4, ct: ct);
        return GradingResult.Done("AI grading queued; the unresolved responses will be flagged, not used.");
    }

    // ================================================================== workspace

    public async Task<GradingWorkspace?> WorkspaceAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (!await RequireAsync(Permission.ViewTranscripts, "Opening a grading workspace", sessionId, ct)) return null;
        // GAD-T468: a blind second marker never sees this session's panel or first-examiner grades here.
        if (Actor.UserId is { } me && await SecondMarkerBlindness.IsSecondMarkerAsync(db, Org, sessionId, me, ct))
        {
            await GradingAudit.DeniedAsync(db, Org, Actor, Permission.ViewTranscripts, "Opening the grading page of a session the caller second-marked", sessionId, Now, ct);
            return null;
        }
        var w = await LoadAsync(sessionId, ct);
        if (w is not null) await SecondMarkerBlindness.NoteGradesSeenAsync(db, Org, Actor, w, Now, ct);
        return w;
    }

    /// <summary>True when the caller second-marked the session, so its grading page is closed to them (GAD-T468).</summary>
    public async Task<bool> IsSecondMarkerAsync(Guid sessionId, CancellationToken ct = default) =>
        tenant.OrganizationId is { } org && Actor.UserId is { } me && await SecondMarkerBlindness.IsSecondMarkerAsync(db, org, sessionId, me, ct);

    internal async Task<GradingWorkspace?> LoadAsync(Guid sessionId, CancellationToken ct)
    {
        var org = Org;
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == org, ct);
        if (session is null) await db.RefuseForeignAsync(OwnershipScope.Kind.Session, sessionId, "Opening a session for grading", ct);
        if (session is null) return null;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == session.ExamId && e.OrganizationId == org, ct);
        var grade = await db.FinalGrades.AsNoTracking().FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == org, ct);
        var policy = GradingJson.Read<PolicySnapshot>(grade?.PolicyJson)
                     ?? PolicySnapshot.From(await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == session.ExamId && p.OrganizationId == org, ct), Now);
        var aiVisible = AiVisible(policy, grade?.Status);
        var anonymous = AnonymousMarking.Hides(policy, grade?.Status);   // GAD-T439

        var turns = await db.SessionTurns.AsNoTracking().Where(t => t.SessionId == sessionId && t.OrganizationId == org)
            .Select(t => new { t.Id, t.Index, t.ConceptKey, t.Question }).ToDictionaryAsync(t => t.Id, ct);
        var responses = await db.StudentResponses.AsNoTracking().Include(r => r.Versions)
            .Where(r => r.SessionId == sessionId && r.OrganizationId == org).OrderBy(r => r.Sequence).ToListAsync(ct);
        var rIds = responses.Select(r => r.Id).ToList();
        var history = (await db.ResponseVerifications.AsNoTracking().Where(v => v.OrganizationId == org && rIds.Contains(v.ResponseId)).OrderBy(v => v.At).ToListAsync(ct))
            .ToLookup(v => v.ResponseId);
        var clarifications = (await db.ClarificationEvents.AsNoTracking().Where(c => c.SessionId == sessionId && c.OrganizationId == org).OrderBy(c => c.AskedAt).ToListAsync(ct))
            .ToLookup(c => c.ResponseId);
        var responseViews = responses.Select(r =>
        {
            var t = turns.GetValueOrDefault(r.TurnId);
            return new ResponseView(r, t?.Index ?? -1, t?.ConceptKey ?? "", t?.Question ?? "", r.Versions.OrderBy(v => v.Version).ToList(),
                history[r.Id].ToList(), clarifications[r.Id].ToList());
        }).ToList();

        var objectives = exam is null ? [] : await GradingInputs.LoadObjectivesAsync(db, exam.Id, org, ct);
        var ai = (await db.AiObjectiveGrades.AsNoTracking().Where(a => a.SessionId == sessionId && a.OrganizationId == org).ToListAsync(ct))
            .GroupBy(a => a.ObjectiveId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.CreatedAt).First());
        var humans = await db.HumanObjectiveGrades.AsNoTracking().Where(h => h.SessionId == sessionId && h.OrganizationId == org).OrderBy(h => h.SubmittedAt).ToListAsync(ct);
        var evidence = (await db.AssessmentEvidence.AsNoTracking().Where(e => e.SessionId == sessionId && e.OrganizationId == org).ToListAsync(ct)).ToLookup(e => e.ObjectiveId);
        var allDisagreements = await db.GradeDisagreements.AsNoTracking().Where(d => d.SessionId == sessionId && d.OrganizationId == org).ToListAsync(ct);
        var disagreements = allDisagreements
            .GroupBy(d => d.ObjectiveId).ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.DetectedAt).First());
        var dIds = disagreements.Values.Select(d => d.Id).ToList();
        var reconciliations = (await db.GradeReconciliations.AsNoTracking().Where(r => r.OrganizationId == org && dIds.Contains(r.DisagreementId)).OrderBy(r => r.At).ToListAsync(ct))
            .ToLookup(r => r.DisagreementId);
        // The score uses the objective's most recent reconciliation of any of its disagreements, as approval does (ComputeAsync).
        var objectiveOf = allDisagreements.ToDictionary(d => d.Id, d => d.ObjectiveId);
        var allIds = objectiveOf.Keys.ToList();
        var latestRec = (await db.GradeReconciliations.AsNoTracking().Where(r => r.OrganizationId == org && allIds.Contains(r.DisagreementId)).ToListAsync(ct))
            .GroupBy(r => objectiveOf[r.DisagreementId]).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.At).First());

        var states = objectives.Select(o =>
        {
            var d = disagreements.GetValueOrDefault(o.Id);
            var recs = d is null ? [] : reconciliations[d.Id].ToList();
            return (Objective: o, Ai: ai.GetValueOrDefault(o.Id), Human: CurrentHuman(humans, o.Id), Disagreement: d, Recs: recs);
        }).ToList();
        var (combined, excluded) = TryCompute(policy, states.Select(s => (s.Objective, s.Ai, s.Human, latestRec.GetValueOrDefault(s.Objective.Id))).ToList());
        var objectiveViews = states.Select(s => new ObjectiveView(s.Objective, s.Ai is null ? null : AiGradeView.From(s.Ai), s.Human,
            humans.Where(h => h.ObjectiveId == s.Objective.Id).ToList(),
            aiVisible ? evidence[s.Objective.Id].ToList() : [],   // D25 (GAD-T218): the exam-time judge's stance and notes stay hidden while blind
            combined?.Objectives.FirstOrDefault(r => r.ObjectiveKey == s.Objective.Key), excluded.Contains(s.Objective.Key), s.Disagreement, s.Recs)).ToList();

        var approvals = await db.ApprovalRecords.AsNoTracking().Where(a => a.SessionId == sessionId && a.OrganizationId == org).OrderBy(a => a.At).ToListAsync(ct);
        var appeals = await db.GradeAppeals.AsNoTracking().Where(a => a.SessionId == sessionId && a.OrganizationId == org).OrderBy(a => a.OpenedAt).ToListAsync(ct);
        var audit = await db.AuditEvents.AsNoTracking().Where(a => a.SessionId == sessionId && a.OrganizationId == org).OrderBy(a => a.Id).Take(500).ToListAsync(ct);
        var secondReviewers = await (from m in db.OrganizationMembers.AsNoTracking()
                                     join u in db.AuthUsers.AsNoTracking() on m.UserId equals u.Id
                                     where m.OrganizationId == org && m.IsActive && m.Role == OrgRoles.SecondReviewer
                                     orderby u.UserName
                                     select new { m.UserId, u.UserName }).ToListAsync(ct);
        var requested = new Dictionary<Guid, Guid>();
        foreach (var e in audit.Where(e => e.Action == "reconciliation.second-review.requested"))
            if (Guid.TryParse(e.EntityId, out var did) && GradingJson.Read<SecondReviewRequest>(e.AfterJson) is { } req) requested[did] = req.ReviewerUserId;

        var names = await NamesAsync([session.StudentUserId, .. humans.Select(h => h.ReviewerUserId), .. approvals.Select(a => a.ActorUserId),
            .. appeals.Select(a => a.StudentUserId), .. appeals.Select(a => a.ResolvedByUserId).OfType<Guid>(), .. audit.Select(a => a.ActorUserId).OfType<Guid>(),
            .. reconciliations.SelectMany(g => g).Select(r => r.ReviewerUserId), .. reconciliations.SelectMany(g => g).Select(r => r.SecondReviewerUserId).OfType<Guid>(),
            .. requested.Values], ct);
        var jobs = await LatestJobsAsync([sessionId], ct);
        var prior = await PriorAttemptsAsync(session, ct);
        var studentName = AnonymousMarking.NameOrCode(anonymous, sessionId, names.GetValueOrDefault(session.StudentUserId));
        if (anonymous) names.Remove(session.StudentUserId);   // no other line of the page may name the student either

        return new GradingWorkspace
        {
            Session = session, ExamTitle = exam?.Title ?? "(deleted exam)", StudentName = studentName, StudentAnonymous = anonymous,
            Grade = grade, Policy = policy, AiVisible = aiVisible, Responses = responseViews, Objectives = objectiveViews, Combined = combined,
            Approvals = approvals.Select(a => (a, names.GetValueOrDefault(a.ActorUserId) ?? "(unknown)")).ToList(),
            Appeals = appeals.Select(a => (a, anonymous && a.StudentUserId == session.StudentUserId ? studentName : names.GetValueOrDefault(a.StudentUserId) ?? "(unknown)")).ToList(),
            // Blind grading: the AI-grade audit entries carry its scores, so they stay hidden with the grades.
            // Blind double marking (GAD-T428, GAD-T480): a second mark's level and comment never show on the grading page, or the first
            // examiner's "blind" grade could be anchored on it; they are read only on the double-marking results page.
            Audit = audit.Where(a => (aiVisible || a.Action != "grade.ai.recorded") && a.Action != DoubleMarkingService.MarkAction)
                .Select(a => (a, a.ActorUserId is { } u ? names.GetValueOrDefault(u) ?? "(unknown)" : "Gadfly")).ToList(),
            Names = names, SecondReviewers = secondReviewers.Select(s => (s.UserId, s.UserName)).ToList(), RequestedSecondReviews = requested,
            JobState = jobs.GetValueOrDefault(sessionId).State, JobError = jobs.GetValueOrDefault(sessionId).Error, Viewer = Actor,
            PriorAttempts = prior,
            ViewerScoredHuman = Actor.UserId is { } viewer && humans.Any(h => h.ReviewerUserId == viewer),
            SingleGraderAllowed = await SingleGraderAllowedAsync(ct),
            ReconcilerApproval = Actor.UserId is { } approver ? await ApproverSeparationForAsync(sessionId, approver, ct) : ApproverSeparationDecision.NotApplicable,
        };
    }

    /// <summary>
    /// The student's earlier attempts on the same assignment that have a computed result, latest first (GAD-T273):
    /// their per-objective combined scores, whether the student may see them, and whether their feedback was
    /// published before this attempt started (retakes without feedback give about no benefit: research note 03).
    /// </summary>
    private async Task<IReadOnlyList<PriorAttempt>> PriorAttemptsAsync(ExamSession session, CancellationToken ct)
    {
        if (session.Attempt <= 1) return [];
        var org = Org;
        var rows = await (from s in db.ExamSessions.AsNoTracking()
                          join g in db.FinalGrades.AsNoTracking() on s.Id equals g.SessionId
                          where s.OrganizationId == org && g.OrganizationId == org && s.AssignmentId == session.AssignmentId
                                && s.StudentUserId == session.StudentUserId && s.Attempt < session.Attempt && g.ResultJson != null
                          orderby s.Attempt descending
                          select new { s.Id, s.Attempt, g.Status, g.ResultJson }).Take(50).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.Id).ToList();
        var approvals = await db.ApprovalRecords.AsNoTracking()
            .Where(a => a.OrganizationId == org && ids.Contains(a.SessionId) && (a.Action == ApprovalAction.Publish || a.Action == ApprovalAction.Finalize))
            .Select(a => new { a.SessionId, a.Action, a.At }).ToListAsync(ct);
        var started = session.StartedAt ?? session.CreatedAt;
        var result = new List<PriorAttempt>();
        foreach (var r in rows)
        {
            if (GradingJson.Read<StoredResult>(r.ResultJson)?.Result is not { } stored) continue;
            var published = approvals.Where(a => a.SessionId == r.Id && a.Action == ApprovalAction.Publish).Select(a => (DateTimeOffset?)a.At).Min();
            var finalized = approvals.Any(a => a.SessionId == r.Id && a.Action == ApprovalAction.Finalize);
            var visible = published is not null && finalized && r.Status is GradeStatus.Published or GradeStatus.UnderAppeal or GradeStatus.Amended;
            var combined = new Dictionary<string, decimal?>(StringComparer.Ordinal);
            foreach (var o in stored.Objectives) combined[o.ObjectiveKey] = o.Combined;
            result.Add(new PriorAttempt(r.Id, r.Attempt, combined, visible, published is { } p && p <= started));
        }
        return result;
    }

    private sealed record SecondReviewRequest(Guid ReviewerUserId);

    /// <summary>Blind grading (Part 8.3): the AI's evaluation stays hidden until the human evaluation is submitted.</summary>
    public static bool AiVisible(PolicySnapshot policy, GradeStatus? status) =>
        !policy.Rules.BlindHumanGrading
        || status is not (null or GradeStatus.ExaminationInProgress or GradeStatus.ExaminationCompleted or GradeStatus.TranscriptVerificationPending
            or GradeStatus.AiGradingPending or GradeStatus.HumanReviewPending);

    /// <summary>The current human grade of an objective: the newest one no later grade supersedes.</summary>
    public static HumanObjectiveGrade? CurrentHuman(IReadOnlyList<HumanObjectiveGrade> all, Guid objectiveId)
    {
        var mine = all.Where(h => h.ObjectiveId == objectiveId).ToList();
        var superseded = mine.Where(h => h.SupersedesId is not null).Select(h => h.SupersedesId!.Value).ToHashSet();
        return mine.Where(h => !superseded.Contains(h.Id)).OrderByDescending(h => h.SubmittedAt).FirstOrDefault();
    }

    // ================================================================== combined scoring (Parts 9-10)

    /// <summary>
    /// Per-objective AI and human scores into <see cref="CombinedScoring"/>. A documented reconciliation's
    /// resolved scores replace the originals. When the AI found insufficient evidence (Level 0) its score is
    /// left out: the objective's combined score is the human's (Part 5.3: insufficient evidence is not zero
    /// understanding; Part 6.4: the inconclusive procedure is human review).
    /// </summary>
    public static (CombinedResult? Result, HashSet<string> AiExcluded) TryCompute(PolicySnapshot policy,
        IReadOnlyList<(LearningObjective Objective, AiObjectiveGrade? Ai, HumanObjectiveGrade? Human, GradeReconciliation? Rec)> states)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        if (states.Count == 0) return (null, excluded);
        var scores = new List<ObjectiveScores>();
        foreach (var s in states)
        {
            // A human grade submitted after the reconciliation (an amendment) supersedes its resolved score (GAD-T148).
            var amendedAfterRec = s.Rec is not null && s.Human is not null && s.Human.SubmittedAt > s.Rec.At;
            decimal? human = amendedAfterRec ? s.Human!.Score : s.Rec?.ResolvedHumanScore ?? s.Human?.Score;
            decimal? ai = s.Rec?.ResolvedAiScore;
            if (ai is null)
            {
                if (s.Ai is null || s.Ai.Level == MasteryLevel.InsufficientEvidence) { excluded.Add(s.Objective.Key); ai = human; }
                else ai = s.Ai.Score;
            }
            scores.Add(new ObjectiveScores(s.Objective.Key, s.Objective.MaxScore, s.Objective.Weight, ai, human));
        }
        try { return (CombinedScoring.Calculate(scores, policy.Rules), excluded); }
        catch (ArgumentException) { return (null, excluded); }
    }

    private async Task<(CombinedResult Result, HashSet<string> Excluded, List<(LearningObjective Objective, AiObjectiveGrade? Ai, HumanObjectiveGrade? Human, GradeDisagreement? Disagreement)> States)>
        ComputeAsync(ExamSession session, PolicySnapshot policy, CancellationToken ct)
    {
        var objectives = await GradingInputs.LoadObjectivesAsync(db, session.ExamId, Org, ct);
        var ai = (await db.AiObjectiveGrades.AsNoTracking().Where(a => a.SessionId == session.Id && a.OrganizationId == Org).ToListAsync(ct))
            .GroupBy(a => a.ObjectiveId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.CreatedAt).First());
        // Tracked + newly added rows, so a submission in progress is included.
        var humans = (await db.HumanObjectiveGrades.Where(h => h.SessionId == session.Id && h.OrganizationId == Org).ToListAsync(ct))
            .Concat(db.ChangeTracker.Entries<HumanObjectiveGrade>().Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .DistinctBy(h => h.Id).ToList();
        var allDisagreements = await db.GradeDisagreements.Where(d => d.SessionId == session.Id && d.OrganizationId == Org).ToListAsync(ct);
        var disagreements = allDisagreements
            .GroupBy(d => d.ObjectiveId).ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.DetectedAt).First());
        // The objective's most recent reconciliation, of any of its disagreements: a resubmission after a reconciliation
        // records a new disagreement, and the resolved scores must not be lost with it (the resubmission was compared against them).
        var objectiveOf = allDisagreements.ToDictionary(d => d.Id, d => d.ObjectiveId);
        var dIds = objectiveOf.Keys.ToList();
        var recs = (await db.GradeReconciliations.AsNoTracking().Where(r => dIds.Contains(r.DisagreementId) && r.OrganizationId == Org).ToListAsync(ct))
            .Concat(db.ChangeTracker.Entries<GradeReconciliation>().Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .Where(r => objectiveOf.ContainsKey(r.DisagreementId))
            .GroupBy(r => objectiveOf[r.DisagreementId]).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.At).First());
        var states = objectives.Select(o =>
        {
            var d = disagreements.GetValueOrDefault(o.Id);
            return (o, ai.GetValueOrDefault(o.Id), CurrentHuman(humans, o.Id), d, recs.GetValueOrDefault(o.Id));
        }).ToList();
        var (result, excluded) = TryCompute(policy, states.Select(s => (s.o, s.Item2, s.Item3, s.Item5)).ToList());
        if (result is null) throw new InvalidOperationException("The combined score could not be computed (check the objectives' maximum scores and weights).");
        return (result, excluded, states.Select(s => (s.o, s.Item2, s.Item3, s.d)).ToList());
    }

    private void StoreResult(FinalGrade grade, CombinedResult result, HashSet<string> excluded)
    {
        grade.FinalPercent = result.FinalPercent;
        grade.ResultJson = GradingJson.Write(new StoredResult(result, [.. excluded], Now));
        grade.UpdatedAt = Now;
    }

    /// <summary>
    /// Records a disagreement row per objective for the newly computed comparison (Part 10.1) and closes the
    /// previous comparison's open rows. Standard-band rows are resolved at once; others stay open for review.
    /// GAD-T270: an objective whose panel is split or whose score sits on a boundary
    /// (<see cref="ReconciliationTriggers"/>) is recorded as needing reconciliation whatever its difference band,
    /// unless it was already reconciled once (the triggers would fire again on the same panel grade). Returns
    /// whether any trigger fired.
    /// </summary>
    private bool RecordDisagreements(Guid sessionId, CombinedResult result, HashSet<string> excluded,
        IReadOnlyList<(LearningObjective Objective, AiObjectiveGrade? Ai, HumanObjectiveGrade? Human, GradeDisagreement? Disagreement)> states, string reason,
        PolicySnapshot policy, IReadOnlySet<Guid> reconciledObjectives)
    {
        var triggeredAny = false;
        foreach (var s in states)
        {
            if (s.Disagreement is { ResolvedAt: null } old)
            {
                old.ResolvedAt = Now;
                GradingAudit.Add(db, Org, Actor, "grade.disagreement.superseded", "GradeDisagreement", old.Id.ToString(), sessionId, Now, reason: reason);
            }
            var r = result.Objectives.FirstOrDefault(x => x.ObjectiveKey == s.Objective.Key);
            if (r?.DifferencePoints is not { } diff || r.Band is not { } band || excluded.Contains(s.Objective.Key)) continue;
            var panelLevels = s.Ai is null ? [] : AiGradeView.From(s.Ai).Voters.Select(v => (int)v.Level).ToList();
            var triggers = reconciledObjectives.Contains(s.Objective.Id) ? [] : ReconciliationTriggers.Evaluate(r, panelLevels, policy.Rules);
            if (triggers.Count > 0 && band != DisagreementBand.MandatoryReconciliation)
            {
                band = DisagreementBand.MandatoryReconciliation;
                triggeredAny = true;
            }
            var d = new GradeDisagreement
            {
                OrganizationId = Org, SessionId = sessionId, ObjectiveId = s.Objective.Id, AiScore = r.AiScore!.Value, HumanScore = r.HumanScore!.Value,
                DifferencePoints = diff, Band = band, DetectedAt = Now, ResolvedAt = band == DisagreementBand.Standard ? Now : null,
            };
            db.GradeDisagreements.Add(d);
            GradingAudit.Add(db, Org, Actor, "grade.disagreement.detected", "GradeDisagreement", d.Id.ToString(), sessionId, Now,
                after: new { objective = s.Objective.Key, ai = d.AiScore, human = d.HumanScore, differencePoints = diff, band = band.ToString(), triggers, panelLevels },
                reason: triggers.Count == 0 ? null : string.Join(" ", triggers.Select(ReconciliationTriggers.Describe)));
        }
        return triggeredAny;
    }

    // ================================================================== human grading (Part 8)

    /// <summary>
    /// Submits the human examiner's evaluation of every objective (Part 8.3 step 3). Earlier grades are kept:
    /// a new grade supersedes the previous one. Then the combined score and disagreements are computed and the
    /// grade moves to Reconciliation Required or Awaiting Final Approval (Part 10.4).
    /// </summary>
    public async Task<GradingResult> SubmitHumanGradesAsync(Guid sessionId, IReadOnlyList<HumanGradeInput> inputs, CancellationToken ct = default)
    {
        if (!await RequireAsync(Permission.EnterHumanScore, "Entering human scores", sessionId, ct)) return GradingResult.Fail(Denied(Permission.EnterHumanScore));
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == Org, ct);
        var grade = await db.FinalGrades.FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == Org, ct);
        if (session is null) await db.RefuseForeignAsync(OwnershipScope.Kind.Session, sessionId, "Opening a session for grading", ct);
        if (session is null || grade is null) return GradingResult.Fail("That session has no grade record in this organization.");
        if (grade.Status != GradeStatus.HumanReviewPending)
            return GradingResult.Fail($"Human scores are entered while the grade is Human Review Pending (it is {GradeStatusMachine.Label(grade.Status)}).");
        var policy = GradingJson.Read<PolicySnapshot>(grade.PolicyJson) ?? new PolicySnapshot { TakenAt = Now };
        var objectives = await GradingInputs.LoadObjectivesAsync(db, session.ExamId, Org, ct);
        if (objectives.Count == 0) return GradingResult.Fail("This exam has no learning objectives to grade.");

        var byObjective = inputs.GroupBy(i => i.ObjectiveId).ToDictionary(g => g.Key, g => g.Last());
        foreach (var o in objectives)
        {
            if (!byObjective.TryGetValue(o.Id, out var i) || i.Score is not { } sc) return GradingResult.Fail($"Enter a score for every objective (missing: {o.Title}).");
            if (sc < 0 || sc > o.MaxScore) return GradingResult.Fail($"The score for {o.Title} must be between 0 and {o.MaxScore:0.##}.");
            if (!Enum.IsDefined(i.Level)) return GradingResult.Fail($"Choose a level for {o.Title}.");
            if (i.Comment is { Length: > 4000 } || i.EvidenceNote is { Length: > 4000 }) return GradingResult.Fail("Keep comments and evidence notes under 4,000 characters.");
        }

        var existing = await db.HumanObjectiveGrades.AsNoTracking().Where(h => h.SessionId == sessionId && h.OrganizationId == Org).ToListAsync(ct);
        // Only a first submission can be blind: after a reopen the examiner has already seen the AI's scores (GAD-T183).
        var wasBlind = !AiVisible(policy, grade.Status) && existing.Count == 0;
        foreach (var o in objectives)
        {
            var i = byObjective[o.Id];
            var previous = CurrentHuman(existing, o.Id);
            var row = new HumanObjectiveGrade
            {
                OrganizationId = Org, SessionId = sessionId, ObjectiveId = o.Id, ReviewerUserId = Actor.UserId!.Value, ReviewerRole = Actor.Role ?? "",
                Level = i.Level, Score = Math.Round(i.Score!.Value, 2, MidpointRounding.AwayFromZero), MaxScore = o.MaxScore,
                Comment = Blank(i.Comment), EvidenceNote = Blank(i.EvidenceNote), WasBlind = wasBlind, SupersedesId = previous?.Id, SubmittedAt = Now,
            };
            db.HumanObjectiveGrades.Add(row);
            GradingAudit.Add(db, Org, Actor, previous is null ? "grade.human.recorded" : "grade.human.adjusted", "HumanObjectiveGrade", row.Id.ToString(), sessionId, Now,
                previous is null ? null : new { level = previous.Level.ToString(), score = previous.Score },
                new { objective = o.Key, level = row.Level.ToString(), score = row.Score, maxScore = row.MaxScore, wasBlind, supersedes = previous?.Id });
        }

        var (result, excluded, states) = await ComputeAsync(session, policy, ct);
        var reconciled = (await (from rec in db.GradeReconciliations.AsNoTracking()
                                 join dis in db.GradeDisagreements.AsNoTracking() on rec.DisagreementId equals dis.Id
                                 where dis.SessionId == sessionId && dis.OrganizationId == Org && rec.OrganizationId == Org
                                 select dis.ObjectiveId).Distinct().ToListAsync(ct)).ToHashSet();
        var triggered = RecordDisagreements(sessionId, result, excluded, states, "A new human evaluation was submitted.", policy, reconciled);
        StoreResult(grade, result, excluded);
        var requires = result.RequiresReconciliation || triggered;
        var next = requires ? GradeStatus.ReconciliationRequired : GradeStatus.AwaitingFinalApproval;
        var refused = await TryMoveAsync(grade, next, result.RequiresReconciliation
            ? "AI and human scores differ by at least the reconciliation threshold; finalization is blocked until reconciled."
            : triggered ? "A split panel or a score on a grade boundary sends an objective to reconciliation (GAD-T270); finalization is blocked until reconciled."
            : "The human evaluation is in; the weighted combined score awaits final approval.", ct);
        if (refused is not null) return refused;
        var saved = await SaveAsync(ct);
        if (saved is not null) return saved;
        return GradingResult.Done(result.RequiresReconciliation
            ? "Human grades saved. The AI and human scores disagree substantially: reconciliation is required before this grade can be finalized."
            : triggered ? "Human grades saved. An objective's panel was split or its score sits on a grade boundary: reconciliation is required before this grade can be finalized."
            : $"Human grades saved. Combined score {result.FinalPercent:0.##}%: awaiting final approval.");
    }

    /// <summary>Reopens human grading so the examiner can adjust their scores (new rows supersede the old).</summary>
    public async Task<GradingResult> ReopenHumanReviewAsync(Guid sessionId, string reason, CancellationToken ct = default)
    {
        if (!await RequireAsync(Permission.EnterHumanScore, "Reopening human grading", sessionId, ct)) return GradingResult.Fail(Denied(Permission.EnterHumanScore));
        if (string.IsNullOrWhiteSpace(reason)) return GradingResult.Fail("Say why the human grades are being adjusted.");
        if (!await SessionVisibleAsync(sessionId, ct)) return GradingResult.Fail("That session was not found in this organization.");
        var grade = await db.FinalGrades.FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == Org, ct);
        if (grade is null) return GradingResult.Fail("This session has no grade record yet.");
        var refused = await TryMoveAsync(grade, GradeStatus.HumanReviewPending, "Human grades reopened for adjustment: " + reason.Trim(), ct);
        return refused ?? await SaveAsync(ct) ?? GradingResult.Done("Human grading reopened. Submit the adjusted scores; earlier ones are kept in the history.");
    }

    // ================================================================== reconciliation (Part 10.3)

    public async Task<GradingResult> RequestSecondReviewAsync(Guid sessionId, Guid disagreementId, Guid reviewerUserId, CancellationToken ct = default)
    {
        if (!await RequireAsync(Permission.ReconcileDisagreement, "Requesting a second review", sessionId, ct)) return GradingResult.Fail(Denied(Permission.ReconcileDisagreement));
        if (!await SessionVisibleAsync(sessionId, ct)) return GradingResult.Fail("That session was not found in this organization.");
        var d = await db.GradeDisagreements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == disagreementId && x.SessionId == sessionId && x.OrganizationId == Org, ct);
        if (d is null || d.ResolvedAt is not null) return GradingResult.Fail("That disagreement is not open.");
        var isSecondReviewer = await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == Org && m.UserId == reviewerUserId && m.IsActive && m.Role == OrgRoles.SecondReviewer, ct);
        if (!isSecondReviewer) return GradingResult.Fail("Choose a member who holds the Second Reviewer role.");
        GradingAudit.Add(db, Org, Actor, "reconciliation.second-review.requested", "GradeDisagreement", disagreementId.ToString(), sessionId, Now,
            after: new SecondReviewRequest(reviewerUserId));
        return await SaveAsync(ct) ?? GradingResult.Done("Second review requested.");
    }

    /// <summary>
    /// Documents how a disagreement was resolved (cause, resolution, optional resolved scores), recalculates,
    /// and moves the grade to Awaiting Final Approval once no mandatory disagreement remains open.
    /// </summary>
    public async Task<GradingResult> ReconcileAsync(Guid sessionId, Guid disagreementId, string cause, string resolution, decimal? resolvedAiScore,
        decimal? resolvedHumanScore, Guid? secondReviewerUserId, CancellationToken ct = default)
    {
        var permission = Can(Permission.ReconcileDisagreement) ? Permission.ReconcileDisagreement : Permission.ActAsSecondReviewer;
        if (!await RequireAsync(permission, "Reconciling a grade disagreement", sessionId, ct)) return GradingResult.Fail(Denied(Permission.ReconcileDisagreement));
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == Org, ct);
        var grade = await db.FinalGrades.FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == Org, ct);
        var d = await db.GradeDisagreements.FirstOrDefaultAsync(x => x.Id == disagreementId && x.SessionId == sessionId && x.OrganizationId == Org, ct);
        if (session is null) await db.RefuseForeignAsync(OwnershipScope.Kind.Session, sessionId, "Opening a session for grading", ct);
        if (session is null || grade is null || d is null) return GradingResult.Fail("That disagreement was not found.");
        if (d.ResolvedAt is not null) return GradingResult.Fail("That disagreement is already resolved.");
        if (grade.Status is not (GradeStatus.ReconciliationRequired or GradeStatus.AwaitingFinalApproval))
            return GradingResult.Fail($"Disagreements are reconciled before final approval (the grade is {GradeStatusMachine.Label(grade.Status)}).");
        if (!ReconciliationCauses.IsValid(cause)) return GradingResult.Fail("Choose what caused the disagreement.");
        if (string.IsNullOrWhiteSpace(resolution) || resolution.Length > 4000) return GradingResult.Fail("Document the resolution (up to 4,000 characters).");
        var objective = await db.LearningObjectives.AsNoTracking().FirstAsync(o => o.Id == d.ObjectiveId, ct);
        foreach (var s in new[] { resolvedAiScore, resolvedHumanScore })
            if (s is { } v && (v < 0 || v > objective.MaxScore)) return GradingResult.Fail($"Resolved scores must be between 0 and {objective.MaxScore:0.##}.");
        Guid? second = Actor.Role == OrgRoles.SecondReviewer ? Actor.UserId : secondReviewerUserId;
        if (second is { } sid && sid != Actor.UserId
            && !await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == Org && m.UserId == sid && m.IsActive && m.Role == OrgRoles.SecondReviewer, ct))
            return GradingResult.Fail("The second reviewer must hold the Second Reviewer role.");
        // Last check before anything is written, so a single-grader waiver is only audited with an act that goes ahead.
        if (await SeparationOfDutiesAsync(sessionId, "Reconciling a grade disagreement", ct) is { } separated) return separated;

        var rec = new GradeReconciliation
        {
            OrganizationId = Org, DisagreementId = d.Id, ReviewerUserId = Actor.UserId!.Value, SecondReviewerUserId = second, Cause = cause,
            // The same rounding a submitted human score gets (half away from zero), so 2.345 is 2.35 here too (GAD-T315).
            Resolution = resolution.Trim(), ResolvedAiScore = resolvedAiScore is { } a ? Math.Round(a, 2, MidpointRounding.AwayFromZero) : null,
            ResolvedHumanScore = resolvedHumanScore is { } h ? Math.Round(h, 2, MidpointRounding.AwayFromZero) : null, At = Now,
        };
        db.GradeReconciliations.Add(rec);
        d.ResolvedAt = Now;
        GradingAudit.Add(db, Org, Actor, "grade.reconciled", "GradeDisagreement", d.Id.ToString(), sessionId, Now,
            new { ai = d.AiScore, human = d.HumanScore, band = d.Band.ToString() },
            new { objective = objective.Key, cause, resolvedAi = rec.ResolvedAiScore, resolvedHuman = rec.ResolvedHumanScore, secondReviewer = second },
            rec.Resolution);

        var policy = GradingJson.Read<PolicySnapshot>(grade.PolicyJson) ?? new PolicySnapshot { TakenAt = Now };
        var (result, excluded, _) = await ComputeAsync(session, policy, ct);
        StoreResult(grade, result, excluded);
        GradingAudit.Add(db, Org, Actor, "grade.recalculated", "FinalGrade", sessionId.ToString(), sessionId, Now, after: new { finalPercent = result.FinalPercent });
        var stillOpen = await db.GradeDisagreements.AnyAsync(x => x.SessionId == sessionId && x.OrganizationId == Org && x.Id != d.Id
                                                               && x.ResolvedAt == null && x.Band == DisagreementBand.MandatoryReconciliation, ct);
        if (!stillOpen && grade.Status == GradeStatus.ReconciliationRequired)
        {
            var refused = await TryMoveAsync(grade, GradeStatus.AwaitingFinalApproval, "Every mandatory disagreement has a documented resolution.", ct);
            if (refused is not null) return refused;
        }
        return await SaveAsync(ct) ?? GradingResult.Done(stillOpen
            ? "Resolution recorded. Other disagreements still need reconciliation."
            : $"Resolution recorded. Recalculated combined score {result.FinalPercent:0.##}%: awaiting final approval.");
    }

    // ================================================================== approval and publication (Part 11)

    public async Task<GradingResult> ApproveAsync(Guid sessionId, string? label, string? note, CancellationToken ct = default)
    {
        if (!await RequireAsync(Permission.ApproveFinalGrade, "Approving a final grade", sessionId, ct)) return GradingResult.Fail(Denied(Permission.ApproveFinalGrade));
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == Org, ct);
        var grade = await db.FinalGrades.FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == Org, ct);
        if (session is null) await db.RefuseForeignAsync(OwnershipScope.Kind.Session, sessionId, "Opening a session for grading", ct);
        if (session is null || grade is null) return GradingResult.Fail("That session has no grade record in this organization.");
        // Part 10.4, checked again here: never finalize over an open mandatory disagreement.
        if (await db.GradeDisagreements.AnyAsync(x => x.SessionId == sessionId && x.OrganizationId == Org && x.ResolvedAt == null && x.Band == DisagreementBand.MandatoryReconciliation, ct))
            return await RefuseAsync(grade, GradeStatus.Finalized, "An AI-human disagreement above the reconciliation threshold is unresolved.", ct);
        var policy = GradingJson.Read<PolicySnapshot>(grade.PolicyJson) ?? new PolicySnapshot { TakenAt = Now };
        var (result, excluded, _) = await ComputeAsync(session, policy, ct);
        if (result.FinalPercent is null) return GradingResult.Fail("Every objective needs both evaluations before approval.");
        if (label is { Length: > 40 }) return GradingResult.Fail("Keep the grade label under 40 characters.");
        if (await SeparationOfDutiesAsync(sessionId, "Approving a final grade", ct) is { } separated) return separated;
        if (await ReconcilerApproverAsync(sessionId, ct) is { } reconciler) return reconciler;   // GAD-T444
        // GAD-T603: an attempt marked for instructor review (window absences, cross-lingual scores) is decided by a person here: the
        // approval clears the mark, so it needs a note saying what was looked at; the decision is audited beside the approval.
        if (session.NeedsInstructorReview)
        {
            if (Blank(note) is null)
                return GradingResult.Fail($"This attempt is marked \"{Review.InstructorReview.Badge}\": add a note saying what you looked at (the reason, the absences and the student's explanation) before approving.");
            var tracked = await db.ExamSessions.FirstAsync(s => s.Id == sessionId && s.OrganizationId == Org, ct);
            Review.InstructorReview.Decide(db, Org, Actor, tracked, Review.InstructorReview.DecidedByApprovalAction, note!.Trim(), Now);
        }
        StoreResult(grade, result, excluded);
        grade.LetterOrLabel = Blank(label);
        var refused = await TryMoveAsync(grade, GradeStatus.Finalized, Blank(note) ?? "Final grade approved.", ct);
        if (refused is not null) return refused;
        AddApproval(sessionId, ApprovalAction.Finalize, note);
        GradingAudit.Add(db, Org, Actor, "grade.approved", "FinalGrade", sessionId.ToString(), sessionId, Now,
            after: new { finalPercent = grade.FinalPercent, label = grade.LetterOrLabel }, reason: Blank(note));
        return await SaveAsync(ct) ?? GradingResult.Done($"Final grade approved: {grade.FinalPercent:0.##}%{(grade.LetterOrLabel is { } l ? $" ({l})" : "")}. Publish it to release it to the student.");
    }

    public async Task<GradingResult> PublishAsync(Guid sessionId, string? note, CancellationToken ct = default)
    {
        if (!await RequireAsync(Permission.PublishGrade, "Publishing a grade", sessionId, ct)) return GradingResult.Fail(Denied(Permission.PublishGrade));
        if (!await SessionVisibleAsync(sessionId, ct)) return GradingResult.Fail("That session has no grade record in this organization.");
        var grade = await db.FinalGrades.FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == Org, ct);
        if (grade is null) return GradingResult.Fail("That session has no grade record in this organization.");
        if (!await db.ApprovalRecords.AnyAsync(a => a.SessionId == sessionId && a.OrganizationId == Org && a.Action == ApprovalAction.Finalize, ct))
            return await RefuseAsync(grade, GradeStatus.Published, "No final-approval record exists for this grade.", ct);
        // GAD-T603: held until a person decides (clears the review mark, or approves with a note).
        if (await Review.InstructorReview.IsHeldAsync(db, Org, sessionId, ct))
            return await RefuseAsync(grade, GradeStatus.Published, $"The attempt is marked \"{Review.InstructorReview.Badge}\": clear the mark (with a note) or approve the grade again with a note before publishing.", ct);
        var refused = await TryMoveAsync(grade, GradeStatus.Published, Blank(note) ?? "Published to the student.", ct);
        if (refused is not null) return refused;
        AddApproval(sessionId, ApprovalAction.Publish, note);
        GradingAudit.Add(db, Org, Actor, "grade.published", "FinalGrade", sessionId.ToString(), sessionId, Now, after: new { finalPercent = grade.FinalPercent });
        await NotifyStudentAsync(sessionId, Notifications.EmailKinds.ResultReleased, (title, orgName, name) =>
            new(orgName, name, ExamTitle: title, Score: grade.LetterOrLabel ?? (grade.FinalPercent is { } p ? $"{p:0.##}%" : null), ActionUrl: "/my-results", ActionLabel: "View my result"), ct);
        return await SaveAsync(ct) ?? GradingResult.Done("Published: the student can now see the result and the report.");
    }

    // ================================================================== appeals and amendments

    /// <summary>The student appeals their own published result (Permission.AppealOwnResult).</summary>
    public async Task<GradingResult> AppealAsync(Guid sessionId, string reason, CancellationToken ct = default)
    {
        if (!await RequireAsync(Permission.AppealOwnResult, "Appealing a result", sessionId, ct)) return GradingResult.Fail(Denied(Permission.AppealOwnResult));
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == Org, ct);
        var grade = await db.FinalGrades.FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == Org, ct);
        if (session is null || grade is null || session.StudentUserId != Actor.UserId || !GradeStatusMachine.IsVisibleToStudent(grade.Status))
        {
            await GradingAudit.DeniedAsync(db, Org, Actor, Permission.AppealOwnResult, "Appealing a result that is not the caller's published result", sessionId, Now, ct);
            return GradingResult.Fail("You can appeal only your own published result.");
        }
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4000) return GradingResult.Fail("Explain your appeal (up to 4,000 characters).");
        if (await db.GradeAppeals.AnyAsync(a => a.SessionId == sessionId && a.OrganizationId == Org && a.State == AppealState.Open, ct))
            return GradingResult.Fail("You already have an open appeal for this result.");
        var refused = await TryMoveAsync(grade, GradeStatus.UnderAppeal, "The student appealed.", ct);
        if (refused is not null) return refused;
        var appeal = new GradeAppeal { OrganizationId = Org, SessionId = sessionId, StudentUserId = Actor.UserId!.Value, Reason = reason.Trim(), State = AppealState.Open, OpenedAt = Now };
        db.GradeAppeals.Add(appeal);
        AddApproval(sessionId, ApprovalAction.AppealOpened, null);
        GradingAudit.Add(db, Org, Actor, "grade.appeal.opened", "GradeAppeal", appeal.Id.ToString(), sessionId, Now, reason: appeal.Reason);
        await NotifyStudentAsync(sessionId, Notifications.EmailKinds.AppealReceived, (title, orgName, name) => new(orgName, name, ExamTitle: title), ct);
        return await SaveAsync(ct) ?? GradingResult.Done("Your appeal was submitted. Your instructor will review it; your published result stays visible meanwhile.");
    }

    /// <summary>Upholds or dismisses an open appeal without changing the grade: back to Published.</summary>
    public async Task<GradingResult> ResolveAppealAsync(Guid sessionId, Guid appealId, AppealState outcome, string resolution, CancellationToken ct = default)
    {
        if (!await RequireAsync(Permission.ResolveAppeal, "Resolving an appeal", sessionId, ct)) return GradingResult.Fail(Denied(Permission.ResolveAppeal));
        if (outcome is not (AppealState.Upheld or AppealState.Dismissed)) return GradingResult.Fail("To change the grade, amend it instead.");
        if (string.IsNullOrWhiteSpace(resolution) || resolution.Length > 4000) return GradingResult.Fail("Document the resolution (up to 4,000 characters).");
        if (!await SessionVisibleAsync(sessionId, ct)) return GradingResult.Fail("That appeal is not open.");
        var grade = await db.FinalGrades.FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == Org, ct);
        var appeal = await db.GradeAppeals.FirstOrDefaultAsync(a => a.Id == appealId && a.SessionId == sessionId && a.OrganizationId == Org, ct);
        if (grade is null || appeal is null || appeal.State != AppealState.Open) return GradingResult.Fail("That appeal is not open.");
        var refused = await TryMoveAsync(grade, GradeStatus.Published, $"Appeal {outcome.ToString().ToLowerInvariant()}: the grade stands.", ct);
        if (refused is not null) return refused;
        appeal.State = outcome;
        appeal.Resolution = resolution.Trim();
        appeal.ResolvedByUserId = Actor.UserId;
        appeal.ResolvedAt = Now;
        AddApproval(sessionId, ApprovalAction.AppealResolved, resolution);
        GradingAudit.Add(db, Org, Actor, "grade.appeal.resolved", "GradeAppeal", appeal.Id.ToString(), sessionId, Now,
            after: new { outcome = outcome.ToString() }, reason: appeal.Resolution);
        await NotifyStudentAsync(sessionId, Notifications.EmailKinds.AppealDecided, (title, orgName, name) =>
            new(orgName, name, ExamTitle: title, AppealOutcome: outcome.ToString(), AppealResolution: appeal.Resolution, ActionUrl: "/my-results", ActionLabel: "View my result"), ct);
        return await SaveAsync(ct) ?? GradingResult.Done("Appeal resolved; the published grade stands.");
    }

    /// <summary>
    /// Amends a published grade (Permission.AmendGrade), optionally resolving an open appeal: changed objectives
    /// get new human grades that supersede the old ones, the combined score is recalculated under the frozen
    /// policy, and the grade becomes Amended (visible to the student) with an Amend approval record.
    /// </summary>
    public async Task<GradingResult> AmendAsync(Guid sessionId, IReadOnlyList<HumanGradeInput> changes, string reason, Guid? appealId, CancellationToken ct = default)
    {
        if (!await RequireAsync(Permission.AmendGrade, "Amending a grade", sessionId, ct)) return GradingResult.Fail(Denied(Permission.AmendGrade));
        if (appealId is not null && !await RequireAsync(Permission.ResolveAppeal, "Resolving an appeal by amendment", sessionId, ct)) return GradingResult.Fail(Denied(Permission.ResolveAppeal));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4000) return GradingResult.Fail("Give the reason for the amendment (up to 4,000 characters).");
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == Org, ct);
        var grade = await db.FinalGrades.FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == Org, ct);
        if (session is null) await db.RefuseForeignAsync(OwnershipScope.Kind.Session, sessionId, "Opening a session for grading", ct);
        if (session is null || grade is null) return GradingResult.Fail("That session has no grade record in this organization.");
        if (!GradeStatusMachine.CanMove(grade.Status, GradeStatus.Amended))
            return await RefuseAsync(grade, GradeStatus.Amended, "Only a published grade (or one under appeal) can be amended.", ct);
        GradeAppeal? appeal = null;
        if (appealId is { } aid)
        {
            appeal = await db.GradeAppeals.FirstOrDefaultAsync(a => a.Id == aid && a.SessionId == sessionId && a.OrganizationId == Org, ct);
            if (appeal is null || appeal.State != AppealState.Open) return GradingResult.Fail("That appeal is not open.");
        }
        // An open appeal is resolved by the amendment that answers it: amending around it would leave it open, so the student could
        // not appeal again and resolving it later would move the amended grade back to Published (GAD-T324).
        else if (await db.GradeAppeals.AnyAsync(a => a.SessionId == sessionId && a.OrganizationId == Org && a.State == AppealState.Open, ct))
            return GradingResult.Fail("This grade is under appeal. Amend it from the open appeal so the appeal is resolved with the amendment.");
        var objectives = (await GradingInputs.LoadObjectivesAsync(db, session.ExamId, Org, ct)).ToDictionary(o => o.Id);
        var existing = await db.HumanObjectiveGrades.AsNoTracking().Where(h => h.SessionId == sessionId && h.OrganizationId == Org).ToListAsync(ct);
        var changed = 0;
        foreach (var c in changes)
        {
            if (!objectives.TryGetValue(c.ObjectiveId, out var o) || c.Score is not { } sc) continue;
            if (sc < 0 || sc > o.MaxScore) return GradingResult.Fail($"The score for {o.Title} must be between 0 and {o.MaxScore:0.##}.");
            if (!Enum.IsDefined(c.Level)) return GradingResult.Fail($"Choose a level for {o.Title}.");
            var rounded = Math.Round(sc, 2, MidpointRounding.AwayFromZero);   // the rounding the stored score gets
            var previous = CurrentHuman(existing, o.Id);
            if (previous is not null && previous.Score == rounded && previous.Level == c.Level) continue;
            var row = new HumanObjectiveGrade
            {
                OrganizationId = Org, SessionId = sessionId, ObjectiveId = o.Id, ReviewerUserId = Actor.UserId!.Value, ReviewerRole = Actor.Role ?? "",
                Level = c.Level, Score = rounded, MaxScore = o.MaxScore, Comment = Blank(c.Comment) ?? previous?.Comment,
                EvidenceNote = Blank(c.EvidenceNote) ?? previous?.EvidenceNote, WasBlind = false, SupersedesId = previous?.Id, SubmittedAt = Now,
            };
            db.HumanObjectiveGrades.Add(row);
            changed++;
            GradingAudit.Add(db, Org, Actor, "grade.human.amended", "HumanObjectiveGrade", row.Id.ToString(), sessionId, Now,
                previous is null ? null : new { level = previous.Level.ToString(), score = previous.Score }, new { objective = o.Key, level = row.Level.ToString(), score = row.Score }, reason);
        }
        if (changed == 0) return GradingResult.Fail("Change at least one objective's score or level to amend the grade.");

        var before = grade.FinalPercent;
        var policy = GradingJson.Read<PolicySnapshot>(grade.PolicyJson) ?? new PolicySnapshot { TakenAt = Now };
        var (result, excluded, _) = await ComputeAsync(session, policy, ct);
        if (result.FinalPercent is null) return GradingResult.Fail("The amended grade could not be computed.");
        StoreResult(grade, result, excluded);
        var refused = await TryMoveAsync(grade, GradeStatus.Amended, reason.Trim(), ct);
        if (refused is not null) return refused;
        AddApproval(sessionId, ApprovalAction.Amend, reason);
        if (appeal is not null)
        {
            appeal.State = AppealState.Amended;
            appeal.Resolution = reason.Trim();
            appeal.ResolvedByUserId = Actor.UserId;
            appeal.ResolvedAt = Now;
            AddApproval(sessionId, ApprovalAction.AppealResolved, reason);
            GradingAudit.Add(db, Org, Actor, "grade.appeal.resolved", "GradeAppeal", appeal.Id.ToString(), sessionId, Now, after: new { outcome = "Amended" }, reason: reason);
            await NotifyStudentAsync(sessionId, Notifications.EmailKinds.AppealDecided, (title, orgName, name) =>
                new(orgName, name, ExamTitle: title, AppealOutcome: "Amended", AppealResolution: appeal.Resolution, ActionUrl: "/my-results", ActionLabel: "View my result"), ct);
        }
        GradingAudit.Add(db, Org, Actor, "grade.amended", "FinalGrade", sessionId.ToString(), sessionId, Now,
            new { finalPercent = before }, new { finalPercent = grade.FinalPercent }, reason);
        return await SaveAsync(ct) ?? GradingResult.Done($"Grade amended: {before:0.##}% → {grade.FinalPercent:0.##}%. The student sees the amended result.");
    }

    // ================================================================== student side

    /// <summary>The signed-in student's results that are visible to them (published, under appeal, amended).</summary>
    public async Task<IReadOnlyList<StudentResultRow>> MyResultsAsync(CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || tenant.UserId is not { } me || !Can(Permission.ViewOwnPublishedResults)) return [];
        var rows = await (from g in db.FinalGrades.AsNoTracking()
                          join s in db.ExamSessions.AsNoTracking() on g.SessionId equals s.Id
                          join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
                          where g.OrganizationId == org && s.StudentUserId == me && !s.NeedsInstructorReview   // GAD-T603: held
                                && (g.Status == GradeStatus.Published || g.Status == GradeStatus.UnderAppeal || g.Status == GradeStatus.Amended)
                          select new { g.SessionId, s.AssignmentId, e.Title, s.Attempt, g.Status, g.FinalPercent, g.LetterOrLabel }).ToListAsync(ct);
        var ids = rows.Select(r => r.SessionId).ToList();
        var published = (await db.ApprovalRecords.AsNoTracking().Where(a => a.OrganizationId == org && ids.Contains(a.SessionId) && a.Action == ApprovalAction.Publish)
            .ToListAsync(ct)).GroupBy(a => a.SessionId).ToDictionary(g => g.Key, g => g.Min(a => a.At));
        var finalized = (await db.ApprovalRecords.AsNoTracking().Where(a => a.OrganizationId == org && ids.Contains(a.SessionId) && a.Action == ApprovalAction.Finalize)
            .Select(a => a.SessionId).ToListAsync(ct)).ToHashSet();
        var appeals = (await db.GradeAppeals.AsNoTracking().Where(a => a.OrganizationId == org && ids.Contains(a.SessionId) && a.State == AppealState.Open)
            .Select(a => a.SessionId).ToListAsync(ct)).ToHashSet();
        // Never without both an approval record and a publication record.
        return rows.Where(r => finalized.Contains(r.SessionId) && published.ContainsKey(r.SessionId))
            .Select(r => new StudentResultRow(r.SessionId, r.AssignmentId, r.Title, r.Attempt, r.Status, r.FinalPercent, r.LetterOrLabel, published[r.SessionId], appeals.Contains(r.SessionId)))
            .ToList();
    }

    /// <summary>The student's own published workspace, or null when it isn't theirs or isn't published with approval.</summary>
    public async Task<GradingWorkspace?> StudentWorkspaceAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (!(await MyResultsAsync(ct)).Any(r => r.SessionId == sessionId))
        {
            if (tenant.OrganizationId is { } org)
                await GradingAudit.DeniedAsync(db, org, Actor, Permission.ViewOwnPublishedResults, "Opening a result that is not the caller's published result", sessionId, Now, ct);
            return null;
        }
        return await LoadAsync(sessionId, ct);
    }

    // ================================================================== helpers

    private void AddApproval(Guid sessionId, ApprovalAction action, string? note) =>
        db.ApprovalRecords.Add(new ApprovalRecord
        {
            OrganizationId = Org, SessionId = sessionId, Action = action, ActorUserId = Actor.UserId!.Value, ActorRole = Actor.Role ?? "",
            Note = Blank(note) is { } n && n.Length > 4000 ? n[..4000] : Blank(note), At = Now,
        });

    /// <summary>Moves the grade; an invalid move is refused, audited and saved (the caller returns the result).</summary>
    private async Task<GradingResult?> TryMoveAsync(FinalGrade grade, GradeStatus to, string reason, CancellationToken ct)
    {
        if (GradeStatusMachine.CanMove(grade.Status, to))
        {
            GradeLifecycle.Move(db, grade, to, Actor, Now, reason);
            return null;
        }
        return await RefuseAsync(grade, to, $"{GradeStatusMachine.Label(grade.Status)} cannot become {GradeStatusMachine.Label(to)}.", ct);
    }

    private async Task<GradingResult> RefuseAsync(FinalGrade grade, GradeStatus to, string why, CancellationToken ct)
    {
        var from = grade.Status;
        db.ChangeTracker.Clear();
        GradingAudit.Add(db, Org, Actor, "grade.transition.refused", "FinalGrade", grade.SessionId.ToString(), grade.SessionId, Now,
            new { status = from.ToString() }, new { status = to.ToString() }, why);
        await db.SaveChangesAsync(ct);
        return GradingResult.Fail($"Refused: {why}");
    }

    private async Task<GradingResult?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return GradingResult.Fail("Someone else changed this grade at the same time. Reload and try again.");
        }
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        return distinct.Count == 0 ? [] : await db.AuthUsers.AsNoTracking().Where(u => distinct.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
