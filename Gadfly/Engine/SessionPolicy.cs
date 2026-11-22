using System.Text.RegularExpressions;
using Gadfly.Assessment;

namespace Gadfly.Engine;

/// <summary>
/// The rules one session runs under: which concepts to cover, how long, how deep, and when a
/// concept counts as mastered. An exam template supplies it; the planner enforces it.
/// </summary>
public sealed record SessionPolicy
{
    private static readonly Regex LanguagePattern = new(@"^[a-zA-Z]{2,8}(-[a-zA-Z0-9]{1,8})?$", RegexOptions.Compiled);

    /// <summary>Concept ids to examine, in the order the instructor prefers. Empty means "the subject decides".</summary>
    public IReadOnlyList<string> ConceptIds { get; init; } = [];

    /// <summary>Wall-clock budget for the whole session. The server's timer is authoritative.</summary>
    public TimeSpan TimeBudget { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>Hard cap on turns across the session.</summary>
    public int MaxTurns { get; init; } = 40;

    /// <summary>Most follow-up turns spent on one concept before the planner moves on.</summary>
    public int MaxTurnsPerConcept { get; init; } = 6;

    /// <summary>Mastery estimate (0..1) at or above which a concept is closed as mastered.</summary>
    public double MasteryThreshold { get; init; } = 0.8;

    /// <summary>Mastery estimate (0..1) at or below which, after <see cref="MinTurnsBeforeAporia"/>, a concept closes as aporia.</summary>
    public double AporiaThreshold { get; init; } = 0.25;

    /// <summary>Turns on a concept before a low estimate may close it as aporia.</summary>
    public int MinTurnsBeforeAporia { get; init; } = 3;

    /// <summary>1 (gentle) to 5 (rigorous): how hard follow-ups push.</summary>
    public int Difficulty { get; init; } = 3;

    /// <summary>Seed for every random choice the planner makes, so a session can be replayed for audit.</summary>
    public int Seed { get; init; }

    /// <summary>
    /// Learning objectives to examine (GAD-T89), in the instructor's order. Empty means one implicit
    /// objective per concept in <see cref="ConceptIds"/>, so concept-only exams keep working.
    /// </summary>
    public IReadOnlyList<ExamObjective> Objectives { get; init; } = [];

    /// <summary>Examination (never teaches) or Learning (hints and guided explanations; assisted evidence). Part 3.</summary>
    public AssessmentMode Mode { get; init; } = AssessmentMode.Examination;

    /// <summary>Accessibility accommodation: the time budget is multiplied by this (1 = none, 1.5 = time and a half).</summary>
    public double TimeMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Whether the recitation check's timing-based read-out pattern (a long, fluent answer after a long silent wait) runs at
    /// all (GAD-T272). Its false flag rate on honest students is unknown and timing varies with anxiety, language background
    /// and careful thinking, so an exam can switch it off. The source-overlap half of the check always runs.
    /// </summary>
    public bool ReadOutCheck { get; init; } = true;

    /// <summary>
    /// True when the read-out pattern may flag an answer: <see cref="ReadOutCheck"/> is on and the student has no
    /// accommodation (an extra-time <see cref="TimeMultiplier"/> above 1 suppresses it, since a slow start is what the
    /// accommodation is for). GAD-T272.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ReadOutCheckActive => ReadOutCheck && TimeMultiplier <= 1.0;

    /// <summary>Independent supporting answers needed before a level counts as shown (Part 6.3: never one answer).</summary>
    public int MinCorroboratingAnswers { get; init; } = 2;

    /// <summary>Learning Mode: most hints or guided explanations per objective.</summary>
    public int MaxAssistsPerObjective { get; init; } = 2;

    /// <summary>
    /// Fewest objectives an Examination session must actually examine before its per-objective scores are reported as
    /// summative (GAD-T267). Oral-exam reliability comes from sampling many content areas, not from one long thread
    /// (research note 02: Wass2003, VanderVleuten2010), so a session that examined fewer is labelled formative. No source
    /// gives an exact count: 4 is a starting floor the instructor can raise or lower.
    /// </summary>
    public int MinSummativeObjectives { get; init; } = ExamObjectives.DefaultMinSummativeObjectives;

    /// <summary>The budget the session actually runs under: <see cref="TimeBudget"/> times <see cref="TimeMultiplier"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan EffectiveTimeBudget => TimeSpan.FromTicks((long)(TimeBudget.Ticks * Math.Max(1.0, TimeMultiplier)));

    /// <summary>
    /// The spoken language expected on this exam (GAD-T357), an IETF-ish code such as <c>en</c> or <c>en-US</c>, passed to the
    /// speech recognizer. Null lets the recognizer detect it (the provider's own default). Stored in the exam's existing
    /// policy JSON, so adding it needed no schema change.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Fixed opening questions (GAD-T436), by objective key: the instructor-approved question asked word for word as the first
    /// question on that objective to every student, so the one observation all students share is literally the same
    /// (ElenchusAtScale M2, 2.4.3, correction 31). An objective without one gets an opening newly worded by the panel. Stored in
    /// the exam's existing policy JSON, so it needed no schema change.
    /// </summary>
    public IReadOnlyDictionary<string, string> OpeningQuestions { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The instructor's approval of the concept map and rubric version this exam runs on (GAD-T437; ElenchusAtScale 4.2,
    /// correction 35, 6.8): the panel-built map and per-concept rubric are the judge's standard of truth, so a summative
    /// examination is not published, and no new attempt starts, until a person has read and approved the version in use.
    /// Null when none was given (Learning Mode, or an exam published before approval was required). Set by the host.
    /// </summary>
    public RubricApproval? RubricApproval { get; init; }

    /// <summary>Longest fixed opening question accepted (GAD-T436).</summary>
    public const int MaxOpeningQuestionLength = 500;

    /// <summary>Throws when the policy is not usable.</summary>
    public void Validate()
    {
        if (Language is { Length: > 0 } lang && !LanguagePattern.IsMatch(lang))
            throw new ArgumentException("Language must be a short code such as 'en' or 'en-US', or left blank.");
        // NaN and the infinities pass every ordinary comparison below, so each double field is checked finite first (GAD-T195).
        if (!double.IsFinite(MasteryThreshold)) throw new ArgumentException("MasteryThreshold must be a finite number.");
        if (!double.IsFinite(AporiaThreshold)) throw new ArgumentException("AporiaThreshold must be a finite number.");
        if (!double.IsFinite(TimeMultiplier) || TimeMultiplier < 1 || TimeMultiplier > 4)
            throw new ArgumentException("TimeMultiplier must be between 1 and 4.");
        if (MinCorroboratingAnswers is < 2 or > 5) throw new ArgumentException("MinCorroboratingAnswers must be 2 to 5.");
        if (MaxAssistsPerObjective is < 0 or > 5) throw new ArgumentException("MaxAssistsPerObjective must be 0 to 5.");
        if (MinSummativeObjectives is < 1 or > 50) throw new ArgumentException("MinSummativeObjectives must be 1 to 50.");
        foreach (var (key, text) in OpeningQuestions ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("A fixed opening question needs an objective and some text.");
            if (text.Length > MaxOpeningQuestionLength)
                throw new ArgumentException($"A fixed opening question must be at most {MaxOpeningQuestionLength} characters.");
        }
        var keys = Objectives.Select(o => o.Key).ToList();
        if (keys.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Every objective needs a key.");
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count) throw new ArgumentException("Objective keys must be unique.");
        if (TimeBudget <= TimeSpan.Zero) throw new ArgumentException("TimeBudget must be positive.");
        if (MaxTurns < 1) throw new ArgumentException("MaxTurns must be at least 1.");
        if (MaxTurnsPerConcept < 1) throw new ArgumentException("MaxTurnsPerConcept must be at least 1.");
        if (MasteryThreshold is <= 0 or > 1) throw new ArgumentException("MasteryThreshold must be in (0, 1].");
        if (AporiaThreshold < 0 || AporiaThreshold >= MasteryThreshold)
            throw new ArgumentException("AporiaThreshold must be in [0, MasteryThreshold).");
        if (MinTurnsBeforeAporia < 1) throw new ArgumentException("MinTurnsBeforeAporia must be at least 1.");
        if (Difficulty is < 1 or > 5) throw new ArgumentException("Difficulty must be 1 to 5.");
    }
}

/// <summary>
/// An instructor's approval of one concept map and rubric version (GAD-T437): <see cref="ConceptMapVersion"/> is the source
/// collection's version that was read and approved, by <see cref="ApprovedByUserId"/> at <see cref="ApprovedAt"/>.
/// </summary>
public sealed record RubricApproval(int ConceptMapVersion, Guid ApprovedByUserId, DateTimeOffset ApprovedAt);
