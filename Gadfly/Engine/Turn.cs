namespace Gadfly.Engine;

/// <summary>One spoken (or typed) word with its timing inside the answer, from speech recognition.</summary>
public sealed record TimedWord(string Text, TimeSpan Start, TimeSpan End, double Confidence = 1.0);

/// <summary>The student's answer to one question.</summary>
public sealed record Answer
{
    /// <summary>Final transcript (or typed text in text mode).</summary>
    public required string Text { get; init; }

    /// <summary>"voice" or "text".</summary>
    public string Mode { get; init; } = "text";

    /// <summary>Word timings when the answer was spoken; empty in text mode.</summary>
    public IReadOnlyList<TimedWord> Words { get; init; } = [];

    /// <summary>When the student started answering (first word, or first keystroke).</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When the answer was complete.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Descriptive speech metrics (latency, rate, pauses...) by name, from MindAttic.SpeechRecognition.</summary>
    public IReadOnlyDictionary<string, double> SpeechMetrics { get; init; } = new Dictionary<string, double>();
}

/// <summary>A source passage a judgment or rubric point relies on.</summary>
public sealed record Citation(string ChunkId, string? Quote = null);

/// <summary>One rubric criterion's score for an answer.</summary>
public sealed record CriterionScore
{
    public required string Criterion { get; init; }
    /// <summary>0..1.</summary>
    public required double Score { get; init; }
    public string Rationale { get; init; } = "";
    public IReadOnlyList<Citation> Citations { get; init; } = [];
}

/// <summary>One voter's raw verdict, kept so dissent is visible to the reviewer.</summary>
public sealed record JudgeVote(string ProviderId, string Model, double OverallScore, string Verdict);

/// <summary>The Legion panel's judgment of one answer.</summary>
public sealed record AnswerJudgment
{
    public required IReadOnlyList<CriterionScore> Criteria { get; init; }
    /// <summary>0..1 weighted overall score.</summary>
    public required double OverallScore { get; init; }
    public IReadOnlyList<JudgeVote> Votes { get; init; } = [];
    /// <summary>0..1: how closely the voters agreed (1 = unanimous).</summary>
    public double Agreement { get; init; } = 1.0;
    /// <summary>True when agreement was too low for the recommendation to stand without a human look.</summary>
    public bool NeedsHumanReview { get; init; }
    /// <summary>The rubric version that judged this answer.</summary>
    public int RubricVersion { get; init; }
    public string Summary { get; init; } = "";
}

/// <summary>A concept's mastery estimate at one moment.</summary>
public sealed record MasterySnapshot(string ConceptId, double Estimate, int TurnsOnConcept);

/// <summary>One exchange: the examiner's move and question, the answer, and its judgment.</summary>
public sealed record Turn
{
    /// <summary>0-based position in the session.</summary>
    public required int Index { get; init; }
    public required string ConceptId { get; init; }
    public required MoveKind Move { get; init; }
    public required string Question { get; init; }
    public required DateTimeOffset AskedAt { get; init; }
    /// <summary>Claim ids from the ledger this question targets (e.g. the two sides of a Contradiction).</summary>
    public IReadOnlyList<string> TargetClaimIds { get; init; } = [];
    public Answer? Answer { get; init; }
    public AnswerJudgment? Judgment { get; init; }
    /// <summary>The concept's mastery right after this turn was judged.</summary>
    public MasterySnapshot? MasteryAfter { get; init; }
}
