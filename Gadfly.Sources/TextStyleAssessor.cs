using Gadfly.Engine;
using Gadfly.Socrates;

namespace Gadfly.Sources;

/// <summary>
/// Answer-text integrity (GAD-T59): how much an answer reads like written or AI-generated text read
/// aloud, compared with the same student's other answers. Weak evidence by design: it needs a
/// baseline (at least <see cref="MinOtherAnswers"/> earlier answers), every provider's estimate is
/// averaged, and the result is only ever shown to a human reviewer, never used to score or fail.
/// <para>
/// GAD-T284: this is a detector of machine-style text in all but name, and the research on such detectors is strongly
/// against them (docs/EVIDENCE_MAP.md mechanic 9). It is kept only as reviewer-only context: labelled an unvalidated
/// estimate, hidden from a blind human grader until their grade is submitted, never an integrity verdict and never part of
/// the score. <see cref="Measure"/> (and <c>gadfly textstyle-rate</c>) counts how often it reaches
/// <see cref="ReviewerThreshold"/>, so its false-flag rate on honest students can be measured once pilot data exists.
/// </para>
/// </summary>
public sealed class TextStyleAssessor(ISocraticVotingClient panel, PromptSet? prompts = null)
{
    private readonly PromptSet _prompts = prompts ?? ExamPrompts.Set;

    /// <summary>
    /// The mean estimate at which the answer is also recorded as a low-severity, reviewer-only integrity event
    /// ("text-reads-as-written"). Unvalidated: its false-flag rate is unknown until <see cref="Measure"/> is run on pilot data.
    /// </summary>
    public const double ReviewerThreshold = 0.7;

    /// <summary>Earlier answers needed before an assessment means anything.</summary>
    public int MinOtherAnswers { get; init; } = 2;

    /// <summary>Answers shorter than this are too short to judge style.</summary>
    public int MinAnswerChars { get; init; } = 120;

    public async Task<TextStyleAssessment?> AssessAsync(SocraticSession session, Turn turn, CancellationToken ct = default)
    {
        var text = turn.Answer?.Text ?? "";
        if (text.Length < MinAnswerChars) return null;
        var others = session.Turns.Where(t => t.Index != turn.Index && t.Answer is { } a && a.Text.Length >= 20)
            .Select(t => t.Answer!.Text).TakeLast(6).ToList();
        if (others.Count < MinOtherAnswers) return null;

        var prompt = ExamPrompts.Fill(_prompts[ExamPrompts.TextStyleKey], new Dictionary<string, string?>
        {
            ["other_answers"] = string.Join("\n", others.Select(o => "- " + ExamPrompts.Student(o))),
            ["answer"] = ExamPrompts.Student(text),
        });
        var replies = await panel.FanOutAsync(PanelJson.JsonOnlySystem, prompt, maxTokens: 300, temperature: 0.1, ct: ct);
        var estimates = new List<(double Score, string Reason)>();
        foreach (var r in replies.Where(r => !r.IsError))
        {
            using var doc = PanelJson.ParseObject(r.Text);
            if (doc is null) continue;
            var score = PanelJson.Num(doc.RootElement, "score", double.NaN);
            if (double.IsNaN(score)) continue;
            estimates.Add((Math.Clamp(score, 0, 1), PanelJson.Str(doc.RootElement, "reason")));
        }
        if (estimates.Count == 0) return null;
        var mean = estimates.Average(e => e.Score);
        return new TextStyleAssessment(Math.Round(mean, 3), estimates.OrderBy(e => Math.Abs(e.Score - mean)).First().Reason, estimates.Count);
    }

    /// <summary>
    /// How often the estimate reaches <see cref="ReviewerThreshold"/> over recorded sessions (GAD-T284). It reads the estimates
    /// stored with each judgment and makes no model calls. Over sessions from students known to be answering honestly, every
    /// answer at or above the threshold is a false flag, so <see cref="TextStyleRate.FlagRate"/> is the false-flag rate.
    /// </summary>
    public static TextStyleRate Measure(IEnumerable<SocraticSession> sessions)
    {
        int count = 0, answers = 0, assessed = 0, flagged = 0, middling = 0, sessionsAssessed = 0, sessionsFlagged = 0;
        foreach (var s in sessions)
        {
            count++;
            var withAnswers = s.Turns.Where(t => t.Answer is not null).ToList();
            answers += withAnswers.Count;
            var estimates = withAnswers.Select(t => t.Judgment?.TextStyle).OfType<TextStyleAssessment>().ToList();
            if (estimates.Count == 0) continue;
            sessionsAssessed++;
            assessed += estimates.Count;
            var high = estimates.Count(e => e.Score >= ReviewerThreshold);
            flagged += high;
            middling += estimates.Count(e => e.Score is >= 0.4 and < ReviewerThreshold);
            if (high > 0) sessionsFlagged++;
        }
        return new TextStyleRate(count, answers, assessed, flagged, middling, sessionsAssessed, sessionsFlagged);
    }
}

/// <summary>The text-style estimate's firing counts over a set of sessions (GAD-T284).</summary>
public sealed record TextStyleRate(int Sessions, int Answers, int AssessedAnswers, int Flagged, int Middling,
    int SessionsAssessed, int SessionsFlagged)
{
    /// <summary>Share of assessed answers at or above the reviewer threshold; null when nothing was assessed.</summary>
    public double? FlagRate => AssessedAnswers == 0 ? null : (double)Flagged / AssessedAnswers;
    /// <summary>Share of assessed sessions with at least one answer at or above the threshold.</summary>
    public double? SessionRate => SessionsAssessed == 0 ? null : (double)SessionsFlagged / SessionsAssessed;
}
