namespace Gadfly.Server.Voice;

/// <summary>
/// What the examiner says while the panel judges an answer and writes the next question (GAD-T290), so the student doesn't sit in
/// dead air. The lines are fixed here, never text from the browser, and are neutral on purpose: about her taking a moment, never
/// about the answer (no "good", "right" or "interesting"), and chosen at random, so they carry no hint of how it was judged. Each is
/// spoken with the avatar hub's fixed-phrase cache, so a line is synthesized once per server and every later use costs nothing.
/// The browser says the first line only when the next question isn't ready within <see cref="FirstAfter"/>, and the second only on
/// a long wait (<see cref="AgainAfter"/>). They are not questions: not listed in the transcript and never part of the record.
/// </summary>
public static class ExaminerFiller
{
    /// <summary>A question ready sooner than this needs no filler.</summary>
    public static readonly TimeSpan FirstAfter = TimeSpan.FromSeconds(2);

    /// <summary>Still waiting this long after the judging began: one more short line.</summary>
    public static readonly TimeSpan AgainAfter = TimeSpan.FromSeconds(20);

    /// <summary>Said once the wait has gone past <see cref="FirstAfter"/>.</summary>
    public static readonly IReadOnlyList<string> First =
    [
        "Let me think about that.",
        "Okay, give me a moment.",
        "Alright, one moment.",
        "Let me consider that for a moment.",
        "Okay. Bear with me a moment.",
        "Let me think.",
    ];

    /// <summary>Said once more when the wait runs long.</summary>
    public static readonly IReadOnlyList<string> Again =
    [
        "Just a moment more.",
        "Bear with me, nearly there.",
        "Still thinking. One moment.",
    ];

    /// <summary>A first and a long-wait line, the first never the same as <paramref name="lastFirst"/> so turns in a row vary.</summary>
    public static (string First, string Again) Pick(Random random, string? lastFirst)
    {
        var choices = First.Where(t => t != lastFirst).ToArray();
        return (choices[random.Next(choices.Length)], Again[random.Next(Again.Count)]);
    }
}
