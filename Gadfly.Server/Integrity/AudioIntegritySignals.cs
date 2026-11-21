using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Voice;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Integrity;

/// <summary>One audio observation about a spoken answer, ready to store as an <see cref="IntegrityEvent"/> (source "audio").</summary>
public sealed record AudioSignal(string Type, double Confidence, IntegritySeverity Severity, string Summary, IReadOnlyDictionary<string, double> Evidence);

/// <summary>
/// Audio integrity signals from the speech metrics of a spoken answer (GAD-T58; docs/SOCRATIC_PLATFORM.md "Anti-cheating
/// layers", Speech row). They are annotations for a human reviewer and nothing more:
/// <list type="bullet">
/// <item>never a score, a penalty or a recommendation, and never worded as dishonesty or a feeling: each says what was measured;</item>
/// <item>low confidence by construction (at most 0.6) and never above <see cref="IntegritySeverity.Low"/> severity;</item>
/// <item>each carries its metric evidence, in the summary for the reviewer and as numbers in <see cref="IntegrityEvent.DataJson"/>;</item>
/// <item>thresholds are deliberately blunt and sit beside the explanation: accent, language background, anxiety, careful speech and
/// accommodations all move these numbers (master plan Part 15.1), which is why the review page words them as weak evidence.</item>
/// </list>
/// Signals: <c>second-speaker</c> (the recognizer's diarization heard another voice), <c>reading-cadence</c> (several reading-aloud
/// features agree), <c>fluent-after-silence</c> (a long wait before the first word, then delivery with none of the usual
/// hesitation). <b>Not covered, a known gap:</b> whispered prompting. The library's metrics carry no loudness or signal-level
/// data and diarization does not mark quiet speech, so nothing here can tell a whisper from silence; a whisper that the
/// recognizer transcribes as a second speaker is reported as <c>second-speaker</c>. The review page already has wording for a
/// <c>whispering</c> event for when a source for it exists (a level feature in the speech library).
/// </summary>
public static class AudioIntegritySignals
{
    public const string Source = "audio";
    public const string SecondSpeaker = "second-speaker", ReadingCadence = "reading-cadence", FluentAfterSilence = "fluent-after-silence";

    /// <summary>The longest confidence any audio signal gets.</summary>
    public const double MaxConfidence = 0.6;

    /// <summary>A wait before the first word at least this long counts as a long silence (seconds).</summary>
    public const double LongSilenceSeconds = 10;

    /// <summary>Answers with fewer words say too little for cadence or fluency to mean anything.</summary>
    public const int MinWordsForCadence = 25, MinWordsForFluency = 25;

    public static IReadOnlyList<AudioSignal> Evaluate(IReadOnlyDictionary<string, double> m, IReadOnlyList<IReadOnlyDictionary<string, double>>? earlierAnswers = null)
    {
        var signals = new List<AudioSignal>();
        double? Get(string key) => m.TryGetValue(key, out var v) ? v : null;
        var words = (int)(Get(SpeechMetricKeys.WordCount) ?? 0);

        // 1. A second voice, from diarization: a datum, not a finding. A person in the room, a video call or a diarization error all produce it.
        if (Get(SpeechMetricKeys.SecondSpeaker) == 1)
        {
            var other = (int)(Get(SpeechMetricKeys.OtherSpeakerWords) ?? 0);
            var confidence = Math.Min(MaxConfidence, 0.3 + other / 40.0);
            signals.Add(Make(SecondSpeaker, confidence,
                $"The recognizer heard {Get(SpeechMetricKeys.DistinctSpeakers) ?? 2:0} voices in this answer; {other} of {words} words were attributed to a voice other than the main speaker.",
                m, SpeechMetricKeys.DistinctSpeakers, SpeechMetricKeys.OtherSpeakerWords, SpeechMetricKeys.WordCount));
        }

        // 2. Reading-aloud cadence: several raw features that tend to differ between reading and speaking all point the same way.
        if (words >= MinWordsForCadence)
        {
            var checks = new List<(string Name, bool Hit, string Evidence, string Key)>();
            if (Get(SpeechMetricKeys.PauseLengthCv) is { } pcv && Get(SpeechMetricKeys.PauseCount) is >= 4)
                checks.Add(("regular pauses", pcv < 0.35, $"pause-length variation {pcv:0.00} (reading tends to be below 0.35)", SpeechMetricKeys.PauseLengthCv));
            if (Get(SpeechMetricKeys.RateVariationCv) is { } rcv)
                checks.Add(("steady pace", rcv < 0.20, $"pace variation {rcv:0.00} (below 0.20 is very even)", SpeechMetricKeys.RateVariationCv));
            if (Get(SpeechMetricKeys.PausesAtPunctuation) is { } pap && Get(SpeechMetricKeys.PauseCount) is >= 4)
                checks.Add(("pauses at punctuation", pap >= 0.7, $"{pap:P0} of pauses fall at punctuation (reading tends to be 70% or more)", SpeechMetricKeys.PausesAtPunctuation));
            if (Get(SpeechMetricKeys.WordsBetweenPauses) is { } wbp)
                checks.Add(("long runs between pauses", wbp >= 12, $"{wbp:0.0} words between pauses (reading tends to run 12 or more)", SpeechMetricKeys.WordsBetweenPauses));
            if (Get(SpeechMetricKeys.DisfluencyPer100) is { } dis)
                checks.Add(("no hesitations", dis <= 1, $"{dis:0.0} hesitations per 100 words (reading tends to be 1 or fewer)", SpeechMetricKeys.DisfluencyPer100));
            var hits = checks.Where(c => c.Hit).ToList();
            // At least four features measured and all but one agree; fewer measurements say too little.
            if (checks.Count >= 4 && hits.Count >= checks.Count - 1 && hits.Count >= 4)
                signals.Add(Make(ReadingCadence, Math.Min(MaxConfidence, 0.2 + 0.1 * hits.Count),
                    $"{hits.Count} of {checks.Count} measured features of this answer's rhythm match reading aloud: {string.Join("; ", hits.Select(h => h.Evidence))}.",
                    m, hits.Select(h => h.Key).Append(SpeechMetricKeys.WordCount).ToArray()));
        }

        // 3. A long wait, then an answer with none of the usual hesitation. Compared with the student's own earlier spoken answers when there are any.
        if (words >= MinWordsForFluency && Get(SpeechMetricKeys.TimeToFirstWordSeconds) is { } wait && wait >= LongSilenceSeconds
            && Get(SpeechMetricKeys.DisfluencyTotal) == 0 && Get(SpeechMetricKeys.LongPauseCount) is null or 0)
        {
            var earlier = (earlierAnswers ?? []).Select(e => e.TryGetValue(SpeechMetricKeys.DisfluencyPer100, out var v) ? v : (double?)null).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            var contrast = earlier.Count > 0 && earlier.Average() >= 3;
            signals.Add(Make(FluentAfterSilence, Math.Min(MaxConfidence, contrast ? 0.45 : 0.3),
                $"{wait:0} seconds passed before the first word, then {words} words came with no hesitations, repeats or long pauses"
                + (contrast ? $", where this student's earlier spoken answers averaged {earlier.Average():0.0} hesitations per 100 words." : "."),
                m, SpeechMetricKeys.TimeToFirstWordSeconds, SpeechMetricKeys.WordCount, SpeechMetricKeys.DisfluencyTotal, SpeechMetricKeys.LongPauseCount, SpeechMetricKeys.SpokenSeconds));
        }
        return signals;
    }

    private static AudioSignal Make(string type, double confidence, string measured, IReadOnlyDictionary<string, double> m, params string[] evidenceKeys)
    {
        var evidence = evidenceKeys.Distinct().Where(m.ContainsKey).ToDictionary(k => k, k => m[k]);
        var summary = measured + " " + Caveat;
        return new AudioSignal(type, Math.Round(confidence, 2), confidence >= 0.4 ? IntegritySeverity.Low : IntegritySeverity.Info, summary, evidence);
    }

    private const string Caveat = "A description of the audio, not a finding; accent, language, nerves and accommodations change these numbers.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Stores the signals for a just-captured spoken answer as audio integrity events on the turn, once per type per turn. Part of the
    /// answer's own save, so an event and its answer commit together. Does nothing for typed answers. Never throws into the exam.
    /// </summary>
    public static async Task RecordAsync(GadflyDbContext db, Guid org, Guid sessionId, Guid turnId, Answer answer, CancellationToken ct)
    {
        if (answer.Mode != "voice" || answer.SpeechMetrics.Count == 0) return;
        try
        {
            var earlier = (await db.SessionTurns.AsNoTracking()
                    .Where(t => t.SessionId == sessionId && t.Id != turnId && t.AnswerMode == "voice" && t.SpeechMetricsJson != null)
                    .Select(t => t.SpeechMetricsJson!).ToListAsync(ct))
                .Select(j => JsonSerializer.Deserialize<Dictionary<string, double>>(j, Json)).Where(d => d is not null)
                .Select(d => (IReadOnlyDictionary<string, double>)d!).ToList();
            var have = (await db.IntegrityEvents.Where(e => e.SessionId == sessionId && e.TurnId == turnId && e.Source == Source).Select(e => e.Type).ToListAsync(ct)).ToHashSet();
            foreach (var s in Evaluate(answer.SpeechMetrics, earlier).Where(s => !have.Contains(s.Type)))
                db.IntegrityEvents.Add(new IntegrityEvent
                {
                    OrganizationId = org, SessionId = sessionId, TurnId = turnId, Source = Source, Type = s.Type,
                    StartedAt = answer.StartedAt ?? answer.CompletedAt, EndedAt = answer.CompletedAt,
                    Confidence = s.Confidence, Severity = s.Severity, Summary = s.Summary,
                    DataJson = JsonSerializer.Serialize(s.Evidence, Json),
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Annotations are best effort; a failure here must never cost the student their answer.
        }
    }
}
