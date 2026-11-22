using Gadfly.Assessment;

namespace Gadfly.Server.Data;

// Master-plan assessment model (docs/MASTER-PLAN.md Part 17.4; GAD-T81, T82, T84, T86 and the grading
// tickets). Originals are immutable; corrections, interpretations, grades and approvals are separate rows;
// every grading-state change is also written to the append-only AuditEvent table.

// ------------------------------------------------------------------ courses, objectives, rubrics (GAD-T81)

public sealed class Course : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsArchived { get; set; }
}

public sealed class LearningObjective : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ExamId { get; set; }
    /// <summary>Stable key within the assessment.</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>The source-material concept this objective is grounded in, if any.</summary>
    public string? ConceptKey { get; set; }
    /// <summary>Relative weight in the examination grade.</summary>
    public decimal Weight { get; set; } = 1m;
    public decimal MaxScore { get; set; } = 100m;
    public int Ordinal { get; set; }
    /// <summary>
    /// Learning Mode's fixed hint ladder (GAD-T271): the instructor's rungs, weakest first, as a JSON array of strings.
    /// Empty uses the engine's default ladder. Every student who needs help gets the same rungs in the same order.
    /// </summary>
    public string HintLadderJson { get; set; } = "[]";
    public List<RubricCriterion> Criteria { get; set; } = [];
}

public sealed class RubricCriterion : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ObjectiveId { get; set; }
    public AssessmentDimension Dimension { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Weight { get; set; } = 1m;
    public int Ordinal { get; set; }
    public List<RubricLevelDescriptor> Levels { get; set; } = [];
}

public sealed class RubricLevelDescriptor : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CriterionId { get; set; }
    public MasteryLevel Level { get; set; }
    public string Descriptor { get; set; } = "";
}

// ------------------------------------------------------------------ grading policy (GAD-T82)

public enum InconclusiveProcedure { HumanReview, AdditionalExamination }

public sealed class GradingPolicy : ITenantOwned
{
    public Guid ExamId { get; set; }
    public Guid OrganizationId { get; set; }
    public decimal AiWeight { get; set; } = 0.4m;
    public decimal HumanWeight { get; set; } = 0.6m;
    public decimal AdditionalReviewAt { get; set; } = 10m;
    public decimal MandatoryReconciliationAt { get; set; } = 20m;
    public bool BlindHumanGrading { get; set; } = true;
    /// <summary>GAD-T439: graders see a session code, not the student's name, until the human score is submitted.</summary>
    public bool AnonymousMarking { get; set; }
    public InconclusiveProcedure InconclusiveProcedure { get; set; }
    /// <summary>Whether answers given in Learning Mode may count as examination evidence (Part 3.2). Off by default.</summary>
    public bool AllowAssistedEvidence { get; set; }
    /// <summary>Set when the assessment is published; after this, changes need an approved GradingPolicyChange.</summary>
    public DateTimeOffset? LockedAt { get; set; }
    public Guid? LockedByUserId { get; set; }

    public GradingPolicyRules ToRules() => new()
    {
        AiWeight = AiWeight, HumanWeight = HumanWeight, AdditionalReviewAt = AdditionalReviewAt,
        MandatoryReconciliationAt = MandatoryReconciliationAt, BlindHumanGrading = BlindHumanGrading, AnonymousMarking = AnonymousMarking,
    };
}

public sealed class GradingPolicyChange : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ExamId { get; set; }
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
    public string Reason { get; set; } = "";
    public Guid RequestedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}

// ------------------------------------------------------------------ audit (GAD-T84)

/// <summary>Append-only: the DbContext refuses to modify or delete these rows.</summary>
public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorRole { get; set; }
    /// <summary>e.g. "response.corrected", "grade.ai.recorded", "grade.human.recorded", "grade.approved", "grade.published", "access.denied".</summary>
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public Guid? SessionId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset At { get; set; }
}

// ------------------------------------------------------------------ responses and verification (GAD-T86)

public enum ResponseKind { Original, SubstantiveRevision }

/// <summary>One answer event. <see cref="OriginalText"/> is never changed after insert.</summary>
public sealed class StudentResponse : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public Guid TurnId { get; set; }
    public int Sequence { get; set; }
    public ResponseKind Kind { get; set; }
    /// <summary>For a revision, the response it revises.</summary>
    public Guid? RevisesResponseId { get; set; }
    public string Modality { get; set; } = "text";
    public string OriginalText { get; set; } = "";
    /// <summary>Raw speech recognition output and its confidence, for voice answers.</summary>
    public string? SpeechRecognitionText { get; set; }
    public double? SpeechRecognitionConfidence { get; set; }
    public VerificationState VerificationState { get; set; } = VerificationState.Captured;
    /// <summary>The text grading uses: the latest confirmed/corrected version (or the original).</summary>
    public string VerifiedText { get; set; } = "";
    public bool Assisted { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public List<TranscriptVersion> Versions { get; set; } = [];
}

public sealed class TranscriptVersion : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ResponseId { get; set; }
    public int Version { get; set; }
    public string Text { get; set; } = "";
    public CorrectionKind? CorrectionKind { get; set; }
    /// <summary>"student", "speech-recognition", "reviewer".</summary>
    public string Source { get; set; } = "student";
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ResponseVerification : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ResponseId { get; set; }
    public VerificationState From { get; set; }
    public VerificationState To { get; set; }
    /// <summary>Why (trigger name or the actor's note).</summary>
    public string Reason { get; set; } = "";
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset At { get; set; }
}

public enum ClarificationType { Transcription, Meaning }

public sealed class ClarificationEvent : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public Guid ResponseId { get; set; }
    public ClarificationType Type { get; set; }
    public string Question { get; set; } = "";
    /// <summary>The leak guard checked it reveals nothing (Part 4.4).</summary>
    public bool NonLeadingChecked { get; set; }
    public string? StudentReply { get; set; }
    public DateTimeOffset AskedAt { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
}

public enum EvidenceStance { Supports, Contradicts, Misconception, Uncertain }

public sealed class AssessmentEvidence : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid ResponseId { get; set; }
    public Guid? TranscriptVersionId { get; set; }
    public EvidenceStance Stance { get; set; }
    public AssessmentDimension? Dimension { get; set; }
    public string Note { get; set; } = "";
    /// <summary>Given after instructional help (Learning Mode).</summary>
    public bool Assisted { get; set; }
    /// <summary>Given after a question that revealed the answer (Scenario I): never counts as independent mastery.</summary>
    public bool Contaminated { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// ------------------------------------------------------------------ grading (GAD-T94..T98)

public sealed class AiObjectiveGrade : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public Guid ObjectiveId { get; set; }
    public MasteryLevel Level { get; set; }
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public string Explanation { get; set; } = "";
    /// <summary>JSON: per-criterion level/score/explanation.</summary>
    public string CriteriaJson { get; set; } = "[]";
    /// <summary>JSON array of StudentResponse ids the score rests on.</summary>
    public string EvidenceRefsJson { get; set; } = "[]";
    public string MisconceptionsJson { get; set; } = "[]";
    public string AmbiguitiesJson { get; set; } = "[]";
    public string ReviewFlagsJson { get; set; } = "[]";
    /// <summary>Evidence-criteria confidence category (Part 5.4), never a made-up percentage.</summary>
    public string Confidence { get; set; } = "";
    /// <summary>Produced in mock mode: never shown or used as a genuine AI evaluation.</summary>
    public bool IsMock { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class HumanObjectiveGrade : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public string ReviewerRole { get; set; } = "";
    public MasteryLevel Level { get; set; }
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public string? Comment { get; set; }
    public string? EvidenceNote { get; set; }
    /// <summary>True when the reviewer could not see the AI grade when scoring (blind mode).</summary>
    public bool WasBlind { get; set; }
    /// <summary>A later adjustment points at the grade it replaces; history is kept.</summary>
    public Guid? SupersedesId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}

public sealed class GradeDisagreement : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public Guid ObjectiveId { get; set; }
    public decimal AiScore { get; set; }
    public decimal HumanScore { get; set; }
    public decimal DifferencePoints { get; set; }
    public DisagreementBand Band { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class GradeReconciliation : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid DisagreementId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public Guid? SecondReviewerUserId { get; set; }
    /// <summary>"error-in-ai", "error-in-human", "transcript-ambiguity", "rubric-interpretation", "both-defensible".</summary>
    public string Cause { get; set; } = "";
    public string Resolution { get; set; } = "";
    public decimal? ResolvedAiScore { get; set; }
    public decimal? ResolvedHumanScore { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>The examination's grade record and its status (Part 11.2). One per session.</summary>
public sealed class FinalGrade : ITenantOwned
{
    public Guid SessionId { get; set; }
    public Guid OrganizationId { get; set; }
    public GradeStatus Status { get; set; }
    public decimal? FinalPercent { get; set; }
    public string? LetterOrLabel { get; set; }
    /// <summary>JSON of the CombinedResult used, and of the policy it was computed under.</summary>
    public string? ResultJson { get; set; }
    public string? PolicyJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[]? RowVersion { get; set; }
}

public enum ApprovalAction { Finalize, Publish, Amend, AppealOpened, AppealResolved, PolicyChangeApproved }

public sealed class ApprovalRecord : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public ApprovalAction Action { get; set; }
    public Guid ActorUserId { get; set; }
    public string ActorRole { get; set; } = "";
    public string? Note { get; set; }
    public DateTimeOffset At { get; set; }
}

public enum AppealState { Open, Upheld, Amended, Dismissed }

public sealed class GradeAppeal : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public Guid StudentUserId { get; set; }
    public string Reason { get; set; } = "";
    public AppealState State { get; set; }
    public string? Resolution { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
