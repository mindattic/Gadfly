namespace Gadfly.Assessment;

// Deterministic assessment rules (docs/MASTER-PLAN.md Parts 4.6, 5.3, 9, 10, 11.2). Everything here is plain
// application code with no LLM involvement: score arithmetic, thresholds and state transitions must be exact,
// reproducible and auditable. The server persists the results; NUnit tests cover every rule.

/// <summary>Part 5.3 understanding levels. Level 0 means "not enough reliable evidence", never "no understanding".</summary>
public enum MasteryLevel
{
    InsufficientEvidence = 0,
    Recognition = 1,
    BasicExplanation = 2,
    ConceptualUnderstanding = 3,
    Application = 4,
    TransferAndCriticalReasoning = 5,
}

/// <summary>Part 5.1 assessment dimensions; <see cref="Custom"/> for subject-specific ones.</summary>
public enum AssessmentDimension
{
    FactualKnowledge,
    ConceptualUnderstanding,
    LogicalReasoning,
    Application,
    KnowledgeTransfer,
    MisconceptionRecognition,
    Consistency,
    Metacognition,
    Custom,
}

/// <summary>Part 3: the two operating modes. Evidence never mixes between them without an institutional rule.</summary>
public enum AssessmentMode { Examination, Learning }

public static class MasteryLevels
{
    public static string Label(MasteryLevel level) => level switch
    {
        MasteryLevel.InsufficientEvidence => "Insufficient Evidence",
        MasteryLevel.Recognition => "Recognition",
        MasteryLevel.BasicExplanation => "Basic Explanation",
        MasteryLevel.ConceptualUnderstanding => "Conceptual Understanding",
        MasteryLevel.Application => "Application",
        MasteryLevel.TransferAndCriticalReasoning => "Transfer and Critical Reasoning",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>The default descriptor of a level (Part 5.3), used when a rubric does not define its own.</summary>
    public static string DefaultDescriptor(MasteryLevel level) => level switch
    {
        MasteryLevel.InsufficientEvidence => "The examination does not provide enough reliable evidence to determine understanding.",
        MasteryLevel.Recognition => "The student recognizes relevant concepts but cannot reliably explain them.",
        MasteryLevel.BasicExplanation => "The student explains familiar concepts with partial accuracy.",
        MasteryLevel.ConceptualUnderstanding => "The student provides accurate explanations and demonstrates an understanding of underlying relationships.",
        MasteryLevel.Application => "The student applies concepts to relevant problems and explains the reasoning.",
        MasteryLevel.TransferAndCriticalReasoning => "The student applies knowledge to unfamiliar situations, evaluates assumptions, and defends conclusions.",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public static bool IsConclusive(MasteryLevel level) => level != MasteryLevel.InsufficientEvidence;
}

/// <summary>Part 4.6 response verification states.</summary>
public enum VerificationState
{
    Captured,
    VerificationRequired,
    AwaitingStudentConfirmation,
    Confirmed,
    Corrected,
    Clarified,
    Unresolved,
    FlaggedForHumanReview,
}

/// <summary>Part 4.7 kinds of change to a response; each has different assessment implications.</summary>
public enum CorrectionKind
{
    /// <summary>The recorded text did not match what the student said. Never reduces the score.</summary>
    Transcription,
    /// <summary>The student explained what they meant. Stored beside the original, not over it.</summary>
    MeaningClarification,
    /// <summary>The student changed the substance of the answer: a new response event under the exam's rules.</summary>
    SubstantiveRevision,
}

/// <summary>Allowed response-verification transitions (Part 4.6). Anything else is refused.</summary>
public static class VerificationMachine
{
    private static readonly IReadOnlyDictionary<VerificationState, VerificationState[]> Allowed = new Dictionary<VerificationState, VerificationState[]>
    {
        [VerificationState.Captured] = [VerificationState.Confirmed, VerificationState.VerificationRequired],
        [VerificationState.VerificationRequired] = [VerificationState.AwaitingStudentConfirmation, VerificationState.FlaggedForHumanReview, VerificationState.Unresolved],
        [VerificationState.AwaitingStudentConfirmation] = [VerificationState.Confirmed, VerificationState.Corrected, VerificationState.Clarified, VerificationState.Unresolved],
        [VerificationState.Unresolved] = [VerificationState.FlaggedForHumanReview, VerificationState.AwaitingStudentConfirmation],
        [VerificationState.Confirmed] = [VerificationState.FlaggedForHumanReview],
        [VerificationState.Corrected] = [VerificationState.FlaggedForHumanReview],
        [VerificationState.Clarified] = [VerificationState.FlaggedForHumanReview],
        [VerificationState.FlaggedForHumanReview] = [VerificationState.Confirmed, VerificationState.Corrected, VerificationState.Clarified],
    };

    public static bool CanMove(VerificationState from, VerificationState to) => Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static VerificationState Move(VerificationState from, VerificationState to) =>
        CanMove(from, to) ? to : throw new InvalidStateTransitionException($"A response cannot go from {from} to {to}.");

    /// <summary>States in which the response may be used as grading evidence without a human look.</summary>
    public static bool IsVerified(VerificationState s) => s is VerificationState.Confirmed or VerificationState.Corrected or VerificationState.Clarified
        or VerificationState.Captured;

    /// <summary>States that block AI grading until a human resolves them.</summary>
    public static bool NeedsHuman(VerificationState s) => s is VerificationState.Unresolved or VerificationState.FlaggedForHumanReview;
}

/// <summary>Part 11.2 grade statuses.</summary>
public enum GradeStatus
{
    ExaminationInProgress,
    ExaminationCompleted,
    TranscriptVerificationPending,
    AiGradingPending,
    HumanReviewPending,
    ReconciliationRequired,
    AwaitingFinalApproval,
    Finalized,
    Published,
    UnderAppeal,
    Amended,
}

/// <summary>Allowed grade-status transitions (Part 11). Publication only after finalization; appeals reopen review.</summary>
public static class GradeStatusMachine
{
    private static readonly IReadOnlyDictionary<GradeStatus, GradeStatus[]> Allowed = new Dictionary<GradeStatus, GradeStatus[]>
    {
        [GradeStatus.ExaminationInProgress] = [GradeStatus.ExaminationCompleted],
        [GradeStatus.ExaminationCompleted] = [GradeStatus.TranscriptVerificationPending, GradeStatus.AiGradingPending],
        [GradeStatus.TranscriptVerificationPending] = [GradeStatus.AiGradingPending],
        [GradeStatus.AiGradingPending] = [GradeStatus.HumanReviewPending],
        [GradeStatus.HumanReviewPending] = [GradeStatus.ReconciliationRequired, GradeStatus.AwaitingFinalApproval],
        [GradeStatus.ReconciliationRequired] = [GradeStatus.AwaitingFinalApproval, GradeStatus.HumanReviewPending],
        [GradeStatus.AwaitingFinalApproval] = [GradeStatus.Finalized, GradeStatus.ReconciliationRequired, GradeStatus.HumanReviewPending],
        [GradeStatus.Finalized] = [GradeStatus.Published],
        [GradeStatus.Published] = [GradeStatus.UnderAppeal, GradeStatus.Amended],
        [GradeStatus.UnderAppeal] = [GradeStatus.Published, GradeStatus.Amended],
        [GradeStatus.Amended] = [GradeStatus.Published, GradeStatus.UnderAppeal],
    };

    public static bool CanMove(GradeStatus from, GradeStatus to) => Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static GradeStatus Move(GradeStatus from, GradeStatus to) =>
        CanMove(from, to) ? to : throw new InvalidStateTransitionException($"A grade cannot go from {Label(from)} to {Label(to)}.");

    /// <summary>A student may see the grade only in these statuses.</summary>
    public static bool IsVisibleToStudent(GradeStatus s) => s is GradeStatus.Published or GradeStatus.UnderAppeal or GradeStatus.Amended;

    public static string Label(GradeStatus s) => s switch
    {
        GradeStatus.ExaminationInProgress => "Examination In Progress",
        GradeStatus.ExaminationCompleted => "Examination Completed",
        GradeStatus.TranscriptVerificationPending => "Transcript Verification Pending",
        GradeStatus.AiGradingPending => "AI Grading Pending",
        GradeStatus.HumanReviewPending => "Human Review Pending",
        GradeStatus.ReconciliationRequired => "Reconciliation Required",
        GradeStatus.AwaitingFinalApproval => "Awaiting Final Approval",
        GradeStatus.Finalized => "Finalized",
        GradeStatus.Published => "Published",
        GradeStatus.UnderAppeal => "Under Appeal",
        GradeStatus.Amended => "Amended",
        _ => s.ToString(),
    };
}

public sealed class InvalidStateTransitionException(string message) : InvalidOperationException(message);
