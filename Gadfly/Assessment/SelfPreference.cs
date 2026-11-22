namespace Gadfly.Assessment;

/// <summary>
/// One grading member's level on one objective of one session, with what it is compared against.
/// </summary>
/// <param name="Member">The member's provider family (e.g. "openai").</param>
/// <param name="Level">The member's own rubric level for the objective (0..5).</param>
/// <param name="PeerLevel">The median level of the other members on the same answers; null when it graded alone.</param>
/// <param name="HumanLevel">The human examiner's blind level, when there is one.</param>
/// <param name="OwnShare">The share (0..1) of the model-written questions on this objective that the member's own family wrote.</param>
public sealed record SelfPreferenceObservation(string Member, int Level, double? PeerLevel, int? HumanLevel, double OwnShare);

/// <summary>One member's comparison: its levels on answers to its own family's questions against answers to others'.</summary>
public sealed record SelfPreferenceRow(
    string Member,
    int OwnAnswers,
    int OtherAnswers,
    double? OwnVsPeers,
    double? OtherVsPeers,
    double? LiftVsPeers,
    double? LiftLow,
    double? LiftHigh,
    double? OwnVsHuman,
    double? OtherVsHuman,
    double? LiftVsHuman,
    bool Flagged,
    string Finding);

/// <summary>
/// The self-preference check (GAD-T431; ElenchusAtScale Sections 3.8, 5.4.2, M12; Panickssery et al. 2024). For each grading
/// member, its levels on answers to questions its own family wrote are compared with its levels on answers to questions other
/// families wrote, relative to the other members on the same answers (and to the blind human where there is one), so a harder
/// or easier set of questions does not look like favouritism. An objective counts as "own" when the member's family wrote at
/// least half of its model-written questions in that session, "other" when it wrote none; mixed ones are left out.
/// A positive lift whose 95% interval stays above zero, on enough answers each way, is flagged as possible self-preference.
/// </summary>
public static class SelfPreference
{
    public const int MinPerGroup = 10;
    public const double OwnAtLeast = 0.5;

    public static IReadOnlyList<SelfPreferenceRow> Analyze(IEnumerable<SelfPreferenceObservation> observations)
    {
        var rows = new List<SelfPreferenceRow>();
        foreach (var g in observations.GroupBy(o => o.Member.Trim().ToLowerInvariant()).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var own = g.Where(o => o.OwnShare >= OwnAtLeast).ToList();
            var other = g.Where(o => o.OwnShare == 0).ToList();
            var ownPeer = own.Where(o => o.PeerLevel is not null).Select(o => o.Level - o.PeerLevel!.Value).ToList();
            var otherPeer = other.Where(o => o.PeerLevel is not null).Select(o => o.Level - o.PeerLevel!.Value).ToList();
            var ownHuman = own.Where(o => o.HumanLevel is not null).Select(o => (double)(o.Level - o.HumanLevel!.Value)).ToList();
            var otherHuman = other.Where(o => o.HumanLevel is not null).Select(o => (double)(o.Level - o.HumanLevel!.Value)).ToList();

            double? lift = null, low = null, high = null;
            if (ownPeer.Count > 0 && otherPeer.Count > 0)
            {
                lift = ownPeer.Average() - otherPeer.Average();
                var se = Math.Sqrt(Var(ownPeer) / ownPeer.Count + Var(otherPeer) / otherPeer.Count);
                (low, high) = (lift - 1.96 * se, lift + 1.96 * se);
            }
            double? liftHuman = ownHuman.Count > 0 && otherHuman.Count > 0 ? ownHuman.Average() - otherHuman.Average() : null;
            var enough = ownPeer.Count >= MinPerGroup && otherPeer.Count >= MinPerGroup;
            var flagged = enough && low > 0;
            var finding = !enough
                ? $"Not enough answers to tell: {ownPeer.Count} to its own family's questions and {otherPeer.Count} to others' (needs {MinPerGroup} each, graded with other members)."
                : flagged ? $"Possible self-preference: on answers to its own family's questions it grades {lift:0.00} levels higher, relative to the other members, than on answers to other families' questions (95% interval {low:0.00} to {high:0.00})."
                : high < 0 ? $"It grades answers to its own family's questions lower relative to the other members ({lift:0.00} levels): not self-preference."
                : $"No self-preference detected: the difference ({lift:0.00} levels, 95% interval {low:0.00} to {high:0.00}) includes zero.";
            rows.Add(new SelfPreferenceRow(g.Key, own.Count, other.Count, Mean(ownPeer), Mean(otherPeer), Round(lift), Round(low), Round(high),
                Mean(ownHuman), Mean(otherHuman), Round(liftHuman), flagged, finding));
        }
        return rows;
    }

    /// <summary>Median of the other members' levels, or null when there are none.</summary>
    public static double? PeerMedian(IReadOnlyList<int> others)
    {
        if (others.Count == 0) return null;
        var s = others.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2.0;
    }

    private static double Var(List<double> xs)
    {
        if (xs.Count < 2) return 0;
        var m = xs.Average();
        return xs.Sum(x => (x - m) * (x - m)) / (xs.Count - 1);
    }

    private static double? Mean(List<double> xs) => xs.Count == 0 ? null : Math.Round(xs.Average(), 4);
    private static double? Round(double? x) => x is { } v ? Math.Round(v, 4) : null;
}
