using System.Text.Json;
using System.Text.Json.Serialization;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Sources.Grading;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>Who performs a grading action: a person in a role, or the platform itself (background grading).</summary>
public sealed record GradingActor(Guid? UserId, string? Role)
{
    public static readonly GradingActor System = new(null, "system");
    public bool Has(Permission p) => UserId is not null && PermissionMatrix.Has(Role, p);
}

/// <summary>The outcome of a grading action, in words for the page.</summary>
public sealed record GradingResult(bool Ok, string Message)
{
    public static GradingResult Fail(string m) => new(false, m);
    public static GradingResult Done(string m) => new(true, m);
}

/// <summary>
/// The grading policy a session is graded under, snapshotted into FinalGrade.PolicyJson when AI grading
/// starts (Part 9.3: weights are approved before the examination; later changes need an approved, audited
/// GradingPolicyChange and do not silently re-grade finished work).
/// </summary>
public sealed record PolicySnapshot
{
    public GradingPolicyRules Rules { get; init; } = new();
    public bool AllowAssistedEvidence { get; init; }
    public string InconclusiveProcedure { get; init; } = nameof(Data.InconclusiveProcedure.HumanReview);
    /// <summary>"exam-policy" (the exam's GradingPolicy row) or "platform-default" (none was configured).</summary>
    public string Source { get; init; } = "platform-default";
    public DateTimeOffset? LockedAt { get; init; }
    public DateTimeOffset TakenAt { get; init; }
    /// <summary>
    /// The exam's grading-panel calibration when AI grading started (GAD-T270): null when none was recorded, so the
    /// panel's grades for this exam are provisional (not accepted on calibration evidence).
    /// </summary>
    public PanelCalibrationNote? Panel { get; init; }
    /// <summary>
    /// The calibration gate when the exam uses it (GAD-T427): whether it set the panel's weight to 0 for this session, and why.
    /// Null when the exam does not gate the panel.
    /// </summary>
    public PanelGateNote? Gate { get; init; }

    public static PolicySnapshot From(GradingPolicy? p, DateTimeOffset now) => p is null
        ? new PolicySnapshot { TakenAt = now }
        : new PolicySnapshot
        {
            Rules = p.ToRules(), AllowAssistedEvidence = p.AllowAssistedEvidence, InconclusiveProcedure = p.InconclusiveProcedure.ToString(),
            Source = "exam-policy", LockedAt = p.LockedAt, TakenAt = now,
        };
}

internal static class GradingJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return null; }
    }

    public static List<T> ReadList<T>(string? json) => Read<List<T>>(json) ?? [];
}

/// <summary>What AiObjectiveGrade.CriteriaJson holds: the merged criteria and each voter's own result.</summary>
public sealed record StoredAiDetail(IReadOnlyList<CriterionGrade> Criteria, IReadOnlyList<VoterGrade> Voters);

/// <summary>Append-only audit writing for grading (GAD-T84): every state change and every refusal.</summary>
internal static class GradingAudit
{
    public static void Add(GadflyDbContext db, Guid organizationId, GradingActor actor, string action, string entityType, string? entityId,
        Guid? sessionId, DateTimeOffset at, object? before = null, object? after = null, string? reason = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId,
            ActorUserId = actor.UserId,
            ActorRole = Trim(actor.Role, 40),
            Action = Trim(action, 80)!,
            EntityType = Trim(entityType, 80)!,
            EntityId = Trim(entityId, 80),
            SessionId = sessionId,
            BeforeJson = before is null ? null : before as string ?? GradingJson.Write(before),
            AfterJson = after is null ? null : after as string ?? GradingJson.Write(after),
            Reason = Trim(reason, 1000),
            At = at,
        });
    }

    /// <summary>Records a refused attempt (Scenario J) and saves it at once, independently of anything else.</summary>
    public static async Task DeniedAsync(GadflyDbContext db, Guid organizationId, GradingActor actor, Permission permission, string attempted,
        Guid? sessionId, DateTimeOffset at, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        Add(db, organizationId, actor, "access.denied", "FinalGrade", sessionId?.ToString(), sessionId, at,
            reason: $"{attempted} needs the {permission} permission; role {actor.Role ?? "(none)"} does not have it.");
        await db.SaveChangesAsync(ct);
    }

    public static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}

/// <summary>
/// Moves a session's FinalGrade through <see cref="GradeStatusMachine"/> (Part 11.2). The machine is the only
/// authority on allowed moves; every move is audited and an invalid one throws (callers audit the refusal).
/// </summary>
internal static class GradeLifecycle
{
    public static void Move(GadflyDbContext db, FinalGrade grade, GradeStatus to, GradingActor actor, DateTimeOffset at, string? reason = null,
        GradingMode mode = GradingMode.Combined)
    {
        var from = grade.Status;
        grade.Status = GradeStatusMachine.Move(from, to, mode);
        grade.UpdatedAt = at;
        GradingAudit.Add(db, grade.OrganizationId, actor, "grade.status.changed", "FinalGrade", grade.SessionId.ToString(), grade.SessionId, at,
            new { status = from.ToString() }, new { status = to.ToString() }, reason);
    }

    /// <summary>Creates the session's grade record at Examination Completed (idempotent).</summary>
    public static async Task<FinalGrade> EnsureAsync(GadflyDbContext db, ExamSession session, GradingActor actor, DateTimeOffset at, CancellationToken ct)
    {
        var existing = await db.FinalGrades.FirstOrDefaultAsync(g => g.SessionId == session.Id && g.OrganizationId == session.OrganizationId, ct);
        if (existing is not null) return existing;
        var grade = new FinalGrade { SessionId = session.Id, OrganizationId = session.OrganizationId, Status = GradeStatus.ExaminationCompleted, UpdatedAt = at };
        db.FinalGrades.Add(grade);
        GradingAudit.Add(db, session.OrganizationId, actor, "grade.created", "FinalGrade", session.Id.ToString(), session.Id, at,
            after: new { status = grade.Status.ToString() }, reason: "The examination ended.");
        await db.SaveChangesAsync(ct);
        return grade;
    }
}

/// <summary>
/// The two moments other features hang work on (GAD-T703; GAD-T702 posts them to the student's inbox). Each is called once
/// per occurrence, adds its rows to <paramref name="db"/> and leaves saving to the caller.
/// </summary>
internal static class GradingEvents
{
    /// <summary>AI grading has recorded every objective of the session, in any grading mode, before the grade moves on.</summary>
    public static Task AiGradingFinishedAsync(GadflyDbContext db, FinalGrade grade, PolicySnapshot policy, DateTimeOffset at, CancellationToken ct)
    {
        GradingAudit.Add(db, grade.OrganizationId, GradingActor.System, "grade.ai.completed", "FinalGrade", grade.SessionId.ToString(), grade.SessionId, at,
            after: new { mode = policy.Rules.Mode.ToString() });
        return Task.CompletedTask;
    }

    /// <summary>The grade was published to the student: by a person (GradingService.PublishAsync) or by the system under AI-only grading.</summary>
    public static Task GradePublishedAsync(GadflyDbContext db, FinalGrade grade, GradingActor actor, DateTimeOffset at, CancellationToken ct)
    {
        GradingAudit.Add(db, grade.OrganizationId, actor, "grade.published", "FinalGrade", grade.SessionId.ToString(), grade.SessionId, at,
            after: new { finalPercent = grade.FinalPercent });
        return Task.CompletedTask;
    }
}

/// <summary>
/// AI-only grading (GAD-T703, owner decision 2026-11-23): once the AI has graded every objective its levels are the final
/// grade. The system finalizes it (AI Grading Pending → Finalized, a move <see cref="GradeStatusMachine"/> allows only in
/// this mode) and publishes it (Finalized → Published), with an approval record and an audit row for each step, so the
/// student's results page shows it. An attempt held for instructor review (GAD-T603) is finalized but waits for a person
/// to publish it.
/// </summary>
internal static class AiOnlyGrading
{
    /// <summary>Stands in for a person on the approval records the system writes (ApprovalRecord.ActorUserId is required).</summary>
    public static readonly Guid SystemActorId = Guid.Empty;

    public static async Task FinalizeAsync(GadflyDbContext db, ExamSession session, FinalGrade grade, PolicySnapshot policy,
        IReadOnlyList<LearningObjective> objectives, DateTimeOffset at, CancellationToken ct)
    {
        var ai = (await db.AiObjectiveGrades.AsNoTracking().Where(a => a.SessionId == session.Id && a.OrganizationId == session.OrganizationId).ToListAsync(ct))
            .GroupBy(a => a.ObjectiveId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.CreatedAt).First());
        var (result, excluded) = GradingService.TryCompute(policy,
            objectives.Select(o => (o, ai.GetValueOrDefault(o.Id), (HumanObjectiveGrade?)null, (GradeReconciliation?)null)).ToList());
        if (result?.FinalPercent is null)
            throw new InvalidOperationException("The AI-only grade could not be computed (check the objectives' maximum scores and weights).");
        grade.FinalPercent = result.FinalPercent;
        grade.ResultJson = GradingJson.Write(new StoredResult(result, [.. excluded], at));

        const string why = "AI-only grading: the AI's grade is final (owner decision 2026-11-23).";
        GradeLifecycle.Move(db, grade, GradeStatus.Finalized, GradingActor.System, at, why, GradingMode.AiOnly);
        AddApproval(db, session, ApprovalAction.Finalize, why, at);
        GradingAudit.Add(db, grade.OrganizationId, GradingActor.System, "grade.approved", "FinalGrade", grade.SessionId.ToString(), grade.SessionId, at,
            after: new { finalPercent = grade.FinalPercent, mode = nameof(GradingMode.AiOnly) }, reason: why);
        if (await Review.InstructorReview.IsHeldAsync(db, session.OrganizationId, session.Id, ct)) return;

        GradeLifecycle.Move(db, grade, GradeStatus.Published, GradingActor.System, at, "AI-only grading: published to the student.", GradingMode.AiOnly);
        AddApproval(db, session, ApprovalAction.Publish, "AI-only grading: published automatically.", at);
        await GradingEvents.GradePublishedAsync(db, grade, GradingActor.System, at, ct);
    }

    private static void AddApproval(GadflyDbContext db, ExamSession session, ApprovalAction action, string note, DateTimeOffset at) =>
        db.ApprovalRecords.Add(new ApprovalRecord
        {
            OrganizationId = session.OrganizationId, SessionId = session.Id, Action = action, ActorUserId = SystemActorId,
            ActorRole = GradingActor.System.Role!, Note = note, At = at,
        });
}
