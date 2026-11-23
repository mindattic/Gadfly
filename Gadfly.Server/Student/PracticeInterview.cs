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
/// exam room's talking examiner (<c>VoiceRoom</c>: her judging wait with the thinking gesture and a short line, GAD-T559), camera,
/// and eye-contact check with its gentle reminder ladder (examiner-gaze.js, GAD-T557/GAD-T561) run on it unchanged, but everything the
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
    /// <summary>
    /// How long the examiner "thinks" after each practice answer, so the student meets the wait as it is in an exam: long enough
    /// (past <see cref="Voice.ExaminerFiller.FirstAfter"/> and the gap after her acknowledgement) for her thinking gesture and one
    /// short line to show (GAD-T559, GAD-T624).
    /// </summary>
    public static readonly TimeSpan ThinkingPause = TimeSpan.FromSeconds(4.5);

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
            "After each answer the examiner takes a moment. You may see the examiner glance aside, as people do when they think, and hear a short line such as \"Let me think\". It says nothing about how your answer went.",
            "\"I don't know\" is a real, honest answer. It never fails you on its own: the examiner tries another angle or moves on.",
            "Try it: if you don't know the answer to this one, just say \"I don't know\".",
        ], true),
        new(EyeContact, "Eye contact and gentle reminders",
        [
            "While you answer, look at the examiner, as you would with a person; there's no need to stare. Your camera checks this on your device only; no video is recorded or sent.",
            "Looking away to think is normal: nothing happens before you start speaking, short glances while you speak are fine, and anywhere on the screen counts as looking at it.",
            "Only a long look off the screen while you speak (about 8 seconds) brings a reminder, gentlest first: the examiner tilts their head a little; then the screen around the examiner dims slightly and clears as soon as you look back; at most, one short, friendly line at a pause. Nothing flashes and there is no warning text.",
            "Try it if you like: describe the room, and keep talking while you look away from the screen for about 15 seconds, then look back.",
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
        [DontKnow] = "Thank you. While I was thinking just now, you may have seen me glance aside and heard me say something short. "
            + "That's just me taking a moment, the way people do. It says nothing about how your answer went. "
            + "Next: not knowing. Sometimes I'll ask something you can't answer. Saying I don't know is honest, and it's fine. "
            + "Try it now. What is the capital city of Burkina Faso? If you're not sure, just say I don't know.",
        [EyeContact] = "Now, eye contact. While you answer, look at me, as you would with a person. There's no need to stare. "
            + "Your camera checks this on your device only. No video is recorded or sent. "
            + "Looking away to think is normal: nothing happens while you work out your answer, and short glances while you talk are fine. "
            + "Only if you look off the screen for a long time while you speak, I tilt my head a little, the screen around me dims slightly "
            + "and clears as soon as you look back, and at a pause I may say one short, friendly line. "
            + "Try it: describe the room you're in. If you'd like to see the reminder, keep talking while you look away from the screen for about fifteen seconds.",
        [Pause] = "If you saw me react, or the screen dim a little, it cleared as soon as you looked back. Here, nothing about it is kept. "
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

    // ------------------------------------------------------------------------------------------ the practice in Korean (GAD-T544)
    // The same steps and lines, said the way a warm Korean examiner would (해요체, with 합쇼체 for her greeting), following the
    // glossary in docs/LOCALIZATION.md. Change both languages together.

    public static readonly IReadOnlyList<PracticeStep> KoreanSteps =
    [
        new(Welcome, "말로 답하기",
        [
            "자신의 말로 소리 내어 답해 주세요. 말하는 동안 \"들은 내용\" 아래에 답이 표시돼요.",
            "말을 멈추면 짧은 카운트다운이 나타나고, 끝나면 답이 제출돼요. 다시 말하면 카운트다운이 취소되니, 생각하느라 잠시 멈춰도 괜찮아요.",
            "\"답변 완료\"를 누르면 바로 제출돼요. \"질문 다시 듣기\"를 누르면 시험관이 질문을 다시 말해 줘요.",
        ], true),
        new(AsHeard, "답은 들은 그대로 처리돼요",
        [
            "시험관은 답을 다시 읽어 주지 않아요. 제출된 답은 들은 그대로 처리되고, 사람과 대화하듯 시험이 이어져요.",
            "제출한 뒤에는 답을 바꿀 수 없으니, 카운트다운이 끝나기 전에 하고 싶은 말을 해 주세요. 그 전까지는 계속 말하거나 \"지우고 다시 답하기\"를 눌러 처음부터 다시 할 수 있어요.",
            "단어를 잘못 알아들어도 따로 할 일은 없어요. 채점자는 음성 인식이 확신하지 못한 단어가 무엇인지 볼 수 있어요.",
            "해 보세요: 답을 시작했다가 \"지우고 다시 답하기\"를 누르고, 다시 답해 보세요.",
        ], true),
        new(DontKnow, "\"잘 모르겠어요\"라고 말하기",
        [
            "답을 하면 시험관은 잠시 생각해요. 사람이 생각할 때처럼 잠깐 옆을 보고 \"잠시만요\" 같은 짧은 말을 할 수 있지만, 답이 어땠는지와는 아무 관계가 없어요.",
            "\"잘 모르겠어요\"도 솔직하고 엄연한 답이에요. 그것만으로 불합격이 되지 않으며, 시험관은 다른 방향으로 묻거나 다음으로 넘어가요.",
            "해 보세요: 이 질문의 답을 모르면 \"잘 모르겠어요\"라고 말하면 돼요.",
        ], true),
        new(EyeContact, "시선 맞추기와 부드러운 알림",
        [
            "답하는 동안에는 사람과 이야기하듯 시험관을 봐 주세요. 뚫어지게 볼 필요는 없어요. 카메라는 이 기기 안에서만 확인하며, 영상은 기록되거나 전송되지 않아요.",
            "생각하느라 다른 곳을 보는 건 자연스러운 일이에요. 말을 시작하기 전에는 아무 일도 없고, 말하는 중에 잠깐 시선을 돌려도 괜찮으며, 화면 안이라면 어디를 봐도 화면을 보는 것으로 쳐요.",
            "말하는 중에 화면 밖을 오래(약 8초) 볼 때만, 가장 부드러운 것부터 알려 줘요. 시험관이 고개를 살짝 기울이고, 그다음 시험관 주변 화면이 살짝 어두워졌다가 다시 보면 바로 돌아오며, 많아야 말이 잠시 멈출 때 짧고 친절한 한마디를 해요. 화면이 번쩍이거나 경고 문구가 뜨지는 않아요.",
            "원하면 해 보세요: 방을 설명하면서, 말을 계속하는 채로 15초쯤 화면 밖을 봤다가 다시 보세요.",
        ], true),
        new(Pause, "마이크나 카메라가 멈추면",
        [
            "마이크나 카메라가 멈추거나 연결이 끊기면, 시험 화면에 \"시험이 일시 정지되었어요\"와 함께 무엇이 문제이고 어떻게 고치는지 나와요.",
            "문제를 해결하고 \"다시 시도\"를 누르면 시험관이 질문을 다시 해요.",
            "첫 질문을 한 뒤에는 일시 정지된 동안 시간이 멈춰요(모두 합해 최대 10분). 직접 해 본다면 위의 연습 시계를 지켜보세요.",
            "원하면 해 보세요: 마이크를 뽑거나 카메라를 끈 뒤 다시 연결하고 \"다시 시도\"를 누르세요. 다 되면 \"준비됐어요\"라고 말해 주세요.",
        ], true),
        new(Ready, "준비가 끝났어요",
        [
            "실제 시험에서는 수업 내용에 대해 질문하며, 각 질문은 바로 앞의 답에서 이어져요.",
            "마이크와 카메라는 시험 내내 켜져 있어야 해요. 입력 답변이나 시선 맞추기 편의 지원은 담당 교수자만 허락할 수 있어요.",
            "이 연습은 내 시험에서 언제든 다시 할 수 있어요.",
        ], false),
    ];

    private static readonly IReadOnlyDictionary<string, string> KoreanLines = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Welcome] = "안녕하세요, 저는 앞으로 시험을 함께할 시험관입니다. 지금은 연습이에요. 여기서 하는 말은 저장되지도, 채점되지도, 응시 횟수에 들어가지도 않아요. "
            + "제가 질문을 소리 내어 하면, 자신의 말로 소리 내어 답해 주세요. 한번 해 볼까요? "
            + "요즘 무엇을 공부하고 있는지 한두 문장으로 말해 주세요. 말을 멈추면 짧은 카운트다운이 지나고 답이 제출돼요.",
        [AsHeard] = "고마워요. 제가 답을 다시 읽어 드리지 않았죠? 시험에서는 답이 제출되면 제가 들은 그대로 처리되고, "
            + "사람과 대화하듯 다음으로 넘어가요. 제출한 뒤에는 바꿀 수 없으니, 카운트다운이 끝나기 전에 하고 싶은 말을 해 주세요. "
            + "그 전까지는 계속 말하거나, 지우고 다시 답하기를 눌러 처음부터 다시 할 수 있어요. 한번 해 봐요. "
            + "즐겨 하는 일에 대해 이야기하다가, 중간에 지우고 다시 답하기를 누르고 다시 시작해 보세요.",
        [DontKnow] = "고마워요. 방금 제가 생각하는 동안 잠깐 옆을 보며 짧은 말을 하는 걸 보셨을 수도 있어요. "
            + "그건 사람들이 그러듯 제가 잠시 생각하는 것일 뿐, 답이 어땠는지와는 아무 관계가 없어요. "
            + "다음은 모를 때예요. 가끔은 답할 수 없는 질문을 할 수도 있어요. 잘 모르겠다고 말하는 건 솔직한 답이고, 괜찮아요. "
            + "지금 해 볼까요? 부르키나파소의 수도는 어디일까요? 잘 모르겠으면 그냥 잘 모르겠어요라고 말해 주세요.",
        [EyeContact] = "이제 시선 맞추기예요. 답하는 동안에는 사람과 이야기하듯 저를 봐 주세요. 뚫어지게 볼 필요는 없어요. "
            + "카메라는 이 기기 안에서만 확인하고, 영상은 기록되거나 전송되지 않아요. "
            + "생각하느라 다른 곳을 보는 건 자연스러운 일이에요. 답을 생각하는 동안에는 아무 일도 없고, 말하다가 잠깐 시선을 돌려도 괜찮아요. "
            + "말하는 중에 화면 밖을 오래 볼 때만, 제가 고개를 살짝 기울이고, 제 주변 화면이 살짝 어두워졌다가 다시 보면 바로 돌아와요. "
            + "말이 잠시 멈출 때 짧고 친절한 한마디를 할 수도 있어요. "
            + "해 볼까요? 지금 있는 방을 설명해 주세요. 알림을 보고 싶다면, 말을 계속하면서 15초쯤 화면 밖을 봐 보세요.",
        [Pause] = "제가 반응하거나 화면이 살짝 어두워졌다면, 다시 보자마자 원래대로 돌아오는 걸 보셨을 거예요. 여기서는 그 기록도 남지 않아요. "
            + "마지막이에요. 마이크나 카메라가 멈추거나 연결이 끊기면 시험이 일시 정지돼요. "
            + "화면에 무엇이 문제인지 나와요. 문제를 해결하고 다시 시도를 누르면 제가 질문을 다시 할게요. "
            + "일시 정지된 동안에는 시간이 멈추고, 모두 합해 최대 10분까지예요. "
            + "원하면 지금 해 보세요. 마이크를 뽑거나 카메라를 껐다가 다시 연결하고 다시 시도를 누르면 돼요. "
            + "마칠 준비가 되면 준비됐어요라고 말해 주세요.",
        [Ready] = "이게 전부예요. 실제 시험에서는 수업 내용에 대해 질문하고, 각 질문은 바로 앞의 답에서 이어져요. "
            + "천천히, 자신의 말로 답하고, 모를 때는 잘 모르겠어요라고 말해 주세요. 이제 준비됐어요. 시험 잘 보세요.",
    };

    /// <summary>The Korean of <see cref="AporiaThanks"/>.</summary>
    public const string KoreanAporiaThanks = "솔직한 답이었고, 그렇게 말하는 게 바로 맞아요. 저라면 다른 방향으로 다시 묻거나 다음으로 넘어갈 거예요.";

    /// <summary>The Korean of <see cref="AnsweredThanks"/>.</summary>
    public const string KoreanAnsweredThanks = "고마워요. 알고 있었든 아니든, 잘 모르겠어요는 언제나 허용되는 답이라는 걸 기억해 주세요.";

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

    public PracticeStep Current => StepsFor(Language)[StepIndex];

    /// <summary>The steps in <paramref name="language"/> (GAD-T544): Korean when the practice runs in Korean, else English. Same keys and order.</summary>
    public static IReadOnlyList<PracticeStep> StepsFor(string? language) => Gadfly.Engine.ExamLanguage.IsKorean(language) ? KoreanSteps : Steps;

    public bool IsFinished => StepIndex == Steps.Count - 1;

    /// <summary>True while the examiner is waiting for a spoken answer to this step.</summary>
    public bool WantsAnswer => Current.WantsAnswer;

    /// <summary>What the examiner says for the current step.</summary>
    public string Line => LineFor(Current.Key, SaidIDontKnow, Language);

    /// <summary>The examiner's words for a step; the eye-contact step starts with her reaction to the "I don't know" step.</summary>
    public static string LineFor(string key, bool saidIDontKnow) => LineFor(key, saidIDontKnow, null);

    /// <summary>The examiner's words for a step in <paramref name="language"/> (GAD-T544): Korean, else English.</summary>
    public static string LineFor(string key, bool saidIDontKnow, string? language)
    {
        var korean = Gadfly.Engine.ExamLanguage.IsKorean(language);
        var lines = korean ? KoreanLines : Lines;
        if (key != EyeContact) return lines[key];
        var reaction = saidIDontKnow ? (korean ? KoreanAporiaThanks : AporiaThanks) : (korean ? KoreanAnsweredThanks : AnsweredThanks);
        return reaction + " " + lines[key];
    }

    /// <summary>Every line the examiner could say in a practice (the step lines with both reactions), in English and Korean.</summary>
    public static IEnumerable<string> AllLines() =>
        new string?[] { null, Gadfly.Engine.ExamLanguage.Korean }.SelectMany(lang => Steps.SelectMany(s => s.Key == EyeContact
            ? new[] { LineFor(s.Key, true, lang), LineFor(s.Key, false, lang) }
            : new[] { LineFor(s.Key, false, lang) }));

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
    /// The eye-contact check reports episodes (a long look away, how far the reminders went...) as it would in an exam. In a practice they are dropped here:
    /// never stored, never sent anywhere. Returns true so the browser keeps the check running.
    /// </summary>
    public static bool DropGazeEpisodes(IReadOnlyCollection<Sessions.HubIntegrityEvent>? episodes) => true;

    /// <summary>
    /// The examiner's spoken eye-contact remark for <paramref name="kind"/> (the same fixed words as in an exam), offered to the
    /// avatar hub for this practice only. Null when there is nothing to say or voice isn't armed.
    /// </summary>
    public string? Remark(VoiceGrants grants, Guid user, string? kind)
    {
        // In the student's language (GAD-T532): Korean lines when the practice runs in Korean, English otherwise.
        if (kind is null || Voice.ExaminerFiller.Remark(kind, Language) is not { } text) return null;
        // A reminder (GAD-T561) is a warm line, never the same twice in one practice; once all are said she starts over.
        if (kind is "look-away" or "reading")
        {
            if (WebcamIntegrityRecorder.NextReminder(remindersSaid, Language) is not { } line)
            {
                remindersSaid.Clear();
                line = WebcamIntegrityRecorder.NextReminder(remindersSaid, Language)!;
            }
            text = line;
        }
        return grants.Offer(user, Id, text) ? text : null;
    }

    private readonly HashSet<string> remindersSaid = new(StringComparer.Ordinal);

    /// <summary>The language the examiner's spoken remarks use in this practice (GAD-T532): the student's UI language; English by default.</summary>
    public string Language { get; set; } = Gadfly.Engine.ExamLanguage.English;

    /// <summary>
    /// Whether a spoken answer sounds like "I don't know": the exam's own aporia word, or the usual ways of saying it. Only used to
    /// pick the examiner's next words in a practice; an exam judges answers through the panel, never with this.
    /// </summary>
    public static bool SoundsLikeIDontKnow(string? heard)
    {
        if (string.IsNullOrWhiteSpace(heard)) return false;
        if (ResponseVerifier.IsAporiaWord(heard)) return true;
        var t = heard.ToLowerInvariant().Replace('’', '\'');
        string[] phrases = ["don't know", "dont know", "do not know", "no idea", "not sure", "no clue", "can't remember", "cannot remember", "don't remember",
            // Korean (GAD-T544): 모르겠어요, 몰라요, 모릅니다, 잘 모르, 기억이 안 나요
            "모르겠", "몰라", "모릅니다", "잘 모르", "기억이 안", "기억 안"];
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
