using Gadfly.Server.Integrity;
using Gadfly.Server.Voice;
using Gadfly.Sources.Verification;

namespace Gadfly.Server.Student;

/// <summary>One step of the practice interview: what the examiner says, and what the page explains beside it.</summary>
/// <param name="Key">Stable name of the step (tests and the page's step list use it).</param>
/// <param name="Title">The step's short title, shown in the step list.</param>
/// <param name="Points">What the page shows while the examiner speaks this step: the same advice, to read at your own pace.</param>
/// <param name="WantsAnswer">True when the examiner waits for a spoken answer; false for the closing step.</param>
public sealed record PracticeStep(string Key, string Title, IReadOnlyList<string> Points, bool WantsAnswer);

/// <summary>
/// "Meet your examiner" (GAD-T294): an ungraded practice interview any student can take from My exams at any time. The real
/// exam room's talking examiner (<c>VoiceRoom</c>), camera self-view, eye-contact check and fade run on it, but everything the
/// examiner says is fixed text written here, so it never asks the AI panel for anything (in live mode a practice costs only the
/// examiner's voice and the speech recognizer, never an LLM call) and it works the same in mock mode.
/// <para>
/// Nothing about a practice is stored. This class has no database: it is created by the practice page, lives in that page's
/// memory, and is gone when the page closes. There is no exam session, no assignment, no attempt, no turn, no grade, no report,
/// no integrity note and no audit row. <see cref="Id"/> is a throwaway id that names no row anywhere; it exists only so the
/// voice hubs can be armed for this student (<see cref="VoiceGrants"/> is in memory too). Eye-contact episodes the browser
/// reports are dropped (<see cref="DropGazeEpisodes"/>).
/// </para>
/// </summary>
public sealed class PracticeInterview
{
    /// <summary>How long the examiner "thinks" after each practice answer, so the student hears the wait as it is in an exam.</summary>
    public static readonly TimeSpan ThinkingPause = TimeSpan.FromSeconds(3.5);

    /// <summary>The practice clock, so the student can watch it stop during a pause. Nothing ends when it runs out.</summary>
    public static readonly TimeSpan ClockBudget = TimeSpan.FromMinutes(10);

    /// <summary>The most paused time given back, as in an exam (the real figure is per exam: 10 minutes, or half a short exam).</summary>
    public static readonly TimeSpan PauseAllowance = TimeSpan.FromMinutes(10);

    public const string Welcome = "welcome";
    public const string AsHeard = "as-heard";
    public const string DontKnow = "dont-know";
    public const string EyeContact = "eye-contact";
    public const string Pause = "pause";
    public const string Ready = "ready";

    /// <summary>The fixed practice material: the steps in order. The examiner's words are in <see cref="Line"/>.</summary>
    public static readonly IReadOnlyList<PracticeStep> Steps =
    [
        new(Welcome, "Speaking an answer",
        [
            "Answer out loud, in your own words. What you say appears under \"What I heard\" as you speak.",
            "Stop talking and a short countdown appears. When it runs out, your answer is sent. Speaking again cancels it, so you can pause to think.",
            "\"Done answering\" sends it at once. \"Repeat the question\" has the examiner say it again.",
        ], true),
        new(AsHeard, "Your answer is taken as heard",
        [
            "The examiner doesn't read your answer back. Once it is sent it is taken as heard, and the exam moves on, like a conversation with a person.",
            "You can't change an answer afterwards, so say what you mean before the countdown ends. Until then you can keep talking, or press \"Clear and answer again\" to start over.",
            "If a word is misheard, you don't need to do anything: the person who grades your exam sees which words the recognizer was unsure of.",
            "Try it: start answering, press \"Clear and answer again\", then answer again.",
        ], true),
        new(DontKnow, "Saying \"I don't know\"",
        [
            "After each answer the examiner takes a moment. You may hear a short line such as \"Let me think\" and a quiet sound of pages turning. It says nothing about how your answer went.",
            "\"I don't know\" is a real, honest answer. It never fails you on its own: the examiner tries another angle or moves on.",
            "Try it: if you don't know the answer to this one, just say \"I don't know\".",
        ], true),
        new(EyeContact, "Eye contact and the screen fade",
        [
            "While you answer, look at the examiner, as you would with a person. Your camera checks this on your device only; no video is recorded or sent.",
            "Look away for a while during an answer and the screen slowly fades with \"Please look at the examiner\". Look back and it clears at once. If you keep looking away, she asks you out loud to look at her.",
            "Short glances while you think are fine, and you get extra time at the start of an answer.",
            "Try it: start answering, then look away from the screen for a few seconds and watch the screen.",
        ], true),
        new(Pause, "If the microphone or camera stops",
        [
            "If your microphone or camera stops working, or the connection drops, the exam shows \"The exam is paused\" with what went wrong and how to fix it.",
            "Fix it and press \"Try again\": the examiner asks the question again.",
            "Once the first question has been asked, the clock stops while the exam is paused, for up to 10 minutes in all. Watch the practice clock above if you try it.",
            "Try it if you like: unplug your microphone or turn off your camera, then put it back and press \"Try again\". When you're done, say \"ready\".",
        ], true),
        new(Ready, "You're ready",
        [
            "In your real exam the questions are about your course, and each one follows from your last answer.",
            "Your microphone and camera must stay on for the whole exam. A typed-answer or eye-contact accommodation can only be granted by your instructor.",
            "You can take this practice again at any time from My exams.",
        ], false),
    ];

    /// <summary>What the examiner says to open each step (and, from the second step on, after the student's last answer).</summary>
    private static readonly IReadOnlyDictionary<string, string> Lines = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Welcome] = "Hello, I'm your examiner. This is only practice: nothing you say here is kept, graded, or counted as an attempt. "
            + "I ask each question out loud, and you answer out loud, in your own words. Let's try one. "
            + "In a sentence or two, what are you studying at the moment? When you stop talking, a short countdown sends your answer.",
        [AsHeard] = "Thank you. Notice that I didn't read your answer back. In the exam, once your answer is sent, it is taken as I heard it, "
            + "and we move on, just like talking with a person. You can't change it afterwards, so say what you mean before the countdown ends. "
            + "Until then you can keep talking, or press Clear and answer again to start over. Let's try that. "
            + "Tell me about something you enjoy doing, and partway through, press Clear and answer again, then start over.",
        [DontKnow] = "Thank you. While I was thinking just now, you may have heard me say something short, and a quiet sound of pages turning. "
            + "That's just me taking a moment. It says nothing about how your answer went. "
            + "Next: not knowing. Sometimes I'll ask something you can't answer. Saying I don't know is honest, and it's fine. "
            + "Try it now. What is the capital city of Burkina Faso? If you're not sure, just say I don't know.",
        [EyeContact] = "Now, eye contact. While you answer, look at me, as you would with a person. "
            + "Your camera checks this on your device only. No video is recorded or sent. "
            + "If you look away for a while during an answer, the screen slowly fades and asks you to look back, and it clears as soon as you do. "
            + "Short glances while you think are fine. Try it: describe the room you're in, and partway through, look away from the screen for a few seconds.",
        [Pause] = "If the screen faded, you saw it clear as soon as you looked back. Here, nothing about it is kept. "
            + "Last thing: if your microphone or camera stops working, or your connection drops, the exam pauses. "
            + "The screen tells you what went wrong. Fix it, press Try again, and I'll ask the question again. "
            + "The clock stops while you're paused, for up to ten minutes in all. "
            + "If you like, try it now: unplug your microphone or turn off your camera, then put it back and press Try again. "
            + "When you're ready to finish, just say ready.",
        [Ready] = "That's everything. In your real exam, the questions will be about your course, and each one follows from your last answer. "
            + "Take your time, answer in your own words, and say I don't know when you don't. You're ready. Good luck.",
    };

    /// <summary>Said before the eye-contact step when the last answer sounded like "I don't know".</summary>
    public const string AporiaThanks = "That was an honest answer, and it's exactly right to say so. I'd simply try another angle, or move on.";

    /// <summary>Said before the eye-contact step when the student answered the question anyway.</summary>
    public const string AnsweredThanks = "Thank you. Whatever you knew, remember that I don't know is always an allowed answer.";

    /// <summary>A throwaway id for this practice. It names no exam session or any other row; it only arms the voice hubs.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The step the examiner is on (0-based).</summary>
    public int StepIndex { get; private set; }

    /// <summary>
    /// Changes every time the examiner moves to a step, including after <see cref="Restart"/>, so the voice room speaks each step
    /// once and an answer given to an earlier step is recognised as stale.
    /// </summary>
    public int Turn { get; private set; }

    /// <summary>Whether the last "I don't know" step was answered with "I don't know" (only changes what the examiner says next).</summary>
    public bool SaidIDontKnow { get; private set; }

    /// <summary>How many answers the student gave (counted in memory for the page, never stored).</summary>
    public int AnswersGiven { get; private set; }

    public PracticeStep Current => Steps[StepIndex];

    public bool IsFinished => StepIndex == Steps.Count - 1;

    /// <summary>True while the examiner is waiting for a spoken answer to this step.</summary>
    public bool WantsAnswer => Current.WantsAnswer;

    /// <summary>What the examiner says for the current step.</summary>
    public string Line => LineFor(Current.Key, SaidIDontKnow);

    /// <summary>The examiner's words for a step; the eye-contact step starts with her reaction to the "I don't know" step.</summary>
    public static string LineFor(string key, bool saidIDontKnow) =>
        key == EyeContact ? (saidIDontKnow ? AporiaThanks : AnsweredThanks) + " " + Lines[key] : Lines[key];

    /// <summary>Every line the examiner could say in a practice (the step lines with both reactions).</summary>
    public static IEnumerable<string> AllLines() =>
        Steps.SelectMany(s => s.Key == EyeContact ? new[] { LineFor(s.Key, true), LineFor(s.Key, false) } : new[] { LineFor(s.Key, false) });

    /// <summary>
    /// The student answered <paramref name="turn"/> with <paramref name="heard"/>. An answer to an earlier turn (it arrived after
    /// the page moved on) is ignored. Returns true when the practice moved to the next step. The words are looked at only to tell
    /// an "I don't know" apart, then dropped.
    /// </summary>
    public bool Answer(int? turn, string? heard)
    {
        if (turn is { } t && t != Turn) return false;
        if (!WantsAnswer) return false;
        AnswersGiven++;
        if (Current.Key == DontKnow) SaidIDontKnow = SoundsLikeIDontKnow(heard);
        return Next();
    }

    /// <summary>Moves on without an answer (the page's "Skip this step"). False on the last step.</summary>
    public bool Next()
    {
        if (IsFinished) return false;
        StepIndex++;
        Turn++;
        return true;
    }

    /// <summary>Back to the first step, as if new; the clock starts again.</summary>
    public void Restart()
    {
        StepIndex = 0;
        Turn++;
        SaidIDontKnow = false;
        AnswersGiven = 0;
        Clock = new PracticeClock(ClockBudget, PauseAllowance);
    }

    /// <summary>The practice clock (in memory, like everything else here).</summary>
    public PracticeClock Clock { get; private set; } = new(ClockBudget, PauseAllowance);

    /// <summary>
    /// The eye-contact check reports episodes (looked away, faded...) as it would in an exam. In a practice they are dropped here:
    /// never stored, never sent anywhere. Returns true so the browser keeps the check running.
    /// </summary>
    public static bool DropGazeEpisodes(IReadOnlyCollection<Sessions.HubIntegrityEvent>? episodes) => true;

    /// <summary>
    /// The examiner's spoken eye-contact remark for <paramref name="kind"/> (the same fixed words as in an exam), offered to the
    /// avatar hub for this practice only. Null when there is nothing to say or voice isn't armed.
    /// </summary>
    public string? Remark(VoiceGrants grants, Guid user, string? kind)
    {
        if (kind is null || !WebcamIntegrityRecorder.ExaminerRemarks.TryGetValue(kind, out var text)) return null;
        // A reminder (GAD-T561) is a warm line, never the same twice in one practice; once all are said she starts over.
        if (kind is "look-away" or "reading")
        {
            if (WebcamIntegrityRecorder.NextReminder(remindersSaid) is not { } line)
            {
                remindersSaid.Clear();
                line = WebcamIntegrityRecorder.NextReminder(remindersSaid)!;
            }
            text = line;
        }
        return grants.Offer(user, Id, text) ? text : null;
    }

    private readonly HashSet<string> remindersSaid = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether a spoken answer sounds like "I don't know": the exam's own aporia word, or the usual ways of saying it. Only used to
    /// pick the examiner's next words in a practice; an exam judges answers through the panel, never with this.
    /// </summary>
    public static bool SoundsLikeIDontKnow(string? heard)
    {
        if (string.IsNullOrWhiteSpace(heard)) return false;
        if (ResponseVerifier.IsAporiaWord(heard)) return true;
        var t = heard.ToLowerInvariant().Replace('’', '\'');
        string[] phrases = ["don't know", "dont know", "do not know", "no idea", "not sure", "no clue", "can't remember", "cannot remember", "don't remember"];
        return phrases.Any(t.Contains);
    }
}

/// <summary>
/// The practice clock (GAD-T294): counts down once the examiner first speaks, stops while the room is paused, and gives the paused
/// time back up to an allowance, the way an exam's clock does (SocraticSession's pause handling). In memory only.
/// </summary>
public sealed class PracticeClock(TimeSpan budget, TimeSpan allowance)
{
    public TimeSpan Budget { get; } = budget;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? PausedAt { get; private set; }

    /// <summary>Paused time already given back.</summary>
    public TimeSpan Credited { get; private set; }

    public bool IsStarted => StartedAt is not null;
    public bool IsPaused => PausedAt is not null;

    /// <summary>The examiner began speaking: the clock starts (once).</summary>
    public void Start(DateTimeOffset now) => StartedAt ??= now;

    /// <summary>The microphone, camera or connection went: the clock stops, but only once it has started.</summary>
    public void Pause(DateTimeOffset now)
    {
        if (StartedAt is null || PausedAt is not null) return;
        PausedAt = now;
    }

    /// <summary>The devices are back: the paused time is given back, up to what is left of the allowance.</summary>
    public void Resume(DateTimeOffset now)
    {
        if (PausedAt is not { } p) return;
        PausedAt = null;
        Credited += Clamp(now - p, TimeSpan.Zero, allowance - Credited);
    }

    /// <summary>Time left: the budget, less the time used, with paused time (so far, within the allowance) not counted.</summary>
    public TimeSpan Remaining(DateTimeOffset now)
    {
        if (StartedAt is not { } s) return Budget;
        var pausedNow = PausedAt is { } p ? Clamp(now - p, TimeSpan.Zero, allowance - Credited) : TimeSpan.Zero;
        var left = Budget - (now - s) + Credited + pausedNow;
        return Clamp(left, TimeSpan.Zero, Budget);
    }

    /// <summary>All the time given back so far, including an open pause.</summary>
    public TimeSpan GivenBack(DateTimeOffset now) =>
        Credited + (PausedAt is { } p ? Clamp(now - p, TimeSpan.Zero, allowance - Credited) : TimeSpan.Zero);

    private static TimeSpan Clamp(TimeSpan v, TimeSpan lo, TimeSpan hi) => v < lo ? lo : v > hi ? (hi < lo ? lo : hi) : v;
}
