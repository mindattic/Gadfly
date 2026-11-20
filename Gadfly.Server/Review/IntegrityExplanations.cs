namespace Gadfly.Server.Review;

/// <summary>
/// Plain-language readings of integrity events for the reviewer (GAD-T54, docs/SOCRATIC_PLATFORM.md
/// "Eye movement" and "Anti-cheating layers"). Every event is evidence for a human to weigh, never a
/// verdict: the platform never infers deception or emotion and never auto-fails anyone (D2). Looking
/// up or away while answering is a well-supported sign of thinking, so it is shown as normal.
/// </summary>
public static class IntegrityExplanations
{
    private sealed record Entry(string Label, string Explanation, bool Normal = false);

    private static readonly Dictionary<string, Entry> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["looked-away"] = new("Looked away while thinking",
            "Normal. People look up or away from a screen to think while recalling or reasoning; it is not a sign of dishonesty.", true),
        ["gaze-away"] = new("Looked away while thinking",
            "Normal. People look up or away from a screen to think while recalling or reasoning; it is not a sign of dishonesty.", true),
        ["glance"] = new("Glanced away",
            "Normal. Brief glances away from the screen happen in every conversation and mean nothing on their own.", true),
        ["face-absent"] = new("No face in view",
            "The camera did not see a face for this stretch. Often harmless (the student leaned out of frame, adjusted the camera, or the lighting changed); check whether answers continued during it."),
        ["multiple-faces"] = new("More than one face",
            "The camera saw a second face. Could be someone helping, or someone simply walking past; look at whether the answers around this time changed."),
        ["identity-drift"] = new("Possibly a different person",
            "The face partway through did not closely match the one at the start. Camera angle and lighting cause false alarms; compare with the start of the session."),
        ["offscreen-reading"] = new("Reading pattern off-screen",
            "A sustained look at one off-screen spot with repeated left-to-right sweeps, the pattern of reading notes or another screen. The most meaningful webcam signal, but still evidence, not proof."),
        ["second-speaker"] = new("A second voice",
            "Another voice was heard during the answer. Could be coaching, or just someone else in the room; listen to the answer if audio is available."),
        ["whispering"] = new("Whispering heard",
            "Quiet speech was heard that wasn't the answer itself. Some students murmur while thinking; weigh it with the answers around it."),
        ["reading-cadence"] = new("Reading-aloud rhythm",
            "The answer's pace and pauses sounded like reading rather than talking. Weak evidence on its own; some people speak carefully when nervous."),
        ["tab-hidden"] = new("Exam tab hidden",
            "The exam tab was hidden (another tab or window was in front). Could be looking something up, or a notification or accident; check the time it lasted."),
        ["focus-lost"] = new("Window lost focus",
            "The exam window lost focus, usually a click on another window. Short blips are common and usually harmless."),
        ["fullscreen-exit"] = new("Left full screen",
            "The student left full-screen mode. Often an accidental key press; look at what happened next."),
        ["extra-screen"] = new("Extra screen connected",
            "The browser reported more than one screen. That alone is common (laptops on a monitor); it matters only with other signals."),
        ["text-reads-as-written"] = new("Answer reads like written text",
            "This answer reads more like text read aloud than the student's other answers. Weak evidence: never a score, and never meaningful on its own."),
    };

    /// <summary>A short label, an explanation, and whether this is normal behavior rather than a flag.</summary>
    public static (string Label, string Explanation, bool Normal) For(string type, string source)
    {
        if (Known.TryGetValue(type, out var e)) return (e.Label, e.Explanation, e.Normal);
        // Any gaze-related event we don't know by name is still just looking around.
        if (type.Contains("gaze", StringComparison.OrdinalIgnoreCase) || type.Contains("glance", StringComparison.OrdinalIgnoreCase)
            || type.Contains("look", StringComparison.OrdinalIgnoreCase))
            return ("Looked away", "Normal. Looking away from the screen is a sign of thinking, not of dishonesty.", true);
        var label = type.Replace('-', ' ');
        return (char.ToUpperInvariant(label.FirstOrDefault()) + label[Math.Min(1, label.Length)..],
            $"A {source} signal. Treat it as evidence to weigh with the answers around it, not as a finding.", false);
    }

    /// <summary>How the text-style score should be read.</summary>
    public static string TextStyle(double score) => score switch
    {
        >= 0.7 => "Reads noticeably more like written text than this student's other answers. Weak evidence on its own; it never changes the score.",
        >= 0.4 => "Somewhat more polished than this student's other answers. Not meaningful on its own.",
        _ => "Reads like this student's other spoken answers.",
    };
}
