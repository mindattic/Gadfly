using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Transcripts;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>
/// Builds the class summary PDF's content (GAD-T311/GAD-T378): every assignment of one exam, in this organization,
/// bucketed into released (its session's <see cref="AssessmentReport"/> feeds the distribution and the per-objective
/// averages), completed but not yet released, and not yet attempted. "Released" is decided the same way the
/// transcript's student copy is (<see cref="TranscriptAccess.PublishedToStudentAsync"/>: a Publish approval record
/// over a Finalize one) rather than <see cref="ExamAssignment.ResultsReleasedAt"/>, which nothing in the current
/// grading workflow sets any more (it predates results moving to the Grading module). Built over
/// <see cref="GradingService"/> so a released result here is computed exactly the way the student's own report
/// computes it. Same role gate as the rest of grading (<see cref="Permission.ViewTranscripts"/>); a refusal is
/// audited like every other one in grading.
/// </summary>
public sealed class ClassSummaryService(GadflyDbContext db, ITenantContext tenant, GradingService grading, TimeProvider clock)
{
    /// <summary>Exams with more assignments than this are summarized from a sample-sized first page worth of attention: still
    /// correct (every released result is counted), just capped so one huge cohort can't make the PDF unusably long to build.</summary>
    public const int MaxAssignments = 5000;

    public async Task<ClassSummaryReport?> BuildAsync(Guid examId, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org) return null;
        if (!grading.Can(Permission.ViewTranscripts))
        {
            await GradingAudit.DeniedAsync(db, org, grading.Actor, Permission.ViewTranscripts, "Building a class summary", null, clock.GetUtcNow(), ct);
            return null;
        }
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null)
        {
            await db.RefuseForeignAsync(OwnershipScope.Kind.Exam, examId, "Building a class summary", ct);
            return null;
        }

        var assignments = await db.ExamAssignments.AsNoTracking().Where(a => a.OrganizationId == org && a.ExamId == examId)
            .OrderBy(a => a.AssignedAt).Take(MaxAssignments)
            .Select(a => new { a.Id, a.StudentUserId }).ToListAsync(ct);
        var assignmentIds = assignments.Select(a => a.Id).ToList();

        // The latest ended session of each assignment (an unfinished later attempt never hides an earlier, graded one).
        var endedSessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == org && assignmentIds.Contains(s.AssignmentId) && s.State == SessionState.Ended)
            .Select(s => new { s.Id, s.AssignmentId, s.Attempt }).ToListAsync(ct);
        var latestEndedByAssignment = endedSessions.GroupBy(s => s.AssignmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Attempt).First().Id);

        var orgName = await db.Organizations.AsNoTracking().Where(o => o.Id == org).Select(o => o.Name).FirstOrDefaultAsync(ct);

        var released = new List<AssessmentReport>();
        var completedNotReleased = 0;
        var notStarted = new List<string>();

        foreach (var a in assignments)
        {
            if (!latestEndedByAssignment.TryGetValue(a.Id, out var sessionId))
            {
                var student = await db.AuthUsers.AsNoTracking().Where(u => u.Id == a.StudentUserId).Select(u => new { u.UserName, u.DisplayName }).FirstOrDefaultAsync(ct);
                notStarted.Add(student is null ? "(unknown student)" : string.IsNullOrWhiteSpace(student.DisplayName) ? student.UserName : student.DisplayName);
                continue;
            }
            if (!await TranscriptAccess.PublishedToStudentAsync(db, org, sessionId, ct)) { completedNotReleased++; continue; }
            var w = await grading.WorkspaceAsync(sessionId, ct);
            if (w is null) { completedNotReleased++; continue; }   // refused or gone between listing and loading; counted as not yet visible
            released.Add(AssessmentReport.Build(w, forStudent: false));
        }

        var scores = released.Select(r => r.FinalPercent).Where(p => p is not null).Select(p => p!.Value).ToList();
        var objectives = released.SelectMany(r => r.Objectives).Where(o => o.Combined is not null)
            .GroupBy(o => o.Key)
            .Select(g => new ClassSummaryObjective(g.Key, g.First().Title, g.First().MaxScore,
                g.Select(o => o.Combined!.Value).DefaultIfEmpty().Average() is var avg && g.Any() ? Math.Round(avg, 2) : null, g.Count()))
            .OrderBy(o => o.Title, StringComparer.OrdinalIgnoreCase).ToList();

        return new ClassSummaryReport
        {
            ExamId = examId, ExamTitle = exam.Title, OrganizationName = orgName, TotalAssigned = assignments.Count,
            Released = released.Count, CompletedNotReleased = completedNotReleased, NotStarted = notStarted,
            Scores = scores, Objectives = objectives,
        };
    }
}
