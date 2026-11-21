namespace Gadfly.Assessment;

/// <summary>
/// Why one objective's grade goes to reconciliation even though the AI-human difference alone would not send it
/// (GAD-T270; research note 05, Synthesis). A design proposal rather than a sourced rule: judgements are least
/// stable near a cut and when the panel is split (Wang2024), and routing only the most uncertain items to humans
/// (about 20% in Wang2024) recovered most of the benefit. Tune the policy's limits with calibration data.
/// </summary>
public static class ReconciliationTriggers
{
    public const string PanelSpread = "panel-spread";
    public const string Boundary = "boundary";

    public static string Describe(string code) => code switch
    {
        PanelSpread => "The panel members disagree among themselves by more than the allowed number of rubric levels.",
        Boundary => "The score sits on a pass/fail or grade boundary.",
        _ => code,
    };

    /// <summary>
    /// The triggers that fire for one combined objective result. <paramref name="panelLevels"/> are the panel
    /// members' rubric levels for that objective (empty when unknown). An objective without both scores fires none.
    /// </summary>
    public static IReadOnlyList<string> Evaluate(ObjectiveResult result, IReadOnlyCollection<int> panelLevels, GradingPolicyRules policy)
    {
        var fired = new List<string>();
        if (panelLevels.Count >= 2 && panelLevels.Max() - panelLevels.Min() > policy.MaxPanelSpreadLevels) fired.Add(PanelSpread);
        if (result is { AiScore: { } ai, HumanScore: { } human, Combined: { } combined } && result.MaxScore > 0
            && OnBoundary(ai / result.MaxScore * 100m, human / result.MaxScore * 100m, combined / result.MaxScore * 100m, policy))
            fired.Add(Boundary);
        return fired;
    }

    /// <summary>The two raters fall on opposite sides of a boundary, or the combined score is within the margin of one.</summary>
    public static bool OnBoundary(decimal aiPercent, decimal humanPercent, decimal combinedPercent, GradingPolicyRules policy) =>
        (policy.ScoreBoundaries ?? []).Any(b => (aiPercent < b) != (humanPercent < b) || Math.Abs(combinedPercent - b) < policy.BoundaryMarginPoints);
}
