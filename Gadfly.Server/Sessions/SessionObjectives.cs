using Gadfly.Engine;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Sessions;

/// <summary>
/// Turns an assessment's learning objectives into what the examination engine examines (GAD-T120): one
/// <see cref="ExamObjective"/> per <see cref="LearningObjective"/> row, grounded in its concept (the engine matches an
/// objective without one to the closest concept by title) and assessed on its rubric criteria. Publishing writes
/// them into the exam's policy; a session's policy carries them, the exam's mode and the student's extra time.
/// </summary>
public static class SessionObjectives
{
    /// <summary>The objectives in the instructor's order. An empty list means one implicit objective per concept.</summary>
    public static IReadOnlyList<ExamObjective> From(IEnumerable<LearningObjective> objectives) =>
        objectives.OrderBy(o => o.Ordinal).Select(o => new ExamObjective
        {
            Key = o.Key,
            Title = o.Title,
            Description = o.Description ?? "",
            ConceptKeys = string.IsNullOrWhiteSpace(o.ConceptKey) ? [] : [o.ConceptKey],
            Weight = o.Weight,
            Criteria = o.Criteria.OrderBy(c => c.Ordinal)
                .Select(c => new ObjectiveCriterion(c.Name, c.Dimension, c.Weight, c.Description)).ToList(),
            HintLadder = HintLadder(o),
        }).ToList();

    /// <summary>The objective's fixed hint ladder (GAD-T271); empty (the engine's default ladder) when none or unreadable.</summary>
    public static IReadOnlyList<string> HintLadder(LearningObjective o)
    {
        try
        {
            return (System.Text.Json.JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(o.HintLadderJson) ? "[]" : o.HintLadderJson) ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>The exam's objectives from SQL (with their criteria).</summary>
    public static async Task<IReadOnlyList<ExamObjective>> LoadAsync(GadflyDbContext db, Guid organizationId, Guid examId, CancellationToken ct) =>
        From(await db.LearningObjectives.AsNoTracking().Include(o => o.Criteria)
            .Where(o => o.ExamId == examId && o.OrganizationId == organizationId).ToListAsync(ct));

    /// <summary>
    /// The policy a session runs under: the exam's policy with its objectives (loaded when the stored policy has none,
    /// e.g. an exam published before objectives were written into it), the exam's assessment mode, and the
    /// assignment's extra-time multiplier (an accommodation already in the policy is kept).
    /// </summary>
    public static async Task<SessionPolicy> ForSessionAsync(GadflyDbContext db, SessionPolicy policy, Exam exam, decimal? timeMultiplier, CancellationToken ct)
    {
        var objectives = policy.Objectives.Count > 0 ? policy.Objectives : await LoadAsync(db, exam.OrganizationId, exam.Id, ct);
        var multiplier = policy.TimeMultiplier > 1.0 ? policy.TimeMultiplier : (double)Math.Clamp(timeMultiplier ?? 1m, 1m, 4m);
        return policy with { Objectives = objectives, Mode = exam.AssessmentMode, TimeMultiplier = multiplier };
    }
}
