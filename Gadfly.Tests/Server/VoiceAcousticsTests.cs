using Gadfly.Server.Voice;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The browser's voice measurements (GAD-T232) are range-checked, not recomputed: only finite, plausible values are kept,
/// and pitch, jitter and loudness only once at least a second of voiced audio was measured.
/// </summary>
[TestFixture]
public class VoiceAcousticsTests
{
    private static readonly IReadOnlyDictionary<string, double> Existing = new Dictionary<string, double> { ["wordsPerMinute"] = 120 };

    [Test]
    public void NoMeasurements_ReturnsTheMetricsUnchanged()
    {
        Assert.That(VoiceAcousticKeys.Add(Existing, null), Is.SameAs(Existing));
    }

    [Test]
    public void PlausibleValues_AreAddedRoundedBesideTheExistingMetrics()
    {
        var d = VoiceAcousticKeys.Add(Existing, new VoiceAcoustics
        {
            VoicedSeconds = 12.34567, VoicedFraction = 0.6, PitchMedianHz = 180, PitchSdSemitones = 2.5, JitterPercent = 1.2,
            LoudnessMeanDbfs = -24, LoudnessSdDb = 6,
        });
        Assert.That(d["wordsPerMinute"], Is.EqualTo(120));
        Assert.That(d[VoiceAcousticKeys.VoicedSeconds], Is.EqualTo(12.346));
        Assert.That(d[VoiceAcousticKeys.PitchMedianHz], Is.EqualTo(180));
        Assert.That(d[VoiceAcousticKeys.LoudnessMeanDbfs], Is.EqualTo(-24));
        Assert.That(d, Has.Count.EqualTo(8));
        Assert.That(Existing, Has.Count.EqualTo(1), "the input dictionary is not modified");
    }

    [Test]
    public void UnderASecondOfVoice_KeepsOnlyTheVoicedAmounts()
    {
        var d = VoiceAcousticKeys.Add(Existing, new VoiceAcoustics { VoicedSeconds = 0.5, VoicedFraction = 0.1, PitchMedianHz = 180, JitterPercent = 1 });
        Assert.That(d.ContainsKey(VoiceAcousticKeys.VoicedSeconds), Is.True);
        Assert.That(d.ContainsKey(VoiceAcousticKeys.VoicedFraction), Is.True);
        Assert.That(d.ContainsKey(VoiceAcousticKeys.PitchMedianHz), Is.False);
        Assert.That(d.ContainsKey(VoiceAcousticKeys.JitterPercent), Is.False);
    }

    [Test]
    public void OutOfRangeOrNonFiniteValues_AreDropped()
    {
        var d = VoiceAcousticKeys.Add(new Dictionary<string, double>(), new VoiceAcoustics
        {
            VoicedSeconds = 10, VoicedFraction = 1.5, PitchMedianHz = 20, PitchSdSemitones = double.NaN, JitterPercent = double.PositiveInfinity,
            LoudnessMeanDbfs = 3, LoudnessSdDb = -1,
        });
        Assert.That(d.Keys, Is.EquivalentTo(new[] { VoiceAcousticKeys.VoicedSeconds }));
    }

    [Test]
    public void ImplausiblyLongVoice_KeepsNothingElse()
    {
        var d = VoiceAcousticKeys.Add(new Dictionary<string, double>(), new VoiceAcoustics { VoicedSeconds = 4000, PitchMedianHz = 180 });
        Assert.That(d, Is.Empty);
    }
}
