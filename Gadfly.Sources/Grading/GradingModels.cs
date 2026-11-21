using Gadfly.Assessment;

namespace Gadfly.Sources.Grading;

/// <summary>One rubric criterion of a learning objective, as the grader sees it.</summary>
public sealed record GradingCriterion(string Name, AssessmentDimension Dimension, string? Description, decimal Weight,
    IReadOnlyDictionary<MasteryLevel, string> Levels);

/// <summary>The learning objective being graded.</summary>
public sealed record GradingObjective(string Key, string Title, string? Description, decimal MaxScore, IReadOnlyList<GradingCriterion> Criteria,
    string? ConceptTitle = null, string? ConceptDefinition = null, IReadOnlyList<string>? KnownMisconceptions = null);

/// <summary>
/// One student response offered as evidence. <see cref="VerifiedText"/> is the only text the grader is shown and
/// the only text a quote may come from. Responses that may not count (contaminated, assisted, needing a human)
/// are still passed so the grader can explain what was excluded; it never sees their text.
/// <see cref="UncertainWords"/> are runs of words the speech recognizer was unsure of in a transcript the student
/// never checked (GAD-T265): they may be mis-heard, so they are not to be scored as wrong content.
/// <see cref="TranscriptCorrected"/> is true when the student corrected the recognizer's transcript (a recording fix).
/// </summary>
public sealed record EvidenceItem(Guid ResponseId, int Sequence, string Question, string VerifiedText, VerificationState State,
    bool Assisted = false, bool Contaminated = false, string? Note = null, EvidenceStanceHint? Stance = null,
    IReadOnlyList<string>? UncertainWords = null, bool TranscriptCorrected = false);

/// <summary>A reviewer-recorded stance on a piece of evidence (AssessmentEvidence), passed through as a hint.</summary>
public enum EvidenceStanceHint { Supports, Contradicts, Misconception, Uncertain }

/// <summary>Everything needed to grade one objective.</summary>
public sealed record ObjectiveGradingInput(GradingObjective Objective, IReadOnlyList<EvidenceItem> Evidence, bool AllowAssistedEvidence = false);

/// <summary>The merged evaluation of one criterion.</summary>
public sealed record CriterionGrade(string Criterion, AssessmentDimension Dimension, MasteryLevel Level, decimal Score, decimal MaxScore,
    string Rationale, IReadOnlyList<Guid> EvidenceRefs, IReadOnlyList<string> Quotes, IReadOnlyList<int> VoterLevels, bool Dissent);

/// <summary>One voter's evaluation, kept for the record (dissent is visible to reviewers).</summary>
public sealed record VoterGrade(string ProviderId, string Model, decimal Score, MasteryLevel Level, string Explanation, bool Dissents);

/// <summary>A misconception noted by the panel and how many voters noted it.</summary>
public sealed record NotedMisconception(string Text, int Voters);

/// <summary>
/// The AI's preliminary evaluation of one objective (Part 7.1). Never final: a human examiner grades too.
/// Scores are computed in code from the voters' rubric levels; the panel only judges levels and evidence.
/// </summary>
public sealed record AiObjectiveEvaluation
{
    public required string ObjectiveKey { get; init; }
    public required MasteryLevel Level { get; init; }
    public required decimal Score { get; init; }
    public required decimal MaxScore { get; init; }
    public required string Explanation { get; init; }
    public required IReadOnlyList<CriterionGrade> Criteria { get; init; }
    public required IReadOnlyList<Guid> EvidenceRefs { get; init; }
    public required IReadOnlyList<NotedMisconception> Misconceptions { get; init; }
    public required IReadOnlyList<string> Ambiguities { get; init; }
    public required IReadOnlyList<string> ReviewFlags { get; init; }
    /// <summary>A Part 5.4 category from <see cref="GradingConfidence"/>; never a percentage.</summary>
    public required string Confidence { get; init; }
    public required IReadOnlyList<VoterGrade> Voters { get; init; }
    public bool IsMock { get; init; }
    /// <summary>True when the panel produced no valid evaluation at all (the grade is Level 0 and flagged).</summary>
    public bool PanelFailed { get; init; }
    public bool IsInsufficientEvidence => Level == MasteryLevel.InsufficientEvidence;
}

/// <summary>
/// Part 5.4 confidence categories, each defined by evidence criteria (not a model's self-reported certainty).
/// </summary>
public static class GradingConfidence
{
    /// <summary>No usable verified evidence, or every criterion was judged to lack evidence.</summary>
    public const string Insufficient = "insufficient";
    /// <summary>One usable response, or unresolved transcript issues, or the voters disagreed.</summary>
    public const string Limited = "limited";
    /// <summary>Two or more usable responses, but a single voter (no independent corroboration).</summary>
    public const string Moderate = "moderate";
    /// <summary>Two or more usable responses, two or more voters agreeing within one level, no unresolved issues.</summary>
    public const string Corroborated = "corroborated";

    public static string Describe(string category) => category switch
    {
        Insufficient => "Insufficient: no usable verified evidence for this objective (Level 0 means not enough evidence, not zero understanding).",
        Limited => "Limited: a single usable response, unresolved transcript issues, or the AI voters disagreed.",
        Moderate => "Moderate: several usable responses, but only one AI voter evaluated them.",
        Corroborated => "Corroborated: several usable responses and independent AI voters agreeing within one level.",
        _ => category,
    };

    /// <summary>The category from the evidence criteria.</summary>
    public static string Categorize(int usableResponses, int validVoters, bool dissent, bool unresolvedIssues, bool allCriteriaInsufficient)
    {
        if (usableResponses == 0 || validVoters == 0 || allCriteriaInsufficient) return Insufficient;
        if (usableResponses == 1 || dissent || unresolvedIssues) return Limited;
        if (validVoters == 1) return Moderate;
        return Corroborated;
    }
}

/// <summary>Standard review-flag codes on an AI objective grade.</summary>
public static class ReviewFlags
{
    public const string InsufficientEvidence = "insufficient-evidence";
    public const string UnresolvedTranscript = "unresolved-transcript";
    public const string PanelDissent = "panel-dissent";
    public const string SingleVoter = "single-voter";
    public const string ContaminatedExcluded = "contaminated-evidence-excluded";
    public const string AssistedExcluded = "assisted-evidence-excluded";
    public const string PanelFailed = "ai-panel-failed";
    public const string Mock = "mock-panel";
    public const string ContradictingEvidence = "contradicting-evidence";
    public const string LowConfidenceSpeech = "low-confidence-speech";

    public static string Describe(string flag) => flag switch
    {
        InsufficientEvidence => "Not enough reliable evidence to judge this objective; a human must decide (Level 0 is not zero understanding).",
        UnresolvedTranscript => "Some responses on this objective have unresolved transcript or meaning questions and were not used as evidence.",
        PanelDissent => "The AI voters disagreed by two or more levels on at least one criterion.",
        SingleVoter => "Only one AI voter returned a valid evaluation.",
        ContaminatedExcluded => "Evidence given after a question that revealed the answer was excluded (it never counts as independent mastery).",
        AssistedExcluded => "Answers given after instructional help were excluded (the grading policy does not allow assisted evidence).",
        PanelFailed => "The AI panel returned no valid evaluation; this objective needs human grading alone.",
        Mock => "Produced by the mock panel: not a genuine AI evaluation.",
        ContradictingEvidence => "A reviewer marked evidence that contradicts mastery on this objective.",
        LowConfidenceSpeech => "Speech recognition was unsure of some words in the evidence and the student did not check them; they may be mis-heard, not wrong. Check them before scoring.",
        _ => flag,
    };
}
