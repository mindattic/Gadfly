using Gadfly.Server.Voice;
using Microsoft.Extensions.Configuration;
using MindAttic.SpeechRecognition;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Voice settings, grants and answer building (GAD-T53). Pure: no database, no provider.</summary>
[TestFixture]
public class VoiceTests
{
    private static IConfiguration Config(params (string, string?)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();

    [Test]
    public void Voice_is_mock_in_development_and_off_elsewhere_unless_configured()
    {
        Assert.That(VoiceSettings.From(Config(), development: true, null).Mode, Is.EqualTo(VoiceMode.Mock));
        Assert.That(VoiceSettings.From(Config(), development: false, null).Mode, Is.EqualTo(VoiceMode.Off));
        Assert.That(VoiceSettings.From(Config(("Gadfly:Voice:Mode", "live")), development: false, null).Mode, Is.EqualTo(VoiceMode.Live));
        Assert.That(VoiceSettings.From(Config(("Gadfly:Voice:Mode", "off")), development: true, null).Available, Is.False);
        Assert.That(VoiceSettings.From(Config(("Gadfly:Voice:Mode", "nonsense")), development: true, null).Mode, Is.EqualTo(VoiceMode.Off));
        Assert.That(VoiceSettings.From(Config(("Gadfly:Voice:EndOfAnswerSeconds", "99")), true, null).EndOfAnswerSeconds, Is.EqualTo(15));
    }

    [Test]
    public void End_of_answer_cue_has_a_floor_and_ceiling_that_an_accommodation_lengthens()
    {
        var settings = VoiceSettings.From(Config(), true, null);
        Assert.That(settings.EndCue(1), Is.EqualTo((4000, 12000)));
        Assert.That(settings.EndCue(1.5), Is.EqualTo((6000, 18000)), "extra time lengthens both the floor and the ceiling");
        Assert.That(settings.EndCue(0.5), Is.EqualTo((4000, 12000)), "never shorter than configured");
        Assert.That(settings.EndCue(double.NaN), Is.EqualTo((4000, 12000)));
        Assert.That(settings.EndCue(4).CeilingMs, Is.EqualTo(45000), "hard cap");

        var tight = VoiceSettings.From(Config(("Gadfly:Voice:EndOfAnswerSeconds", "8"), ("Gadfly:Voice:EndOfAnswerMaxSeconds", "3")), true, null);
        Assert.That(tight.EndOfAnswerMaxSeconds, Is.EqualTo(8), "the ceiling is never below the floor");
    }

    [Test]
    public void A_finished_sentence_has_a_shorter_end_of_answer_silence_that_never_passes_the_floor()
    {
        var settings = VoiceSettings.From(Config(), true, null);
        Assert.That(settings.EndCueCompleteMs(1), Is.EqualTo(2000));
        Assert.That(settings.EndCueCompleteMs(1.5), Is.EqualTo(3000), "extra time lengthens it too");
        Assert.That(settings.SpeechHangoverMs, Is.EqualTo(700));

        var custom = VoiceSettings.From(Config(("Gadfly:Voice:EndOfAnswerSeconds", "3"), ("Gadfly:Voice:EndOfAnswerCompleteSeconds", "9"),
            ("Gadfly:Voice:SpeechHangoverMs", "50")), true, null);
        Assert.That(custom.EndCueCompleteMs(1), Is.EqualTo(3000), "never longer than the floor");
        Assert.That(custom.SpeechHangoverMs, Is.EqualTo(300), "clamped");
    }

    [Test]
    public void Grants_allow_only_texts_the_server_offered_to_an_armed_student()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-11-20T10:00:00Z"));
        var grants = new VoiceGrants(clock);
        Guid student = Guid.NewGuid(), other = Guid.NewGuid(), org = Guid.NewGuid(), session = Guid.NewGuid();

        Assert.That(grants.IsArmed(student), Is.False);
        Assert.That(grants.Offer(student, session, "Why?"), Is.False, "nothing can be offered before the exam room arms the student");
        grants.Arm(student, org, session);
        Assert.That(grants.IsArmed(student), Is.True);
        Assert.That(grants.MaySpeak(student, "Why?"), Is.False);
        Assert.That(grants.Offer(student, Guid.NewGuid(), "Why?"), Is.False, "another session's id");
        Assert.That(grants.Offer(student, session, " Why? "), Is.True);
        Assert.That(grants.MaySpeak(student, "Why?"), Is.True);
        Assert.That(grants.MaySpeak(student, "Anything else"), Is.False);
        Assert.That(grants.MaySpeak(other, "Why?"), Is.False, "grants are per student");

        clock.Advance(VoiceGrants.ArmedFor + TimeSpan.FromMinutes(1));
        Assert.That(grants.IsArmed(student), Is.False);
        Assert.That(grants.MaySpeak(student, "Why?"), Is.False);
    }

    [Test]
    public void Grants_end_when_the_student_leaves_and_cap_the_offers()
    {
        var grants = new VoiceGrants(TimeProvider.System);
        Guid student = Guid.NewGuid(), session = Guid.NewGuid();
        grants.Arm(student, Guid.NewGuid(), session);
        for (var i = 0; i < VoiceGrants.MaxOffers + 5; i++) grants.Offer(student, session, "text " + i);
        Assert.That(grants.MaySpeak(student, "text " + (VoiceGrants.MaxOffers + 4)), Is.True);
        Assert.That(grants.MaySpeak(student, "text 0"), Is.False, "the oldest offer fell out");
        grants.Disarm(student, Guid.NewGuid());
        Assert.That(grants.IsArmed(student), Is.True, "another session can't disarm this one");
        grants.Disarm(student, session);
        Assert.That(grants.IsArmed(student), Is.False);
    }

    private static VoiceAnswerPayload Payload(double? confidence, string text = "the groups are equal sized and we add them up", double listening = 0)
    {
        var words = text.Split(' ').Select((w, i) => new VoiceWord { Text = w, StartMs = 1000 + i * 400, EndMs = 1000 + i * 400 + 300, Confidence = confidence }).ToList();
        return new VoiceAnswerPayload { Segments = [new VoiceSegment { Text = text, Words = words }], ListeningStartMs = listening, StartedAtUnixMs = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds() };
    }

    [Test]
    public void A_voice_answer_carries_modality_words_metrics_and_the_librarys_verification_advice()
    {
        var built = VoiceAnswerBuilder.Build(Payload(0.95), DateTimeOffset.UtcNow)!;
        Assert.That(built.Answer.Mode, Is.EqualTo("voice"));
        Assert.That(built.Answer.Text, Is.EqualTo("the groups are equal sized and we add them up"));
        Assert.That(built.Answer.Words, Has.Count.EqualTo(10));
        Assert.That(built.Answer.Words[0].Start, Is.EqualTo(TimeSpan.Zero), "word times are inside the answer");
        Assert.That(built.Answer.SpeechMetrics[SpeechMetricKeys.WordCount], Is.EqualTo(10));
        Assert.That(built.Answer.SpeechMetrics[SpeechMetricKeys.TimeToFirstWordSeconds], Is.EqualTo(1).Within(0.001));
        Assert.That(built.Answer.SpeechMetrics[SpeechMetricKeys.VerificationRecommended], Is.EqualTo(0));
        Assert.That(built.Answer.SpeechMetrics.Count, Is.LessThanOrEqualTo(64), "SessionHub's own cap on speech metrics");

        var doubtful = VoiceAnswerBuilder.Build(Payload(0.3), DateTimeOffset.UtcNow)!;
        Assert.That(doubtful.Verification.Recommended, Is.True);
        Assert.That(doubtful.Answer.SpeechMetrics[SpeechMetricKeys.VerificationRecommended], Is.EqualTo(1));
    }

    [Test]
    public void A_recognizer_with_no_confidence_leaves_no_invented_confidence()
    {
        var built = VoiceAnswerBuilder.Build(Payload(null), DateTimeOffset.UtcNow)!;
        Assert.That(built.Answer.Words, Is.Empty, "the ledger averages word confidence, so none is better than a made-up 1.0");
        Assert.That(built.Verification.Recommended, Is.True, "no confidence data is itself a reason to read it back");
        Assert.That(built.Answer.SpeechMetrics.ContainsKey(SpeechMetricKeys.VerificationConfidence), Is.False);
    }

    [Test]
    public void Empty_or_hostile_payloads_build_nothing()
    {
        Assert.That(VoiceAnswerBuilder.Build(null, DateTimeOffset.UtcNow), Is.Null);
        Assert.That(VoiceAnswerBuilder.Build(new VoiceAnswerPayload(), DateTimeOffset.UtcNow), Is.Null);
        Assert.That(VoiceAnswerBuilder.Build(new VoiceAnswerPayload { Segments = [new VoiceSegment { Text = "   " }] }, DateTimeOffset.UtcNow), Is.Null);
        var many = new VoiceAnswerPayload { Segments = Enumerable.Range(0, VoiceAnswerBuilder.MaxSegments + 1).Select(_ => new VoiceSegment { Text = "x" }).ToList() };
        Assert.That(VoiceAnswerBuilder.Build(many, DateTimeOffset.UtcNow), Is.Null);
        var nan = Payload(0.9);
        nan.Segments[0].Words[0].StartMs = double.NaN;
        Assert.That(VoiceAnswerBuilder.Build(nan, DateTimeOffset.UtcNow)!.Answer.Words, Has.Count.EqualTo(9), "a word with a non-finite time is dropped");
    }

    [Test]
    public void Vocabulary_and_language_are_only_allowed_once_armed_and_set()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-11-20T10:00:00Z"));
        var grants = new VoiceGrants(clock);
        Guid student = Guid.NewGuid(), org = Guid.NewGuid(), session = Guid.NewGuid();

        // Not armed yet: nothing is allowed, and setting the vocabulary before arming does nothing.
        grants.SetVocabulary(student, session, "en", ["mitosis"]);
        Assert.That(grants.AllowedLanguage(student), Is.Null);
        Assert.That(grants.IsVocabularyAllowed(student, ["mitosis"]), Is.False);

        grants.Arm(student, org, session);
        Assert.That(grants.IsVocabularyAllowed(student, ["mitosis"]), Is.False, "armed, but no vocabulary computed yet: nothing is allowed");
        Assert.That(grants.IsVocabularyAllowed(student, []), Is.True, "asking for nothing is always safe, even before the vocabulary is set");
        grants.SetVocabulary(student, session, "en", ["mitosis", "meiosis"]);

        Assert.That(grants.AllowedLanguage(student), Is.EqualTo("en"));
        Assert.That(grants.IsVocabularyAllowed(student, ["mitosis"]), Is.True, "a subset of the exam's own vocabulary");
        Assert.That(grants.IsVocabularyAllowed(student, ["mitosis", "meiosis"]), Is.True);
        Assert.That(grants.IsVocabularyAllowed(student, []), Is.True, "no hints asked for is always safe");
        Assert.That(grants.IsVocabularyAllowed(student, null), Is.True);
        Assert.That(grants.IsVocabularyAllowed(student, ["mitosis", "something the browser made up"]), Is.False,
            "a client-supplied term outside the exam's own vocabulary is refused");

        // Another student's grant can't see this one's vocabulary or language.
        var other = Guid.NewGuid();
        Assert.That(grants.AllowedLanguage(other), Is.Null);
        Assert.That(grants.IsVocabularyAllowed(other, ["mitosis"]), Is.False);
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
