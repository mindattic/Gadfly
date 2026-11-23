namespace Gadfly.Assessment;

/// <summary>
/// Whose evaluation is the grade (GAD-T703). Owner decision, 2026-11-23: "let the LLM do the final grade, lets see how close
/// it gets, make a human grader an option; both as single of either, or average of both". This deliberately overrides the
/// dissertation's Part 8.1 rule that a human examiner must take part in every grade. AI grading runs in every mode, so there
/// is always an AI grade to compare a human grade against.
/// </summary>
public enum GradingMode
{
    /// <summary>AI × AiWeight + human × HumanWeight per objective (Part 9), with reconciliation of disagreements (Part 10).</summary>
    Combined,
    /// <summary>The AI's grade is final and published without a person; a human grade is an optional second opinion that never changes it.</summary>
    AiOnly,
    /// <summary>The human's grade is the grade; the AI still grades, for comparison only, with no weight.</summary>
    HumanOnly,
}

/// <summary>
/// An institution's grading policy for one assessment (Parts 9.3, 10.2). Approved before the examination
/// begins; weights are fractions that sum to 1.
/// </summary>
public sealed record GradingPolicyRules
{
    /// <summary>Combined unless the exam chose otherwise (snapshots taken before GAD-T703 have no mode and stay Combined).</summary>
    public GradingMode Mode { get; init; } = GradingMode.Combined;
    public decimal AiWeight { get; init; } = 0.4m;
    public decimal HumanWeight { get; init; } = 0.6m;
    /// <summary>Differences below this (in percentage points of the objective's maximum) are standard review.</summary>
    public decimal AdditionalReviewAt { get; init; } = 10m;
    /// <summary>Differences at or above this require reconciliation and block finalization.</summary>
    public decimal MandatoryReconciliationAt { get; init; } = 20m;
    public bool BlindHumanGrading { get; init; } = true;
    /// <summary>
    /// Anonymous human marking (GAD-T439; ElenchusAtScale 2.9.3, Meadows &amp; Billington 2005: remove names before marking):
    /// graders see a session code instead of the student's name until the human score is submitted. Off unless the
    /// exam's policy turns it on.
    /// </summary>
    public bool AnonymousMarking { get; init; }
    /// <summary>
    /// Single-answer trigger (GAD-T270): panel members whose rubric levels for one objective differ by more than
    /// this many levels send it to reconciliation, whatever the AI-human difference.
    /// </summary>
    public int MaxPanelSpreadLevels { get; init; } = 1;
    /// <summary>
    /// Single-answer trigger (GAD-T270): pass/fail or grade boundaries, in percent of an objective's maximum. An
    /// objective goes to reconciliation when the AI and human scores fall on opposite sides of one, or the combined
    /// score is within <see cref="BoundaryMarginPoints"/> of one. Empty turns the trigger off.
    /// </summary>
    public IReadOnlyList<decimal> ScoreBoundaries { get; init; } = [50m];
    /// <summary>How close (percentage points) a combined score must be to a boundary to count as on it.</summary>
    public decimal BoundaryMarginPoints { get; init; } = 2.5m;

    /// <summary>Throws when the policy is unusable (weights not in [0,1] or not summing to 1, thresholds out of order).</summary>
    public void Validate()
    {
        if (AiWeight is < 0 or > 1 || HumanWeight is < 0 or > 1) throw new ArgumentException("Weights must be between 0 and 1.");
        if (AiWeight + HumanWeight != 1m) throw new ArgumentException($"AI and human weights must sum to 1 (they sum to {AiWeight + HumanWeight}).");
        // Part 8.1 (a human must take part) no longer holds for every exam: owner decision 2026-11-23 (GAD-T703). Combined still
        // means both evaluations count; an exam where only the AI counts chooses AiOnly.
        if (Mode == GradingMode.Combined && HumanWeight == 0) throw new ArgumentException("In combined grading the human weight cannot be 0: choose AI-only grading instead.");
        if (!Enum.IsDefined(Mode)) throw new ArgumentException("Unknown grading mode.");
        if (AdditionalReviewAt < 0 || MandatoryReconciliationAt <= 0) throw new ArgumentException("Thresholds must be positive.");
        if (AdditionalReviewAt > MandatoryReconciliationAt) throw new ArgumentException("The additional-review threshold must not exceed the reconciliation threshold.");
        if (MaxPanelSpreadLevels < 0) throw new ArgumentException("The panel spread limit cannot be negative.");
        if ((ScoreBoundaries ?? []).Any(b => b <= 0 || b >= 100)) throw new ArgumentException("Score boundaries must be between 0 and 100 percent.");
        if (BoundaryMarginPoints < 0) throw new ArgumentException("The boundary margin cannot be negative.");
    }
}

/// <summary>How far apart the AI and human scores are (Part 10.2).</summary>
public enum DisagreementBand { Standard, AdditionalReview, MandatoryReconciliation }

/// <summary>One learning objective's scores, all on the objective's own scale (0..MaxScore).</summary>
public sealed record ObjectiveScores(string ObjectiveKey, decimal MaxScore, decimal Weight, decimal? AiScore, decimal? HumanScore);

/// <summary>The combined result of one objective.</summary>
public sealed record ObjectiveResult(string ObjectiveKey, decimal MaxScore, decimal Weight, decimal? AiScore, decimal? HumanScore,
    decimal? Combined, decimal? DifferencePoints, DisagreementBand? Band);

/// <summary>The combined result of an examination.</summary>
public sealed record CombinedResult(IReadOnlyList<ObjectiveResult> Objectives, decimal? FinalPercent, bool RequiresReconciliation,
    bool HasAdditionalReviewFlags, IReadOnlyList<string> MissingScores);

/// <summary>
/// Part 9 weighted scoring and Part 10 disagreement detection, as exact decimal arithmetic. Under
/// <see cref="GradingMode.AiOnly"/> or <see cref="GradingMode.HumanOnly"/> the "combined" score is that one evaluation's.
/// Combined = AI × AIWeight + Human × HumanWeight per objective; the examination percentage is the
/// objective-weighted mean of combined/max. Differences are measured in percentage points of the
/// objective's maximum so objectives on different scales compare fairly.
/// </summary>
public static class CombinedScoring
{
    public static DisagreementBand Band(decimal differencePoints, GradingPolicyRules policy) =>
        differencePoints >= policy.MandatoryReconciliationAt ? DisagreementBand.MandatoryReconciliation
        : differencePoints >= policy.AdditionalReviewAt ? DisagreementBand.AdditionalReview
        : DisagreementBand.Standard;

    public static decimal Combine(decimal ai, decimal human, GradingPolicyRules policy) => policy.Mode switch
    {
        GradingMode.AiOnly => ai,
        GradingMode.HumanOnly => human,
        _ => Math.Round(ai * policy.AiWeight + human * policy.HumanWeight, 4, MidpointRounding.AwayFromZero),
    };

    /// <summary>The objective's grade under the policy's mode, or null when the evaluation that counts is missing.</summary>
    private static decimal? Grade(decimal? ai, decimal? human, GradingPolicyRules policy) => policy.Mode switch
    {
        GradingMode.AiOnly => ai,
        GradingMode.HumanOnly => human,
        _ => ai is { } a && human is { } h ? Combine(a, h, policy) : null,
    };

    public static CombinedResult Calculate(IReadOnlyList<ObjectiveScores> objectives, GradingPolicyRules policy)
    {
        policy.Validate();
        if (objectives.Count == 0) throw new ArgumentException("An examination needs at least one learning objective.");
        var results = new List<ObjectiveResult>();
        var missing = new List<string>();
        foreach (var o in objectives)
        {
            if (o.MaxScore <= 0) throw new ArgumentException($"Objective '{o.ObjectiveKey}' needs a positive maximum score.");
            if (o.Weight < 0) throw new ArgumentException($"Objective '{o.ObjectiveKey}' has a negative weight.");
            foreach (var s in new[] { o.AiScore, o.HumanScore })
                if (s is { } v && (v < 0 || v > o.MaxScore))
                    throw new ArgumentException($"A score for '{o.ObjectiveKey}' is outside 0..{o.MaxScore}.");
            // The difference is measured whenever both exist, in every mode: that is the AI-versus-human comparison (GAD-T703).
            decimal? diff = o.AiScore is { } ai && o.HumanScore is { } human
                ? Math.Round(Math.Abs(ai - human) / o.MaxScore * 100m, 4, MidpointRounding.AwayFromZero)
                : null;
            var grade = Grade(o.AiScore, o.HumanScore, policy);
            if (grade is null) missing.Add(o.ObjectiveKey);
            results.Add(new ObjectiveResult(o.ObjectiveKey, o.MaxScore, o.Weight, o.AiScore, o.HumanScore, grade, diff, diff is { } d ? Band(d, policy) : null));
        }

        decimal? final = null;
        if (missing.Count == 0)
        {
            var totalWeight = results.Sum(r => r.Weight);
            if (totalWeight <= 0) throw new ArgumentException("Objective weights must not all be zero.");
            final = Math.Round(results.Sum(r => r.Combined!.Value / r.MaxScore * 100m * r.Weight) / totalWeight, 2, MidpointRounding.AwayFromZero);
        }
        // Only a combined grade is reconciled: in AI-only and human-only grading the other evaluation has no weight to reconcile.
        var reconciles = policy.Mode == GradingMode.Combined;
        return new CombinedResult(results, final,
            reconciles && results.Any(r => r.Band == DisagreementBand.MandatoryReconciliation),
            reconciles && results.Any(r => r.Band == DisagreementBand.AdditionalReview),
            missing);
    }
}

/// <summary>How close the AI's levels came to a human's on one examination: "lets see how close it gets" (GAD-T703).</summary>
/// <param name="Objectives">Objectives graded by both.</param>
/// <param name="ExactLevels">Objectives where the two levels are the same.</param>
/// <param name="WithinOneLevel">Objectives where the levels differ by at most one.</param>
/// <param name="MeanLevelDifference">Mean of (AI level − human level): positive when the AI grades higher.</param>
/// <param name="Kappa">Quadratic weighted kappa (<see cref="RaterAgreement"/>, as panel calibration uses); null when undefined.</param>
public sealed record AiHumanAgreement(int Objectives, int ExactLevels, int WithinOneLevel, double MeanLevelDifference, double? Kappa)
{
    public double ExactShare => Objectives == 0 ? 0 : (double)ExactLevels / Objectives;

    /// <summary>The agreement over the objectives both graded, or null when none were.</summary>
    public static AiHumanAgreement? Of(IReadOnlyList<(MasteryLevel Ai, MasteryLevel Human)> pairs)
    {
        if (pairs.Count == 0) return null;
        var levels = pairs.Select(p => ((int)p.Ai, (int)p.Human)).ToList();
        return new AiHumanAgreement(levels.Count, levels.Count(p => p.Item1 == p.Item2), levels.Count(p => Math.Abs(p.Item1 - p.Item2) <= 1),
            levels.Average(p => (double)(p.Item1 - p.Item2)),
            RaterAgreement.QuadraticWeightedKappa(levels, (int)MasteryLevel.InsufficientEvidence, (int)MasteryLevel.TransferAndCriticalReasoning));
    }
}
