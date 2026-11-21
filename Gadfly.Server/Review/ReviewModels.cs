using Gadfly.Server.Data;

namespace Gadfly.Server.Review;

// Read models for instructor review (GAD-T54). Built by ReviewService from the rows SessionOrchestrator
// persists; the Razor pages under Components/Pages/Review and the exports only ever see these.

/// <summary>One row of the /review list.</summary>
public sealed record ReviewListRow(
    Guid SessionId,
    Guid ExamId,
    string ExamTitle,
    Guid StudentUserId,
    string StudentName,
    int Attempt,
    SessionState State,
    DateTimeOffset? EndedAt,
    string? Recommendation,
    double? AiScore,
    int JudgesDisagreed,
    int IntegrityFlags,
    GradeDecision? LatestGrade)
{
    public bool IsGraded => LatestGrade is not null;
    public bool NeedsGrade => State == SessionState.Ended && LatestGrade is null;
}

/// <summary>The filters on the /review list.</summary>
public sealed record ReviewListFilter(Guid? ExamId = null, bool NeedsGradeOnly = false, bool NeedsHumanReviewOnly = false);

/// <summary>A source passage a criterion cites, resolved to its text and location.</summary>
public sealed record CitedPassage(string ChunkId, string? Quote, string? Text, string? Document, string? Location)
{
    public bool Found => Text is not null;
}

/// <summary>One rubric criterion's score, with its citations resolved.</summary>
public sealed record CriterionView(string Criterion, double Score, string Rationale, IReadOnlyList<CitedPassage> Citations);

/// <summary>One voter on the judging panel.</summary>
public sealed record VoteView(string ProviderId, string Model, double OverallScore, string Verdict, bool Dissents);

/// <summary>The panel's judgment of one answer.</summary>
public sealed record JudgmentView(
    double OverallScore,
    double Agreement,
    bool NeedsHumanReview,
    string Summary,
    IReadOnlyList<CriterionView> Criteria,
    IReadOnlyList<VoteView> Votes,
    double? TextStyleScore);

/// <summary>A claim the student made that contradicts an earlier one.</summary>
public sealed record ContradictionView(string Quote, string EarlierQuote);

/// <summary>One question and answer.</summary>
public sealed record TurnView(
    Guid TurnId,
    int Index,
    string ConceptKey,
    string ConceptTitle,
    string Move,
    string Question,
    string? PlannerReason,
    DateTimeOffset AskedAt,
    string? AnswerText,
    string? AnswerMode,
    DateTimeOffset? AnswerStartedAt,
    DateTimeOffset? AnswerCompletedAt,
    bool HasAudio,
    double? MasteryAfter,
    IReadOnlyDictionary<string, double> SpeechMetrics,
    JudgmentView? Judgment,
    IReadOnlyList<ContradictionView> Contradictions)
{
    /// <summary>Seconds from the question to the first word of the answer, when known.</summary>
    public double? LatencySeconds => AnswerStartedAt is { } s ? Math.Max(0, (s - AskedAt).TotalSeconds) : null;
}

/// <summary>An integrity event, with a plain-language reading of what it does and doesn't mean.</summary>
public sealed record IntegrityView(
    Guid Id,
    string Source,
    string Type,
    string Label,
    string Explanation,
    bool IsNormalBehavior,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double Confidence,
    IntegritySeverity Severity,
    string Summary,
    Guid? TurnId,
    bool Ongoing = false,
    bool SessionOver = false);

/// <summary>One entry of the review timeline: a turn, or an integrity event that happened between turns.</summary>
public sealed record TimelineItem(DateTimeOffset At, TurnView? Turn, IntegrityView? Event, IReadOnlyList<IntegrityView> DuringTurn);

/// <summary>Mastery of one concept at the end of the session.</summary>
public sealed record MasteryView(string ConceptKey, string ConceptTitle, double Estimate, int Turns, string Status);

/// <summary>Everything the instructor needs to review and grade one session.</summary>
public sealed record SessionReviewView(
    ExamSession Session,
    string ExamTitle,
    Guid CollectionId,
    string StudentName,
    ExamAssignment? Assignment,
    IReadOnlyList<TurnView> Turns,
    IReadOnlyList<IntegrityView> Integrity,
    IReadOnlyList<TimelineItem> Timeline,
    IReadOnlyList<MasteryView> Mastery,
    Data.SessionReport? Report,
    IReadOnlyList<(GradeDecision Decision, string DecidedBy)> Grades)
{
    /// <summary>True when blind grading hid the AI report and the exam-time judge's per-answer data from this viewer (D25).</summary>
    public bool Blinded { get; init; }
    public GradeDecision? LatestGrade => Grades.Count == 0 ? null : Grades[0].Decision;
    public int JudgesDisagreed => Turns.Count(t => t.Judgment?.NeedsHumanReview == true);
}

/// <summary>What the instructor entered in the grade form.</summary>
public sealed record GradeInput(string Grade, double? Score, string? Comment);

/// <summary>The outcome of saving a grade or releasing a result.</summary>
public sealed record ReviewResult(bool Ok, string Message);
