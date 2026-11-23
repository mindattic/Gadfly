using System.Diagnostics;
using System.Text.Json;
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

    // ------------------------------------------------------------------------------------------------- lip-sync (GAD-T608)

    [Test]
    public void Spanish_lip_sync_reads_regular_spelling_into_visemes()
    {
        // The module is browser JavaScript; it is run here with Node when Node is installed.
        var module = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "Gadfly.Server", "wwwroot", "js", "lipsync-es.js"));
        Assert.That(File.Exists(module), Is.True, module);
        const string script = """
            const m = await import(process.argv[1]);
            const k = new m.LipsyncEs();
            const head = {};
            const v = (w) => k.wordsToVisemes(w).visemes;
            const out = {
              llamar: v("llamar"), chico: v("chico"), perro: v("perro"), queso: v("queso"), guerra: v("guerra"), pinguino: v("pingüino"),
              hola: v("hola"), nino: v("niño"), cena: v("cena"), yo: v("yo"), hoy: v("hoy"), cancion: v("canción"),
              pre: k.preProcessText("¿Qué es el 25%? ¡Explíquelo en 3 pasos!"), n: m.numberToSpanish("2026"), n21: m.numberToSpanish("21000"),
              registered: m.registerSpanish(head) && head.lipsync.es instanceof m.LipsyncEs, times: k.wordsToVisemes("fotosíntesis").times,
            };
            console.log(JSON.stringify(out));
            """;
        string output;
        try
        {
            var psi = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { "--input-type=module", "-e", script, new Uri(module).AbsoluteUri }) psi.ArgumentList.Add(a);
            using var node = Process.Start(psi)!;
            output = node.StandardOutput.ReadToEnd();
            var error = node.StandardError.ReadToEnd();
            node.WaitForExit(30_000);
            Assert.That(node.ExitCode, Is.Zero, error);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Ignore("Node is not installed: the Spanish lip-sync module is not run here.");
            return;
        }
        var r = JsonDocument.Parse(output).RootElement;
        string[] V(string name) => r.GetProperty(name).EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.That(V("llamar"), Is.EqualTo(new[] { "CH", "aa", "PP", "aa", "RR" }), "ll is one sound (yeísmo)");
        Assert.That(V("chico"), Is.EqualTo(new[] { "CH", "I", "kk", "O" }));
        Assert.That(V("perro"), Is.EqualTo(new[] { "PP", "E", "RR", "O" }), "rr is one trill");
        Assert.That(V("queso"), Is.EqualTo(new[] { "kk", "E", "SS", "O" }), "the u of qu is silent");
        Assert.That(V("guerra"), Is.EqualTo(new[] { "kk", "E", "RR", "aa" }), "the u of gue is silent");
        Assert.That(V("pinguino"), Is.EqualTo(new[] { "PP", "I", "nn", "kk", "U", "I", "nn", "O" }), "the u of gü is heard");
        Assert.That(V("hola"), Is.EqualTo(new[] { "O", "nn", "aa" }), "h is silent");
        Assert.That(V("nino"), Is.EqualTo(new[] { "nn", "I", "nn", "I", "O" }), "ñ is n with an i-glide");
        Assert.That(V("cena"), Is.EqualTo(new[] { "SS", "E", "nn", "aa" }), "seseo: c before e is s");
        Assert.That(V("yo"), Is.EqualTo(new[] { "CH", "O" }));
        Assert.That(V("hoy"), Is.EqualTo(new[] { "O", "I" }));
        Assert.That(V("cancion"), Is.EqualTo(new[] { "kk", "aa", "nn", "SS", "I", "O", "nn" }), "an accented vowel has its base vowel's shape");
        Assert.That(r.GetProperty("pre").GetString(), Is.EqualTo("Qué es el veinticinco por ciento? Explíquelo en tres pasos!"));
        Assert.That(r.GetProperty("n").GetString(), Is.EqualTo("dos mil veintiséis"));
        Assert.That(r.GetProperty("n21").GetString(), Is.EqualTo("veintiún mil"));
        Assert.That(r.GetProperty("registered").GetBoolean(), Is.True);
        var times = r.GetProperty("times").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Assert.That(times.Zip(times.Skip(1)).All(p => p.Second > p.First), Is.True, "visemes in time order");
    }

    // ------------------------------------------------------------------------------------------------- fillers (GAD-T609)

    [Test]
    public void Spanish_fixed_lines_never_judge_the_answer()
    {
        // "correct" (correct-), "good / very good" (bien), "exactly" (exact-), "perfect", "excellent", "great" (genial), "of course"
        // (claro), "that's it" (eso es), "interesting", "right?" (¿verdad?), "wrong" (incorrect-, equivoc-, mal): none may appear in a line
        // she says to every answer.
        var judging = new[] { "correct", "bien", "exact", "perfect", "excelent", "genial", "claro", "eso es", "interesante", "verdad", "incorrect", "equivoc", "mal" };
        var es = ExaminerFiller.Spanish;
        foreach (var line in es.Acknowledgements.Concat(es.Backchannels).Concat(es.First).Concat(es.Again))
            Assert.That(judging.Where(w => line.Contains(w, StringComparison.OrdinalIgnoreCase)), Is.Empty, line);
        Assert.That(es.Acknowledgements, Does.Contain("Mm.").And.Contain("Ajá.").And.Contain("Entiendo.").And.Contain("De acuerdo.").And.Contain("Gracias."));
        Assert.That(es.Remarks.Keys, Is.EquivalentTo(ExaminerFiller.English.Remarks.Keys), "every eye-contact remark exists in Spanish");
        Assert.That(ExaminerFiller.For("es-MX"), Is.SameAs(ExaminerFiller.Spanish));
        Assert.That(ExaminerFiller.For("ko"), Is.SameAs(ExaminerFiller.Korean), "Korean unchanged");
        // Usted, never tú: no tú-form imperatives or pronouns in what she says.
        var all = ExaminerFiller.FixedLines("es", backchannels: true).ToArray();
        foreach (var tu in new[] { " tú ", "dime", "piensa", "cuéntame", "tómate", "continúa", " te " })
            Assert.That(all.Where(l => (" " + l.ToLowerInvariant() + " ").Contains(tu, StringComparison.Ordinal)), Is.Empty, tu);
    }

    [Test]
    public void Spanish_eye_contact_remarks_never_ask_the_student_to_look_at_her_and_gentle_reminders_are_warmed()
    {
        foreach (var kind in new[] { "look-away", "reading" })
            Assert.That(ExaminerFiller.Remark(kind, "es"), Does.Not.Contain("míreme").And.Not.Contain("mire").And.Not.Contain("mírame"), kind);
        Assert.That(ExaminerFiller.Remark("look-away", "es"), Is.EqualTo(Gadfly.Server.Integrity.WebcamIntegrityRecorder.GentleRemindersSpanish[0]));
        Assert.That(Gadfly.Server.Integrity.WebcamIntegrityRecorder.RemindersFor("es-419"), Is.SameAs(Gadfly.Server.Integrity.WebcamIntegrityRecorder.GentleRemindersSpanish));
        Assert.That(Gadfly.Server.Integrity.WebcamIntegrityRecorder.GentleRemindersSpanish, Has.Count.EqualTo(Gadfly.Server.Integrity.WebcamIntegrityRecorder.GentleReminders.Count));
        var es = ExaminerFiller.FixedLines("es", false).ToArray();
        Assert.That(es, Is.SupersetOf(Gadfly.Server.Integrity.WebcamIntegrityRecorder.GentleRemindersSpanish));
        Assert.That(es, Is.Unique);
    }

    [Test]
    public void Spanish_acknowledgements_and_fillers_are_picked_from_the_Spanish_set_and_vary()
    {
        var random = new Random(5);
        string? lastAck = null, lastFirst = null;
        for (var i = 0; i < 100; i++)
        {
            var ack = ExaminerFiller.PickAcknowledgement(random, lastAck, "es");
            Assert.That(ExaminerFiller.Spanish.Acknowledgements, Does.Contain(ack));
            Assert.That(ack, Is.Not.EqualTo(lastAck));
            lastAck = ack;
            var (first, again) = ExaminerFiller.Pick(random, lastFirst, "es");
            Assert.That(ExaminerFiller.Spanish.First, Does.Contain(first));
            Assert.That(ExaminerFiller.Spanish.Again, Does.Contain(again));
            lastFirst = first;
        }
    }
}
