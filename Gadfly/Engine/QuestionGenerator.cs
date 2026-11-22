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
    private readonly PromptSet _prompts = prompts ?? ExamPromptsV3.Set;

    public const string Leaks = "LEAKS";
    public const string Safe = "SAFE";

    /// <summary>How the last question was produced: "drafted", "redrafted", or "fallback: ..." with the reason.</summary>
    public string LastOutcome { get; private set; } = "";

    /// <summary>What the leak guard decided about the last question.</summary>
    public LeakGuardResult LastGuard { get; private set; } = LeakGuardResult.Skipped;

    /// <summary>Who wrote the last question (GAD-T431): the drafting provider(s) and their models, or a fixed source.</summary>
    public QuestionAuthorship? LastAuthor { get; private set; }

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
                question = Usable(fusedOne);
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
                question = Usable(fused);
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
        return Fallback(r.Decision.Move, r.ConceptTitle, r.RelatedConceptTitle);
    }

    /// <summary>
    /// Who wrote the question asked (GAD-T431): the voters whose own draft is the question, word for word after cleaning; when
    /// none is, the judge that synthesized it from the drafts. Recorded so self-preference can be tested: whether a grading member
    /// favours answers to questions its own provider family wrote (ElenchusAtScale Section 5.4.2, M12).
    /// </summary>
    private QuestionAuthorship Author(VotingResult result, string question, string how)
    {
        var drafters = result.IndividualVotes.Where(v => !v.IsError && string.Equals(Usable(Clean(v.Decision)), question, StringComparison.Ordinal))
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
    /// is exactly what was said. A provider failure before anything was said hands the attempt to the panel.
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

        void Form(bool final)
        {
            if (stopReason is not null || building.Count == 0) return;
            var text = string.Join(" ", building);
            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            if (formed.Count == 0 && words < FirstPartMinWords && !final) return;   // too short to start on: wait for the next sentence
            building.Clear();
            if (PartProblem(text, formed.Count == 0) is { } problem)
            {
                stopReason = problem;
                stopIntrospective = IsIntrospective(text);
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
                while (stopReason is null && !reply.LineEnded)
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
            if (failure is null && stopReason is null)
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
        var fallback = Fallback(move, r.ConceptTitle, r.RelatedConceptTitle);
        speech.Say(fallback, move);
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
        if (first && part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < FirstPartMinWords) return "empty draft (too short)";
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
            foreach (var label in new[] { "Question:", "Examiner:", "Q:" })
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
        var drafts = result.IndividualVotes.Where(v => !v.IsError).Select(v => Usable(Clean(v.Decision)))
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

    /// <summary>Strips quotes, labels and extra lines a model may wrap the question in.</summary>
    internal static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var line = raw.Trim().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        foreach (var label in new[] { "Question:", "Examiner:", "Q:" })
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
    /// The draft when it reads as a question, else "": a reply that is only a label or the move's name (a panel
    /// sometimes answers "ScenarioVariation") or a fragment of a few words is not asked.
    /// </summary>
    internal static string Usable(string draft)
    {
        var words = draft.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4) return "";
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
        + @"|\bwalk\s+me\s+through\s+(how\s+you\s+(thought|got|arrived|figured)|your\s+(thinking|thoughts))\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex Introspection();

    internal static string Fallback(MoveKind move, string concept, string? related) => move switch
    {
        MoveKind.Clarify => $"Could you say more precisely what you mean, in your own words, when you talk about {concept}?",
        MoveKind.Probe => "Why do you think that is true? Walk me through your reasoning.",
        MoveKind.Counterexample => $"Can you think of a case where your explanation of {concept} would not hold? What would happen then?",
        MoveKind.Contradiction => "Earlier you said something that seems to conflict with what you just said. How do both fit together?",
        MoveKind.Apply => $"Imagine a situation you haven't seen before where {concept} matters. What would you expect to happen, and why?",
        MoveKind.Synthesize => $"How does {concept} relate to {related ?? "the other ideas we have discussed"}?",
        MoveKind.CausalReasoning => "What do you think causes that to happen? Explain the chain of cause and effect.",
        MoveKind.ScenarioVariation => "Suppose one of the conditions in that situation changed while everything else stayed the same. What would change, and why?",
        MoveKind.AssumptionTesting => "What has to be true for your explanation to hold? Which of those assumptions could fail?",
        MoveKind.IndependentExplanation => $"Without using a textbook definition, how would you explain {concept} from your own reasoning?",
        MoveKind.Hint => $"Here is a hint: think about what {concept} needs in order to happen. With that in mind, how would you explain it now?",
        MoveKind.GuidedExplanation => $"Let's look at {concept} step by step, starting from what you already said. Which step do you think comes first, and why?",
        _ => $"In your own words, what is {concept}?",
    };
}
