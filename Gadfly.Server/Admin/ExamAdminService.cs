using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Exams;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Admin;

/// <summary>One exam in the exams manager's list, with its counts. Average time is over completed attempts only.</summary>
public sealed record ExamAdminRow(Guid Id, string Title, string? CourseTitle, string CollectionTitle, ExamStatus Status, AssessmentMode AssessmentMode,
    int Objectives, int Assigned, int Started, int Completed, TimeSpan? AverageTime, ExamWindow Window, DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt);

/// <summary>Where an exam's assignments stand right now: their earliest opening and latest closing, and how many are open.</summary>
public sealed record ExamWindow(int Assignments, int OpenNow, int NotOpenYet, int Closed, DateTimeOffset? EarliestOpens, DateTimeOffset? LatestCloses)
{
    public static readonly ExamWindow None = new(0, 0, 0, 0, null, null);

    /// <summary>A short plain description for the list ("Open to 12", "Closed", "Opens 3 Dec"...).</summary>
    public string Describe()
    {
        if (Assignments == 0) return "Default terms";
        if (OpenNow == Assignments) return LatestCloses is { } c ? $"Open, closes {c.UtcDateTime:d MMM HH:mm} UTC" : "Open";
        if (Closed == Assignments) return "Closed";
        if (NotOpenYet == Assignments) return EarliestOpens is { } o ? $"Opens {o.UtcDateTime:d MMM HH:mm} UTC" : "Not open yet";
        return $"Open to {OpenNow} of {Assignments}";
    }
}

/// <summary>The settings the exams manager edits. Duration and the summative floor are frozen once the exam is published.</summary>
public sealed class ExamSettingsForm
{
    public Guid ExamId { get; set; }
    public int DurationMinutes { get; set; } = 20;
    public IntegrityLevel IntegrityLevel { get; set; } = IntegrityLevel.Standard;
    public bool ReadOutCheck { get; set; } = true;
    public int MinSummativeObjectives { get; set; } = ExamObjectives.DefaultMinSummativeObjectives;
    public string? VoiceId { get; set; }
    public string? AvatarId { get; set; }
    /// <summary>The examiner's voice quality/speed tier (GAD-T617, <see cref="Voice.VoiceTier"/> id); blank uses the organization's.</summary>
    public string? VoiceTier { get; set; }
    /// <summary>The spoken language this exam expects (GAD-T357), e.g. "en"; blank lets the recognizer detect it.</summary>
    public string? Language { get; set; }
    /// <summary>Why, recorded in the audit log. Required once students have started the exam.</summary>
    public string? Reason { get; set; }
}

/// <summary>An exam's settings and what may still change.</summary>
public sealed record ExamSettingsView(ExamSettingsForm Form, string Title, ExamStatus Status, int StartedAttempts)
{
    /// <summary>Duration and the summative floor shape what is measured, so they are frozen with the published policy.</summary>
    public bool PolicyFrozen => Status != ExamStatus.Draft;
}

/// <summary>How many attempts examined one objective (or, for an exam without objectives, one concept).</summary>
public sealed record ObjectiveCoverageRow(string Key, string Title, string? ConceptKey, int AttemptsExamined, int AttemptsWithIndependentEvidence);

public sealed record ExamCoverage(Guid ExamId, string Title, int AttemptsStarted, bool ByConcept, IReadOnlyList<ObjectiveCoverageRow> Rows);

public sealed record ExamImportResult(bool Ok, string Message, Guid? Id, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record ExamExport(string FileName, string Json);

public sealed record CourseOption(Guid Id, string Title);

/// <summary>One of the organization's exam categories (GAD-T656), with how many exams are filed under it.</summary>
public sealed record ExamCategoryRow(Guid Id, string Name, string Slug, int SortOrder, IReadOnlyDictionary<string, string> DisplayNames, int Exams)
{
    /// <summary>The category's name in the given language (primary tag, e.g. "ko"), falling back to <see cref="Name"/>.</summary>
    public string DisplayName(string? language) =>
        language is { Length: > 0 } && DisplayNames.TryGetValue(language.Split('-')[0].ToLowerInvariant(), out var n) && n.Length > 0 ? n : Name;
}

/// <summary>The create-category form (GAD-T656). Slug is optional (made from the name when blank).</summary>
public sealed class ExamCategoryForm
{
    public string Name { get; set; } = "";
    public string? Slug { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Optional display names by primary language tag (e.g. "ko", "es"); blank entries are dropped.</summary>
    public Dictionary<string, string> DisplayNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The admin exams manager (GAD-T297): every exam with its counts, duplicating (a deep copy of the exam, its objectives, rubric
/// criteria and level descriptors, hint ladders, session settings and grading policy, never its assignments or attempts), archiving
/// and restoring, opening and closing the window on its assignments, editing settings, objective coverage, and export and import
/// as versioned JSON (<see cref="ExamDefinitionJson"/>). Every change checks a <see cref="Permission"/> and is written to the audit
/// log in the same save. Everything is scoped to the caller's organization; the DbContext's tenant filter and write guard back that up.
/// </summary>
public sealed class ExamAdminService(GadflyDbContext db, ITenantContext tenant, IOrganizationDirectory directory, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AssessmentGuard guard = new(db, tenant, clock);

    private Guid Org => tenant.OrganizationId ?? throw new InvalidOperationException("No active organization.");
    private Guid User => tenant.UserId ?? throw new InvalidOperationException("No signed-in user.");

    public AssessmentGuard Guard => guard;

    /// <summary>True when the caller may manage exams here (Owner, Admin, Instructor).</summary>
    public bool CanManage => guard.Has(Permission.ManageAssessments);

    // ================================================================== reading

    /// <summary>
    /// Every exam with its objectives, assignments, started and completed attempts and average completed time. Five grouped
    /// queries whatever the number of exams; archived exams only when asked for.
    /// </summary>
    public async Task<IReadOnlyList<ExamAdminRow>> ListAsync(bool includeArchived = true, CancellationToken ct = default)
    {
        if (!CanManage) return [];
        var org = Org;
        var now = clock.GetUtcNow();
        var q = db.Exams.AsNoTracking().Where(e => e.OrganizationId == org);
        if (!includeArchived) q = q.Where(e => e.Status != ExamStatus.Archived);
        var exams = await q.OrderBy(e => e.Status).ThenByDescending(e => e.CreatedAt)
            .Select(e => new { e.Id, e.Title, e.CourseId, e.CollectionId, e.Status, e.AssessmentMode, e.CreatedAt, e.PublishedAt })
            .ToListAsync(ct);
        if (exams.Count == 0) return [];

        var courses = await db.Courses.AsNoTracking().Where(c => c.OrganizationId == org).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var collections = await db.SourceCollections.AsNoTracking().Where(c => c.OrganizationId == org).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var objectives = await db.LearningObjectives.AsNoTracking().Where(o => o.OrganizationId == org)
            .GroupBy(o => o.ExamId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var windows = await db.ExamAssignments.AsNoTracking().Where(a => a.OrganizationId == org)
            .GroupBy(a => a.ExamId)
            .Select(g => new
            {
                g.Key,
                Count = g.Count(),
                NotOpenYet = g.Count(a => a.OpensAt != null && a.OpensAt > now),
                Closed = g.Count(a => a.ClosesAt != null && a.ClosesAt <= now),
                EarliestOpens = g.Min(a => a.OpensAt),
                LatestCloses = g.Max(a => a.ClosesAt),
            })
            .ToDictionaryAsync(x => x.Key, ct);
        const string promptSetChanged = nameof(EndReason.PromptSetChanged);
        var sessions = await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org)
            .GroupBy(s => s.ExamId)
            .Select(g => new
            {
                g.Key,
                Started = g.Count(s => s.StartedAt != null),
                Completed = g.Count(s => s.State == SessionState.Ended && s.EndReason != promptSetChanged),
                AverageSeconds = g.Average(s => s.State == SessionState.Ended && s.EndReason != promptSetChanged && s.StartedAt != null && s.EndedAt != null
                    ? (double?)EF.Functions.DateDiffSecond(s.StartedAt, s.EndedAt) : null),
            })
            .ToDictionaryAsync(x => x.Key, ct);

        return exams.Select(e =>
        {
            var w = windows.GetValueOrDefault(e.Id);
            var window = w is null ? ExamWindow.None
                : new ExamWindow(w.Count, w.Count - w.NotOpenYet - w.Closed, w.NotOpenYet, w.Closed, w.EarliestOpens, w.LatestCloses);
            var s = sessions.GetValueOrDefault(e.Id);
            return new ExamAdminRow(e.Id, e.Title, e.CourseId is { } c ? courses.GetValueOrDefault(c) : null,
                collections.GetValueOrDefault(e.CollectionId, "(missing source material)"), e.Status, e.AssessmentMode,
                objectives.GetValueOrDefault(e.Id), w?.Count ?? 0, s?.Started ?? 0, s?.Completed ?? 0,
                s?.AverageSeconds is { } secs ? TimeSpan.FromSeconds(Math.Round(secs)) : null, window, e.CreatedAt, e.PublishedAt);
        }).ToList();
    }

    public async Task<ExamSettingsView?> SettingsAsync(Guid examId, CancellationToken ct = default)
    {
        if (!CanManage) return null;
        var org = Org;
        var e = await db.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == examId && x.OrganizationId == org, ct);
        if (e is null) { await db.RefuseForeignAsync(OwnershipScope.Kind.Exam, examId, "Opening an exam's settings", ct); return null; }
        var p = ExamService.ReadPolicy(e.PolicyJson);
        var started = await db.ExamSessions.AsNoTracking().CountAsync(s => s.ExamId == examId && s.OrganizationId == org && s.StartedAt != null, ct);
        return new ExamSettingsView(new ExamSettingsForm
        {
            ExamId = e.Id,
            DurationMinutes = (int)Math.Round(p.TimeBudget.TotalMinutes),
            IntegrityLevel = e.IntegrityLevel,
            ReadOutCheck = p.ReadOutCheck,
            MinSummativeObjectives = p.MinSummativeObjectives,
            VoiceId = e.VoiceId,
            AvatarId = e.AvatarId,
            VoiceTier = Voice.VoiceTier.Normalize(e.VoiceTier) ?? "",
            Language = p.Language,
        }, e.Title, e.Status, started);
    }

    /// <summary>
    /// How many attempts examined each objective, and how many of those produced independent evidence (not assisted, not
    /// after a question that gave the answer away). An exam without learning objectives is covered by concept instead.
    /// </summary>
    public async Task<ExamCoverage?> CoverageAsync(Guid examId, CancellationToken ct = default)
    {
        if (!CanManage) return null;
        var org = Org;
        var exam = await db.Exams.AsNoTracking().Where(x => x.Id == examId && x.OrganizationId == org).Select(x => new { x.Id, x.Title, x.PolicyJson }).FirstOrDefaultAsync(ct);
        if (exam is null) { await db.RefuseForeignAsync(OwnershipScope.Kind.Exam, examId, "Opening an exam's coverage", ct); return null; }
        var sessionIds = db.ExamSessions.Where(s => s.ExamId == examId && s.OrganizationId == org).Select(s => s.Id);
        var started = await db.ExamSessions.AsNoTracking().CountAsync(s => s.ExamId == examId && s.OrganizationId == org && s.StartedAt != null, ct);
        var objectives = await db.LearningObjectives.AsNoTracking().Where(o => o.ExamId == examId && o.OrganizationId == org)
            .OrderBy(o => o.Ordinal).Select(o => new { o.Id, o.Key, o.Title, o.ConceptKey }).ToListAsync(ct);

        if (objectives.Count > 0)
        {
            var counts = await db.AssessmentEvidence.AsNoTracking()
                .Where(v => v.OrganizationId == org && sessionIds.Contains(v.SessionId))
                .GroupBy(v => v.ObjectiveId)
                .Select(g => new
                {
                    g.Key,
                    Examined = g.Select(v => v.SessionId).Distinct().Count(),
                    Independent = g.Where(v => !v.Assisted && !v.Contaminated).Select(v => v.SessionId).Distinct().Count(),
                })
                .ToDictionaryAsync(x => x.Key, ct);
            return new ExamCoverage(exam.Id, exam.Title, started, false, objectives.Select(o =>
            {
                var c = counts.GetValueOrDefault(o.Id);
                return new ObjectiveCoverageRow(o.Key, o.Title, o.ConceptKey, c?.Examined ?? 0, c?.Independent ?? 0);
            }).ToList());
        }

        // The older exam builder's concept-only exams: a concept counts as examined when a question in the attempt was about it.
        var keys = ExamService.ReadPolicy(exam.PolicyJson).ConceptIds;
        var byConcept = await db.SessionTurns.AsNoTracking()
            .Where(t => t.OrganizationId == org && sessionIds.Contains(t.SessionId))
            .GroupBy(t => t.ConceptKey)
            .Select(g => new { g.Key, Examined = g.Select(t => t.SessionId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Examined, ct);
        var titles = (await ConceptTitlesAsync(examId, ct)) ?? [];
        var all = keys.Concat(byConcept.Keys.Where(k => !keys.Contains(k))).ToList();
        return new ExamCoverage(exam.Id, exam.Title, started, true,
            all.Select(k => new ObjectiveCoverageRow(k, titles.GetValueOrDefault(k, k), k, byConcept.GetValueOrDefault(k), byConcept.GetValueOrDefault(k))).ToList());
    }

    /// <summary>Source collections ready to examine on (for an import).</summary>
    public Task<IReadOnlyList<CollectionOption>> CollectionsAsync(CancellationToken ct = default) =>
        CanManage ? Exams().ReadyCollectionsAsync(ct) : Task.FromResult<IReadOnlyList<CollectionOption>>([]);

    /// <summary>Active courses (for an import).</summary>
    public async Task<IReadOnlyList<CourseOption>> CoursesAsync(CancellationToken ct = default)
    {
        if (!CanManage) return [];
        var org = Org;
        return await db.Courses.AsNoTracking().Where(c => c.OrganizationId == org && !c.IsArchived).OrderBy(c => c.Title)
            .Select(c => new CourseOption(c.Id, c.Title)).ToListAsync(ct);
    }

    // ================================================================== duplicate

    /// <summary>
    /// A deep copy of the exam as a new draft: its details, session settings (with a new random seed), learning objectives,
    /// rubric criteria with their level descriptors, hint ladders and grading policy (unlocked, since the copy is a draft).
    /// Never its assignments, attempts, grades or results. Audited as assessment.duplicated, naming the original.
    /// </summary>
    public async Task<AssessmentResult> DuplicateAsync(Guid examId, string? title = null, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Duplicating an exam", "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var source = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (source is null) return new(false, "That exam no longer exists.");
        var newTitle = string.IsNullOrWhiteSpace(title) ? await CopyTitleAsync(source.Title, ct) : title.Trim();
        if (newTitle.Length > 200) return new(false, "Keep the title under 200 characters.");

        var objectives = await LoadObjectivesAsync(examId, ct);
        var grading = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == examId && p.OrganizationId == org, ct);
        var copy = CopyOf(source, objectives, grading, newTitle, User, clock.GetUtcNow(), Random.Shared.Next());
        db.Exams.Add(copy.Exam);
        db.GradingPolicies.Add(copy.Grading);
        foreach (var o in copy.Objectives)
        {
            db.LearningObjectives.Add(o);
            foreach (var c in o.Criteria)
            {
                db.RubricCriteria.Add(c);
                db.RubricLevelDescriptors.AddRange(c.Levels);
            }
        }
        guard.Audit("assessment.duplicated", "Exam", copy.Exam.Id.ToString(),
            before: new { SourceExamId = source.Id, source.Title, Status = source.Status.ToString() },
            after: new { copy.Exam.Title, Objectives = copy.Objectives.Count, Criteria = copy.Objectives.Sum(o => o.Criteria.Count) },
            reason: $"Copied from \"{source.Title}\" ({source.Id}); assignments and attempts are not copied.");
        await SaveAsync(ct);
        return new(true, $"Copied as \"{copy.Exam.Title}\", a draft. Its assignments and attempts stayed with the original.", copy.Exam.Id);
    }

    /// <summary>What <see cref="DuplicateAsync"/> adds: pure, so the copy's shape is tested without a database.</summary>
    public sealed record ExamCopy(Exam Exam, GradingPolicy Grading, IReadOnlyList<LearningObjective> Objectives);

    /// <summary>
    /// Builds the deep copy: new ids throughout, the same organization, course and source material, status Draft, a fresh
    /// seed, the grading policy unlocked. Objective keys stay the same (they are unique within an exam, not across exams).
    /// </summary>
    public static ExamCopy CopyOf(Exam source, IReadOnlyList<LearningObjective> objectives, GradingPolicy? grading, string title, Guid createdBy,
        DateTimeOffset now, int seed)
    {
        var policy = ExamService.ReadPolicy(source.PolicyJson) with { Seed = seed, RubricApproval = null };   // a copy is approved on its own (GAD-T437)
        var exam = new Exam
        {
            OrganizationId = source.OrganizationId,
            CollectionId = source.CollectionId,
            CourseId = source.CourseId,
            Subject = source.Subject,
            Topic = source.Topic,
            AssessmentMode = source.AssessmentMode,
            Title = title,
            Instructions = source.Instructions,
            PolicyJson = JsonSerializer.Serialize(policy, Json),
            Mode = source.Mode,
            IntegrityLevel = source.IntegrityLevel,
            VoiceId = source.VoiceId,
            AvatarId = source.AvatarId,
            VoiceTier = source.VoiceTier,   // the examiner's voice tier (GAD-T617)
            PromptSetId = source.PromptSetId,
            PromptSetVersion = source.PromptSetVersion,
            Languages = source.Languages,   // the offered languages (GAD-T539); the copy's texts are drafted and approved on their own
            Status = ExamStatus.Draft,
            CreatedByUserId = createdBy,
            CreatedAt = now,
        };
        var g = grading is null ? PolicySnapshot.Default : PolicySnapshot.Of(grading);
        var gradingCopy = new GradingPolicy { ExamId = exam.Id, OrganizationId = source.OrganizationId };
        g.ApplyTo(gradingCopy);
        var copies = objectives.OrderBy(o => o.Ordinal).Select(o =>
        {
            var oc = new LearningObjective
            {
                OrganizationId = source.OrganizationId, ExamId = exam.Id, Key = o.Key, Title = o.Title, Description = o.Description,
                ConceptKey = o.ConceptKey, Weight = o.Weight, MaxScore = o.MaxScore, Ordinal = o.Ordinal, HintLadderJson = o.HintLadderJson,
            };
            foreach (var c in o.Criteria.OrderBy(c => c.Ordinal))
            {
                var cc = new RubricCriterion
                {
                    OrganizationId = source.OrganizationId, ObjectiveId = oc.Id, Dimension = c.Dimension, Name = c.Name,
                    Description = c.Description, Weight = c.Weight, Ordinal = c.Ordinal,
                };
                foreach (var l in c.Levels.OrderBy(l => l.Level))
                    cc.Levels.Add(new RubricLevelDescriptor { OrganizationId = source.OrganizationId, CriterionId = cc.Id, Level = l.Level, Descriptor = l.Descriptor });
                oc.Criteria.Add(cc);
            }
            return oc;
        }).ToList();
        return new ExamCopy(exam, gradingCopy, copies);
    }

    // ================================================================== archive / restore

    /// <summary>Archives the exam: students can no longer start it and it can't be assigned; attempts and results stay.</summary>
    public Task<AssessmentResult> ArchiveAsync(Guid examId, CancellationToken ct = default) =>
        new AssessmentService(db, tenant, directory, clock).ArchiveAsync(examId, ct);

    /// <summary>
    /// Restores an archived exam to where it was: published when it had been published (its frozen settings and locked grading
    /// policy unchanged), otherwise a draft. Audited as assessment.restored.
    /// </summary>
    public async Task<AssessmentResult> RestoreAsync(Guid examId, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Restoring an exam", "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        if (exam.Status != ExamStatus.Archived) return new(true, "It isn't archived.", exam.Id);
        exam.Status = exam.PublishedAt is null ? ExamStatus.Draft : ExamStatus.Published;
        guard.Audit("assessment.restored", "Exam", exam.Id.ToString(), new { Status = nameof(ExamStatus.Archived) }, new { Status = exam.Status.ToString() });
        await SaveAsync(ct);
        return new(true, exam.Status == ExamStatus.Published
            ? "Restored and published again. Every student can start it while their window is open."
            : "Restored as a draft.", exam.Id);
    }

    // ================================================================== windows

    /// <summary>Opens the exam now for everyone it is assigned to: an opening date still to come becomes now, a closing date already past is cleared.</summary>
    public Task<AssessmentResult> OpenNowAsync(Guid examId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return ChangeWindowsAsync(examId, "Opening an exam's window", "assessment.window.opened", a =>
        {
            if (a.OpensAt is { } o && o > now) a.OpensAt = now;
            if (a.ClosesAt is { } c && c <= now) a.ClosesAt = null;
        }, requirePublished: true, "Open now for {0}. Students can start it straight away.", ct);
    }

    /// <summary>Closes the exam now for everyone it is assigned to. An attempt already in progress can still be finished.</summary>
    public Task<AssessmentResult> CloseNowAsync(Guid examId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return ChangeWindowsAsync(examId, "Closing an exam's window", "assessment.window.closed", a =>
        {
            if (a.ClosesAt is null || a.ClosesAt > now) a.ClosesAt = now;
            if (a.OpensAt is { } o && o > now) a.OpensAt = now;
        }, requirePublished: false, "Closed for {0}. No new attempt can start; an attempt already in progress can still be finished.", ct);
    }

    /// <summary>Sets the same window on every assignment of the exam (either end may be left open).</summary>
    public Task<AssessmentResult> SetWindowAsync(Guid examId, DateTimeOffset? opensAt, DateTimeOffset? closesAt, CancellationToken ct = default)
    {
        if (opensAt is { } o && closesAt is { } c && c <= o)
            return Task.FromResult(new AssessmentResult(false, "The window must close after it opens."));
        return ChangeWindowsAsync(examId, "Setting an exam's window", "assessment.window.set", a =>
        {
            a.OpensAt = opensAt;
            a.ClosesAt = closesAt;
        }, requirePublished: false, "Window set for {0}.", ct);
    }

    private async Task<AssessmentResult> ChangeWindowsAsync(Guid examId, string attempted, string action, Action<ExamAssignment> change, bool requirePublished,
        string done, CancellationToken ct)
    {
        if (!await guard.RequireAsync(Permission.AssignExaminations, attempted, "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.AssignExaminations));
        var org = Org;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        if (requirePublished && exam.Status != ExamStatus.Published)
            return new(false, exam.Status == ExamStatus.Archived ? "It is archived. Restore it first, then open it." : "Publish it first; a draft can't be opened to students.");
        var assignments = await db.ExamAssignments.Where(a => a.ExamId == examId && a.OrganizationId == org).ToListAsync(ct);
        if (assignments.Count == 0) return new(false, "No student has terms for it yet. Set terms first; the window is set per student.");
        var before = assignments.Select(a => new { a.Id, a.StudentUserId, a.OpensAt, a.ClosesAt }).ToList();
        foreach (var a in assignments) change(a);
        var changed = assignments.Where(a => db.Entry(a).State == EntityState.Modified).ToList();
        if (changed.Count == 0)
        {
            db.ChangeTracker.Clear();
            return new(true, "Nothing to change: every student already had that window.", examId);
        }
        guard.Audit(action, "Exam", examId.ToString(),
            before: before.Where(b => changed.Any(c => c.Id == b.Id)).ToList(),
            after: changed.Select(a => new { a.Id, a.StudentUserId, a.OpensAt, a.ClosesAt }).ToList(),
            reason: $"{changed.Count} of {assignments.Count} student term(s) changed.");
        await SaveAsync(ct);
        return new(true, string.Format(done, changed.Count == 1 ? "1 student" : $"{changed.Count} students"), examId);
    }

    // ================================================================== settings

    /// <summary>
    /// Saves the exam's settings. A draft takes them all. A published or archived exam keeps its duration and summative floor
    /// (they shape what is measured, so they are frozen with its policy: duplicate it to change them); its integrity level,
    /// read-out check and examiner voice and avatar may change and apply to attempts that start from now on. Once students have
    /// started it, a reason is required. Audited as assessment.settings.updated with before and after.
    /// </summary>
    public async Task<AssessmentResult> SaveSettingsAsync(ExamSettingsForm form, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Changing an exam's settings", "Exam", form.ExamId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == form.ExamId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        if (!Enum.IsDefined(form.IntegrityLevel)) return new(false, "Choose an integrity level.");
        var voice = string.IsNullOrWhiteSpace(form.VoiceId) ? null : form.VoiceId.Trim();
        var avatar = string.IsNullOrWhiteSpace(form.AvatarId) ? null : form.AvatarId.Trim();
        if (voice?.Length > 100 || avatar?.Length > 100) return new(false, "Keep the voice and avatar ids under 100 characters.");
        if (voice is not null && !Voice.ExaminerVoice.IsWellFormed(voice)) return new(false, "That voice id doesn't look like an ElevenLabs voice id (letters and digits only, copied from the voice's page in ElevenLabs).");
        var voiceTier = Voice.VoiceTier.Normalize(form.VoiceTier);
        if (voiceTier is null && !string.IsNullOrWhiteSpace(form.VoiceTier))
            return new(false, $"Choose a voice quality: {string.Join(" or ", Voice.VoiceTier.All.Select(t => t.Label))}, or the organization's default.");
        var reason = string.IsNullOrWhiteSpace(form.Reason) ? null : form.Reason.Trim();
        if (reason?.Length > 1000) return new(false, "Keep the reason under 1000 characters.");
        var language = string.IsNullOrWhiteSpace(form.Language) ? null : form.Language.Trim();

        var policy = ExamService.ReadPolicy(exam.PolicyJson);
        var frozen = exam.Status != ExamStatus.Draft;
        var currentDuration = (int)Math.Round(policy.TimeBudget.TotalMinutes);
        if (frozen && (form.DurationMinutes != currentDuration || form.MinSummativeObjectives != policy.MinSummativeObjectives))
            return new(false, $"This exam is {exam.Status.ToString().ToLowerInvariant()}, so its duration and minimum objectives are frozen: students already sat it under them. Duplicate it to make a version with different ones.");
        if (form.DurationMinutes < ExamService.MinDurationMinutes || form.DurationMinutes > ExamService.MaxDurationMinutes)
            return new(false, $"The duration must be {ExamService.MinDurationMinutes} to {ExamService.MaxDurationMinutes} minutes.");
        if (form.MinSummativeObjectives is < 1 or > 50) return new(false, "The minimum objectives for a summative result must be 1 to 50.");

        var started = await db.ExamSessions.AsNoTracking().CountAsync(s => s.ExamId == exam.Id && s.OrganizationId == org && s.StartedAt != null, ct);
        var before = SettingsAudit(exam, policy);
        var updated = policy with
        {
            TimeBudget = TimeSpan.FromMinutes(form.DurationMinutes),
            ReadOutCheck = form.ReadOutCheck,
            MinSummativeObjectives = form.MinSummativeObjectives,
            Language = language,
        };
        try { updated.Validate(); }
        catch (ArgumentException ex) { return new(false, ex.Message); }
        exam.IntegrityLevel = form.IntegrityLevel;
        exam.VoiceId = voice;
        exam.AvatarId = avatar;
        var tierBefore = exam.VoiceTier;
        exam.VoiceTier = voiceTier;
        var newJson = JsonSerializer.Serialize(updated, Json);
        if (updated != policy) exam.PolicyJson = newJson;
        var after = SettingsAudit(exam, updated);
        if (JsonSerializer.Serialize(before, Json) == JsonSerializer.Serialize(after, Json))
        {
            db.ChangeTracker.Clear();
            return new(true, "Nothing changed.", exam.Id);
        }
        if (started > 0 && (reason is null || reason.Length < 10))
        {
            db.ChangeTracker.Clear();
            return new(false, $"{started} attempt{(started == 1 ? " has" : "s have")} already started, so say why the settings change (a sentence); it goes in the audit log.");
        }
        guard.Audit("assessment.settings.updated", "Exam", exam.Id.ToString(), before, after,
            reason ?? (frozen ? "Applies to attempts that start from now on." : null));
        // GAD-T617: the voice tier trades the examiner's latency for quality, so its change is also recorded on its own.
        if (tierBefore != voiceTier)
            guard.Audit("exam.voice-tier.changed", "Exam", exam.Id.ToString(), new { voiceTier = tierBefore }, new { voiceTier },
                reason ?? "GAD-T415: the examiner's voice quality/speed tier for this exam.");
        await SaveAsync(ct);
        return new(true, frozen || started > 0 ? "Settings saved. They apply to attempts that start from now on; attempts already started keep theirs." : "Settings saved.", exam.Id);
    }

    private static object SettingsAudit(Exam e, SessionPolicy p) => new
    {
        DurationMinutes = (int)Math.Round(p.TimeBudget.TotalMinutes),
        IntegrityLevel = e.IntegrityLevel.ToString(),
        p.ReadOutCheck,
        p.MinSummativeObjectives,
        e.VoiceId,
        e.AvatarId,
        e.VoiceTier,
        p.Language,
    };

    // ================================================================== export / import

    /// <summary>The exam as a versioned JSON definition (<see cref="ExamDefinitionJson"/>). Audited as assessment.exported.</summary>
    public async Task<ExamExport?> ExportAsync(Guid examId, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Exporting an exam", "Exam", examId.ToString(), ct)) return null;
        var org = Org;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) { await db.RefuseForeignAsync(OwnershipScope.Kind.Exam, examId, "Exporting an exam", ct); return null; }
        var objectives = await LoadObjectivesAsync(examId, ct);
        var grading = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == examId && p.OrganizationId == org, ct);
        var course = exam.CourseId is { } cid ? await db.Courses.AsNoTracking().Where(c => c.Id == cid && c.OrganizationId == org).Select(c => c.Title).FirstOrDefaultAsync(ct) : null;
        var collection = await db.SourceCollections.AsNoTracking().Where(c => c.Id == exam.CollectionId && c.OrganizationId == org).Select(c => c.Title).FirstOrDefaultAsync(ct);
        var file = ExamDefinitionJson.Build(exam, objectives, grading, course, collection, clock.GetUtcNow(), "Gadfly Exams");
        guard.Audit("assessment.exported", "Exam", exam.Id.ToString(), after: new { exam.Title, Objectives = objectives.Count, Format = ExamDefinitionJson.FormatName, Version = ExamDefinitionJson.CurrentVersion });
        await SaveAsync(ct);
        return new ExamExport(ExamDefinitionJson.FileName(exam.Title), ExamDefinitionJson.Serialize(file));
    }

    /// <summary>
    /// Imports a JSON definition as a new draft on the chosen source material (and course, else the course named in the file
    /// when this organization has one by that title). Every problem in the file is listed and nothing is created while any
    /// remain. Objectives whose concept the chosen source material doesn't have arrive unlinked, with a warning: link them
    /// before publishing. Audited as assessment.imported.
    /// </summary>
    public async Task<ExamImportResult> ImportAsync(string? json, Guid collectionId, Guid? courseId, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Importing an exam", "Exam", null, ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments), null, [], []);
        var read = ExamDefinitionJson.Parse(json);
        if (!read.Ok) return new(false, $"The file can't be imported: {Count(read.Errors.Count, "problem")} to fix.", null, read.Errors, []);
        var d = read.Definition!;
        var org = Org;

        var collection = await db.SourceCollections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == collectionId && c.OrganizationId == org, ct);
        if (collection is null) return new(false, "Choose the source material the exam should examine.", null, ["Choose the source material."], []);
        var warnings = new List<string>();
        Guid? course = null;
        if (courseId is { } cid)
        {
            if (!await db.Courses.AnyAsync(c => c.Id == cid && c.OrganizationId == org && !c.IsArchived, ct))
                return new(false, "That course no longer exists or is archived.", null, ["Choose an active course."], []);
            course = cid;
        }
        else if (!string.IsNullOrWhiteSpace(d.CourseTitle))
        {
            course = await db.Courses.AsNoTracking().Where(c => c.OrganizationId == org && !c.IsArchived && c.Title == d.CourseTitle.Trim())
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (course is null) warnings.Add($"This organization has no active course called \"{d.CourseTitle}\"; choose one in the assessment editor before publishing.");
        }
        else warnings.Add("No course chosen; choose one in the assessment editor before publishing.");

        var concepts = (await Exams().ConceptsAsync(collectionId, ct)).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        if (concepts.Count == 0) warnings.Add($"\"{collection.Title}\" has no concept map yet, so no objective could be linked to a concept.");
        var title = d.Title!.Trim();
        var policy = ExamDefinitionJson.ToPolicy(d.Settings, new SessionPolicy()) with { Seed = Random.Shared.Next(), Objectives = [] };
        var missingKeys = policy.ConceptIds.Where(k => !concepts.Contains(k)).ToList();
        if (missingKeys.Count > 0)
        {
            policy = policy with { ConceptIds = policy.ConceptIds.Where(concepts.Contains).ToList() };
            if (concepts.Count > 0) warnings.Add($"These concepts are not in \"{collection.Title}\" and were left out: {string.Join(", ", missingKeys)}.");
        }

        var now = clock.GetUtcNow();
        var exam = new Exam
        {
            OrganizationId = org,
            CollectionId = collectionId,
            CourseId = course,
            Title = title,
            Subject = d.Subject?.Trim() ?? "",
            Topic = d.Topic?.Trim() ?? "",
            Instructions = string.IsNullOrWhiteSpace(d.Instructions) ? null : d.Instructions.Trim(),
            AssessmentMode = ExamDefinitionJson.EnumOr(d.AssessmentMode, AssessmentMode.Examination),
            Mode = ExamDefinitionJson.EnumOr(d.Mode, ExamMode.Voice),
            IntegrityLevel = ExamDefinitionJson.EnumOr(d.IntegrityLevel, IntegrityLevel.Standard),
            VoiceId = string.IsNullOrWhiteSpace(d.VoiceId) ? null : d.VoiceId.Trim(),
            AvatarId = string.IsNullOrWhiteSpace(d.AvatarId) ? null : d.AvatarId.Trim(),
            VoiceTier = Voice.VoiceTier.Normalize(d.VoiceTier),
            PromptSetId = ExamPrompts.SetId,
            PromptSetVersion = ExamPrompts.Version,
            PolicyJson = JsonSerializer.Serialize(policy, Json),
            Status = ExamStatus.Draft,
            CreatedByUserId = User,
            CreatedAt = now,
        };
        db.Exams.Add(exam);
        var grading = new GradingPolicy { ExamId = exam.Id, OrganizationId = org };
        ExamDefinitionJson.ToSnapshot(d.GradingPolicy).ApplyTo(grading);
        db.GradingPolicies.Add(grading);

        // A fixed opening question (GAD-T436) is approved only after the leak check against this organization's own rubric.
        if ((d.Objectives ?? []).Any(od => !string.IsNullOrWhiteSpace(od?.OpeningQuestion)))
            warnings.Add("The file has fixed opening questions. They were not imported as approved: approve each again on the Objectives step, " +
                         "where it is checked against this source material.");
        var unlinked = new List<string>();
        var ordinal = 0;
        foreach (var od in d.Objectives ?? [])
        {
            var conceptKey = string.IsNullOrWhiteSpace(od.ConceptKey) ? null : od.ConceptKey.Trim();
            if (conceptKey is not null && !concepts.Contains(conceptKey))
            {
                unlinked.Add($"{od.Title?.Trim()} ({conceptKey})");
                conceptKey = null;
            }
            var (ladder, _) = HintLadders.Parse(string.Join('\n', od.HintLadder ?? []));
            var o = new LearningObjective
            {
                OrganizationId = org, ExamId = exam.Id, Key = od.Key!.Trim(), Title = od.Title!.Trim(),
                Description = string.IsNullOrWhiteSpace(od.Description) ? null : od.Description.Trim(), ConceptKey = conceptKey,
                Weight = od.Weight ?? 1m, MaxScore = od.MaxScore ?? 100m, Ordinal = ordinal++,
                HintLadderJson = JsonSerializer.Serialize(ladder ?? []),
            };
            db.LearningObjectives.Add(o);
            var cOrdinal = 0;
            foreach (var cd in od.Criteria ?? [])
            {
                var c = new RubricCriterion
                {
                    OrganizationId = org, ObjectiveId = o.Id, Dimension = ExamDefinitionJson.EnumOr(cd.Dimension, AssessmentDimension.ConceptualUnderstanding),
                    Name = cd.Name!.Trim(), Description = string.IsNullOrWhiteSpace(cd.Description) ? null : cd.Description.Trim(),
                    Weight = cd.Weight ?? 1m, Ordinal = cOrdinal++,
                };
                db.RubricCriteria.Add(c);
                foreach (var level in Enum.GetValues<MasteryLevel>())
                {
                    var text = cd.Levels?.ElementAtOrDefault((int)level);
                    db.RubricLevelDescriptors.Add(new RubricLevelDescriptor
                    {
                        OrganizationId = org, CriterionId = c.Id, Level = level,
                        Descriptor = string.IsNullOrWhiteSpace(text) ? MasteryLevels.DefaultDescriptor(level) : text.Trim(),
                    });
                }
            }
        }
        if (unlinked.Count > 0)
            warnings.Add($"{Count(unlinked.Count, "objective")} arrived without a concept, because \"{collection.Title}\" doesn't have it: {string.Join("; ", unlinked)}. Link {(unlinked.Count == 1 ? "it" : "them")} in the assessment editor before publishing.");

        guard.Audit("assessment.imported", "Exam", exam.Id.ToString(),
            after: new { exam.Title, CollectionId = collectionId, CourseId = course, Objectives = d.Objectives?.Count ?? 0, Warnings = warnings },
            reason: d.CollectionTitle is { Length: > 0 } from ? $"Imported from a file exported on \"{from}\"." : "Imported from a file.");
        await SaveAsync(ct);
        return new(true, $"Imported \"{exam.Title}\" as a draft.{(warnings.Count > 0 ? " Read the notes below before publishing." : "")}", exam.Id, [], warnings);
    }

    // ================================================================== categories (GAD-T656)

    /// <summary>The organization's exam categories, by sort order then name, each with its exam count.</summary>
    public async Task<IReadOnlyList<ExamCategoryRow>> CategoriesAsync(CancellationToken ct = default)
    {
        if (!CanManage) return [];
        var org = Org;
        var categories = await db.ExamCategories.AsNoTracking().Where(c => c.OrganizationId == org)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        if (categories.Count == 0) return [];
        var counts = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && e.CategoryId != null)
            .GroupBy(e => e.CategoryId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return categories.Select(c => new ExamCategoryRow(c.Id, c.Name, c.Slug, c.SortOrder, ReadDisplayNames(c.DisplayNamesJson),
            counts.GetValueOrDefault(c.Id))).ToList();
    }

    /// <summary>Each exam's category (exam id to category id), for exams that have one.</summary>
    public async Task<IReadOnlyDictionary<Guid, Guid>> ExamCategoryIdsAsync(CancellationToken ct = default)
    {
        if (!CanManage) return new Dictionary<Guid, Guid>();
        var org = Org;
        return await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && e.CategoryId != null)
            .ToDictionaryAsync(e => e.Id, e => e.CategoryId!.Value, ct);
    }

    /// <summary>Creates a category. The slug (made from the name when blank) must be unique in the organization. Audited as exam.category.created.</summary>
    public async Task<AssessmentResult> CreateCategoryAsync(ExamCategoryForm form, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Creating an exam category", "ExamCategory", null, ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var name = (form.Name ?? "").Trim();
        if (name.Length == 0) return new(false, "Give the category a name.");
        if (name.Length > ExamCategory.MaxNameLength) return new(false, $"Keep the name to {ExamCategory.MaxNameLength} characters.");
        var slug = Slugify(string.IsNullOrWhiteSpace(form.Slug) ? name : form.Slug);
        if (slug.Length == 0) return new(false, "The name needs at least one letter or digit.");
        var org = Org;
        if (await db.ExamCategories.AnyAsync(c => c.OrganizationId == org && c.Slug == slug, ct))
            return new(false, $"A category with the slug \"{slug}\" already exists.");
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (lang, text) in form.DisplayNames ?? new())
        {
            if (string.IsNullOrWhiteSpace(lang) || string.IsNullOrWhiteSpace(text)) continue;
            var v = text.Trim();
            names[lang.Trim().Split('-')[0].ToLowerInvariant()] = v.Length > ExamCategory.MaxNameLength ? v[..ExamCategory.MaxNameLength] : v;
        }
        var category = new ExamCategory
        {
            OrganizationId = org, Name = name, Slug = slug, SortOrder = form.SortOrder,
            DisplayNamesJson = JsonSerializer.Serialize(names), CreatedByUserId = User, CreatedAt = clock.GetUtcNow(),
        };
        db.ExamCategories.Add(category);
        guard.Audit("exam.category.created", "ExamCategory", category.Id.ToString(), after: new { category.Name, category.Slug, category.SortOrder, DisplayNames = names });
        await SaveAsync(ct);
        return new(true, $"Created the category \"{name}\".", category.Id);
    }

    /// <summary>
    /// Files an exam under a category, or none (null). Allowed on published exams too: a category is how the exam is listed,
    /// not one of its frozen settings. Audited as exam.category.set with before and after.
    /// </summary>
    public async Task<AssessmentResult> SetCategoryAsync(Guid examId, Guid? categoryId, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Changing an exam's category", "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        if (exam.CategoryId == categoryId) return new(true, "No change.", exam.Id);
        string? categoryName = null;
        if (categoryId is { } id)
        {
            categoryName = await db.ExamCategories.AsNoTracking().Where(c => c.Id == id && c.OrganizationId == org).Select(c => c.Name).FirstOrDefaultAsync(ct);
            if (categoryName is null) return new(false, "That category no longer exists.");
        }
        var before = exam.CategoryId;
        exam.CategoryId = categoryId;
        guard.Audit("exam.category.set", "Exam", exam.Id.ToString(), new { CategoryId = before }, new { CategoryId = categoryId, Category = categoryName });
        await SaveAsync(ct);
        return new(true, categoryName is null ? "Removed the exam's category." : $"Filed under \"{categoryName}\".", exam.Id);
    }

    /// <summary>Reads a category's display-name JSON (language tag to name); an empty map when blank or malformed.</summary>
    public static IReadOnlyDictionary<string, string> ReadDisplayNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return map is null ? new Dictionary<string, string>() : new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { return new Dictionary<string, string>(); }
    }

    /// <summary>Lower-case letters and digits joined by single hyphens, at most <see cref="ExamCategory.MaxSlugLength"/> long.</summary>
    public static string Slugify(string? text)
    {
        var sb = new System.Text.StringBuilder();
        var pendingHyphen = false;
        foreach (var ch in (text ?? "").Normalize(System.Text.NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToLowerInvariant(ch);
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingHyphen && sb.Length > 0) sb.Append('-');
                pendingHyphen = false;
                sb.Append(c);
            }
            else pendingHyphen = true;
            if (sb.Length >= ExamCategory.MaxSlugLength) break;
        }
        return sb.Length > ExamCategory.MaxSlugLength ? sb.ToString(0, ExamCategory.MaxSlugLength).TrimEnd('-') : sb.ToString().TrimEnd('-');
    }

    // ================================================================== helpers

    private ExamService Exams() => new(db, tenant, directory, clock);

    private async Task<Dictionary<string, string>?> ConceptTitlesAsync(Guid examId, CancellationToken ct)
    {
        var org = Org;
        var collectionId = await db.Exams.AsNoTracking().Where(e => e.Id == examId && e.OrganizationId == org).Select(e => (Guid?)e.CollectionId).FirstOrDefaultAsync(ct);
        if (collectionId is null) return null;
        return (await Exams().ConceptsAsync(collectionId.Value, ct)).ToDictionary(c => c.Key, c => c.Title, StringComparer.Ordinal);
    }

    private async Task<string> CopyTitleAsync(string title, CancellationToken ct)
    {
        var org = Org;
        var basis = title.Length > 185 ? title[..185].TrimEnd() : title;
        var stem = $"{basis} (copy";
        var taken = (await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && e.Title.StartsWith(stem)).Select(e => e.Title).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = $"{basis} (copy)";
        for (var i = 2; taken.Contains(candidate); i++) candidate = $"{basis} (copy {i})";
        return candidate;
    }

    private Task<List<LearningObjective>> LoadObjectivesAsync(Guid examId, CancellationToken ct)
    {
        var org = Org;
        return db.LearningObjectives.AsNoTracking().Include(o => o.Criteria).ThenInclude(c => c.Levels)
            .Where(o => o.ExamId == examId && o.OrganizationId == org).OrderBy(o => o.Ordinal).AsSplitQuery().ToListAsync(ct);
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private async Task SaveAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
