using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Exams;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Assessments;

/// <summary>
/// Teacher assessment setup (GAD-T85) and grading policies (GAD-T82): courses, assessments, learning
/// objectives, rubric criteria with level descriptors, the grading policy, publishing (which locks the
/// policy and freezes the assessment) and approved policy changes. Every action checks a named
/// <see cref="Permission"/> (GAD-T83) and every state change writes an <see cref="AuditEvent"/> in the same
/// save. Publishing writes the exam's <see cref="SessionPolicy"/> so the existing session engine runs the
/// assessment unchanged. Everything is scoped to the caller's active organization; the DbContext's tenant
/// filter and write guard back that up.
/// </summary>
public sealed class AssessmentService(GadflyDbContext db, ITenantContext tenant, IOrganizationDirectory directory, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public const int MinDurationMinutes = ExamService.MinDurationMinutes;
    public const int MaxDurationMinutes = ExamService.MaxDurationMinutes;
    public const int MaxQuestionLimit = 100;
    public const decimal MaxObjectiveScore = 10000m;

    private readonly AssessmentGuard guard = new(db, tenant, clock);

    private Guid Org => tenant.OrganizationId ?? throw new InvalidOperationException("No active organization.");
    private Guid User => tenant.UserId ?? throw new InvalidOperationException("No signed-in user.");

    public AssessmentGuard Guard => guard;

    public AssessmentCapabilities Capabilities() => new(tenant.Role,
        guard.Has(Permission.ManageCourses), guard.Has(Permission.ManageAssessments),
        guard.Has(Permission.AssignExaminations), guard.Has(Permission.ApproveGradingPolicyChange));

    /// <summary>
    /// Called when someone opens the assessment area: returns what they may do, and audits a refusal when
    /// they may do nothing here (a student or examiner reaching the page). <paramref name="audit"/> is false for
    /// a prerender, so one visit is audited once.
    /// </summary>
    public async Task<AssessmentCapabilities> EnterAsync(string area, bool audit = true, CancellationToken ct = default)
    {
        var caps = Capabilities();
        if (!caps.Any && audit) await guard.RequireAsync(Permission.ManageAssessments, $"Opening {area}", "Exam", null, ct);
        return caps;
    }

    // ================================================================== courses

    public async Task<IReadOnlyList<CourseSummary>> CoursesAsync(bool includeArchived = true, CancellationToken ct = default)
    {
        if (!guard.Has(Permission.ManageCourses) && !guard.Has(Permission.ManageAssessments)) return [];
        var org = Org;
        var counts = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && e.CourseId != null)
            .GroupBy(e => e.CourseId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var courses = await db.Courses.AsNoTracking().Where(c => c.OrganizationId == org && (includeArchived || !c.IsArchived))
            .OrderBy(c => c.IsArchived).ThenBy(c => c.Title).ToListAsync(ct);
        return courses.Select(c => new CourseSummary(c.Id, c.Title, c.Subject, c.Description, c.IsArchived, counts.GetValueOrDefault(c.Id), c.CreatedAt)).ToList();
    }

    public async Task<AssessmentResult> SaveCourseAsync(CourseDraft draft, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageCourses, draft.Id is null ? "Creating a course" : "Editing a course", "Course", draft.Id?.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageCourses));
        var title = draft.Title?.Trim() ?? "";
        var subject = draft.Subject?.Trim() ?? "";
        var description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim();
        if (title.Length == 0) return new(false, "Give the course a title.");
        if (title.Length > 300) return new(false, "Keep the course title under 300 characters.");
        if (subject.Length == 0) return new(false, "Give the course a subject (for example Biology).");
        if (subject.Length > 200) return new(false, "Keep the subject under 200 characters.");
        if (description?.Length > 4000) return new(false, "Keep the description under 4000 characters.");

        var org = Org;
        if (await db.Courses.AnyAsync(c => c.OrganizationId == org && c.Title == title && c.Id != (draft.Id ?? Guid.Empty), ct))
            return new(false, $"A course called \"{title}\" already exists.");

        if (draft.Id is { } id)
        {
            var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == org, ct);
            if (course is null) return new(false, "That course no longer exists.");
            var before = new { course.Title, course.Subject, course.Description };
            course.Title = title;
            course.Subject = subject;
            course.Description = description;
            guard.Audit("course.updated", "Course", course.Id.ToString(), before, new { course.Title, course.Subject, course.Description });
            await SaveAsync(ct);
            return new(true, "Course saved.", course.Id);
        }

        var created = new Course
        {
            OrganizationId = org, Title = title, Subject = subject, Description = description, CreatedByUserId = User, CreatedAt = clock.GetUtcNow(),
        };
        db.Courses.Add(created);
        guard.Audit("course.created", "Course", created.Id.ToString(), after: new { created.Title, created.Subject, created.Description });
        await SaveAsync(ct);
        return new(true, "Course created.", created.Id);
    }

    /// <summary>Archives (or restores) a course: it stays with its assessments and results, but new assessments can't use it.</summary>
    public async Task<AssessmentResult> SetCourseArchivedAsync(Guid courseId, bool archived, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageCourses, archived ? "Archiving a course" : "Restoring a course", "Course", courseId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageCourses));
        var org = Org;
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == courseId && c.OrganizationId == org, ct);
        if (course is null) return new(false, "That course no longer exists.");
        if (course.IsArchived == archived) return new(true, archived ? "Already archived." : "Already active.", course.Id);
        course.IsArchived = archived;
        guard.Audit(archived ? "course.archived" : "course.restored", "Course", course.Id.ToString(),
            new { IsArchived = !archived }, new { IsArchived = archived });
        await SaveAsync(ct);
        return new(true, archived ? "Course archived. Its assessments and results are kept." : "Course restored.", course.Id);
    }

    // ================================================================== assessments: reading

    public async Task<IReadOnlyList<AssessmentSummary>> ListAsync(Guid? courseId = null, CancellationToken ct = default)
    {
        if (!guard.Has(Permission.ManageAssessments)) return [];
        var org = Org;
        var q = db.Exams.AsNoTracking().Where(e => e.OrganizationId == org);
        if (courseId is { } cid) q = q.Where(e => e.CourseId == cid);
        var exams = await q.OrderBy(e => e.Status).ThenByDescending(e => e.CreatedAt).ToListAsync(ct);
        var courses = await db.Courses.AsNoTracking().Where(c => c.OrganizationId == org).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var objectives = await db.LearningObjectives.AsNoTracking().Where(o => o.OrganizationId == org)
            .GroupBy(o => o.ExamId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var assigned = await db.ExamAssignments.AsNoTracking().Where(a => a.OrganizationId == org)
            .GroupBy(a => a.ExamId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var locked = (await db.GradingPolicies.AsNoTracking().Where(p => p.OrganizationId == org && p.LockedAt != null)
            .Select(p => p.ExamId).ToListAsync(ct)).ToHashSet();
        var pending = await db.GradingPolicyChanges.AsNoTracking().Where(c => c.OrganizationId == org && c.ApprovedAt == null)
            .GroupBy(c => c.ExamId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return exams.Select(e => new AssessmentSummary(e.Id, e.Title, e.CourseId, e.CourseId is { } c ? courses.GetValueOrDefault(c) : null,
            e.Subject, e.Topic, e.AssessmentMode, e.Status, objectives.GetValueOrDefault(e.Id), assigned.GetValueOrDefault(e.Id),
            locked.Contains(e.Id) || e.Status != ExamStatus.Draft, pending.GetValueOrDefault(e.Id), e.CreatedAt)).ToList();
    }

    /// <summary>Source collections whose concept map is ready.</summary>
    public Task<IReadOnlyList<CollectionOption>> CollectionsAsync(CancellationToken ct = default) =>
        guard.Has(Permission.ManageAssessments) ? Exams().ReadyCollectionsAsync(ct) : Task.FromResult<IReadOnlyList<CollectionOption>>([]);

    public async Task<AssessmentView?> GetAsync(Guid examId, CancellationToken ct = default)
    {
        if (!guard.Has(Permission.ManageAssessments) && !guard.Has(Permission.ApproveGradingPolicyChange)) return null;
        var org = Org;
        var e = await db.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == examId && x.OrganizationId == org, ct);
        if (e is null) return null;
        var p = ExamService.ReadPolicy(e.PolicyJson);
        var details = new AssessmentDetails
        {
            Id = e.Id, CourseId = e.CourseId, CollectionId = e.CollectionId, Title = e.Title, Subject = e.Subject, Topic = e.Topic,
            Mode = e.AssessmentMode, DurationMinutes = (int)Math.Round(p.TimeBudget.TotalMinutes), QuestionLimit = p.MaxTurns,
            Difficulty = p.Difficulty, Instructions = e.Instructions,
        };
        var courseTitle = e.CourseId is { } cid ? await db.Courses.AsNoTracking().Where(c => c.Id == cid && c.OrganizationId == org).Select(c => c.Title).FirstOrDefaultAsync(ct) : null;
        var collectionTitle = await db.SourceCollections.AsNoTracking().Where(c => c.Id == e.CollectionId && c.OrganizationId == org).Select(c => c.Title).FirstOrDefaultAsync(ct);
        var concepts = (await Exams().ConceptsAsync(e.CollectionId, ct)).ToDictionary(c => c.Key, c => c.Title, StringComparer.Ordinal);

        var objectives = await LoadObjectivesAsync(examId, ct);
        var objectiveViews = objectives.Select(o => new ObjectiveView(o.Id, o.Key, o.Title, o.Description, o.ConceptKey,
            o.ConceptKey is { } k ? concepts.GetValueOrDefault(k) : null, o.ConceptKey is { } k2 && !concepts.ContainsKey(k2),
            o.Weight, o.MaxScore, o.Ordinal,
            o.Criteria.OrderBy(c => c.Ordinal).Select(c => new CriterionView(c.Id, c.Dimension, c.Name, c.Description, c.Weight, c.Ordinal, LevelTexts(c))).ToList()))
            .ToList();

        var policy = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.ExamId == examId && x.OrganizationId == org, ct);
        var names = await NamesAsync(ct);
        var changes = (await db.GradingPolicyChanges.AsNoTracking().Where(c => c.ExamId == examId && c.OrganizationId == org)
                .OrderByDescending(c => c.RequestedAt).ToListAsync(ct))
            .Select(c => new PolicyChangeView(c.Id, ReadSnapshot(c.BeforeJson), ReadSnapshot(c.AfterJson), c.Reason, c.RequestedByUserId,
                names.GetValueOrDefault(c.RequestedByUserId, "(unknown user)"), c.RequestedAt, c.ApprovedByUserId,
                c.ApprovedByUserId is { } a ? names.GetValueOrDefault(a, "(unknown user)") : null, c.ApprovedAt))
            .ToList();
        var anyCompleted = await AnySessionCompletedAsync(examId, ct);
        return new AssessmentView(details, e.Status, e.PublishedAt, courseTitle, collectionTitle, objectiveViews,
            policy is null ? PolicySnapshot.Default : PolicySnapshot.Of(policy), policy is not null,
            policy?.LockedAt ?? (e.Status != ExamStatus.Draft ? e.PublishedAt : null),
            policy?.LockedByUserId is { } lb ? names.GetValueOrDefault(lb, "(unknown user)") : null,
            changes, anyCompleted);
    }

    /// <summary>Concepts of the assessment's source collection that no objective uses yet: suggested objectives.</summary>
    public async Task<IReadOnlyList<ConceptSuggestion>> SuggestionsAsync(Guid examId, CancellationToken ct = default)
    {
        if (!guard.Has(Permission.ManageAssessments)) return [];
        var org = Org;
        var e = await db.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == examId && x.OrganizationId == org, ct);
        if (e is null) return [];
        var used = (await db.LearningObjectives.AsNoTracking().Where(o => o.ExamId == examId && o.OrganizationId == org && o.ConceptKey != null)
            .Select(o => o.ConceptKey!).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        return (await Exams().ConceptsAsync(e.CollectionId, ct)).Where(c => !used.Contains(c.Key))
            .Select(c => new ConceptSuggestion(c.Key, c.Title, c.Definition)).ToList();
    }

    /// <summary>Every concept of the assessment's source collection (for linking an objective).</summary>
    public async Task<IReadOnlyList<ConceptSuggestion>> ConceptsAsync(Guid examId, CancellationToken ct = default)
    {
        if (!guard.Has(Permission.ManageAssessments)) return [];
        var org = Org;
        var e = await db.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == examId && x.OrganizationId == org, ct);
        return e is null ? [] : (await Exams().ConceptsAsync(e.CollectionId, ct)).Select(c => new ConceptSuggestion(c.Key, c.Title, c.Definition)).ToList();
    }

    // ================================================================== assessments: details

    /// <summary>Creates an assessment (a draft Exam with a default grading policy) or edits a draft's details.</summary>
    public async Task<AssessmentResult> SaveDetailsAsync(AssessmentDetails d, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, d.Id is null ? "Creating an assessment" : "Editing an assessment", "Exam", d.Id?.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var title = d.Title?.Trim() ?? "";
        var topic = d.Topic?.Trim() ?? "";
        var subject = d.Subject?.Trim() ?? "";
        if (title.Length == 0) return new(false, "Give the assessment a title.");
        if (title.Length > 200) return new(false, "Keep the title under 200 characters.");
        if (topic.Length > 200 || subject.Length > 200) return new(false, "Keep the subject and topic under 200 characters each.");
        if (d.DurationMinutes < MinDurationMinutes || d.DurationMinutes > MaxDurationMinutes)
            return new(false, $"The duration must be {MinDurationMinutes} to {MaxDurationMinutes} minutes.");
        if (d.QuestionLimit is < 1 or > MaxQuestionLimit) return new(false, $"The question limit must be 1 to {MaxQuestionLimit}.");
        if (d.Difficulty is < 1 or > 5) return new(false, "Difficulty must be 1 to 5.");
        if (!Enum.IsDefined(d.Mode)) return new(false, "Choose Examination or Learning mode.");
        if (d.Instructions?.Length > 4000) return new(false, "Keep the instructions under 4000 characters.");
        if (d.CourseId is not { } courseId) return new(false, "Choose the course this assessment belongs to.");
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId && c.OrganizationId == org, ct);
        if (course is null) return new(false, "That course no longer exists.");
        if (!await db.SourceCollections.AnyAsync(c => c.Id == d.CollectionId && c.OrganizationId == org, ct))
            return new(false, "Choose a source collection.");
        if (subject.Length == 0) subject = course.Subject;

        Exam exam;
        object? before = null;
        if (d.Id is { } id)
        {
            exam = (await db.Exams.FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == org, ct))!;
            if (exam is null) return new(false, "That assessment no longer exists.");
            if (exam.Status != ExamStatus.Draft) return new(false, Frozen(exam.Status));
            if (course.IsArchived && exam.CourseId != course.Id) return new(false, "That course is archived; choose an active one.");
            if (exam.CollectionId != d.CollectionId && await db.LearningObjectives.AnyAsync(o => o.ExamId == id && o.OrganizationId == org && o.ConceptKey != null, ct))
                return new(false, "Objectives are linked to concepts of the current source collection. Remove those links (or the objectives) before changing the collection.");
            before = DetailsAudit(exam);
        }
        else
        {
            if (course.IsArchived) return new(false, "That course is archived; choose an active one.");
            exam = new Exam
            {
                OrganizationId = org, Status = ExamStatus.Draft, CreatedByUserId = User, CreatedAt = clock.GetUtcNow(), Mode = ExamMode.Text,
                PolicyJson = JsonSerializer.Serialize(new SessionPolicy { Seed = Random.Shared.Next() }, Json),
            };
            db.Exams.Add(exam);
            db.GradingPolicies.Add(new GradingPolicy { ExamId = exam.Id, OrganizationId = org });
        }

        exam.CourseId = course.Id;
        exam.CollectionId = d.CollectionId;
        exam.Title = title;
        exam.Subject = subject;
        exam.Topic = topic;
        exam.AssessmentMode = d.Mode;
        exam.Instructions = string.IsNullOrWhiteSpace(d.Instructions) ? null : d.Instructions.Trim();
        exam.PromptSetId = ExamPrompts.SetId;
        exam.PromptSetVersion = ExamPrompts.Version;
        var sp = ExamService.ReadPolicy(exam.PolicyJson) with
        {
            TimeBudget = TimeSpan.FromMinutes(d.DurationMinutes),
            MaxTurns = d.QuestionLimit,
            Difficulty = d.Difficulty,
        };
        exam.PolicyJson = JsonSerializer.Serialize(sp, Json);
        guard.Audit(before is null ? "assessment.created" : "assessment.updated", "Exam", exam.Id.ToString(), before, DetailsAudit(exam));
        await SaveAsync(ct);
        return new(true, before is null ? "Assessment created. Next: its learning objectives." : "Details saved.", exam.Id);
    }

    // ================================================================== objectives

    public async Task<AssessmentResult> SaveObjectiveAsync(ObjectiveDraft d, CancellationToken ct = default)
    {
        var (exam, refusal) = await EditableExamAsync(d.ExamId, d.Id is null ? "Adding a learning objective" : "Editing a learning objective", ct);
        if (exam is null) return refusal!;
        var org = Org;
        var title = d.Title?.Trim() ?? "";
        var description = string.IsNullOrWhiteSpace(d.Description) ? null : d.Description.Trim();
        var conceptKey = string.IsNullOrWhiteSpace(d.ConceptKey) ? null : d.ConceptKey.Trim();
        if (title.Length == 0) return new(false, "Give the objective a title.");
        if (title.Length > 300) return new(false, "Keep the objective title under 300 characters.");
        if (description?.Length > 4000) return new(false, "Keep the description under 4000 characters.");
        if (d.Weight < 0 || d.Weight > 1000) return new(false, "The weight must be 0 to 1000.");
        if (d.MaxScore <= 0 || d.MaxScore > MaxObjectiveScore) return new(false, $"The maximum score must be above 0 and at most {MaxObjectiveScore}.");
        if (conceptKey is not null)
        {
            var keys = (await Exams().ConceptsAsync(exam.CollectionId, ct)).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
            if (!keys.Contains(conceptKey)) return new(false, "That concept is not in the source collection's concept map.");
        }

        LearningObjective o;
        object? before = null;
        if (d.Id is { } id)
        {
            o = (await db.LearningObjectives.FirstOrDefaultAsync(x => x.Id == id && x.ExamId == exam.Id && x.OrganizationId == org, ct))!;
            if (o is null) return new(false, "That objective no longer exists.");
            before = ObjectiveAudit(o);
        }
        else
        {
            var existing = await db.LearningObjectives.AsNoTracking().Where(x => x.ExamId == exam.Id && x.OrganizationId == org)
                .Select(x => new { x.Key, x.Ordinal }).ToListAsync(ct);
            o = new LearningObjective
            {
                OrganizationId = org, ExamId = exam.Id, Key = NextKey(existing.Select(x => x.Key)),
                Ordinal = existing.Count == 0 ? 0 : existing.Max(x => x.Ordinal) + 1,
            };
            db.LearningObjectives.Add(o);
        }
        o.Title = title;
        o.Description = description;
        o.ConceptKey = conceptKey;
        o.Weight = d.Weight;
        o.MaxScore = d.MaxScore;
        guard.Audit(before is null ? "objective.created" : "objective.updated", "LearningObjective", o.Id.ToString(), before, ObjectiveAudit(o));
        await SaveAsync(ct);
        return new(true, before is null ? "Objective added. Give it at least one rubric criterion." : "Objective saved.", o.Id);
    }

    /// <summary>
    /// Adds the chosen concepts as objectives (title and description from the concept map), each with one
    /// default criterion (Conceptual Understanding, default level descriptors) for the teacher to refine.
    /// </summary>
    public async Task<AssessmentResult> AddSuggestedAsync(Guid examId, IReadOnlyCollection<string> conceptKeys, CancellationToken ct = default)
    {
        var (exam, refusal) = await EditableExamAsync(examId, "Adding suggested objectives", ct);
        if (exam is null) return refusal!;
        var org = Org;
        var concepts = (await Exams().ConceptsAsync(exam.CollectionId, ct)).ToDictionary(c => c.Key, StringComparer.Ordinal);
        var existing = await db.LearningObjectives.AsNoTracking().Where(x => x.ExamId == exam.Id && x.OrganizationId == org).ToListAsync(ct);
        var used = existing.Where(x => x.ConceptKey != null).Select(x => x.ConceptKey!).ToHashSet(StringComparer.Ordinal);
        var keys = existing.Select(x => x.Key).ToList();
        var ordinal = existing.Count == 0 ? 0 : existing.Max(x => x.Ordinal) + 1;
        var added = 0;
        foreach (var key in conceptKeys.Distinct(StringComparer.Ordinal))
        {
            if (!concepts.TryGetValue(key, out var c) || used.Contains(key)) continue;
            var o = new LearningObjective
            {
                OrganizationId = org, ExamId = exam.Id, Key = NextKey(keys), Title = Cap(c.Title, 300),
                Description = string.IsNullOrWhiteSpace(c.Definition) ? null : Cap(c.Definition, 4000), ConceptKey = key, Ordinal = ordinal++,
            };
            keys.Add(o.Key);
            db.LearningObjectives.Add(o);
            AddCriterion(o.Id, new CriterionDraft { Name = "Conceptual understanding", Dimension = AssessmentDimension.ConceptualUnderstanding }, 0);
            guard.Audit("objective.created", "LearningObjective", o.Id.ToString(), after: ObjectiveAudit(o), reason: $"Suggested from concept {key}");
            added++;
        }
        if (added == 0) return new(false, "Choose at least one concept that isn't already an objective.");
        await SaveAsync(ct);
        return new(true, $"Added {added} objective{(added == 1 ? "" : "s")} from the source material, each with a starting criterion.", exam.Id);
    }

    public async Task<AssessmentResult> DeleteObjectiveAsync(Guid objectiveId, CancellationToken ct = default)
    {
        var org = Org;
        var o = await db.LearningObjectives.Include(x => x.Criteria).ThenInclude(c => c.Levels)
            .FirstOrDefaultAsync(x => x.Id == objectiveId && x.OrganizationId == org, ct);
        if (o is null) return new(false, "That objective no longer exists.");
        var (exam, refusal) = await EditableExamAsync(o.ExamId, "Removing a learning objective", ct);
        if (exam is null) return refusal!;
        foreach (var c in o.Criteria)
        {
            db.RubricLevelDescriptors.RemoveRange(c.Levels);
            db.RubricCriteria.Remove(c);
        }
        db.LearningObjectives.Remove(o);
        guard.Audit("objective.deleted", "LearningObjective", o.Id.ToString(), ObjectiveAudit(o));
        await SaveAsync(ct);
        return new(true, "Objective removed.", exam.Id);
    }

    /// <summary>Moves an objective one place up (-1) or down (+1).</summary>
    public async Task<AssessmentResult> MoveObjectiveAsync(Guid objectiveId, int direction, CancellationToken ct = default)
    {
        var org = Org;
        var o = await db.LearningObjectives.AsNoTracking().FirstOrDefaultAsync(x => x.Id == objectiveId && x.OrganizationId == org, ct);
        if (o is null) return new(false, "That objective no longer exists.");
        var (exam, refusal) = await EditableExamAsync(o.ExamId, "Reordering learning objectives", ct);
        if (exam is null) return refusal!;
        var list = await db.LearningObjectives.Where(x => x.ExamId == exam.Id && x.OrganizationId == org).OrderBy(x => x.Ordinal).ToListAsync(ct);
        var i = list.FindIndex(x => x.Id == objectiveId);
        var j = i + Math.Sign(direction);
        if (j < 0 || j >= list.Count) return new(true, "Already there.", exam.Id);
        (list[i], list[j]) = (list[j], list[i]);
        for (var k = 0; k < list.Count; k++) list[k].Ordinal = k;
        guard.Audit("objective.reordered", "Exam", exam.Id.ToString(), after: list.Select(x => x.Key).ToList());
        await SaveAsync(ct);
        return new(true, "Order saved.", exam.Id);
    }

    // ================================================================== rubric criteria

    public async Task<AssessmentResult> SaveCriterionAsync(CriterionDraft d, CancellationToken ct = default)
    {
        var org = Org;
        var o = await db.LearningObjectives.AsNoTracking().FirstOrDefaultAsync(x => x.Id == d.ObjectiveId && x.OrganizationId == org, ct);
        if (o is null) return new(false, "That objective no longer exists.");
        var (exam, refusal) = await EditableExamAsync(o.ExamId, d.Id is null ? "Adding a rubric criterion" : "Editing a rubric criterion", ct);
        if (exam is null) return refusal!;
        var name = d.Name?.Trim() ?? "";
        if (name.Length == 0) return new(false, "Give the criterion a name.");
        if (name.Length > 200) return new(false, "Keep the criterion name under 200 characters.");
        if (!Enum.IsDefined(d.Dimension)) return new(false, "Choose a dimension.");
        if (d.Weight <= 0 || d.Weight > 1000) return new(false, "The criterion weight must be above 0 and at most 1000.");
        if (d.Description?.Length > 4000) return new(false, "Keep the description under 4000 characters.");
        if (d.Levels.Length != 6) return new(false, "A criterion describes all six levels (0 to 5).");
        if (d.Levels.Any(l => l?.Length > 2000)) return new(false, "Keep each level descriptor under 2000 characters.");

        if (d.Id is { } id)
        {
            var c = await db.RubricCriteria.Include(x => x.Levels).FirstOrDefaultAsync(x => x.Id == id && x.ObjectiveId == o.Id && x.OrganizationId == org, ct);
            if (c is null) return new(false, "That criterion no longer exists.");
            var before = CriterionAudit(c);
            c.Name = name;
            c.Dimension = d.Dimension;
            c.Description = string.IsNullOrWhiteSpace(d.Description) ? null : d.Description.Trim();
            c.Weight = d.Weight;
            foreach (var level in Enum.GetValues<MasteryLevel>())
            {
                var text = Descriptor(d.Levels[(int)level], level);
                var row = c.Levels.FirstOrDefault(l => l.Level == level);
                if (row is null) db.RubricLevelDescriptors.Add(new RubricLevelDescriptor { OrganizationId = org, CriterionId = c.Id, Level = level, Descriptor = text });
                else row.Descriptor = text;
            }
            guard.Audit("rubric.criterion.updated", "RubricCriterion", c.Id.ToString(), before, CriterionAudit(c));
            await SaveAsync(ct);
            return new(true, "Criterion saved.", c.Id);
        }

        var ordinal = await db.RubricCriteria.Where(x => x.ObjectiveId == o.Id && x.OrganizationId == org).Select(x => (int?)x.Ordinal).MaxAsync(ct) ?? -1;
        var created = AddCriterion(o.Id, d, ordinal + 1, name);
        guard.Audit("rubric.criterion.created", "RubricCriterion", created.Id.ToString(), after: CriterionAudit(created));
        await SaveAsync(ct);
        return new(true, "Criterion added.", created.Id);
    }

    public async Task<AssessmentResult> DeleteCriterionAsync(Guid criterionId, CancellationToken ct = default)
    {
        var org = Org;
        var c = await db.RubricCriteria.Include(x => x.Levels).FirstOrDefaultAsync(x => x.Id == criterionId && x.OrganizationId == org, ct);
        if (c is null) return new(false, "That criterion no longer exists.");
        var examId = await db.LearningObjectives.Where(o => o.Id == c.ObjectiveId && o.OrganizationId == org).Select(o => o.ExamId).FirstOrDefaultAsync(ct);
        var (exam, refusal) = await EditableExamAsync(examId, "Removing a rubric criterion", ct);
        if (exam is null) return refusal!;
        db.RubricLevelDescriptors.RemoveRange(c.Levels);
        db.RubricCriteria.Remove(c);
        guard.Audit("rubric.criterion.deleted", "RubricCriterion", c.Id.ToString(), CriterionAudit(c));
        await SaveAsync(ct);
        return new(true, "Criterion removed.", exam.Id);
    }

    // ================================================================== grading policy (GAD-T82)

    /// <summary>Validates a policy the teacher entered; null when usable, else the reason.</summary>
    public static string? ValidatePolicy(PolicySnapshot p)
    {
        if (!Enum.IsDefined(p.InconclusiveProcedure)) return "Choose what happens when the evidence is inconclusive.";
        if (p.AdditionalReviewAt > 100 || p.MandatoryReconciliationAt > 100) return "Thresholds are percentage points of an objective's maximum: at most 100.";
        try
        {
            p.ToRules().Validate();
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Saves a draft assessment's policy. A locked (published) policy only changes through an approved request.</summary>
    public async Task<AssessmentResult> SavePolicyAsync(Guid examId, PolicyDraft draft, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Editing a grading policy", "GradingPolicy", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That assessment no longer exists.");
        var policy = await db.GradingPolicies.FirstOrDefaultAsync(p => p.ExamId == examId && p.OrganizationId == org, ct);
        if (exam.Status != ExamStatus.Draft || policy?.LockedAt is not null)
            return new(false, "This grading policy is locked because the assessment is published. Request a change below; an institution administrator must approve it.");
        var after = draft.ToSnapshot();
        if (ValidatePolicy(after) is { } problem) return new(false, problem);
        object? before = null;
        if (policy is null)
        {
            policy = new GradingPolicy { ExamId = examId, OrganizationId = org };
            db.GradingPolicies.Add(policy);
        }
        else
        {
            before = PolicySnapshot.Of(policy);
        }
        after.ApplyTo(policy);
        guard.Audit("policy.updated", "GradingPolicy", examId.ToString(), before, after);
        await SaveAsync(ct);
        return new(true, "Grading policy saved. It locks when you publish.", examId);
    }

    /// <summary>Requests a change to a locked policy. It applies only once someone with ApproveGradingPolicyChange approves it.</summary>
    public async Task<AssessmentResult> RequestPolicyChangeAsync(Guid examId, PolicyDraft draft, string reason, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Requesting a grading-policy change", "GradingPolicy", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That assessment no longer exists.");
        var policy = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == examId && p.OrganizationId == org, ct);
        if (exam.Status == ExamStatus.Draft && policy?.LockedAt is null)
            return new(false, "This assessment isn't published, so its policy isn't locked: edit it directly.");
        reason = reason?.Trim() ?? "";
        if (reason.Length < 10) return new(false, "Explain why the policy must change (at least a sentence); the approver and the audit log see it.");
        if (reason.Length > 1000) return new(false, "Keep the reason under 1000 characters.");
        var current = policy is null ? PolicySnapshot.Default : PolicySnapshot.Of(policy);
        var after = draft.ToSnapshot();
        if (ValidatePolicy(after) is { } problem) return new(false, problem);
        if (after == current) return new(false, "That is the current policy; nothing would change.");
        if (await db.GradingPolicyChanges.AnyAsync(c => c.ExamId == examId && c.OrganizationId == org && c.ApprovedAt == null, ct))
            return new(false, "A change to this policy is already waiting for approval. It must be approved or rejected first.");
        var change = new GradingPolicyChange
        {
            OrganizationId = org, ExamId = examId, BeforeJson = AssessmentGuard.ToJson(current), AfterJson = AssessmentGuard.ToJson(after),
            Reason = reason, RequestedByUserId = User, RequestedAt = clock.GetUtcNow(),
        };
        db.GradingPolicyChanges.Add(change);
        guard.Audit("policy.change.requested", "GradingPolicyChange", change.Id.ToString(), current, after, reason);
        await SaveAsync(ct);
        return new(true, "Change requested. It applies once an institution administrator approves it.", change.Id);
    }

    /// <summary>
    /// Approves a pending policy change and applies it (both audited). Refused when the policy changed since
    /// the request, and, once any session of the assessment has completed, when the approver is the requester
    /// (grades already rest on the policy, so a second person must agree).
    /// </summary>
    public async Task<AssessmentResult> ApprovePolicyChangeAsync(Guid changeId, string? note, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ApproveGradingPolicyChange, "Approving a grading-policy change", "GradingPolicyChange", changeId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ApproveGradingPolicyChange));
        var org = Org;
        var change = await db.GradingPolicyChanges.FirstOrDefaultAsync(c => c.Id == changeId && c.OrganizationId == org, ct);
        if (change is null) return new(false, "That change request no longer exists.");
        if (change.ApprovedAt is not null) return new(true, "Already approved.", change.Id);
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == change.ExamId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That assessment no longer exists.");
        var policy = await db.GradingPolicies.FirstOrDefaultAsync(p => p.ExamId == change.ExamId && p.OrganizationId == org, ct);
        var current = policy is null ? PolicySnapshot.Default : PolicySnapshot.Of(policy);
        if (ReadSnapshot(change.BeforeJson) != current)
            return new(false, "The policy changed after this request was made. Reject it and request the change again against the current policy.");
        var after = ReadSnapshot(change.AfterJson);
        if (ValidatePolicy(after) is { } problem) return new(false, $"The requested policy is not valid: {problem}");
        var completed = await AnySessionCompletedAsync(change.ExamId, ct);
        if (completed && change.RequestedByUserId == User)
            return new(false, "Students have already completed this assessment, so a policy change needs a second person: someone other than the requester must approve it.");
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > 1000) return new(false, "Keep the note under 1000 characters.");

        var now = clock.GetUtcNow();
        if (policy is null)
        {
            // Published before grading policies existed: the default applied implicitly; materialize it, locked.
            policy = new GradingPolicy { ExamId = change.ExamId, OrganizationId = org, LockedAt = exam.PublishedAt ?? now, LockedByUserId = exam.CreatedByUserId };
            db.GradingPolicies.Add(policy);
        }
        after.ApplyTo(policy);
        change.ApprovedByUserId = User;
        change.ApprovedAt = now;
        guard.Audit("policy.change.approved", "GradingPolicyChange", change.Id.ToString(), current, after,
            note ?? (completed ? "Approved after sessions had completed." : null));
        guard.Audit("policy.updated", "GradingPolicy", change.ExamId.ToString(), current, after, $"Applied approved change {change.Id}");
        await SaveAsync(ct);
        return new(true, "Change approved and applied." + (completed ? " Grades already finalized keep the policy they were computed under." : ""), change.Id);
    }

    /// <summary>
    /// Rejects (approver) or withdraws (the requester) a pending change. The request row is removed; the audit
    /// log keeps the full request and who rejected it and why.
    /// </summary>
    public async Task<AssessmentResult> RejectPolicyChangeAsync(Guid changeId, string? reason, CancellationToken ct = default)
    {
        var org = Org;
        var change = await db.GradingPolicyChanges.FirstOrDefaultAsync(c => c.Id == changeId && c.OrganizationId == org, ct);
        // The requester withdraws (no reason needed); anyone else, or a requester giving a reason with approval rights, rejects.
        var withdrawing = change is not null && change.RequestedByUserId == tenant.UserId && guard.Has(Permission.ManageAssessments)
                          && (string.IsNullOrWhiteSpace(reason) || !guard.Has(Permission.ApproveGradingPolicyChange));
        if (!withdrawing && !await guard.RequireAsync(Permission.ApproveGradingPolicyChange, "Rejecting a grading-policy change", "GradingPolicyChange", changeId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ApproveGradingPolicyChange));
        if (change is null) return new(false, "That change request no longer exists.");
        if (change.ApprovedAt is not null) return new(false, "That change was already approved and applied.");
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (!withdrawing && (reason is null || reason.Length < 5)) return new(false, "Say why the change is rejected; the requester and the audit log see it.");
        db.GradingPolicyChanges.Remove(change);
        guard.Audit(withdrawing ? "policy.change.withdrawn" : "policy.change.rejected", "GradingPolicyChange", change.Id.ToString(),
            change.BeforeJson, change.AfterJson, $"{reason ?? "Withdrawn by the requester."} (requested {change.RequestedAt:u} by {change.RequestedByUserId}: {change.Reason})");
        await SaveAsync(ct);
        return new(true, withdrawing ? "Request withdrawn." : "Change rejected. The policy stays as it is.", change.ExamId);
    }

    // ================================================================== publishing

    /// <summary>Everything that blocks publishing, in the order the teacher should fix it. Empty means ready.</summary>
    public async Task<IReadOnlyList<string>> PublishProblemsAsync(Guid examId, CancellationToken ct = default)
    {
        var org = Org;
        var problems = new List<string>();
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return ["That assessment no longer exists."];
        if (exam.Status != ExamStatus.Draft) problems.Add($"It is already {exam.Status.ToString().ToLowerInvariant()}.");
        if (exam.CourseId is not { } courseId || !await db.Courses.AnyAsync(c => c.Id == courseId && c.OrganizationId == org && !c.IsArchived, ct))
            problems.Add("Details: choose an active course.");
        if (string.IsNullOrWhiteSpace(exam.Title)) problems.Add("Details: give it a title.");
        var collectionReady = await db.SourceCollections.AnyAsync(c => c.Id == exam.CollectionId && c.OrganizationId == org
                                                                       && c.Status == ProcessingStatus.Ready && c.ConceptMapVersion > 0, ct);
        if (!collectionReady) problems.Add("Details: the source collection's concept map is not ready.");

        var objectives = await LoadObjectivesAsync(examId, ct);
        if (objectives.Count == 0) problems.Add("Objectives: add at least one learning objective.");
        var concepts = collectionReady ? (await Exams().ConceptsAsync(exam.CollectionId, ct)).Select(c => c.Key).ToHashSet(StringComparer.Ordinal) : [];
        foreach (var o in objectives)
        {
            if (o.MaxScore <= 0) problems.Add($"Objectives: \"{o.Title}\" needs a maximum score above 0.");
            if (o.Weight < 0) problems.Add($"Objectives: \"{o.Title}\" has a negative weight.");
            if (o.ConceptKey is null)
                problems.Add($"Objectives: link \"{o.Title}\" to a concept of the source material; the examiner only questions what the material covers.");
            else if (collectionReady && !concepts.Contains(o.ConceptKey))
                problems.Add($"Objectives: \"{o.Title}\" is linked to concept {o.ConceptKey}, which is no longer in the concept map.");
            if (o.Criteria.Count == 0) problems.Add($"Rubric: \"{o.Title}\" needs at least one criterion.");
            foreach (var c in o.Criteria)
            {
                if (c.Weight <= 0) problems.Add($"Rubric: \"{o.Title}\" / \"{c.Name}\" needs a weight above 0.");
                var levels = c.Levels.Where(l => !string.IsNullOrWhiteSpace(l.Descriptor)).Select(l => l.Level).ToHashSet();
                if (Enum.GetValues<MasteryLevel>().Any(l => !levels.Contains(l)))
                    problems.Add($"Rubric: \"{o.Title}\" / \"{c.Name}\" must describe every level 0 to 5.");
            }
        }
        if (objectives.Count > 0 && objectives.Sum(o => o.Weight) <= 0) problems.Add("Objectives: the objective weights must not all be 0.");

        var policy = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == examId && p.OrganizationId == org, ct);
        if (policy is null) problems.Add("Policy: save a grading policy.");
        else if (ValidatePolicy(PolicySnapshot.Of(policy)) is { } policyProblem) problems.Add($"Policy: {policyProblem}");

        try
        {
            BuildSessionPolicy(exam, objectives).Validate();
        }
        catch (ArgumentException ex)
        {
            problems.Add($"Details: {ex.Message}");
        }
        return problems;
    }

    /// <summary>
    /// Publishes a draft: validates everything, writes the SessionPolicy the session engine reads (concepts
    /// from the objectives, question limit as MaxTurns), freezes the assessment, locks the grading policy and
    /// audits it all in one save.
    /// </summary>
    public async Task<AssessmentResult> PublishAsync(Guid examId, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Publishing an assessment", "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var problems = await PublishProblemsAsync(examId, ct);
        if (problems.Count > 0) return new(false, "Not ready to publish: " + string.Join(" ", problems));
        var org = Org;
        var exam = await db.Exams.FirstAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        var objectives = await LoadObjectivesAsync(examId, ct);
        var policy = await db.GradingPolicies.FirstAsync(p => p.ExamId == examId && p.OrganizationId == org, ct);
        var now = clock.GetUtcNow();
        var session = BuildSessionPolicy(exam, objectives);
        exam.PolicyJson = JsonSerializer.Serialize(session, Json);
        exam.Status = ExamStatus.Published;
        exam.PublishedAt = now;
        exam.PromptSetId = ExamPrompts.SetId;
        exam.PromptSetVersion = ExamPrompts.Version;
        policy.LockedAt = now;
        policy.LockedByUserId = User;
        guard.Audit("assessment.published", "Exam", exam.Id.ToString(), after: new
        {
            Details = DetailsAudit(exam),
            SessionPolicy = session,
            Objectives = objectives.Select(o => new { o.Key, o.Title, o.ConceptKey, o.Weight, o.MaxScore, Criteria = o.Criteria.OrderBy(c => c.Ordinal).Select(CriterionAudit) }),
        });
        guard.Audit("policy.locked", "GradingPolicy", exam.Id.ToString(), after: PolicySnapshot.Of(policy));
        await SaveAsync(ct);
        return new(true, "Published. The assessment and its grading policy are locked; assign it to students next.", exam.Id);
    }

    /// <summary>Archives an assessment: no new attempts start; sessions, results and the audit trail stay.</summary>
    public async Task<AssessmentResult> ArchiveAsync(Guid examId, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Archiving an assessment", "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That assessment no longer exists.");
        if (exam.Status == ExamStatus.Archived) return new(true, "Already archived.", exam.Id);
        var before = exam.Status;
        exam.Status = ExamStatus.Archived;
        guard.Audit("assessment.archived", "Exam", exam.Id.ToString(), new { Status = before.ToString() }, new { Status = exam.Status.ToString() });
        await SaveAsync(ct);
        return new(true, "Archived. Students can no longer start it; existing results are kept.", exam.Id);
    }

    // ================================================================== helpers

    /// <summary>The older exam service over the same context and tenant (concept maps, collections, assignments).</summary>
    public ExamService Exams() => new(db, tenant, directory, clock);

    /// <summary>
    /// The session policy publishing writes: the objectives' concepts in objective order; the question limit caps turns;
    /// the learning objectives (with their rubric criteria) the engine examines, and the assessment's mode (GAD-T120).
    /// </summary>
    public static SessionPolicy BuildSessionPolicy(Exam exam, IReadOnlyList<LearningObjective> objectives)
    {
        var basis = ExamService.ReadPolicy(exam.PolicyJson);
        var concepts = objectives.OrderBy(o => o.Ordinal).Select(o => o.ConceptKey).OfType<string>()
            .Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var limit = Math.Max(1, basis.MaxTurns);
        return basis with
        {
            ConceptIds = concepts,
            MaxTurns = limit,
            MaxTurnsPerConcept = Math.Max(1, limit / Math.Max(1, concepts.Count)),
            Objectives = Sessions.SessionObjectives.From(objectives),
            Mode = exam.AssessmentMode,
        };
    }

    private async Task<(Exam? Exam, AssessmentResult? Refusal)> EditableExamAsync(Guid examId, string action, CancellationToken ct)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, action, "Exam", examId.ToString(), ct))
            return (null, new(false, AssessmentGuard.Denied(Permission.ManageAssessments)));
        var org = Org;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return (null, new(false, "That assessment no longer exists."));
        if (exam.Status != ExamStatus.Draft) return (null, new(false, Frozen(exam.Status)));
        return (exam, null);
    }

    private static string Frozen(ExamStatus status) =>
        $"This assessment is {status.ToString().ToLowerInvariant()}, so its details, objectives and rubric are locked. Grading-policy changes go through an approval request.";

    private Task<List<LearningObjective>> LoadObjectivesAsync(Guid examId, CancellationToken ct)
    {
        var org = Org;
        return db.LearningObjectives.AsNoTracking().Include(o => o.Criteria).ThenInclude(c => c.Levels)
            .Where(o => o.ExamId == examId && o.OrganizationId == org).OrderBy(o => o.Ordinal).AsSplitQuery().ToListAsync(ct);
    }

    private Task<bool> AnySessionCompletedAsync(Guid examId, CancellationToken ct)
    {
        var org = Org;
        return db.ExamSessions.AsNoTracking().AnyAsync(s => s.ExamId == examId && s.OrganizationId == org && s.State == SessionState.Ended, ct);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(CancellationToken ct) =>
        (await directory.MembersAsync(Org, ct)).GroupBy(m => m.Member.UserId).ToDictionary(g => g.Key, g => g.First().UserName);

    private RubricCriterion AddCriterion(Guid objectiveId, CriterionDraft d, int ordinal, string? with_name = null)
    {
        var c = new RubricCriterion
        {
            OrganizationId = Org, ObjectiveId = objectiveId, Dimension = d.Dimension, Name = (with_name ?? d.Name).Trim(),
            Description = string.IsNullOrWhiteSpace(d.Description) ? null : d.Description.Trim(), Weight = d.Weight, Ordinal = ordinal,
        };
        db.RubricCriteria.Add(c);
        foreach (var level in Enum.GetValues<MasteryLevel>())
        {
            var row = new RubricLevelDescriptor
            {
                OrganizationId = Org, CriterionId = c.Id, Level = level,
                Descriptor = Descriptor(d.Levels.ElementAtOrDefault((int)level), level),
            };
            c.Levels.Add(row);
            db.RubricLevelDescriptors.Add(row);
        }
        return c;
    }

    private static string Descriptor(string? text, MasteryLevel level) =>
        string.IsNullOrWhiteSpace(text) ? MasteryLevels.DefaultDescriptor(level) : text.Trim();

    private static IReadOnlyList<string> LevelTexts(RubricCriterion c) =>
        Enum.GetValues<MasteryLevel>().OrderBy(l => (int)l)
            .Select(l => c.Levels.FirstOrDefault(x => x.Level == l)?.Descriptor is { Length: > 0 } t ? t : MasteryLevels.DefaultDescriptor(l)).ToList();

    private static string NextKey(IEnumerable<string> existing)
    {
        var n = existing.Select(k => k.StartsWith("obj-", StringComparison.Ordinal) && int.TryParse(k[4..], out var i) ? i : 0).DefaultIfEmpty(0).Max();
        return $"obj-{n + 1}";
    }

    private static PolicySnapshot ReadSnapshot(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PolicySnapshot>(json, Json) ?? PolicySnapshot.Default;
        }
        catch (JsonException)
        {
            return PolicySnapshot.Default;
        }
    }

    private static object DetailsAudit(Exam e)
    {
        var p = ExamService.ReadPolicy(e.PolicyJson);
        return new
        {
            e.Title, e.CourseId, e.CollectionId, e.Subject, e.Topic, Mode = e.AssessmentMode.ToString(), e.Instructions,
            DurationMinutes = p.TimeBudget.TotalMinutes, QuestionLimit = p.MaxTurns, p.Difficulty,
        };
    }

    private static object ObjectiveAudit(LearningObjective o) => new { o.Key, o.Title, o.Description, o.ConceptKey, o.Weight, o.MaxScore, o.Ordinal };

    private static object CriterionAudit(RubricCriterion c) => new
    {
        c.ObjectiveId, Dimension = c.Dimension.ToString(), c.Name, c.Description, c.Weight,
        Levels = c.Levels.OrderBy(l => l.Level).Select(l => new { Level = (int)l.Level, l.Descriptor }),
    };

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max];

    private async Task SaveAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
