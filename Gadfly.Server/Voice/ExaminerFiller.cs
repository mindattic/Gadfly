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

    /// <summary>
    /// The acknowledgement she gives the moment an answer is in (GAD-T496), the backchannel a listener gives when someone stops
    /// talking, so the turn never ends in dead air. Receipt only, never assessment: no "good", "right" (nor "alright", which can sound like "that's right": GAD-T519), "exactly" or "correct",
    /// which would leak how the answer was judged; the same few words follow a strong answer and a weak one alike. Offered to the
    /// browser ahead of time (with each question), so it plays without a round trip; prepared ahead in the phrase cache with the
    /// other fixed lines, per examiner voice.
    /// </summary>
    public static readonly IReadOnlyList<string> Acknowledgements =
    [
        "Mm, okay.",
        "Okay.",
        "Mm-hm.",
        "I see.",
        "Okay, thank you.",
        "Mm. Okay.",
        "Thank you.",
    ];

    /// <summary>
    /// The soft listening sounds she may make while the student is still holding the floor (GAD-T496, only when
    /// <c>Gadfly:Voice:BackchannelMs</c> is set): a finished sentence followed by a short silence gets one of these, which does not
    /// end the turn. Non-words on purpose: no "yes", "right" or "okay" that could read as agreement or a verdict.
    /// </summary>
    public static readonly IReadOnlyList<string> Backchannels = ["Mm.", "Mm-hm."];

    /// <summary>An acknowledgement, never the same as <paramref name="last"/>, so two answers in a row never get the same one.</summary>
    public static string PickAcknowledgement(Random random, string? last) => PickAcknowledgement(random, last, Gadfly.Engine.ExamLanguage.English);

    /// <summary>A first and a long-wait line, the first never the same as <paramref name="lastFirst"/> so turns in a row vary.</summary>
    public static (string First, string Again) Pick(Random random, string? lastFirst) => Pick(random, lastFirst, Gadfly.Engine.ExamLanguage.English);

    /// <summary>
    /// The fixed lines in the exam language (GAD-T532). English is exactly the lists above; another language has its own set, written
    /// to the same rules: receipt and "a moment, please" only, never a word that judges the answer. A language without a set of its
    /// own falls back to English (the examiner then says these few fixed lines in English, never anything assessing).
    /// </summary>
    public sealed record Lines(
        IReadOnlyList<string> First, IReadOnlyList<string> Again, IReadOnlyList<string> Acknowledgements, IReadOnlyList<string> Backchannels,
        IReadOnlyDictionary<string, string> Remarks);

    /// <summary>
    /// The Korean set (GAD-T532), reviewed as non-evaluative: "네." and "음." are receipt (the listener's ordinary 맞장구 tokens), "그렇군요."
    /// is "I see", "알겠습니다." is "understood", "감사합니다." is "thank you". Never 맞아요/맞습니다 ("that's right"), 좋아요/좋습니다
    /// ("good"), 잘했어요 ("well done"), 정확해요 ("exactly"), 그렇죠 ("right?") or 훌륭해요 ("excellent"). The backchannels are non-words.
    /// Polite 해요체/합니다체 throughout, as an examiner speaks to a student.
    /// </summary>
    public static readonly Lines Korean = new(
        First: ["잠시 생각해 볼게요.", "잠시만 기다려 주세요.", "음, 잠시만요.", "잠깐만요.", "생각 좀 해 볼게요.", "잠시 정리해 볼게요."],
        Again: ["조금만 더 기다려 주세요.", "거의 다 됐어요.", "아직 생각 중이에요. 잠시만요."],
        Acknowledgements: ["네.", "음.", "그렇군요.", "알겠습니다.", "네, 알겠습니다.", "음, 그렇군요.", "감사합니다."],
        Backchannels: ["음.", "으음."],
        Remarks: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["look-away"] = "답변하는 동안 저를 봐 주세요.",
            ["reading"] = "읽지 마시고, 답변하는 동안 저를 봐 주세요.",
            ["face-absent"] = "얼굴이 보이지 않아요. 답변하는 동안 화면 안으로 돌아와 주세요.",
            ["second-face"] = "카메라에 다른 사람이 보여요. 혼자서 답변하고 있는지 확인해 주세요.",
        });

    /// <summary>The English set: the lists above and the exam room's eye-contact remarks.</summary>
    public static readonly Lines English = new(First, Again, Acknowledgements, Backchannels, Integrity.WebcamIntegrityRecorder.ExaminerRemarks);

    /// <summary>The fixed lines for <paramref name="language"/> (a BCP-47 tag; "ko-KR" is Korean); English for English, empty or unknown.</summary>
    public static Lines For(string? language) => Gadfly.Engine.ExamLanguage.Normalize(language) switch
    {
        Gadfly.Engine.ExamLanguage.Korean => Korean,
        _ => English,
    };

    /// <summary>An acknowledgement in <paramref name="language"/>, never the same as <paramref name="last"/>.</summary>
    public static string PickAcknowledgement(Random random, string? last, string? language)
    {
        var set = For(language).Acknowledgements;
        var choices = set.Where(t => t != last).ToArray();
        return choices[random.Next(choices.Length)];
    }

    /// <summary>A first and a long-wait line in <paramref name="language"/>, the first never the same as <paramref name="lastFirst"/>.</summary>
    public static (string First, string Again) Pick(Random random, string? lastFirst, string? language)
    {
        var set = For(language);
        var choices = set.First.Where(t => t != lastFirst).ToArray();
        return (choices[random.Next(choices.Length)], set.Again[random.Next(set.Again.Count)]);
    }

    /// <summary>
    /// The examiner's spoken eye-contact remark of <paramref name="kind"/> in <paramref name="language"/> (GAD-T532), or null for an unknown
    /// kind. English is exactly <see cref="Integrity.WebcamIntegrityRecorder.ExaminerRemarks"/>.
    /// </summary>
    public static string? Remark(string? kind, string? language) =>
        kind is not null && For(language).Remarks.TryGetValue(kind, out var text) ? text : null;

    /// <summary>
    /// Every fixed line for <paramref name="language"/>, in the order they are needed (acknowledgements, backchannels when on, wait
    /// fillers, remarks), for the phrase cache (GAD-T303). The cache is keyed by voice, model and text, so each language's lines are
    /// prepared and kept separately for each examiner voice.
    /// </summary>
    public static IEnumerable<string> FixedLines(string? language, bool backchannels)
    {
        var set = For(language);
        return set.Acknowledgements.Concat(backchannels ? set.Backchannels : []).Concat(set.First).Concat(set.Again).Concat(set.Remarks.Values);
    }
}
