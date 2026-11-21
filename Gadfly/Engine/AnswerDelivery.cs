using System.Globalization;
using System.Text.RegularExpressions;

namespace Gadfly.Engine;

/// <summary>How firmly the examiner should press after an answer (GAD-T233).</summary>
public enum ProbePressure
{
    /// <summary>Nothing in how the answer came suggests shaky understanding.</summary>
    Steady,
    /// <summary>One cue (a long wait, many hesitations, strain above the student's own baseline, or hedging).</summary>
    Some,
    /// <summary>Two or more cues together: probe harder until the concept is clearly understood or clearly not.</summary>
    Shaky,
    /// <summary>
    /// The answer read as recited (GAD-T242: close to the source's wording, or a read-out pattern) and was not accepted: ask for
    /// it differently, applied to a new case, or why.
    /// </summary>
    Recited,
}

/// <summary>
/// How one answer was delivered, against the student's own earlier answers in the session (GAD-T233): the wait before
/// it started, its hesitations, voice strain above the student's baseline, and hedging words. These only steer how hard the
/// next question presses (a concrete example, a challenged assumption, "why" again, a counterfactual). They are never a
/// score, never evidence for or against understanding, and never told to the student: nerves, accent, language background
/// and accommodations all move these numbers.
/// </summary>
public sealed record AnswerDelivery
{
    /// <summary>0-based turn index of the answer described.</summary>
    public required int TurnIndex { get; init; }
    /// <summary>Seconds from the question to the first word (or keystroke); null when unknown.</summary>
    public double? DelaySeconds { get; init; }
    /// <summary>The student's usual delay over their earlier answers; null with no earlier answers.</summary>
    public double? BaselineDelaySeconds { get; init; }
    /// <summary>Hesitations (fillers, repeats, fragments) per 100 words; null for typed answers.</summary>
    public double? HesitationsPer100 { get; init; }
    public double? BaselineHesitationsPer100 { get; init; }
    /// <summary>Pauses longer than the recognizer's long-pause threshold.</summary>
    public int LongPauses { get; init; }
    /// <summary>Voice strain against the student's own baseline (0 = at baseline, higher = more strained); null when unknown.</summary>
    public double? StrainVsBaseline { get; init; }
    /// <summary>Hedging phrases found in the answer, as said.</summary>
    public IReadOnlyList<string> Hedges { get; init; } = [];
    /// <summary>The cues that fired, in plain words for the prompt and the reviewer.</summary>
    public IReadOnlyList<string> Cues { get; init; } = [];
    public ProbePressure Pressure { get; init; }
    /// <summary>The recitation check's finding on this answer (GAD-T242); null when it read as the student's own.</summary>
    public RecitationFlag? Recitation { get; init; }

    /// <summary>Speech metric names read here; they match the server's SpeechMetricKeys (the engine cannot reference the server).</summary>
    public const string TimeToFirstWordKey = "timeToFirstWordSeconds", DisfluencyPer100Key = "disfluency.per100Words",
        FilledPausesPer100Key = "filledPauses.per100Words", LongPauseCountKey = "pauses.longCount", PauseRatioKey = "pauses.ratio",
        SpeechRateKey = "speechRateWpm";

    /// <summary>
    /// Voice strain against the student's own baseline, when the browser's voice analysis supplies it (GAD-T232): 0 at the
    /// baseline, 1 = clearly above it. Without it, a proxy from pause ratio and speech rate against earlier answers is used.
    /// </summary>
    public const string StrainVsBaselineKey = "strain.vsBaseline";

    /// <summary>A wait at least this long, and at least <see cref="DelayFactor"/> times the student's usual, is a cue.</summary>
    public const double LongDelaySeconds = 8, DelayFactor = 2;
    /// <summary>Hesitations per 100 words at least this many, and at least <see cref="HesitationFactor"/> times the usual, is a cue.</summary>
    public const double ManyHesitationsPer100 = 6, HesitationFactor = 2;
    /// <summary>Strain against baseline at or above this is a cue.</summary>
    public const double StrainCue = 0.5;
    /// <summary>This many hedging phrases in one answer is a cue.</summary>
    public const int HedgesCue = 2;

    /// <summary>Describes <paramref name="turn"/>'s answer against the student's other answers in <paramref name="session"/>; null when it has no answer.</summary>
    public static AnswerDelivery? Read(SocraticSession session, Turn turn)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (turn.Answer is not { } answer) return null;
        var earlier = session.Turns.Where(t => t.Index < turn.Index && t.Answer is not null && !string.IsNullOrWhiteSpace(t.Answer.Text)).ToList();

        var delay = Delay(turn);
        var baseDelay = Median(earlier.Select(Delay));
        var hes = Hesitations(answer);
        var baseHes = Median(earlier.Select(t => Hesitations(t.Answer!)));
        var longPauses = (int)(Metric(answer, LongPauseCountKey) ?? 0);
        var strain = Metric(answer, StrainVsBaselineKey) ?? StrainProxy(answer, earlier.Select(t => t.Answer!).ToList());
        var hedges = HedgesIn(answer.Text);

        var cues = new List<string>();
        if (delay is { } d && d >= LongDelaySeconds && (baseDelay is not { } bd || d >= DelayFactor * Math.Max(bd, 1)))
            cues.Add($"a long wait before answering ({Num(d)}s" + (baseDelay is { } b1 ? $", usually {Num(b1)}s)" : ")"));
        if ((hes is { } h && h >= ManyHesitationsPer100 && (baseHes is not { } bh || h >= HesitationFactor * Math.Max(bh, 1))) || longPauses >= 2)
            cues.Add($"many hesitations ({(hes is { } h2 ? $"{Num(h2)} per 100 words" + (baseHes is { } b2 ? $", usually {Num(b2)}" : "") : "")}"
                + (longPauses >= 2 ? $"{(hes is null ? "" : "; ")}{longPauses} long pauses" : "") + ")");
        if (strain is { } s && s >= StrainCue)
            cues.Add($"voice strain above the student's own baseline ({Num(s)})");
        if (hedges.Count >= HedgesCue)
            cues.Add($"hedging ({string.Join(", ", hedges.Select(x => $"\"{x}\""))})");
        var recited = turn.Judgment?.Recitation;
        if (recited is not null) cues.Add($"recited, not accepted ({recited.Reason})");

        return new AnswerDelivery
        {
            TurnIndex = turn.Index, DelaySeconds = delay, BaselineDelaySeconds = baseDelay, HesitationsPer100 = hes,
            BaselineHesitationsPer100 = baseHes, LongPauses = longPauses, StrainVsBaseline = strain, Hedges = hedges, Cues = cues,
            Recitation = recited,
            Pressure = recited is not null ? ProbePressure.Recited
                : cues.Count switch { 0 => ProbePressure.Steady, 1 => ProbePressure.Some, _ => ProbePressure.Shaky },
        };
    }

    /// <summary>One line per answered turn in <paramref name="turns"/>, for the question prompt; "(none)" when nothing was answered.</summary>
    public static string Describe(SocraticSession session, IEnumerable<Turn> turns)
    {
        var lines = turns.Select(t => Read(session, t)).Where(d => d is not null).Select(d => d!.Line()).ToList();
        return lines.Count == 0 ? "(no answers yet)" : string.Join("\n", lines);
    }

    /// <summary>The prompt's guidance for the next question, from the last answer's delivery.</summary>
    public static string Guidance(AnswerDelivery? last) => last?.Pressure switch
    {
        ProbePressure.Recited => RecitationCheck.Guidance,
        ProbePressure.Shaky => "Understanding in the last answer looks shaky. Probe harder within this move: ask for a concrete example, "
            + "challenge an assumption the answer relies on, ask why once more, or pose a counterfactual, so the next answer shows clearly "
            + "whether the concept is understood or not.",
        ProbePressure.Some => "The last answer shows some doubt. Press a little: prefer asking for a concrete example or why once more over moving on.",
        _ => "No sign of shaky understanding in how the last answer came; ask the move as planned.",
    };

    /// <summary>This answer as one prompt line, e.g. "Answer 3: a long wait before answering (12s, usually 3s); hedging (...)".</summary>
    public string Line()
    {
        var facts = new List<string>();
        if (DelaySeconds is { } d) facts.Add($"started after {Num(d)}s");
        if (HesitationsPer100 is { } h) facts.Add($"{Num(h)} hesitations per 100 words");
        if (StrainVsBaseline is { } s) facts.Add($"strain vs baseline {Num(s)}");
        facts.Add(Hedges.Count == 0 ? "no hedging" : $"{Hedges.Count} hedge(s)");
        var verdict = Cues.Count == 0 ? "steady" : string.Join("; ", Cues);
        return $"- Answer {TurnIndex + 1}: {string.Join(", ", facts)}. Cues: {verdict}.";
    }

    private static double? Delay(Turn t)
    {
        if (t.Answer is not { } a) return null;
        // Only the recognizer's same-clock measure: Answer.StartedAt is the browser's clock and Turn.AskedAt the server's,
        // so their difference is clock skew as much as delay. Unknown otherwise.
        return Metric(a, TimeToFirstWordKey);
    }

    private static double? Hesitations(Answer a)
    {
        // Disfluency already counts filled pauses (fillers + repetitions + fragments): filled pauses alone only when it is missing.
        return Metric(a, DisfluencyPer100Key) ?? Metric(a, FilledPausesPer100Key);
    }

    /// <summary>More pausing and slower speech than the student's own earlier spoken answers, as a 0.. rise; null without both.</summary>
    private static double? StrainProxy(Answer a, IReadOnlyList<Answer> earlier)
    {
        var parts = new List<double>();
        if (Metric(a, PauseRatioKey) is { } pr && Median(earlier.Select(e => Metric(e, PauseRatioKey))) is { } bpr && bpr > 0)
            parts.Add(Math.Max(0, pr / bpr - 1));
        if (Metric(a, SpeechRateKey) is { } sr && sr > 0 && Median(earlier.Select(e => Metric(e, SpeechRateKey))) is { } bsr && bsr > 0)
            parts.Add(Math.Max(0, bsr / sr - 1));
        return parts.Count == 0 ? null : Math.Round(parts.Average(), 2);
    }

    private static double? Metric(Answer a, string key) =>
        a.SpeechMetrics.TryGetValue(key, out var v) && !double.IsNaN(v) && !double.IsInfinity(v) ? v : null;

    private static double? Median(IEnumerable<double?> values)
    {
        var v = values.Where(x => x.HasValue).Select(x => x!.Value).OrderBy(x => x).ToList();
        if (v.Count == 0) return null;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    /// <summary>Hedging phrases in <paramref name="text"/>, lower-cased, in order, each once.</summary>
    public static IReadOnlyList<string> HedgesIn(string? text) => string.IsNullOrWhiteSpace(text) ? []
        : Hedge.Matches(text).Select(m => Regex.Replace(m.Value.ToLowerInvariant(), @"\s+", " ").Replace('’', '\'')).Distinct().ToList();

    private static string Num(double v) => v.ToString(v >= 10 ? "0" : "0.#", CultureInfo.InvariantCulture);

    private static readonly Regex Hedge = new(
        @"\b(i\s+think|i\s+guess|i\s+believe|i\s+suppose|i\s+assume|maybe|perhaps|probably|possibly|might\s+be|could\s+be|kind\s+of|sort\s+of"
        + @"|i['’]?m\s+not\s+(?:sure|certain)|not\s+(?:sure|certain)|i\s+don['’]?t\s+(?:really\s+)?know|if\s+i\s+remember(?:\s+right(?:ly)?)?|or\s+something"
        + @"|something\s+like\s+that|i['’]?d\s+say|presumably|i\s+feel\s+like|not\s+really)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
