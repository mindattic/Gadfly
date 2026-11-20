using Gadfly.Socrates;
using MindAttic.Legion;

namespace Gadfly.Engine;

/// <summary>What the generator needs to word one question.</summary>
public sealed record QuestionRequest
{
    public required SocraticSession Session { get; init; }
    public required PlanDecision Decision { get; init; }
    public required string ConceptTitle { get; init; }
    public string? RelatedConceptTitle { get; init; }
    public required GroundingContext Grounding { get; init; }
    public required ClaimLedger Ledger { get; init; }
    /// <summary>The rubric's expected points, for the leak guard. Empty skips the guard.</summary>
    public string RubricForGuard { get; init; } = "";
}

/// <summary>
/// Words each question through the Legion panel (GAD-T35): several providers draft a question for
/// the planned move and a judge fuses the sharpest one. A second panel vote guards against
/// questions that give away rubric points; a leaking draft is redrafted once, then replaced by a
/// plain, safe question for the move.
/// </summary>
public sealed class QuestionGenerator(ISocraticVotingClient panel, PromptSet? prompts = null)
{
    private readonly PromptSet _prompts = prompts ?? ExamPrompts.Set;

    public const string Leaks = "LEAKS";
    public const string Safe = "SAFE";

    /// <summary>Turns of dialogue included in the prompt.</summary>
    public int DialogueTurns { get; init; } = 8;

    public async Task<string> GenerateAsync(QuestionRequest r, CancellationToken ct = default)
    {
        var dialogue = Dialogue(r.Session);
        var targets = string.Join("\n", r.Decision.TargetClaimIds.Select(id => r.Ledger.Find(id))
            .Where(c => c is not null).Select(c => $"- (turn {c!.TurnIndex + 1}) \"{c.Quote}\""));
        var prompt = ExamPrompts.Fill(_prompts[ExamPrompts.QuestionKey], new Dictionary<string, string?>
        {
            ["move"] = r.Decision.Move.ToString(),
            ["concept"] = r.ConceptTitle,
            ["related_concept"] = r.RelatedConceptTitle ?? "(none)",
            ["difficulty"] = r.Session.Policy.Difficulty.ToString(),
            ["target_claims"] = targets.Length == 0 ? "(none)" : targets,
            ["grounding"] = r.Grounding.Text,
            ["dialogue"] = dialogue.Length == 0 ? "(this is the first question)" : dialogue,
        });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var ask = attempt == 0 ? prompt : prompt + "\n\nYour previous draft revealed expected answer points. Ask without stating or hinting at any of them.";
            var result = await panel.AskAsync(ask, "", Quorum.Plurality, ct);
            var question = Clean(!string.IsNullOrWhiteSpace(result.Consensus) ? result.Consensus : result.NarrativeSummary);
            if (question.Length == 0) continue;
            if (string.IsNullOrWhiteSpace(r.RubricForGuard) || !await LeaksAsync(question, r.RubricForGuard, dialogue, ct))
                return question;
        }
        return Fallback(r.Decision.Move, r.ConceptTitle, r.RelatedConceptTitle);
    }

    private async Task<bool> LeaksAsync(string question, string rubric, string dialogue, CancellationToken ct)
    {
        var guard = ExamPrompts.Fill(_prompts[ExamPrompts.QuestionGuardKey], new Dictionary<string, string?>
        {
            ["rubric"] = rubric,
            ["dialogue"] = dialogue.Length == 0 ? "(none)" : dialogue,
            ["question"] = question,
        });
        var d = await panel.DecideAsync(guard, [Leaks, Safe], "", ct);
        return string.Equals(d.Choice, Leaks, StringComparison.OrdinalIgnoreCase);
    }

    private string Dialogue(SocraticSession s) =>
        string.Join("\n", s.Turns.TakeLast(DialogueTurns).SelectMany(t => t.Answer is null
            ? new[] { $"Examiner: {t.Question}" }
            : new[] { $"Examiner: {t.Question}", $"Student: {t.Answer.Text}" }));

    /// <summary>Strips quotes, labels and extra lines a model may wrap the question in.</summary>
    internal static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var line = raw.Trim().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        foreach (var label in new[] { "Question:", "Examiner:", "Q:" })
            if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase)) line = line[label.Length..].Trim();
        return line.Trim('"', '“', '”', '*', ' ');
    }

    internal static string Fallback(MoveKind move, string concept, string? related) => move switch
    {
        MoveKind.Clarify => $"Could you say more precisely what you mean, in your own words, when you talk about {concept}?",
        MoveKind.Probe => "Why do you think that is true? Walk me through your reasoning.",
        MoveKind.Counterexample => $"Can you think of a case where your explanation of {concept} would not hold? What would happen then?",
        MoveKind.Contradiction => "Earlier you said something that seems to conflict with what you just said. How do both fit together?",
        MoveKind.Apply => $"Imagine a situation you haven't seen before where {concept} matters. What would you expect to happen, and why?",
        MoveKind.Synthesize => $"How does {concept} relate to {related ?? "the other ideas we have discussed"}?",
        _ => $"In your own words, what is {concept}?",
    };
}
