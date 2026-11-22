using System.Text.RegularExpressions;
using Gadfly.Engine;

namespace Gadfly.Sources.Verification;

/// <summary>A run of consecutive words the speech recognizer was unsure of (GAD-T265).</summary>
/// <param name="Text">The words as the recognizer wrote them.</param>
/// <param name="MinConfidence">The lowest word confidence in the run (0-1).</param>
public sealed record UncertainSegment(string Text, TimeSpan Start, TimeSpan End, double MinConfidence);

/// <summary>Word-level differences between a reference transcript and what the recognizer wrote.</summary>
public sealed record WordErrorCount(int Substitutions, int Deletions, int Insertions, int ReferenceWords)
{
    public int Errors => Substitutions + Deletions + Insertions;
    /// <summary>Word error rate: errors over reference words (0 when the reference is empty and nothing was inserted).</summary>
    public double Rate => ReferenceWords == 0 ? (Insertions == 0 ? 0 : 1) : (double)Errors / ReferenceWords;
}

/// <summary>One voice answer in the speech-recognition accuracy audit.</summary>
/// <param name="StudentKey">Who answered (only used to count distinct students per group).</param>
/// <param name="RecognizedText">What the recognizer wrote (transcript version 1).</param>
/// <param name="ReferenceText">The verified text after any transcription correction.</param>
/// <param name="TranscriptChecked">True when the student confirmed or corrected the transcript, so <paramref name="ReferenceText"/> is a reference.</param>
/// <param name="Corrected">True when a transcription correction changed the text.</param>
/// <param name="Words">Words with recognizer confidence (empty when timings were not kept).</param>
/// <param name="HumanReference">
/// True when <paramref name="ReferenceText"/> is a human listener's transcript of what was said (GAD-T426), not the student's
/// check; <paramref name="TranscriptChecked"/> is then true too, so the answer counts toward word error rate.
/// </param>
public sealed record SpeechSample(string StudentKey, string RecognizedText, string ReferenceText, bool TranscriptChecked, bool Corrected,
    int Words, int LowConfidenceWords, double? MeanConfidence, bool HumanReference = false);

/// <summary>The audit's figures for one group of students.</summary>
/// <param name="HumanReferenced">Answers whose word error rate is measured against a human listener's transcript (GAD-T426).</param>
public sealed record SpeechGroupStats(string Group, int Students, int Responses, int CheckedResponses, int CorrectedResponses,
    int ReferenceWords, int WordErrors, double? WordErrorRate, double? MeanConfidence, double? LowConfidenceShare, bool Reportable,
    int HumanReferenced = 0)
{
    /// <summary>Share of all voice answers whose transcript the student had to correct.</summary>
    public double? CorrectedShare => Responses == 0 ? null : (double)CorrectedResponses / Responses;
}

/// <summary>
/// Speech-recognition accuracy (GAD-T265, research note 02: Koenecke2020, Mujtaba2024). Recognizers make more
/// errors for some accents and dialects and for people who stutter, and a transcription error must not be scored
/// as wrong content. Deterministic helpers, no LLM:
/// <list type="bullet">
/// <item><see cref="UncertainSegments"/> finds the runs of words the recognizer was unsure of, so graders (the AI
/// panel and the human) are told which words may be mis-heard.</item>
/// <item><see cref="WordErrors"/> is the standard word error rate (Levenshtein over normalized words).</item>
/// <item><see cref="DisfluencyRate"/> counts fillers and repeated words, a proxy grouping that needs no personal data.</item>
/// <item><see cref="Summarize"/> builds one group's audit figures, with small groups marked not reportable.</item>
/// </list>
/// </summary>
public static partial class SpeechAccuracy
{
    /// <summary>A group needs at least this many distinct students before its rates are shown (small numbers mislead and can identify).</summary>
    public const int MinStudentsPerGroup = 5;

    /// <summary>Observed-disfluency bands (fillers and repetitions per 100 words).</summary>
    public const double SomeDisfluencyPer100 = 2, FrequentDisfluencyPer100 = 6;

    public const string FluentBand = "Few fillers or repetitions", SomeBand = "Some fillers or repetitions", FrequentBand = "Frequent fillers or repetitions";

    // ------------------------------------------------------------------ low-confidence segments

    /// <summary>
    /// Runs of consecutive words below <paramref name="threshold"/>. A run made only of one- and two-letter
    /// words is dropped (the recognizer is often unsure of "a" or "of" without the meaning being at stake).
    /// </summary>
    public static IReadOnlyList<UncertainSegment> UncertainSegments(IReadOnlyList<TimedWord>? words, double threshold = ResponseVerifier.LowSpeechConfidenceThreshold)
    {
        var segments = new List<UncertainSegment>();
        if (words is null || words.Count == 0) return segments;
        var run = new List<TimedWord>();
        void Flush()
        {
            if (run.Count > 0 && run.Any(w => w.Text.Trim().Length > 2))
                segments.Add(new UncertainSegment(string.Join(" ", run.Select(w => w.Text.Trim())), run[0].Start, run[^1].End, run.Min(w => w.Confidence)));
            run.Clear();
        }
        foreach (var w in words)
        {
            if (string.IsNullOrWhiteSpace(w.Text)) continue;
            if (w.Confidence < threshold) run.Add(w);
            else Flush();
        }
        Flush();
        return segments;
    }

    // ------------------------------------------------------------------ word error rate

    /// <summary>Lowercased words without punctuation, as word error rate is conventionally scored.</summary>
    public static IReadOnlyList<string> NormalizedWords(string? text) =>
        WordRx().Matches((text ?? "").ToLowerInvariant()).Select(m => m.Value.Trim('\'')).Where(w => w.Length > 0).ToList();

    /// <summary>Substitutions, deletions and insertions turning <paramref name="reference"/> into <paramref name="hypothesis"/>.</summary>
    public static WordErrorCount WordErrors(string? reference, string? hypothesis) => Errors(NormalizedWords(reference), NormalizedWords(hypothesis));

    /// <summary>True when <paramref name="text"/> has Hangul (Korean syllables or jamo).</summary>
    public static bool IsKorean(string? text) => (text ?? "").Any(c => c is >= '\uAC00' and <= '\uD7A3' or >= '\u3131' and <= '\u318E' or >= '\u1100' and <= '\u11FF');

    /// <summary>
    /// The units an error rate is scored over (GAD-T554, research note R6.1): words, as word error rate is conventionally scored, except
    /// for a Korean reference, which is scored by character (letters and digits, spaces and punctuation left out): Korean spacing is
    /// inconsistent, so word error rate over-counts errors there and character error rate is the measure used for it.
    /// </summary>
    public static IReadOnlyList<string> ErrorUnits(string? text, bool korean) =>
        korean ? (text ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).Select(c => c.ToString()).ToList() : NormalizedWords(text);

    /// <summary>
    /// <see cref="WordErrors"/>, or the character errors for a Korean <paramref name="reference"/> (<see cref="ErrorUnits"/>); the count's
    /// <see cref="WordErrorCount.ReferenceWords"/> is then reference characters. English text scores exactly as <see cref="WordErrors"/>.
    /// </summary>
    public static WordErrorCount UnitErrors(string? reference, string? hypothesis)
    {
        var korean = IsKorean(reference);
        return Errors(ErrorUnits(reference, korean), ErrorUnits(hypothesis, korean));
    }

    private static WordErrorCount Errors(IReadOnlyList<string> r, IReadOnlyList<string> h)
    {
        // cost[i, j] is the fewest edits aligning r[..i] with h[..j]; op[i, j] the last edit, for the backtrace.
        var cost = new int[r.Count + 1, h.Count + 1];
        var op = new byte[r.Count + 1, h.Count + 1];   // 0 match, 1 substitution, 2 deletion, 3 insertion
        for (var i = 1; i <= r.Count; i++) { cost[i, 0] = i; op[i, 0] = 2; }
        for (var j = 1; j <= h.Count; j++) { cost[0, j] = j; op[0, j] = 3; }
        for (var i = 1; i <= r.Count; i++)
            for (var j = 1; j <= h.Count; j++)
            {
                var same = string.Equals(r[i - 1], h[j - 1], StringComparison.Ordinal);
                var diag = cost[i - 1, j - 1] + (same ? 0 : 1);
                var del = cost[i - 1, j] + 1;
                var ins = cost[i, j - 1] + 1;
                if (diag <= del && diag <= ins) { cost[i, j] = diag; op[i, j] = (byte)(same ? 0 : 1); }
                else if (del <= ins) { cost[i, j] = del; op[i, j] = 2; }
                else { cost[i, j] = ins; op[i, j] = 3; }
            }
        int s = 0, d = 0, n = 0;
        for (int i = r.Count, j = h.Count; i > 0 || j > 0;)
        {
            switch (op[i, j])
            {
                case 0: i--; j--; break;
                case 1: s++; i--; j--; break;
                case 2: d++; i--; break;
                default: n++; j--; break;
            }
        }
        return new WordErrorCount(s, d, n, r.Count);
    }

    // ------------------------------------------------------------------ disfluency (proxy grouping)

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal) { "um", "umm", "uh", "uhh", "er", "erm", "ah", "hmm", "mm" };

    /// <summary>
    /// Fillers ("um", "uh") plus immediately repeated words and part-word starts ("c- cell") per 100 words. A
    /// proxy only: recognizers often drop the very repetitions a stutter produces (Mujtaba2024), so a low rate
    /// does not show fluent speech; it groups answers without asking anyone about their speech.
    /// </summary>
    public static double DisfluencyRate(string? text)
    {
        var raw = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (raw.Length == 0) return 0;
        var markers = 0;
        string? previous = null;
        foreach (var token in raw)
        {
            var partWord = token.EndsWith('-') && token.Length <= 4;
            var w = token.ToLowerInvariant().Trim('.', ',', ';', ':', '!', '?', '"', '\'', '…', '-', '—', '(', ')');
            if (partWord || Fillers.Contains(w) || (w.Length > 0 && w == previous)) markers++;
            if (w.Length > 0) previous = w;
        }
        return 100.0 * markers / raw.Length;
    }

    public static string DisfluencyBand(double per100Words) =>
        per100Words >= FrequentDisfluencyPer100 ? FrequentBand : per100Words >= SomeDisfluencyPer100 ? SomeBand : FluentBand;

    // ------------------------------------------------------------------ group summary

    /// <summary>
    /// One group's figures. Word error rate is measured only on answers with a reference: a human listener's transcript
    /// (GAD-T426), or, for answers before GAD-T405, the transcript the student checked (confirmed or corrected). Because the exam asks for a check mostly when
    /// the recognizer was unsure, the rate describes the answers most at risk; compare groups with each other, not
    /// with published benchmarks. Confidence figures cover every voice answer that kept its word timings.
    /// </summary>
    public static SpeechGroupStats Summarize(string group, IEnumerable<SpeechSample> samples)
    {
        var list = samples.ToList();
        var students = list.Select(s => s.StudentKey).Distinct(StringComparer.Ordinal).Count();
        int refWords = 0, errors = 0;
        foreach (var s in list.Where(s => s.TranscriptChecked))
        {
            var e = UnitErrors(s.ReferenceText, s.RecognizedText);   // characters for Korean answers (GAD-T554)
            refWords += e.ReferenceWords;
            errors += e.Errors;
        }
        var timed = list.Where(s => s.Words > 0).ToList();
        var words = timed.Sum(s => s.Words);
        var rated = timed.Where(s => s.MeanConfidence is not null).ToList();
        var ratedWords = rated.Sum(s => s.Words);
        var meanConfidence = ratedWords == 0 ? (double?)null : rated.Sum(s => s.MeanConfidence!.Value * s.Words) / ratedWords;
        return new SpeechGroupStats(group, students, list.Count, list.Count(s => s.TranscriptChecked), list.Count(s => s.Corrected),
            refWords, errors, refWords == 0 ? null : (double)errors / refWords, meanConfidence,
            words == 0 ? null : (double)timed.Sum(s => s.LowConfidenceWords) / words, students >= MinStudentsPerGroup,
            list.Count(s => s.HumanReference));
    }

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex WordRx();
}
