using Gadfly.Server.Integrity;
using Gadfly.Server.Voice;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Audio integrity signals from speech metrics (GAD-T58): low-confidence annotations with their evidence. Pure, no database.</summary>
[TestFixture]
public class AudioIntegritySignalsTests
{
    private static Dictionary<string, double> Metrics(params (string, double)[] values) =>
        new(values.Select(v => new KeyValuePair<string, double>(v.Item1, v.Item2)));

    [Test]
    public void A_second_speaker_becomes_a_low_confidence_event_with_its_evidence()
    {
        var signals = AudioIntegritySignals.Evaluate(Metrics(
            (SpeechMetricKeys.WordCount, 40), (SpeechMetricKeys.SecondSpeaker, 1), (SpeechMetricKeys.DistinctSpeakers, 2), (SpeechMetricKeys.OtherSpeakerWords, 6)));
        var s = signals.Single(x => x.Type == AudioIntegritySignals.SecondSpeaker);
        Assert.That(s.Confidence, Is.LessThanOrEqualTo(AudioIntegritySignals.MaxConfidence));
        Assert.That(s.Severity, Is.LessThanOrEqualTo(Gadfly.Server.Data.IntegritySeverity.Low));
        Assert.That(s.Summary, Does.Contain("6 of 40 words"));
        Assert.That(s.Evidence[SpeechMetricKeys.OtherSpeakerWords], Is.EqualTo(6));
        Assert.That(s.Summary.ToLowerInvariant(), Does.Not.Contain("cheat").And.Not.Contain("dishonest").And.Not.Contain("lying"));
    }

    [Test]
    public void Reading_cadence_needs_most_measured_features_to_agree()
    {
        var reading = Metrics(
            (SpeechMetricKeys.WordCount, 60), (SpeechMetricKeys.PauseCount, 8), (SpeechMetricKeys.PauseLengthCv, 0.2), (SpeechMetricKeys.RateVariationCv, 0.1),
            (SpeechMetricKeys.PausesAtPunctuation, 0.9), (SpeechMetricKeys.WordsBetweenPauses, 14), (SpeechMetricKeys.DisfluencyPer100, 0));
        var signal = AudioIntegritySignals.Evaluate(reading).Single(s => s.Type == AudioIntegritySignals.ReadingCadence);
        Assert.That(signal.Confidence, Is.InRange(0.2, AudioIntegritySignals.MaxConfidence));

        var talking = Metrics(
            (SpeechMetricKeys.WordCount, 60), (SpeechMetricKeys.PauseCount, 8), (SpeechMetricKeys.PauseLengthCv, 0.9), (SpeechMetricKeys.RateVariationCv, 0.5),
            (SpeechMetricKeys.PausesAtPunctuation, 0.2), (SpeechMetricKeys.WordsBetweenPauses, 5), (SpeechMetricKeys.DisfluencyPer100, 6));
        Assert.That(AudioIntegritySignals.Evaluate(talking), Is.Empty);

        var tooShort = new Dictionary<string, double>(reading) { [SpeechMetricKeys.WordCount] = 10 };
        Assert.That(AudioIntegritySignals.Evaluate(tooShort), Is.Empty, "a short answer says too little");

        var fewFeatures = Metrics((SpeechMetricKeys.WordCount, 60), (SpeechMetricKeys.RateVariationCv, 0.1), (SpeechMetricKeys.DisfluencyPer100, 0));
        Assert.That(AudioIntegritySignals.Evaluate(fewFeatures), Is.Empty, "fewer than four measured features");
    }

    [Test]
    public void A_long_silence_then_smooth_delivery_is_noted_and_stronger_against_a_hesitant_baseline()
    {
        var m = Metrics((SpeechMetricKeys.WordCount, 50), (SpeechMetricKeys.TimeToFirstWordSeconds, 14), (SpeechMetricKeys.DisfluencyTotal, 0),
            (SpeechMetricKeys.LongPauseCount, 0), (SpeechMetricKeys.SpokenSeconds, 20));
        var alone = AudioIntegritySignals.Evaluate(m).Single(s => s.Type == AudioIntegritySignals.FluentAfterSilence);
        var against = AudioIntegritySignals.Evaluate(m, [Metrics((SpeechMetricKeys.DisfluencyPer100, 6)), Metrics((SpeechMetricKeys.DisfluencyPer100, 4))])
            .Single(s => s.Type == AudioIntegritySignals.FluentAfterSilence);
        Assert.That(against.Confidence, Is.GreaterThan(alone.Confidence));
        Assert.That(against.Confidence, Is.LessThanOrEqualTo(AudioIntegritySignals.MaxConfidence));

        var shortWait = new Dictionary<string, double>(m) { [SpeechMetricKeys.TimeToFirstWordSeconds] = 4 };
        Assert.That(AudioIntegritySignals.Evaluate(shortWait), Is.Empty);
        var hesitant = new Dictionary<string, double>(m) { [SpeechMetricKeys.DisfluencyTotal] = 3 };
        Assert.That(AudioIntegritySignals.Evaluate(hesitant), Is.Empty);
        var unknownWait = new Dictionary<string, double>(m);
        unknownWait.Remove(SpeechMetricKeys.TimeToFirstWordSeconds);
        Assert.That(AudioIntegritySignals.Evaluate(unknownWait), Is.Empty, "absent means unknown, never zero");
    }
}
