namespace Gadfly.Server.Voice;

/// <summary>How the voice exam room runs. Nothing but <see cref="Live"/> can reach a paid provider.</summary>
public enum VoiceMode
{
    /// <summary>
    /// Voice is not offered (the default outside Development unless Gadfly:Voice:Mode is set). Exams are spoken, so with
    /// voice off the get-ready page's spoken-examiner check fails and no exam can begin; a deployment that runs exams sets
    /// the mode (docs/DEPLOY.md).
    /// </summary>
    Off,
    /// <summary>
    /// Simulated speech recognition (placeholder words timed to the microphone) and an in-process fake text-to-speech with
    /// real word timings. No key, no network, no cost; for development, demos and verification. Transcripts are placeholders.
    /// </summary>
    Mock,
    /// <summary>ElevenLabs Scribe / OpenAI for recognition and ElevenLabs for the examiner's voice. Billed. Keys stay on the server (Vault).</summary>
    Live,
}

/// <summary>
/// Voice exam room settings (GAD-T53), from <c>Gadfly:Voice</c>:
/// <c>Mode</c> (<c>off</c> | <c>mock</c> | <c>live</c>; default <c>mock</c> in Development, otherwise <c>off</c>),
/// <c>AvatarModelUrl</c> (the GLB; default <c>/assets/avatars/mpfb.glb</c> when that file is present, else her live presence),
/// <c>EndOfAnswerSeconds</c> (the shortest silence that ends an answer, default 4),
/// <c>EndOfAnswerMaxSeconds</c> (the longest it may grow to as it adapts to the student's own pauses, default 12; GAD-T264),
/// <c>EndOfAnswerCompleteSeconds</c> (the shorter silence after an answer that ends in a finished sentence, default 2; GAD-T498),
/// <c>SpeechHangoverMs</c> (the silence after which the browser closes one stretch of speech and commits it, default 700; GAD-T498).
/// Provider choice and keys are the libraries' own settings: <c>MindAttic:SpeechRecognition:Provider</c> and
/// <c>MindAttic:Avatar:ElevenLabs:VoiceId</c>; the ElevenLabs key is read from Vault on the server only.
/// </summary>
public sealed record VoiceSettings(VoiceMode Mode, string? AvatarModelUrl, double EndOfAnswerSeconds)
{
    public bool Available => Mode != VoiceMode.Off;

    /// <summary>
    /// The ceiling the end-of-answer silence may adapt up to (GAD-T264). The browser lengthens the cue toward this when the
    /// student's own mid-answer pauses are long, so a slow or disfluent speaker is not cut off; it never goes below
    /// <see cref="EndOfAnswerSeconds"/>.
    /// </summary>
    public double EndOfAnswerMaxSeconds { get; init; } = 12;

    /// <summary>
    /// The end-of-answer silence when what was heard last ends in a finished sentence (GAD-T498): the recognizer's final ends in
    /// a full stop, question mark or exclamation mark and its last word is not one that leads into more ("and", "because", "the",
    /// "um"...). A finished answer then goes in after this much silence instead of <see cref="EndOfAnswerSeconds"/>; a student
    /// who stops mid-sentence still gets the full floor. It adapts upward with the student's own pauses like the floor does, and
    /// lengthens after each answer that turned out to be cut off (GAD-T462), never past the floor. <c>Gadfly:Voice:EndOfAnswerCompleteSeconds</c>,
    /// 1 to <see cref="EndOfAnswerSeconds"/>, default 2.
    /// </summary>
    public double EndOfAnswerCompleteSeconds { get; init; } = 2;

    /// <summary>
    /// How long the browser's voice activity detection waits in silence before it closes a stretch of speech and commits it to the
    /// recognizer (GAD-T498; the speech library's <c>hangoverMs</c>, whose own default is 800). Shorter commits sooner, so the
    /// final transcript, and with it the end-of-answer countdown, starts sooner; a commit is not the end of the answer (speaking
    /// again simply starts the next stretch). <c>Gadfly:Voice:SpeechHangoverMs</c>, 300 to 2000, default 700.
    /// </summary>
    public int SpeechHangoverMs { get; init; } = 700;

    /// <summary>
    /// The listening backchannel (GAD-T496): after a finished sentence and this many milliseconds of silence, while the
    /// end-of-answer countdown runs, she makes a soft "Mm." or "Mm-hm." that does not end the turn (speaking again stops her at once).
    /// 0 turns it off, the default: the microphone is open while she makes it, so it relies on the browser's echo cancellation to
    /// keep her sound out of the answer. <c>Gadfly:Voice:BackchannelMs</c>, 0 or 300 to 3000.
    /// </summary>
    public int BackchannelMs { get; init; }

    /// <summary>
    /// Prepare the examiner's fixed lines ahead in the avatar's phrase cache (GAD-T303, <see cref="ExaminerSpeechWarmup"/>), so the
    /// first one a student hears starts at once. <c>Gadfly:Voice:WarmPhrases</c>, default true.
    /// </summary>
    public bool WarmPhrases { get; init; } = true;

    /// <summary>
    /// The ElevenLabs model the examiner speaks with when <c>MindAttic:Avatar:ElevenLabs:ModelId</c> isn't set (GAD-T303):
    /// <c>Gadfly:Voice:TtsModel</c>, default <c>eleven_flash_v2_5</c>, the provider's low-latency model, so a question starts within
    /// about a second of being sent.
    /// </summary>
    public string TtsModel { get; init; } = "eleven_flash_v2_5";

    /// <summary>
    /// The ElevenLabs model per exam language (GAD-T530), <c>Gadfly:Voice:TtsModels:&lt;language&gt;</c> (e.g. <c>TtsModels:ko</c>). A
    /// language without one uses the examiner's model (<see cref="TtsModel"/>, or <c>MindAttic:Avatar:ElevenLabs:ModelId</c>), which by
    /// default is <c>eleven_flash_v2_5</c>: it speaks Korean and Spanish (as do <c>eleven_turbo_v2_5</c>, <c>eleven_multilingual_v2</c>
    /// and <c>eleven_v3</c>; <c>eleven_flash_v2</c>/<c>eleven_turbo_v2</c> are English only, so a Korean or Spanish exam (GAD-T607) must
    /// not be left on those).
    /// </summary>
    public IReadOnlyDictionary<string, string> TtsModels { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The examiner's ElevenLabs voice id per exam language (GAD-T530), <c>Gadfly:Voice:VoiceIds:&lt;language&gt;</c>, for a platform voice
    /// that does not speak that language well. Used only where the exam and the organization name no voice of their own (GAD-T475): their
    /// choice stands in every language. Unset: the same voice speaks every language (the multilingual models do).
    /// </summary>
    public IReadOnlyDictionary<string, string> VoiceIds { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// ESL mode's end-of-answer floor (GAD-T554, docs/research/esl-and-korean.md R3): a student answering in a second language pauses
    /// longer, and mid-clause, so the shortest silence that ends an answer starts at 6 s instead of <see cref="EndOfAnswerSeconds"/>.
    /// <c>Gadfly:Voice:Esl:EndOfAnswerSeconds</c>, 2 to 20, default 6. Pilot-tunable, like the rest of the <c>Esl</c> settings.
    /// </summary>
    public double EslEndOfAnswerSeconds { get; init; } = 6;

    /// <summary>ESL mode's ceiling for the adaptive silence (<c>Gadfly:Voice:Esl:EndOfAnswerMaxSeconds</c>, up to 30, default 15).</summary>
    public double EslEndOfAnswerMaxSeconds { get; init; } = 15;

    /// <summary>
    /// ESL mode's silence after a finished sentence (<c>Gadfly:Voice:Esl:EndOfAnswerCompleteSeconds</c>, 1 to the ESL floor, default 3.5):
    /// second-language speakers pause after a complete clause while planning the next, so the short cue is longer, and it is used only
    /// after <see cref="EslCompleteAfterAnswers"/> answers in a row that were not cut off.
    /// </summary>
    public double EslEndOfAnswerCompleteSeconds { get; init; } = 3.5;

    /// <summary>How many answers in a row with no cut-off before ESL mode uses the finished-sentence cue at all
    /// (<c>Gadfly:Voice:Esl:CompleteAfterAnswers</c>, 0 to 10, default 3).</summary>
    public int EslCompleteAfterAnswers { get; init; } = 3;

    /// <summary>ESL mode multiplies the cut-off backoff step (GAD-T462) by this (<c>Gadfly:Voice:Esl:CutOffBackoffFactor</c>, 1 to 4, default 2).</summary>
    public double EslCutOffBackoffFactor { get; init; } = 2;

    /// <summary>
    /// The examiner's speaking speed for a student answering in a second language (GAD-T555): ElevenLabs <c>voice_settings.speed</c>,
    /// 0.7 to 1.2 (1 is the voice's normal pace). <c>Gadfly:Voice:PlainSpeechSpeed</c>, default 0.9: slightly slower, still natural.
    /// 1 turns it off.
    /// </summary>
    public double PlainSpeechSpeed { get; init; } = 0.9;

    /// <summary>
    /// Per-examiner overrides (GAD-T504), <c>Gadfly:Voice:Examiners:&lt;id&gt;</c> with <c>ModelUrl</c>, <c>VoiceId</c>, <c>VoiceIds:&lt;language&gt;</c> and <c>Speed</c>, keyed by the examiner catalog's ids
    /// (<see cref="Gadfly.Server.Student.ExaminerCatalog"/>). Unset parts keep the catalog's defaults.
    /// </summary>
    public IReadOnlyDictionary<string, ExaminerSettings> Examiners { get; init; } = new Dictionary<string, ExaminerSettings>();

    /// <summary>The model configured for <paramref name="language"/>, or null for the examiner's usual model.</summary>
    public string? TtsModelFor(string? language) =>
        TtsModels.TryGetValue(Gadfly.Engine.ExamLanguage.Normalize(language), out var m) && !string.IsNullOrWhiteSpace(m) ? m.Trim() : null;

    /// <summary>The platform voice configured for <paramref name="language"/>, or null for the platform's usual voice.</summary>
    public string? VoiceIdFor(string? language) =>
        VoiceIds.TryGetValue(Gadfly.Engine.ExamLanguage.Normalize(language), out var v) && ExaminerVoice.IsWellFormed(v) ? v.Trim() : null;

    /// <summary>The hard upper bound on the cue after an accommodation is applied: past this the student should press Done answering.</summary>
    public const double EndOfAnswerHardCapSeconds = 45;

    /// <summary>
    /// The end-of-answer cue's floor and ceiling for one student (GAD-T264), in milliseconds: both are lengthened by the
    /// extra-time accommodation (<paramref name="timeMultiplier"/>, 1 to 4), so a student with extra time also gets more
    /// silence before an answer is taken as finished.
    /// </summary>
    public (int FloorMs, int CeilingMs) EndCue(double timeMultiplier)
    {
        var m = double.IsFinite(timeMultiplier) ? Math.Clamp(timeMultiplier, 1, 4) : 1;
        var floor = Math.Min(EndOfAnswerSeconds * m, EndOfAnswerHardCapSeconds);
        var ceiling = Math.Clamp(EndOfAnswerMaxSeconds * m, floor, EndOfAnswerHardCapSeconds);
        return ((int)Math.Round(floor * 1000), (int)Math.Round(ceiling * 1000));
    }

    /// <summary>
    /// The end-of-answer silence after a finished sentence for one student (GAD-T498), in milliseconds: lengthened by the
    /// extra-time accommodation like the floor, and never longer than the floor (<see cref="EndCue"/>).
    /// </summary>
    public int EndCueCompleteMs(double timeMultiplier)
    {
        var m = double.IsFinite(timeMultiplier) ? Math.Clamp(timeMultiplier, 1, 4) : 1;
        var (floorMs, _) = EndCue(timeMultiplier);
        return Math.Min(floorMs, (int)Math.Round(Math.Min(EndOfAnswerCompleteSeconds * m, EndOfAnswerHardCapSeconds) * 1000));
    }

    /// <summary>The floor is at least the student's longest calibration-reading pause times this (GAD-T594), as the browser's own
    /// adaptation takes half again the student's longer pauses (GAD-T264).</summary>
    public const double CalibratedPauseFactor = 1.5;

    /// <summary>The end-of-answer silences for one student in one attempt (GAD-T264, GAD-T498, GAD-T554), in milliseconds.</summary>
    /// <param name="FloorMs">The shortest silence that ends an answer.</param>
    /// <param name="CeilingMs">The longest the silence adapts up to.</param>
    /// <param name="CompleteMs">The silence after a finished sentence (equal to the floor: no short cue).</param>
    /// <param name="CompleteAfterAnswers">Answers in a row with no cut-off before the short cue is used (0: at once, as always).</param>
    /// <param name="BackoffFactor">Multiplies the browser's cut-off backoff step (1: unchanged).</param>
    public sealed record EndCues(int FloorMs, int CeilingMs, int CompleteMs, int CompleteAfterAnswers, double BackoffFactor);

    /// <summary>
    /// The end-of-answer silences for <paramref name="speech"/> and the extra-time accommodation (<paramref name="timeMultiplier"/>).
    /// A default attempt (<see cref="SessionSpeech.IsDefault"/>, or no languages known) gets exactly <see cref="EndCue"/> and
    /// <see cref="EndCueCompleteMs"/>, as before. ESL mode (a second-language speaker, GAD-T554) starts from the <c>Esl</c> settings,
    /// with the accommodation stacked on top and the hard cap still applying. A non-English exam never uses the short finished-sentence
    /// cue: that test reads English punctuation habits and an English list of continuation words (research note R6.2).
    /// </summary>
    public EndCues EndCuesFor(double timeMultiplier, SessionSpeech? speech)
    {
        var (floorMs, ceilingMs) = EndCue(timeMultiplier);
        var cues = new EndCues(floorMs, ceilingMs, EndCueCompleteMs(timeMultiplier), 0, 1);
        if (speech is null || speech.IsDefault) return cues;
        if (speech.NonNative)
        {
            var esl = this with
            {
                EndOfAnswerSeconds = EslEndOfAnswerSeconds,
                EndOfAnswerMaxSeconds = Math.Max(EslEndOfAnswerMaxSeconds, EslEndOfAnswerSeconds),
                EndOfAnswerCompleteSeconds = Math.Min(EslEndOfAnswerCompleteSeconds, EslEndOfAnswerSeconds),
            };
            var (floor, ceiling) = esl.EndCue(timeMultiplier);
            cues = new EndCues(floor, ceiling, esl.EndCueCompleteMs(timeMultiplier), EslCompleteAfterAnswers, EslCutOffBackoffFactor);
        }
        // The student's own calibration reading in the exam language (GAD-T594): a floor shorter than half again their longest pause
        // while reading is lengthened to it (never past the ceiling); it never shortens anything.
        if (speech.Calibration is { Readings: > 0, LongestPauseMs: > 0 and < 60_000 } calibration)
        {
            var m = double.IsFinite(timeMultiplier) ? Math.Clamp(timeMultiplier, 1, 4) : 1;
            var calibrated = (int)Math.Round(Math.Min(calibration.LongestPauseMs * CalibratedPauseFactor * m, EndOfAnswerHardCapSeconds * 1000));
            if (calibrated > cues.FloorMs) cues = cues with { FloorMs = Math.Min(cues.CeilingMs, calibrated) };
        }
        if (!Gadfly.Engine.ExamLanguage.IsEnglish(speech.Language)) cues = cues with { CompleteMs = cues.FloorMs };
        return cues;
    }

    public static readonly VoiceSettings Disabled = new(VoiceMode.Off, null, 4);

    /// <summary>Reads the settings. <paramref name="webRoot"/> is checked for the default avatar model.</summary>
    public static VoiceSettings From(IConfiguration config, bool development, string? webRoot)
    {
        var section = config.GetSection("Gadfly:Voice");
        var mode = (section["Mode"] ?? (development ? "mock" : "off")).Trim().ToLowerInvariant() switch
        {
            "mock" => VoiceMode.Mock,
            "live" => VoiceMode.Live,
            _ => VoiceMode.Off,
        };
        var model = section["AvatarModelUrl"];
        if (string.IsNullOrWhiteSpace(model) && webRoot is not null
            && File.Exists(Path.Combine(webRoot, "assets", "avatars", "mpfb.glb")))
            model = "/assets/avatars/mpfb.glb";
        var cue = double.TryParse(section["EndOfAnswerSeconds"], System.Globalization.CultureInfo.InvariantCulture, out var c) && double.IsFinite(c) ? Math.Clamp(c, 2, 15) : 4;
        var max = double.TryParse(section["EndOfAnswerMaxSeconds"], System.Globalization.CultureInfo.InvariantCulture, out var x) && double.IsFinite(x) ? Math.Clamp(x, cue, 30) : Math.Max(12, cue);
        var complete = double.TryParse(section["EndOfAnswerCompleteSeconds"], System.Globalization.CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? Math.Clamp(d, 1, cue) : Math.Min(2, cue);
        var hangover = int.TryParse(section["SpeechHangoverMs"], System.Globalization.CultureInfo.InvariantCulture, out var h) ? Math.Clamp(h, 300, 2000) : 700;
        var backchannel = int.TryParse(section["BackchannelMs"], System.Globalization.CultureInfo.InvariantCulture, out var b) && b > 0 ? Math.Clamp(b, 300, 3000) : 0;
        return new VoiceSettings(mode, string.IsNullOrWhiteSpace(model) ? null : model, cue)
        {
            EndOfAnswerMaxSeconds = max,
            EndOfAnswerCompleteSeconds = complete,
            SpeechHangoverMs = hangover,
            BackchannelMs = backchannel,
            WarmPhrases = !bool.TryParse(section["WarmPhrases"], out var warm) || warm,
            TtsModel = section["TtsModel"] is { Length: > 0 } tts ? tts.Trim() : "eleven_flash_v2_5",
            TtsModels = PerLanguage(section.GetSection("TtsModels")),
            VoiceIds = PerLanguage(section.GetSection("VoiceIds")),
            EslEndOfAnswerSeconds = Number(section["Esl:EndOfAnswerSeconds"], 2, 20, 6),
            EslEndOfAnswerMaxSeconds = Number(section["Esl:EndOfAnswerMaxSeconds"], 2, 30, 15),
            EslEndOfAnswerCompleteSeconds = Number(section["Esl:EndOfAnswerCompleteSeconds"], 1, 20, 3.5),
            EslCompleteAfterAnswers = (int)Number(section["Esl:CompleteAfterAnswers"], 0, 10, 3),
            EslCutOffBackoffFactor = Number(section["Esl:CutOffBackoffFactor"], 1, 4, 2),
            PlainSpeechSpeed = double.TryParse(section["PlainSpeechSpeed"], System.Globalization.CultureInfo.InvariantCulture, out var sp) && double.IsFinite(sp) ? Math.Clamp(sp, 0.7, 1.2) : 0.9,
            Examiners = section.GetSection("Examiners").GetChildren().ToDictionary(e => e.Key.Trim(), e => new ExaminerSettings(
                Blank(e["ModelUrl"]), Blank(e["VoiceId"]), PerLanguage(e.GetSection("VoiceIds")),
                double.TryParse(e["Speed"], System.Globalization.CultureInfo.InvariantCulture, out var es) && double.IsFinite(es) ? Math.Clamp(es, 0.7, 1.2) : null),
                StringComparer.Ordinal),
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double Number(string? value, double min, double max, double fallback) =>
        double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>A per-language section ("ko": value) as normalized language tags to trimmed values; blank values are left out.</summary>
    private static Dictionary<string, string> PerLanguage(IConfigurationSection section)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in section.GetChildren())
            if (!string.IsNullOrWhiteSpace(child.Value)) d[Gadfly.Engine.ExamLanguage.Normalize(child.Key)] = child.Value.Trim();
        return d;
    }
}

/// <summary>
/// One examiner's configured overrides (GAD-T504), from <c>Gadfly:Voice:Examiners:&lt;id&gt;</c>: where the model is
/// hosted (e.g. blob storage) and the examiner's own ElevenLabs voice, for every language
/// (<c>VoiceId</c>) or one (<c>VoiceIds:&lt;language&gt;</c>), and the examiner's own speaking speed (<c>Speed</c>, ElevenLabs
/// <c>voice_settings.speed</c>, 0.7 to 1.2; GAD-T726). Voice ids are settings, not secrets.
/// </summary>
public sealed record ExaminerSettings(string? ModelUrl, string? VoiceId,
    IReadOnlyDictionary<string, string> VoiceIds, double? Speed = null);
