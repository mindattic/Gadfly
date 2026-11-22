using Gadfly.Engine;
using MindAttic.SpeechRecognition;
using MindAttic.SpeechRecognition.Metrics;

namespace Gadfly.Server.Voice;

/// <summary>One recognized word, as the browser relays it from the speech relay's final segment. Times are capture-clock milliseconds.</summary>
public sealed class VoiceWord
{
    public string Text { get; set; } = "";
    public double StartMs { get; set; }
    public double EndMs { get; set; }
    public double? Confidence { get; set; }
    public string? SpeakerId { get; set; }
    public bool IsAudioEvent { get; set; }
}

public sealed class VoiceSegment
{
    public string Text { get; set; } = "";
    public List<VoiceWord> Words { get; set; } = [];
}

/// <summary>What the exam room's browser sends when an answer is finished: the final segments and when the student could first speak.</summary>
public sealed class VoiceAnswerPayload
{
    public List<VoiceSegment> Segments { get; set; } = [];
    /// <summary>
    /// The question this answers (GAD-T321): the turn the browser heard asked, so an answer it kept and sent again later (GAD-T473)
    /// still names its own question; VoiceRoom fills in the turn shown when it arrived if the browser gave none. Used only to turn an
    /// answer away from a question it wasn't meant for, never to choose which question it answers.
    /// </summary>
    public int? Turn { get; set; }
    /// <summary>The capture-clock time the examiner stopped speaking (the microphone opened), for time to first word.</summary>
    public double? ListeningStartMs { get; set; }
    /// <summary>Wall-clock time of the first speech (Unix ms), the answer's start.</summary>
    public long? StartedAtUnixMs { get; set; }
    /// <summary>
    /// How long the student took to start speaking after the examiner finished, as the browser timed it and showed it live in the
    /// interview room (GAD-T231). Display only, for the on-screen transcript: never scored or stored (the stored time to first word
    /// is recomputed from the words by <see cref="VoiceAnswerBuilder"/>).
    /// </summary>
    public double? ResponseDelayMs { get; set; }
    /// <summary>The browser's measurements of the student's voice during this answer (GAD-T232), or null; see <see cref="VoiceAcousticKeys"/>.</summary>
    public VoiceAcoustics? Acoustics { get; set; }
    /// <summary>
    /// Recognizer latency (GAD-T361): how long each committed utterance of this answer took, from the browser's
    /// <c>Commit</c> call to the recognizer's final transcript arriving back, timed in the browser since the hub pushes
    /// the final segment rather than returning it from a call the server could time itself. Diagnostics only (the
    /// in-memory status-page counters, <see cref="Gadfly.Server.Diagnostics.VoiceRecognitionTimingRecorder"/>): never
    /// shown to the student, never stored with the answer, never part of a grade.
    /// </summary>
    public List<double>? RecognizerLatenciesMs { get; set; }
}

/// <summary>The answer the orchestrator takes, plus what the speech library advised about the transcript.</summary>
public sealed record VoiceAnswerResult(Answer Answer, VerificationAssessment Verification, SpeechMetrics Metrics);

/// <summary>
/// Turns the browser's relayed segments into a Gadfly <see cref="Answer"/> (GAD-T53). The metrics and the verification advice
/// are recomputed here from the words with the library's calculators, not trusted from the browser. Confidence is kept only when
/// the provider gave some: a recognizer with none (the Simulated one) leaves the answer's word list empty rather than inventing
/// a perfect score, so the ledger's confidence stays null.
/// </summary>
public static class VoiceAnswerBuilder
{
    public const int MaxSegments = 60, MaxWords = 5000, MaxText = 20_000;

    public static VoiceAnswerResult? Build(VoiceAnswerPayload? payload, DateTimeOffset now)
    {
        if (payload?.Segments is not { Count: > 0 and <= MaxSegments } raw) return null;
        var segments = new List<TranscriptSegment>();
        var index = 0;
        var wordTotal = 0;
        foreach (var s in raw)
        {
            var words = (s.Words ?? []).Where(w => IsFinite(w.StartMs) && IsFinite(w.EndMs) && !string.IsNullOrWhiteSpace(w.Text)).Take(MaxWords - wordTotal)
                .Select(w => new TranscriptWord(w.Text.Trim(), TimeSpan.FromMilliseconds(w.StartMs), TimeSpan.FromMilliseconds(Math.Max(w.StartMs, w.EndMs)),
                    w.Confidence is { } c and >= 0 and <= 1 ? c : null, string.IsNullOrWhiteSpace(w.SpeakerId) ? null : w.SpeakerId, w.IsAudioEvent))
                .ToList();
            wordTotal += words.Count;
            segments.Add(new TranscriptSegment { Index = index++, IsFinal = true, Text = (s.Text ?? "").Trim(), Words = words });
        }
        var text = string.Join(' ', segments.Select(s => s.Text).Where(t => t.Length > 0));
        if (text.Length == 0 || text.Length > MaxText) return null;

        var listening = payload.ListeningStartMs is { } l && IsFinite(l) && l >= 0 ? TimeSpan.FromMilliseconds(l) : (TimeSpan?)null;
        var metrics = SpeechMetricsCalculator.Compute(segments, new SpeechMetricsContext { ListeningStartedAt = listening });
        var verification = TranscriptVerification.Assess(segments);

        var spoken = segments.SelectMany(s => s.Words).Where(w => !w.IsAudioEvent).ToList();
        var first = spoken.Count > 0 ? spoken.Min(w => w.Start) : TimeSpan.Zero;
        // Words are timed inside the answer; confidence is only reported when the provider has one for every word.
        var timed = spoken.Count > 0 && spoken.All(w => w.Confidence.HasValue)
            ? spoken.Select(w => new TimedWord(w.Text, w.Start - first, w.End - first, w.Confidence!.Value)).ToList()
            : [];
        var startedAt = payload.StartedAtUnixMs is { } ms && ms > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null;
        if (startedAt > now || startedAt < now - TimeSpan.FromHours(6)) startedAt = null;

        var answer = new Answer
        {
            Text = text, Mode = "voice", Words = timed, StartedAt = startedAt, CompletedAt = now,
            // Watched for a cut-off by the end-of-answer silence (GAD-T462): 0 now, set to 1 if the student goes on speaking at once.
            SpeechMetrics = AnswerCutOff.Watched(VoiceAcousticKeys.Add(SpeechMetricKeys.Flatten(metrics, verification), payload.Acoustics)),
        };
        return new VoiceAnswerResult(answer, verification, metrics);
    }

    private static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
}

/// <summary>
/// The names under which an answer's speech metrics are stored (<c>SessionTurn.SpeechMetricsJson</c> and the ledger's capture
/// audit event). Numbers only; booleans are 0 or 1. Absent means unknown, never zero.
/// </summary>
public static class SpeechMetricKeys
{
    public const string WordCount = "wordCount", SpokenSeconds = "spokenSeconds", TimeToFirstWordSeconds = "timeToFirstWordSeconds",
        SpeechRateWpm = "speechRateWpm", ArticulationRateWpm = "articulationRateWpm",
        PauseCount = "pauses.count", PauseTotalSeconds = "pauses.totalSeconds", PauseMeanSeconds = "pauses.meanSeconds",
        PauseMaxSeconds = "pauses.maxSeconds", LongPauseCount = "pauses.longCount", PausesPerMinute = "pauses.perMinute", PauseRatio = "pauses.ratio",
        FilledPauseCount = "filledPauses.count", FilledPausesPer100 = "filledPauses.per100Words",
        Repetitions = "disfluency.repetitions", Fragments = "disfluency.fragments", DisfluencyTotal = "disfluency.total", DisfluencyPer100 = "disfluency.per100Words",
        PauseLengthCv = "reading.pauseLengthCv", RateVariationCv = "reading.rateVariationCv", PausesAtPunctuation = "reading.pausesAtPunctuationFraction",
        WordsBetweenPauses = "reading.meanWordsBetweenPauses",
        SpeakerLabels = "speakers.hasLabels", DistinctSpeakers = "speakers.distinct", OtherSpeakerWords = "speakers.otherSpeakerWords",
        SecondSpeaker = "speakers.secondSpeakerDetected",
        VerificationRecommended = "verification.recommended", VerificationConfidence = "verification.confidence", LowConfidenceWords = "verification.lowConfidenceWords";

    public static IReadOnlyDictionary<string, double> Flatten(SpeechMetrics m, VerificationAssessment? verification = null)
    {
        var d = new Dictionary<string, double>
        {
            [WordCount] = m.WordCount, [SpokenSeconds] = m.SpokenSeconds,
            [PauseCount] = m.Pauses.Count, [PauseTotalSeconds] = m.Pauses.TotalSeconds, [PauseMeanSeconds] = m.Pauses.MeanSeconds,
            [PauseMaxSeconds] = m.Pauses.MaxSeconds, [LongPauseCount] = m.Pauses.LongCount, [PausesPerMinute] = m.Pauses.PerMinute, [PauseRatio] = m.Pauses.Ratio,
            [FilledPauseCount] = m.FilledPauses.Count, [FilledPausesPer100] = m.FilledPauses.Per100Words,
            [Repetitions] = m.Disfluency.Repetitions, [Fragments] = m.Disfluency.Fragments, [DisfluencyTotal] = m.Disfluency.Total, [DisfluencyPer100] = m.Disfluency.Per100Words,
            [SpeakerLabels] = m.Speakers.HasSpeakerLabels ? 1 : 0, [DistinctSpeakers] = m.Speakers.DistinctSpeakers,
            [OtherSpeakerWords] = m.Speakers.OtherSpeakerWords, [SecondSpeaker] = m.Speakers.SecondSpeakerDetected ? 1 : 0,
        };
        void Put(string key, double? v) { if (v is { } x && !double.IsNaN(x) && !double.IsInfinity(x)) d[key] = Math.Round(x, 4); }
        Put(TimeToFirstWordSeconds, m.TimeToFirstWordSeconds);
        Put(SpeechRateWpm, m.SpeechRateWpm);
        Put(ArticulationRateWpm, m.ArticulationRateWpm);
        Put(PauseLengthCv, m.ReadingAloud.PauseLengthCv);
        Put(RateVariationCv, m.ReadingAloud.RateVariationCv);
        Put(PausesAtPunctuation, m.ReadingAloud.PausesAtPunctuationFraction);
        Put(WordsBetweenPauses, m.ReadingAloud.MeanWordsBetweenPauses);
        if (verification is not null)
        {
            d[VerificationRecommended] = verification.Recommended ? 1 : 0;
            d[LowConfidenceWords] = verification.LowConfidenceWords.Count;
            Put(VerificationConfidence, verification.Confidence);
        }
        foreach (var key in d.Keys.ToList()) d[key] = Math.Round(d[key], 4);
        return d;
    }
}
