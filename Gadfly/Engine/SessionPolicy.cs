using Gadfly.Assessment;

namespace Gadfly.Engine;

/// <summary>
/// The rules one session runs under: which concepts to cover, how long, how deep, and when a
/// concept counts as mastered. An exam template supplies it; the planner enforces it.
/// </summary>
public sealed record SessionPolicy
{
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

    /// <summary>The budget the session actually runs under: <see cref="TimeBudget"/> times <see cref="TimeMultiplier"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan EffectiveTimeBudget => TimeSpan.FromTicks((long)(TimeBudget.Ticks * Math.Max(1.0, TimeMultiplier)));

    /// <summary>Throws when the policy is not usable.</summary>
    public void Validate()
    {
        // NaN and the infinities pass every ordinary comparison below, so each double field is checked finite first (GAD-T195).
        if (!double.IsFinite(MasteryThreshold)) throw new ArgumentException("MasteryThreshold must be a finite number.");
        if (!double.IsFinite(AporiaThreshold)) throw new ArgumentException("AporiaThreshold must be a finite number.");
        if (!double.IsFinite(TimeMultiplier) || TimeMultiplier < 1 || TimeMultiplier > 4)
            throw new ArgumentException("TimeMultiplier must be between 1 and 4.");
        if (MinCorroboratingAnswers is < 2 or > 5) throw new ArgumentException("MinCorroboratingAnswers must be 2 to 5.");
        if (MaxAssistsPerObjective is < 0 or > 5) throw new ArgumentException("MaxAssistsPerObjective must be 0 to 5.");
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
