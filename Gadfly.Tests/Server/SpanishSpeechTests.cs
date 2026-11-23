using Gadfly.Engine;
using Gadfly.Server.Voice;
using Microsoft.Extensions.Configuration;
using MindAttic.Avatar.ElevenLabs;
using NUnit.Framework;
using VoiceSettings = Gadfly.Server.Voice.VoiceSettings;

namespace Gadfly.Tests.Server;

/// <summary>
/// Speech in Spanish (GAD-T596: GAD-T607 to GAD-T609), mirroring <see cref="KoreanSpeechTests"/>: the recognizer's language, the
/// examiner's voice and model, the mock voice. No provider: mock voice only.
/// </summary>
[TestFixture]
public class SpanishSpeechTests
{
    private static VoiceSettings Settings(params (string Key, string Value)[] values) =>
        VoiceSettings.From(new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => "Gadfly:Voice:" + v.Key, v => (string?)v.Value)).Build(), true, null);

    private static readonly SessionSpeech SpanishNative = new(ExamLanguage.Spanish, ExamLanguage.Spanish);
    private static readonly SessionSpeech EnglishBySpanish = new(ExamLanguage.English, ExamLanguage.Spanish);

    // ---------------------------------------------------------------------------------------------- language (GAD-T607)

    [Test]
    public void Spanish_is_a_supported_exam_language_with_its_names()
    {
        Assert.That(ExamLanguage.Supported, Does.Contain("es"));
        Assert.That(ExamLanguage.Normalize("es-419"), Is.EqualTo("es"));
        Assert.That(ExamLanguage.Normalize("ES_mx"), Is.EqualTo("es"));
        Assert.That(ExamLanguage.IsSpanish("es-MX") && !ExamLanguage.IsSpanish("en"), Is.True);
        Assert.That(ExamLanguage.NativeName("es"), Is.EqualTo("Español"));
        Assert.That(ExamLanguage.EnglishName("es"), Is.EqualTo("Spanish"));
        Assert.That(ExamLanguage.ParseList("en, es-419 ,xx"), Is.EqualTo(new[] { "en", "es" }));
        Assert.That(new AttemptLanguage("es", "es").IsSpanish && !new AttemptLanguage("es", "es").IsDefault, Is.True);
    }

    // ------------------------------------------------------------------------------------------- recognizer language (GAD-T607)

    [Test]
    public void A_spanish_exam_is_heard_as_spanish_and_an_english_exam_by_a_spanish_speaker_is_pinned_to_english()
    {
        Assert.That(SpanishNative.NonNative || SpanishNative.IsDefault, Is.False);
        Assert.That(SpanishNative.RecognizerLanguage(null), Is.EqualTo("es"));
        Assert.That(new SessionSpeech("es-MX", "en").RecognizerLanguage("en"), Is.EqualTo("es"), "Scribe language_code / Whisper language es");
        Assert.That(EnglishBySpanish.RecognizerLanguage(null), Is.EqualTo("en"), "an accent never switches the recognizer to Spanish");
        Assert.That(EnglishBySpanish.Primary, Is.EqualTo("es"));
    }

    [Test]
    public void The_grant_only_allows_the_spanish_recognizer_for_a_spanish_attempt()
    {
        var grants = new VoiceGrants(TimeProvider.System);
        var (user, org, session) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        grants.Arm(user, org, session);
        grants.SetVocabulary(user, session, SpanishNative.RecognizerLanguage(null), ["fotosíntesis", "photosynthesis"]);
        grants.SetSpeech(user, session, SpanishNative);
        Assert.That(grants.AllowedLanguage(user), Is.EqualTo("es"));
        Assert.That(grants.IsVocabularyAllowed(user, ["fotosíntesis"]), Is.True);
        Assert.That(grants.SpeechOf(user, session), Is.EqualTo(SpanishNative));
    }

    [Test]
    public void A_spanish_exam_never_uses_the_english_finished_sentence_cue()
    {
        var cues = Settings().EndCuesFor(1, SpanishNative);
        Assert.That(cues.CompleteMs, Is.EqualTo(cues.FloorMs));
        Assert.That(cues.FloorMs, Is.EqualTo(4000), "a native speaker keeps the usual floor");
        var esl = Settings().EndCuesFor(1, EnglishBySpanish);
        Assert.That(esl.FloorMs, Is.EqualTo(6000), "ESL mode for an English exam by a Spanish speaker");
    }

    // ----------------------------------------------------------------------------------------------- voice (GAD-T607)

    [Test]
    public void A_spanish_exam_speaks_with_the_librarys_multilingual_voice_unless_a_spanish_model_or_voice_is_set()
    {
        var plain = Settings();
        Assert.That(ExaminerTts.Choose(SpanishNative, plain, "eleven_flash_v2_5", null), Is.Null, "flash v2.5 speaks Spanish: the library's voice fits");
        Assert.That(ExaminerTts.Choose(SpanishNative, Settings(("TtsModels:es", "eleven_multilingual_v2")), "eleven_flash_v2_5", null),
            Is.EqualTo(new ExaminerTts.Profile("eleven_multilingual_v2", null)));
        Assert.That(ExaminerTts.Choose(EnglishBySpanish, plain, "eleven_flash_v2_5", null), Is.EqualTo(new ExaminerTts.Profile("eleven_flash_v2_5", 0.9)));
        var s = Settings(("TtsModels:es-419", "eleven_turbo_v2_5"), ("VoiceIds:ES", "abcdefghij1234567890"));
        Assert.That(s.TtsModelFor("es-MX"), Is.EqualTo("eleven_turbo_v2_5"));
        Assert.That(s.VoiceIdFor("es"), Is.EqualTo("abcdefghij1234567890"));
        Assert.That(s.TtsModelFor("ko"), Is.Null);
    }

    [Test]
    public async Task The_mock_voice_reads_spanish_and_counts_accented_vowels()
    {
        var options = new ElevenLabsOptions { ApiKey = "mock-key-not-real", VoiceId = "mock-voice", OutputFormat = "pcm_22050" };
        using var http = new HttpClient(new MockTextToSpeech()) { BaseAddress = new Uri("https://mock.invalid") };
        var client = new ElevenLabsTtsClient(http, options);
        var result = await client.SynthesizeAsync("¿Qué es la fotosíntesis? Explíquelo, por favor.", useCache: false);
        Assert.That(result.Words.Select(w => w.Word), Is.EqualTo(new[] { "¿Qué", "es", "la", "fotosíntesis?", "Explíquelo,", "por", "favor." }));
        Assert.That(result.Audio, Is.Not.Empty);
        Assert.That(MockTextToSpeech.Syllables("canción"), Is.EqualTo(2), "ió is one vowel group");
        Assert.That(MockTextToSpeech.Syllables("mamá"), Is.EqualTo(2));
        Assert.That(MockTextToSpeech.Syllables("photosynthesis"), Is.EqualTo(5), "English is counted as before");
    }
}
