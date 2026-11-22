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
    /// <summary>GAD-T285: true when blind grading hides this session's integrity flags from the viewer (IntegrityFlags is then 0).</summary>
    public bool FlagsHidden { get; init; }
    /// <summary>
    /// GAD-T425: the AI report's corroborated level per learning objective (Level 0 = insufficient evidence); empty when blind
    /// grading hides the report or the report predates objectives. This, not <see cref="Recommendation"/> or
    /// <see cref="AiScore"/> (the examiner's running estimate, which only steers questioning), is what the list shows.
    /// </summary>
    public IReadOnlyList<ObjectiveLevel> AiLevels { get; init; } = [];
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
public sealed record ContradictionView(string Quote, string EarlierQuote)
{
    /// <summary>The turn (1-based) of the earlier claim, when known.</summary>
    public int? EarlierTurn { get; init; }
    /// <summary>The student later withdrew one of the two claims (GAD-T441).</summary>
    public bool Withdrawn { get; init; }
    /// <summary>Share of the panel that found the contradiction (0 when not reported, GAD-T440).</summary>
    public double Confidence { get; init; }
    /// <summary>A split panel: kept as a possible contradiction for the reviewer, never put to the student (GAD-T440).</summary>
    public bool Possible { get; init; }
}

/// <summary>
/// An earlier claim the student explicitly withdrew in this answer (GAD-T441). It stays on record but is no longer a premise of a
/// contradiction. <see cref="EarlierTurn"/> is null when no recorded claim matched what they withdrew.
/// </summary>
public sealed record RetractionView(string Quote, string Text, int? EarlierTurn);

/// <summary>
/// The recitation check's finding on one answer (GAD-T242), as the reviewer sees it (GAD-T248): how much of the answer follows
/// the source word for word, the longest copied stretch, and which pattern fired. Stored on the turn's "possibly-recited"
/// integrity event. It explains why the examiner asked for the answer differently; it is never a verdict of cheating.
/// </summary>
public sealed record RecitationView(double SourceOverlap, int LongestCopiedWords, string? CopiedText, bool MatchesSource,
    bool ReadOutPattern, string Reason)
{
    /// <summary>The integrity event type a recited answer is recorded under.</summary>
    public const string EventType = "possibly-recited";

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static RecitationView From(Gadfly.Engine.RecitationFlag flag) => new(flag.SourceOverlap, flag.LongestCopiedWords,
        flag.CopiedText, flag.MatchesSource, flag.ReadOutPattern, flag.Reason);

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, Json);

    /// <summary>The finding stored on an event, or null when there is none or it can't be read.</summary>
    public static RecitationView? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<RecitationView>(json, Json); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Which pattern(s) fired, in plain words.</summary>
    public string Patterns => (MatchesSource, ReadOutPattern) switch
    {
        (true, true) => "close to the source's wording, and a read-out pattern (long, fluent answer after a long silent wait)",
        (true, false) => "close to the source's wording",
        (false, true) => "read-out pattern (long, fluent answer after a long silent wait)",
        _ => "none recorded",
    };
}

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

    /// <summary>The spoken answer's voice against the student's own first answers (GAD-T232); descriptive only, null when unknown or blinded.</summary>
    public Gadfly.Server.Voice.VoiceStrainReading? Strain { get; init; }

    /// <summary>The recitation check's finding when this answer read as possibly recited (GAD-T248); null otherwise.</summary>
    public RecitationView? Recitation { get; init; }

    /// <summary>
    /// The end-of-answer silence closed this spoken answer and the student went on speaking at once (GAD-T462): a neutral note that
    /// the answer may be incomplete, shown to every reviewer (it is not an integrity signal and never part of a grade).
    /// </summary>
    public bool MayHaveBeenCutOff { get; init; }

    /// <summary>Seconds from the silence closing the answer to the student speaking again, when <see cref="MayHaveBeenCutOff"/>.</summary>
    public double? CutOffResumeSeconds { get; init; }

    /// <summary>Earlier claims the student withdrew in this answer (GAD-T441); empty when none, or for a blinded grader.</summary>
    public IReadOnlyList<RetractionView> Retractions { get; init; } = [];
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

/// <summary>One entry of the review timeline: a turn, an integrity event that happened between turns, or a staff action.</summary>
public sealed record TimelineItem(DateTimeOffset At, TurnView? Turn, IntegrityView? Event, IReadOnlyList<IntegrityView> DuringTurn,
    StaffActionView? Action = null);

/// <summary>
/// Something staff did to the exam while it ran, from the live monitor (GAD-T299): extra time, a pause, a resume, an early end or a
/// notice to the student. Read from the audit log; shown to every reviewer, because it explains the timing of what follows.
/// </summary>
public sealed record StaffActionView(DateTimeOffset At, string Label, string? Detail, string? Reason, string By);

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
