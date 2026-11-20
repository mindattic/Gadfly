using Gadfly.Engine;
using Gadfly.Socrates;

namespace Gadfly.Sources;

/// <summary>
/// Answer-text integrity (GAD-T59): how much an answer reads like written or AI-generated text read
/// aloud, compared with the same student's other answers. Weak evidence by design: it needs a
/// baseline (at least <see cref="MinOtherAnswers"/> earlier answers), every provider's estimate is
/// averaged, and the result is only ever shown to a human reviewer, never used to score or fail.
/// </summary>
public sealed class TextStyleAssessor(ISocraticVotingClient panel, PromptSet? prompts = null)
{
    private readonly PromptSet _prompts = prompts ?? ExamPrompts.Set;

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
            ["other_answers"] = string.Join("\n", others.Select(o => "- " + o)),
            ["answer"] = text,
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
}
