using System.Globalization;
using Gadfly.Assessment;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Admin;

/// <summary>Where one student's assignment stands, as the admin pages show it (GAD-T296, GAD-T298).</summary>
public enum AssignmentProgress
{
    /// <summary>No attempt yet.</summary>
    NotStarted,
    /// <summary>An attempt is open (created, consented, running or paused).</summary>
    InProgress,
    /// <summary>The only attempts ended with an error before a proper end.</summary>
    Interrupted,
    /// <summary>An attempt ended and its grade is not final yet.</summary>
    AwaitingGrading,
    /// <summary>The grade is final but results are not released to the student.</summary>
    Graded,
    /// <summary>Results are released to the student.</summary>
    Released,
}

/// <summary>Shared rules and helpers for the admin people pages: who may use them, status words, the audit write, names.</summary>
public static class AdminPeople
{
    /// <summary>Grade statuses that mean a human has finished grading.</summary>
    public static readonly GradeStatus[] FinalStatuses = [GradeStatus.Finalized, GradeStatus.Published, GradeStatus.Amended];

    /// <summary>The end reason of a session that never counted as an attempt (the exam's prompts changed under it).</summary>
    public static readonly string NotAnAttempt = nameof(Gadfly.Engine.EndReason.PromptSetChanged);

    public const int MaxPageSize = 200;
    public const int MaxReasonLength = 500;

    /// <summary>The status from the facts the list queries gather in SQL.</summary>
    public static AssignmentProgress Of(int attemptsUsed, bool live, bool ended, bool graded, DateTimeOffset? releasedAt)
    {
        if (live) return AssignmentProgress.InProgress;
        if (attemptsUsed == 0) return AssignmentProgress.NotStarted;
        if (releasedAt is not null) return AssignmentProgress.Released;
        if (!ended) return AssignmentProgress.Interrupted;
        return graded ? AssignmentProgress.Graded : AssignmentProgress.AwaitingGrading;
    }

    public static string Describe(AssignmentProgress p) => p switch
    {
        AssignmentProgress.NotStarted => "Not started",
        AssignmentProgress.InProgress => "In progress",
        AssignmentProgress.Interrupted => "Interrupted",
        AssignmentProgress.AwaitingGrading => "Awaiting grading",
        AssignmentProgress.Graded => "Graded, not released",
        AssignmentProgress.Released => "Released",
        _ => p.ToString(),
    };

    /// <summary>A CSS-friendly name for the status pill.</summary>
    public static string Css(AssignmentProgress p) => "progress-" + p.ToString().ToLowerInvariant();

    /// <summary>The extra-time accommodation in words ("none", "1.5x").</summary>
    public static string Time(decimal multiplier) =>
        multiplier <= 1m ? "none" : multiplier.ToString("0.##", CultureInfo.InvariantCulture) + "x";

    /// <summary>A display name: the account's name when it has one, else its user name.</summary>
    public static string NameOf(string userName, string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? userName : displayName.Trim();

    /// <summary>A reason as stored: trimmed and capped; null when blank.</summary>
    public static string? Reason(string? reason)
    {
        var r = reason?.Trim();
        return string.IsNullOrEmpty(r) ? null : r.Length <= MaxReasonLength ? r : r[..MaxReasonLength];
    }

    public static int ClampPageSize(int size) => Math.Clamp(size <= 0 ? 25 : size, 5, MaxPageSize);

    /// <summary>
    /// A grade as the admin pages show it: the final percent with its status, else the AI report's per-objective levels
    /// (GAD-T425). The examiner's running mastery estimate and the mean answer score are never shown as the student's
    /// score: they only steer the questions (ElenchusAtScale Prop. 3.3, M9, M10).
    /// </summary>
    public static string? Score(decimal? finalPercent, GradeStatus? status, string? aiReportJson)
    {
        if (finalPercent is { } pct) return $"{pct.ToString("0.#", CultureInfo.InvariantCulture)}%{(status is { } s ? $" ({Words(s)})" : "")}";
        if (Gadfly.Server.Review.ReportedLevels.Short(Gadfly.Server.Review.ReportedLevels.FromReportJson(aiReportJson)) is { } levels)
            return $"AI {char.ToLowerInvariant(levels[0])}{levels[1..]}, not yet graded";
        return null;
    }

    /// <summary>A grade status in plain words.</summary>
    public static string Words(GradeStatus s) => s switch
    {
        GradeStatus.ExaminationInProgress => "held",
        GradeStatus.ExaminationCompleted => "ended",
        GradeStatus.TranscriptVerificationPending => "transcript check",
        GradeStatus.AiGradingPending => "AI grading",
        GradeStatus.HumanReviewPending => "human review",
        GradeStatus.ReconciliationRequired => "reconciliation",
        GradeStatus.AwaitingFinalApproval => "awaiting approval",
        GradeStatus.Finalized => "final",
        GradeStatus.Published => "published",
        GradeStatus.UnderAppeal => "under appeal",
        GradeStatus.Amended => "amended",
        _ => s.ToString(),
    };
}

/// <summary>
/// The admin people pages' gate and audit writer: the caller must be the organization's Owner or Admin (the pages' own policy
/// checks the same, with MFA); a refusal is recorded as <c>access.denied</c>, as everywhere else.
/// </summary>
internal sealed class AdminGate(GadflyDbContext db, ITenantContext tenant, TimeProvider clock)
{
    private AssessmentGuard Guard => field ??= new AssessmentGuard(db, tenant, clock);

    public Guid Org => tenant.OrganizationId ?? throw new InvalidOperationException("No active organization.");
    public Guid User => tenant.UserId ?? throw new InvalidOperationException("No signed-in user.");
    public string? Role => tenant.Role;

    public bool Allowed => tenant.OrganizationId is not null && tenant.UserId is not null && AdminArea.IsOrgAdmin(tenant.Role);

    /// <summary>Throws for a caller who is not an Owner or Admin (reads); the page's policy has already refused them.</summary>
    public void EnsureReader()
    {
        if (!Allowed) throw new UnauthorizedAccessException("Only the organization's owners and admins can open this.");
    }

    /// <summary>True when allowed; otherwise records and saves an <c>access.denied</c> event and returns false.</summary>
    public async Task<bool> RequireAsync(string attempted, string entityType, string? entityId, CancellationToken ct)
    {
        if (Allowed) return true;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = tenant.OrganizationId,
            ActorUserId = tenant.UserId,
            ActorRole = tenant.Role is { Length: > 40 } r ? r[..40] : tenant.Role,
            Action = "access.denied",
            EntityType = entityType,
            EntityId = entityId,
            Reason = $"{attempted} is for the organization's owners and admins; role {tenant.Role ?? "(none)"} is not.",
            At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return false;
    }

    public const string DeniedMessage = "Only the organization's owners and admins can do this. The attempt was recorded.";

    /// <summary>Adds (does not save) an audit event.</summary>
    public void Audit(string action, string entityType, string? entityId, object? before = null, object? after = null, string? reason = null) =>
        Guard.Audit(action, entityType, entityId, before, after, reason);

    /// <summary>
    /// True when the account is nothing but a student of this organization (active or not) and is not platform staff: only then may
    /// this organization's admins change its name, email or password. Same rule as the admin desk's password reset.
    /// </summary>
    public async Task<bool> OwnsAccountAsync(Guid userId, CancellationToken ct)
    {
        var org = Org;
        if (await db.OrganizationMembers.IgnoreQueryFilters()
                .AnyAsync(m => m.UserId == userId && !(m.OrganizationId == org && m.Role == OrgRoles.Student), ct))
            return false;
        return !await db.AuthUsers.AnyAsync(u => u.Id == userId && u.Role == MindAttic.Authentication.MaRoles.Admin, ct);
    }
}
