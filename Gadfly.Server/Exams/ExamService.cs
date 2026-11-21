using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Exams;

/// <summary>What the exam builder edits (GAD-T50). Becomes an <see cref="Exam"/> row plus its <see cref="SessionPolicy"/>.</summary>
public sealed class ExamDraft
{
    public Guid? Id { get; set; }
    public Guid CollectionId { get; set; }
    public string Title { get; set; } = "";
    public string? Instructions { get; set; }
    /// <summary>Concept keys, in the order the instructor wants them examined.</summary>
    public List<string> ConceptKeys { get; set; } = [];
    public int DurationMinutes { get; set; } = 20;
    public int Difficulty { get; set; } = 3;
    /// <summary>Follow-up questions allowed per concept after the opening question.</summary>
    public int MaxFollowUps { get; set; } = 5;
    /// <summary>Text by default: voice needs the speech and avatar libraries, which are not built yet.</summary>
    public ExamMode Mode { get; set; } = ExamMode.Text;
    public IntegrityLevel IntegrityLevel { get; set; } = IntegrityLevel.Standard;
    public string? VoiceId { get; set; }
    public string? AvatarId { get; set; }
    public ExamStatus Status { get; set; } = ExamStatus.Draft;
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed record ExamResult(bool Ok, string Message, Guid? Id = null);

public sealed record ExamSummary(Guid Id, string Title, string CollectionTitle, ExamStatus Status, int Assigned, int SessionsStarted,
    int SessionsEnded, DateTimeOffset CreatedAt);

public sealed record CollectionOption(Guid Id, string Title, int ConceptMapVersion);

public sealed record ConceptOption(string Key, string Title, string Definition);

public sealed record StudentOption(Guid UserId, string UserName, string? Email);

public sealed record AssignmentRow(Guid Id, Guid StudentUserId, string StudentName, DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt,
    int AttemptsAllowed, int AttemptsUsed, SessionState? LatestState, DateTimeOffset? LatestEndedAt, DateTimeOffset AssignedAt,
    DateTimeOffset? ResultsReleasedAt, string? ReleaseBlocker = null)
{
    /// <summary>True when results can be released now (<see cref="Review.ResultRelease"/>).</summary>
    public bool CanRelease => ReleaseBlocker is null;
}

/// <summary>
/// Exam builder and assignments (GAD-T50): drafts, publishing (which freezes the policy), archiving,
/// assigning published exams to students, and releasing results (the instructor's decision, D13).
/// Everything runs inside the caller's active organization; <see cref="GadflyDbContext"/>'s tenant
/// filter and write guard back that up.
/// </summary>
public sealed class ExamService(GadflyDbContext db, ITenantContext tenant, IOrganizationDirectory directory, TimeProvider clock)
{
    /// <summary>Same JSON shape <see cref="Sessions.SessionOrchestrator"/> reads.</summary>
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public const int MinDurationMinutes = 1;
    public const int MaxDurationMinutes = 240;
    public const int MaxFollowUpsLimit = 20;

    private Guid Org => tenant.OrganizationId ?? throw new InvalidOperationException("No active organization.");
    private Guid User => tenant.UserId ?? throw new InvalidOperationException("No signed-in user.");

    /// <summary>Permission checks (GAD-T83): every change names a <see cref="Permission"/>; refusals are audited.</summary>
    private AssessmentGuard Guard => field ??= new AssessmentGuard(db, tenant, clock);

    private async Task<ExamResult?> DeniedAsync(Permission permission, string action, string entityType, Guid? id, CancellationToken ct) =>
        await Guard.RequireAsync(permission, action, entityType, id?.ToString(), ct) ? null : new ExamResult(false, AssessmentGuard.Denied(permission));

    // ------------------------------------------------------------------ reading

    public async Task<IReadOnlyList<ExamSummary>> ListAsync(CancellationToken ct = default)
    {
        var org = Org;
        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org)
            .OrderBy(e => e.Status).ThenByDescending(e => e.CreatedAt).ToListAsync(ct);
        var titles = await db.SourceCollections.AsNoTracking().Where(c => c.OrganizationId == org)
            .ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var assigned = await db.ExamAssignments.AsNoTracking().Where(a => a.OrganizationId == org)
            .GroupBy(a => a.ExamId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var sessions = await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org)
            .GroupBy(s => s.ExamId)
            .Select(g => new { g.Key, Started = g.Count(s => s.StartedAt != null), Ended = g.Count(s => s.State == SessionState.Ended) })
            .ToDictionaryAsync(x => x.Key, ct);
        return exams.Select(e => new ExamSummary(e.Id, e.Title, titles.GetValueOrDefault(e.CollectionId, "(missing collection)"), e.Status,
            assigned.GetValueOrDefault(e.Id), sessions.TryGetValue(e.Id, out var s) ? s.Started : 0, s?.Ended ?? 0, e.CreatedAt)).ToList();
    }

    /// <summary>Source collections whose concept map is ready (processed at least once).</summary>
    public async Task<IReadOnlyList<CollectionOption>> ReadyCollectionsAsync(CancellationToken ct = default)
    {
        var org = Org;
        return await db.SourceCollections.AsNoTracking()
            .Where(c => c.OrganizationId == org && c.Status == ProcessingStatus.Ready && c.ConceptMapVersion > 0)
            .OrderBy(c => c.Title)
            .Select(c => new CollectionOption(c.Id, c.Title, c.ConceptMapVersion))
            .ToListAsync(ct);
    }

    /// <summary>The collection's current (non-archived) concepts, in map order.</summary>
    public async Task<IReadOnlyList<ConceptOption>> ConceptsAsync(Guid collectionId, CancellationToken ct = default)
    {
        var map = await new SqlConceptMapStore(db, Org).LoadMapAsync(collectionId.ToString(), ct);
        return map is null ? [] : map.Concepts.Select(c => new ConceptOption(c.Key, c.Title, c.Definition)).ToList();
    }

    public async Task<ExamDraft?> GetAsync(Guid examId, CancellationToken ct = default)
    {
        var org = Org;
        var e = await db.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == examId && x.OrganizationId == org, ct);
        if (e is null) return null;
        var p = ReadPolicy(e.PolicyJson);
        return new ExamDraft
        {
            Id = e.Id,
            CollectionId = e.CollectionId,
            Title = e.Title,
            Instructions = e.Instructions,
            ConceptKeys = [.. p.ConceptIds],
            DurationMinutes = (int)Math.Round(p.TimeBudget.TotalMinutes),
            Difficulty = p.Difficulty,
            MaxFollowUps = Math.Max(0, p.MaxTurnsPerConcept - 1),
            Mode = e.Mode,
            IntegrityLevel = e.IntegrityLevel,
            VoiceId = e.VoiceId,
            AvatarId = e.AvatarId,
            Status = e.Status,
            PublishedAt = e.PublishedAt,
        };
    }

    // ------------------------------------------------------------------ building

    /// <summary>
    /// Saves a draft. A new exam, or edits to an existing draft, save in place; edits to a published or
    /// archived exam never touch it (its policy is frozen) and are saved as a copy, a new draft.
    /// </summary>
    public async Task<ExamResult> SaveDraftAsync(ExamDraft draft, CancellationToken ct = default)
    {
        if (await DeniedAsync(Permission.ManageAssessments, "Saving an exam draft", "Exam", draft.Id, ct) is { } denied) return denied;
        var org = Org;
        if (string.IsNullOrWhiteSpace(draft.Title)) return new(false, "Give the exam a title.");
        if (draft.Title.Trim().Length > 200) return new(false, "Keep the title under 200 characters.");
        if (!await db.SourceCollections.AnyAsync(c => c.Id == draft.CollectionId && c.OrganizationId == org, ct))
            return new(false, "Choose a source collection.");

        Exam? existing = null;
        if (draft.Id is { } id)
        {
            existing = await db.Exams.FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == org, ct);
            if (existing is null) return new(false, "That exam no longer exists.");
        }

        if (existing is { Status: ExamStatus.Draft })
        {
            Apply(existing, draft, ReadPolicy(existing.PolicyJson));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            return new(true, "Draft saved.", existing.Id);
        }

        var exam = new Exam
        {
            OrganizationId = org,
            Status = ExamStatus.Draft,
            CreatedByUserId = User,
            CreatedAt = clock.GetUtcNow(),
        };
        // A copy keeps the original's hidden policy settings (seed, thresholds) but gets its own seed.
        var basis = existing is null ? new SessionPolicy() : ReadPolicy(existing.PolicyJson);
        Apply(exam, draft, basis with { Seed = Random.Shared.Next() });
        db.Exams.Add(exam);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return existing is null
            ? new(true, "Draft created.", exam.Id)
            : new(true, $"\"{existing.Title}\" is {existing.Status.ToString().ToLowerInvariant()}, so its settings are frozen; your changes were saved as a new draft.", exam.Id);
    }

    /// <summary>Publishes a draft after validating its policy against the collection's current concept map.</summary>
    public async Task<ExamResult> PublishAsync(Guid examId, CancellationToken ct = default)
    {
        if (await DeniedAsync(Permission.ManageAssessments, "Publishing an exam", "Exam", examId, ct) is { } denied) return denied;
        var org = Org;
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        if (exam.Status != ExamStatus.Draft) return new(false, "Only a draft can be published.");

        var policy = ReadPolicy(exam.PolicyJson);
        if (policy.ConceptIds.Count == 0) return new(false, "Choose at least one concept before publishing.");
        try
        {
            policy.Validate();
        }
        catch (ArgumentException ex)
        {
            return new(false, $"The exam settings are not valid: {ex.Message}");
        }
        if (!await db.SourceCollections.AnyAsync(c => c.Id == exam.CollectionId && c.OrganizationId == org
                                                     && c.Status == ProcessingStatus.Ready && c.ConceptMapVersion > 0, ct))
            return new(false, "The source collection's concept map is not ready.");
        var keys = (await ConceptsAsync(exam.CollectionId, ct)).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var missing = policy.ConceptIds.Where(k => !keys.Contains(k)).ToList();
        if (missing.Count > 0)
            return new(false, $"These concepts are no longer in the collection's concept map: {string.Join(", ", missing)}. Edit the concept list and save first.");

        exam.Status = ExamStatus.Published;
        exam.PublishedAt = clock.GetUtcNow();
        exam.PromptSetId = ExamPrompts.SetId;
        exam.PromptSetVersion = ExamPrompts.Version;
        Guard.Audit("assessment.published", "Exam", exam.Id.ToString(), after: new { exam.Title, SessionPolicy = policy }, reason: "Published from the older exam builder");
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new(true, "Published. Its settings are now frozen; assign it to students below.", exam.Id);
    }

    /// <summary>Archives an exam: no new attempts start; existing sessions and results stay.</summary>
    public async Task<ExamResult> ArchiveAsync(Guid examId, CancellationToken ct = default)
    {
        if (await DeniedAsync(Permission.ManageAssessments, "Archiving an exam", "Exam", examId, ct) is { } denied) return denied;
        var org = Org;
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        if (exam.Status == ExamStatus.Archived) return new(true, "Already archived.", exam.Id);
        var wasStatus = exam.Status;
        exam.Status = ExamStatus.Archived;
        Guard.Audit("assessment.archived", "Exam", exam.Id.ToString(), new { Status = wasStatus.ToString() }, new { Status = exam.Status.ToString() });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new(true, "Archived. Students can no longer start it; existing results are kept.", exam.Id);
    }

    // ------------------------------------------------------------------ assigning

    /// <summary>Active members of the organization whose role is Student.</summary>
    public async Task<IReadOnlyList<StudentOption>> StudentsAsync(CancellationToken ct = default) =>
        (await directory.MembersAsync(Org, ct))
            .Where(m => m.Member.IsActive && m.Member.Role == OrgRoles.Student)
            .OrderBy(m => m.UserName, StringComparer.OrdinalIgnoreCase)
            .Select(m => new StudentOption(m.Member.UserId, m.UserName, m.Email))
            .ToList();

    /// <summary>
    /// Assigns a published exam to the given students (or every student when <paramref name="studentUserIds"/>
    /// is null). Students who already have an assignment for this exam are skipped.
    /// </summary>
    public async Task<ExamResult> AssignAsync(Guid examId, IReadOnlyCollection<Guid>? studentUserIds, DateTimeOffset? opensAt,
        DateTimeOffset? closesAt, int attemptsAllowed, CancellationToken ct = default)
    {
        if (await DeniedAsync(Permission.AssignExaminations, "Assigning an exam", "Exam", examId, ct) is { } denied) return denied;
        var org = Org;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        if (exam.Status != ExamStatus.Published) return new(false, "Publish the exam before assigning it.");
        if (attemptsAllowed is < 1 or > 20) return new(false, "Attempts allowed must be 1 to 20.");
        if (opensAt is { } o && closesAt is { } c && c <= o) return new(false, "The window must close after it opens.");

        var students = (await StudentsAsync(ct)).Select(s => s.UserId).ToHashSet();
        var wanted = studentUserIds is null ? students : studentUserIds.Where(students.Contains).ToHashSet();
        if (wanted.Count == 0) return new(false, studentUserIds is null ? "This organization has no students yet." : "Choose at least one student.");

        var already = (await db.ExamAssignments.AsNoTracking()
            .Where(a => a.ExamId == examId && a.OrganizationId == org)
            .Select(a => a.StudentUserId).ToListAsync(ct)).ToHashSet();
        var now = clock.GetUtcNow();
        var added = 0;
        foreach (var student in wanted.Where(s => !already.Contains(s)))
        {
            db.ExamAssignments.Add(new ExamAssignment
            {
                OrganizationId = org,
                ExamId = examId,
                StudentUserId = student,
                OpensAt = opensAt,
                ClosesAt = closesAt,
                AttemptsAllowed = attemptsAllowed,
                AssignedByUserId = User,
                AssignedAt = now,
            });
            added++;
        }
        if (added > 0)
        {
            Guard.Audit("assessment.assigned", "Exam", examId.ToString(),
                after: new { Students = wanted.Where(s => !already.Contains(s)).ToList(), opensAt, closesAt, attemptsAllowed });
            await db.SaveChangesAsync(ct);
        }
        db.ChangeTracker.Clear();
        var skipped = wanted.Count - added;
        var msg = $"Assigned to {added} student{(added == 1 ? "" : "s")}.";
        if (skipped > 0) msg += $" Skipped {skipped} already assigned.";
        return new(true, msg, examId);
    }

    /// <summary>Every assignment of an exam with the student's latest attempt.</summary>
    public async Task<IReadOnlyList<AssignmentRow>> AssignmentsAsync(Guid examId, CancellationToken ct = default)
    {
        var org = Org;
        var rows = await db.ExamAssignments.AsNoTracking().Where(a => a.ExamId == examId && a.OrganizationId == org).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var sessions = (await db.ExamSessions.AsNoTracking().Where(s => s.ExamId == examId && s.OrganizationId == org)
                .Select(s => new { s.Id, s.AssignmentId, s.Attempt, s.State, s.EndedAt }).ToListAsync(ct))
            .GroupBy(s => s.AssignmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Attempt).ToList());
        // Release rule (GAD-T70): the latest ended session of each assignment must have a grade.
        var latestEnded = sessions.ToDictionary(kv => kv.Key, kv => kv.Value.FirstOrDefault(s => s.State == SessionState.Ended)?.Id);
        var latestEndedIds = latestEnded.Values.OfType<Guid>().ToList();
        var graded = latestEndedIds.Count == 0 ? [] : (await db.GradeDecisions.AsNoTracking()
            .Where(g => g.OrganizationId == org && latestEndedIds.Contains(g.SessionId))
            .Select(g => g.SessionId).Distinct().ToListAsync(ct)).ToHashSet();
        var names = (await directory.MembersAsync(org, ct)).GroupBy(m => m.Member.UserId).ToDictionary(g => g.Key, g => g.First().UserName);
        return rows.Select(a =>
            {
                var list = sessions.GetValueOrDefault(a.Id) ?? [];
                var latest = list.FirstOrDefault();
                return new AssignmentRow(a.Id, a.StudentUserId, names.GetValueOrDefault(a.StudentUserId, "(unknown user)"), a.OpensAt, a.ClosesAt,
                    a.AttemptsAllowed, list.Count, latest?.State, latest?.EndedAt, a.AssignedAt, a.ResultsReleasedAt,
                    Review.ResultRelease.Blocker(latestEnded.GetValueOrDefault(a.Id), latestEnded.GetValueOrDefault(a.Id) is { } e && graded.Contains(e)));
            })
            .OrderBy(r => r.StudentName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Releases one student's results to them. The instructor decides when (D13); the result must be
    /// graded first, the same rule as the review page (<see cref="Review.ResultRelease"/>).
    /// </summary>
    public async Task<ExamResult> ReleaseResultsAsync(Guid assignmentId, CancellationToken ct = default)
    {
        if (await DeniedAsync(Permission.PublishGrade, "Releasing results", "ExamAssignment", assignmentId, ct) is { } denied) return denied;
        var org = Org;
        var a = await db.ExamAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId && x.OrganizationId == org, ct);
        if (a is null) return new(false, "That assignment no longer exists.");
        if (a.ResultsReleasedAt is not null) return new(true, "Results were already released.", a.Id);
        if (await Review.ResultRelease.BlockerAsync(db, org, a.Id, ct) is { } blocker) return new(false, blocker, a.Id);
        a.ResultsReleasedAt = clock.GetUtcNow();
        Guard.Audit("results.released", "ExamAssignment", a.Id.ToString(), after: new { a.ExamId, a.StudentUserId, a.ResultsReleasedAt });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new(true, "Results released to the student.", a.Id);
    }

    // ------------------------------------------------------------------ policy

    public static SessionPolicy ReadPolicy(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SessionPolicy();
        try
        {
            return JsonSerializer.Deserialize<SessionPolicy>(json, Json) ?? new SessionPolicy();
        }
        catch (JsonException)
        {
            return new SessionPolicy();
        }
    }

    /// <summary>The session policy a draft describes, layered over <paramref name="basis"/> (keeps thresholds and seed).</summary>
    public static SessionPolicy BuildPolicy(ExamDraft d, SessionPolicy basis)
    {
        var keys = d.ConceptKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        var perConcept = Math.Clamp(d.MaxFollowUps, 0, MaxFollowUpsLimit) + 1;
        return basis with
        {
            ConceptIds = keys,
            TimeBudget = TimeSpan.FromMinutes(Math.Clamp(d.DurationMinutes, MinDurationMinutes, MaxDurationMinutes)),
            Difficulty = Math.Clamp(d.Difficulty, 1, 5),
            MaxTurnsPerConcept = perConcept,
            MaxTurns = Math.Max(1, keys.Count * perConcept),
        };
    }

    private static void Apply(Exam exam, ExamDraft d, SessionPolicy basis)
    {
        exam.CollectionId = d.CollectionId;
        exam.Title = d.Title.Trim();
        exam.Instructions = string.IsNullOrWhiteSpace(d.Instructions) ? null : d.Instructions.Trim();
        exam.Mode = d.Mode;
        exam.IntegrityLevel = d.IntegrityLevel;
        exam.VoiceId = string.IsNullOrWhiteSpace(d.VoiceId) ? null : d.VoiceId.Trim();
        exam.AvatarId = string.IsNullOrWhiteSpace(d.AvatarId) ? null : d.AvatarId.Trim();
        exam.PromptSetId = ExamPrompts.SetId;
        exam.PromptSetVersion = ExamPrompts.Version;
        exam.PolicyJson = JsonSerializer.Serialize(BuildPolicy(d, basis), Json);
    }
}
