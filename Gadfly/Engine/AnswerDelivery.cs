using System.Globalization;
using System.Text.RegularExpressions;

namespace Gadfly.Engine;

/// <summary>How firmly the examiner should press after an answer (GAD-T233, GAD-T258).</summary>
public enum ProbePressure
{
    /// <summary>Nothing in what the answer said, or in how the judges read it, suggests shaky understanding.</summary>
    Steady,
    /// <summary>One content cue (hedging, vague wording, no reason or example, a judged score not yet convincing, evidence against, or unclear meaning).</summary>
    Some,
    /// <summary>Two or more content cues together: probe harder until the concept is clearly understood or clearly not.</summary>
    Shaky,
    /// <summary>
    /// The answer read as recited (GAD-T242) and was not accepted as the student's own understanding: ask for it differently,
    /// applied to a new case, or why.
    /// </summary>
    Recited,
}

/// <summary>
/// What one answer showed, for steering how hard the next question presses (GAD-T233). Since GAD-T258 (the owner reversed
/// decision (b) on 2026-11-21) the pressure comes only from the answer's content and the judges' reading of it: hedging and
/// vague wording in the transcript, a missing reason or example where the move asked for one, a judged score that is not yet
/// convincing, the response analysis's evidence against understanding or unclear meaning, and the recitation check. Nothing
/// from the voice steers it: not the voice comparison (pitch, jitter, loudness, <c>strain.vsBaseline</c>), and not the wait
/// before the first word, the hesitation count or long pauses either (timing is weak evidence of understanding and moves with
/// nerves, accent, second-language use, stuttering and accommodations). Those timing and voice numbers are still recorded on
/// this record and with the answer, and reviewers see them raw where voice measurements are on; they never reach the question
/// prompt. The cues are never a score, never evidence for or against understanding, and never told to the student.
/// </summary>
public sealed record AnswerDelivery
{
    /// <summary>0-based turn index of the answer described.</summary>
    public required int TurnIndex { get; init; }
    /// <summary>Seconds from the question to the first word (or keystroke); null when unknown. Recorded only, never a cue (GAD-T258).</summary>
    public double? DelaySeconds { get; init; }
    /// <summary>The student's usual delay over their earlier answers; null with no earlier answers. Recorded only.</summary>
    public double? BaselineDelaySeconds { get; init; }
    /// <summary>Hesitations (fillers, repeats, fragments) per 100 words; null for typed answers. Recorded only, never a cue (GAD-T258).</summary>
    public double? HesitationsPer100 { get; init; }
    public double? BaselineHesitationsPer100 { get; init; }
    /// <summary>Pauses longer than the recognizer's long-pause threshold. Recorded only, never a cue (GAD-T258).</summary>
    public int LongPauses { get; init; }
    /// <summary>The voice comparison against the student's own baseline, as measured; null when unknown or off. Never a cue (GAD-T258).</summary>
    public double? StrainVsBaseline { get; init; }
    /// <summary>Hedging phrases found in the answer, as said.</summary>
    public IReadOnlyList<string> Hedges { get; init; } = [];
    /// <summary>Vague phrases found in the answer ("stuff", "somehow", "and so on"...), as said.</summary>
    public IReadOnlyList<string> Vague { get; init; } = [];
    /// <summary>False when the move asked for a reason or example and the answer gave neither; null when the move did not ask.</summary>
    public bool? GaveReasonOrExample { get; init; }
    /// <summary>The judges' overall score for the answer (0..1); null before it was judged.</summary>
    public double? JudgedScore { get; init; }
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
    /// The voice comparison against the student's own baseline, when the browser's voice analysis supplies it (GAD-T232): 0 at
    /// the baseline, 1 = clearly above it. Recorded for the reviewer; since GAD-T258 it never steers probing.
    /// </summary>
    public const string StrainVsBaselineKey = "strain.vsBaseline";

    /// <summary>
    /// GAD-T259: set on a spoken answer when voice measurements are off for the organization or student (off by default in the
    /// EU/EEA). Then nothing voice-derived is read at all (GAD-T283), so the read-out pattern is not applied either.
    /// </summary>
    public const string StrainOffKey = "strain.off";

    /// <summary>This many hedging phrases in one answer is a cue.</summary>
    public const int HedgesCue = 2;
    /// <summary>This many vague phrases in one answer is a cue.</summary>
    public const int VagueCue = 2;
    /// <summary>A judged overall score below this (not yet positive evidence, <see cref="ObjectiveAssessor.PositiveAtOrAbove"/>) is a cue.</summary>
    public const double ConvincingScore = ObjectiveAssessor.PositiveAtOrAbove;

    /// <summary>Moves whose question asks for a reason, a mechanism or an example, so an answer with neither is a cue.</summary>
    public static bool AsksForReason(MoveKind move) => move is MoveKind.Probe or MoveKind.CausalReasoning or MoveKind.ScenarioVariation
        or MoveKind.AssumptionTesting or MoveKind.IndependentExplanation or MoveKind.Apply or MoveKind.Synthesize
        or MoveKind.Counterexample or MoveKind.Contradiction;

    /// <summary>Describes <paramref name="turn"/>'s answer against the student's other answers in <paramref name="session"/>; null when it has no answer.</summary>
    public static AnswerDelivery? Read(SocraticSession session, Turn turn)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (turn.Answer is not { } answer) return null;
        var earlier = session.Turns.Where(t => t.Index < turn.Index && t.Answer is not null && !string.IsNullOrWhiteSpace(t.Answer.Text)).ToList();

        // Recorded only (GAD-T258): timing and voice numbers stay on the record for the read-out check and the reviewer, and are
        // not read at all when voice measurements are off (GAD-T259, GAD-T283). None of them is a cue.
        var voiceOff = VoiceOff(answer);
        var delay = voiceOff ? null : Delay(turn);
        var baseDelay = voiceOff ? null : Median(earlier.Select(Delay));
        var hes = voiceOff ? null : Hesitations(answer);
        var baseHes = voiceOff ? null : Median(earlier.Select(t => Hesitations(t.Answer!)));
        var longPauses = voiceOff ? 0 : (int)(Metric(answer, LongPauseCountKey) ?? 0);
        var strain = voiceOff ? null : Metric(answer, StrainVsBaselineKey);

        // The cues: what the answer said, and how the judges and the response analysis read it (GAD-T258).
        var hedges = HedgesIn(answer.Text);
        var vague = VagueIn(answer.Text);
        bool? reasoned = AsksForReason(turn.Move) && !string.IsNullOrWhiteSpace(answer.Text) ? HasReasonOrExample(answer.Text) : null;
        var recited = turn.Judgment?.Recitation;
        var score = turn.Judgment?.OverallScore;
        var analysis = turn.Analysis is { IsValid: true } an ? an : null;

        var cues = new List<string>();
        if (hedges.Count >= HedgesCue)
            cues.Add($"hedging ({Quoted(hedges)})");
        if (vague.Count >= VagueCue)
            cues.Add($"vague wording ({Quoted(vague)})");
        if (reasoned == false)
            cues.Add($"no reason or example given, though the {turn.Move} question asked for one");
        // A recited answer is left out of the evidence, so its score says nothing about understanding.
        if (recited is null && score is { } sc && sc < ConvincingScore)
            cues.Add($"the judges did not find it convincing (score {Num(sc)})");
        if (analysis is not null)
        {
            var against = analysis.Misconceptions.Select(m => m.Text)
                .Concat(analysis.Evidence.Where(e => e.Polarity is EvidencePolarity.Contradicts or EvidencePolarity.Misconception).Select(e => e.Note))
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (against.Count > 0) cues.Add($"evidence against understanding ({against[0]})");
            var unclear = analysis.Uncertainties
                .Concat(analysis.Evidence.Where(e => e.Polarity == EvidencePolarity.Uncertain).Select(e => e.Note))
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (unclear.Count > 0) cues.Add($"unclear meaning ({unclear[0]})");
        }
        if (recited is not null) cues.Add($"recited, not accepted ({recited.Reason})");

        return new AnswerDelivery
        {
            TurnIndex = turn.Index, DelaySeconds = delay, BaselineDelaySeconds = baseDelay, HesitationsPer100 = hes,
            BaselineHesitationsPer100 = baseHes, LongPauses = longPauses, StrainVsBaseline = strain, Hedges = hedges, Vague = vague,
            GaveReasonOrExample = reasoned, JudgedScore = score, Cues = cues, Recitation = recited,
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
        _ => "No sign of shaky understanding in what the last answer said; ask the move as planned.",
    };

    /// <summary>
    /// This answer as one prompt line, e.g. "Answer 3: 2 hedge(s), judged 0.4. Cues: hedging (...); ...". Content only: the
    /// timing and voice numbers on this record never reach the question prompt (GAD-T258).
    /// </summary>
    public string Line()
    {
        var facts = new List<string> { Hedges.Count == 0 ? "no hedging" : $"{Hedges.Count} hedge(s)" };
        if (Vague.Count > 0) facts.Add($"{Vague.Count} vague phrase(s)");
        if (GaveReasonOrExample is { } r) facts.Add(r ? "gave a reason or example" : "no reason or example");
        if (JudgedScore is { } j && Recitation is null) facts.Add($"judged {Num(j)}");
        var verdict = Cues.Count == 0 ? "steady" : string.Join("; ", Cues);
        return $"- Answer {TurnIndex + 1}: {string.Join(", ", facts)}. Cues: {verdict}.";
    }

    /// <summary>True when the answer was taken with voice measurements off (GAD-T259).</summary>
    internal static bool VoiceOff(Answer a) => Metric(a, StrainOffKey) is > 0;

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
        : Hedge.Matches(text).Select(m => Regex.Replace(m.Value.ToLowerInvariant(), @"\s+", " ").Replace('’', '\'')).Distinct()
            .Concat(LanguageText.HasHangul(text) ? LanguageText.KoreanHedges(text) : []).ToList();   // Korean lists only on Hangul (GAD-T537)

    /// <summary>Vague phrases in <paramref name="text"/> ("stuff", "somehow", "and so on"...), lower-cased, in order, each once.</summary>
    public static IReadOnlyList<string> VagueIn(string? text) => string.IsNullOrWhiteSpace(text) ? []
        : VaguePhrase.Matches(text).Select(m => Regex.Replace(m.Value.ToLowerInvariant(), @"\s+", " ")).Distinct()
            .Concat(LanguageText.HasHangul(text) ? LanguageText.KoreanVague(text) : []).ToList();

    /// <summary>True when <paramref name="text"/> gives a reason ("because", "so", "which means", "if"...) or an example ("for example", "such as"...).</summary>
    public static bool HasReasonOrExample(string? text) => !string.IsNullOrWhiteSpace(text)
        && (Reasoning.IsMatch(text) || LanguageText.HasHangul(text) && LanguageText.KoreanReasonOrExample(text));

    private static string Quoted(IEnumerable<string> phrases) => string.Join(", ", phrases.Select(x => $"\"{x}\""));

    private static string Num(double v) => v.ToString(v >= 10 ? "0" : "0.#", CultureInfo.InvariantCulture);

    private static readonly Regex Hedge = new(
        @"\b(i\s+think|i\s+guess|i\s+believe|i\s+suppose|i\s+assume|maybe|perhaps|probably|possibly|might\s+be|could\s+be|kind\s+of|sort\s+of"
        + @"|i['’]?m\s+not\s+(?:sure|certain)|not\s+(?:sure|certain)|i\s+don['’]?t\s+(?:really\s+)?know|if\s+i\s+remember(?:\s+right(?:ly)?)?|or\s+something"
        + @"|something\s+like\s+that|i['’]?d\s+say|presumably|i\s+feel\s+like|not\s+really)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex VaguePhrase = new(
        @"\b(stuff|somehow|whatever|etc|et\s+cetera|and\s+so\s+(?:on|forth)|and\s+all\s+that|basically|in\s+general|generally\s+speaking"
        + @"|you\s+know|kind\s+of\s+thing|(?:things|stuff)\s+like\s+that|the\s+thing\s+is|some\s+kind\s+of)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Reasoning = new(
        @"\b(because|cause|since|so|therefore|thus|hence|which\s+means|that\s+means|this\s+means|that['’]?s\s+why|due\s+to|as\s+a\s+result"
        + @"|leads?\s+to|led\s+to|causes?|caused|results?\s+in|in\s+order\s+to|if|unless|otherwise|for\s+example|for\s+instance|such\s+as"
        + @"|like\s+when|imagine|suppose|say\s+(?:you|we|i))\b|(?<!\w)e\.\s?g\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
