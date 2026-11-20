using Gadfly.Engine;
using Gadfly.Socrates;

namespace Gadfly.Sources;

/// <summary>The student side of an exam: the CLI's console, the server's SessionHub.</summary>
public interface IExamConversation
{
    /// <summary>Puts the question to the student (prints it, or has the avatar speak it).</summary>
    Task AskAsync(Turn turn, CancellationToken ct);

    /// <summary>The student's answer, or null when they have left (end of input, disconnect).</summary>
    Task<Answer?> ListenAsync(Turn turn, CancellationToken ct);

    /// <summary>Called after each judgment, for progress display. Students never see scores mid-exam.</summary>
    Task OnJudgedAsync(Turn turn, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Runs one Socratic exam over source material, end to end: plan the move, word the question,
/// ask, listen, judge (or record honest aporia), update mastery, extend the claim ledger, repeat
/// until the planner ends the session; then build the report. The same loop serves the CLI
/// (GAD-T38) and, driven turn by turn, the server's session orchestration (GAD-T44).
/// </summary>
public sealed class SourceExamRunner(ISocraticVotingClient panel, SourceMaterialSubject subject, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly QuestionGenerator _generator = new(panel);
    private readonly PanelAnswerJudge _judge = new(panel);
    private readonly ClaimAnalyzer _claims = new(panel, ExamPrompts.Set);

    public const string AporiaWord = "aporia";

    /// <summary>Receives diagnostics (why each move, how each question was produced); null = silent.</summary>
    public IProgress<string>? Diagnostics { get; init; }

    public async Task<(SocraticSession Session, SessionReport Report)> RunAsync(SessionPolicy policy, IExamConversation student,
        CancellationToken ct = default)
    {
        var concepts = await subject.GetConceptsAsync(ct);
        var planner = new QuestionPlanner(concepts);
        var session = new SocraticSession(subject.Id, policy, subject.PromptSet, _clock.GetUtcNow());
        var ledger = new ClaimLedger();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var decision = planner.Next(session, _clock.GetUtcNow(), ledger.ForPlanner());
            if (decision.EndsSession)
            {
                session.End(decision.End, _clock.GetUtcNow());
                break;
            }

            var concept = subject.Concept(decision.ConceptId!);
            var rubric = subject.Rubric(concept.Key);
            var related = decision.RelatedConceptId is null ? null : subject.Concept(decision.RelatedConceptId).Title;
            var lastAnswer = session.Turns.LastOrDefault(t => t.ConceptId == concept.Key)?.Answer?.Text;
            var grounding = await subject.GetGroundingAsync(concept.Key, lastAnswer, ct);

            var question = await _generator.GenerateAsync(new QuestionRequest
            {
                Session = session,
                Decision = decision,
                ConceptTitle = concept.Title,
                RelatedConceptTitle = related,
                Grounding = grounding,
                Ledger = ledger,
                RubricForGuard = rubric.Describe(),
            }, ct);
            Diagnostics?.Report($"[{decision.Move} on {concept.Key}: {decision.Reason} Question {_generator.LastOutcome}.]");
            var turn = session.Ask(concept.Key, decision.Move, question, _clock.GetUtcNow(), decision.TargetClaimIds);
            await student.AskAsync(turn, ct);

            var answer = await student.ListenAsync(turn, ct);
            if (answer is null)
            {
                session.End(EndReason.Abandoned, _clock.GetUtcNow());
                break;
            }
            turn = session.RecordAnswer(answer);

            AnswerJudgment judgment;
            if (await IsAporiaAsync(turn, ct))
            {
                judgment = new AnswerJudgment
                {
                    Criteria = [],
                    OverallScore = 0,
                    RubricVersion = rubric.Version,
                    Summary = "The student said they do not know (aporia).",
                };
            }
            else
            {
                var passages = await subject.PassagesAsync(concept.Key, answer.Text, ct);
                judgment = await _judge.JudgeAsync(new JudgeInput
                {
                    Turn = turn,
                    Concept = concept,
                    Rubric = rubric,
                    Passages = passages,
                    EarlierClaims = ledger.Describe(concept.Key),
                }, rubric.Version, ct);
            }

            var prior = session.Mastery.TryGetValue(concept.Key, out var m) ? m.Estimate : MasteryModel.Prior;
            var judgedBefore = session.Turns.Count(t => t.ConceptId == concept.Key && t.Judgment is not null);
            var estimate = MasteryModel.Update(prior, judgment.OverallScore,
                MasteryModel.QuestionDifficulty(turn.Move, policy.Difficulty), judgedBefore);
            turn = session.RecordJudgment(judgment, estimate);
            await student.OnJudgedAsync(turn, ct);

            if (judgment.OverallScore > 0)
                await _claims.AnalyzeAsync(ledger, turn, concept.Title, ct);
        }

        var titles = subject.Map.Concepts.ToDictionary(c => c.Key, c => c.Title, StringComparer.Ordinal);
        return (session, SessionReport.From(session, titles));
    }

    private async Task<bool> IsAporiaAsync(Turn turn, CancellationToken ct)
    {
        var text = turn.Answer!.Text.Trim();
        if (text.Equals(AporiaWord, StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Length > 200) return false; // a long answer is an attempt, not a shrug
        var prompt = ExamPrompts.Fill(ExamPrompts.Set[ExamPrompts.AporiaKey], new Dictionary<string, string?>
        {
            ["question"] = turn.Question,
            ["answer"] = text,
        });
        var d = await panel.DecideAsync(prompt, ["APORIA", "ANSWER"], "", ct);
        return string.Equals(d.Choice, "APORIA", StringComparison.OrdinalIgnoreCase);
    }
}
