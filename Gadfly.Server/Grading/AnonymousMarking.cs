using Gadfly.Assessment;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>
/// Anonymous human marking (GAD-T439; ElenchusAtScale Section 2.9.3, Meadows &amp; Billington 2005: remove names from
/// scripts before marking; Section 5.3.2). When the exam's grading policy turns it on, graders see a session code instead
/// of the student's name until the human score is submitted: the same window in which blind grading hides the AI's
/// evaluation (<see cref="GradingService.AiVisible"/>). After that the name is shown again for reconciliation, approval
/// and publishing. It hides the name the app shows; it cannot remove a name the student says aloud in an answer.
/// </summary>
public static class AnonymousMarking
{
    /// <summary>A stable, readable code for one session, e.g. "Session 5A7D-9C2E".</summary>
    public static string Code(Guid sessionId)
    {
        var hex = sessionId.ToString("N").ToUpperInvariant();
        return $"Session {hex[..4]}-{hex[4..8]}";
    }

    /// <summary>True while the student's name is hidden: the policy asks for anonymous marking and the human score is not in yet.</summary>
    public static bool Hides(PolicySnapshot policy, GradeStatus? status) =>
        policy.Rules.AnonymousMarking && status is null or GradeStatus.ExaminationInProgress or GradeStatus.ExaminationCompleted
            or GradeStatus.TranscriptVerificationPending or GradeStatus.AiGradingPending or GradeStatus.HumanReviewPending;

    /// <summary>The name to show: the real one, or the session code while anonymous marking hides it.</summary>
    public static string NameOrCode(bool hidden, Guid sessionId, string? name) => hidden ? Code(sessionId) : name ?? "(unknown student)";

    /// <summary>
    /// The sessions among <paramref name="sessionIds"/> whose student name is hidden right now, read from each grade's
    /// snapshotted policy (or, before grading starts, the exam's current policy).
    /// </summary>
    public static async Task<HashSet<Guid>> HiddenSessionsAsync(GadflyDbContext db, Guid organizationId, IReadOnlyCollection<Guid> sessionIds,
        DateTimeOffset now, CancellationToken ct = default)
    {
        var hidden = new HashSet<Guid>();
        if (sessionIds.Count == 0) return hidden;
        var ids = sessionIds.ToList();
        var sessions = await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == organizationId && ids.Contains(s.Id))
            .Select(s => new { s.Id, s.ExamId }).ToListAsync(ct);
        var examIds = sessions.Select(s => s.ExamId).Distinct().ToList();
        var policies = (await db.GradingPolicies.AsNoTracking().Where(p => p.OrganizationId == organizationId && examIds.Contains(p.ExamId)).ToListAsync(ct))
            .GroupBy(p => p.ExamId).ToDictionary(g => g.Key, g => g.First());
        var grades = (await db.FinalGrades.AsNoTracking().Where(g => g.OrganizationId == organizationId && ids.Contains(g.SessionId))
                .Select(g => new { g.SessionId, g.Status, g.PolicyJson }).ToListAsync(ct))
            .ToDictionary(g => g.SessionId);
        foreach (var s in sessions)
        {
            grades.TryGetValue(s.Id, out var grade);
            var policy = GradingJson.Read<PolicySnapshot>(grade?.PolicyJson) ?? PolicySnapshot.From(policies.GetValueOrDefault(s.ExamId), now);
            if (Hides(policy, grade?.Status)) hidden.Add(s.Id);
        }
        return hidden;
    }
}
