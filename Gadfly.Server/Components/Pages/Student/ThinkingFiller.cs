using System.Diagnostics.Metrics;

namespace Gadfly.Server.Components.Pages.Student;

/// <summary>
/// What fills the examiner's wait while an answer is judged (GAD-T559, docs/research/attention-and-gaze.md 4.6 and section 5). The
/// default arm, "gesture", is a thinking gesture (a glance aside and a small nod) with her short spoken filler line: a natural filler
/// plus a thinking gesture improved perceived latency where an artificial processing sound did not (Maslych et al. 2025), and keystrokes
/// are the changing-state sound most disruptive to verbal thinking (Ellermeier and Zimmer 2014). The old typing ambience (GAD-T502)
/// is kept only as the "typing" arm of an A/B test, given to the share of sessions set in Gadfly:Voice:WaitTypingShare (0, the
/// default, turns it off). Each wait's length and lines said are counted per arm (meter Gadfly.ExamRoom) and logged with the session.
/// </summary>
public static class ThinkingFiller
{
    public const string Gesture = "gesture";
    public const string Typing = "typing";
    public const string ShareSetting = "Gadfly:Voice:WaitTypingShare";

    private static readonly Meter Meter = new("Gadfly.ExamRoom");
    private static readonly Histogram<double> WaitMs = Meter.CreateHistogram<double>("gadfly.examroom.wait_ms", "ms", "How long the examiner's judging wait lasted, by filler arm.");
    private static readonly Counter<long> Lines = Meter.CreateCounter<long>("gadfly.examroom.wait_lines", null, "Filler lines said during waits, by filler arm.");
    private static readonly Counter<long> Waits = Meter.CreateCounter<long>("gadfly.examroom.waits", null, "Judging waits, by filler arm.");

    /// <summary>The arm for a session: stable for the whole attempt (from its id), typing for <paramref name="typingShare"/> of sessions.</summary>
    public static string ArmFor(Guid session, double typingShare)
    {
        if (!(typingShare > 0)) return Gesture;
        var bucket = (BitConverter.ToUInt32(session.ToByteArray(), 0) % 10_000) / 10_000.0;
        return bucket < Math.Min(1, typingShare) ? Typing : Gesture;
    }

    /// <summary>One wait as the browser measured it; out-of-range values are ignored.</summary>
    public static bool Record(string? arm, double ms, int lines)
    {
        if (arm is not (Gesture or Typing) || !double.IsFinite(ms) || ms < 0 || ms > 10 * 60_000 || lines is < 0 or > 10) return false;
        var tag = new KeyValuePair<string, object?>("arm", arm);
        Waits.Add(1, tag);
        WaitMs.Record(ms, tag);
        if (lines > 0) Lines.Add(lines, tag);
        return true;
    }
}
