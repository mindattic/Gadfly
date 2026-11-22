using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Platform;

/// <summary>
/// A snapshot of one organization's plan and how it stands against it (GAD-T309), for the owner's plan page,
/// the platform admin's plan page and enforcement itself. "Used" counts are always organization-wide
/// (<see cref="PlanEnforcement"/> reads with <c>IgnoreQueryFilters</c>), never narrowed to one instructor's own items.
/// </summary>
public sealed record PlanStatus(
    string Plan,
    int StaffSeatLimit, int StaffSeatsUsed,
    int StudentSeatLimit, int StudentSeatsUsed,
    int? MonthlyExamAllowance, int ExamsUsedThisPeriod, DateTimeOffset PeriodStart, DateTimeOffset? PeriodEnd,
    DateTimeOffset? TrialStartedAt, DateTimeOffset? PlanRenewsAt, string? PlanNote)
{
    public bool IsTrial => string.Equals(Plan, PlanCatalog.Trial, StringComparison.Ordinal);
    public bool TrialExpired => IsTrial && PeriodEnd is { } end && end <= DateTimeOffset.UtcNow;

    private static bool AtOrOver(int used, int limit) => limit > 0 && used >= limit;
    private static bool Near(int used, int limit) => limit > 0 && used >= Math.Ceiling(limit * 0.8);

    public bool StaffSeatsBlocked => AtOrOver(StaffSeatsUsed, StaffSeatLimit);
    public bool StaffSeatsWarning => !StaffSeatsBlocked && Near(StaffSeatsUsed, StaffSeatLimit);
    public bool StudentSeatsBlocked => AtOrOver(StudentSeatsUsed, StudentSeatLimit);
    public bool StudentSeatsWarning => !StudentSeatsBlocked && Near(StudentSeatsUsed, StudentSeatLimit);
    public bool ExamsBlocked => TrialExpired || (MonthlyExamAllowance is { } cap && AtOrOver(ExamsUsedThisPeriod, cap));
    public bool ExamsWarning => !ExamsBlocked && MonthlyExamAllowance is { } cap && Near(ExamsUsedThisPeriod, cap);
}

/// <summary>
/// Reads and enforces an organization's plan (GAD-T309): seat limits (gating who may be added), the exam
/// allowance (gating starting a new attempt, alongside <see cref="UsageMeter.MonthAsync"/>'s cost quota), and
/// trial expiry. A platform admin changes the numbers themselves from PlatformPlans.razor by editing
/// <see cref="OrganizationSettings"/> directly; this class only reads and checks against them.
/// </summary>
public sealed class PlanEnforcement(TimeProvider clock)
{
    /// <summary>Exam sessions started this period don't count a session replaced by a prompt-set change (GAD-T235), same as attempt counting elsewhere.</summary>
    private const string NotAnAttempt = "PromptSetChanged";

    public async Task<PlanStatus> StatusAsync(GadflyDbContext db, Guid organizationId, CancellationToken ct = default)
    {
        var settings = await db.OrganizationSettings.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct) ?? new OrganizationSettings { OrganizationId = organizationId };
        var now = clock.GetUtcNow();

        DateTimeOffset periodStart;
        DateTimeOffset? periodEnd;
        if (string.Equals(settings.Plan, PlanCatalog.Trial, StringComparison.Ordinal))
        {
            periodStart = settings.TrialStartedAt ?? now;
            periodEnd = periodStart.AddDays(30);
        }
        else
        {
            periodStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            periodEnd = null;   // paid plans renew every calendar month; PlanRenewsAt is informational (billing), not a hard cutoff
        }

        var examsUsed = await db.ExamSessions.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(s => s.OrganizationId == organizationId && s.CreatedAt >= periodStart && s.EndReason != NotAnAttempt, ct);

        var members = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.IsActive)
            .Select(m => m.Role).ToListAsync(ct);
        var staffUsed = members.Count(r => r != OrgRoles.Student);
        var studentUsed = members.Count(r => r == OrgRoles.Student);

        return new PlanStatus(settings.Plan, settings.StaffSeatLimit, staffUsed, settings.StudentSeatLimit, studentUsed,
            settings.MonthlyExamAllowance, examsUsed, periodStart, periodEnd, settings.TrialStartedAt, settings.PlanRenewsAt, settings.PlanNote);
    }

    /// <summary>Call before starting a new exam attempt, alongside the cost-quota check (GAD-T47).</summary>
    public async Task<(bool Ok, string? Message)> CheckExamAllowanceAsync(GadflyDbContext db, Guid organizationId, CancellationToken ct = default)
    {
        var status = await StatusAsync(db, organizationId, ct);
        if (status.TrialExpired)
            return (false, "Your organization's trial has ended. Ask your organization's owner to choose a plan.");
        if (status.ExamsBlocked)
            return (false, status.IsTrial
                ? "Your organization has used every exam attempt included in its trial. Ask your organization's owner to choose a plan."
                : "Your organization has reached its monthly exam limit. Please contact your instructor.");
        return (true, null);
    }

    /// <summary>Call before a new member joins (direct add or an email invitation) in this role.</summary>
    public async Task<(bool Ok, string? Message)> CheckSeatAsync(GadflyDbContext db, Guid organizationId, string role, CancellationToken ct = default)
    {
        var status = await StatusAsync(db, organizationId, ct);
        var staff = role != OrgRoles.Student;
        var blocked = staff ? status.StaffSeatsBlocked : status.StudentSeatsBlocked;
        if (!blocked) return (true, null);
        var limit = staff ? status.StaffSeatLimit : status.StudentSeatLimit;
        var kind = staff ? "staff" : "student";
        return (false, $"This organization's {status.Plan} plan allows {limit} {kind} seat{(limit == 1 ? "" : "s")}, and it is already using all of them. " +
            "Remove someone first, or ask your organization's owner to upgrade the plan.");
    }
}
