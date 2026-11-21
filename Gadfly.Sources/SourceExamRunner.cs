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
/// One Socratic exam over source material as a resumable, step-driven state machine:
/// <see cref="NextQuestionAsync"/> plans the move and words the question (or ends the session);
/// <see cref="SubmitAnswerAsync"/> records the answer, judges it (or records honest aporia),
/// updates mastery and extends the claim ledger. The server drives it one step per student
/// message and persists <see cref="Session"/> and <see cref="Ledger"/> after each step, so a
/// session resumes exactly where it stopped (GAD-T44); the CLI drives it in a loop (GAD-T38).
/// Not thread-safe: callers serialize steps per session.
/// </summary>
public sealed class SourceExamEngine
{
    public const string AporiaWord = "aporia";

    private readonly ISocraticVotingClient _panel;
    private readonly SourceMaterialSubject _subject;
    private readonly TimeProvider _clock;
    private readonly QuestionPlanner _planner;
    private readonly QuestionGenerator _generator;
    private readonly PanelAnswerJudge _judge;
    private readonly ClaimAnalyzer _claims;
    private readonly TextStyleAssessor _style;

    private SourceExamEngine(ISocraticVotingClient panel, SourceMaterialSubject subject, SocraticSession session, ClaimLedger ledger,
        IReadOnlyList<PlannerConcept> concepts, TimeProvider clock)
    {
        _panel = panel;
        _subject = subject;
        _clock = clock;
        Session = session;
        Ledger = ledger;
        _planner = new QuestionPlanner(concepts);
        _generator = new QuestionGenerator(panel);
        _judge = new PanelAnswerJudge(panel);
        _claims = new ClaimAnalyzer(panel, ExamPrompts.Set);
        _style = new TextStyleAssessor(panel);
    }

    /// <summary>Whether answers get the answer-text integrity assessment (GAD-T59). On by default.</summary>
    public bool AssessTextStyle { get; set; } = true;

    public SocraticSession Session { get; }
    public ClaimLedger Ledger { get; }
    public SourceMaterialSubject Subject => _subject;

    /// <summary>Receives diagnostics (why each move, how each question was produced); null = silent.</summary>
    public IProgress<string>? Diagnostics { get; set; }

    /// <summary>Starts a new session.</summary>
    public static async Task<SourceExamEngine> StartAsync(ISocraticVotingClient panel, SourceMaterialSubject subject, SessionPolicy policy,
        TimeProvider? clock = null, Guid? sessionId = null, CancellationToken ct = default)
    {
        var c = clock ?? TimeProvider.System;
        var concepts = await subject.GetConceptsAsync(ct);
        var session = new SocraticSession(subject.Id, policy, subject.PromptSet, c.GetUtcNow(), sessionId);
        return new SourceExamEngine(panel, subject, session, new ClaimLedger(), concepts, c);
    }

    /// <summary>Resumes a persisted session. The prompt set must be the one it started with.</summary>
    public static async Task<SourceExamEngine> ResumeAsync(ISocraticVotingClient panel, SourceMaterialSubject subject, SocraticSession session,
        ClaimLedger ledger, TimeProvider? clock = null, CancellationToken ct = default)
    {
        if (session.PromptSetSha256 != subject.PromptSet.Sha256)
            throw new InvalidOperationException(
                $"Session {session.Id} ran prompt set {session.PromptSetSha256[..12]}, but this build has {subject.PromptSet.Sha256[..12]}; it cannot resume on different wording.");
        var concepts = await subject.GetConceptsAsync(ct);
        return new SourceExamEngine(panel, subject, session, ledger, concepts, clock ?? TimeProvider.System);
    }

    /// <summary>
    /// The question waiting for an answer: the open turn if there is one (a resumed session asks it
    /// again), otherwise a newly planned and worded one. Null when the session has ended.
    /// </summary>
    public async Task<Turn?> NextQuestionAsync(CancellationToken ct = default)
    {
        if (Session.IsEnded) return null;
        if (Session.OpenTurn is { } open) return open;
        if (Session.AwaitingJudgment is not null)
            throw new InvalidOperationException("The last answer has not been judged; call SubmitAnswerAsync again to finish it.");

        var decision = _planner.Next(Session, _clock.GetUtcNow(), Ledger.ForPlanner());
        if (decision.EndsSession)
        {
            Session.End(decision.End, _clock.GetUtcNow());
            return null;
        }

        var concept = _subject.Concept(decision.ConceptId!);
        var rubric = _subject.Rubric(concept.Key);
        var related = decision.RelatedConceptId is null ? null : _subject.Concept(decision.RelatedConceptId).Title;
        var lastAnswer = Session.Turns.LastOrDefault(t => t.ConceptId == concept.Key)?.Answer?.Text;
        var grounding = await _subject.GetGroundingAsync(concept.Key, lastAnswer, ct);

        var question = await _generator.GenerateAsync(new QuestionRequest
        {
            Session = Session,
            Decision = decision,
            ConceptTitle = concept.Title,
            RelatedConceptTitle = related,
            Grounding = grounding,
            Ledger = Ledger,
            RubricForGuard = rubric.Describe(),
        }, ct);
        Diagnostics?.Report($"[{decision.Move} on {concept.Key}: {decision.Reason} Question {_generator.LastOutcome}.]");
        return Session.Ask(concept.Key, decision.Move, question, _clock.GetUtcNow(), decision.TargetClaimIds, decision.Reason);
    }

    /// <summary>
    /// Records the answer to the open turn (unless it already has one, e.g. after a crash between
    /// recording and judging), judges it, updates mastery and the claim ledger. Returns the judged turn.
    /// </summary>
    public async Task<Turn> SubmitAnswerAsync(Answer? answer, CancellationToken ct = default)
    {
        Turn turn;
        if (Session.OpenTurn is not null)
        {
            ArgumentNullException.ThrowIfNull(answer);
            turn = Session.RecordAnswer(answer);
        }
        else
        {
            turn = Session.AwaitingJudgment ?? throw new InvalidOperationException("There is no question waiting for an answer.");
        }

        var concept = _subject.Concept(turn.ConceptId);
        var rubric = _subject.Rubric(concept.Key);
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
            var passages = await _subject.PassagesAsync(concept.Key, turn.Answer!.Text, ct);
            judgment = await _judge.JudgeAsync(new JudgeInput
            {
                Turn = turn,
                Concept = concept,
                Rubric = rubric,
                Passages = passages,
                EarlierClaims = Ledger.Describe(concept.Key),
            }, rubric.Version, ct);
            if (AssessTextStyle && await _style.AssessAsync(Session, turn, ct) is { } style)
                judgment = judgment with { TextStyle = style };
        }

        var prior = Session.Mastery.TryGetValue(concept.Key, out var m) ? m.Estimate : MasteryModel.Prior;
        var judgedBefore = Session.Turns.Count(t => t.ConceptId == concept.Key && t.Judgment is not null);
        var estimate = MasteryModel.Update(prior, judgment.OverallScore,
            MasteryModel.QuestionDifficulty(turn.Move, Session.Policy.Difficulty), judgedBefore);
        turn = Session.RecordJudgment(judgment, estimate);

        if (judgment.OverallScore > 0)
            await _claims.AnalyzeAsync(Ledger, turn, concept.Title, ct);
        return turn;
    }

    /// <summary>Ends the session early (the student left, the host stopped it).</summary>
    public void End(EndReason reason)
    {
        if (!Session.IsEnded) Session.End(reason, _clock.GetUtcNow());
    }

    /// <summary>The report for the session so far (normally called once it has ended).</summary>
    public SessionReport Report() =>
        SessionReport.From(Session, _subject.Map.Concepts.ToDictionary(c => c.Key, c => c.Title, StringComparer.Ordinal));

    private async Task<bool> IsAporiaAsync(Turn turn, CancellationToken ct)
    {
        var text = turn.Answer!.Text.Trim();
        if (text.Equals(AporiaWord, StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Length == 0) return true;
        if (text.Length > 200) return false; // a long answer is an attempt, not a shrug
        var prompt = ExamPrompts.Fill(ExamPrompts.Set[ExamPrompts.AporiaKey], new Dictionary<string, string?>
        {
            ["question"] = turn.Question,
            ["answer"] = text,
        });
        var d = await _panel.DecideAsync(prompt, ["APORIA", "ANSWER"], "", ct);
        return string.Equals(d.Choice, "APORIA", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Drives a <see cref="SourceExamEngine"/> in a loop against an <see cref="IExamConversation"/> (the CLI).</summary>
public sealed class SourceExamRunner(ISocraticVotingClient panel, SourceMaterialSubject subject, TimeProvider? clock = null)
{
    /// <summary>Receives diagnostics (why each move, how each question was produced); null = silent.</summary>
    public IProgress<string>? Diagnostics { get; init; }

    public async Task<(SocraticSession Session, SessionReport Report)> RunAsync(SessionPolicy policy, IExamConversation student,
        CancellationToken ct = default)
    {
        var engine = await SourceExamEngine.StartAsync(panel, subject, policy, clock, ct: ct);
        engine.Diagnostics = Diagnostics;
        while (await engine.NextQuestionAsync(ct) is { } turn)
        {
            await student.AskAsync(turn, ct);
            var answer = await student.ListenAsync(turn, ct);
            if (answer is null)
            {
                engine.End(EndReason.Abandoned);
                break;
            }
            var judged = await engine.SubmitAnswerAsync(answer, ct);
            await student.OnJudgedAsync(judged, ct);
        }
        return (engine.Session, engine.Report());
    }
}
