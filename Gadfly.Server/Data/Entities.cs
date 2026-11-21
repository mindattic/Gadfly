namespace Gadfly.Server.Data;

// The platform schema (GAD-T40, docs/SOCRATIC_PLATFORM.md "Data"). Every tenant-owned row carries
// OrganizationId (a MindAttic.Authentication organization id); GAD-T42 adds the global query filter
// that scopes every query to the caller's active organization. User ids are MindAttic.Authentication
// AuthUser ids. Rows are never cascade-deleted: retention and data-deletion jobs (GAD-T61) remove
// them explicitly and auditably.

/// <summary>A row that belongs to one customer organization.</summary>
public interface ITenantOwned
{
    Guid OrganizationId { get; set; }
}

// ---------------------------------------------------------------- organizations (interim)

// Customer organizations and their members. Interim: these live in Gadfly until
// MindAttic.Authentication ships organizations (M.AUTH-T34); then IOrganizationDirectory switches to the
// library's tables and these rows migrate across. Not ITenantOwned: a user's memberships span orgs.

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>URL-safe unique name.</summary>
    public string Slug { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OrganizationMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    /// <summary>MindAttic.Authentication AuthUser id.</summary>
    public Guid UserId { get; set; }
    /// <summary>One of <c>OrgRoles</c>: Owner, Admin, Instructor, Student.</summary>
    public string Role { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

// ---------------------------------------------------------------- organization settings

public sealed class OrganizationSettings
{
    public Guid OrganizationId { get; set; }
    /// <summary>Days audio and transcripts are kept (decision D7 default 90).</summary>
    public int RetentionDays { get; set; } = 90;
    /// <summary>Monthly provider-cost ceiling in USD; null = unlimited.</summary>
    public decimal? MonthlyCostQuotaUsd { get; set; }
    public string? DefaultVoiceId { get; set; }
    public string? DefaultAvatarId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// ---------------------------------------------------------------- source material

public enum ProcessingStatus { Pending, Processing, Ready, Failed }

public sealed class SourceCollection : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ProcessingStatus Status { get; set; }
    /// <summary>Bumped whenever the concept map or any rubric changes.</summary>
    public int ConceptMapVersion { get; set; }
    public List<SourceDocument> Documents { get; set; } = [];
}

public sealed class SourceDocument : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CollectionId { get; set; }
    public SourceCollection? Collection { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    /// <summary>The stored original (MindAttic.Media uid, or a blob path).</summary>
    public string? StorageRef { get; set; }
    public ProcessingStatus Status { get; set; }
    public string? Error { get; set; }
    public int? PageCount { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? IngestedAt { get; set; }
}

public sealed class SourceChunk : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CollectionId { get; set; }
    public Guid DocumentId { get; set; }
    public SourceDocument? Document { get; set; }
    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Human-readable location: page, chapter, section.</summary>
    public string? Location { get; set; }
    public int TokenCount { get; set; }
    /// <summary>float32 little-endian vector.</summary>
    public byte[]? Embedding { get; set; }
    public string? EmbeddingModel { get; set; }
}

// ---------------------------------------------------------------- concept map and rubric

public sealed class Concept : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CollectionId { get; set; }
    /// <summary>Stable key that survives re-extraction; sessions refer to concepts by it.</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Definition { get; set; } = "";
    public string? Summary { get; set; }
    /// <summary>JSON array of strings.</summary>
    public string KeyFactsJson { get; set; } = "[]";
    /// <summary>JSON array of strings: concept-level wrong beliefs (rubrics add their own).</summary>
    public string MisconceptionsJson { get; set; } = "[]";
    public int Ordinal { get; set; }
    public bool EditedByInstructor { get; set; }
    public bool IsArchived { get; set; }
    public List<ConceptCitation> Citations { get; set; } = [];
    public List<RubricPoint> RubricPoints { get; set; } = [];
}

public sealed class ConceptRelation : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid FromConceptId { get; set; }
    public Guid ToConceptId { get; set; }
    /// <summary>e.g. "prerequisite", "part-of", "contrasts-with", "causes".</summary>
    public string Kind { get; set; } = "";
}

public sealed class ConceptCitation : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ConceptId { get; set; }
    public Guid ChunkId { get; set; }
    public string? Quote { get; set; }
}

public enum RubricPointKind { Understanding, AcceptableVariant, Misconception }

public sealed class RubricPoint : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ConceptId { get; set; }
    public int RubricVersion { get; set; }
    public RubricPointKind Kind { get; set; }
    public string Text { get; set; } = "";
    public double Weight { get; set; } = 1.0;
    /// <summary>JSON array of { chunkId, quote }.</summary>
    public string CitationsJson { get; set; } = "[]";
    public bool EditedByInstructor { get; set; }
}

// ---------------------------------------------------------------- exams and sessions

public enum ExamStatus { Draft, Published, Archived }
public enum ExamMode { Voice, Text }
public enum IntegrityLevel { Off, Standard, Strict }

public sealed class Exam : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CollectionId { get; set; }
    public string Title { get; set; } = "";
    public string? Instructions { get; set; }
    /// <summary>Serialized Gadfly.Engine.SessionPolicy (concept keys, budget, difficulty...).</summary>
    public string PolicyJson { get; set; } = "{}";
    public ExamMode Mode { get; set; } = ExamMode.Voice;
    public IntegrityLevel IntegrityLevel { get; set; } = IntegrityLevel.Standard;
    public string? VoiceId { get; set; }
    public string? AvatarId { get; set; }
    public string PromptSetId { get; set; } = "exam";
    public int PromptSetVersion { get; set; }
    public ExamStatus Status { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class ExamAssignment : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ExamId { get; set; }
    public Exam? Exam { get; set; }
    public Guid StudentUserId { get; set; }
    public DateTimeOffset? OpensAt { get; set; }
    public DateTimeOffset? ClosesAt { get; set; }
    public int AttemptsAllowed { get; set; } = 1;
    public Guid AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    /// <summary>Set when the instructor releases results to the student.</summary>
    public DateTimeOffset? ResultsReleasedAt { get; set; }
}

public enum SessionState { Created, ConsentGiven, Running, Paused, Ended, Failed }

public sealed class ExamSession : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid AssignmentId { get; set; }
    public ExamAssignment? Assignment { get; set; }
    public Guid ExamId { get; set; }
    public Guid StudentUserId { get; set; }
    public int Attempt { get; set; } = 1;
    public SessionState State { get; set; }
    /// <summary>Gadfly.Engine.EndReason name once ended.</summary>
    public string? EndReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    /// <summary>Serialized policy actually used (a snapshot of the exam's at start).</summary>
    public string PolicyJson { get; set; } = "{}";
    public string PromptSetSha256 { get; set; } = "";
    public int ConceptMapVersion { get; set; }
    public DateTimeOffset? ConsentRecordedAt { get; set; }
    /// <summary>SHA-256 of the consent text the student accepted.</summary>
    public string? ConsentTextSha256 { get; set; }
    public string? ClientInfo { get; set; }
    /// <summary>The engine's full session (SessionTranscriptSerializer), saved after every step; the resume point.</summary>
    public string? TranscriptJson { get; set; }
    /// <summary>The claim ledger (ClaimLedger.ToJson), saved with the transcript.</summary>
    public string? LedgerJson { get; set; }
    /// <summary>The concept map and rubrics this session started on (GAD-T72), so later edits can't change how it is judged.</summary>
    public string? SubjectSnapshotJson { get; set; }
    /// <summary>When the retention job removed what the student said (GAD-T61); the outcome is kept.</summary>
    public DateTimeOffset? RedactedAt { get; set; }
    /// <summary>Optimistic-concurrency token: two servers can't both advance one session.</summary>
    public byte[]? RowVersion { get; set; }
    public List<SessionTurn> Turns { get; set; } = [];
}

public sealed class SessionTurn : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public ExamSession? Session { get; set; }
    public int Index { get; set; }
    public string ConceptKey { get; set; } = "";
    /// <summary>Gadfly.Engine.MoveKind name.</summary>
    public string Move { get; set; } = "";
    public string Question { get; set; } = "";
    public string? PlannerReason { get; set; }
    public string TargetClaimIdsJson { get; set; } = "[]";
    public DateTimeOffset AskedAt { get; set; }
    public string? AnswerText { get; set; }
    public string? AnswerMode { get; set; }
    public DateTimeOffset? AnswerStartedAt { get; set; }
    public DateTimeOffset? AnswerCompletedAt { get; set; }
    public string? WordsJson { get; set; }
    public string? SpeechMetricsJson { get; set; }
    /// <summary>Stored answer audio (MindAttic.Media uid), deleted by retention.</summary>
    public string? AudioRef { get; set; }
    public double? MasteryAfter { get; set; }
}

public sealed class SessionClaim : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public Guid TurnId { get; set; }
    public string ConceptKey { get; set; } = "";
    public string Quote { get; set; } = "";
    public string Normalized { get; set; } = "";
    public Guid? ContradictsClaimId { get; set; }
}

public sealed class Judgment : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid TurnId { get; set; }
    public double OverallScore { get; set; }
    public double Agreement { get; set; }
    public bool NeedsHumanReview { get; set; }
    public int RubricVersion { get; set; }
    public string Summary { get; set; } = "";
    /// <summary>JSON array of Gadfly.Engine.CriterionScore.</summary>
    public string CriteriaJson { get; set; } = "[]";
    /// <summary>"Reads like generated text" score (GAD-T59), 0..1; null until assessed.</summary>
    public double? TextStyleScore { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<JudgeVoteRecord> Votes { get; set; } = [];
}

public sealed class JudgeVoteRecord : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid JudgmentId { get; set; }
    public string ProviderId { get; set; } = "";
    public string Model { get; set; } = "";
    public double OverallScore { get; set; }
    public string Verdict { get; set; } = "";
}

public sealed class ConceptMasteryRecord : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public string ConceptKey { get; set; } = "";
    public double Estimate { get; set; }
    public int TurnsOnConcept { get; set; }
    /// <summary>Gadfly.Engine.ConceptStatus name.</summary>
    public string Status { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum IntegritySeverity { Info, Low, Medium, High }

public sealed class IntegrityEvent : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    /// <summary>"webcam", "audio", "browser" or "text".</summary>
    public string Source { get; set; } = "";
    /// <summary>e.g. "face-absent", "multiple-faces", "offscreen-reading", "second-speaker", "tab-hidden".</summary>
    public string Type { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public double Confidence { get; set; }
    public IntegritySeverity Severity { get; set; }
    /// <summary>Plain-language explanation shown to the reviewer.</summary>
    public string Summary { get; set; } = "";
    public string? DataJson { get; set; }
    public Guid? TurnId { get; set; }
}

public sealed class SessionReport : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    /// <summary>AI recommendation, e.g. "mastered", "partial", "not-demonstrated".</summary>
    public string Recommendation { get; set; } = "";
    public double OverallScore { get; set; }
    public string ReportJson { get; set; } = "{}";
    public string Markdown { get; set; } = "";
    public string? IntegritySummaryJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GradeDecision : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SessionId { get; set; }
    public Guid DecidedByUserId { get; set; }
    public string Grade { get; set; } = "";
    public double? Score { get; set; }
    public string? Comment { get; set; }
    /// <summary>True when the instructor changed the AI recommendation.</summary>
    public bool OverridesRecommendation { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}

// ---------------------------------------------------------------- platform integration and operations

public sealed class ApiKey : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>First characters of the key, shown in the console to tell keys apart.</summary>
    public string Prefix { get; set; } = "";
    /// <summary>SHA-256 of the full key; the key itself is shown once and never stored.</summary>
    public string KeyHash { get; set; } = "";
    public string Scopes { get; set; } = "";
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class Webhook : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Url { get; set; } = "";
    /// <summary>Signing secret, encrypted with ASP.NET Core Data Protection.</summary>
    public string ProtectedSecret { get; set; } = "";
    /// <summary>Comma-separated event names, e.g. "session.completed".</summary>
    public string Events { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public enum DeliveryStatus { Pending, Delivered, Failed, GaveUp }

public sealed class WebhookDelivery : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid WebhookId { get; set; }
    public string EventType { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public int Attempt { get; set; }
    public DeliveryStatus Status { get; set; }
    public int? ResponseCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}

public enum UsageKind { Llm, SpeechToText, TextToSpeech, Embedding }

public sealed class UsageEntry : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? SessionId { get; set; }
    public UsageKind Kind { get; set; }
    public string Provider { get; set; } = "";
    public string? Model { get; set; }
    /// <summary>Input tokens, audio seconds, or characters, by kind.</summary>
    public double InputUnits { get; set; }
    public double OutputUnits { get; set; }
    public decimal CostUsd { get; set; }
    public string? Purpose { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum JobState { Queued, Running, Succeeded, Failed, Cancelled }

/// <summary>A background job (GAD-T43). Not tenant-filtered: the worker sees every org's queue.</summary>
public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OrganizationId { get; set; }
    public string Kind { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public JobState State { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTimeOffset RunAfter { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    /// <summary>0..1.</summary>
    public double Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
