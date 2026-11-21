using System.Text;
using System.Text.RegularExpressions;
using Gadfly.Socrates;

namespace Gadfly.Sources.Grading;

/// <summary>One probe's outcome: the same answers, changed only in form, graded again.</summary>
/// <param name="Probe"><see cref="PanelBiasCheck.Padded"/> or <see cref="PanelBiasCheck.Disfluent"/>.</param>
/// <param name="Shift">Probe score minus baseline score, in points of the objective's maximum.</param>
/// <param name="Biased">Whether the shift went past the tolerance in the direction the bias predicts.</param>
public sealed record BiasProbeResult(string Probe, decimal BaselineScore, decimal ProbeScore, decimal Shift, bool Biased, string Finding);

/// <summary>A panel's bias check on one objective.</summary>
public sealed record PanelBiasReport(string ObjectiveKey, decimal MaxScore, decimal BaselineScore, IReadOnlyList<BiasProbeResult> Probes, bool IsMock)
{
    public bool AnyBias => Probes.Any(p => p.Biased);
}

/// <summary>
/// Padded-answer and fluency-paraphrase checks on the grading panel (GAD-T269; docs/research/05, Zheng2023's
/// "repetitive list" attack and Liang2023's bias against non-native writing). Each probe grades the same
/// objective three ways through <see cref="AiObjectiveGrader"/>: the verified answers as they are, the answers
/// padded with restatement and repetition (no new content), and the answers made less fluent (fillers, dropped
/// articles, no capitals or final punctuation; same content). A panel that scores the padded answers higher, or
/// the less fluent ones lower, by more than the tolerance is showing verbosity or fluency bias.
/// The rewrites are deterministic; only the grading calls use the panel, so a live panel costs three
/// grading calls per objective checked. Run it on demand, never on the live grading path.
/// </summary>
public sealed class PanelBiasCheck(ISocraticVotingClient panel, Random? shuffle = null)
{
    public const string Padded = "padded-answer";
    public const string Disfluent = "fluency-paraphrase";

    /// <summary>The default tolerance: a shift of more than 10% of the objective's maximum counts as bias.</summary>
    public const decimal DefaultTolerancePercent = 10m;

    private static readonly Regex Sentences = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);
    private static readonly Regex Articles = new(@"\b(the|a|an)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<PanelBiasReport> RunAsync(ObjectiveGradingInput input, decimal tolerancePercent = DefaultTolerancePercent, CancellationToken ct = default)
    {
        var grader = new AiObjectiveGrader(panel, shuffle);
        var max = input.Objective.MaxScore;
        var tolerance = max * Math.Max(0m, tolerancePercent) / 100m;

        var baseline = await grader.GradeAsync(input, ct);
        var padded = await grader.GradeAsync(Rewrite(input, Pad), ct);
        var disfluent = await grader.GradeAsync(Rewrite(input, Disfluency), ct);

        var paddedShift = padded.Score - baseline.Score;
        var disfluentShift = disfluent.Score - baseline.Score;
        var probes = new List<BiasProbeResult>
        {
            new(Padded, baseline.Score, padded.Score, paddedShift, paddedShift > tolerance,
                paddedShift > tolerance ? $"Verbosity bias: padding the same answers with restatement raised the score by {paddedShift:0.##} of {max:0.##}."
                : paddedShift < -tolerance ? $"Padding lowered the score by {-paddedShift:0.##} of {max:0.##}: not verbosity bias, but the panel is unstable on identical content."
                : "Padding with restatement did not change the score beyond the tolerance."),
            new(Disfluent, baseline.Score, disfluent.Score, disfluentShift, disfluentShift < -tolerance,
                disfluentShift < -tolerance ? $"Fluency bias: the same answers, less fluent, scored {-disfluentShift:0.##} of {max:0.##} lower."
                : disfluentShift > tolerance ? $"Less fluent answers scored {disfluentShift:0.##} of {max:0.##} higher: not fluency bias, but the panel is unstable on identical content."
                : "Less fluent wording did not change the score beyond the tolerance."),
        };
        return new PanelBiasReport(input.Objective.Key, max, baseline.Score, probes, baseline.IsMock || padded.IsMock || disfluent.IsMock);
    }

    private static ObjectiveGradingInput Rewrite(ObjectiveGradingInput input, Func<string, string> change) =>
        input with { Evidence = input.Evidence.Select(e => string.IsNullOrWhiteSpace(e.VerifiedText) ? e : e with { VerifiedText = change(e.VerifiedText) }).ToList() };

    /// <summary>The answer, restated and repeated, with no new content.</summary>
    public static string Pad(string answer)
    {
        var text = answer.Trim();
        if (text.Length == 0) return answer;
        var sentences = Sentences.Split(text).Where(s => s.Length > 0).ToList();
        var sb = new StringBuilder(text);
        sb.Append(" To put it another way, as I said: ").Append(text);
        foreach (var s in sentences) sb.Append(" Again, ").Append(s);
        sb.Append(" So, to restate my answer once more, ").Append(text);
        sb.Append(" That is the key point, and it is a very important one.");
        return sb.ToString();
    }

    /// <summary>The same answer, less fluent: fillers, dropped articles, no capitals and no final punctuation.</summary>
    public static string Disfluency(string answer)
    {
        var text = answer.Trim();
        if (text.Length == 0) return answer;
        text = Articles.Replace(text, "");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder("um, ");
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0) sb.Append(i % 6 == 0 ? " uh, " : " ");
            sb.Append(words[i]);
        }
        return sb.ToString().ToLowerInvariant().TrimEnd('.', '!', '?');
    }
}
