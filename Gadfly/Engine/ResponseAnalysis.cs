using Gadfly.Assessment;

namespace Gadfly.Engine;

/// <summary>Which way a piece of evidence points for an objective (maps to the server's EvidenceStance).</summary>
public enum EvidencePolarity { Supports, Contradicts, Misconception, Uncertain }

/// <summary>A claim the student asserted, in their exact words and restated plainly.</summary>
public sealed record AnalysisClaim(string Quote, string Text);

/// <summary>A misconception the panel found in an answer, for one objective, quoting the student.</summary>
public sealed record AnalysisMisconception(string ObjectiveKey, string Text, string Quote);

/// <summary>
/// One piece of evidence about one objective (GAD-T91, Component D): the student's exact words, which way
/// they point, and the Part 5.1 dimension they bear on. <see cref="Support"/> is how many voters found it.
/// The quote is always a verbatim span of the student's answer; the analyzer drops anything else.
/// </summary>
public sealed record ObjectiveEvidence(string ObjectiveKey, EvidencePolarity Polarity, AssessmentDimension Dimension, string Quote,
    string Note, int Support = 1);

/// <summary>
/// The panel's schema-validated analysis of one answer (GAD-T91, Component B): claims, misconceptions,
/// unresolved uncertainties, and evidence for or against each objective. Merged across voters by majority.
/// </summary>
public sealed record ResponseAnalysis
{
    public IReadOnlyList<AnalysisClaim> Claims { get; init; } = [];
    public IReadOnlyList<AnalysisMisconception> Misconceptions { get; init; } = [];
    /// <summary>What remains unclear about the answer's meaning (a reason to ask a neutral clarification).</summary>
    public IReadOnlyList<string> Uncertainties { get; init; } = [];
    public IReadOnlyList<ObjectiveEvidence> Evidence { get; init; } = [];
    /// <summary>A majority judged that the question itself gave away expected answer points (Scenario I).</summary>
    public bool QuestionRevealedAnswer { get; init; }
    /// <summary>Voters whose output passed schema validation.</summary>
    public int Voters { get; init; }
    /// <summary>Voters whose output was malformed and rejected.</summary>
    public int RejectedVotes { get; init; }
    /// <summary>Why each rejected voter's output failed validation ("provider: reason"), for the audit trail.</summary>
    public IReadOnlyList<string> Rejections { get; init; } = [];
    /// <summary>Items dropped because their quote was not the student's words (never fabricate evidence).</summary>
    public int DroppedItems { get; init; }
    /// <summary>True when produced deterministically (e.g. an honest "I don't know"), not by the panel.</summary>
    public bool Deterministic { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Voters > 0 || Deterministic;
}

/// <summary>Who found that a question (or a clarification) revealed the answer.</summary>
public enum LeakSource
{
    /// <summary>The post-answer panel analysis judged the question gave expected points away.</summary>
    Analysis,
    /// <summary>The leak guard could not decide and the question was asked anyway.</summary>
    GuardFailedOpen,
    /// <summary>A clarification given during response verification revealed expected points.</summary>
    Clarification,
    /// <summary>A human reviewer marked it.</summary>
    Reviewer,
}

/// <summary>
/// The record that a turn's question (or a clarification on it) leaked rubric content (GAD-T92, Scenario I).
/// Every answer on the same objective from this turn on is contaminated: it never counts as independent mastery.
/// </summary>
public sealed record LeakFlag(LeakSource Source, string Reason, DateTimeOffset FlaggedAt);
