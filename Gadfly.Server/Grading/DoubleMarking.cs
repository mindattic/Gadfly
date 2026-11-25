using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Sources.Grading;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>A session in an exam's second-marking queue.</summary>
public sealed record SecondMarkQueueRow(Guid SessionId, string Code, DateTimeOffset? EndedAt, bool Ready, bool MarkedByMe, bool MarkedByOther, bool IAmFirstExaminer)
{
    /// <summary>Whether the caller may second-mark it now.</summary>
    public bool Open => Ready && !MarkedByOther && !IAmFirstExaminer;
}

public sealed record SecondMarkQueue(Guid ExamId, string ExamTitle, bool DoubleMarking, IReadOnlyList<SecondMarkQueueRow> Rows);

public sealed record SecondMarkLevel(int Level, string Label, string Descriptor);
public sealed record SecondMarkCriterion(string Name, string Dimension, string? Description, IReadOnlyList<SecondMarkLevel> Levels);
public sealed record SecondMarkEvidence(int Sequence, string Question, string Answer);
public sealed record SecondMarkObjective(Guid ObjectiveId, string Title, string? Description, IReadOnlyList<SecondMarkCriterion> Criteria,
    IReadOnlyList<SecondMarkEvidence> Evidence, int? MyLevel, string? MyComment);

/// <summary>
/// What a blind second marker sees (GAD-T428): the session code (never the student's name), each objective's rubric and the
/// same verified answers the panel was shown. Never the AI's grade, the first examiner's grade, integrity notes or the
/// examiner's in-exam stance notes.
/// </summary>
public sealed record SecondMarkSheet(Guid SessionId, Guid ExamId, string ExamTitle, string Code, IReadOnlyList<SecondMarkObjective> Objectives)
{
    /// <summary>The attempt's language (GAD-T548): the answers are shown as given, with a machine translation on demand.</summary>
    public string Language { get; init; } = Gadfly.Engine.ExamLanguage.English;
}

public sealed record SecondMarkInput(Guid ObjectiveId, int Level, string? Comment);

/// <summary>One double-marked answer: the panel's level and two blind human levels on the same evidence.</summary>
public sealed record DoubleMarkItem(string ItemId, Guid SessionId, Guid ObjectiveId, string Code, string ObjectiveTitle, int PanelLevel, int FirstLevel,
    int SecondLevel, IReadOnlyList<int> MemberLevels, bool Mock, Guid StudentUserId)
{
    public CalibrationItem ToCalibration() => new(ItemId, PanelLevel, FirstLevel, SecondLevel);
}

/// <summary>An exam's double-marking results and what they say about the panel (GAD-T428, GAD-T427).</summary>
public sealed record DoubleMarkingSummary(
    Guid ExamId,
    string ExamTitle,
    int SessionsEnded,
    int SessionsSecondMarked,
    IReadOnlyList<DoubleMarkItem> Items,
    CalibrationReport Preview,
    double? PanelVsFirst,
    double? PanelVsSecond,
    double? SmdVsFirst,
    double? SmdVsSecond,
    bool AnyMock,
    CalibrationAdviceReport Advice,
    IReadOnlyList<GroupSmd> Groups,
    IReadOnlyList<string> GroupProblems,
    int Ungrouped);

/// <summary>
/// Blind double marking inside the app (GAD-T428; ElenchusAtScale Sections 2.9.3, 7.2-7.3, M13). When an exam is flagged for
/// double marking, a second examiner grades its sessions from <see cref="SecondMarkSheet"/>: blind to the panel and to the
/// first examiner, and never the same person as the first examiner. Second marks are append-only audit events
/// (<c>grade.second-mark.recorded</c>, entity <c>SecondMark</c>, id = session id; the latest per objective counts) and never
/// change the student's grade: they exist to measure agreement. From them the app builds the calibration set (panel level,
/// the first examiner's original blind level, the second marker's level), the human-human QWK and the panel's agreement with
/// each marker, and records it as the exam's calibration in one step.
/// </summary>
public sealed class DoubleMarkingService(GadflyDbContext db, ITenantContext tenant, TimeProvider clock, PanelCalibrationService calibration,
    PanelModelInventory models)
{
    public const string MarkAction = "grade.second-mark.recorded";
    public const string MarkEntity = "SecondMark";
    public const int MaxCommentLength = 1000;

    private GradingActor Actor => new(tenant.UserId, tenant.Role);
    private bool CanMark => Actor.Has(Permission.EnterHumanScore) || Actor.Has(Permission.ActAsSecondReviewer);
    private bool CanSeeAgreement => Actor.Has(Permission.ApproveGradingPolicyChange) || Actor.Has(Permission.ViewAnalytics);

    private sealed record StoredMark(Guid ExamId, Guid ObjectiveId, int Level, string? Comment);
    private sealed record Mark(Guid SessionId, Guid ObjectiveId, Guid? By, int Level, string? Comment, DateTimeOffset At);

    /// <summary>The exam's sessions for second marking, or null when the caller may not mark or cannot see the exam.</summary>
    public async Task<SecondMarkQueue?> QueueAsync(Guid examId, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || tenant.UserId is not { } me) return null;
        if (!CanMark)
        {
            await GradingAudit.DeniedAsync(db, org, Actor, Permission.EnterHumanScore, "Opening the second-marking queue", null, clock.GetUtcNow(), ct);
            return null;
        }
        var exam = await db.Exams.AsNoTracking().Where(e => e.Id == examId && e.OrganizationId == org).Select(e => new { e.Id, e.Title }).FirstOrDefaultAsync(ct);
        if (exam is null) return null;
        var settings = await PanelGovernanceService.LatestAsync(db, org, examId, ct);
        var sessions = await EndedSessionsAsync(org, examId, ct);
        var ids = sessions.Select(s => s.Id).ToList();
        var ready = (await db.FinalGrades.AsNoTracking().Where(g => g.OrganizationId == org && ids.Contains(g.SessionId)
                && g.Status != GradeStatus.ExaminationCompleted && g.Status != GradeStatus.TranscriptVerificationPending && g.Status != GradeStatus.AiGradingPending)
            .Select(g => g.SessionId).ToListAsync(ct)).ToHashSet();
        var firstExaminers = (await db.HumanObjectiveGrades.AsNoTracking().Where(h => h.OrganizationId == org && ids.Contains(h.SessionId))
            .Select(h => new { h.SessionId, h.ReviewerUserId }).Distinct().ToListAsync(ct)).ToLookup(h => h.SessionId, h => h.ReviewerUserId);
        var marks = await MarksAsync(org, ids, ct);
        var rows = sessions.Select(s =>
        {
            var by = marks.Where(m => m.SessionId == s.Id).Select(m => m.By).Distinct().ToList();
            return new SecondMarkQueueRow(s.Id, AnonymousMarking.Code(s.Id), s.EndedAt, ready.Contains(s.Id), by.Contains(me), by.Any(b => b != me),
                firstExaminers[s.Id].Contains(me));
        }).ToList();
        return new SecondMarkQueue(exam.Id, exam.Title, settings.DoubleMarking, rows);
    }

    /// <summary>The blind marking sheet for one session; null with a reason when the caller may not second-mark it.</summary>
    public async Task<(SecondMarkSheet? Sheet, string? Refusal)> SheetAsync(Guid sessionId, CancellationToken ct = default)
    {
        var (session, exam, refusal) = await CheckAsync(sessionId, "Opening a second-marking sheet", ct);
        if (refusal is not null) return (null, refusal);
        var org = session!.OrganizationId;
        var policy = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == exam!.Id && p.OrganizationId == org, ct);
        var responses = await GradingInputs.EnsureResponsesAsync(db, session, clock.GetUtcNow(), ct);
        var objectives = await GradingInputs.LoadObjectivesAsync(db, exam!.Id, org, ct);
        var mine = (await MarksAsync(org, [sessionId], ct)).Where(m => m.By == tenant.UserId).ToDictionary(m => m.ObjectiveId);
        var list = new List<SecondMarkObjective>();
        foreach (var o in objectives)
        {
            var input = await GradingInputs.BuildInputAsync(db, session, exam, o, responses, policy?.AllowAssistedEvidence ?? false, ct);
            var criteria = (o.Criteria.Count > 0
                    ? o.Criteria.OrderBy(c => c.Ordinal).Select(c => new SecondMarkCriterion(c.Name, c.Dimension.ToString(), c.Description,
                        Enum.GetValues<MasteryLevel>().Select(l => new SecondMarkLevel((int)l, MasteryLevels.Label(l),
                            c.Levels.FirstOrDefault(x => x.Level == l)?.Descriptor is { Length: > 0 } d ? d : MasteryLevels.DefaultDescriptor(l))).ToList()))
                    : AiObjectiveGrader.DefaultCriteria().Select(c => new SecondMarkCriterion(c.Name, c.Dimension.ToString(), c.Description,
                        Enum.GetValues<MasteryLevel>().Select(l => new SecondMarkLevel((int)l, MasteryLevels.Label(l), MasteryLevels.DefaultDescriptor(l))).ToList())))
                .ToList();
            var evidence = AiObjectiveGrader.UsableEvidence(input).Select(e => new SecondMarkEvidence(e.Sequence, e.Question, e.VerifiedText)).ToList();
            mine.TryGetValue(o.Id, out var m);
            list.Add(new SecondMarkObjective(o.Id, o.Title, o.Description, criteria, evidence, m?.Level, m?.Comment));
        }
        return (new SecondMarkSheet(session.Id, exam.Id, exam.Title, AnonymousMarking.Code(session.Id), list) { Language = session.Language }, null);
    }

    /// <summary>
    /// Records the caller's blind second marks for a session: one level (0..5) for every objective. A later submission by the same
    /// marker replaces theirs (the history stays in the audit log); another marker's marks on the session refuse it.
    /// </summary>
    public async Task<GradingResult> SubmitAsync(Guid sessionId, IReadOnlyList<SecondMarkInput> marks, CancellationToken ct = default)
    {
        var (session, exam, refusal) = await CheckAsync(sessionId, "Recording second marks", ct);
        if (refusal is not null) return GradingResult.Fail(refusal);
        var org = session!.OrganizationId;
        var objectives = await GradingInputs.LoadObjectivesAsync(db, exam!.Id, org, ct);
        var byObjective = marks.GroupBy(m => m.ObjectiveId).ToDictionary(g => g.Key, g => g.Last());
        var missing = objectives.Where(o => !byObjective.ContainsKey(o.Id)).Select(o => o.Title).ToList();
        if (missing.Count > 0) return GradingResult.Fail($"Give a level for every objective (missing: {string.Join(", ", missing)}).");
        if (byObjective.Keys.Any(id => objectives.All(o => o.Id != id))) return GradingResult.Fail("A mark names an objective this exam does not have.");
        if (byObjective.Values.Any(m => m.Level is < PanelCalibration.MinLevel or > PanelCalibration.MaxLevel))
            return GradingResult.Fail($"Levels run from {PanelCalibration.MinLevel} to {PanelCalibration.MaxLevel}.");
        if (byObjective.Values.Any(m => (m.Comment?.Length ?? 0) > MaxCommentLength)) return GradingResult.Fail($"Keep each comment under {MaxCommentLength} characters.");

        var now = clock.GetUtcNow();
        foreach (var o in objectives)
        {
            var m = byObjective[o.Id];
            GradingAudit.Add(db, org, Actor, MarkAction, MarkEntity, sessionId.ToString(), sessionId, now,
                after: new StoredMark(exam.Id, o.Id, m.Level, string.IsNullOrWhiteSpace(m.Comment) ? null : m.Comment.Trim()),
                reason: "Blind second mark for double marking; it measures agreement and does not change the student's grade.");
        }
        await db.SaveChangesAsync(ct);
        return GradingResult.Done($"Second marks recorded for {AnonymousMarking.Code(sessionId)}. They do not change the student's grade.");
    }

    /// <summary>
    /// The exam's double-marking results: every answer with a panel level, the first examiner's original blind level and a second
    /// mark, the agreement statistics, the calibration they would give, and the policy advice. <paramref name="grouping"/> is an
    /// optional pasted "student email or session code, group" list for the by-group SMD; it is used for this view only and never stored.
    /// </summary>
    public async Task<DoubleMarkingSummary?> SummaryAsync(Guid examId, string? grouping = null, CancellationToken ct = default,
        Gadfly.Server.Analytics.FairnessGrouping? byLanguage = null)
    {
        if (tenant.OrganizationId is not { } org || !CanSeeAgreement) return null;
        var exam = await db.Exams.AsNoTracking().Where(e => e.Id == examId && e.OrganizationId == org).Select(e => new { e.Id, e.Title }).FirstOrDefaultAsync(ct);
        if (exam is null) return null;
        var sessions = await EndedSessionsAsync(org, examId, ct);
        var items = await ItemsAsync(org, examId, sessions, ct);
        // GAD-T742: the items show the panel's and the first examiner's levels, so a viewer who then second-marks one of these sessions is no longer blind.
        if (tenant.UserId is not null)
            foreach (var sessionId in items.Select(i => i.SessionId).Distinct())
                await SecondMarkerBlindness.NoteSeenAsync(db, org, Actor, sessionId, examId, SecondMarkerBlindness.SeenOn.AgreementSummary, clock.GetUtcNow(), ct);
        var calibrationItems = items.Select(i => i.ToCalibration()).ToList();
        var preview = PanelCalibration.Evaluate(calibrationItems);
        var policyRow = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == examId && p.OrganizationId == org, ct);
        var boundaries = (policyRow?.ToRules() ?? new GradingPolicyRules()).ScoreBoundaries;
        var advice = CalibrationAdvice.Advise(calibrationItems, preview, boundaries ?? [], items.ToDictionary(i => i.ItemId, i => i.MemberLevels));

        var groups = (IReadOnlyList<GroupSmd>)[];
        IReadOnlyList<string> problems = [];
        var ungrouped = 0;
        if (!string.IsNullOrWhiteSpace(grouping))
        {
            var pasted = CalibrationAdvice.ParseGrouping(grouping, out problems);
            var studentIds = items.Select(i => i.StudentUserId).Distinct().ToList();
            var emails = (await db.AuthUsers.AsNoTracking().Where(u => studentIds.Contains(u.Id)).Select(u => new { u.Id, u.Email }).ToListAsync(ct))
                .ToDictionary(u => u.Id, u => u.Email ?? "");
            var groupOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var i in items)
            {
                var code = i.Code.Replace("Session ", "", StringComparison.Ordinal);
                if (pasted.TryGetValue(emails.GetValueOrDefault(i.StudentUserId, ""), out var g) || pasted.TryGetValue(i.Code, out g) || pasted.TryGetValue(code, out g)
                    || pasted.TryGetValue(i.SessionId.ToString(), out g))
                    groupOf[i.ItemId] = g;
                else ungrouped++;
            }
            groups = CalibrationAdvice.SmdByGroup(calibrationItems, groupOf, preview.Thresholds.MaxAbsSmd);
        }
        else if (byLanguage is Gadfly.Server.Analytics.FairnessGrouping.ExamLanguage or Gadfly.Server.Analytics.FairnessGrouping.PrimaryLanguage)
            (groups, problems) = await LanguageGroupsAsync(org, items, calibrationItems, byLanguage.Value, preview.Thresholds.MaxAbsSmd, ct);

        double? Qwk(Func<DoubleMarkItem, int> human) => RaterAgreement.QuadraticWeightedKappa(items.Select(i => (i.PanelLevel, human(i))).ToList(), PanelCalibration.MinLevel, PanelCalibration.MaxLevel);
        double? Smd(Func<DoubleMarkItem, int> human) => RaterAgreement.StandardizedMeanDifference(items.Select(i => (double)i.PanelLevel).ToList(), items.Select(i => (double)human(i)).ToList());
        var marked = items.Select(i => i.SessionId).Distinct().Count();
        return new DoubleMarkingSummary(exam.Id, exam.Title, sessions.Count, marked, items, preview, Qwk(i => i.FirstLevel), Qwk(i => i.SecondLevel),
            Smd(i => i.FirstLevel), Smd(i => i.SecondLevel), items.Any(i => i.Mock), advice, groups, problems, ungrouped);
    }

    /// <summary>
    /// The panel's systematic difference from the markers by exam language or by the student's stated primary language (GAD-T549,
    /// GAD-T555). Only for the roles the fairness views are for (<see cref="Gadfly.Server.Analytics.GroupFairness.ViewerAllowed"/>),
    /// and with their rule: a group of fewer than <see cref="Gadfly.Server.Analytics.GroupFairness.MinStudents"/> students keeps
    /// its name and answer count but no figure.
    /// </summary>
    private async Task<(IReadOnlyList<GroupSmd> Groups, IReadOnlyList<string> Problems)> LanguageGroupsAsync(Guid org, IReadOnlyList<DoubleMarkItem> items,
        IReadOnlyList<CalibrationItem> calibrationItems, Gadfly.Server.Analytics.FairnessGrouping by, double maxAbsSmd, CancellationToken ct)
    {
        if (!Gadfly.Server.Analytics.GroupFairness.ViewerAllowed(tenant.Role))
            return ([], ["Grouping by language is shown to Owners and Admins only, as the fairness views are."]);
        var ids = items.Select(i => i.SessionId).Distinct().ToList();
        var langs = (await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org && ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Language, s.PrimaryLanguage }).ToListAsync(ct)).ToDictionary(s => s.Id);
        string Label(Guid sessionId)
        {
            var l = langs.GetValueOrDefault(sessionId);
            if (by == Gadfly.Server.Analytics.FairnessGrouping.ExamLanguage) return Gadfly.Server.Analytics.GroupFairness.LanguageLabel(l?.Language);
            return string.IsNullOrWhiteSpace(l?.PrimaryLanguage) ? Gadfly.Server.Analytics.GroupFairness.NotStated
                : Gadfly.Server.Analytics.GroupFairness.LanguageLabel(l.PrimaryLanguage);
        }
        var groupOf = items.ToDictionary(i => i.ItemId, i => Label(i.SessionId), StringComparer.Ordinal);
        var students = items.GroupBy(i => groupOf[i.ItemId], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(i => i.StudentUserId).Distinct().Count(), StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var groups = CalibrationAdvice.SmdByGroup(calibrationItems, groupOf, maxAbsSmd).Select(g =>
        {
            if (students.GetValueOrDefault(g.Group) >= Gadfly.Server.Analytics.GroupFairness.MinStudents) return g;
            problems.Add($"{g.Group}: withheld, fewer than {Gadfly.Server.Analytics.GroupFairness.MinStudents} students.");
            return g with { Smd = null, Flagged = false };
        }).ToList();
        return (groups, problems);
    }

    /// <summary>Records the double-marked set as the exam's calibration (needs ApproveGradingPolicyChange, checked by the calibration service).</summary>
    public async Task<(GradingResult Result, CalibrationReport? Report)> RecordCalibrationAsync(Guid examId, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org) return (GradingResult.Fail("No active organization."), null);
        var sessions = await EndedSessionsAsync(org, examId, ct);
        // GAD-T480: the calibration is recorded against the models the panel runs now, so only answers those models graded count.
        // GAD-T484: and under the grading prompts it uses now, since the calibration also records the prompt-set version.
        var (items, otherModels, otherPrompts) = await ItemsAsync(org, examId, sessions, currentModelsOnly: true, ct);
        if (items.Count == 0)
            return (GradingResult.Fail(StaleRefusal(otherModels, otherPrompts)), null);
        return await calibration.EvaluateAsync(examId, null, items.Select(i => i.ToCalibration()).ToList(), ct,
            PanelCalibrationService.SourceDoubleMarking, fromMockGrades: items.Any(i => i.Mock));
    }

    // ------------------------------------------------------------------ helpers

    private sealed record EndedSession(Guid Id, Guid StudentUserId, DateTimeOffset? EndedAt);

    private async Task<List<EndedSession>> EndedSessionsAsync(Guid org, Guid examId, CancellationToken ct)
    {
        const string notAnAttempt = nameof(Gadfly.Engine.EndReason.PromptSetChanged);
        return await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == org && s.ExamId == examId && s.State == SessionState.Ended && s.EndReason != notAnAttempt)
            .OrderBy(s => s.EndedAt).Select(s => new EndedSession(s.Id, s.StudentUserId, s.EndedAt)).ToListAsync(ct);
    }

    private async Task<List<Mark>> MarksAsync(Guid org, IReadOnlyList<Guid> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0) return [];
        var ids = sessionIds.Select(i => (Guid?)i).ToList();
        var rows = await db.AuditEvents.AsNoTracking()
            .Where(a => a.OrganizationId == org && a.Action == MarkAction && a.EntityType == MarkEntity && ids.Contains(a.SessionId))
            .OrderBy(a => a.At).ThenBy(a => a.Id).Select(a => new { a.SessionId, a.ActorUserId, a.AfterJson, a.At }).ToListAsync(ct);
        // The latest mark per (session, objective, marker) counts.
        return rows.Select(r => (r, m: GradingJson.Read<StoredMark>(r.AfterJson))).Where(x => x.m is not null)
            .GroupBy(x => (x.r.SessionId, x.m!.ObjectiveId, x.r.ActorUserId))
            .Select(g => g.Last()).Select(x => new Mark(x.r.SessionId!.Value, x.m!.ObjectiveId, x.r.ActorUserId, x.m.Level, x.m.Comment, x.r.At)).ToList();
    }

    private async Task<List<DoubleMarkItem>> ItemsAsync(Guid org, Guid examId, List<EndedSession> sessions, CancellationToken ct) =>
        (await ItemsAsync(org, examId, sessions, currentModelsOnly: false, ct)).Items;

    /// <summary>
    /// Why no double-marked answer can be recorded as a calibration: none graded by the panel's current models (GAD-T480) or under
    /// the current grading prompts (GAD-T484), or none double-marked at all.
    /// </summary>
    public static string StaleRefusal(int otherModels, int otherPrompts)
    {
        if (otherModels == 0 && otherPrompts == 0) return "No answer has a panel grade, a blind first grade and a second mark yet.";
        var why = new List<string>();
        if (otherModels > 0) why.Add($"{otherModels} were graded by earlier models");
        if (otherPrompts > 0) why.Add($"{otherPrompts} were graded under earlier grading prompts");
        return $"No double-marked answer was graded by the models and grading prompts the panel uses now ({string.Join("; ", why)}). " +
               "Re-grade or double-mark new answers, then record the calibration.";
    }

    /// <summary>True when the grade was made under the grading prompt-set version in use now (GAD-T484).</summary>
    public static bool GradedUnderCurrentPrompts(int gradingPromptVersion, int current = AiObjectiveGrader.PromptVersion) =>
        gradingPromptVersion == current;

    /// <param name="currentModelsOnly">Leave out answers the panel graded with other models than it runs now (counted in OtherModels) or
    /// under other grading prompts (counted in OtherPrompts).</param>
    private async Task<(List<DoubleMarkItem> Items, int OtherModels, int OtherPrompts)> ItemsAsync(Guid org, Guid examId, List<EndedSession> sessions,
        bool currentModelsOnly, CancellationToken ct)
    {
        var otherModels = 0;
        var otherPrompts = 0;
        var ids = sessions.Select(s => s.Id).ToList();
        if (ids.Count == 0) return ([], 0, 0);
        var marks = (await MarksAsync(org, ids, ct)).GroupBy(m => (m.SessionId, m.ObjectiveId)).ToDictionary(g => g.Key, g => g.OrderBy(m => m.At).First());
        if (marks.Count == 0) return ([], 0, 0);
        var objectives = (await GradingInputs.LoadObjectivesAsync(db, examId, org, ct)).ToDictionary(o => o.Id);
        var ai = (await db.AiObjectiveGrades.AsNoTracking().Where(g => g.OrganizationId == org && ids.Contains(g.SessionId))
            .Select(g => new { g.SessionId, g.ObjectiveId, g.Level, g.IsMock, g.CriteriaJson, g.GradingPromptVersion, g.CreatedAt }).ToListAsync(ct))
            .GroupBy(g => (g.SessionId, g.ObjectiveId)).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());
        // The first examiner's original grade, given blind to the panel: an adjustment made after seeing the AI is anchored on it.
        var first = (await db.HumanObjectiveGrades.AsNoTracking().Where(h => h.OrganizationId == org && ids.Contains(h.SessionId) && h.WasBlind && h.SupersedesId == null)
            .Select(h => new { h.SessionId, h.ObjectiveId, h.Level, h.ReviewerUserId, h.SubmittedAt }).ToListAsync(ct))
            .GroupBy(h => (h.SessionId, h.ObjectiveId)).ToDictionary(g => g.Key, g => g.OrderBy(x => x.SubmittedAt).First());
        var student = sessions.ToDictionary(s => s.Id, s => s.StudentUserId);
        var current = models.Current;

        var items = new List<DoubleMarkItem>();
        foreach (var ((sessionId, objectiveId), mark) in marks.OrderBy(m => m.Key.SessionId).ThenBy(m => m.Key.ObjectiveId))
        {
            if (!ai.TryGetValue((sessionId, objectiveId), out var a) || !first.TryGetValue((sessionId, objectiveId), out var h)) continue;
            if (h.ReviewerUserId == mark.By) continue;   // never counts one person twice
            var voters = GradingJson.Read<StoredAiDetail>(a.CriteriaJson)?.Voters ?? [];
            // GAD-T480: a calibration is recorded against the models the panel runs now, so it may only rest on grades those models gave.
            // A grade from before a model change would otherwise renew an expired calibration on the old models' agreement.
            if (currentModelsOnly && !GradedByCurrentModels(voters, current)) { otherModels++; continue; }
            // GAD-T484: likewise the calibration records the grading prompt-set version, so a grade made under other prompts is left out.
            if (currentModelsOnly && !GradedUnderCurrentPrompts(a.GradingPromptVersion)) { otherPrompts++; continue; }
            var members = voters.Select(v => (int)v.Level).ToList();
            var code = AnonymousMarking.Code(sessionId);
            var title = objectives.TryGetValue(objectiveId, out var o) ? o.Title : "(removed objective)";
            items.Add(new DoubleMarkItem($"{code} / {(o?.Key ?? objectiveId.ToString("N")[..8])}", sessionId, objectiveId, code, title,
                (int)a.Level, (int)h.Level, mark.Level, members, a.IsMock, student[sessionId]));
        }
        return (items, otherModels, otherPrompts);
    }

    /// <summary>
    /// True when every panel member that graded the answer runs the same model now (GAD-T480): the grade describes the panel a
    /// calibration would be recorded against. A grade without voter detail cannot show that and is left out. Null
    /// <paramref name="current"/> (the host's panel does not say) skips the check, as the calibration's own expiry check does.
    /// </summary>
    public static bool GradedByCurrentModels(IReadOnlyList<VoterGrade> voters, IReadOnlyDictionary<string, string>? current)
    {
        if (current is null) return true;
        if (voters.Count == 0) return false;
        var now = current.ToDictionary(m => m.Key.Trim(), m => (m.Value ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
        return voters.All(v => now.TryGetValue(v.ProviderId.Trim(), out var model) && string.Equals(model, (v.Model ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(ExamSession? Session, Exam? Exam, string? Refusal)> CheckAsync(Guid sessionId, string attempted, CancellationToken ct)
    {
        if (tenant.OrganizationId is not { } org || tenant.UserId is not { } me) return (null, null, "No active organization.");
        if (!CanMark)
        {
            await GradingAudit.DeniedAsync(db, org, Actor, Permission.EnterHumanScore, attempted, sessionId, clock.GetUtcNow(), ct);
            return (null, null, "Your role in this organization does not include grading. The attempt was recorded.");
        }
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == org, ct);
        if (session is null || session.State != SessionState.Ended) return (null, null, "That session was not found, or it has not ended.");
        var exam = await db.Exams.AsNoTracking().FirstAsync(e => e.Id == session.ExamId && e.OrganizationId == org, ct);
        if (!(await PanelGovernanceService.LatestAsync(db, org, exam.Id, ct)).DoubleMarking)
            return (null, null, "This exam is not set for double marking. Someone who manages the assessment can turn it on from the panel calibration page.");
        var status = await db.FinalGrades.AsNoTracking().Where(g => g.SessionId == sessionId && g.OrganizationId == org).Select(g => (GradeStatus?)g.Status).FirstOrDefaultAsync(ct);
        if (status is null or GradeStatus.ExaminationCompleted or GradeStatus.TranscriptVerificationPending or GradeStatus.AiGradingPending)
            return (null, null, "This session's answers are not ready for marking yet (its transcripts are still being checked or graded).");
        if (await db.HumanObjectiveGrades.AsNoTracking().AnyAsync(h => h.SessionId == sessionId && h.OrganizationId == org && h.ReviewerUserId == me, ct))
            return (null, null, "You graded this session as its examiner, so you cannot also be its second marker: the two marks must come from different people.");
        if (await SecondMarkerBlindness.SawGradesAsync(db, org, sessionId, me, ct))
            return (null, null, "You have already seen this session's grades (on its grading page, its review page or its full transcript), so you cannot mark it blind. Ask another examiner to second-mark it.");
        var others = await db.AuditEvents.AsNoTracking()
            .AnyAsync(a => a.OrganizationId == org && a.Action == MarkAction && a.EntityType == MarkEntity && a.SessionId == sessionId && a.ActorUserId != me, ct);
        if (others) return (null, null, "Another examiner has already second-marked this session.");
        return (session, exam, null);
    }
}

/// <summary>
/// Keeps a second marker blind outside the second-marking sheet too (GAD-T468, following GAD-T428; ElenchusAtScale ch. 5):
/// the normal grading page shows the panel's grade and the first examiner's grade, so it is refused to anyone who has
/// second-marked the session, and anyone who opens it on a double-marked exam once grades are showing is recorded, which
/// then stops them second-marking that session. Both are audited. GAD-T485: downloading the full transcript or opening the review
/// page (or its exports) with the panel's judgments showing counts the same, and a second marker gets the blind view of both.
/// </summary>
public static class SecondMarkerBlindness
{
    /// <summary>Recorded once per person and session when they open a double-marked session's grading page with grades showing.</summary>
    public const string SawGradesAction = "grade.second-mark.grades-seen";

    /// <summary>What the grading page says to a second marker it refuses.</summary>
    public const string Refusal = "You are this session's blind second marker, so its grading page, which shows the panel's and the first examiner's grades, is closed to you. "
        + "Your second marks are on the second-marking sheet. The attempt was recorded.";

    /// <summary>True when <paramref name="userId"/> has recorded second marks for the session.</summary>
    public static Task<bool> IsSecondMarkerAsync(GadflyDbContext db, Guid org, Guid sessionId, Guid userId, CancellationToken ct) =>
        db.AuditEvents.AsNoTracking().AnyAsync(a => a.OrganizationId == org && a.Action == DoubleMarkingService.MarkAction
            && a.EntityType == DoubleMarkingService.MarkEntity && a.SessionId == sessionId && a.ActorUserId == userId, ct);

    /// <summary>True when <paramref name="userId"/> has opened the session's grading page while its grades were showing.</summary>
    public static Task<bool> SawGradesAsync(GadflyDbContext db, Guid org, Guid sessionId, Guid userId, CancellationToken ct) =>
        db.AuditEvents.AsNoTracking().AnyAsync(a => a.OrganizationId == org && a.Action == SawGradesAction && a.SessionId == sessionId && a.ActorUserId == userId, ct);

    /// <summary>
    /// Records (once) that the viewer saw a grade on the session's grading page, when its exam is set for double marking and the
    /// page showed the panel's grade or a human grade. The first examiner is not recorded: they can never second-mark it anyway.
    /// </summary>
    public static async Task NoteGradesSeenAsync(GadflyDbContext db, Guid org, GradingActor viewer, GradingWorkspace w, DateTimeOffset now, CancellationToken ct)
    {
        if (viewer.UserId is not { } me) return;
        var gradesShowing = w.Objectives.Any(o => o.Human is not null) || (w.AiVisible && w.Objectives.Any(o => o.Ai is not null));
        if (!gradesShowing) return;
        if (w.Objectives.Any(o => o.HumanHistory.Any(h => h.ReviewerUserId == me))) return;
        await NoteSeenAsync(db, org, viewer, w.Session.Id, w.Session.ExamId, SeenOn.GradingPage, now, ct);
    }

    /// <summary>Where a viewer saw a double-marked session's grades or the panel's judgments (GAD-T468, GAD-T485).</summary>
    public enum SeenOn { GradingPage, FullTranscript, ReviewPage, AgreementSummary }

    /// <summary>The audit reason for each place grades were seen.</summary>
    public static string SeenReason(SeenOn where) => where switch
    {
        SeenOn.FullTranscript => "Downloaded the full transcript of a double-marked session (the panel's judgments and integrity notes), so this person cannot second-mark it blind.",
        SeenOn.ReviewPage => "Opened the review page or its export of a double-marked session with the panel's judgments showing, so this person cannot second-mark it blind.",
        SeenOn.AgreementSummary => "Opened the double-marking results of a double-marked session, which show the panel's and the first examiner's levels, so this person cannot second-mark it blind.",
        _ => "Opened the grading page of a double-marked session with its grades showing, so this person cannot second-mark it blind.",
    };

    /// <summary>
    /// Records (once) that the viewer saw the session's grades or the panel's judgments somewhere other than the second-marking
    /// sheet (GAD-T485: the full transcript and the review page count like the grading page), when its exam is set for double
    /// marking. Not recorded for the first examiner (they can never second-mark it) or for someone who has already second-marked
    /// it (those places give them the blind view instead), nor for a caller with no user (an API key).
    /// </summary>
    public static async Task NoteSeenAsync(GadflyDbContext db, Guid org, GradingActor viewer, Guid sessionId, Guid examId, SeenOn where,
        DateTimeOffset now, CancellationToken ct)
    {
        if (viewer.UserId is not { } me) return;
        if (!(await PanelGovernanceService.LatestAsync(db, org, examId, ct)).DoubleMarking) return;
        if (where is SeenOn.FullTranscript or SeenOn.ReviewPage)
        {
            if (await db.HumanObjectiveGrades.AsNoTracking().AnyAsync(h => h.SessionId == sessionId && h.OrganizationId == org && h.ReviewerUserId == me, ct)) return;
            if (await IsSecondMarkerAsync(db, org, sessionId, me, ct)) return;
        }
        if (await SawGradesAsync(db, org, sessionId, me, ct)) return;
        GradingAudit.Add(db, org, viewer, SawGradesAction, DoubleMarkingService.MarkEntity, sessionId.ToString(), sessionId, now, reason: SeenReason(where));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The sessions among <paramref name="sessionIds"/> that <paramref name="userId"/> has second-marked (GAD-T485).</summary>
    public static async Task<HashSet<Guid>> SecondMarkedAsync(GadflyDbContext db, Guid org, IReadOnlyCollection<Guid> sessionIds, Guid userId, CancellationToken ct)
    {
        if (sessionIds.Count == 0) return [];
        var ids = sessionIds.Select(i => (Guid?)i).ToList();
        return (await db.AuditEvents.AsNoTracking().Where(a => a.OrganizationId == org && a.Action == DoubleMarkingService.MarkAction
                && a.EntityType == DoubleMarkingService.MarkEntity && a.ActorUserId == userId && ids.Contains(a.SessionId))
            .Select(a => a.SessionId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
    }
}
