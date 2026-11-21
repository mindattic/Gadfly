namespace Gadfly.Engine;

/// <summary>One rung of an objective's hint ladder as given to a student (GAD-T271).</summary>
/// <param name="Step">1-based position on the ladder.</param>
/// <param name="Of">How many rungs the ladder has.</param>
/// <param name="Text">What the examiner says: the rung, then the fixed retry question.</param>
/// <param name="InstructorWritten">False when the objective has no ladder of its own and the default ladder was used.</param>
public sealed record HintRung(int Step, int Of, string Text, bool InstructorWritten);

/// <summary>
/// Learning Mode's fixed hint ladder (GAD-T271; research note 04, EVIDENCE_MAP.md mechanic 8). Interventionist dynamic
/// assessment needs the same graduated prompts for every student (Caffrey 2008): the instructor writes the rungs per
/// objective, weakest first, and every student who needs help gets them in that order. The planner decides when a hint is
/// given (only after a weak answer or a misconception); no model ever writes or rewords a hint, so the number of hints a
/// student needed means the same thing for every student. An objective without its own ladder uses
/// <see cref="Default"/>, which is equally fixed.
/// <para>
/// The hints given are reported beside the unaided level, never folded into it, and Gadfly computes no gain, help-needed or
/// "learning potential" score from them: those are confounded with the starting level (De Beer 2006) and remain
/// experimental. See <see cref="ObjectiveReport.HintsGiven"/>.
/// </para>
/// </summary>
public static class HintLadders
{
    /// <summary>Most rungs an instructor's ladder may have (the policy allows at most five assists per objective).</summary>
    public const int MaxRungs = 5;

    /// <summary>Longest rung, in characters.</summary>
    public const int MaxRungLength = 600;

    /// <summary>The fixed ladder for an objective the instructor gave none: the same three rungs for every student.</summary>
    public static IReadOnlyList<string> Default(string objectiveTitle) =>
    [
        $"Here is a hint: think about what {objectiveTitle} needs in order to happen, and what it leads to.",
        $"Let's take {objectiveTitle} step by step. Start from the part of your answer you are surest of, and ask what has to come before it and what follows from it.",
        $"One more hint: name the single most important idea in {objectiveTitle}, then explain how each other part of your answer depends on it.",
    ];

    /// <summary>The ladder an objective's hints come from: the instructor's rungs, else <see cref="Default"/>.</summary>
    public static IReadOnlyList<string> For(ExamObjective? objective, string objectiveTitle) =>
        objective is { HintLadder.Count: > 0 } o ? o.HintLadder : Default(objectiveTitle);

    /// <summary>How many hints an objective can get: the policy's limit, capped by the ladder's length (rungs are never repeated).</summary>
    public static int MaxAssists(SessionPolicy policy, ExamObjective? objective, string objectiveTitle) =>
        Math.Min(policy.MaxAssistsPerObjective, For(objective, objectiveTitle).Count);

    /// <summary>
    /// The rung for the next assistance on an objective that has had <paramref name="assistsGiven"/> already, with the fixed
    /// retry question for <paramref name="move"/> appended (a rung that already ends in a question is said as written).
    /// Past the top of the ladder the last rung is repeated.
    /// </summary>
    public static HintRung Rung(ExamObjective? objective, string objectiveTitle, int assistsGiven, MoveKind move)
    {
        var ladder = For(objective, objectiveTitle);
        var index = Math.Clamp(assistsGiven, 0, ladder.Count - 1);
        var rung = ladder[index].Trim();
        var text = rung.EndsWith('?') ? rung : $"{rung} {Retry(move, objectiveTitle)}";
        return new HintRung(index + 1, ladder.Count, text, objective is { HintLadder.Count: > 0 });
    }

    /// <summary>The fixed question after a rung: the student tries again (Hint) or is reassessed (GuidedExplanation).</summary>
    public static string Retry(MoveKind move, string objectiveTitle) => move == MoveKind.GuidedExplanation
        ? "Now, in your own words, which step comes first, and why?"
        : $"With that in mind, how would you explain {objectiveTitle} now?";

    /// <summary>
    /// An instructor's ladder from text, one rung per line: blank lines dropped, each line trimmed. Null with the reason when
    /// it has more than <see cref="MaxRungs"/> rungs or a rung longer than <see cref="MaxRungLength"/>.
    /// </summary>
    public static (IReadOnlyList<string>? Ladder, string? Error) Parse(string? text)
    {
        var rungs = (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (rungs.Count > MaxRungs) return (null, $"A hint ladder has at most {MaxRungs} rungs (one per line).");
        if (rungs.Any(r => r.Length > MaxRungLength)) return (null, $"Keep each hint under {MaxRungLength} characters.");
        return (rungs, null);
    }
}
