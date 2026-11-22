using Gadfly.Server.Data;

namespace Gadfly.Server.Review;

/// <summary>One line of the integrity summary: a kind of signal, how often, and what it does and doesn't mean.</summary>
public sealed record IntegritySummaryLine(string Label, string Source, int Count, TimeSpan TotalDuration, IntegritySeverity Highest,
    string Explanation, IReadOnlyList<int> Turns);

/// <summary>
/// A session's integrity summary (GAD-T60): every signal from every source (browser, webcam, audio,
/// answer text) grouped and explained for the reviewing instructor. It is a reading aid, never a
/// verdict: there is no automatic failing, ordinary behavior such as a brief look away (many causes,
/// none of which the camera can tell apart; GAD-T446) is shown separately as normal, and the overall level only says how much there is to look at.
/// </summary>
public sealed record IntegritySummary(string Level, string Headline, IReadOnlyList<IntegritySummaryLine> Lines,
    IReadOnlyList<IntegritySummaryLine> Normal, int TurnsWithSignals)
{
    public const string Nothing = "none";
    public const string Minor = "minor";
    public const string Review = "review";

    public static IntegritySummary From(IReadOnlyList<IntegrityView> events, IReadOnlyList<TurnView> turns)
    {
        var turnIndex = turns.ToDictionary(t => t.TurnId, t => t.Index);
        IntegritySummaryLine Line(IGrouping<(string Label, string Source), IntegrityView> g) => new(
            g.Key.Label,
            g.Key.Source,
            g.Count(),
            TimeSpan.FromSeconds(g.Sum(e => e.EndedAt is { } end ? Math.Max(0, (end - e.StartedAt).TotalSeconds) : 0)),
            g.Max(e => e.Severity),
            g.First().Explanation,
            g.Where(e => e.TurnId is { } id && turnIndex.ContainsKey(id)).Select(e => turnIndex[e.TurnId!.Value] + 1).Distinct().Order().ToList());

        var flagged = events.Where(e => !e.IsNormalBehavior).GroupBy(e => (e.Label, e.Source)).Select(Line)
            .OrderByDescending(l => l.Highest).ThenByDescending(l => l.Count).ToList();
        var normal = events.Where(e => e.IsNormalBehavior).GroupBy(e => (e.Label, e.Source)).Select(Line).ToList();
        var turnsWith = flagged.SelectMany(l => l.Turns).Distinct().Count();

        var highest = flagged.Count == 0 ? IntegritySeverity.Info : flagged.Max(l => l.Highest);
        var level = flagged.Count == 0 || highest == IntegritySeverity.Info ? Nothing
            : highest >= IntegritySeverity.Medium || flagged.Sum(l => l.Count) >= 5 ? Review
            : Minor;
        var headline = level switch
        {
            Nothing => "No integrity signals worth attention.",
            Minor => $"A few minor signals{(turnsWith > 0 ? $" around {Plural(turnsWith, "turn")}" : "")}; likely nothing, but they're listed below.",
            _ => $"Signals worth a look{(turnsWith > 0 ? $" around {Plural(turnsWith, "turn")}" : "")}. Read those answers and judge for yourself; nothing here is proof of anything.",
        };
        return new IntegritySummary(level, headline, flagged, normal, turnsWith);
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    public static string Duration(TimeSpan d) =>
        d <= TimeSpan.Zero ? "" : d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} min {d.Seconds} s" : $"{d.Seconds} s";
}
