using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Diagnostics;
using Gadfly.Server.Voice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Avatar.ElevenLabs;
using NUnit.Framework;
using VoiceSettings = Gadfly.Server.Voice.VoiceSettings;

namespace Gadfly.Tests.Server;

/// <summary>
/// Speech in the exam language and for second-language speakers (GAD-T528 to GAD-T532, GAD-T554, GAD-T555, GAD-T597): the recognizer's
/// language, the examiner's Korean lines and voice, Korean lip-sync, ESL endpointing and pace, and the keyterm cap. No provider: mock voice only.
/// </summary>
[TestFixture]
public class KoreanSpeechTests
{
    private static VoiceSettings Settings(params (string Key, string Value)[] values) =>
        VoiceSettings.From(new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => "Gadfly:Voice:" + v.Key, v => (string?)v.Value)).Build(), true, null);

    private static readonly SessionSpeech EnglishNative = new(ExamLanguage.English, ExamLanguage.English);
    private static readonly SessionSpeech EnglishByKorean = new(ExamLanguage.English, ExamLanguage.Korean);
    private static readonly SessionSpeech KoreanNative = new(ExamLanguage.Korean, ExamLanguage.Korean);

    // ------------------------------------------------------------------------------------------------- fillers (GAD-T532)

    [Test]
    public void Korean_fixed_lines_never_judge_the_answer()
    {
        // "correct/right" (맞다, 옳다), "good" (좋다), "well done" (잘했다), "exactly" (정확), "excellent" (훌륭), "great/wonderful" (대단,
        // 멋지), "perfect" (완벽), "interesting" (흥미), "right?" (그렇죠), "wrong" (틀리): none may appear in a line she says to every answer.
        var judging = new[] { "맞", "옳", "좋", "잘했", "잘 했", "정확", "훌륭", "대단", "멋지", "멋있", "완벽", "흥미", "그렇죠", "틀리", "틀렸" };
        var k = ExaminerFiller.Korean;
        foreach (var line in k.Acknowledgements.Concat(k.Backchannels).Concat(k.First).Concat(k.Again))
            Assert.That(judging.Where(w => line.Contains(w, StringComparison.Ordinal)), Is.Empty, line);
        Assert.That(k.Acknowledgements, Does.Contain("네.").And.Contain("음.").And.Contain("그렇군요.").And.Contain("알겠습니다."));
        Assert.That(k.Remarks.Keys, Is.EquivalentTo(ExaminerFiller.English.Remarks.Keys), "every eye-contact remark exists in Korean");
        Assert.That(k.First.Concat(k.Again).Concat(k.Acknowledgements).All(l => l.Any(MockTextToSpeech.IsHangulSyllable)), Is.True);
    }

    [Test]
    public void English_lines_are_exactly_the_lines_from_before_and_unknown_languages_fall_back_to_them()
    {
        Assert.That(ExaminerFiller.For(null), Is.SameAs(ExaminerFiller.English));
        Assert.That(ExaminerFiller.For("en-US"), Is.SameAs(ExaminerFiller.English));
        Assert.That(ExaminerFiller.For("fr"), Is.SameAs(ExaminerFiller.English));
        Assert.That(ExaminerFiller.For("ko-KR"), Is.SameAs(ExaminerFiller.Korean));
        var before = ExaminerFiller.Acknowledgements.Concat(ExaminerFiller.Backchannels).Concat(ExaminerFiller.First).Concat(ExaminerFiller.Again)
            .Concat(Gadfly.Server.Integrity.WebcamIntegrityRecorder.ExaminerRemarks.Values)
            .Concat(Gadfly.Server.Integrity.WebcamIntegrityRecorder.GentleReminders).Append(Gadfly.Server.Sessions.ClosingReflection.English).Distinct();
        Assert.That(ExaminerFiller.FixedLines("en", backchannels: true), Is.EqualTo(before));
        Assert.That(ExaminerFiller.Remark("look-away", "en"), Is.EqualTo(Gadfly.Server.Integrity.WebcamIntegrityRecorder.ExaminerRemarks["look-away"]));
        Assert.That(ExaminerFiller.Remark("look-away", "ko"), Is.EqualTo(Gadfly.Server.Integrity.WebcamIntegrityRecorder.GentleRemindersKorean[0]));
        Assert.That(ExaminerFiller.Remark("no-such-kind", "ko"), Is.Null);
    }

    [Test]
    public void Korean_eye_contact_remarks_never_ask_the_student_to_look_at_her_and_gentle_reminders_are_warmed()
    {
        // GAD-T605: the gentle-reminder rule (docs/research/attention-and-gaze.md 4.2) holds in Korean too.
        foreach (var kind in new[] { "look-away", "reading" })
            Assert.That(ExaminerFiller.Remark(kind, "ko"), Does.Not.Contain("봐 주세요").And.Not.Contain("보세요").And.Not.Contain("저를 봐"), kind);
        Assert.That(ExaminerFiller.Remark("reading", "ko"), Is.EqualTo(Gadfly.Server.Integrity.WebcamIntegrityRecorder.GentleRemindersKorean[1]));
        var ko = ExaminerFiller.FixedLines("ko", false).ToArray();
        var en = ExaminerFiller.FixedLines("en", false).ToArray();
        Assert.That(ko, Is.SupersetOf(Gadfly.Server.Integrity.WebcamIntegrityRecorder.GentleRemindersKorean));
        Assert.That(en, Is.SupersetOf(Gadfly.Server.Integrity.WebcamIntegrityRecorder.GentleReminders));
        Assert.That(ko, Is.Unique);
        Assert.That(en, Is.Unique);
        // The closing reflection is prepared with the other fixed lines, in the exam language (GAD-T818).
        Assert.That(ko, Does.Contain(Gadfly.Server.Sessions.ClosingReflection.Korean));
        Assert.That(ExaminerFiller.FixedLines("es-MX", false), Does.Contain(Gadfly.Server.Sessions.ClosingReflection.Spanish));
    }

    [Test]
    public void Korean_acknowledgements_and_fillers_are_picked_from_the_Korean_set_and_vary()
    {
        var random = new Random(3);
        string? lastAck = null, lastFirst = null;
        for (var i = 0; i < 100; i++)
        {
            var ack = ExaminerFiller.PickAcknowledgement(random, lastAck, "ko");
            Assert.That(ExaminerFiller.Korean.Acknowledgements, Does.Contain(ack));
            Assert.That(ack, Is.Not.EqualTo(lastAck));
            lastAck = ack;
            var (first, again) = ExaminerFiller.Pick(random, lastFirst, "ko");
            Assert.That(ExaminerFiller.Korean.First, Does.Contain(first));
            Assert.That(ExaminerFiller.Korean.Again, Does.Contain(again));
            Assert.That(first, Is.Not.EqualTo(lastFirst));
            lastFirst = first;
        }
    }

    [Test]
    public void Fixed_lines_are_warmed_separately_per_voice_and_language_profile()
    {
        var warmup = new ExaminerSpeechWarmup(null!, Settings(), TimeProvider.System, NullLogger<ExaminerSpeechWarmup>.Instance);
        var now = DateTimeOffset.UnixEpoch;
        var lines = ExaminerFiller.FixedLines("ko", false).ToArray();
        Assert.That(warmup.Due(lines, now), Is.Not.Empty);
        Assert.That(warmup.Due(lines, now), Is.Empty, "the same voice and model: prepared already");
        Assert.That(warmup.Due(lines, now, profile: new ExaminerTts.Profile("eleven_multilingual_v2", null)), Is.Not.Empty, "another model is another cache entry");
        Assert.That(warmup.Due(lines, now, profile: new ExaminerTts.Profile("eleven_flash_v2_5", 0.9)), Is.Not.Empty, "another speed too");
    }

    // ---------------------------------------------------------------------------------------- recognizer language (GAD-T529, GAD-T554)

    [Test]
    public void The_recognizer_is_told_the_exam_language_and_never_left_to_detect_an_accent()
    {
        Assert.That(SessionSpeech.Default.IsDefault && EnglishNative.IsDefault, Is.True);
        Assert.That(new SessionSpeech("en", null).IsDefault, Is.True, "no profile: treated as a native speaker");
        Assert.That(EnglishByKorean.NonNative && !EnglishByKorean.IsDefault, Is.True);
        Assert.That(KoreanNative.NonNative || KoreanNative.IsDefault, Is.False);

        // Default: the exam policy's language, as before (the browser sends "en" when there is none).
        Assert.That(EnglishNative.RecognizerLanguage(null), Is.Null);
        Assert.That(EnglishNative.RecognizerLanguage("en"), Is.EqualTo("en"));
        // A Korean exam: Scribe language_code / Whisper language "ko", whatever the English-era policy said.
        Assert.That(KoreanNative.RecognizerLanguage(null), Is.EqualTo("ko"));
        Assert.That(new SessionSpeech("ko-KR", "en").RecognizerLanguage("en"), Is.EqualTo("ko"));
        // English by a Korean speaker: pinned to English, never auto-detected.
        Assert.That(EnglishByKorean.RecognizerLanguage(null), Is.EqualTo("en"));
        Assert.That(EnglishByKorean.Primary, Is.EqualTo("ko"));
    }

    [Test]
    public void The_grant_carries_the_recognizer_language_and_the_attempts_languages()
    {
        var grants = new VoiceGrants(TimeProvider.System);
        var (user, org, session) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        grants.Arm(user, org, session);
        grants.SetVocabulary(user, session, KoreanNative.RecognizerLanguage(null), ["광합성", "photosynthesis"]);
        grants.SetSpeech(user, session, KoreanNative);
        Assert.That(grants.AllowedLanguage(user), Is.EqualTo("ko"), "a StartSession asking for another language is refused");
        Assert.That(grants.IsVocabularyAllowed(user, ["광합성"]), Is.True);
        Assert.That(grants.SpeechOf(user, session), Is.EqualTo(KoreanNative));
        grants.Offer(user, session, "네.");
        Assert.That(grants.OffererOf(user, "네.") is { Speech: { } s, Session: var sid } && s == KoreanNative && sid == session, Is.True);
        Assert.That(grants.OffererOf(user, "never offered"), Is.Null);
    }

    // ------------------------------------------------------------------------------------------- keyterm cap (GAD-T597)

    [Test]
    public void Keyterms_are_capped_at_Scribes_realtime_limits_most_important_first()
    {
        var many = Enumerable.Range(0, 80).Select(i => $"term {i:00}").ToList();
        var capped = VoiceVocabulary.Cap(many);
        Assert.That(capped, Has.Count.EqualTo(VoiceVocabulary.MaxTerms).And.Count.EqualTo(50));
        Assert.That(capped, Is.EqualTo(many.Take(50)), "in the order given");

        var longTerms = Enumerable.Range(0, 30).Select(i => $"{i:00}" + new string('x', 47)).ToList();   // 49 characters each
        var byChars = VoiceVocabulary.Cap(longTerms.Append("short"));
        Assert.That(byChars.Sum(t => t.Length), Is.LessThanOrEqualTo(VoiceVocabulary.MaxTotalChars));
        Assert.That(byChars, Has.Count.EqualTo(21), "20 long terms take 980 characters; the 21st would pass 1,000, a short one after it still fits");
        Assert.That(byChars[^1], Is.EqualTo("short"));

        Assert.That(VoiceVocabulary.Cap([" Mitosis ", "mitosis", "", null, new string('y', 61), "광합성"]), Is.EqualTo(new[] { "Mitosis", "광합성" }));
    }

    // ------------------------------------------------------------------------------- monitoring (GAD-T554)

    [Test]
    public void Korean_answers_are_scored_by_character_and_english_ones_by_word_as_before()
    {
        // A spacing difference alone is no error in Korean (character error rate); one wrong syllable is one error of six characters.
        var spacing = Gadfly.Sources.Verification.SpeechAccuracy.UnitErrors("광합성은 빛을 이용해요", "광합성은빛을 이용해요");
        Assert.That((spacing.Errors, spacing.ReferenceWords), Is.EqualTo((0, 10)));
        var one = Gadfly.Sources.Verification.SpeechAccuracy.UnitErrors("광합성은 빛을", "광합성는 빛을");
        Assert.That((one.Errors, one.ReferenceWords), Is.EqualTo((1, 6)));
        const string reference = "The cell divides into two cells.", recognized = "The sell divides in two cells";
        Assert.That(Gadfly.Sources.Verification.SpeechAccuracy.UnitErrors(reference, recognized),
            Is.EqualTo(Gadfly.Sources.Verification.SpeechAccuracy.WordErrors(reference, recognized)));
    }

    // ------------------------------------------------------------------------------------- endpointing (GAD-T554)

    [Test]
    public void An_english_exam_by_an_english_speaker_keeps_exactly_the_old_end_of_answer_silences()
    {
        var settings = Settings();
        foreach (var m in new[] { 1, 1.5, 2 })
        {
            var cues = settings.EndCuesFor(m, EnglishNative);
            var (floor, ceiling) = settings.EndCue(m);
            Assert.That(cues, Is.EqualTo(new VoiceSettings.EndCues(floor, ceiling, settings.EndCueCompleteMs(m), 0, 1)));
            Assert.That(settings.EndCuesFor(m, null), Is.EqualTo(cues));
        }
    }

    [Test]
    public void ESL_mode_waits_longer_uses_the_short_cue_only_after_three_uncut_answers_and_backs_off_twice_as_fast()
    {
        var settings = Settings();
        var cues = settings.EndCuesFor(1, EnglishByKorean);
        Assert.That(cues, Is.EqualTo(new VoiceSettings.EndCues(6000, 15000, 3500, 3, 2)));
        // The extra-time accommodation stacks on top, under the hard cap.
        var extra = settings.EndCuesFor(2, EnglishByKorean);
        Assert.That((extra.FloorMs, extra.CeilingMs, extra.CompleteMs), Is.EqualTo((12000, 30000, 7000)));
        Assert.That(settings.EndCuesFor(4, EnglishByKorean).CeilingMs, Is.EqualTo((int)(VoiceSettings.EndOfAnswerHardCapSeconds * 1000)));
        // Pilot-tunable.
        var tuned = Settings(("Esl:EndOfAnswerSeconds", "5"), ("Esl:EndOfAnswerCompleteSeconds", "3"), ("Esl:CompleteAfterAnswers", "2"), ("Esl:CutOffBackoffFactor", "1.5"));
        Assert.That(tuned.EndCuesFor(1, EnglishByKorean), Is.EqualTo(new VoiceSettings.EndCues(5000, 15000, 3000, 2, 1.5)));
    }

    [Test]
    public void A_korean_exam_never_uses_the_english_finished_sentence_cue()
    {
        var cues = Settings().EndCuesFor(1, KoreanNative);
        Assert.That(cues.CompleteMs, Is.EqualTo(cues.FloorMs));
        Assert.That(cues.FloorMs, Is.EqualTo(4000), "a native speaker keeps the usual floor");
        var esl = Settings().EndCuesFor(1, new SessionSpeech("ko", "en"));
        Assert.That((esl.FloorMs, esl.CompleteMs), Is.EqualTo((6000, 6000)));
    }

    [Test]
    public void The_students_calibration_reading_only_ever_lengthens_the_floor()
    {
        var settings = Settings();
        var slow = EnglishByKorean with { Calibration = new VoiceCalibration { Readings = 3, LongestPauseMs = 5000 } };
        Assert.That(settings.EndCuesFor(1, slow).FloorMs, Is.EqualTo(7500));
        var quick = EnglishByKorean with { Calibration = new VoiceCalibration { Readings = 3, LongestPauseMs = 800 } };
        Assert.That(settings.EndCuesFor(1, quick).FloorMs, Is.EqualTo(6000));
        var huge = EnglishByKorean with { Calibration = new VoiceCalibration { Readings = 3, LongestPauseMs = 20000 } };
        Assert.That(settings.EndCuesFor(1, huge).FloorMs, Is.EqualTo(15000), "never past the ceiling");
    }

    // ------------------------------------------------------------------------------------------ voice (GAD-T530, GAD-T555)

    [Test]
    public void Settings_read_a_model_and_a_voice_per_language_and_the_plain_speech_speed()
    {
        var s = Settings(("TtsModels:ko", " eleven_multilingual_v2 "), ("VoiceIds:KO", "abcdefghij1234567890"), ("VoiceIds:en", "bad id"), ("PlainSpeechSpeed", "0.5"));
        Assert.That(s.TtsModelFor("ko-KR"), Is.EqualTo("eleven_multilingual_v2"));
        Assert.That(s.TtsModelFor("en"), Is.Null);
        Assert.That(s.VoiceIdFor("ko"), Is.EqualTo("abcdefghij1234567890"));
        Assert.That(s.VoiceIdFor("en"), Is.Null, "a malformed voice id is ignored");
        Assert.That(s.PlainSpeechSpeed, Is.EqualTo(0.7), "clamped to ElevenLabs' range");
        Assert.That(Settings().PlainSpeechSpeed, Is.EqualTo(0.9));
        Assert.That(ExaminerVoice.Choose(VoiceMode.Live, null, null, "abcdefghij1234567890"), Is.EqualTo("abcdefghij1234567890"));
        Assert.That(ExaminerVoice.Choose(VoiceMode.Live, "exam0voice00id", null, "abcdefghij1234567890"), Is.EqualTo("exam0voice00id"), "the exam's own voice stands");
        Assert.That(ExaminerVoice.Choose(VoiceMode.Mock, null, null, "abcdefghij1234567890"), Is.Null);
    }

    [Test]
    public void Only_sessions_the_librarys_voice_does_not_fit_get_their_own_voice_profile()
    {
        var plain = Settings();
        Assert.That(ExaminerTts.Choose(EnglishNative, plain, "eleven_flash_v2_5", null), Is.Null, "today's behavior");
        Assert.That(ExaminerTts.Choose(null, plain, "eleven_flash_v2_5", null), Is.Null);
        Assert.That(ExaminerTts.Choose(KoreanNative, plain, "eleven_flash_v2_5", null), Is.Null, "flash v2.5 speaks Korean: the library's voice fits");
        Assert.That(ExaminerTts.Choose(KoreanNative, Settings(("TtsModels:ko", "eleven_multilingual_v2")), "eleven_flash_v2_5", null),
            Is.EqualTo(new ExaminerTts.Profile("eleven_multilingual_v2", null)));
        Assert.That(ExaminerTts.Choose(EnglishByKorean, plain, "eleven_flash_v2_5", null), Is.EqualTo(new ExaminerTts.Profile("eleven_flash_v2_5", 0.9)));
        Assert.That(ExaminerTts.Choose(EnglishByKorean, Settings(("PlainSpeechSpeed", "1")), "eleven_flash_v2_5", null), Is.Null, "speed 1 turns plain speech off");
    }

    /// <summary>Records each synthesis request body and answers with the mock voice.</summary>
    private sealed class Recording : DelegatingHandler
    {
        public List<JsonNode> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!);
            return await base.SendAsync(request, ct);
        }
    }

    [Test]
    public async Task A_profile_speaks_korean_with_its_own_model_and_speed_through_the_mock_voice_and_caches_fixed_lines()
    {
        var recording = new Recording { InnerHandler = new MockTextToSpeech() };
        var services = new ServiceCollection();
        services.AddSingleton(new ElevenLabsOptions { ApiKey = "mock-key-not-real", VoiceId = "mock-voice", OutputFormat = "pcm_22050", BaseUrl = "https://mock.invalid" });
        services.AddSingleton(new SpeechCache(16));
        services.AddHttpClient(nameof(ElevenLabsTtsClient)).ConfigurePrimaryHttpMessageHandler(() => recording);
        await using var sp = services.BuildServiceProvider();
        var tts = new ExaminerTts(sp, Settings(("TtsModels:ko", "eleven_multilingual_v2")));
        var profile = tts.For(new SessionSpeech("ko", "en"))!;
        Assert.That(profile, Is.EqualTo(new ExaminerTts.Profile("eleven_multilingual_v2", 0.9)));

        var words = new List<WordTiming>();
        await foreach (var chunk in tts.StreamAsync(profile, "광합성은 무엇인가요?", null, cacheable: false)) words.AddRange(chunk.Words);
        Assert.That(words.Select(w => w.Word), Is.EqualTo(new[] { "광합성은", "무엇인가요?" }));
        Assert.That(words.All(w => w.DurationMs > 0), Is.True);
        Assert.That(recording.Bodies[0]["model_id"]!.GetValue<string>(), Is.EqualTo("eleven_multilingual_v2"));
        Assert.That(recording.Bodies[0]["voice_settings"]!["speed"]!.GetValue<double>(), Is.EqualTo(0.9));
        Assert.That(recording.Bodies[0]["text"]!.GetValue<string>(), Is.EqualTo("광합성은 무엇인가요?"));

        await foreach (var _ in tts.StreamAsync(profile, "네.", null, cacheable: true)) { }
        await foreach (var _ in tts.StreamAsync(profile, "네.", null, cacheable: true)) { }
        Assert.That(recording.Bodies.Count(b => b["text"]!.GetValue<string>() == "네."), Is.EqualTo(1), "a fixed line is synthesized once");
    }

    [Test]
    public async Task The_mock_voice_times_hangul_by_syllable_and_aligns_every_character()
    {
        var options = new ElevenLabsOptions { ApiKey = "mock-key-not-real", VoiceId = "mock-voice", OutputFormat = "pcm_22050" };
        using var http = new HttpClient(new MockTextToSpeech()) { BaseAddress = new Uri("https://mock.invalid") };
        var client = new ElevenLabsTtsClient(http, options);
        var result = await client.SynthesizeAsync("안녕하세요. 한국어 시험입니다.", useCache: false);
        Assert.That(result.Words.Select(w => w.Word), Is.EqualTo(new[] { "안녕하세요.", "한국어", "시험입니다." }));
        Assert.That(result.Audio, Is.Not.Empty);
        Assert.That(MockTextToSpeech.Syllables("한국어"), Is.EqualTo(3));
        Assert.That(MockTextToSpeech.Syllables("photosynthesis"), Is.EqualTo(5), "English is counted as before");
        Assert.That(MockTextToSpeech.WordSeconds("한국어"), Is.GreaterThan(MockTextToSpeech.WordSeconds("abc")));
        Assert.That(MockTextToSpeech.WordSeconds("abc"), Is.EqualTo(0.14 + 0.045 * 3).Within(1e-9), "English timing unchanged");
    }

    [Test]
    public async Task A_whole_text_said_with_a_profile_plays_as_one_utterance_without_question_parts()
    {
        static async IAsyncEnumerable<SpeechChunk> Tts(string text, string? voice, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new SpeechChunk(new byte[4410], [new WordTiming(text, 0, 90)]);
        }
        var stream = new QuestionVoiceStream("w1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow,
            Tts, new ApiCallRecorder(NullLogger<ApiCallRecorder>.Instance), NullLogger.Instance, wholeText: true);
        stream.Say("그렇군요.", MoveKind.Elicit);
        stream.Finish("그렇군요.");
        var sent = new List<string>();
        await stream.PlayAsync("u1", "pcm_22050", 22050, (name, _, _) => { lock (sent) sent.Add(name); return Task.CompletedTask; }, CancellationToken.None);
        Assert.That(sent.First(), Is.EqualTo("speechStart"));
        Assert.That(sent.Last(), Is.EqualTo("speechEnd"));
        Assert.That(sent, Does.Not.Contain("questionText"), "the browser already shows the text");
    }

    // ------------------------------------------------------------------------------------------------- lip-sync (GAD-T531)

    [Test]
    public void Korean_lip_sync_decomposes_hangul_into_jamo_and_maps_them_to_visemes()
    {
        // The module is browser JavaScript; it is run here with Node when Node is installed.
        var module = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "Gadfly.Server", "wwwroot", "js", "lipsync-ko.js"));
        Assert.That(File.Exists(module), Is.True, module);
        const string script = """
            const m = await import(process.argv[1]);
            const k = new m.LipsyncKo();
            const head = {};
            const out = {
              han: m.decompose("한"), gwa: m.decompose("과"), a: m.decompose("A"),
              hanguk: k.wordsToVisemes("한국어").visemes, ppang: k.wordsToVisemes("빵").visemes, wae: k.wordsToVisemes("왜").visemes,
              pre: k.preProcessText("광합성은 3단계, \"DNA\""), registered: m.registerKorean(head) && head.lipsync.ko instanceof m.LipsyncKo,
              times: k.wordsToVisemes("안녕").times, hangul: m.hasHangul("abc 가") && !m.hasHangul("abc"),
            };
            console.log(JSON.stringify(out));
            """;
        string output;
        try
        {
            // Node writes UTF-8 to a pipe; without this .NET reads it in the console code page (CP437 on Windows) and Hangul or accents come back garbled.
            var psi = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            foreach (var a in new[] { "--input-type=module", "-e", script, new Uri(module).AbsoluteUri }) psi.ArgumentList.Add(a);
            using var node = Process.Start(psi)!;
            output = node.StandardOutput.ReadToEnd();
            var error = node.StandardError.ReadToEnd();
            node.WaitForExit(30_000);
            Assert.That(node.ExitCode, Is.Zero, error);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Ignore("Node is not installed: the Korean lip-sync module is not run here.");
            return;
        }
        var r = JsonDocument.Parse(output).RootElement;
        // 한 = ㅎ (18) + ㅏ (0) + ㄴ (4): (0xD55C - 0xAC00) = 10588 = 18 * 588 + 0 * 28 + 4.
        Assert.That((r.GetProperty("han").GetProperty("initial").GetInt32(), r.GetProperty("han").GetProperty("vowel").GetInt32(), r.GetProperty("han").GetProperty("final").GetInt32()), Is.EqualTo((18, 0, 4)));
        Assert.That((r.GetProperty("gwa").GetProperty("initial").GetInt32(), r.GetProperty("gwa").GetProperty("vowel").GetInt32(), r.GetProperty("gwa").GetProperty("final").GetInt32()), Is.EqualTo((0, 9, 0)));
        Assert.That(r.GetProperty("a").ValueKind, Is.EqualTo(JsonValueKind.Null));
        static string[] V(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();
        // 한 (ㅎ silent: aa, ㄴ nn) 국 (ㄱ kk, ㅜ U, ㄱ kk) 어 (ㅇ silent, ㅓ aa).
        Assert.That(V(r.GetProperty("hanguk")), Is.EqualTo(new[] { "aa", "nn", "kk", "U", "kk", "aa" }));
        Assert.That(V(r.GetProperty("ppang")), Is.EqualTo(new[] { "PP", "aa", "kk" }), "ㅃ lips together, ㅇ final is ng");
        Assert.That(V(r.GetProperty("wae")), Is.EqualTo(new[] { "U", "E" }), "the w- on-glide rounds the lips first");
        Assert.That(r.GetProperty("pre").GetString(), Is.EqualTo("광합성은 삼단계, DNA"));
        Assert.That(r.GetProperty("registered").GetBoolean() && r.GetProperty("hangul").GetBoolean(), Is.True);
        var times = r.GetProperty("times").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Assert.That(times.Zip(times.Skip(1)).All(p => p.Second > p.First), Is.True, "visemes in time order");
    }
}
