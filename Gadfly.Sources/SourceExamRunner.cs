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
        _generator = new QuestionGenerator(panel, _prompts) { Language = subject.Language };
        // The exam language (GAD-T535, GAD-T536, GAD-T555): an English attempt by an English-primary student judges exactly as before.
        _judge = new PanelAnswerJudge(panel, _prompts)
        {
            ContentView = subject.Language.IsNonNative,
            CrossLingualReview = subject.Language.Exam != ExamLanguage.English,
        };
        _claims = new ClaimAnalyzer(panel, _prompts);
        _style = new TextStyleAssessor(panel, _prompts);
        _analyzer = new ResponseAnalyzer(panel, _prompts);
        Objectives = _planner.ObjectivesFor(session);
    }

    /// <summary>Whether answers get the answer-text integrity assessment (GAD-T59). On by default.</summary>
    public bool AssessTextStyle { get; set; } = true;

    /// <summary>
    /// The live turn's one fast model (GAD-T494): when set, it words each question and runs its leak guard instead of the panel
    /// vote, so the examiner can answer in conversational time. Null (the default) keeps the panel.
    /// </summary>
    public ILiveTurnModel? LiveTurnModel { get => _generator.LiveModel; set => _generator.LiveModel = value; }

    /// <summary>Whether answers get the panel response analysis (GAD-T91). On by default; off only for cost-limited runs.</summary>
    public bool AnalyzeResponses { get; set; } = true;

    /// <summary>
    /// Machine-translates source text into a language (text, target exam-language tag), for the cross-lingual recitation check
    /// (GAD-T600, GAD-T642): a Korean or Spanish answer recited from a translation of the English notes is compared with the
    /// translated passages too. Null (the default) checks against the source alone. A failure is skipped, never failing a judgment.
    /// </summary>
    public Func<string, string, CancellationToken, Task<string?>>? SourceTranslator { get; set; }

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

    /// <summary>
    /// "Say it another way" (GAD-T555): the open question reworded through the leak guard and recorded on the turn. When no rewording
    /// passes the guard the question itself is returned, to be said again word for word. Null when no question is open.
    /// </summary>
    public async Task<string?> RephraseAsync(CancellationToken ct = default)
    {
        if (Session.OpenTurn is not { } open) return null;
        var rubric = _subject.Rubric(open.ConceptId).Describe();
        var latest = open.Rephrasings.Count > 0 ? open.Rephrasings[^1] : open.Question;
        var text = await _generator.RephraseAsync(latest, rubric, Session, ct) ?? open.Question;
        Session.RecordRephrasing(text);
        return text;
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

        // Planned from the newest full judgments plus the provisional ones still pending (GAD-T508).
        var decision = _planner.Next(Session.ForPlanning(), _clock.GetUtcNow(), Ledger.ForPlanner());
        if (decision.EndsSession && Session.PendingFullJudgments.Count > 0)
        {
            // The exam would end on a provisional judgment: the panel's full judgments come in first, and decide again from them
            // (they may show an objective is not yet settled). A speculative copy leaves that to the live session.
            if (Speculative) throw new InvalidOperationException("A speculative step does not end the exam before the panel has judged.");
            var waitStarted = _clock.GetTimestamp();
            var pending = Session.PendingFullJudgments.Count;
            var ok = false;
            try
            {
                await FinishFullJudgmentsAsync(ct);
                decision = _planner.Next(Session.ForPlanning(), _clock.GetUtcNow(), Ledger.ForPlanner());
                ok = true;
            }
            finally { NoteEndWait(pending, waitStarted, ok && decision.EndsSession, !ok, planned: true); }
        }
        else if (!decision.EndsSession && LeavesSettledObjectiveWithPending(decision))
        {
            // Leaving an objective its provisional judgments call settled (GAD-T515): a misconception or contradiction the panel
            // finds in its last answers would otherwise arrive after the examiner left, and the objective would turn Inconclusive
            // with no chance to probe it. Its pending full judgments come in first (one wait per objective) and the move is planned
            // again. A panel failure keeps the move as planned; a speculative copy leaves the wait to the live session.
            if (Speculative) throw new InvalidOperationException("A speculative step does not leave an objective before the panel has judged it.");
            try
            {
                await FinishFullJudgmentsAsync(ct);
                decision = _planner.Next(Session.ForPlanning(), _clock.GetUtcNow(), Ledger.ForPlanner());
            }
            catch (InvalidOperationException ex)
            {
                Diagnostics?.Report($"[Leaving the objective on its provisional judgments: {ex.Message}]");
            }
        }
        if (decision.EndsSession)
        {
            Diagnostics?.Report($"[End: {decision.Reason}]");
            Session.End(decision.End, _clock.GetUtcNow());
            return null;
        }

        // A second-language speaker's word the recognizer was unsure of, in an answer not yet convincing (GAD-T598): the examiner
        // asks about that one word before moving on, instead of the judges scoring a misheard word as wrong. Fixed text that only
        // quotes the word as heard, so there is nothing to leak.
        if (_subject.Language.IsNonNative && Session.Turns.LastOrDefault() is { } previous && UncertainWords.ToAsk(Session, previous) is { } word)
        {
            var check = UncertainWords.Question(word, _subject.Language.Exam);
            var why = $"Word check (GAD-T598): speech recognition was unsure of \"{word}\" in the last answer, which was not yet convincing; the student is asked about the word instead of it being judged as misheard.";
            Diagnostics?.Report($"[Clarify on {previous.ObjectiveKey ?? previous.ConceptId}: {why}]");
            return Session.Ask(previous.ConceptId, MoveKind.Clarify, check, _clock.GetUtcNow(), reason: why, objectiveKey: previous.ObjectiveKey,
                author: QuestionAuthorship.Fixed(UncertainWords.Source));
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
        // The turn records the move actually asked (GAD-T814): a fixed fallback for a Counterexample, an Apply, or a Contradiction
        // without both quoted claims asks another move, so its probed level and dimensions follow the question and it never credits transfer.
        var move = _generator.LastMove;
        // An Apply question's reason carries its transfer label, so reviewers see how its situation differs from the source,
        // and that the transfer measured is sequestered (GAD-T268).
        // Whether the Apply situation is really new (GAD-T442): word overlap with the passages the question was grounded in and
        // with the student's earlier answers. Deterministic and local, so it adds no wait; a reused situation is not redrafted
        // (that would be another panel round trip) but recorded, and its answer is credited as extrapolation, not transfer.
        var novelty = move == MoveKind.Apply
            ? ApplyNovelty.Check(question, grounding.Text, Session, concept.Title, objective?.Title, related)
            : null;
        var reason = move == MoveKind.Apply
            ? $"{decision.Reason} Transfer item ({TransferLabels.Label(TransferLabels.Next(Session, decision.ObjectiveKey ?? concept.Key))}). {TransferLabels.Caveat} {novelty!.Describe()}"
            : decision.Reason;
        if (move != decision.Move)
            reason = $"{reason} Planned {decision.Move}; the fixed fallback question asks {move} instead, and the turn is recorded as {move}.".Trim();
        if (_generator.LastOutcome == QuestionGenerator.FixedOpeningOutcome)
            reason = $"{reason} Fixed opening question: approved by the instructor and asked word for word to every student (GAD-T436).".Trim();
        Diagnostics?.Report($"[{move} on {decision.ObjectiveKey ?? concept.Key}{(decision.IsReplacement ? " (replacement)" : "")}: {reason} Question {_generator.LastOutcome}.]");
        return Session.Ask(concept.Key, move, question, _clock.GetUtcNow(), decision.TargetClaimIds, reason,
            decision.ObjectiveKey, decision.IsReplacement, novelty, _generator.LastAuthor);
    }

    /// <summary>
    /// True when <paramref name="decision"/> moves off the objective of the last turn while that objective is Sufficient only on the
    /// planning view and some of its answers still wait for the panel's full judgment (GAD-T515).
    /// </summary>
    private bool LeavesSettledObjectiveWithPending(PlanDecision decision)
    {
        if (Session.Turns.Count == 0 || Session.PendingFullJudgments.Count == 0) return false;
        var current = ExamObjectives.KeyOf(Session.Turns[^1]);
        if (decision.ObjectiveKey == current || !Session.PendingFullJudgments.Any(t => ExamObjectives.KeyOf(t) == current)) return false;
        return _planner.Assess(Session.ForPlanning(), Ledger.ForPlanner()).FirstOrDefault(a => a.ObjectiveKey == current)?.Status == ObjectiveStatus.Sufficient;
    }

    /// <summary>
    /// Records the answer to the open turn (unless it already has one, e.g. after a crash between recording and judging) and judges
    /// it. With <see cref="DeferFullJudgment"/> and a <see cref="LiveTurnModel"/> (the server's live exam, GAD-T508) the judgment
    /// made here is provisional: one fast model scores the answer so the next move can be planned in conversational time, and the
    /// full panel judgment (aporia, judges, analysis, claims) follows off the turn (<see cref="StartFullJudgment"/>). Otherwise, or
    /// when the fast model fails, the panel judges and analyses it here, updates mastery and the claim ledger. Returns the judged turn.
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

        if (DeferFullJudgment && LiveTurnModel is { } fast && await ProvisionalAsync(fast, turn, ct) is { } steered) return steered;
        // No fast model, or it failed: the panel judges on the turn, as before GAD-T508 (an outage costs speed, never the judgment).
        // Earlier turns still waiting for their full judgment go first, so the claim ledger stays in turn order.
        if (Session.PendingFullJudgments.Count > 0)
        {
            if (Speculative) throw new InvalidOperationException("A speculative step does not run the panel on earlier answers.");
            await FinishFullJudgmentsAsync(ct);
        }
        return await JudgeOnTurnAsync(turn, ct);
    }

    /// <summary>The panel judges <paramref name="turn"/> on the turn: the whole judgment before the next move (the CLI, and the fallback).</summary>
    private async Task<Turn> JudgeOnTurnAsync(Turn turn, CancellationToken ct)
    {
        var concept = _subject.Concept(turn.ConceptId);
        var rubric = _subject.Rubric(concept.Key);
        var objective = ObjectiveOf(turn);
        var (judgment, analysis, passages) = await PanelJudgmentAsync(turn, concept, rubric, objective, Ledger.Describe(concept.Key), Session, ct);
        // Recitation check (GAD-T242): an answer that closely follows the source's wording, or a long fluent one after a long
        // silent wait, is not accepted as the student's own understanding. It is recorded for review (never a cheating verdict),
        // left out of mastery and evidence, and the next question asks for it differently, applied, or why.
        if (passages is not null && await RecitationAsync(turn, passages, ct) is { } recited)
            judgment = WithRecitation(judgment, recited, turn);

        turn = Session.RecordJudgment(judgment, Estimate(turn, judgment), analysis);

        // Scenario I: the panel found the question itself gave expected points away (hints teach on purpose).
        if (analysis is { QuestionRevealedAnswer: true } && !MoveProfiles.IsAssistance(turn.Move))
            FlagLeak(turn.Index, LeakSource.Analysis, "A majority of the analysis panel judged the question revealed expected answer points.");

        await RecordClaimsAsync(Ledger, turn, concept.Title, judgment, analysis, ct);
        ReportClaims(turn, objective);
        return Session.Turns[turn.Index];
    }

    /// <summary>
    /// The panel's part of judging one answer: the aporia check, then the verdict, the text-style estimate and the analysis together.
    /// Reads only its arguments (never the live session or ledger), so it can run off the session's step lock (GAD-T508). Passages are
    /// null for an aporia answer. <paramref name="styleSession"/> is what the text-style estimate compares against.
    /// </summary>
    private async Task<(AnswerJudgment Judgment, ResponseAnalysis? Analysis, IReadOnlyList<SourcePassage>? Passages)> PanelJudgmentAsync(Turn turn,
        ConceptEntry concept, ConceptRubric rubric, ExamObjective objective, string earlier, SocraticSession? styleSession, CancellationToken ct)
    {
        if (await IsAporiaAsync(turn, ct))
            return (AporiaJudgment(rubric.Version), ResponseAnalyzer.ForAporia(turn, objective.Key), null);

        var passages = await _subject.PassagesAsync(concept.Key, turn.Answer!.Text, ct);
        // The verdict, the text-style estimate and the analysis each read only the answer, the passages and the earlier claims
        // (none uses another's result), so their panel calls run together instead of one after another (GAD-T287).
        var judgeTask = _judge.JudgeAsync(new JudgeInput
        {
            Turn = turn,
            Concept = concept,
            Rubric = rubric,
            Passages = passages,
            EarlierClaims = earlier,
        }, rubric.Version, ct);
        var styleTask = AssessTextStyle && styleSession is not null ? _style.AssessAsync(styleSession, turn, ct) : null;
        var analysisTask = AnalyzeResponses
            ? _analyzer.AnalyzeAsync(new AnalysisInput
            {
                Turn = turn, Objective = objective, Objectives = Objectives, Rubric = rubric, Passages = passages, EarlierClaims = earlier,
            }, ct)
            : null;
        await Task.WhenAll(new Task?[] { judgeTask, styleTask, analysisTask }.OfType<Task>());
        var judgment = await judgeTask;
        if (styleTask is not null && await styleTask is { } style)
            judgment = judgment with { TextStyle = style };
        ResponseAnalysis? analysis = null;
        if (analysisTask is not null)
        {
            analysis = await analysisTask;
            Diagnostics?.Report($"[Analysis: {analysis.Evidence.Count} evidence, {analysis.Misconceptions.Count} misconception(s), " +
                $"{analysis.Uncertainties.Count} uncertaint(ies), {analysis.Claims.Count} claim(s); {analysis.Voters} valid voter(s), " +
                $"{analysis.RejectedVotes} rejected{(analysis.Rejections.Count > 0 ? " (" + string.Join("; ", analysis.Rejections) + ")" : "")}, " +
                $"{analysis.DroppedItems} unquoted item(s) dropped.]");
            if (!analysis.IsValid) analysis = null;
        }
        return (judgment, analysis, passages);
    }

    private static AnswerJudgment AporiaJudgment(int rubricVersion) => new()
    {
        Criteria = [],
        OverallScore = 0,
        RubricVersion = rubricVersion,
        Summary = "The student said they do not know (aporia).",
    };

    /// <summary>
    /// The recitation check (GAD-T242) of <paramref name="turn"/>'s answer against <paramref name="passages"/> and, in a non-English
    /// exam with a <see cref="SourceTranslator"/>, against their translation into the exam language too (GAD-T600, GAD-T642).
    /// Reads <see cref="Session"/> for the answer's timing; null when the answer reads as the student's own.
    /// </summary>
    public async Task<RecitationFlag?> RecitationAsync(Turn turn, IReadOnlyList<SourcePassage> passages, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(passages);
        var translated = await TranslatedSourceAsync(turn, passages, ct);
        return RecitationCheck.Check(Session, turn, SourceText(passages), translated);
    }

    private static string SourceText(IReadOnlyList<SourcePassage> passages) => string.Join('\n', passages.Select(p => p.Text));

    /// <summary>
    /// <paramref name="passages"/> machine-translated into the exam language, each separately, joined by newlines;
    /// null when there is no <see cref="SourceTranslator"/>, the exam is in English, or the answer is too short to be compared
    /// (<see cref="RecitationCheck.MinWordsForSourceMatch"/>). A passage whose translation fails is left out: the check never
    /// fails a judgment.
    /// </summary>
    private async Task<string?> TranslatedSourceAsync(Turn turn, IReadOnlyList<SourcePassage> passages, CancellationToken ct)
    {
        var translator = SourceTranslator;
        var language = _subject.Language.Exam;
        if (translator is null || language == ExamLanguage.English || passages.Count == 0
            || LanguageText.WordCount(turn.Answer?.Text) < RecitationCheck.MinWordsForSourceMatch)
            return null;
        var translations = await Task.WhenAll(passages.Select(async p =>
        {
            if (string.IsNullOrWhiteSpace(p.Text)) return null;
            try { return await translator(p.Text, language, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Diagnostics?.Report($"[Recitation check: a source passage could not be translated into {language}; checked without it: {ex.Message}]");
                return null;
            }
        }));
        var parts = translations.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        return parts.Count == 0 ? null : string.Join('\n', parts);
    }

    private AnswerJudgment WithRecitation(AnswerJudgment judgment, RecitationFlag recited, Turn turn)
    {
        if (judgment.Recitation is not null) return judgment;
        Diagnostics?.Report($"[Recitation on turn {turn.Index + 1}: {recited.Reason}. Not accepted; asking for it differently.]");
        return judgment with
        {
            Recitation = recited,
            Summary = string.IsNullOrWhiteSpace(judgment.Summary) ? RecitationCheck.ReviewNote(recited)
                : $"{judgment.Summary} {RecitationCheck.ReviewNote(recited)}",
        };
    }

    /// <summary>
    /// The concept's new mastery estimate after <paramref name="turn"/>, from the judgments the examiner steers by. A failed panel (no
    /// usable verdict) is not evidence of anything: it leaves the estimate where it was, as ObjectiveAssessor already treats such an
    /// answer as neither for nor against (GAD-T176). A recited answer (GAD-T242) is not evidence of the student's own understanding
    /// either: it leaves the estimate too.
    /// </summary>
    private double Estimate(Turn turn, AnswerJudgment judgment)
    {
        var prior = Session.Mastery.TryGetValue(turn.ConceptId, out var m) ? m.Estimate : MasteryModel.Prior;
        var judgedBefore = Session.Turns.Count(t => t.ConceptId == turn.ConceptId && t.Index != turn.Index
            && SocraticSession.SteeringJudgment(t) is { } j && !ObjectiveAssessor.IsUnjudged(j));
        return ObjectiveAssessor.IsUnjudged(judgment) || judgment.Recitation is not null ? prior
            : MasteryModel.Update(prior, judgment.OverallScore, MasteryModel.QuestionDifficulty(turn.Move, Session.Policy.Difficulty), judgedBefore);
    }

    /// <summary>
    /// Claims the student withdrew in this answer ("I was wrong earlier", GAD-T441) are recorded before the new claims are checked,
    /// so a withdrawn claim is never a premise of a new contradiction. No extra panel call: the analysis found them.
    /// </summary>
    private async Task RecordClaimsAsync(ClaimLedger ledger, Turn turn, string conceptTitle, AnswerJudgment judgment, ResponseAnalysis? analysis,
        CancellationToken ct)
    {
        var retractions = analysis?.Retractions.Select(r => (r.Quote, r.Text)).ToList();
        if (judgment.OverallScore > 0)
        {
            if (analysis is { Deterministic: false } && (analysis.Claims.Count > 0 || analysis.Retractions.Count > 0))
                await _claims.AddClaimsAsync(ledger, turn, analysis.Claims.Select(c => (c.Quote, c.Text)).ToList(), ct, retractions);
            else
                await _claims.AnalyzeAsync(ledger, turn, conceptTitle, ct);
        }
        else
            ClaimAnalyzer.ApplyRetractions(ledger, turn, retractions);
    }

    private void ReportClaims(Turn turn, ExamObjective objective)
    {
        foreach (var r in Ledger.Retractions.Where(r => r.TurnIndex == turn.Index))
            Diagnostics?.Report($"[Turn {turn.Index + 1}: the student withdrew {(r.ClaimId is { } id ? $"claim {id}" : "an earlier claim no ledger entry matched")}: \"{r.Text}\".]");

        if (Diagnostics is not null)
        {
            var a = ObjectiveAssessor.Assess(Session, objective, Ledger.ForPlanner());
            Diagnostics.Report($"[Objective {objective.Key}: {a.Status}, level {(int)a.Level} reported / {(int)a.ProvisionalLevel} provisional; " +
                $"{a.IndependentAnswers} independent, {a.SupportingAnswers} supporting, {a.ContaminatedAnswers} contaminated, {a.AssistedAnswers} assisted. {a.Explanation}]");
        }
    }

    // ------------------------------------------------------------------ provisional judgment and the full panel off the turn (GAD-T508)

    /// <summary>
    /// When set (the server's live exam, GAD-T508), an answer is judged provisionally by the <see cref="LiveTurnModel"/> on the turn
    /// and the panel's full judgment runs off the turn (<see cref="StartFullJudgment"/>, <see cref="ApplyFinishedJudgmentAsync"/>).
    /// Off by default: the CLI, previews and tests judge with the panel on the turn.
    /// </summary>
    public bool DeferFullJudgment { get; set; }

    /// <summary>
    /// A speculative copy of the session (GAD-T497): it never runs the panel on answers whose full judgment is still pending (the live
    /// session does that). Where it would have to (the exam would end, or the fast model failed with earlier answers pending), it throws,
    /// and the speculation is dropped.
    /// </summary>
    public bool Speculative { get; set; }

    /// <summary>
    /// A full panel judgment prepared off the session's step lock for <see cref="TurnIndex"/> (GAD-T508), with the claim ledger as it
    /// will be once it is applied (<see cref="LedgerBefore"/> is the ledger it was computed from), the panel calls it made (for
    /// metering) and, once applied, how it compared with the provisional judgment.
    /// </summary>
    public sealed record FullJudgment
    {
        public required int TurnIndex { get; init; }
        public AnswerJudgment? Judgment { get; init; }
        public ResponseAnalysis? Analysis { get; init; }
        public ClaimLedger? Ledger { get; init; }
        public string LedgerBefore { get; init; } = "";
        public IReadOnlyList<PanelCall> Calls { get; init; } = [];
        public long ElapsedMs { get; init; }
        /// <summary>Why the panel could not judge (the judgment is then null and the turn stays pending).</summary>
        public string? Error { get; init; }
        /// <summary>True once it was recorded on its turn; false when dropped (the turn was judged meanwhile, or the session ended).</summary>
        public bool Applied { get; init; }
        /// <summary>The provisional judgment it replaced, as it stood when the full one arrived.</summary>
        public ProvisionalJudgment? Provisional { get; init; }
        /// <summary>Set when the full judgment disagreed materially with the provisional one (see <see cref="Disagreement(AnswerJudgment, AnswerJudgment)"/>).</summary>
        public string? Disagreement { get; init; }
    }

    /// <summary>A score difference at or above this between the provisional and the full judgment is a material disagreement.</summary>
    public const double MaterialDisagreement = 0.25;

    private Task<FullJudgment>? _inflight;
    private int _inflightTurn = -1;
    private readonly List<FullJudgment> _finished = [];

    /// <summary>
    /// The provisional judgment (GAD-T508): the judge prompt answered by the one fast model, the recitation check (local), and the
    /// concept's mastery from it. Null when the fast model fails or gives no usable verdict.
    /// </summary>
    private async Task<Turn?> ProvisionalAsync(ILiveTurnModel fast, Turn turn, CancellationToken ct)
    {
        var concept = _subject.Concept(turn.ConceptId);
        var rubric = _subject.Rubric(concept.Key);
        var started = _clock.GetTimestamp();
        var text = turn.Answer!.Text.Trim();
        AnswerJudgment judgment;
        if (text.Length == 0 || text.Equals(AporiaWord, StringComparison.OrdinalIgnoreCase))
            judgment = AporiaJudgment(rubric.Version);
        else
        {
            var passages = await _subject.PassagesAsync(concept.Key, turn.Answer.Text, ct);
            // The source's translation (cross-lingual recitation check, GAD-T642) runs alongside the fast judge call.
            var translation = TranslatedSourceAsync(turn, passages, ct);
            var verdict = await _judge.JudgeWithAsync(fast, new JudgeInput
            {
                Turn = turn, Concept = concept, Rubric = rubric, Passages = passages, EarlierClaims = Ledger.Describe(concept.Key),
            }, rubric.Version, ct);
            if (verdict is not { } v) return null;
            // As with the panel's aporia check, only a short answer can be a plain "I don't know" (a long one is an attempt).
            judgment = v.Aporia && text.Length <= 200 ? AporiaJudgment(rubric.Version) : v.Judgment;
            if (RecitationCheck.Check(Session, turn, SourceText(passages), await translation) is { } recited)
                judgment = WithRecitation(judgment, recited, turn);
        }
        var provisional = new ProvisionalJudgment
        {
            Judgment = judgment,
            ProviderId = fast.ProviderId,
            Model = fast.Model,
            JudgedAt = _clock.GetUtcNow(),
            ElapsedMs = (long)_clock.GetElapsedTime(started).TotalMilliseconds,
        };
        Diagnostics?.Report($"[Turn {turn.Index + 1} judged provisionally by {fast.ProviderId}/{fast.Model}: {judgment.OverallScore:0.00}; the panel judges it in the background.]");
        return Session.RecordProvisional(provisional, Estimate(turn, judgment));
    }

    /// <summary>
    /// Starts the full panel judgment of the earliest answer still waiting for one, unless it is already running (GAD-T508), and
    /// returns its task (null when nothing is pending or the session has ended). One at a time, in turn order, so each sees the claim
    /// ledger of every earlier answer. It reads a snapshot taken here, never the live session, and never throws (a failure comes back
    /// as <see cref="FullJudgment.Error"/>). <paramref name="retryFailed"/> false leaves a judgment that failed for the next step to retry.
    /// Call under the session's step serialization, like every other engine call.
    /// </summary>
    public Task<FullJudgment>? StartFullJudgment(bool retryFailed = true)
    {
        if (Session.IsEnded || Session.PendingFullJudgments.FirstOrDefault() is not { } next) return null;
        if (_inflight is not null && _inflightTurn == next.Index)
        {
            var failed = _inflight.IsFaulted || _inflight.IsCanceled || (_inflight.IsCompletedSuccessfully && _inflight.Result.Error is not null);
            if (!failed || !retryFailed) return _inflight;
        }
        var concept = _subject.Concept(next.ConceptId);
        var rubric = _subject.Rubric(concept.Key);
        var objective = ObjectiveOf(next);
        var earlier = Ledger.Describe(concept.Key);
        var ledgerBefore = Ledger.ToJson();
        var styleSession = AssessTextStyle ? SessionTranscriptSerializer.Deserialize(SessionTranscriptSerializer.Serialize(Session)) : null;
        _inflightTurn = next.Index;
        _inflight = Task.Run(() => FullJudgmentAsync(next, concept, rubric, objective, earlier, ledgerBefore, styleSession));
        return _inflight;
    }

    private async Task<FullJudgment> FullJudgmentAsync(Turn turn, ConceptEntry concept, ConceptRubric rubric, ExamObjective objective, string earlier,
        string ledgerBefore, SocraticSession? styleSession)
    {
        // Its own metering scope: the step that started it may have finished long before it does.
        using var usage = PanelUsage.Track(out var calls);
        var started = _clock.GetTimestamp();
        try
        {
            var (judgment, analysis, _) = await PanelJudgmentAsync(turn, concept, rubric, objective, earlier, styleSession, CancellationToken.None);
            var ledger = ClaimLedger.FromJson(ledgerBefore);
            await RecordClaimsAsync(ledger, turn, concept.Title, judgment, analysis, CancellationToken.None);
            return new FullJudgment
            {
                TurnIndex = turn.Index, Judgment = judgment, Analysis = analysis, Ledger = ledger, LedgerBefore = ledgerBefore,
                Calls = Snapshot(calls), ElapsedMs = (long)_clock.GetElapsedTime(started).TotalMilliseconds,
            };
        }
        catch (Exception ex)
        {
            return new FullJudgment
            {
                TurnIndex = turn.Index, LedgerBefore = ledgerBefore, Calls = Snapshot(calls), Error = $"{ex.GetType().Name}: {ex.Message}",
                ElapsedMs = (long)_clock.GetElapsedTime(started).TotalMilliseconds,
            };
        }

        static IReadOnlyList<PanelCall> Snapshot(IReadOnlyList<PanelCall> list)
        {
            lock (list) return [.. list];
        }
    }

    /// <summary>
    /// Applies the full judgment that was running, if it has finished (GAD-T508): it becomes the turn's judgment (the one graded), the
    /// concept's mastery is replayed from it, a leak the analysis found is flagged, and the claim ledger takes in its claims. Returns
    /// true when the session changed. Does not wait; call under the session's step serialization.
    /// </summary>
    public async Task<bool> ApplyFinishedJudgmentAsync(CancellationToken ct = default)
    {
        if (_inflight is not { IsCompleted: true } done) return false;
        return await ApplyAsync(await done, ct);
    }

    /// <summary>
    /// Waits for the full judgment of every answer still pending, one after another in turn order, and applies each (GAD-T508): before
    /// the session ends, before the panel judges a later answer on the turn, and wherever the full judgments must be in. Returns how many
    /// were applied; throws when the panel could not judge one (it stays pending, with its provisional judgment).
    /// </summary>
    public async Task<int> FinishFullJudgmentsAsync(CancellationToken ct = default)
    {
        var applied = 0;
        while (!Session.IsEnded && StartFullJudgment() is { } task)
        {
            var result = await task.WaitAsync(ct);
            if (result.Error is not null)
            {
                await ApplyAsync(result, ct);
                throw new InvalidOperationException($"The panel could not judge turn {result.TurnIndex + 1}: {result.Error}");
            }
            if (await ApplyAsync(result, ct)) applied++;
        }
        return applied;
    }

    /// <summary>
    /// The full judgments finished since the last call, applied or dropped, with the panel calls they made (GAD-T508): the host meters
    /// them, and records any material disagreement with the provisional judgment for reviewers.
    /// </summary>
    public IReadOnlyList<FullJudgment> TakeFinishedJudgments()
    {
        var list = _finished.ToList();
        _finished.Clear();
        return list;
    }

    /// <summary>
    /// This engine replaces <paramref name="other"/> for the same session (a speculative step adopted, GAD-T497): the full judgment
    /// <paramref name="other"/> has running, and the finished ones not yet taken, carry over, so nothing is judged twice or left unmetered.
    /// </summary>
    public void TakeOverJudgingFrom(SourceExamEngine other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this)) return;
        _inflight = other._inflight;
        _inflightTurn = other._inflightTurn;
        _finished.AddRange(other._finished);
        other._finished.Clear();
        other._inflight = null;
        other._inflightTurn = -1;
    }

    private async Task<bool> ApplyAsync(FullJudgment result, CancellationToken ct)
    {
        if (_inflightTurn == result.TurnIndex && _inflight is { IsCompletedSuccessfully: true } && ReferenceEquals(_inflight.Result, result) && result.Error is null)
        {
            _inflight = null;
            _inflightTurn = -1;
        }
        if (result.Error is not null || result.Judgment is null)
        {
            if (!_finished.Contains(result)) _finished.Add(result with { Calls = Consumed(result) });
            Diagnostics?.Report($"[The panel could not judge turn {result.TurnIndex + 1} yet: {result.Error}]");
            return false;
        }
        if (Session.IsEnded || result.TurnIndex >= Session.Turns.Count
            || Session.Turns[result.TurnIndex] is not { Judgment: null, Provisional: { } provisional } turn)
        {
            _finished.Add(result with { Calls = Consumed(result) });
            return false;
        }
        var judgment = provisional.Judgment.Recitation is { } recited ? WithRecitation(result.Judgment, recited, turn) : result.Judgment;
        var disagreement = Disagreement(provisional.Judgment, judgment);
        var updated = provisional with { FullScore = judgment.OverallScore, Disagreement = disagreement };
        turn = Session.RecordFullJudgment(result.TurnIndex, judgment, result.Analysis, updated);
        if (result.Analysis is { QuestionRevealedAnswer: true } && !MoveProfiles.IsAssistance(turn.Move))
            FlagLeak(turn.Index, LeakSource.Analysis, "A majority of the analysis panel judged the question revealed expected answer points.");
        if (result.Ledger is not null && Ledger.ToJson() == result.LedgerBefore) Ledger.ReplaceWith(result.Ledger);
        else await RecordClaimsAsync(Ledger, turn, _subject.Concept(turn.ConceptId).Title, judgment, result.Analysis, ct);
        ReportClaims(turn, ObjectiveOf(turn));
        Diagnostics?.Report($"[Turn {turn.Index + 1} judged by the panel: {judgment.OverallScore:0.00} (provisional {provisional.Judgment.OverallScore:0.00})" +
            (disagreement is null ? ".]" : $"; they disagree: {disagreement}]"));
        _finished.Add(result with { Calls = Consumed(result), Applied = true, Provisional = updated, Disagreement = disagreement });
        return true;
    }

    /// <summary>A finished judgment's calls are metered once: a result seen twice (a failure retried) hands them over only the first time.</summary>
    private readonly HashSet<FullJudgment> _metered = new(ReferenceEqualityComparer.Instance);

    private IReadOnlyList<PanelCall> Consumed(FullJudgment result) => _metered.Add(result) ? result.Calls : [];

    /// <summary>
    /// Why the full judgment disagrees materially with the provisional one (GAD-T508), or null when it does not: one heard an honest
    /// "I don't know" and the other an attempt, the answer falls on a different side of the evidence thresholds (positive, mixed,
    /// negative), or the overall scores differ by <see cref="MaterialDisagreement"/> or more. A panel with no usable verdict is not
    /// compared (the answer goes to a human either way).
    /// </summary>
    public static string? Disagreement(AnswerJudgment provisional, AnswerJudgment full)
    {
        ArgumentNullException.ThrowIfNull(provisional);
        ArgumentNullException.ThrowIfNull(full);
        if (ObjectiveAssessor.IsUnjudged(full) || ObjectiveAssessor.IsUnjudged(provisional)) return null;
        var (pa, fa) = (ObjectiveAssessor.IsAporia(provisional), ObjectiveAssessor.IsAporia(full));
        if (pa != fa)
            return fa ? "The panel heard an honest \"I don't know\"; the fast model scored it as an attempt."
                : "The fast model heard an honest \"I don't know\"; the panel scored it as an attempt.";
        var (pr, fr) = (ObjectiveAssessor.ResultOf(provisional.OverallScore), ObjectiveAssessor.ResultOf(full.OverallScore));
        var delta = Math.Abs(provisional.OverallScore - full.OverallScore);
        if (pr == fr && delta < MaterialDisagreement) return null;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"The fast model scored {provisional.OverallScore:0.00} ({pr}), the panel {full.OverallScore:0.00} ({fr}).");
    }

    /// <summary>Ends the session early (the student left, the host stopped it).</summary>
    public void End(EndReason reason)
    {
        if (!Session.IsEnded) Session.End(reason, _clock.GetUtcNow());
    }

    /// <summary>
    /// Ends the session once every answer's full panel judgment is in (GAD-T508), so the report and grading never read a provisional
    /// one. A panel that still fails does not stop the session ending: that answer keeps only its provisional judgment and goes to a
    /// human to grade, as an answer the panel never judged always has.
    /// </summary>
    public async Task EndAsync(EndReason reason, CancellationToken ct = default)
    {
        if (Session.IsEnded) return;
        if (Session.PendingFullJudgments.Count > 0)
        {
            var waitStarted = _clock.GetTimestamp();
            var pending = Session.PendingFullJudgments.Count;
            var failed = false;
            try { await FinishFullJudgmentsAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed = true;
                Diagnostics?.Report($"[Ending with an answer the panel could not judge; it goes to a human: {ex.Message}]");
            }
            NoteEndWait(pending, waitStarted, ended: true, failed, planned: false);
        }
        End(reason);
    }

    /// <summary>
    /// How long the exam's end waited for the panel's last full judgments (GAD-T516), measured where the exam would end: the planner
    /// deciding it is over (<see cref="NextQuestionAsync"/>) or the student, staff, the clock or the sweep ending it (<see cref="EndAsync"/>).
    /// </summary>
    /// <param name="Pending">Answers still waiting for their full judgment when the wait began (usually the last one or two).</param>
    /// <param name="WaitedMs">Wall time the end waited for them: the time a student sits on the last answer beyond the examiner's own step.</param>
    /// <param name="Ended">False when the panel's judgments changed the planner's mind and the exam went on (the wait still happened).</param>
    /// <param name="Failed">The panel could not judge one of them (it goes to a human) or the wait threw.</param>
    /// <param name="Planned">True when the planner ended it; false when the student, staff, the clock or the sweep did.</param>
    public sealed record EndJudgingWait(int Pending, long WaitedMs, bool Ended, bool Failed, bool Planned);

    private EndJudgingWait? _endWait;

    /// <summary>The end-of-exam judging wait measured since the last call (GAD-T516), or null; the host records it once.</summary>
    public EndJudgingWait? TakeEndJudgingWait()
    {
        var w = _endWait;
        _endWait = null;
        return w;
    }

    private void NoteEndWait(int pending, long started, bool ended, bool failed, bool planned)
    {
        if (pending == 0) return;
        var waited = (long)_clock.GetElapsedTime(started).TotalMilliseconds;
        _endWait = new EndJudgingWait(pending, waited, ended, failed, planned);
        Diagnostics?.Report($"[End waited {waited} ms for the panel's last {pending} judgment(s){(ended ? "" : "; the exam goes on")}{(failed ? "; one failed" : "")}.]");
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

    /// <summary>The last run's claim ledger (claims, contradictions, withdrawals and every pair checked), for saving beside the transcript.</summary>
    public ClaimLedger? Ledger { get; private set; }

    public async Task<(SocraticSession Session, SessionReport Report)> RunAsync(SessionPolicy policy, IExamConversation student,
        CancellationToken ct = default)
    {
        var engine = await SourceExamEngine.StartAsync(panel, subject, policy, clock, ct: ct);
        engine.Diagnostics = Diagnostics;
        Ledger = engine.Ledger;
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
