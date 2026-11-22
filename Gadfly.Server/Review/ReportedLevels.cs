using System.Text.Json;

namespace Gadfly.Server.Review;

/// <summary>One learning objective's corroborated level from a session report: 1-5, or 0 for insufficient evidence.</summary>
public sealed record ObjectiveLevel(string Title, int Level, string Label)
{
    public bool Insufficient => Level == 0;
}

/// <summary>
/// What a session's result is before a person has graded it (GAD-T425; ElenchusAtScale Prop. 3.3, Prop. 3.4(b), M9, M10,
/// Section 4.5): the corroborated level of each learning objective, with Level 0 meaning the evidence was insufficient, never
/// zero understanding. The examiner's running mastery estimate (MasteryModel) and the mean of the judged answer scores only
/// steer which question comes next; they are never shown as the student's score, and no average stands in for a level.
/// Reports written before objectives existed have no levels, and then there is nothing to show.
/// </summary>
public static class ReportedLevels
{
    /// <summary>How the running estimate is labelled wherever it is still shown.</summary>
    public const string SteeringAidLabel = "Examiner's running estimate (steers the questions; not a score)";

    /// <summary>The per-objective levels in a stored report's JSON; empty when there are none or the JSON can't be read.</summary>
    public static IReadOnlyList<ObjectiveLevel> FromReportJson(string? reportJson)
    {
        if (string.IsNullOrWhiteSpace(reportJson)) return [];
        try
        {
            return From(Gadfly.Engine.SessionReport.FromJson(reportJson));
        }
        catch (JsonException)
        {
            return [];
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    public static IReadOnlyList<ObjectiveLevel> From(Gadfly.Engine.SessionReport? report) =>
        report?.Objectives.Select(o => new ObjectiveLevel(string.IsNullOrWhiteSpace(o.Title) ? o.ObjectiveKey : o.Title,
            Math.Clamp(o.Level, 0, 5), string.IsNullOrWhiteSpace(o.LevelLabel) ? (o.Level == 0 ? "Insufficient evidence" : $"Level {o.Level}") : o.LevelLabel)).ToList()
        ?? [];

    /// <summary>A short form for a list cell, e.g. "Levels 4, 2, 0 (1 with insufficient evidence)"; null when there are no levels.</summary>
    public static string? Short(IReadOnlyList<ObjectiveLevel> levels)
    {
        if (levels.Count == 0) return null;
        var word = levels.Count == 1 ? "Level" : "Levels";
        var insufficient = levels.Count(l => l.Insufficient);
        return $"{word} {string.Join(", ", levels.Select(l => l.Level))}"
               + (insufficient == 0 ? "" : $" ({insufficient} with insufficient evidence)");
    }

    /// <summary>Every objective with its level, e.g. "Photosynthesis: Level 4 (Application); Respiration: Level 0 (Insufficient evidence)".</summary>
    public static string? Detail(IReadOnlyList<ObjectiveLevel> levels) =>
        levels.Count == 0 ? null : string.Join("; ", levels.Select(l => $"{l.Title}: Level {l.Level} ({l.Label})"));
}
