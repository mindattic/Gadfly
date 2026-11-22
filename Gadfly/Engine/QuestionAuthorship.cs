namespace Gadfly.Engine;

/// <summary>
/// Who wrote a question (GAD-T431; ElenchusAtScale Sections 3.8, 5.4.2, M12; Panickssery et al. 2024). The same providers draft
/// the questions and grade the answers, so a grader could favour answers to questions its own family wrote. Recording the
/// author of every asked question makes that testable (<c>Gadfly.Assessment.SelfPreference</c>).
/// </summary>
/// <param name="How">
/// <see cref="PanelDraft"/> (the panel's consensus was one or more members' own draft), <see cref="ChosenDraft"/> (the panel
/// voted for one member's draft), <see cref="JudgeSynthesis"/> (the judge fused the drafts), or a fixed source
/// (<see cref="InstructorText"/>, <see cref="HintLadder"/>, <see cref="FallbackText"/>), which no model wrote.
/// </param>
/// <param name="Providers">The provider ids (families) that wrote it; empty for a fixed source.</param>
/// <param name="Models">The model id of each provider, in the same order ("" when unknown).</param>
public sealed record QuestionAuthorship(string How, IReadOnlyList<string> Providers, IReadOnlyList<string> Models)
{
    public const string PanelDraft = "panel-draft";
    public const string ChosenDraft = "chosen-draft";
    public const string JudgeSynthesis = "judge-synthesis";
    public const string InstructorText = "instructor-text";
    public const string HintLadder = "hint-ladder";
    public const string FallbackText = "fallback";

    /// <summary>A question no model wrote.</summary>
    public static QuestionAuthorship Fixed(string how) => new(how, [], []);

    /// <summary>Whether a model wrote it (so it counts in the self-preference check).</summary>
    public bool ModelWritten => Providers.Count > 0;

    /// <summary>The provider families, lower-case.</summary>
    public IEnumerable<string> Families => Providers.Select(p => p.Trim().ToLowerInvariant()).Distinct();
}
