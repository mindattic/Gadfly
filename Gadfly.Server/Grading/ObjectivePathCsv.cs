using System.Globalization;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Sources.Grading;

namespace Gadfly.Server.Grading;

/// <summary>
/// The path each objective took (<see cref="ObjectivePath"/>, GAD-T435) as CSV cells, for the reviewer exports (GAD-T461): the
/// assessment report CSV (one row per objective) and the review turns CSV (one row per turn). It only formats the path it is
/// given, so it hides exactly what the path hides: while blind grading keeps the AI's view from a grader, the path has no
/// criteria or exclusions and those cells are empty, leaving only the questions. Students never get a path, so never these cells.
/// </summary>
public static class ObjectivePathCsv
{
    /// <summary>Columns added to the assessment report CSV, after the objective's own columns.</summary>
    public static readonly string[] ObjectiveHeaders =
        ["path_questions", "path_follow_ups", "path_moves", "criteria_reached", "criteria_not_reached", "coverage_percent", "exclusions"];

    /// <summary>Columns added to the review turns CSV: the turn's objective path and whether this turn's answer counted.</summary>
    public static readonly string[] TurnHeaders = ["objective_path", "objective_coverage", "turn_excluded"];

    /// <summary>
    /// One objective's cells, in <see cref="ObjectiveHeaders"/> order. All empty without a path (a student, or no readable
    /// transcript); the last four empty while blind. Exclusions read "none" when every answer counted, so an empty cell always
    /// means "not shown", never "nothing excluded".
    /// </summary>
    public static string?[] ObjectiveCells(ObjectivePath? path)
    {
        if (path is null) return new string?[ObjectiveHeaders.Length];
        var ci = CultureInfo.InvariantCulture;
        var moves = string.Join(" | ", path.Moves.Select(m => $"{m.Label}={m.Count.ToString(ci)}"));
        var shown = path.Criteria is not null || path.Exclusions is not null;
        return
        [
            path.Questions.ToString(ci),
            path.FollowUps.ToString(ci),
            moves,
            path.Criteria is { Count: > 0 } c ? string.Join(" | ", c.Where(x => x.Reached).Select(x => x.Name)) : null,
            path.Criteria is { Count: > 0 } m ? string.Join(" | ", m.Where(x => !x.Reached).Select(x => x.Name)) : null,
            path.Coverage is { } cov ? (cov * 100m).ToString("0", ci) : null,
            !shown || path.Exclusions is null ? null
                : path.Exclusions.Count == 0 ? "none"
                : string.Join(" | ", path.Exclusions.Select(e => $"turn {e.Turn.ToString(ci)}: {e.Reason}")),
        ];
    }

    /// <summary>
    /// One turn's cells, in <see cref="TurnHeaders"/> order: its objective's question summary and coverage line, and whether this
    /// turn's answer was excluded ("no", or "yes: " and the reason). Coverage and exclusion are empty while blind.
    /// <paramref name="turnIndex"/> is the stored zero-based index (the path numbers turns from 1, as the review page does).
    /// </summary>
    public static string?[] TurnCells(ObjectivePath? path, int turnIndex)
    {
        if (path is null) return new string?[TurnHeaders.Length];
        string? excluded = path.Exclusions is null ? null
            : path.Exclusions.FirstOrDefault(e => e.Turn == turnIndex + 1) is { } e ? "yes: " + e.Reason : "no";
        return [path.MovesLine(), path.CriteriaLine(), excluded];
    }

    /// <summary>
    /// The review turns CSV's path cells for one session, read through the grading workspace exactly as the review page reads them
    /// (<c>ObjectivePaths.razor</c>), so blind grading hides the same things. Null workspace (the viewer may not open grading, or
    /// no such session): every turn's cells are empty. The result maps a stored zero-based turn index to its cells.
    /// </summary>
    public static Func<int, string?[]> ForReview(GradingWorkspace? w)
    {
        if (w is null) return _ => new string?[TurnHeaders.Length];
        var paths = ObjectivePath.ForSession(w.Session, w.Objectives, w.AiVisible, w.Policy.AllowAssistedEvidence);
        var objectiveOf = TurnObjectives(w.Session);
        return index => TurnCells(objectiveOf.TryGetValue(index, out var key) ? paths.GetValueOrDefault(key) : null, index);
    }

    /// <summary>Each transcript turn's objective key by its zero-based index; empty when the transcript is missing or unreadable.</summary>
    public static IReadOnlyDictionary<int, string> TurnObjectives(ExamSession session)
    {
        if (string.IsNullOrWhiteSpace(session.TranscriptJson)) return new Dictionary<int, string>();
        try
        {
            return SessionTranscriptSerializer.Deserialize(session.TranscriptJson).Turns
                .GroupBy(t => t.Index).ToDictionary(g => g.Key, g => ExamObjectives.KeyOf(g.Last()));
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return new Dictionary<int, string>();
        }
    }
}
