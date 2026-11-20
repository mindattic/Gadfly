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

    /// <summary>Throws when the policy is not usable.</summary>
    public void Validate()
    {
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
