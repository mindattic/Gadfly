namespace Gadfly.Assessment;

/// <summary>
/// An institution's grading policy for one assessment (Parts 9.3, 10.2). Approved before the examination
/// begins; weights are fractions that sum to 1.
/// </summary>
public sealed record GradingPolicyRules
{
    public decimal AiWeight { get; init; } = 0.4m;
    public decimal HumanWeight { get; init; } = 0.6m;
    /// <summary>Differences below this (in percentage points of the objective's maximum) are standard review.</summary>
    public decimal AdditionalReviewAt { get; init; } = 10m;
    /// <summary>Differences at or above this require reconciliation and block finalization.</summary>
    public decimal MandatoryReconciliationAt { get; init; } = 20m;
    public bool BlindHumanGrading { get; init; } = true;
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
        if (HumanWeight == 0) throw new ArgumentException("The human weight cannot be 0: a human examiner must take part in grading (Part 8.1).");
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
/// Part 9 weighted scoring and Part 10 disagreement detection, as exact decimal arithmetic.
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

    public static decimal Combine(decimal ai, decimal human, GradingPolicyRules policy) =>
        Math.Round(ai * policy.AiWeight + human * policy.HumanWeight, 4, MidpointRounding.AwayFromZero);

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
            if (o.AiScore is null || o.HumanScore is null)
            {
                missing.Add(o.ObjectiveKey);
                results.Add(new ObjectiveResult(o.ObjectiveKey, o.MaxScore, o.Weight, o.AiScore, o.HumanScore, null, null, null));
                continue;
            }
            var diff = Math.Round(Math.Abs(o.AiScore.Value - o.HumanScore.Value) / o.MaxScore * 100m, 4, MidpointRounding.AwayFromZero);
            results.Add(new ObjectiveResult(o.ObjectiveKey, o.MaxScore, o.Weight, o.AiScore, o.HumanScore,
                Combine(o.AiScore.Value, o.HumanScore.Value, policy), diff, Band(diff, policy)));
        }

        decimal? final = null;
        if (missing.Count == 0)
        {
            var totalWeight = results.Sum(r => r.Weight);
            if (totalWeight <= 0) throw new ArgumentException("Objective weights must not all be zero.");
            final = Math.Round(results.Sum(r => r.Combined!.Value / r.MaxScore * 100m * r.Weight) / totalWeight, 2, MidpointRounding.AwayFromZero);
        }
        return new CombinedResult(results, final,
            results.Any(r => r.Band == DisagreementBand.MandatoryReconciliation),
            results.Any(r => r.Band == DisagreementBand.AdditionalReview),
            missing);
    }
}
