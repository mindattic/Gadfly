using Gadfly.Server.Data;

namespace Gadfly.Server.Transcripts;

// The transcript maker's neutral document model (GAD-T295). TranscriptBuilder loads one session's rows from the response
// ledger, TranscriptComposer turns them into a TranscriptDocument for one audience, and every renderer (web page, PDF,
// Word, plain text, captions, JSON) only ever sees that document. What a copy leaves out is decided once, in the composer.

/// <summary>Who a transcript copy is for. Each copy has its own content, and so its own hash and verification code.</summary>
public enum TranscriptAudience
{
    /// <summary>The student's own copy, available once the result is published. No reviewer context.</summary>
    Student,
    /// <summary>A grader's copy while grading is blind: no integrity notes, no voice measurements, no AI scores.</summary>
    BlindGrader,
    /// <summary>The full copy for reviewers and administrators.</summary>
    Reviewer,
}

public static class TranscriptAudiences
{
    public static string Key(TranscriptAudience a) => a switch
    {
        TranscriptAudience.Student => "student",
        TranscriptAudience.BlindGrader => "blind-grader",
        _ => "reviewer",
    };

    /// <summary>Reads a copy name from a query string or a stored record; null when it names none.</summary>
    public static TranscriptAudience? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "student" => TranscriptAudience.Student,
        "blind" or "blind-grader" or "grader" => TranscriptAudience.BlindGrader,
        "reviewer" or "full" => TranscriptAudience.Reviewer,
        _ => null,
    };

    public static string Label(TranscriptAudience a) => a switch
    {
        TranscriptAudience.Student => "Student copy",
        TranscriptAudience.BlindGrader => "Grader copy (blind grading)",
        _ => "Reviewer copy",
    };

    /// <summary>What this copy leaves out, in plain words; printed on the copy itself.</summary>
    public static IReadOnlyList<string> LeftOut(TranscriptAudience a) => a switch
    {
        TranscriptAudience.Student =>
        [
            "reviewer notes about the session (camera, sound and browser observations)",
            "voice measurements",
            "the AI panel's exam-time scores and the examiner's reasons for each question",
        ],
        TranscriptAudience.BlindGrader =>
        [
            "integrity notes (camera, sound, browser and writing-style observations)",
            "voice measurements",
            "the AI panel's exam-time scores and the examiner's reasons for each question",
        ],
        _ => [],
    };

    /// <summary>A copy never carries more than the copy the caller is entitled to: Reviewer &gt; BlindGrader &gt; Student.</summary>
    public static TranscriptAudience AtMost(TranscriptAudience entitled, TranscriptAudience? asked) =>
        asked is { } want && Rank(want) < Rank(entitled) ? want : entitled;

    private static int Rank(TranscriptAudience a) => a switch { TranscriptAudience.Student => 0, TranscriptAudience.BlindGrader => 1, _ => 2 };
}

/// <summary>
/// One exam session's official transcript for one audience. This is the content the hash covers: it holds nothing that
/// changes from one download to the next (no "made at" time), so the same record always gives the same code.
/// </summary>
public sealed record TranscriptDocument
{
    public string Schema { get; init; } = "gadfly.transcript/1";
    /// <summary><see cref="TranscriptAudiences.Key"/>.</summary>
    public required string Audience { get; init; }
    public required Guid SessionId { get; init; }
    public string? Organization { get; init; }
    public required string ExamTitle { get; init; }
    public string? Subject { get; init; }
    public string? Topic { get; init; }
    public required string StudentName { get; init; }
    public int Attempt { get; init; }
    /// <summary>"Spoken" or "Typed".</summary>
    public string Mode { get; init; } = "";
    /// <summary>"Examination" or "Practice".</summary>
    public string Purpose { get; init; } = "";
    /// <summary>"In progress", "Ended", ...</summary>
    public string Status { get; init; } = "";
    public string? EndReason { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    /// <summary>When the retention rules removed what the student said; the questions and times remain.</summary>
    public DateTimeOffset? WordsRemovedAt { get; init; }
    public IReadOnlyList<TranscriptTurn> Turns { get; init; } = [];
    /// <summary>Reviewer copy only: integrity notes that fell between questions.</summary>
    public IReadOnlyList<TranscriptNote> SessionNotes { get; init; } = [];
    /// <summary>What this copy leaves out, in plain words.</summary>
    public IReadOnlyList<string> LeftOut { get; init; } = [];
}

/// <summary>One question and everything said in reply to it.</summary>
public sealed record TranscriptTurn
{
    /// <summary>1-based.</summary>
    public int Number { get; init; }
    public string Concept { get; init; } = "";
    /// <summary>The examiner's move in plain words, e.g. "Opening question".</summary>
    public string Move { get; init; } = "";
    /// <summary>The engine's name for the move, e.g. "Elicit".</summary>
    public string MoveKind { get; init; } = "";
    public DateTimeOffset AskedAt { get; init; }
    /// <summary>Seconds from the start of the session.</summary>
    public double OffsetSeconds { get; init; }
    public string Question { get; init; } = "";
    public IReadOnlyList<TranscriptResponse> Responses { get; init; } = [];

    // ---- reviewer copy only
    public string? ExaminerReason { get; init; }
    public TranscriptAiReading? Ai { get; init; }
    public string? VoiceMeasurements { get; init; }
    public IReadOnlyList<TranscriptNote> Notes { get; init; } = [];
}

/// <summary>One answer event: the original answer, or a changed answer recorded beside it.</summary>
public sealed record TranscriptResponse
{
    public int Sequence { get; init; }
    /// <summary>"Answer" or "Changed answer".</summary>
    public string Kind { get; init; } = "";
    /// <summary>"Spoken" or "Typed".</summary>
    public string Modality { get; init; } = "";
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public double? StartOffsetSeconds { get; init; }
    public double EndOffsetSeconds { get; init; }
    /// <summary>The answer exactly as first recorded (spoken answers: as the speech recognizer heard them). Never changed.</summary>
    public string AsRecorded { get; init; } = "";
    /// <summary>The text grading uses, when a correction changed it; null when it is the same as <see cref="AsRecorded"/>.</summary>
    public string? Verified { get; init; }
    /// <summary>Where the answer stands in verification, in plain words.</summary>
    public string RecordStatus { get; init; } = "";
    /// <summary>Seconds from the question to the first word.</summary>
    public double? DelaySeconds { get; init; }
    public double? SpokenSeconds { get; init; }
    public int? Pauses { get; init; }
    public int? LongPauses { get; init; }
    public double? LongestPauseSeconds { get; init; }
    public TranscriptReadBack? ReadBack { get; init; }
    public IReadOnlyList<TranscriptCorrection> Corrections { get; init; } = [];
    public IReadOnlyList<TranscriptClarification> Clarifications { get; init; } = [];
    public IReadOnlyList<TranscriptUnsure> UnsureWords { get; init; } = [];
    /// <summary>Spoken answers: whether the student confirmed or corrected the transcript. Null for typed answers.</summary>
    public bool? CheckedByStudent { get; init; }
    /// <summary>Word timings inside the answer, when the capture kept them; captions are cut from these.</summary>
    public IReadOnlyList<TranscriptWord> Words { get; init; } = [];
}

/// <summary>The read-back: the examiner repeats the recorded answer and the student says whether it is what they said.</summary>
public sealed record TranscriptReadBack(DateTimeOffset AskedAt, string Prompt, string Outcome, DateTimeOffset? AnsweredAt);

/// <summary>A transcription correction: the text before and after, who made it and how.</summary>
public sealed record TranscriptCorrection(int Version, DateTimeOffset At, string Original, string Corrected, string By);

/// <summary>A question about what the student meant, and their reply. Stored beside the answer, never over it.</summary>
public sealed record TranscriptClarification(DateTimeOffset AskedAt, string Question, string? Reply, DateTimeOffset? AnsweredAt);

/// <summary>Words the speech recognizer was unsure of. <see cref="LowestConfidence"/> is on the reviewer copy only.</summary>
public sealed record TranscriptUnsure(string Text, double StartSeconds, double EndSeconds, double? LowestConfidence);

public sealed record TranscriptWord(string Text, double StartSeconds, double EndSeconds);

/// <summary>Reviewer copy only: an integrity note. A low-confidence observation for a human; never a verdict, never part of a grade.</summary>
public sealed record TranscriptNote(DateTimeOffset At, double OffsetSeconds, string Label, string Explanation, string Severity,
    double Confidence, bool NormalBehavior, string? Summary);

/// <summary>Reviewer copy only: the AI panel's exam-time reading of one answer.</summary>
public sealed record TranscriptAiReading(double Score, double Agreement, bool JudgesDisagreed, string Summary, double? TextStyleScore,
    string? TextStyle, double? MasteryAfter);

/// <summary>
/// The verification stamp printed on a copy: the SHA-256 of the transcript's canonical JSON, the short code derived from
/// it, when this file was made and where to check it. Not part of the hashed content.
/// </summary>
public sealed record TranscriptStamp(string Sha256, string Code, DateTimeOffset GeneratedAt, string VerifyUrl);

/// <summary>The rows one transcript is composed from. Everything here belongs to one organization and one session.</summary>
public sealed record TranscriptSource
{
    public required ExamSession Session { get; init; }
    public Exam? Exam { get; init; }
    public string? OrganizationName { get; init; }
    public string StudentName { get; init; } = "(unknown student)";
    public IReadOnlyDictionary<string, string> ConceptTitles { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<SessionTurn> Turns { get; init; } = [];
    public IReadOnlyList<StudentResponse> Responses { get; init; } = [];
    public IReadOnlyList<ClarificationEvent> Clarifications { get; init; } = [];
    public IReadOnlyDictionary<Guid, Verification.SpeechMark> Marks { get; init; } = new Dictionary<Guid, Verification.SpeechMark>();
    /// <summary>Reviewer copy only; the builder does not load these for the other copies.</summary>
    public IReadOnlyList<IntegrityEvent> IntegrityEvents { get; init; } = [];
    /// <summary>Reviewer copy only; the builder does not load these for the other copies.</summary>
    public IReadOnlyList<Judgment> Judgments { get; init; } = [];
}
