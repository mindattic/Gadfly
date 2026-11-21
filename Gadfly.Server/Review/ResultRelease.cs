using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Review;

/// <summary>
/// The old assignment-level "release results" path (GAD-T70), retired by the grading workflow (GAD-T98):
/// a result now reaches a student only when its FinalGrade has a Finalize approval record and has been
/// published (Grading pages). Release is therefore always blocked here; withdrawing an old release is
/// still allowed, and students no longer see instructor GradeDecisions through it.
/// </summary>
public static class ResultRelease
{
    public const string NoEndedSession = "The student hasn't finished an attempt yet, so there is no result to release.";
    public const string NeedsGrade = "Grade the student's latest finished attempt before releasing the result.";
    public const string UseGrading = "Results reach students only through Grading: approve the final grade, then publish it.";

    /// <summary>Why the result can't be released yet, or null when it can.</summary>
    public static string? Blocker(Guid? latestEndedSessionId, bool latestEndedIsGraded) =>
        latestEndedSessionId is null ? NoEndedSession : UseGrading;

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
