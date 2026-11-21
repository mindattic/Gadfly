using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Review;

/// <summary>
/// The one rule for releasing a student's result (GAD-T70), used by the review page and the exam's
/// assignments table alike: a result can be released only when the assignment's latest ended session
/// has a grade (a <see cref="GradeDecision"/>). Withdrawing a release is always allowed.
/// </summary>
public static class ResultRelease
{
    public const string NoEndedSession = "The student hasn't finished an attempt yet, so there is no result to release.";
    public const string NeedsGrade = "Grade the student's latest finished attempt before releasing the result.";

    /// <summary>Why the result can't be released yet, or null when it can.</summary>
    public static string? Blocker(Guid? latestEndedSessionId, bool latestEndedIsGraded) =>
        latestEndedSessionId is null ? NoEndedSession
        : !latestEndedIsGraded ? NeedsGrade
        : null;

    /// <summary>Why the assignment's result can't be released yet, or null when it can.</summary>
    public static async Task<string?> BlockerAsync(GadflyDbContext db, Guid organizationId, Guid assignmentId, CancellationToken ct = default)
    {
        var latestEnded = await db.ExamSessions.AsNoTracking()
            .Where(s => s.AssignmentId == assignmentId && s.OrganizationId == organizationId && s.State == SessionState.Ended)
            .OrderByDescending(s => s.Attempt)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        var graded = latestEnded is { } id
                     && await db.GradeDecisions.AsNoTracking().AnyAsync(g => g.SessionId == id && g.OrganizationId == organizationId, ct);
        return Blocker(latestEnded, graded);
    }
}
