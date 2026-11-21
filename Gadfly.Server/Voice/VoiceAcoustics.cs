namespace Gadfly.Server.Voice;

/// <summary>
/// Summary numbers measured in the browser (wwwroot/js/voice-acoustics.js) from one answer's microphone audio (GAD-T232): no audio,
/// frame or pitch contour leaves the page for these. The server never has the audio, so the numbers are range-checked by
/// <see cref="VoiceAcousticKeys.Add"/>, not recomputed.
/// </summary>
public sealed class VoiceAcoustics
{
    public double? VoicedSeconds { get; set; }
    public double? VoicedFraction { get; set; }
    public double? PitchMedianHz { get; set; }
    public double? PitchSdSemitones { get; set; }
    public double? JitterPercent { get; set; }
    public double? LoudnessMeanDbfs { get; set; }
    public double? LoudnessSdDb { get; set; }
}

/// <summary>
/// The speech-metric names the browser's voice measurements are stored under (with <see cref="SpeechMetricKeys"/>' names in
/// <c>SessionTurn.SpeechMetricsJson</c>); compared with the student's own baseline by <see cref="VoiceStrain"/>.
/// </summary>
public static class VoiceAcousticKeys
{
    public const string VoicedSeconds = "voice.voicedSeconds", VoicedFraction = "voice.voicedFraction", PitchMedianHz = "voice.pitchMedianHz",
        PitchSdSemitones = "voice.pitchSdSemitones", JitterPercent = "voice.jitterPercent", LoudnessMeanDbfs = "voice.loudnessMeanDbfs",
        LoudnessSdDb = "voice.loudnessSdDb";

    /// <summary>
    /// Adds the browser's voice measurements to <paramref name="metrics"/>, keeping only finite values in a plausible range. Pitch,
    /// jitter and loudness are kept only when at least a second of voiced audio was measured.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Add(IReadOnlyDictionary<string, double> metrics, VoiceAcoustics? a)
    {
        if (a is null) return metrics;
        var d = metrics.ToDictionary(kv => kv.Key, kv => kv.Value);
        void Put(string key, double? v, double min, double max)
        {
            if (v is { } x && !double.IsNaN(x) && !double.IsInfinity(x) && x >= min && x <= max) d[key] = Math.Round(x, 3);
        }
        Put(VoicedSeconds, a.VoicedSeconds, 0, 3600);
        Put(VoicedFraction, a.VoicedFraction, 0, 1);
        if (a.VoicedSeconds is >= 1 and <= 3600)
        {
            Put(PitchMedianHz, a.PitchMedianHz, 60, 450);
            Put(PitchSdSemitones, a.PitchSdSemitones, 0, 24);
            Put(JitterPercent, a.JitterPercent, 0, 25);
            Put(LoudnessMeanDbfs, a.LoudnessMeanDbfs, -100, 0);
            Put(LoudnessSdDb, a.LoudnessSdDb, 0, 60);
        }
        return d;
    }
}
