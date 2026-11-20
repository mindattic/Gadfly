namespace Gadfly.Engine;

/// <summary>
/// Elo-style per-concept mastery estimate. The estimate is the probability-like ability on a 0..1
/// scale; each judged answer is a "match" against the question's difficulty. A strong answer to a
/// hard question moves the estimate more than a strong answer to an easy one, and the step size
/// shrinks as evidence accumulates on the concept.
/// </summary>
public static class MasteryModel
{
    /// <summary>Estimate before any evidence.</summary>
    public const double Prior = 0.5;

    /// <summary>
    /// Difficulty (0..1) of a question for <paramref name="move"/> under policy difficulty 1-5.
    /// Opening questions are easiest; transfer and contradiction are hardest.
    /// </summary>
    public static double QuestionDifficulty(MoveKind move, int policyDifficulty)
    {
        var moveBase = move switch
        {
            MoveKind.Elicit => 0.35,
            MoveKind.Clarify => 0.4,
            MoveKind.Probe => 0.5,
            MoveKind.Counterexample => 0.6,
            MoveKind.Synthesize => 0.65,
            MoveKind.Apply => 0.7,
            MoveKind.Contradiction => 0.7,
            _ => 0.5,
        };
        var shift = (Math.Clamp(policyDifficulty, 1, 5) - 3) * 0.05;
        return Math.Clamp(moveBase + shift, 0.05, 0.95);
    }

    /// <summary>Expected score for ability <paramref name="estimate"/> against <paramref name="difficulty"/> (logistic).</summary>
    public static double Expected(double estimate, double difficulty) =>
        1.0 / (1.0 + Math.Exp(-6.0 * (estimate - difficulty)));

    /// <summary>
    /// The new estimate after an answer judged <paramref name="score"/> (0..1) on a question of
    /// <paramref name="difficulty"/>, given <paramref name="priorTurns"/> already judged on the concept.
    /// </summary>
    public static double Update(double estimate, double score, double difficulty, int priorTurns)
    {
        var k = 0.45 / (1.0 + 0.35 * Math.Max(0, priorTurns));
        var surprise = Math.Clamp(score, 0, 1) - Expected(estimate, difficulty);
        // A hard question answered well is stronger evidence than an easy one, and vice versa.
        var weight = surprise >= 0 ? 0.5 + difficulty : 1.5 - difficulty;
        return Math.Clamp(estimate + k * weight * surprise, 0, 1);
    }
}
