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
    /// <summary>The learning objective examined (GAD-T89); the concept title when the exam has none.</summary>
    public string? ObjectiveTitle { get; init; }
    /// <summary>The objective examined, for Learning Mode's fixed hint ladder (GAD-T271); null uses the default ladder.</summary>
    public ExamObjective? Objective { get; init; }
    /// <summary>
    /// Learning Mode: the misconception that prompted the hint. Recorded for context only: since GAD-T271 the hint's text
    /// comes from the fixed ladder, never tailored to it.
    /// </summary>
    public string? Misconception { get; init; }
}

/// <summary>What the leak guard said about the last question (GAD-T92).</summary>
public enum LeakGuardResult
{
    /// <summary>The guard judged the question safe.</summary>
    Safe,
    /// <summary>Every draft leaked; the plain fallback question was used.</summary>
    LeakedReplaced,
    /// <summary>The guard could not decide; the plain fallback question was used instead (fail closed).</summary>
    UndecidedReplaced,
    /// <summary>No guard ran: no rubric, or a Learning Mode hint/explanation, which teaches on purpose.</summary>
    Skipped,
}

/// <summary>
/// On a live exam the question and its leak guard come from one fast model (<see cref="LiveModel"/>, GAD-T494) for latency; the
/// panel path below remains for hosts without one and as the fallback when that model fails.
/// Words each question through the Legion panel (GAD-T35): several providers draft a question for
/// the planned move and a judge fuses the sharpest one. A second panel vote guards against
/// questions that give away rubric points; a leaking draft is redrafted once, then replaced by a
/// plain, safe question for the move. The guard fails closed: when it cannot decide, the draft is not
/// asked (GAD-T92). Learning Mode hints and guided explanations are never drafted: they are the next rung of the
/// objective's fixed, instructor-written hint ladder (GAD-T271), and skip the guard because they teach on purpose;
/// everything after them is assisted evidence (GAD-T93).
/// </summary>
public sealed partial class QuestionGenerator(ISocraticVotingClient panel, PromptSet? prompts = null)
{
    private readonly PromptSet _prompts = prompts ?? ExamPrompts.Set;

    public const string Leaks = "LEAKS";
    public const string Safe = "SAFE";

    /// <summary>How the last question was produced: "drafted", "redrafted", or "fallback: ..." with the reason.</summary>
    public string LastOutcome { get; private set; } = "";

    /// <summary>What the leak guard decided about the last question.</summary>
    public LeakGuardResult LastGuard { get; private set; } = LeakGuardResult.Skipped;

    /// <summary>Who wrote the last question (GAD-T431): the drafting provider(s) and their models, or a fixed source.</summary>
    public QuestionAuthorship? LastAuthor { get; private set; }

    /// <summary>
    /// The move the last question actually asks (GAD-T814): the planned move, or, when the fixed fallback stood in for a move it cannot
    /// ask without a model-written case or the student's quoted claims, the move it asked instead (<see cref="FallbackMove"/>).
    /// </summary>
    public MoveKind LastMove { get; private set; }

    /// <summary>The provider whose judge synthesizes a fused question (Legion's judge; Gadfly configures claude).</summary>
    public string JudgeProviderId { get; init; } = "claude";

    /// <summary>Turns of dialogue included in the prompt.</summary>
    public int DialogueTurns { get; init; } = 8;

    /// <summary>What the last answer showed, as the last question's prompt saw it (GAD-T233, GAD-T258); null before any answer.</summary>
    public AnswerDelivery? LastDelivery { get; private set; }

    public async Task<string> GenerateAsync(QuestionRequest r, CancellationToken ct = default)
    {
        var dialogue = Dialogue(r.Session);
        LastAuthor = null;
        LastMove = r.Decision.Move;
        if (MoveProfiles.IsAssistance(r.Decision.Move))
        {
            LastAuthor = QuestionAuthorship.Fixed(QuestionAuthorship.HintLadder);
            return Assist(r);
        }
        // Adaptive probing (GAD-T233, GAD-T258): what each recent answer said (hedging, vague wording, a missing reason or
        // example) and how the judges read it go into the prompt, and the last one sets how hard the question presses. Nothing
        // from the voice or its timing does.
        var recent = r.Session.Turns.TakeLast(DialogueTurns).ToList();
        LastDelivery = recent.LastOrDefault(t => t.Answer is not null) is { } answered ? AnswerDelivery.Read(r.Session, answered) : null;
        // A fixed opening question (GAD-T436) is asked word for word: no panel call, so it is also the quickest question to ask.
        // It was leak-guarded when the instructor approved it.
        if (FixedOpening(r) is { } fixedOpening)
        {
            LastOutcome = FixedOpeningOutcome;
            LastGuard = LeakGuardResult.Safe;
            LastAuthor = QuestionAuthorship.Fixed(QuestionAuthorship.InstructorText);
            return fixedOpening;
        }
        var targets = string.Join("\n", r.Decision.TargetClaimIds.Select(id => r.Ledger.Find(id))
            .Where(c => c is not null).Select(c => $"- (turn {c!.TurnIndex + 1}) {ExamPrompts.Student(c.Quote)}"));
        // An Apply question is labelled by how its situation differs from the source (GAD-T268, Barnett and Ceci 2002).
        var transfer = r.Decision.Move == MoveKind.Apply
            ? TransferLabels.Instruction(TransferLabels.Next(r.Session, r.Decision.ObjectiveKey ?? r.Decision.ConceptId ?? ""))
            : "(none)";
        var prompt = ExamPrompts.Fill(_prompts[ExamPrompts.QuestionKey], new Dictionary<string, string?>
        {
            ["move"] = r.Decision.Move.ToString(),
            ["objective"] = r.ObjectiveTitle ?? r.ConceptTitle,
            ["concept"] = r.ConceptTitle,
            ["related_concept"] = r.RelatedConceptTitle ?? "(none)",
            ["transfer"] = transfer,
            ["difficulty"] = r.Session.Policy.Difficulty.ToString(),
            ["target_claims"] = targets.Length == 0 ? "(none)" : targets,
            ["delivery"] = AnswerDelivery.Describe(r.Session, recent),
            ["probing"] = AnswerDelivery.Guidance(LastDelivery),
            ["grounding"] = r.Grounding.Text,
            ["dialogue"] = dialogue.Length == 0 ? "(this is the first question)" : dialogue,
        });

        var reasons = new List<string>();
        var undecided = false;
        var guarded = false;
        bool? previous = false; // the guard's verdict on the previous draft: true leaked, null undecided, false none (or empty)
        var introspective = false; // the previous draft asked about the student's own thought process (GAD-T445)
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var ask = previous switch
            {
                true => prompt + "\n\nYour previous draft revealed expected answer points. Ask without stating or hinting at any of them.",
                null => prompt + "\n\nYour previous draft could not be confirmed free of expected answer points. Ask without stating or hinting at any of them.",
                _ when introspective => prompt + "\n\nYour previous draft asked about the student's own thought process. Ask for the reasons behind their claim instead.",
                _ => prompt,
            };
            previous = false;
            introspective = false;
            LiveDraft? single = null;
            // Spoken as it is written (GAD-T495): with a voice waiting for this question, the live model's reply is split into sentences
            // and each part is guarded, then said, while the rest is still being written. Nothing is said before its guard passes.
            if (LiveModel is { } streamer && QuestionSpeech.Current is { } speech)
            {
                var streamed = await StreamDraftAsync(streamer, ask, r, dialogue, speech, ct);
                switch (streamed)
                {
                    case { Question: { } spokenQuestion }:
                        LastOutcome = streamed.Outcome + (attempt == 0 ? "" : " (redrafted)");
                        LastGuard = streamed.Guard;
                        LastAuthor = new QuestionAuthorship(QuestionAuthorship.LiveModelDraft, [streamer.ProviderId], [streamer.Model]);
                        return spokenQuestion;
                    case { Failed: true }:
                        single = new LiveDraft(streamed.Outcome, true);   // the panel drafts this attempt, as when the one-shot call fails
                        break;
                    default:
                        // Nothing was said: the first part leaked, was undecided, was not a usable question, or the reply was empty.
                        reasons.Add(streamed.Outcome);
                        introspective = streamed.Introspective;
                        if (streamed.Verdict is not false)
                        {
                            undecided = streamed.Verdict is null;
                            guarded = true;
                            previous = streamed.Verdict;
                        }
                        continue;
                }
            }
            // The live turn's one fast model words the question and guards it (GAD-T494); the panel only when none is set or it failed.
            if (single is null && LiveModel is { } live) single = await LiveDraftAsync(live, ask, ct);
            QuestionAuthorship author;
            string question;
            if (single is { Failed: false } drafted)
            {
                var fusedOne = Clean(drafted.Text);
                question = Usable(fusedOne, MaxQuestionWords);
                if (question.Length == 0)
                {
                    introspective = IsIntrospective(fusedOne);
                    reasons.Add(introspective ? "draft asked about the student's own thought process" : $"empty draft ({LiveModel!.ProviderId})");
                    continue;
                }
                author = new QuestionAuthorship(QuestionAuthorship.LiveModelDraft, [LiveModel!.ProviderId], [LiveModel.Model]);
            }
            else
            {
                if (single is { } failed) reasons.Add($"live model failed ({failed.Text}); panel drafted instead");
                var result = await panel.AskAsync(ask, "", Quorum.Plurality, ct);
                var fused = Clean(PanelText(result));
                question = Usable(fused, MaxQuestionWords);
                var how = QuestionAuthorship.PanelDraft;
                if (question.Length == 0)
                {
                    question = await PickDraftAsync(result, r.Decision.Move, dialogue, ct);
                    how = QuestionAuthorship.ChosenDraft;
                }
                if (question.Length == 0)
                {
                    // A draft asking how the student arrived at their answer is dropped before any guard call: introspective reports
                    // are unreliable evidence (GAD-T445; Nisbett and Wilson 1977), so it is redrafted, never asked.
                    introspective = IsIntrospective(fused) || result.IndividualVotes.Any(v => !v.IsError && IsIntrospective(Clean(v.Decision)));
                    var errors = result.IndividualVotes.Where(v => v.IsError).Select(v => $"; {v.ProviderId}: {v.ErrorMessage}");
                    reasons.Add(introspective ? "draft asked about the student's own thought process"
                        : $"empty draft ({(result.QuorumReached ? "quorum reached" : "no quorum")}{string.Concat(errors)})");
                    continue;
                }
                author = Author(result, question, how);
            }
            if (string.IsNullOrWhiteSpace(r.RubricForGuard))
            {
                LastOutcome = attempt == 0 ? "drafted" : "redrafted";
                LastGuard = LeakGuardResult.Skipped;
                LastAuthor = author;
                return question;
            }
            // The guard runs before the question is returned, so a leaking question never reaches the student (GAD-T92).
            var verdict = single is { Failed: false } ? await CheckLeakLiveAsync(question, r.RubricForGuard, dialogue, ct)
                : await CheckLeakAsync(question, r.RubricForGuard, dialogue, ct);
            if (verdict == false)
            {
                LastOutcome = attempt == 0 ? "drafted" : "redrafted";
                LastGuard = LeakGuardResult.Safe;
                LastAuthor = author;
                return question;
            }
            reasons.Add(verdict == true ? $"leaked: \"{question}\"" : "leak guard undecided");
            undecided = verdict is null;
            guarded = true;
            previous = verdict;
        }
        LastOutcome = "fallback: " + string.Join(" | ", reasons);
        // Only a guard verdict is reported as one: with no draft to check (or no rubric), no guard ran.
        LastGuard = !guarded ? LeakGuardResult.Skipped : undecided ? LeakGuardResult.UndecidedReplaced : LeakGuardResult.LeakedReplaced;
        LastAuthor = QuestionAuthorship.Fixed(QuestionAuthorship.FallbackText);
        (LastMove, var fallback) = Fallback(r, Language.Exam);
        return fallback;
    }

    /// <summary>
    /// Who wrote the question asked (GAD-T431): the voters whose own draft is the question, word for word after cleaning; when
    /// none is, the judge that synthesized it from the drafts. Recorded so self-preference can be tested: whether a grading member
    /// favours answers to questions its own provider family wrote (ElenchusAtScale Section 5.4.2, M12).
    /// </summary>
    private QuestionAuthorship Author(VotingResult result, string question, string how)
    {
        var drafters = result.IndividualVotes.Where(v => !v.IsError && string.Equals(Usable(Clean(v.Decision), MaxQuestionWords), question, StringComparison.Ordinal))
            .Select(v => v.ProviderId).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (drafters.Count == 0)
        {
            if (how == QuestionAuthorship.ChosenDraft) return new QuestionAuthorship(how, [], []);
            drafters = [JudgeProviderId];
            how = QuestionAuthorship.JudgeSynthesis;
        }
        var models = panel as IPanelModels;
        return new QuestionAuthorship(how, drafters, drafters.Select(p => models?.ConfiguredModels.GetValueOrDefault(p) ?? "").ToList());
    }

    /// <summary>What <see cref="LastOutcome"/> says when the question was the objective's fixed opening question (GAD-T436).</summary>
    public const string FixedOpeningOutcome = "fixed opening question, approved by the instructor and asked word for word";

    /// <summary>
    /// The objective's fixed opening question (GAD-T436) when <paramref name="r"/> plans its opening: an Elicit that is not a
    /// replacement, on an objective with an approved opening in the policy, and the first question asked on that objective.
    /// Null otherwise (the panel words the question).
    /// </summary>
    public static string? FixedOpening(QuestionRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.Decision.Move != MoveKind.Elicit || r.Decision.IsReplacement || r.Decision.ObjectiveKey is not { } key) return null;
        if (r.Session.Policy.OpeningQuestions is not { } fixedQuestions || !fixedQuestions.TryGetValue(key, out var text)
            || string.IsNullOrWhiteSpace(text)) return null;
        return ObjectiveAssessor.TurnsOn(r.Session, key).Any() ? null : text.Trim();
    }

    /// <summary>
    /// Learning Mode: the next rung of the objective's fixed hint ladder, then its fixed retry question (GAD-T271). The planner
    /// chose when; no model writes or rewords the text, so every student who needs help on an objective hears the same
    /// rungs in the same order and hint counts compare across students (research note 04). No leak guard: it teaches on purpose.
    /// </summary>
    private string Assist(QuestionRequest r)
    {
        LastGuard = LeakGuardResult.Skipped;
        if (r.Session.Policy.Mode != Gadfly.Assessment.AssessmentMode.Learning)
            throw new InvalidOperationException("Examination mode never teaches: hints and guided explanations are Learning Mode only.");
        var key = r.Decision.ObjectiveKey ?? r.Objective?.Key;
        var given = key is null ? 0 : ObjectiveAssessor.TurnsOn(r.Session, key).Count(t => MoveProfiles.IsAssistance(t.Move));
        var title = r.Objective?.Title ?? r.ObjectiveTitle ?? r.ConceptTitle;
        var rung = HintLadders.Rung(r.Objective, title, given, r.Decision.Move);
        LastOutcome = $"hint ladder rung {rung.Step} of {rung.Of} ({(rung.InstructorWritten ? "instructor-written" : "default ladder")}; learning mode, unguarded)";
        return rung.Text;
    }

    /// <summary>
    /// The leak guard on any examiner text (a question or a verification clarification): true when it states or
    /// strongly hints at expected answer points the student has not said, false when safe, null when the panel
    /// could not decide (callers fail closed, or flag the turn when the text was already shown).
    /// </summary>
    public async Task<bool?> CheckLeakAsync(string text, string rubric, string dialogue, CancellationToken ct = default)
    {
        var guard = ExamPrompts.Fill(_prompts[ExamPrompts.QuestionGuardKey], new Dictionary<string, string?>
        {
            ["rubric"] = rubric,
            ["dialogue"] = dialogue.Length == 0 ? "(none)" : dialogue,
            ["question"] = text,
        });
        var d = await panel.DecideAsync(guard, [Leaks, Safe], "", ct);
        if (string.Equals(d.Choice, Leaks, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(d.Choice, Safe, StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    /// <summary>
    /// The live turn's one fast model (GAD-T494). When set, it words the question and runs its leak guard in one call each,
    /// instead of the multi-model panel vote; the panel takes over for an attempt when it fails. Null keeps the panel (the CLI,
    /// reflection runs, tests).
    /// </summary>
    public ILiveTurnModel? LiveModel { get; set; }

    /// <summary>
    /// The attempt's languages (GAD-T535): the fallback questions and the rephrasing follow the exam language. The prompts themselves
    /// come in the language's prompt set (<see cref="ExamPromptLanguages"/>). English by default.
    /// </summary>
    public AttemptLanguage Language { get; set; } = AttemptLanguage.Default;

    /// <summary>
    /// "Say it another way" (GAD-T555; research note esl-and-korean R4.1): the examiner's question reworded. Not a prompt-set prompt:
    /// it is no part of how a student is examined or judged (it adds nothing, and is free and outside scoring), so it never changes
    /// the recorded prompt-set hash of any exam.
    /// </summary>
    public const string RephrasePrompt = """
        You are the examiner in a spoken oral exam. The student asked you to say your last question another way.
        Reword the question below so it is easier to understand: shorter sentences, common everyday words, the same technical terms,
        the same meaning and the same kind of answer wanted. Add no information, example, hint or answer point that the original
        question does not already contain, and do not make it easier to answer: only easier to understand.
        Write it in {language}.{plain}

        Conversation so far:
        {dialogue}

        The question to reword:
        {question}

        Reply with only the reworded question, as you would say it aloud.
        """;

    /// <summary>
    /// The question said another way (GAD-T555), through the same fail-closed leak guard as any question. Null when no rewording
    /// passed the guard (the caller repeats the question word for word instead). The live model words it when one is set, the
    /// panel otherwise.
    /// </summary>
    public async Task<string?> RephraseAsync(string question, string rubric, SocraticSession session, CancellationToken ct = default)
    {
        var dialogue = Dialogue(session);
        var prompt = ExamPrompts.Fill(RephrasePrompt, new Dictionary<string, string?>
        {
            ["language"] = ExamLanguage.EnglishName(Language.Exam) + (Language.IsKorean ? " (polite 해요체)" : Language.IsSpanish ? " (neutral Latin American Spanish, addressing the student as usted)" : ""),
            ["plain"] = Language.IsNonNative ? " Use at most about 20 words, no idioms, phrasal verbs or negative questions." : "",
            ["dialogue"] = dialogue.Length == 0 ? "(none)" : dialogue,
            ["question"] = question,
        });
        string draft;
        bool viaLive;
        if (LiveModel is { } live && await LiveDraftAsync(live, prompt, ct) is { Failed: false } drafted)
        {
            draft = Clean(drafted.Text);
            viaLive = true;
        }
        else
        {
            var result = await panel.AskAsync(prompt, "", Quorum.Plurality, ct);
            draft = Clean(PanelText(result));
            viaLive = false;
        }
        draft = Usable(draft, MaxQuestionWords);
        if (draft.Length == 0 || string.Equals(draft, question, StringComparison.Ordinal)) return null;
        if (string.IsNullOrWhiteSpace(rubric)) return draft;
        var verdict = viaLive ? await CheckLeakLiveAsync(draft, rubric, dialogue, ct) : await CheckLeakAsync(draft, rubric, dialogue, ct);
        return verdict == false ? draft : null;
    }

    /// <summary>The live model's system prompt: one spoken question, nothing around it.</summary>
    public const string LiveSystemPrompt =
        "You are the examiner in a spoken Socratic exam. Follow the instructions in the message and reply with only the one question to ask next, as you would say it aloud: no label, no quotes, no commentary.";

    /// <summary>The live guard's system prompt: a one-word verdict.</summary>
    public const string LiveGuardSystemPrompt =
        "You guard a spoken exam against questions that give the answer away. Reply with exactly one word: LEAKS or SAFE.";

    private sealed record LiveDraft(string Text, bool Failed);

    /// <summary>One draft from the live model; a provider failure is returned (not thrown) so the panel can draft instead.</summary>
    private static async Task<LiveDraft> LiveDraftAsync(ILiveTurnModel live, string prompt, CancellationToken ct)
    {
        try
        {
            return new LiveDraft(await live.CompleteAsync(LiveSystemPrompt, prompt, maxTokens: 300, temperature: 0.4, ct: ct), false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new LiveDraft($"{ex.GetType().Name}: {ex.Message}", true);
        }
    }

    /// <summary>The fewest words the first spoken part of a question may have (the same floor as <see cref="Usable"/>); shorter sentences wait for the next.</summary>
    public const int FirstPartMinWords = 4;

    /// <summary>
    /// What one streamed attempt came to (GAD-T495). <see cref="Question"/> is set when something was said (the parts, plus the plain
    /// fallback question when a later part failed); otherwise nothing was said and the caller redrafts or falls back as for any draft.
    /// </summary>
    private sealed record Streamed(string? Question, string Outcome, LeakGuardResult Guard = LeakGuardResult.Skipped, bool Failed = false,
        bool? Verdict = false, bool Introspective = false);

    /// <summary>
    /// One draft from the live model, spoken while it is written (GAD-T495). The reply is cleaned as <see cref="Clean"/> would (the
    /// first line only, without a label or wrapping quotes) and split into sentences as it arrives. Sentences are grouped into parts
    /// (the first part has at least <see cref="FirstPartMinWords"/> words), and every part is checked by the leak guard together with
    /// all the parts before it and the dialogue so far, so the guard always judges everything the student would have heard, ending with
    /// the whole question, the same check as before. Guards run while the model keeps writing; parts are said strictly in order, each
    /// only once its own guard said SAFE. The first part that leaks, is undecided, or is not usable (talks about "the student", asks
    /// about the student's own thought process) stops the reply: nothing after it is said. If nothing was said yet, the caller redrafts
    /// as for a one-shot draft; if something was, the plain fallback question for the move is said after it, and the question recorded
    /// is exactly what was said. A provider failure before anything was said hands the attempt to the panel. One question per turn
    /// (GAD-T815): the part that ends the reply's first question is cut there and nothing after it is read or said, and a reply that
    /// runs past <see cref="MaxQuestionWords"/> words before its question ends stops like a part that cannot be said.
    /// </summary>
    private async Task<Streamed> StreamDraftAsync(ILiveTurnModel live, string prompt, QuestionRequest r, string dialogue, IQuestionSpeech speech,
        CancellationToken ct)
    {
        var guardOn = !string.IsNullOrWhiteSpace(r.RubricForGuard);
        var move = r.Decision.Move;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);     // the model's reply
        using var guards = CancellationTokenSource.CreateLinkedTokenSource(ct);   // the parts' guards, stopped once a part fails
        var reply = new ReplyCleaner();
        var building = new List<string>();          // sentences not yet formed into a part
        var formed = new List<string>();            // every part formed so far, in order
        var verdicts = new List<Task<bool?>>();     // each formed part's guard
        var spoken = new List<string>();            // the parts said
        string? stopReason = null;
        bool? stopVerdict = false;
        var stopIntrospective = false;
        string? failure = null;
        var asked = false;                          // a formed part ended the first question: nothing after it is said (GAD-T815)

        void Form(bool final)
        {
            if (stopReason is not null || asked || building.Count == 0) return;
            var text = string.Join(" ", building);
            var words = LanguageText.WordCount(text);   // Korean words in English-word units (GAD-T537); English unchanged
            if (formed.Count == 0 && words < FirstPartMinWords && !final) return;   // too short to start on: wait for the next sentence
            building.Clear();
            // One question per turn (GAD-T815): the part that ends the first question is cut there and is the last part said.
            var before = string.Join(" ", formed);
            var offset = before.Length == 0 ? 0 : before.Length + 1;
            if (FirstQuestionEnd(before.Length == 0 ? text : before + " " + text) - offset is var end and >= 0)
            {
                text = text[..(end + 1)].TrimEnd();
                asked = true;
            }
            if (PartProblem(text, formed.Count == 0) is { } problem)
            {
                stopReason = problem;
                stopIntrospective = IsIntrospective(text);
                return;
            }
            if (LanguageText.WordCount(before.Length == 0 ? text : before + " " + text) > MaxQuestionWords)
            {
                stopReason = $"draft ran past {MaxQuestionWords} words";
                return;
            }
            formed.Add(text);
            var heard = string.Join(" ", formed);
            verdicts.Add(guardOn ? CheckLeakLiveAsync(heard, r.RubricForGuard, dialogue, guards.Token) : Task.FromResult<bool?>(false));
        }

        // Says every part whose guard has finished, in order; with wait, waits for each. False once a part stopped the reply.
        async Task<bool> SayReadyAsync(bool wait)
        {
            while (spoken.Count < verdicts.Count)
            {
                var guard = verdicts[spoken.Count];
                if (!wait && !guard.IsCompleted) return true;
                var verdict = await guard;
                if (verdict == false)
                {
                    speech.Say(formed[spoken.Count], move);
                    spoken.Add(formed[spoken.Count]);
                    continue;
                }
                stopReason = verdict == true ? $"leaked: \"{string.Join(" ", formed.Take(spoken.Count + 1))}\"" : "leak guard undecided";
                stopVerdict = verdict;
                return false;
            }
            return stopReason is null;
        }

        try
        {
            var e = live.StreamAsync(LiveSystemPrompt, prompt, maxTokens: 300, temperature: 0.4, ct: stop.Token).GetAsyncEnumerator(stop.Token);
            try
            {
                while (stopReason is null && !reply.LineEnded && !asked)
                {
                    bool more;
                    try
                    {
                        more = await e.MoveNextAsync();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        failure = $"{ex.GetType().Name}: {ex.Message}";
                        break;
                    }
                    if (!more) break;
                    building.AddRange(reply.Add(e.Current));
                    Form(final: false);
                    if (!await SayReadyAsync(wait: false)) break;
                }
            }
            finally
            {
                stop.Cancel();   // a reply cut short (a stop, a second line) is not read any further
                try { await e.DisposeAsync(); } catch (Exception) { /* the provider's own teardown */ }
            }
            if (failure is null && stopReason is null && !asked)
            {
                building.AddRange(reply.Finish());
                Form(final: true);
            }
            // A provider failure says nothing more (the panel takes over, or the fallback follows what was said); otherwise every part
            // formed is said in order as its guard passes, up to the first one that does not.
            if (failure is null) await SayReadyAsync(wait: true);
        }
        catch (Exception) when (spoken.Count > 0)
        {
            speech.Abandon(ct.IsCancellationRequested ? "cancelled" : "failed");
            throw;
        }
        finally
        {
            guards.Cancel();   // guards of parts that will never be said
            foreach (var v in verdicts) _ = v.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
        }

        if (spoken.Count == 0)
        {
            if (failure is not null) return new Streamed(null, failure, Failed: true);
            return stopReason is not null
                ? new Streamed(null, stopReason, Verdict: stopVerdict, Introspective: stopIntrospective)
                : new Streamed(null, $"empty draft ({live.ProviderId})");
        }
        var reason = stopReason ?? (failure is not null ? $"live model failed mid-question ({failure})" : null);
        if (reason is null)
        {
            var whole = string.Join(" ", spoken);
            speech.Finish(whole);
            return new Streamed(whole, $"drafted (spoken as it was written, {spoken.Count} part{(spoken.Count == 1 ? "" : "s")})",
                guardOn ? LeakGuardResult.Safe : LeakGuardResult.Skipped);
        }
        // Something was said already: what failed is never said, and the plain question for the move follows what was.
        (LastMove, var fallback) = Fallback(r, Language.Exam);
        speech.Say(fallback, LastMove);
        var question = string.Join(" ", spoken.Append(fallback));
        speech.Finish(question);
        var guardResult = stopVerdict == true ? LeakGuardResult.LeakedReplaced : stopVerdict is null ? LeakGuardResult.UndecidedReplaced
            : guardOn ? LeakGuardResult.Safe : LeakGuardResult.Skipped;
        return new Streamed(question, $"spoke {spoken.Count} part(s) as written, then the fallback question: {reason}", guardResult);
    }

    /// <summary>Why a part of a streamed question can't be said, or null when it can (the per-part form of <see cref="Usable"/>).</summary>
    internal static string? PartProblem(string part, bool first)
    {
        if (string.IsNullOrWhiteSpace(part)) return "empty draft";
        if (first && LanguageText.WordCount(part) < FirstPartMinWords) return "empty draft (too short)";
        if (first && Enum.TryParse<MoveKind>(part.Trim('.', '?', ' '), ignoreCase: true, out _)) return "empty draft (a move name)";
        if (part.Contains("the student", StringComparison.OrdinalIgnoreCase)) return "draft talked about the student instead of to them";
        if (IsIntrospective(part)) return "draft asked about the student's own thought process";
        return null;
    }

    /// <summary>
    /// Cleans a reply as it streams, the way <see cref="Clean"/> cleans a whole one (GAD-T495): the first non-empty line only, without
    /// a leading label ("Question:", "Examiner:", "Q:") or wrapping quotes and asterisks; returns its whole sentences as they complete.
    /// </summary>
    internal sealed class ReplyCleaner
    {
        private const int DecideAfter = 12;   // characters seen before a leading label can be ruled out
        private readonly System.Text.StringBuilder _head = new();
        private readonly SentenceSplitter _splitter = new();
        private bool _started, _wrapped;

        /// <summary>True once the first line has ended: the rest of the reply is not part of the question.</summary>
        public bool LineEnded { get; private set; }

        public IReadOnlyList<string> Add(string? delta)
        {
            if (LineEnded || string.IsNullOrEmpty(delta)) return [];
            string text;
            if (!_started)
            {
                _head.Append(delta);
                var head = _head.ToString().TrimStart();
                if (head.Length < DecideAfter && !head.Contains('\n')) return [];
                text = Start(head);
            }
            else text = delta;
            return Take(text);
        }

        /// <summary>The reply has ended: the sentences still held, the last one included.</summary>
        public IReadOnlyList<string> Finish()
        {
            var done = new List<string>();
            if (!_started && !LineEnded) done.AddRange(Take(Start(_head.ToString().TrimStart())));
            var rest = Tidy(_splitter.Flush());
            if (rest.Length > 0) done.Add(rest);
            return done;
        }

        private string Start(string head)
        {
            _started = true;
            foreach (var label in Labels)
                if (head.StartsWith(label, StringComparison.OrdinalIgnoreCase)) head = head[label.Length..].TrimStart();
            var unwrapped = head.TrimStart('"', '“', '*');
            _wrapped = unwrapped.Length < head.Length && head[0] != '*';
            return unwrapped;
        }

        private IReadOnlyList<string> Take(string text)
        {
            var nl = text.IndexOf('\n');
            if (nl >= 0)
            {
                text = text[..nl];
                LineEnded = true;
            }
            var sentences = _splitter.Add(text).Select(Tidy).Where(s => s.Length > 0).ToList();
            if (LineEnded && Tidy(_splitter.Flush()) is { Length: > 0 } last) sentences.Add(last);
            return sentences;
        }

        private string Tidy(string sentence)
        {
            var s = sentence.Trim().Trim('*').Trim();
            // The closing quote of a reply wrapped in quotes (an odd quote left at the end of a sentence).
            if (_wrapped && s.Length > 0 && s[^1] is '"' or '”' && s.Count(c => c is '"' or '“' or '”') % 2 == 1) s = s[..^1].TrimEnd();
            return s;
        }
    }

    /// <summary>
    /// The leak guard on the live model (GAD-T494): the same guard prompt, one model, one word back. It fails closed like the panel
    /// guard: a reply that is neither LEAKS nor SAFE is undecided (null). When the live model fails outright, the panel decides.
    /// </summary>
    private async Task<bool?> CheckLeakLiveAsync(string text, string rubric, string dialogue, CancellationToken ct)
    {
        var guard = ExamPrompts.Fill(_prompts[ExamPrompts.QuestionGuardKey], new Dictionary<string, string?>
        {
            ["rubric"] = rubric,
            ["dialogue"] = dialogue.Length == 0 ? "(none)" : dialogue,
            ["question"] = text,
        }) + "\n\nReply with exactly one word: LEAKS or SAFE.";
        string reply;
        try
        {
            reply = await LiveModel!.CompleteAsync(LiveGuardSystemPrompt, guard, maxTokens: 5, temperature: 0, ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return await CheckLeakAsync(text, rubric, dialogue, ct);
        }
        return ParseGuard(reply);
    }

    /// <summary>A one-word guard reply: true LEAKS, false SAFE, null anything else (undecided, so the caller fails closed).</summary>
    internal static bool? ParseGuard(string? reply)
    {
        var word = (reply ?? "").Trim().Trim('"', '\'', '*', '.', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        word = word.Trim('"', '\'', '*', '.', ',', ':');
        if (string.Equals(word, Leaks, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(word, Safe, StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    /// <summary>
    /// Free-form drafts rarely match word for word, so a plurality often has no winner and Legion
    /// returns no consensus. Then the drafts themselves become the ballot: the panel picks the
    /// sharpest one. A single draft is used as is.
    /// </summary>
    private async Task<string> PickDraftAsync(VotingResult result, MoveKind move, string dialogue, CancellationToken ct)
    {
        var drafts = result.IndividualVotes.Where(v => !v.IsError).Select(v => Usable(Clean(v.Decision), MaxQuestionWords))
            .Where(d => d.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (drafts.Count <= 1) return drafts.FirstOrDefault() ?? "";
        var ballot = $"""
            Which of these is the sharpest next question for a {move} move in a spoken Socratic exam?
            Prefer the one that builds most directly on the student's own words, is answerable aloud,
            and gives nothing away.

            Conversation so far:
            {(dialogue.Length == 0 ? "(this is the first question)" : dialogue)}
            """;
        var d = await panel.DecideAsync(ballot, drafts, "", ct);
        return drafts.FirstOrDefault(x => string.Equals(x, d.Choice, StringComparison.Ordinal)) ?? drafts[0];
    }

    /// <summary>The recent dialogue as prompt text, student answers fenced.</summary>
    public string Dialogue(SocraticSession s) =>
        string.Join("\n", s.Turns.TakeLast(DialogueTurns).SelectMany(t => t.Answer is null
            ? new[] { $"Examiner: {t.Question}" }
            : new[] { $"Examiner: {t.Question}", $"Student: {ExamPrompts.Student(t.Answer.Text)}" }));

    /// <summary>Labels a model may put before the question ("질문:" and "시험관:" in a Korean exam, GAD-T535; "Pregunta:" in Spanish, GAD-T610).</summary>
    private static readonly string[] Labels = ["Question:", "Examiner:", "Q:", "질문:", "시험관:", "Pregunta:", "Examinador:", "Examinadora:", "P:"];

    /// <summary>Strips quotes, labels and extra lines a model may wrap the question in.</summary>
    internal static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var line = raw.Trim().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        foreach (var label in Labels)
            if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase)) line = line[label.Length..].Trim();
        return line.Trim('"', '“', '”', '*', ' ');
    }

    /// <summary>
    /// The panel's fused text, or "" when the panel failed (GAD-T117): no voter answered, no quorum on the
    /// consensus, or the text is Legion's own status/error wording ("All voters failed."). Provider error text
    /// must never reach a student; an empty draft sends the generator down its redraft/fallback path.
    /// </summary>
    internal static string PanelText(VotingResult result)
    {
        if (!result.IndividualVotes.Any(v => !v.IsError)) return "";
        var text = result.QuorumReached && !string.IsNullOrWhiteSpace(result.Consensus) ? result.Consensus : result.NarrativeSummary;
        return string.IsNullOrWhiteSpace(text) || IsPanelFailureText(text) ? "" : text;
    }

    /// <summary>True for Legion status and error strings rather than model output.</summary>
    internal static bool IsPanelFailureText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        // A real question mentions "exception" or "error" when the subject is exception handling or errors; Legion's
        // status and error strings are never phrased as a question (GAD-T188).
        if (t.EndsWith('?')) return false;
        return t.Contains("voters failed", StringComparison.OrdinalIgnoreCase)
            || t.Contains("no quorum", StringComparison.OrdinalIgnoreCase)
            || t.Contains("quorum not reached", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("error", StringComparison.OrdinalIgnoreCase)
            || t.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || t.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || t.Contains("api key", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The draft up to the end of its first question (GAD-T815): the text before it (a situation, the quoted claims) stays, anything
    /// after it is dropped. A draft with no question mark is returned whole.
    /// </summary>
    internal static string FirstQuestion(string? draft)
    {
        if (string.IsNullOrEmpty(draft)) return "";
        var end = FirstQuestionEnd(draft);
        return end < 0 ? draft : draft[..(end + 1)].TrimEnd();
    }

    /// <summary>
    /// Where the first question in <paramref name="text"/> ends (the index of its last question mark), or -1: the first ASCII question
    /// mark or full-width U+FF1F (Korean), which is also the closing mark of a Spanish ¿...? pair. A question mark inside a quotation
    /// (“...”, «...», 「...」, or one followed straight by a closing ") is the quoted words, not the examiner's question.
    /// </summary>
    private static int FirstQuestionEnd(string text)
    {
        var quoted = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '“' or '«' or '「' or '『') quoted++;
            else if (c is '”' or '»' or '」' or '』') quoted = Math.Max(0, quoted - 1);
            else if (c is '?' or '？')
            {
                var end = i;
                while (end + 1 < text.Length && text[end + 1] is '?' or '？' or '!') end++;
                if (quoted > 0 || (end + 1 < text.Length && text[end + 1] == '"')) { i = end; continue; }
                return end;
            }
        }
        return -1;
    }

    /// <summary>The default for <see cref="MaxQuestionWords"/>.</summary>
    public const int DefaultMaxQuestionWords = 50;

    /// <summary>
    /// The most words a drafted question may run to, up to the end of its first question (GAD-T815). The prompt asks for at most 40;
    /// a draft well past that is redrafted.
    /// </summary>
    public int MaxQuestionWords { get; init; } = DefaultMaxQuestionWords;

    /// <summary>
    /// The draft when it reads as one question, else "": cut after its first question (<see cref="FirstQuestion"/>); a reply that is
    /// only a label or the move's name (a panel sometimes answers "ScenarioVariation"), a fragment of a few words, or a question longer
    /// than <paramref name="maxWords"/> words is not asked.
    /// </summary>
    internal static string Usable(string draft, int maxWords = DefaultMaxQuestionWords)
    {
        // One question per turn (GAD-T815; ElenchusAtScale 3.1, 3.2, 4.3.4): a second question is cut off, and a first question that
        // runs well past the prompt's 40 words is redrafted like an empty draft.
        draft = FirstQuestion(draft);
        var words = LanguageText.WordCount(draft);   // Korean words in English-word units (GAD-T537); English unchanged
        if (words < 4 || words > maxWords) return "";
        if (Enum.TryParse<MoveKind>(draft.Trim('.', '?', ' '), ignoreCase: true, out _)) return "";
        if (IsPanelFailureText(draft)) return "";
        // A draft that talks about the student instead of to them ("...and ask the student to explain") is an instruction, not a question.
        if (draft.Contains("the student", StringComparison.OrdinalIgnoreCase)) return "";
        // A question about the student's own mental process asks for a report people cannot reliably give (GAD-T445).
        if (IsIntrospective(draft)) return "";
        return draft;
    }

    /// <summary>
    /// True when a question asks the student to report their own mental process ("how did you arrive at that?", "what were you
    /// thinking?", "what went through your mind?") rather than the reasons for a claim. Such reports are unreliable (Nisbett and
    /// Wilson 1977; Ericsson and Simon 1980), so the examiner never asks them (GAD-T445). "Why is that so?" and "how do you know?"
    /// ask for a justification and stay allowed.
    /// </summary>
    public static bool IsIntrospective(string? question) => !string.IsNullOrWhiteSpace(question) && Introspection().IsMatch(question);

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\bhow\s+did\s+you\s+(come\s+up\s+with|arrive\s+at|get\s+to|end\s+up\s+with|figure|work\s+(that|this|it)\s+out|work\s+out|reach|decide|land\s+on|think\s+of|go\s+about)\b"
        + @"|\bwhat\s+(made|led|prompted|caused)\s+you\s+to\s+(think|say|decide|conclude|choose|answer|believe)\b"
        + @"|\bwhat\s+(made|led)\s+you\s+(think|say|decide|conclude|choose|believe)\b"
        + @"|\bwhat\s+(were|are)\s+you\s+thinking\b"
        + @"|\bwhat\s+(went|was\s+going|goes)\s+through\s+your\s+(mind|head)\b"
        + @"|\b(your|the)\s+thought\s+process\b|\byour\s+thinking\s+process\b"
        + @"|\bhow\s+were\s+you\s+thinking\b|\bwhat\s+was\s+in\s+your\s+(mind|head)\b"
        + @"|\bwalk\s+me\s+through\s+(how\s+you\s+(thought|got|arrived|figured)|your\s+(thinking|thoughts))\b"
        // Spanish (GAD-T610): "¿cómo llegó a eso?", "¿qué le hizo pensar...?", "¿qué estaba pensando?", "su proceso de pensamiento".
        + @"|\bc[óo]mo\s+lleg[óo]\s+a\b|\bqu[ée]\s+le\s+(hizo|llev[óo]\s+a)\s+(pensar|decir|decidir|concluir|elegir|creer)\b"
        + @"|\bqu[ée]\s+estaba\s+pensando\b|\bqu[ée]\s+(le\s+)?pas[óo]\s+por\s+la\s+(mente|cabeza)\b|\bsu\s+proceso\s+de\s+pensamiento\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex Introspection();

    /// <summary>
    /// The fixed fallback question for <paramref name="r"/>'s move and the move it actually asks (GAD-T814). A Contradiction quotes
    /// the two claims it confronts from the ledger (the student's own words, so nothing can leak); see <see cref="FallbackMove"/>.
    /// </summary>
    internal static (MoveKind Move, string Question) Fallback(QuestionRequest r, string? language)
    {
        var quotes = ContradictionQuotes(r);
        return (FallbackMove(r.Decision.Move, quotes is not null), Fallback(r.Decision.Move, r.ConceptTitle, r.RelatedConceptTitle, language, quotes));
    }

    /// <summary>The first two target claims' quotes for a Contradiction; null for another move or when the ledger has fewer.</summary>
    private static (string First, string Second)? ContradictionQuotes(QuestionRequest r)
    {
        if (r.Decision.Move != MoveKind.Contradiction) return null;
        var quotes = r.Decision.TargetClaimIds.Select(id => r.Ledger.Find(id)?.Quote?.Trim()).Where(q => !string.IsNullOrEmpty(q)).Take(2).ToList();
        return quotes.Count == 2 ? (quotes[0]!, quotes[1]!) : null;
    }

    /// <summary>
    /// The move a fixed fallback question asks for <paramref name="planned"/> (GAD-T814). A Counterexample needs a case and an Apply
    /// a situation new to the student, which only the examiner can set (ElenchusAtScale 3.2, M5), so their fallback asks the nearest
    /// move that needs neither: assumption testing and an independent explanation. A Contradiction counts only once both claims are
    /// put to the student (Proposition 3.2, M7); without their quotes it tests the assumptions instead. The turn records this move,
    /// so its probed level and dimensions match the question asked and no fallback is credited as transfer.
    /// </summary>
    internal static MoveKind FallbackMove(MoveKind planned, bool quotesBothClaims = false) => planned switch
    {
        MoveKind.Counterexample => MoveKind.AssumptionTesting,
        MoveKind.Apply => MoveKind.IndependentExplanation,
        MoveKind.Contradiction when !quotesBothClaims => MoveKind.AssumptionTesting,
        _ => planned,
    };

    /// <summary>
    /// The plain fallback question for <paramref name="move"/>, worded for the move <see cref="FallbackMove"/> asks. None is yes/no,
    /// presupposes an answer point, or asks about the student's own thought process (ElenchusAtScale 2.2.2, 2.3.1, 2.3.2, 3.3; M3),
    /// and each is one question (GAD-T815) of at most 40 words besides the quoted claims.
    /// </summary>
    internal static string Fallback(MoveKind move, string concept, string? related, string? language = null, (string First, string Second)? quotes = null)
    {
        var asked = FallbackMove(move, quotes is not null);
        return ExamLanguage.IsKorean(language) ? KoreanFallback(asked, concept, related, quotes)
            : ExamLanguage.IsSpanish(language) ? SpanishFallback(asked, concept, related, quotes) : asked switch
        {
            MoveKind.Clarify => $"When you talk about {concept}, what exactly do you mean, in your own words?",
            MoveKind.Probe => $"Why does what you just said about {concept} hold, and what shows that it is so?",
            MoveKind.Contradiction => $"You said \"{quotes!.Value.First}\", and you also said \"{quotes.Value.Second}\". How do those two fit together, or which one would you change?",
            MoveKind.Synthesize => $"What connection, if any, is there between {concept} and {related ?? "the other ideas we have discussed"}, and why?",
            MoveKind.CausalReasoning => $"In what you said about {concept}, what depends on what, and what shows that it is so?",
            MoveKind.ScenarioVariation => "Suppose one of the conditions you described changed while everything else stayed the same. What would happen then, and why?",
            MoveKind.AssumptionTesting => $"What has to be true for your explanation of {concept} to hold, and what would follow if one of those things were false?",
            MoveKind.IndependentExplanation => $"Without using a textbook definition, how would you explain {concept} from your own reasoning?",
            MoveKind.Hint => $"Here is a hint: think about what {concept} needs in order to happen. With that in mind, how would you explain it now?",
            MoveKind.GuidedExplanation => $"Let's look at {concept} step by step, starting from what you already said. Which step comes first, and why?",
            _ => $"In your own words, what is {concept}?",
        };
    }

    /// <summary>
    /// The plain fallback questions in Spanish (GAD-T610): neutral Latin American Spanish, usted, no gendered wording, worded so any
    /// concept title (English or Spanish) fits.
    /// </summary>
    private static string SpanishFallback(MoveKind move, string concept, string? related, (string First, string Second)? quotes) => move switch
    {
        MoveKind.Clarify => $"Cuando habla de {concept}, ¿qué quiere decir exactamente, con sus propias palabras?",
        MoveKind.Probe => $"¿Por qué se sostiene lo que acaba de decir sobre {concept}, y qué muestra que es así?",
        MoveKind.Contradiction => $"Usted dijo \"{quotes!.Value.First}\" y también dijo \"{quotes.Value.Second}\". ¿Cómo encajan esas dos cosas, o cuál de ellas cambiaría?",
        MoveKind.Synthesize => $"¿Qué relación hay, si la hay, entre {concept} y {related ?? "las otras ideas que hemos comentado"}, y por qué?",
        MoveKind.CausalReasoning => $"En lo que dijo sobre {concept}, ¿qué depende de qué, y qué muestra que es así?",
        MoveKind.ScenarioVariation => "Supongamos que cambia una de las condiciones que describió y todo lo demás sigue igual. ¿Qué pasaría entonces y por qué?",
        MoveKind.AssumptionTesting => $"¿Qué tiene que ser cierto para que su explicación de {concept} se sostenga, y qué pasaría si una de esas cosas fuera falsa?",
        MoveKind.IndependentExplanation => $"Sin usar una definición de libro, ¿cómo explicaría {concept} a partir de su propio razonamiento?",
        MoveKind.Hint => $"Le doy una pista: piense en qué necesita {concept} para ocurrir. Con eso en mente, ¿cómo lo explicaría ahora?",
        MoveKind.GuidedExplanation => $"Veamos {concept} paso a paso, a partir de lo que ya dijo. ¿Qué paso va primero y por qué?",
        _ => $"Con sus propias palabras, ¿qué es {concept}?",
    };

    /// <summary>The plain fallback questions in Korean (GAD-T535), polite 해요체, worded so any concept title (English or Korean) fits.</summary>
    private static string KoreanFallback(MoveKind move, string concept, string? related, (string First, string Second)? quotes) => move switch
    {
        MoveKind.Clarify => $"{concept}에 대해 말씀하실 때, 자신의 말로 하면 정확히 어떤 뜻인가요?",
        MoveKind.Probe => $"방금 {concept}에 대해 하신 말씀은 왜 성립하고, 무엇이 그렇다는 것을 보여 주나요?",
        MoveKind.Contradiction => $"앞에서 \"{quotes!.Value.First}\"라고 하셨고, \"{quotes.Value.Second}\"라고도 하셨어요. 이 두 말씀은 서로 어떻게 맞아떨어지나요, 아니면 어느 쪽을 고치시겠어요?",
        MoveKind.Synthesize => $"{concept}과(와) {related ?? "지금까지 이야기한 다른 개념들"} 사이에 관계가 있다면 그것은 무엇이고, 왜 그런가요?",
        MoveKind.CausalReasoning => $"{concept}에 대해 하신 설명에서 무엇이 무엇에 달려 있고, 무엇이 그렇다는 것을 보여 주나요?",
        MoveKind.ScenarioVariation => "말씀하신 조건 중 하나만 바뀌고 나머지는 그대로라고 해 볼게요. 그러면 어떻게 되고, 왜 그럴까요?",
        MoveKind.AssumptionTesting => $"{concept}에 대한 설명이 성립하려면 무엇이 사실이어야 하고, 그중 하나가 사실이 아니라면 어떻게 될까요?",
        MoveKind.IndependentExplanation => $"교과서 정의를 쓰지 않고, {concept}에 대해 자신의 생각으로 어떻게 설명하시겠어요?",
        MoveKind.Hint => $"힌트를 드릴게요. {concept}에 무엇이 필요한지 생각해 보세요. 그렇다면 이제 어떻게 설명하시겠어요?",
        MoveKind.GuidedExplanation => $"이미 말씀하신 내용에서 시작해서 {concept}에 대해 한 단계씩 살펴볼게요. 어떤 단계가 먼저 오고, 왜 그런가요?",
        _ => $"{concept}은(는) 자신의 말로 하면 무엇인가요?",
    };
}
