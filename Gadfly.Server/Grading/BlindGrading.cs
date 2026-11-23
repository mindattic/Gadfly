using Gadfly.Assessment;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>
/// Decision D25, default A (GAD-T218): while an examiner's grading of a session is blind, the exam-time judge's
/// per-answer evidence (stance, dimension, notes, scores, votes, summaries) is hidden along with the AI grade. The
/// visibility condition is exactly <see cref="GradingService.AiVisible"/>; this is the one place the rule is
/// applied outside the grading workspace (the legacy review page, its exports and its list).
/// </summary>
public static class BlindGrading
{
    /// <summary>
    /// True when a viewer in <paramref name="role"/> is acting as a potential blind examiner: a role that can enter
    /// human scores. Owners and Admins, who cannot, keep the unredacted review view.
    /// </summary>
    public static bool AppliesToRole(string? role) => PermissionMatrix.Has(role, Permission.EnterHumanScore);

    /// <summary>
    /// The sessions among <paramref name="sessionIds"/> whose AI and exam-time evaluation is hidden: those with a
    /// grade record (or ended, so grading can start) for which <see cref="GradingService.AiVisible"/> is false.
    /// A session still in progress has nothing to protect yet.
    /// </summary>
    public static async Task<HashSet<Guid>> BlindSessionsAsync(GadflyDbContext db, Guid organizationId, IReadOnlyCollection<Guid> sessionIds,
        DateTimeOffset now, CancellationToken ct = default)
    {
        var blind = new HashSet<Guid>();
        if (sessionIds.Count == 0) return blind;
        var ids = sessionIds.ToList();
        var sessions = await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == organizationId && ids.Contains(s.Id))
            .Select(s => new { s.Id, s.ExamId, s.State }).ToListAsync(ct);
        var grades = (await db.FinalGrades.AsNoTracking().Where(g => g.OrganizationId == organizationId && ids.Contains(g.SessionId)).ToListAsync(ct))
            .ToDictionary(g => g.SessionId);
        var examIds = sessions.Select(s => s.ExamId).Distinct().ToList();
        var policies = (await db.GradingPolicies.AsNoTracking().Where(p => p.OrganizationId == organizationId && examIds.Contains(p.ExamId)).ToListAsync(ct))
            .GroupBy(p => p.ExamId).ToDictionary(g => g.Key, g => g.First());
        var humanGraded = (await db.HumanObjectiveGrades.AsNoTracking().Where(h => h.OrganizationId == organizationId && ids.Contains(h.SessionId))
            .Select(h => h.SessionId).Distinct().ToListAsync(ct)).ToHashSet();
        foreach (var s in sessions)
        {
            grades.TryGetValue(s.Id, out var grade);
            if (grade is null && s.State != SessionState.Ended) continue;
            var policy = GradingJson.Read<PolicySnapshot>(grade?.PolicyJson) ?? PolicySnapshot.From(policies.GetValueOrDefault(s.ExamId), now);
            if (!GradingService.AiVisible(policy, grade?.Status, humanGraded.Contains(s.Id))) blind.Add(s.Id);
        }
        return blind;
    }
}
