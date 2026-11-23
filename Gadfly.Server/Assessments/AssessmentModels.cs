using Gadfly.Assessment;
using Gadfly.Server.Data;

namespace Gadfly.Server.Assessments;

// What the teacher assessment setup (GAD-T85) reads and edits. The service turns these into Course, Exam,
// LearningObjective, RubricCriterion, RubricLevelDescriptor and GradingPolicy rows (GAD-T81/T82).

public sealed record AssessmentResult(bool Ok, string Message, Guid? Id = null);

/// <summary>What the caller may do here, from <see cref="PermissionMatrix"/> and their role.</summary>
public sealed record AssessmentCapabilities(string? Role, bool ManageCourses, bool ManageAssessments, bool AssignExaminations,
    bool ApprovePolicyChanges)
{
    public bool Any => ManageCourses || ManageAssessments || AssignExaminations || ApprovePolicyChanges;
}

public sealed class CourseDraft
{
    public Guid? Id { get; set; }
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Description { get; set; }
}

public sealed record CourseSummary(Guid Id, string Title, string Subject, string? Description, bool IsArchived, int Assessments, DateTimeOffset CreatedAt);

/// <summary>Step 1: the assessment's details. Duration, question limit and difficulty go into the exam's SessionPolicy.</summary>
public sealed class AssessmentDetails
{
    public Guid? Id { get; set; }
    public Guid? CourseId { get; set; }
    public Guid CollectionId { get; set; }
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Topic { get; set; } = "";
    public AssessmentMode Mode { get; set; } = AssessmentMode.Examination;
    public int DurationMinutes { get; set; } = 20;
    /// <summary>Most questions in one session (SessionPolicy.MaxTurns).</summary>
    public int QuestionLimit { get; set; } = 12;
    public int Difficulty { get; set; } = 3;
    public string? Instructions { get; set; }
}

public sealed record AssessmentSummary(Guid Id, string Title, Guid? CourseId, string? CourseTitle, string Subject, string Topic, AssessmentMode Mode,
    ExamStatus Status, int Objectives, int Assigned, bool PolicyLocked, int PendingPolicyChanges, DateTimeOffset CreatedAt);

public sealed class ObjectiveDraft
{
    public Guid? Id { get; set; }
    public Guid ExamId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? ConceptKey { get; set; }
    public decimal Weight { get; set; } = 1m;
    public decimal MaxScore { get; set; } = 100m;
    /// <summary>Learning Mode's fixed hint ladder (GAD-T271), one rung per line, weakest first. Blank uses the default ladder.</summary>
    public string? HintLadder { get; set; }
}

public sealed class CriterionDraft
{
    public Guid? Id { get; set; }
    public Guid ObjectiveId { get; set; }
    public AssessmentDimension Dimension { get; set; } = AssessmentDimension.ConceptualUnderstanding;
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Weight { get; set; } = 1m;
    /// <summary>Descriptor per mastery level, indexed by (int)MasteryLevel (0..5). Blank means the level's default.</summary>
    public string[] Levels { get; set; } = DefaultLevels();

    public static string[] DefaultLevels() =>
        Enum.GetValues<MasteryLevel>().OrderBy(l => (int)l).Select(MasteryLevels.DefaultDescriptor).ToArray();
}

public sealed record CriterionView(Guid Id, AssessmentDimension Dimension, string Name, string? Description, decimal Weight, int Ordinal,
    IReadOnlyList<string> Levels);

public sealed record ObjectiveView(Guid Id, string Key, string Title, string? Description, string? ConceptKey, string? ConceptTitle,
    bool ConceptMissing, decimal Weight, decimal MaxScore, int Ordinal, IReadOnlyList<CriterionView> Criteria)
{
    /// <summary>Learning Mode's fixed hint ladder (GAD-T271); empty means the engine's default ladder.</summary>
    public IReadOnlyList<string> HintLadder { get; init; } = [];
    /// <summary>The fixed opening question approved for this objective (GAD-T436); null when the examiner words it for each student.</summary>
    public string? OpeningQuestion { get; init; }
}

/// <summary>The grading policy as the teacher edits it: weights as percentages (50/50), thresholds in percentage points.</summary>
public sealed class PolicyDraft
{
    /// <summary>GAD-T703: AI only by default for new exams (owner decision 2026-11-23); the weights apply to Combined.</summary>
    public GradingMode Mode { get; set; } = GradingMode.AiOnly;
    public decimal AiWeightPercent { get; set; } = 50m;
    public decimal HumanWeightPercent { get; set; } = 50m;
    public decimal AdditionalReviewAt { get; set; } = 10m;
    public decimal MandatoryReconciliationAt { get; set; } = 20m;
    public bool BlindHumanGrading { get; set; } = true;
    /// <summary>GAD-T439: graders see a session code instead of the student's name until the human score is submitted.</summary>
    public bool AnonymousMarking { get; set; }
    public InconclusiveProcedure InconclusiveProcedure { get; set; } = InconclusiveProcedure.HumanReview;
    public bool AllowAssistedEvidence { get; set; }

    public PolicySnapshot ToSnapshot() => new(Math.Round(AiWeightPercent / 100m, 4), Math.Round(HumanWeightPercent / 100m, 4),
        AdditionalReviewAt, MandatoryReconciliationAt, BlindHumanGrading, InconclusiveProcedure, AllowAssistedEvidence, AnonymousMarking, Mode);

    public static PolicyDraft From(PolicySnapshot s) => new()
    {
        Mode = s.Mode,
        AiWeightPercent = s.AiWeight * 100m,
        HumanWeightPercent = s.HumanWeight * 100m,
        AdditionalReviewAt = s.AdditionalReviewAt,
        MandatoryReconciliationAt = s.MandatoryReconciliationAt,
        BlindHumanGrading = s.BlindHumanGrading,
        AnonymousMarking = s.AnonymousMarking,
        InconclusiveProcedure = s.InconclusiveProcedure,
        AllowAssistedEvidence = s.AllowAssistedEvidence,
    };
}

/// <summary>A grading policy's values (fractions for weights): what audit records and change requests store.</summary>
public sealed record PolicySnapshot(decimal AiWeight, decimal HumanWeight, decimal AdditionalReviewAt, decimal MandatoryReconciliationAt,
    bool BlindHumanGrading, InconclusiveProcedure InconclusiveProcedure, bool AllowAssistedEvidence, bool AnonymousMarking = false,
    GradingMode Mode = GradingMode.Combined)
{
    public static PolicySnapshot Default { get; } = Of(new GradingPolicy());

    public static PolicySnapshot Of(GradingPolicy p) => new(p.AiWeight, p.HumanWeight, p.AdditionalReviewAt, p.MandatoryReconciliationAt,
        p.BlindHumanGrading, p.InconclusiveProcedure, p.AllowAssistedEvidence, p.AnonymousMarking, p.Mode);

    public GradingPolicyRules ToRules() => new()
    {
        Mode = Mode, AiWeight = AiWeight, HumanWeight = HumanWeight, AdditionalReviewAt = AdditionalReviewAt,
        MandatoryReconciliationAt = MandatoryReconciliationAt, BlindHumanGrading = BlindHumanGrading, AnonymousMarking = AnonymousMarking,
    };

    public void ApplyTo(GradingPolicy p)
    {
        p.Mode = Mode;
        p.AiWeight = AiWeight;
        p.HumanWeight = HumanWeight;
        p.AdditionalReviewAt = AdditionalReviewAt;
        p.MandatoryReconciliationAt = MandatoryReconciliationAt;
        p.BlindHumanGrading = BlindHumanGrading;
        p.AnonymousMarking = AnonymousMarking;
        p.InconclusiveProcedure = InconclusiveProcedure;
        p.AllowAssistedEvidence = AllowAssistedEvidence;
    }

    public string Describe() =>
        $"{ModeLabel(Mode, AiWeight, HumanWeight)}, review at {AdditionalReviewAt:0.##} pts, reconcile at {MandatoryReconciliationAt:0.##} pts, " +
        $"{(BlindHumanGrading ? "blind" : "non-blind")}{(AnonymousMarking ? ", anonymous" : "")} human grading, inconclusive → {InconclusiveProcedure}, assisted evidence {(AllowAssistedEvidence ? "allowed" : "not allowed")}";

    /// <summary>The grading mode in words (GAD-T703), with the weights when both evaluations count.</summary>
    public static string ModeLabel(GradingMode mode, decimal aiWeight, decimal humanWeight) => mode switch
    {
        GradingMode.AiOnly => "AI grade only",
        GradingMode.HumanOnly => "human grade only",
        _ => $"AI {aiWeight * 100m:0.##}% / human {humanWeight * 100m:0.##}%",
    };
}

public sealed record PolicyChangeView(Guid Id, PolicySnapshot Before, PolicySnapshot After, string Reason, Guid RequestedByUserId, string RequestedBy,
    DateTimeOffset RequestedAt, Guid? ApprovedByUserId, string? ApprovedBy, DateTimeOffset? ApprovedAt)
{
    public bool Pending => ApprovedAt is null;
}

public sealed record AssessmentView(AssessmentDetails Details, ExamStatus Status, DateTimeOffset? PublishedAt, string? CourseTitle, string? CollectionTitle,
    IReadOnlyList<ObjectiveView> Objectives, PolicySnapshot Policy, bool PolicySaved, DateTimeOffset? PolicyLockedAt, string? PolicyLockedBy,
    IReadOnlyList<PolicyChangeView> PolicyChanges, bool AnySessionCompleted)
{
    public bool IsDraft => Status == ExamStatus.Draft;
    public bool PolicyLocked => PolicyLockedAt is not null || Status != ExamStatus.Draft;
}

/// <summary>A suggested objective: a concept from the source collection not yet used by an objective.</summary>
public sealed record ConceptSuggestion(string Key, string Title, string Definition);
