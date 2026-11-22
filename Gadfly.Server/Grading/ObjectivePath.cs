using System.Globalization;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Sources.Grading;

namespace Gadfly.Server.Grading;

/// <summary>How many times one kind of question was asked on an objective.</summary>
public sealed record PathMove(MoveKind Move, string Label, int Count);

/// <summary>One rubric criterion and whether the evidence reached it (a criterion left at Level 0 is out of the score).</summary>
public sealed record PathCriterion(string Name, decimal WeightShare, bool Reached, string Level);

/// <summary>One answer the level does not rest on, and why (turn numbers count from 1, as on the review page).</summary>
public sealed record PathExclusion(int Turn, string Reason);

/// <summary>
/// The path an objective took (GAD-T435; ElenchusAtScale Proposition 3.4(c), M10, Sections 3.6 and 4.6.2): the questions asked
/// and their kinds, the rubric criteria the score covers and those it leaves out (criteria at Level 0 are left out of the score
/// and the rest reweighted, so a short path is scored on fewer criteria), and every answer excluded, by reason. Without it a
/// long, hard path and a short, easy one look the same beside the level.
/// <para>
/// Reviewers only, never students. <see cref="Criteria"/> and <see cref="Exclusions"/> come from the panel's evaluation and its
/// recitation check, so they are null while blind grading hides the AI's view; the moves are the examiner's own choices and are
/// shown to every reviewer, like the follow-up count before them. Nothing here changes a score: it is read from what is stored.
/// </para>
/// </summary>
public sealed record ObjectivePath(int Questions, int FollowUps, IReadOnlyList<PathMove> Moves,
    IReadOnlyList<PathCriterion>? Criteria, IReadOnlyList<PathExclusion>? Exclusions)
{
    /// <summary>Share (0..1) of the objective's rubric weight the reached criteria carry; null when criteria aren't shown.</summary>
    public decimal? Coverage => Criteria is { Count: > 0 } c ? c.Where(x => x.Reached).Sum(x => x.WeightShare) : null;

    public static readonly string Recited = "read as recited, not as the student's own understanding";
    public static readonly string Leaked = "came after a question that gave the answer away";
    public static readonly string AssistedReason = "given after a hint or guided explanation";
    public static readonly string Unjudged = "the panel gave no usable verdict; a person grades it";

    /// <summary>The path of every objective in a stored transcript, by objective key; empty when the transcript is unreadable.</summary>
    public static IReadOnlyDictionary<string, ObjectivePath> ForSession(ExamSession session, IReadOnlyList<ObjectiveView> objectives, bool aiVisible,
        bool assistedCounts)
    {
        if (string.IsNullOrWhiteSpace(session.TranscriptJson)) return new Dictionary<string, ObjectivePath>();
        SocraticSession transcript;
        try { transcript = SessionTranscriptSerializer.Deserialize(session.TranscriptJson); }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return new Dictionary<string, ObjectivePath>();
        }
        return objectives.ToDictionary(o => o.Objective.Key,
            o => Build(transcript, o.Objective.Key, o.Objective.Criteria.ToList(), aiVisible ? o.Ai : null, aiVisible, assistedCounts),
            StringComparer.Ordinal);
    }

    /// <summary>One objective's path. Pure: the same transcript and grade give the same path.</summary>
    public static ObjectivePath Build(SocraticSession transcript, string objectiveKey, IReadOnlyList<RubricCriterion> rubric, AiGradeView? ai,
        bool aiVisible, bool assistedCounts)
    {
        var turns = ObjectiveAssessor.TurnsOn(transcript, objectiveKey);
        var moves = turns.Where(t => t.Move != MoveKind.Close).GroupBy(t => t.Move)
            .Select(g => new PathMove(g.Key, Label(g.Key), g.Count()))
            .OrderBy(m => Order(m.Move)).ToList();
        var followUps = turns.Count(t => MoveProfiles.IsFollowUp(t.Move));
        if (!aiVisible) return new ObjectivePath(turns.Count(t => t.Move != MoveKind.Close), followUps, moves, null, null);
        return new ObjectivePath(turns.Count(t => t.Move != MoveKind.Close), followUps, moves, CriteriaOf(rubric, ai),
            ExclusionsOf(transcript, turns, objectiveKey, assistedCounts));
    }

    /// <summary>
    /// The rubric's criteria in order, each with its share of the weight and whether the panel found evidence for it (a level
    /// above 0). With no panel grade yet, null; a panel that was not asked (no usable evidence) reached none.
    /// </summary>
    public static IReadOnlyList<PathCriterion>? CriteriaOf(IReadOnlyList<RubricCriterion> rubric, AiGradeView? ai)
    {
        if (ai is null || rubric.Count == 0) return null;
        var ordered = rubric.OrderBy(c => c.Ordinal).ToList();
        var weights = ordered.Select(c => c.Weight > 0 ? c.Weight : 0m).ToList();
        if (weights.Sum() <= 0) weights = ordered.Select(_ => 1m).ToList();
        var total = weights.Sum();
        return ordered.Select((c, i) =>
        {
            var grade = ai.Criteria.FirstOrDefault(g => string.Equals(g.Criterion, c.Name, StringComparison.OrdinalIgnoreCase));
            var level = grade?.Level ?? MasteryLevel.InsufficientEvidence;
            return new PathCriterion(c.Name, Math.Round(weights[i] / total, 4), level != MasteryLevel.InsufficientEvidence, MasteryLevels.Label(level));
        }).ToList();
    }

    /// <summary>
    /// Every answer on the objective that the level does not rest on, with each reason that applies. A confrontation and a
    /// closing turn are not evidence for a level, so they are not exclusions. Assisted answers are listed only when the grading
    /// policy leaves them out.
    /// </summary>
    public static IReadOnlyList<PathExclusion> ExclusionsOf(SocraticSession transcript, IReadOnlyList<Turn> turns, string objectiveKey, bool assistedCounts)
    {
        var list = new List<PathExclusion>();
        var leak = ObjectiveAssessor.FirstLeak(transcript, objectiveKey);
        foreach (var t in turns)
        {
            if (t.Answer is null || t.Judgment is null || t.Move is MoveKind.Contradiction or MoveKind.Close) continue;
            var reasons = new List<string>();
            if (ObjectiveAssessor.IsUnjudged(t.Judgment)) reasons.Add(Unjudged);
            else
            {
                if (ObjectiveAssessor.IsRecited(t)) reasons.Add(Recited);
                if (leak is { } l && t.Index >= l && (!t.IsReplacement || t.Leak is not null)) reasons.Add(Leaked);
                if (!assistedCounts && (ObjectiveAssessor.IsAssisted(transcript, t, objectiveKey) || MoveProfiles.IsAssistance(t.Move)))
                    reasons.Add(AssistedReason);
            }
            if (reasons.Count > 0) list.Add(new PathExclusion(t.Index + 1, string.Join("; ", reasons)));
        }
        return list;
    }

    /// <summary>"3 questions (2 follow-ups): opening question ×1, probe the reasoning ×2".</summary>
    public string MovesLine()
    {
        if (Questions == 0) return "not examined (no questions were asked on this objective).";
        var head = $"{Questions} question{(Questions == 1 ? "" : "s")} ({FollowUps} follow-up{(FollowUps == 1 ? "" : "s")})";
        return Moves.Count == 0 ? head : head + ": " + string.Join(", ", Moves.Select(m => $"{m.Label} ×{m.Count}"));
    }

    /// <summary>"Reached 2 of 3 criteria (70% of the weight); not reached: Application", or null when criteria aren't shown.</summary>
    public string? CriteriaLine()
    {
        if (Criteria is not { Count: > 0 } c || Coverage is not { } cov) return null;
        var reached = c.Count(x => x.Reached);
        var line = $"Reached {reached} of {c.Count} criteria ({(cov * 100m).ToString("0", CultureInfo.InvariantCulture)}% of the weight)";
        var missed = c.Where(x => !x.Reached).Select(x => x.Name).ToList();
        if (missed.Count > 0)
            line += $"; not reached: {string.Join(", ", missed)}. The score covers only the criteria reached (no evidence is not a low score, so the rest are reweighted)";
        return line + ".";
    }

    /// <summary>"turn 4 (read as recited, ...); turn 6 (...).", "none: every answer counted.", or null when not shown.</summary>
    public string? ExclusionsLine() => Exclusions is null ? null
        : Exclusions.Count == 0 ? "none: every answer counted."
        : string.Join("; ", Exclusions.Select(e => $"turn {e.Turn} ({e.Reason})")) + ".";

    /// <summary>Plain name of a question kind, for reviewers.</summary>
    public static string Label(MoveKind move) => move switch
    {
        MoveKind.Elicit => "opening question",
        MoveKind.Clarify => "clarify",
        MoveKind.Probe => "probe the reasoning",
        MoveKind.CausalReasoning => "cause and effect",
        MoveKind.Counterexample => "counterexample",
        MoveKind.Contradiction => "contradiction put to the student",
        MoveKind.Apply => "apply to a new situation",
        MoveKind.Synthesize => "connect two objectives",
        MoveKind.ScenarioVariation => "change one condition",
        MoveKind.AssumptionTesting => "test an assumption",
        MoveKind.IndependentExplanation => "explain in own reasoning",
        MoveKind.Hint => "hint (assisted)",
        MoveKind.GuidedExplanation => "guided explanation (assisted)",
        _ => move.ToString(),
    };

    /// <summary>The opening first, then the rest in their ladder order.</summary>
    private static int Order(MoveKind move) => move == MoveKind.Elicit ? -1 : (int)move;
}
