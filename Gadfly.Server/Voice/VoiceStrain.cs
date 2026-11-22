using System.Globalization;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Gadfly.Server.Voice;

/// <summary>One measured difference from the student's own voice baseline, in plain words.</summary>
public sealed record VoiceStrainIndicator(string Feature, string Text, bool Notable);

/// <summary>How one spoken answer's voice compares with the same student's first spoken answers in the session.</summary>
public sealed record VoiceStrainReading(bool IsBaseline, int BaselineAnswers, double? VsBaseline, IReadOnlyList<VoiceStrainIndicator> Indicators)
{
    /// <summary>
    /// A plain-language line for the transcript: the raw acoustic differences from the student's first answers and what they do
    /// not mean. GAD-T259: it names no state ("strain", "stress", "hesitation"); a reader sees measurements, not a reading of the person.
    /// </summary>
    public string Summary
    {
        get
        {
            if (IsBaseline) return "Part of this student's voice baseline (their first spoken answers); nothing to compare yet.";
            if (Indicators.Count == 0) return "No voice measurements to compare with this student's first answers. " + VoiceStrain.Caveat;
            var notable = Indicators.Count(i => i.Notable);
            var head = notable == 0
                ? "Voice measurements close to this student's first answers"
                : $"Voice measurements against this student's first answers ({notable} beyond the usual range, marked *)";
            var detail = ": " + string.Join(", ", Indicators.Select(i => i.Notable ? i.Text + "*" : i.Text));
            return head + detail + ". " + VoiceStrain.Caveat;
        }
    }

    /// <summary>The short form for the student's own live transcript line in the interview room.</summary>
    public string Brief
    {
        get
        {
            if (IsBaseline) return "Voice: measuring how you usually sound (your first answers).";
            var notable = Indicators.Where(i => i.Notable).ToList();
            // GAD-T259: the measured differences only, never a named state such as "strain" or "hesitation".
            var head = notable.Count == 0 ? "close to your first answers" : "against your first answers, " + string.Join(", ", notable.Select(i => i.Text));
            return $"Voice: {head}. A measurement of your voice, not a judgement of you or the answer.";
        }
    }
}

/// <summary>
/// Voice strain indicators (GAD-T232). The browser measures the student's voice during each spoken answer (pitch level and
/// variability, jitter, loudness; <see cref="VoiceAcousticKeys"/>) and the speech library gives speech rate and pause
/// ratio. Here each answer is compared with the same student's first <see cref="BaselineAnswers"/> spoken answers in the session,
/// and the differences are stored with the answer as <c>strain.*</c> speech metrics, shown on the review transcript.
/// <para>
/// Descriptive only. A raised pitch, an unsteady or louder voice, slower speech or more pausing describe how the voice changed;
/// they can come from effort, nerves, a hard question, tiredness, a cold or the room. They are never read as emotion, honesty or
/// deception, never scored, never an integrity event; the student sees a short form on their own live transcript. <see cref="VsBaselineKey"/> may steer how
/// hard the examiner probes (GAD-T233), never the grade.
/// </para>
/// <para>
/// GAD-T259: shown as raw acoustic differences, never labelled as a state, and off by default for an EU/EEA organization and for
/// a student marked as living in the EU/EEA (<see cref="IsEnabled"/>; EU AI Act Art. 5(1)(f), docs/PRIVACY.md). When off, the
/// browser's voice measurements are dropped from the answer, nothing is compared, and <see cref="OffKey"/> tells the examiner's
/// probing (AnswerDelivery) not to stand in its own pause-and-rate proxy.
/// </para>
/// </summary>
public static class VoiceStrain
{
    /// <summary>The first this many spoken answers of a session make the student's baseline.</summary>
    public const int BaselineAnswers = 2;

    public const string Pitch = "pitch", PitchVariability = "pitchVariability", Jitter = "jitter", Loudness = "loudness", Rate = "rate", Pauses = "pauses";

    public const string IsBaselineKey = "strain.isBaseline", BaselineAnswersKey = "strain.baselineAnswers",
        PitchSemitonesKey = "strain.pitchSemitones", PitchVariabilityRatioKey = "strain.pitchVariabilityRatio",
        JitterRatioKey = "strain.jitterRatio", LoudnessDbKey = "strain.loudnessDb", SpeechRatePercentKey = "strain.speechRatePercent",
        PauseRatioDeltaKey = "strain.pauseRatioDelta";

    /// <summary>
    /// 0 at the student's baseline, 1 = clearly above it: the mean of the measured rises (higher pitch, more jitter, a louder or
    /// quieter voice, slower speech, more pausing), each scaled so its notable threshold is 1. Read by GAD-T233's AnswerDelivery.
    /// </summary>
    public const string VsBaselineKey = "strain.vsBaseline";

    /// <summary>Differences at which one feature counts as notable (and scores 1 in <see cref="VsBaselineKey"/>).</summary>
    public const double PitchSemitones = 2, VariabilityRatio = 1.5, JitterRatio = 1.5, LoudnessDb = 4, SlowerFraction = 0.25, PauseRatioDelta = 0.1;

    /// <summary>
    /// GAD-T259: set (to 1) on a spoken answer whose voice was not measured because voice measurements are off for this organization
    /// or student. Matches the engine's <c>AnswerDelivery.StrainOffKey</c>.
    /// </summary>
    public const string OffKey = AnswerDelivery.StrainOffKey;

    public const string Caveat = "This describes the voice, not the person: effort, nerves, a hard question, tiredness or the room all change it, and it says nothing about honesty.";

    /// <summary>
    /// Adds the <c>strain.*</c> comparison to <paramref name="current"/>'s metrics, given the metrics of the session's earlier spoken
    /// answers in the order they were given. Returns the metrics unchanged when there is nothing to compare.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Compare(IReadOnlyDictionary<string, double> current, IReadOnlyList<IReadOnlyDictionary<string, double>> earlierInOrder)
    {
        var d = current.Where(kv => !kv.Key.StartsWith("strain.", StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (earlierInOrder.Count < BaselineAnswers)
        {
            d[IsBaselineKey] = 1;
            d[BaselineAnswersKey] = earlierInOrder.Count;
            return d;
        }
        var baseline = earlierInOrder.Take(BaselineAnswers).ToList();
        double? Base(string key)
        {
            var v = baseline.Select(m => Get(m, key)).OfType<double>().ToList();
            return v.Count == 0 ? null : v.Average();
        }
        void Put(string key, double v) => d[key] = Math.Round(v, 3);

        d[IsBaselineKey] = 0;
        d[BaselineAnswersKey] = baseline.Count;
        var rises = new List<double>();
        if (Get(current, VoiceAcousticKeys.PitchMedianHz) is { } p && p > 0 && Base(VoiceAcousticKeys.PitchMedianHz) is { } bp && bp > 0)
        {
            var st = 12 * Math.Log2(p / bp);
            Put(PitchSemitonesKey, st);
            rises.Add(Math.Max(0, st / PitchSemitones));
        }
        if (Get(current, VoiceAcousticKeys.PitchSdSemitones) is { } v && Base(VoiceAcousticKeys.PitchSdSemitones) is { } bv && bv > 0)
            Put(PitchVariabilityRatioKey, v / bv);
        if (Get(current, VoiceAcousticKeys.JitterPercent) is { } j && Base(VoiceAcousticKeys.JitterPercent) is { } bj && bj > 0)
        {
            Put(JitterRatioKey, j / bj);
            rises.Add(Math.Max(0, (j / bj - 1) / (JitterRatio - 1)));
        }
        if (Get(current, VoiceAcousticKeys.LoudnessMeanDbfs) is { } l && Base(VoiceAcousticKeys.LoudnessMeanDbfs) is { } bl)
        {
            Put(LoudnessDbKey, l - bl);
            rises.Add(Math.Abs(l - bl) / LoudnessDb);
        }
        if (Get(current, SpeechMetricKeys.SpeechRateWpm) is { } r && r > 0 && Base(SpeechMetricKeys.SpeechRateWpm) is { } br && br > 0)
        {
            Put(SpeechRatePercentKey, (r - br) / br * 100);
            rises.Add(Math.Max(0, (br / r - 1) / SlowerFraction));
        }
        if (Get(current, SpeechMetricKeys.PauseRatio) is { } pr && Base(SpeechMetricKeys.PauseRatio) is { } bpr)
        {
            Put(PauseRatioDeltaKey, pr - bpr);
            rises.Add(Math.Max(0, (pr - bpr) / PauseRatioDelta));
        }
        if (rises.Count > 0) Put(VsBaselineKey, Math.Min(3, rises.Average()));
        return d;
    }

    /// <summary>
    /// Adds the comparison to a spoken answer about to be captured, reading the session's earlier spoken answers from the database.
    /// Best effort: on any failure the answer is returned as it came, so the student never loses it.
    /// </summary>
    public static async Task<Answer> ApplyAsync(GadflyDbContext db, Guid sessionId, Answer answer, CancellationToken ct)
    {
        if (answer.Mode != "voice" || answer.SpeechMetrics.Count == 0) return answer;
        // GAD-T259: off for this organization or student (or not known): drop the voice measurements and compare nothing.
        bool enabled;
        try { enabled = await EnabledForSessionAsync(db, sessionId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { enabled = false; }
        if (!enabled) return answer with { SpeechMetrics = TurnOff(answer.SpeechMetrics) };
        try
        {
            var earlier = (await db.SessionTurns.AsNoTracking()
                    .Where(t => t.SessionId == sessionId && t.AnswerMode == "voice" && t.SpeechMetricsJson != null)
                    .OrderBy(t => t.Index).Select(t => t.SpeechMetricsJson!).Take(BaselineAnswers).ToListAsync(ct))
                .Select(j => JsonSerializer.Deserialize<Dictionary<string, double>>(j)).Where(m => m is { Count: > 0 })
                .Select(m => (IReadOnlyDictionary<string, double>)m!).ToList();
            return answer with { SpeechMetrics = Compare(answer.SpeechMetrics, earlier) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return answer;
        }
    }

    /// <summary>
    /// GAD-T259: whether voice measurements run. <see cref="VoiceMeasuresSetting.RegionDefault"/> turns them off for an EU/EEA
    /// organization and for a student marked as living in the EU/EEA, and leaves them on elsewhere; On and Off apply to everyone.
    /// </summary>
    public static bool IsEnabled(VoiceMeasuresSetting setting, bool organizationInEu, bool studentInEu) => setting switch
    {
        VoiceMeasuresSetting.On => true,
        VoiceMeasuresSetting.Off => false,
        _ => !organizationInEu && !studentInEu,
    };

    /// <summary>
    /// GAD-T259: <see cref="IsEnabled"/> for a session, from its organization's settings and its assignment. A session that cannot
    /// be found counts as off.
    /// </summary>
    public static async Task<bool> EnabledForSessionAsync(GadflyDbContext db, Guid sessionId, CancellationToken ct)
    {
        var session = await db.ExamSessions.AsNoTracking().Where(s => s.Id == sessionId)
            .Select(s => new { s.OrganizationId, s.AssignmentId }).FirstOrDefaultAsync(ct);
        if (session is null) return false;
        var settings = await db.OrganizationSettings.AsNoTracking().Where(o => o.OrganizationId == session.OrganizationId)
            .Select(o => new { o.VoiceMeasures, o.InEuropeanUnion }).FirstOrDefaultAsync(ct);
        var studentInEu = await db.ExamAssignments.AsNoTracking().Where(a => a.Id == session.AssignmentId)
            .Select(a => a.StudentInEuropeanUnion).FirstOrDefaultAsync(ct);
        return IsEnabled(settings?.VoiceMeasures ?? VoiceMeasuresSetting.RegionDefault, settings?.InEuropeanUnion ?? false, studentInEu);
    }

    /// <summary>GAD-T259: the metrics without the browser's voice measurements or any comparison, marked <see cref="OffKey"/>.</summary>
    public static IReadOnlyDictionary<string, double> TurnOff(IReadOnlyDictionary<string, double> metrics)
    {
        var d = metrics.Where(kv => !kv.Key.StartsWith("strain.", StringComparison.Ordinal) && !kv.Key.StartsWith("voice.", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        d[OffKey] = 1;
        return d;
    }

    /// <summary>Reads the stored comparison back for the transcript; null for a typed answer or one recorded before GAD-T232.</summary>
    public static VoiceStrainReading? Describe(IReadOnlyDictionary<string, double> m)
    {
        if (Get(m, IsBaselineKey) is not { } isBaseline) return null;
        var count = (int)(Get(m, BaselineAnswersKey) ?? 0);
        if (isBaseline >= 1) return new VoiceStrainReading(true, count, null, []);
        var list = new List<VoiceStrainIndicator>();
        if (Get(m, PitchSemitonesKey) is { } st)
            list.Add(new(Pitch, $"pitch {Signed(st, "0.0")} semitones", st >= PitchSemitones));
        if (Get(m, PitchVariabilityRatioKey) is { } vr)
            list.Add(new(PitchVariability, $"pitch variability {Num(vr, "0.0")}× baseline", vr >= VariabilityRatio || vr <= 1 / VariabilityRatio));
        if (Get(m, JitterRatioKey) is { } jr)
            list.Add(new(Jitter, $"jitter {Num(jr, "0.0")}× baseline", jr >= JitterRatio));
        if (Get(m, LoudnessDbKey) is { } db)
            list.Add(new(Loudness, $"loudness {Signed(db, "0.0")} dB", Math.Abs(db) >= LoudnessDb));
        if (Get(m, SpeechRatePercentKey) is { } rp)
            list.Add(new(Rate, rp < 0 ? $"speech {Num(-rp, "0")}% slower" : $"speech {Num(rp, "0")}% faster", rp < 0 && -rp / (100 + rp) >= SlowerFraction));
        if (Get(m, PauseRatioDeltaKey) is { } pd)
            list.Add(new(Pauses, $"time spent pausing {Signed(pd * 100, "0")} points", pd >= PauseRatioDelta));
        return new VoiceStrainReading(false, count, Get(m, VsBaselineKey), list);
    }

    private static double? Get(IReadOnlyDictionary<string, double> m, string key) =>
        m.TryGetValue(key, out var v) && !double.IsNaN(v) && !double.IsInfinity(v) ? v : null;

    private static string Num(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
    private static string Signed(double v, string format) => (v >= 0 ? "+" : "") + Num(v, format);
}
