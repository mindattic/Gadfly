using System.Globalization;
using System.Text.RegularExpressions;

namespace Gadfly.Engine;

/// <summary>
/// Why an answer was not accepted as the student's own understanding (GAD-T242): it closely follows the source material's
/// wording, or it is a long, fluent spoken answer that started only after a long silent wait (a read-out pattern). Recorded
/// on the judgment for a human reviewer; never a cheating verdict. Students quote and memorize honestly, so a recited answer
/// is simply not evidence either way: the examiner asks them to say it differently, apply it to a new case, or explain why.
/// </summary>
public sealed record RecitationFlag
{
    /// <summary>Share (0..1) of the answer's word runs that appear word for word in the source material.</summary>
    public double SourceOverlap { get; init; }
    /// <summary>The longest stretch of the answer, in words, that appears word for word in the source.</summary>
    public int LongestCopiedWords { get; init; }
    /// <summary>True when the answer closely matches the source material's wording.</summary>
    public bool MatchesSource { get; init; }
    /// <summary>True when the answer was long and fluent and started after a long silent wait.</summary>
    public bool ReadOutPattern { get; init; }
    /// <summary>The longest copied stretch, as the student said it (clipped); null when nothing was copied.</summary>
    public string? CopiedText { get; init; }
    /// <summary>What was seen, in plain words for the reviewer and the question prompt.</summary>
    public string Reason { get; init; } = "";
}

/// <summary>
/// Deterministic recitation check on one answer (GAD-T242). It compares the answer's words with the source passages the
/// judge saw, and its delivery (wait, length, fluency) with the student's own earlier answers through <see cref="AnswerDelivery"/>.
/// It never scores and never accuses: a flagged answer is left out of the evidence and the next question presses for
/// understanding in the student's own head.
/// </summary>
public static class RecitationCheck
{
    /// <summary>Word-run length used to compare the answer with the source.</summary>
    public const int RunWords = 6;
    /// <summary>Answers shorter than this are never checked against the source (a quoted term or short definition is normal).</summary>
    public const int MinWordsForSourceMatch = 15;
    /// <summary>This share of the answer's word runs found verbatim in the source is a close match.</summary>
    public const double SourceOverlapCue = 0.6;
    /// <summary>A verbatim stretch this long is a close match on its own.</summary>
    public const int LongestCopiedCue = 20;

    /// <summary>A spoken answer at least this long counts as long for the read-out pattern.</summary>
    public const int ReadOutMinWords = 60;
    /// <summary>A wait at least this long, and at least <see cref="ReadOutDelayFactor"/> times the student's usual, is a long silent wait.</summary>
    public const double ReadOutDelaySeconds = 15, ReadOutDelayFactor = 2;
    /// <summary>Hesitations per 100 words at or below this, with no long pauses, is fluent.</summary>
    public const double FluentHesitationsPer100 = 2;

    /// <summary>
    /// Checks <paramref name="turn"/>'s answer against <paramref name="sourceText"/> and its delivery; null when it reads as the
    /// student's own (or there is no answer).
    /// </summary>
    public static RecitationFlag? Check(SocraticSession session, Turn turn, string? sourceText)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (turn.Answer is not { } answer || string.IsNullOrWhiteSpace(answer.Text)) return null;
        var words = Words(answer.Text);

        var (overlap, longest, start) = Compare(words, Words(sourceText));
        var matches = words.Count >= MinWordsForSourceMatch && (overlap >= SourceOverlapCue || longest >= LongestCopiedCue);

        var readOut = false;
        string? readOutReason = null;
        if (answer.Mode == "voice" && words.Count >= ReadOutMinWords && AnswerDelivery.Read(session, turn) is { DelaySeconds: { } delay } d
            && delay >= ReadOutDelaySeconds && (d.BaselineDelaySeconds is not { } usual || delay >= ReadOutDelayFactor * Math.Max(usual, 1))
            && d.LongPauses == 0 && (d.HesitationsPer100 ?? 0) <= FluentHesitationsPer100)
        {
            readOut = true;
            readOutReason = $"a long ({words.Count} words), fluent answer ({Num(d.HesitationsPer100 ?? 0)} hesitations per 100 words, no long pauses) "
                + $"that started after a {Num(delay)}s silent wait" + (d.BaselineDelaySeconds is { } b ? $" (usually {Num(b)}s)" : "");
        }

        if (!matches && !readOut) return null;
        var reasons = new List<string>();
        if (matches)
            reasons.Add($"closely follows the source material's wording ({Num(overlap * 100)}% of its word runs verbatim, longest copied stretch {longest} words)");
        if (readOutReason is not null) reasons.Add(readOutReason);
        return new RecitationFlag
        {
            SourceOverlap = Math.Round(overlap, 2), LongestCopiedWords = longest, MatchesSource = matches, ReadOutPattern = readOut,
            CopiedText = longest >= RunWords ? Clip(string.Join(' ', words.Skip(start).Take(longest)), 240) : null,
            Reason = string.Join("; ", reasons),
        };
    }

    /// <summary>The note added to the judgment summary, for the reviewer.</summary>
    public static string ReviewNote(RecitationFlag flag) =>
        $"Recitation check: not accepted as the student's own understanding: {flag.Reason}. The examiner asked for it differently "
        + "(own words, a new case, or why). Left out of the evidence; this is a prompt for review, never a cheating verdict.";

    /// <summary>The question prompt's guidance after a recited answer: ask for understanding the student holds themselves.</summary>
    public const string Guidance =
        "The last answer was not accepted as the student's own understanding: it reads as recited rather than reasoned. Do not move on "
        + "and do not accept it: ask the student to say it differently in their own words, to apply it to a new case they have not "
        + "seen, or to explain why it is so. Never say or hint that the answer sounded read or copied, and never quote the source.";

    /// <summary>
    /// Share of <paramref name="answer"/>'s word runs found in <paramref name="source"/>, the longest verbatim stretch in words,
    /// and where that stretch starts in the answer.
    /// </summary>
    internal static (double Overlap, int Longest, int Start) Compare(IReadOnlyList<string> answer, IReadOnlyList<string> source)
    {
        if (answer.Count < RunWords || source.Count < RunWords) return (0, 0, 0);
        var runs = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + RunWords <= source.Count; i++) runs.Add(Run(source, i));
        int total = answer.Count - RunWords + 1, found = 0, longest = 0, start = 0, current = 0, currentStart = 0;
        for (var i = 0; i < total; i++)
        {
            if (runs.Contains(Run(answer, i)))
            {
                found++;
                if (current == 0) currentStart = i;
                current++;
                // Consecutive matching runs overlap: n runs in a row cover n + RunWords - 1 words.
                if (current + RunWords - 1 > longest) { longest = current + RunWords - 1; start = currentStart; }
            }
            else current = 0;
        }
        return ((double)found / total, longest, start);
    }

    /// <summary>Lower-cased words with punctuation dropped, so wording is compared, not formatting.</summary>
    internal static IReadOnlyList<string> Words(string? text) => string.IsNullOrWhiteSpace(text) ? []
        : Word.Matches(text.ToLowerInvariant()).Select(m => m.Value.Replace("'", "").Replace("’", "")).Where(w => w.Length > 0).ToList();

    private static string Run(IReadOnlyList<string> words, int at) => string.Join(' ', Enumerable.Range(at, RunWords).Select(i => words[i]));

    private static string Num(double v) => v.ToString(v >= 10 ? "0" : "0.#", CultureInfo.InvariantCulture);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    private static readonly Regex Word = new(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
