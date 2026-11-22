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
        ["thinking-glance"] = new("Glanced away while thinking",
            "Normal. A short look away from the screen, the kind people make while recalling or reasoning. Shown so you can see it was noticed and judged ordinary; it is never counted.", true),
        ["gaze-off-screen"] = new("Looked off-screen for a while",
            "The head and eyes pointed away from the screen for several seconds. Long looks away can be thinking, reading something, or a distraction (a notification, a pet, a noise); read it against the answer given. Low confidence: it comes from head direction, not from where the eyes were exactly."),
        ["reading-pattern"] = new("Reading pattern off-screen",
            "Repeated sweeps and returns of the eyes toward one off-screen spot, the movement of reading text placed there. It can have other explanations; check it against the answers given. Evidence to weigh, never proof."),
        ["examiner-look-away"] = new("Looked away from the examiner while answering",
            "In the voice interview the student's head and eyes turned away from the examiner's face for a while during an answer (the summary says how long, and whether the examiner asked out loud for eye contact). Looking away is often thinking; read it against the answer. Low confidence: it comes from head and eye direction, not an exact point on the screen."),
        ["examiner-reading"] = new("Reading-like eyes while answering",
            "During an answer the eyes swept and jumped back away from the examiner, the movement of reading. The student may have been re-reading the question or transcript on screen; check it against the answer. Evidence to weigh, never proof."),
        ["examiner-face-absent"] = new("Face out of view while answering",
            "The camera saw no face for part of an answer in the voice interview. Often harmless (leaning out of frame, the camera moved); check whether the answer continued."),
        ["examiner-second-face"] = new("Second face while answering",
            "The camera saw more than one face during an answer in the voice interview. It may be a person, a photo or a reflection; look at whether the answer changed around then."),
        ["examiner-fade"] = new("Screen dimmed for eye contact",
            "The exam screen faded, with a warning to look at the examiner, because the student looked away while answering; it cleared when they looked back. The summary gives how long and how far it faded. It records what the student saw, not a finding.", true),
        ["camera-unavailable"] = new("Camera not used",
            "Neutral. The camera checks did not run (the student did not allow the camera, there was no camera, or it could not be opened). The exam continued normally and nothing is inferred from it.", true),
        ["webcam-capped"] = new("More camera events not stored",
            "The camera checks kept reporting events past the per-session limit, so later ones were not stored. Usually poor lighting or framing making the face flicker in and out; look at the events that were stored."),
        ["second-speaker"] = new("A second voice",
            "Another voice was heard during the answer. Could be coaching, or just someone else in the room; listen to the answer if audio is available."),
        ["whispering"] = new("Whispering heard",
            "Quiet speech was heard that wasn't the answer itself. Some students murmur while thinking; weigh it with the answers around it."),
        ["reading-cadence"] = new("Reading-aloud rhythm",
            "The answer's pace and pauses sounded like reading rather than talking. Weak evidence on its own; some people speak carefully when nervous."),
        ["fluent-after-silence"] = new("Long wait, then a smooth answer",
            "A long gap before the first word, followed by an answer with none of the usual hesitation. Common causes are thinking carefully before speaking, or a student who simply speaks smoothly once ready. Weak evidence: weigh it against how the student answered other questions."),
        ["tab-hidden"] = new("Exam tab hidden",
            "The exam tab was hidden (another tab or window was in front). Could be looking something up, or a notification or accident; check the time it lasted."),
        ["focus-lost"] = new("Window lost focus",
            "The exam window lost focus, usually a click on another window. Short blips are common and usually harmless."),
        ["fullscreen-exit"] = new("Left full screen",
            "The student left full-screen mode. Often an accidental key press; look at what happened next."),
        ["extra-screen"] = new("Extra screen connected",
            "The browser reported more than one screen. That alone is common (laptops on a monitor); it matters only with other signals."),
        ["paste"] = new("Pasted into the answer",
            "Text was pasted into the answer box rather than typed. Some students draft elsewhere or paste their own earlier words; compare the pasted answer with the student's other answers."),
        ["copy-question"] = new("Copied the question",
            "The question text was copied. Could be pasting it into a search or another tool, or just re-reading; check whether the tab was hidden or text pasted soon after."),
        ["devtools-open"] = new("Developer tools possibly open",
            "A weak guess from the window's size: developer tools may have been open. Window zoom and browser side panels look the same; ignore it unless other signals line up."),
        ["signals-capped"] = new("More browser signals not stored",
            "The browser kept reporting signals past the per-session limit, so later ones were not stored. Usually a flickering window or connection; look at the signals that were stored."),
        [TextStyleEventType] = new("Text style estimate (unvalidated)",
            "An unvalidated panel estimate that this answer reads more like text read aloud than the student's other answers. Estimates of this kind (detectors of machine-written text) misfire often, especially on non-native speakers, and this one's false-flag rate has not been measured. Context only: never a verdict, never a score, never meaningful on its own, and hidden from a blind grader until their grade is in."),
        [RecitationView.EventType] = new("Possibly recited",
            "The recitation check found this answer closely follows the source material's wording, or was a long, fluent spoken answer that began only after a long silent wait (a read-out pattern; never applied to a student with extra time or on an exam that switched it off, since thinking before speaking looks the same); the turn shows the overlap, the copied stretch and which pattern fired. The examiner did not count it as evidence of the student's own understanding, either way, and asked them to say it differently, apply it, or explain why; read the next answer. Students quote and memorize honestly, so this is never a verdict of cheating."),
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

    /// <summary>
    /// The integrity event a text-style estimate at or above the reviewer threshold becomes (GAD-T284): reviewer-only context,
    /// hidden from a blind grader until their grade is submitted.
    /// </summary>
    public const string TextStyleEventType = "text-reads-as-written";

    /// <summary>How the text-style score should be read. It is an unvalidated estimate (GAD-T284), and every reading says so.</summary>
    public static string TextStyle(double score) => score switch
    {
        >= 0.7 => "Unvalidated estimate: reads noticeably more like written text than this student's other answers. Its false-flag rate is not yet measured; weak evidence on its own, never a verdict, and it never changes the score.",
        >= 0.4 => "Unvalidated estimate: somewhat more polished than this student's other answers. Not meaningful on its own.",
        _ => "Unvalidated estimate: reads like this student's other spoken answers.",
    };
}
