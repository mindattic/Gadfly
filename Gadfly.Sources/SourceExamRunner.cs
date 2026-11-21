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
/// <see cref="SubmitAnswerAsync"/> records the answer, judges it (or records honest aporia), analyses it
/// (claims, misconceptions, uncertainties, evidence per objective), updates mastery and extends the claim
/// ledger. The server drives it one step per student message and persists <see cref="Session"/> and
/// <see cref="Ledger"/> after each step, so a session resumes exactly where it stopped (GAD-T44); the CLI
/// drives it in a loop (GAD-T38). Examination is objective-driven (GAD-T89..T93): see <see cref="QuestionPlanner"/>
/// and <see cref="ObjectiveAssessor"/>. Not thread-safe: callers serialize steps per session.
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
    private readonly ResponseAnalyzer _analyzer;
    private readonly PromptSet _prompts;

    private SourceExamEngine(ISocraticVotingClient panel, SourceMaterialSubject subject, SocraticSession session, ClaimLedger ledger,
        IReadOnlyList<PlannerConcept> concepts, TimeProvider clock)
    {
        _panel = panel;
        _subject = subject;
        _clock = clock;
        _prompts = subject.PromptSet;
        Session = session;
        Ledger = ledger;
        _planner = new QuestionPlanner(concepts);
        _generator = new QuestionGenerator(panel, _prompts);
        _judge = new PanelAnswerJudge(panel, _prompts);
        _claims = new ClaimAnalyzer(panel, _prompts);
        _style = new TextStyleAssessor(panel, _prompts);
        _analyzer = new ResponseAnalyzer(panel, _prompts);
        Objectives = _planner.ObjectivesFor(session);
    }

    /// <summary>Whether answers get the answer-text integrity assessment (GAD-T59). On by default.</summary>
    public bool AssessTextStyle { get; set; } = true;

    /// <summary>Whether answers get the panel response analysis (GAD-T91). On by default; off only for cost-limited runs.</summary>
    public bool AnalyzeResponses { get; set; } = true;

    public SocraticSession Session { get; }
    public ClaimLedger Ledger { get; }
    public SourceMaterialSubject Subject => _subject;

    /// <summary>The learning objectives this session examines (implicit per-concept ones when the exam defines none).</summary>
    public IReadOnlyList<ExamObjective> Objectives { get; }

    /// <summary>What the leak guard decided about the most recently worded question.</summary>
    public LeakGuardResult LastGuard => _generator.LastGuard;

    /// <summary>Receives diagnostics (why each move, how each question was produced); null = silent.</summary>
    public IProgress<string>? Diagnostics { get; set; }

    /// <summary>
    /// Starts a new session. Objectives in <paramref name="policy"/> without concept keys are grounded in the
    /// concept whose title matches best; an exam without objectives gets one implicit objective per concept.
    /// </summary>
    public static async Task<SourceExamEngine> StartAsync(ISocraticVotingClient panel, SourceMaterialSubject subject, SessionPolicy policy,
        TimeProvider? clock = null, Guid? sessionId = null, CancellationToken ct = default)
    {
        var c = clock ?? TimeProvider.System;
        var concepts = await subject.GetConceptsAsync(ct);
        policy = WithResolvedObjectives(policy, subject);
        var session = new SocraticSession(subject.Id, policy, subject.PromptSet, c.GetUtcNow(), sessionId);
        return new SourceExamEngine(panel, subject, session, new ClaimLedger(), concepts, c);
    }

    private static string Short(string? hash) => hash is null ? "" : hash.Length <= 12 ? hash : hash[..12];

    /// <summary>Resumes a persisted session. The prompt set must be the one it started with.</summary>
    public static async Task<SourceExamEngine> ResumeAsync(ISocraticVotingClient panel, SourceMaterialSubject subject, SocraticSession session,
        ClaimLedger ledger, TimeProvider? clock = null, CancellationToken ct = default)
    {
        if (session.PromptSetSha256 != subject.PromptSet.Sha256)
            throw new InvalidOperationException(
                $"Session {session.Id} ran prompt set {Short(session.PromptSetSha256)}, but this build has {Short(subject.PromptSet.Sha256)}; it cannot resume on different wording.");
        var concepts = await subject.GetConceptsAsync(ct);
        return new SourceExamEngine(panel, subject, session, ledger, concepts, clock ?? TimeProvider.System);
    }

    /// <summary>
    /// The policy with its objectives made explicit: implicit objectives (titled from the concept map) when it
    /// has none, and every objective grounded in at least one concept of the map.
    /// </summary>
    public static SessionPolicy WithResolvedObjectives(SessionPolicy policy, SourceMaterialSubject subject)
    {
        var map = subject.Map;
        if (policy.Objectives.Count == 0)
        {
            var ids = policy.ConceptIds.Count > 0 ? policy.ConceptIds : map.Concepts.Select(x => x.Key).ToList();
            return policy with { Objectives = ids.Select(id => ExamObjectives.Implicit(id, map.Find(id)?.Title ?? id)).ToList() };
        }
        if (map.Concepts.Count == 0) throw new InvalidOperationException("The source material has no concepts to ground the objectives in.");
        var resolved = policy.Objectives.Select(o =>
        {
            var known = o.ConceptKeys.Where(k => map.Find(k) is not null).ToList();
            if (known.Count > 0) return o with { ConceptKeys = known };
            var text = $"{o.Title} {o.Description}";
            var best = map.Concepts.Select((x, i) => (x.Key, Score: PanelJson.Similarity(text, $"{x.Title} {x.Definition}"), i))
                .OrderByDescending(x => x.Score).ThenBy(x => x.i).First();
            return o with { ConceptKeys = [best.Key] };
        }).ToList();
        var conceptIds = policy.ConceptIds.Count > 0 ? policy.ConceptIds
            : resolved.SelectMany(o => o.ConceptKeys).Distinct(StringComparer.Ordinal).ToList();
        return policy with { Objectives = resolved, ConceptIds = conceptIds };
    }

    /// <summary>Each objective's evidence sufficiency and level right now (GAD-T89).</summary>
    public IReadOnlyList<ObjectiveAssessment> Assessments() => _planner.Assess(Session, Ledger.ForPlanner());

    /// <summary>
    /// Every evidence item so far with its contamination and assistance flags (GAD-T91..T93). The server persists
    /// these as AssessmentEvidence rows (Gadfly.Server.Evidence.EvidenceWriter); re-running is idempotent.
    /// </summary>
    public IReadOnlyList<CollectedEvidence> CollectEvidence() => ObjectiveAssessor.CollectEvidence(Session);

    /// <summary>
    /// Records that turn <paramref name="turnIndex"/>'s question, or a clarification on it, revealed the answer
    /// (GAD-T92). Answers on that objective from that turn on become contaminated and the planner asks a
    /// replacement question. Returns false when the turn was already flagged.
    /// </summary>
    public bool FlagLeak(int turnIndex, LeakSource source, string reason)
    {
        var flagged = Session.FlagLeak(turnIndex, new LeakFlag(source, reason, _clock.GetUtcNow()));
        if (flagged) Diagnostics?.Report($"[Leak flagged on turn {turnIndex + 1} ({source}): {reason}]");
        return flagged;
    }

    /// <summary>
    /// Runs the leak guard on a clarification shown to the student about turn <paramref name="turnIndex"/> (response
    /// verification, Part 4.4). A leaking clarification flags the turn (<see cref="LeakSource.Clarification"/>);
    /// an undecided guard flags it too (<see cref="LeakSource.GuardFailedOpen"/>), since the text was already shown.
    /// Returns the guard's verdict (true leaks, false safe, null undecided).
    /// </summary>
    public async Task<bool?> CheckClarificationAsync(int turnIndex, string clarification, CancellationToken ct = default)
    {
        if (turnIndex < 0 || turnIndex >= Session.Turns.Count) throw new ArgumentOutOfRangeException(nameof(turnIndex));
        var rubric = _subject.Rubric(Session.Turns[turnIndex].ConceptId).Describe();
        if (string.IsNullOrWhiteSpace(rubric)) return false;
        var verdict = await _generator.CheckLeakAsync(clarification, rubric, _generator.Dialogue(Session), ct);
        if (verdict == true) FlagLeak(turnIndex, LeakSource.Clarification, $"The clarification \"{Clip(clarification, 200)}\" revealed expected points.");
        else if (verdict is null) FlagLeak(turnIndex, LeakSource.GuardFailedOpen, "The leak guard could not decide on a clarification already shown to the student.");
        return verdict;
    }

    /// <summary>The objective a turn examines.</summary>
    public ExamObjective ObjectiveOf(Turn turn)
    {
        var key = ExamObjectives.KeyOf(turn);
        return Objectives.FirstOrDefault(o => o.Key == key) ?? ExamObjectives.Implicit(turn.ConceptId, _subject.Concept(turn.ConceptId).Title);
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
            Diagnostics?.Report($"[End: {decision.Reason}]");
            Session.End(decision.End, _clock.GetUtcNow());
            return null;
        }

        var concept = _subject.Concept(decision.ConceptId!);
        var objective = Objectives.FirstOrDefault(o => o.Key == decision.ObjectiveKey);
        var rubric = _subject.Rubric(concept.Key);
        var related = decision.RelatedConceptId is null ? null : _subject.Concept(decision.RelatedConceptId).Title;
        var lastAnswer = Session.Turns.LastOrDefault(t => t.ConceptId == concept.Key)?.Answer?.Text;
        var grounding = await _subject.GetGroundingAsync(concept.Key, lastAnswer, ct);
        var misconception = MoveProfiles.IsAssistance(decision.Move)
            ? Session.Turns.LastOrDefault()?.Analysis?.Misconceptions.FirstOrDefault(m => m.ObjectiveKey == decision.ObjectiveKey)?.Text
            : null;

        var question = await _generator.GenerateAsync(new QuestionRequest
        {
            Session = Session,
            Decision = decision,
            ConceptTitle = concept.Title,
            RelatedConceptTitle = related,
            Grounding = grounding,
            Ledger = Ledger,
            RubricForGuard = rubric.Describe(),
            ObjectiveTitle = objective?.Title,
            Objective = objective,
            Misconception = misconception,
        }, ct);
        // An Apply question's reason carries its transfer label, so reviewers see how its situation differs from the source,
        // and that the transfer measured is sequestered (GAD-T268).
        var reason = decision.Move == MoveKind.Apply
            ? $"{decision.Reason} Transfer item ({TransferLabels.Label(TransferLabels.Next(Session, decision.ObjectiveKey ?? concept.Key))}). {TransferLabels.Caveat}"
            : decision.Reason;
        Diagnostics?.Report($"[{decision.Move} on {decision.ObjectiveKey ?? concept.Key}{(decision.IsReplacement ? " (replacement)" : "")}: {reason} Question {_generator.LastOutcome}.]");
        return Session.Ask(concept.Key, decision.Move, question, _clock.GetUtcNow(), decision.TargetClaimIds, reason,
            decision.ObjectiveKey, decision.IsReplacement);
    }

    /// <summary>
    /// Records the answer to the open turn (unless it already has one, e.g. after a crash between
    /// recording and judging), judges and analyses it, updates mastery and the claim ledger. Returns the judged turn.
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
        var objective = ObjectiveOf(turn);
        AnswerJudgment judgment;
        ResponseAnalysis? analysis = null;
        if (await IsAporiaAsync(turn, ct))
        {
            judgment = new AnswerJudgment
            {
                Criteria = [],
                OverallScore = 0,
                RubricVersion = rubric.Version,
                Summary = "The student said they do not know (aporia).",
            };
            analysis = ResponseAnalyzer.ForAporia(turn, objective.Key);
        }
        else
        {
            var passages = await _subject.PassagesAsync(concept.Key, turn.Answer!.Text, ct);
            var earlier = Ledger.Describe(concept.Key);
            judgment = await _judge.JudgeAsync(new JudgeInput
            {
                Turn = turn,
                Concept = concept,
                Rubric = rubric,
                Passages = passages,
                EarlierClaims = earlier,
            }, rubric.Version, ct);
            if (AssessTextStyle && await _style.AssessAsync(Session, turn, ct) is { } style)
                judgment = judgment with { TextStyle = style };
            // Recitation check (GAD-T242): an answer that closely follows the source's wording, or a long fluent one after a long
            // silent wait, is not accepted as the student's own understanding. It is recorded for review (never a cheating verdict),
            // left out of mastery and evidence, and the next question asks for it differently, applied, or why.
            if (RecitationCheck.Check(Session, turn, string.Join('\n', passages.Select(p => p.Text))) is { } recited)
            {
                judgment = judgment with
                {
                    Recitation = recited,
                    Summary = string.IsNullOrWhiteSpace(judgment.Summary) ? RecitationCheck.ReviewNote(recited)
                        : $"{judgment.Summary} {RecitationCheck.ReviewNote(recited)}",
                };
                Diagnostics?.Report($"[Recitation on turn {turn.Index + 1}: {recited.Reason}. Not accepted; asking for it differently.]");
            }
            if (AnalyzeResponses)
            {
                analysis = await _analyzer.AnalyzeAsync(new AnalysisInput
                {
                    Turn = turn, Objective = objective, Objectives = Objectives, Rubric = rubric, Passages = passages, EarlierClaims = earlier,
                }, ct);
                Diagnostics?.Report($"[Analysis: {analysis.Evidence.Count} evidence, {analysis.Misconceptions.Count} misconception(s), " +
                    $"{analysis.Uncertainties.Count} uncertaint(ies), {analysis.Claims.Count} claim(s); {analysis.Voters} valid voter(s), " +
                    $"{analysis.RejectedVotes} rejected{(analysis.Rejections.Count > 0 ? " (" + string.Join("; ", analysis.Rejections) + ")" : "")}, " +
                    $"{analysis.DroppedItems} unquoted item(s) dropped.]");
                if (!analysis.IsValid) analysis = null;
            }
        }

        var prior = Session.Mastery.TryGetValue(concept.Key, out var m) ? m.Estimate : MasteryModel.Prior;
        var judgedBefore = Session.Turns.Count(t => t.ConceptId == concept.Key && t.Judgment is not null && !ObjectiveAssessor.IsUnjudged(t.Judgment));
        // A failed panel (no usable verdict) is not evidence of anything: it leaves the estimate where it was,
        // as ObjectiveAssessor already treats such an answer as neither for nor against (GAD-T176).
        // A recited answer (GAD-T242) is not evidence of the student's own understanding either: it leaves the estimate too.
        var estimate = ObjectiveAssessor.IsUnjudged(judgment) || judgment.Recitation is not null ? prior
            : MasteryModel.Update(prior, judgment.OverallScore,
                MasteryModel.QuestionDifficulty(turn.Move, Session.Policy.Difficulty), judgedBefore);
        turn = Session.RecordJudgment(judgment, estimate, analysis);

        // Scenario I: the panel found the question itself gave expected points away (hints teach on purpose).
        if (analysis is { QuestionRevealedAnswer: true } && !MoveProfiles.IsAssistance(turn.Move))
            FlagLeak(turn.Index, LeakSource.Analysis, "A majority of the analysis panel judged the question revealed expected answer points.");

        if (judgment.OverallScore > 0)
        {
            if (analysis is { Deterministic: false, Claims.Count: > 0 })
                await _claims.AddClaimsAsync(Ledger, turn, analysis.Claims.Select(c => (c.Quote, c.Text)).ToList(), ct);
            else
                await _claims.AnalyzeAsync(Ledger, turn, concept.Title, ct);
        }

        if (Diagnostics is not null)
        {
            var a = ObjectiveAssessor.Assess(Session, objective, Ledger.ForPlanner());
            Diagnostics.Report($"[Objective {objective.Key}: {a.Status}, level {(int)a.Level} reported / {(int)a.ProvisionalLevel} provisional; " +
                $"{a.IndependentAnswers} independent, {a.SupportingAnswers} supporting, {a.ContaminatedAnswers} contaminated, {a.AssistedAnswers} assisted. {a.Explanation}]");
        }
        return Session.Turns[turn.Index];
    }

    /// <summary>Ends the session early (the student left, the host stopped it).</summary>
    public void End(EndReason reason)
    {
        if (!Session.IsEnded) Session.End(reason, _clock.GetUtcNow());
    }

    /// <summary>The report for the session so far (normally called once it has ended), with per-objective levels.</summary>
    public SessionReport Report() =>
        SessionReport.From(Session, _subject.Map.Concepts.ToDictionary(c => c.Key, c => c.Title, StringComparer.Ordinal),
            objectives: Assessments());

    private async Task<bool> IsAporiaAsync(Turn turn, CancellationToken ct)
    {
        var text = turn.Answer!.Text.Trim();
        if (text.Equals(AporiaWord, StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Length == 0) return true;
        if (text.Length > 200) return false; // a long answer is an attempt, not a shrug
        var prompt = ExamPrompts.Fill(_prompts[ExamPrompts.AporiaKey], new Dictionary<string, string?>
        {
            ["question"] = turn.Question,
            ["answer"] = ExamPrompts.Student(text),
        });
        var d = await _panel.DecideAsync(prompt, ["APORIA", "ANSWER"], "", ct);
        return string.Equals(d.Choice, "APORIA", StringComparison.OrdinalIgnoreCase);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "...";
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
