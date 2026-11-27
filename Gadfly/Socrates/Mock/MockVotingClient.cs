using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MindAttic.Legion;
using static Gadfly.Socrates.Mock.MockPromptReader;

namespace Gadfly.Socrates.Mock;

/// <summary>
/// The clearly labelled mock panel (docs/MASTER-PLAN.md Part 22, GAD-T100): a deterministic stand-in for the
/// Legion panel so the whole application workflow can run without any provider key. It recognizes each exam
/// prompt (question, leak guard, judge, claims, contradiction, aporia, concept map, rubric, text style,
/// verification, clarification, objective grading) and answers it with simple, content-aware rules: questions
/// are templates built from the concept title and the student's own words, scores come from keyword overlap
/// between the answer and the rubric, so a good answer scores higher than a poor one.
/// <para>
/// Nothing here is an AI evaluation. Every reply comes from provider <see cref="ProviderId"/> ("mock"), every
/// rationale, verdict and explanation a reviewer can see starts with <see cref="Marker"/>, and the host shows a
/// MOCK banner on every screen. Student-facing text (questions) carries no marker: the banner labels the session.
/// </para>
/// </summary>
public sealed partial class MockVotingClient : ISocraticVotingClient, IPanelInfo, IPanelModels
{
    public const string ProviderId = "mock";
    public const string ModelName = "gadfly-mock-1";
    public const string Marker = "[MOCK]";

    /// <summary>The mock's one member (GAD-T429): a calibration recorded against it never carries over to a real panel.</summary>
    public IReadOnlyDictionary<string, string> ConfiguredModels { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderId] = ModelName };

    public bool IsMock => true;
    public string Mode => PanelModes.Mock;
    public string Reason { get; init; } = "Gadfly:AiMode is \"mock\".";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    // ------------------------------------------------------------------ ISocraticVotingClient

    public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (answer, why) = Ask(question);
        return Task.FromResult(new VotingResult
        {
            Question = question,
            QuorumType = quorum,
            QuorumReached = true,
            Consensus = answer,
            IndividualVotes = [Vote(answer, why)],
        });
    }

    public Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var votes = voters.Select(v => new VoteResult
        {
            VoterName = v.Name,
            ProviderId = ProviderId,
            Decision = $"{Marker} {v.Name}: no perspective offered in mock mode.",
            Reasoning = $"{Marker} canned reply",
        }).ToList();
        return Task.FromResult(new VotingResult { Question = question, QuorumReached = true, IndividualVotes = votes });
    }

    public Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (choice, why) = Decide(question, options);
        return Task.FromResult(new DecisionResult
        {
            Question = question,
            Options = options.ToList(),
            Choice = choice,
            QuorumReached = choice.Length > 0,
            Reasoning = $"{Marker} {why}",
        });
    }

    public Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096,
        double temperature = 0.2, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var text = FanOut(userMessage);
        return Task.FromResult<IReadOnlyList<PanelReply>>([new PanelReply(ProviderId, text, Model: ModelName)]);
    }

    private static VoteResult Vote(string decision, string why) => new()
    {
        VoterName = "Mock panel",
        ProviderId = ProviderId,
        Decision = decision,
        Reasoning = $"{Marker} {why}",
    };

    // ------------------------------------------------------------------ routing by prompt

    internal static (string Text, string Why) Ask(string prompt)
    {
        if (prompt.Contains("Write the next question for this move:", StringComparison.Ordinal)) return (Question(prompt), "template question for the planned move");
        if (prompt.Contains("You are a Socratic tutor in learning mode", StringComparison.Ordinal)) return (LearningTurn(prompt), "template learning-mode hint");
        if (prompt.Contains("You are one analyst on a panel examining", StringComparison.Ordinal)) return (Analysis(prompt), "keyword evidence analysis");
        if (prompt.Contains("Extract the factual or conceptual claims", StringComparison.Ordinal)) return (Claims(prompt), "one claim per asserted sentence");
        if (prompt.Contains("You write one neutral clarification question", StringComparison.Ordinal)) return (Clarification(prompt), "neutral clarification template");
        if (prompt.Contains("The student asked you to say your last question another way", StringComparison.Ordinal))
        {
            // "Say it another way" (GAD-T555): the same question behind a plain lead-in; it adds nothing, so it cannot leak.
            var question = Section(prompt, "The question to reword:", "Reply with only").Trim();
            return (prompt.Contains("Write it in Korean", StringComparison.Ordinal) ? $"다시 말씀드리면, {question}"
                : prompt.Contains("Write it in Spanish", StringComparison.Ordinal) ? $"Dicho de otra manera: {question}" : $"Put another way: {question}",
                "the question behind a plain lead-in");
        }
        return ($"{Marker} The mock panel has no reply for this request.", "unrecognized prompt");
    }

    internal static (string Choice, string Why) Decide(string prompt, IReadOnlyList<string> options)
    {
        string Pick(string wanted) => options.FirstOrDefault(o => o.Equals(wanted, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault() ?? "";

        if (prompt.Contains("LEAKS or SAFE", StringComparison.Ordinal))
            return (Pick("SAFE"), "mock questions are templates that state no rubric point");
        if (prompt.Contains("CONTRADICT if both", StringComparison.Ordinal))
        {
            var first = StudentAfter(prompt, "Earlier claim");
            var second = StudentAfter(prompt, "Later claim");
            var contradict = HasNegation(first) != HasNegation(second) && Jaccard(WithoutNegation(first), WithoutNegation(second)) >= 0.5;
            return (Pick(contradict ? "CONTRADICT" : "CONSISTENT"), contradict ? "same statement, one side negated" : "no negated restatement");
        }
        if (prompt.Contains("APORIA if the student's answer", StringComparison.Ordinal))
        {
            var answer = StudentAfter(prompt, "Answer:");
            return (Pick(SaysDontKnow(answer) ? "APORIA" : "ANSWER"), "phrase match for \"I don't know\"");
        }
        // The ballot over several drafts (only one draft exists in mock mode) and anything else: the first option.
        return (options.FirstOrDefault() ?? "", "first option");
    }

    /// <summary>
    /// A single model's reply to <paramref name="prompt"/> (the live-turn model in mock mode, GAD-T494): the leak guard answers SAFE
    /// (mock questions are templates that state no rubric point), anything else gets the panel's own mock reply.
    /// </summary>
    public static string Complete(string prompt) =>
        prompt.Contains("LEAKS or SAFE", StringComparison.Ordinal) ? "SAFE"
        // The live turn's provisional judgment (GAD-T508) sends the judge prompt to the one fast model: the same simulated verdict.
        : prompt.Contains("You are one judge on a panel grading", StringComparison.Ordinal) ? Judge(prompt)
        : Ask(prompt).Text;

    internal static string FanOut(string prompt)
    {
        if (prompt.Contains("You are one judge on a panel grading", StringComparison.Ordinal)) return Judge(prompt);
        if (prompt.Contains("PRELIMINARY rubric evaluation", StringComparison.Ordinal)) return ObjectiveGrade(prompt);
        if (prompt.Contains("identify the concepts a student must understand", StringComparison.Ordinal)) return ConceptMap(prompt);
        if (prompt.Contains("Write the grading rubric for one concept", StringComparison.Ordinal)) return Rubric(prompt);
        if (prompt.Contains("You check whether the examiner understood", StringComparison.Ordinal)) return Verification(prompt);
        if (prompt.Contains("You are one analyst on a panel examining", StringComparison.Ordinal)) return Analysis(prompt);
        if (prompt.Contains("Write the next question for this move:", StringComparison.Ordinal)) return Question(prompt);
        if (prompt.Contains("You are a Socratic tutor in learning mode", StringComparison.Ordinal)) return LearningTurn(prompt);
        // Text style (an integrity signal) is not simulated: a fabricated signal would mislead a reviewer.
        return JsonSerializer.Serialize(new { mock = true, note = $"{Marker} not simulated in mock mode" }, Json);
    }

    // ------------------------------------------------------------------ questions

    private static string Question(string prompt)
    {
        if (IsKorean(prompt)) return KoreanQuestion(prompt);
        if (IsSpanish(prompt)) return SpanishQuestion(prompt);
        var move = Line(prompt, "Write the next question for this move:");
        var concept = Line(prompt, "Concept:");
        if (concept.Length == 0) concept = "this idea";
        var related = Line(prompt, "Second concept (Synthesize only):");
        var dialogue = Section(prompt, "Conversation so far:");
        var asked = Regex.Matches(dialogue, @"(?m)^\s*Examiner:\s*(.+)$").Select(m => m.Groups[1].Value.Trim()).ToHashSet(StringComparer.Ordinal);
        var lastAnswer = StudentTexts(dialogue).LastOrDefault() ?? "";
        var phrase = KeyPhrase(lastAnswer);
        var targets = StudentTexts(Section(prompt, "Claims to confront (Contradiction only):", "Source material:"));

        string[] candidates = move switch
        {
            "Clarify" when phrase.Length > 0 =>
            [
                $"You said \"{phrase}\". What exactly do you mean by that?",
                $"When you say \"{phrase}\", what are you referring to, in your own words?",
            ],
            "Clarify" => [$"Could you say more precisely what you mean when you talk about {concept}?"],
            "Probe" =>
            [
                phrase.Length > 0 ? $"Why is it that \"{phrase}\"? Walk me through your reasoning." : $"Why is that true of {concept}? Walk me through your reasoning.",
                $"What follows from what you just said about {concept}, and how do you know?",
                $"How would you convince a classmate that your explanation of {concept} is right?",
            ],
            "Counterexample" =>
            [
                $"Can you think of a case where your explanation of {concept} would not hold? What happens then?",
                $"Is there a situation where {concept} behaves differently from how you described it? Describe it.",
            ],
            "Contradiction" when targets.Count >= 2 =>
            [
                $"Earlier you said \"{Clip(targets[0], 80)}\", and later \"{Clip(targets[1], 80)}\". How can both be true?",
            ],
            "Contradiction" => ["Earlier you said something that seems to conflict with what you just said. How do both fit together?"],
            "Apply" =>
            [
                $"Imagine a new situation, one we have not discussed, where {concept} matters. What would you expect to happen, and why?",
                $"Suppose you had to use {concept} to solve an everyday problem. Describe the problem and how you would reason about it.",
            ],
            "Synthesize" => [$"How does {concept} relate to {(related is { Length: > 0 } r && r != "(none)" ? r : "the other ideas we have discussed")}?"],
            "CausalReasoning" =>
            [
                $"What causes the outcome you just described, and what would follow if that cause changed?",
                $"Thinking about {concept}, why does that happen rather than something else?",
            ],
            "ScenarioVariation" =>
            [
                $"Keep everything in your example the same except one thing you choose to change. What happens then, and why?",
                $"Suppose one condition in the situation we discussed were different. How would {concept} play out then?",
            ],
            "AssumptionTesting" =>
            [
                $"What must be true for your explanation of {concept} to hold?",
                "What are you assuming in that answer, and what would happen if the assumption were false?",
            ],
            "IndependentExplanation" =>
            [
                $"Without using a textbook definition, how would you rebuild the idea of {concept} from your own reasoning?",
            ],
            _ =>
            [
                $"In your own words, what is {concept}, and how would you explain it with an example?",
                $"Let's turn to {concept}. How would you describe it in your own words?",
                $"What is the most important thing to understand about {concept}, and why?",
            ],
        };
        // Never repeat a question already asked in this session; rotate through the variants by turn count.
        var start = asked.Count % candidates.Length;
        for (var i = 0; i < candidates.Length; i++)
        {
            var q = candidates[(start + i) % candidates.Length];
            if (!asked.Contains(q)) return q;
        }
        return $"{candidates[0].TrimEnd('?')}, thinking about it a different way this time (question {asked.Count + 1})?";
    }

    /// <summary>
    /// The template questions of a Korean exam (GAD-T535), in the polite 해요체 register, built like the English ones from the concept
    /// title and the student's own words, and never repeating a question already asked.
    /// </summary>
    private static string KoreanQuestion(string prompt)
    {
        var move = Line(prompt, "Write the next question for this move:");
        var concept = Line(prompt, "Concept:");
        if (concept.Length == 0) concept = "이 개념";
        var related = Line(prompt, "Second concept (Synthesize only):");
        var dialogue = Section(prompt, "Conversation so far:");
        var asked = Regex.Matches(dialogue, @"(?m)^\s*Examiner:\s*(.+)$").Select(m => m.Groups[1].Value.Trim()).ToHashSet(StringComparer.Ordinal);
        var lastAnswer = StudentTexts(dialogue).LastOrDefault() ?? "";
        var phrase = KeyPhrase(lastAnswer);
        string[] candidates = move switch
        {
            "Clarify" when phrase.Length > 0 =>
            [
                $"“{phrase}”라고 하셨는데, 정확히 어떤 뜻인가요?",
                $"“{phrase}”라고 말씀하실 때 무엇을 가리키는지 자신의 말로 설명해 주시겠어요?",
            ],
            "Clarify" => [$"{concept}에 대해 말씀하실 때 어떤 뜻인지 조금 더 정확하게 설명해 주시겠어요?"],
            "Probe" =>
            [
                phrase.Length > 0 ? $"왜 “{phrase}”라고 생각하세요? 이유를 차근차근 설명해 주세요." : $"{concept}에 대해 왜 그것이 맞다고 생각하세요? 이유를 설명해 주세요.",
                $"방금 {concept}에 대해 하신 말씀에서 무엇이 따라 나오나요? 그것을 어떻게 아세요?",
                $"{concept}에 대한 설명이 맞다는 것을 친구에게 어떻게 설득하시겠어요?",
            ],
            "Counterexample" =>
            [
                $"{concept}에 대한 설명이 맞지 않는 경우를 생각해 볼 수 있을까요? 그때는 어떻게 될까요?",
                $"{concept}이(가) 말씀하신 것과 다르게 작동하는 상황이 있을까요? 설명해 주세요.",
            ],
            "Contradiction" => ["앞에서 하신 말씀과 방금 하신 말씀이 서로 맞지 않는 것 같아요. 두 가지가 어떻게 함께 성립할 수 있을까요?"],
            "Apply" =>
            [
                $"아직 이야기하지 않은 새로운 상황에서 {concept}이(가) 중요하다고 상상해 보세요. 어떤 일이 일어날 것 같고, 왜 그럴까요?",
                $"일상의 문제를 {concept}(으)로 풀어야 한다고 해 볼게요. 어떤 문제이고, 어떻게 생각해 보시겠어요?",
            ],
            "Synthesize" => [$"{concept}은(는) {(related is { Length: > 0 } r && r != "(none)" ? r : "지금까지 이야기한 다른 개념들")}과(와) 어떻게 연결되나요?"],
            "CausalReasoning" =>
            [
                "방금 말씀하신 결과의 원인은 무엇이고, 그 원인이 바뀌면 무엇이 달라질까요?",
                $"{concept}을(를) 생각해 볼 때, 왜 다른 일이 아니라 그 일이 일어날까요?",
            ],
            "ScenarioVariation" =>
            [
                "예로 드신 상황에서 한 가지만 바꾸고 나머지는 그대로 둔다고 해 볼게요. 그러면 어떻게 되고, 왜 그럴까요?",
                $"이야기한 상황의 조건 하나가 달랐다면 {concept}은(는) 어떻게 작동할까요?",
            ],
            "AssumptionTesting" =>
            [
                $"{concept}에 대한 설명이 맞으려면 무엇이 사실이어야 하나요?",
                "그 답변에서 무엇을 가정하고 계신가요? 그 가정이 틀리면 어떻게 될까요?",
            ],
            "IndependentExplanation" => [$"교과서 정의를 쓰지 않고, {concept}에 대해 자신의 생각으로 어떻게 설명하시겠어요?"],
            _ =>
            [
                $"{concept}이(가) 무엇인지 자신의 말로 설명하고, 예를 하나 들어 주시겠어요?",
                $"이제 {concept}에 대해 이야기해 볼게요. 자신의 말로 어떻게 설명하시겠어요?",
                $"{concept}에 대해 가장 중요하게 이해해야 할 점은 무엇이고, 왜 그런가요?",
            ],
        };
        var start = asked.Count % candidates.Length;
        for (var i = 0; i < candidates.Length; i++)
        {
            var q = candidates[(start + i) % candidates.Length];
            if (!asked.Contains(q)) return q;
        }
        return $"{candidates[0].TrimEnd('?')}, 이번에는 다른 방식으로 생각해 볼까요 (질문 {asked.Count + 1})?";
    }

    /// <summary>
    /// The template questions of a Spanish exam (GAD-T610), neutral Latin American Spanish with usted, built like the English ones from
    /// the concept title and the student's own words, and never repeating a question already asked.
    /// </summary>
    private static string SpanishQuestion(string prompt)
    {
        var move = Line(prompt, "Write the next question for this move:");
        var concept = Line(prompt, "Concept:");
        if (concept.Length == 0) concept = "esta idea";
        var related = Line(prompt, "Second concept (Synthesize only):");
        var dialogue = Section(prompt, "Conversation so far:");
        var asked = Regex.Matches(dialogue, @"(?m)^\s*Examiner:\s*(.+)$").Select(m => m.Groups[1].Value.Trim()).ToHashSet(StringComparer.Ordinal);
        var lastAnswer = StudentTexts(dialogue).LastOrDefault() ?? "";
        var phrase = KeyPhrase(lastAnswer);
        string[] candidates = move switch
        {
            "Clarify" when phrase.Length > 0 =>
            [
                $"Usted dijo «{phrase}». ¿Qué quiere decir exactamente con eso?",
                $"Cuando dice «{phrase}», ¿a qué se refiere, con sus propias palabras?",
            ],
            "Clarify" => [$"¿Podría explicar con más precisión qué quiere decir cuando habla de {concept}?"],
            "Probe" =>
            [
                phrase.Length > 0 ? $"¿Por qué «{phrase}»? Explíqueme su razonamiento paso a paso." : $"¿Por qué es cierto eso de {concept}? Explíqueme su razonamiento.",
                $"¿Qué se deduce de lo que acaba de decir sobre {concept}, y cómo lo sabe?",
                $"¿Cómo convencería a un compañero de clase de que su explicación de {concept} es correcta?",
            ],
            "Counterexample" =>
            [
                $"¿Se le ocurre un caso en el que su explicación de {concept} no se cumpliría? ¿Qué pasaría entonces?",
                $"¿Hay alguna situación en la que {concept} funcione de otra manera de como lo describió? Descríbala.",
            ],
            "Contradiction" => ["Antes dijo algo que parece no coincidir con lo que acaba de decir. ¿Cómo encajan las dos cosas?"],
            "Apply" =>
            [
                $"Imagine una situación nueva, que no hayamos comentado, en la que {concept} sea importante. ¿Qué esperaría que pasara y por qué?",
                $"Suponga que tiene que usar {concept} para resolver un problema cotidiano. Describa el problema y cómo razonaría.",
            ],
            "Synthesize" => [$"¿Qué relación hay entre {concept} y {(related is { Length: > 0 } r && r != "(none)" ? r : "las otras ideas que hemos comentado")}?"],
            "CausalReasoning" =>
            [
                "¿Qué causa el resultado que acaba de describir, y qué pasaría si esa causa cambiara?",
                $"Pensando en {concept}, ¿por qué ocurre eso y no otra cosa?",
            ],
            "ScenarioVariation" =>
            [
                "Deje todo igual en su ejemplo menos una cosa que usted elija cambiar. ¿Qué pasa entonces y por qué?",
                $"Suponga que una condición de la situación que comentamos fuera distinta. ¿Cómo funcionaría {concept} entonces?",
            ],
            "AssumptionTesting" =>
            [
                $"¿Qué tiene que ser cierto para que su explicación de {concept} funcione?",
                "¿Qué está suponiendo en esa respuesta, y qué pasaría si ese supuesto fuera falso?",
            ],
            "IndependentExplanation" => [$"Sin usar una definición de libro, ¿cómo reconstruiría la idea de {concept} a partir de su propio razonamiento?"],
            _ =>
            [
                $"Con sus propias palabras, ¿qué es {concept}? ¿Cómo lo explicaría con un ejemplo?",
                $"Pasemos a {concept}. ¿Cómo lo describiría con sus propias palabras?",
                $"¿Qué es lo más importante que hay que entender sobre {concept}, y por qué?",
            ],
        };
        var start = asked.Count % candidates.Length;
        for (var i = 0; i < candidates.Length; i++)
        {
            var q = candidates[(start + i) % candidates.Length];
            if (!asked.Contains(q)) return q;
        }
        return $"{candidates[0].TrimEnd('?')}, pensándolo de otra manera esta vez (pregunta {asked.Count + 1})?";
    }

    /// <summary>A short run of the student's own words worth asking about: the content-richest clause, at most six words.</summary>
    private static string KeyPhrase(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer) || SaysDontKnow(answer)) return "";
        var clauses = Regex.Split(answer, @"[,.;:!?]").Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
        var best = clauses.OrderByDescending(c => Keywords(c).Count).FirstOrDefault() ?? "";
        var words = best.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Take(6));
    }

    private static string Clarification(string prompt)
    {
        var phrase = StudentAfter(prompt, "The unclear phrase:");
        if (IsKorean(prompt))
            return phrase.Length > 0 ? $"“{Clip(phrase, 80)}”라고 하셨는데, 자신의 말로 어떤 뜻이었는지 설명해 주시겠어요?"
                : "제가 제대로 이해했는지 확인하고 싶어요. 자신의 말로 다시 한번 말씀해 주시겠어요?";
        if (IsSpanish(prompt))
            return phrase.Length > 0 ? $"Usted dijo «{Clip(phrase, 80)}». ¿Qué quiso decir con eso, con sus propias palabras?"
                : "¿Podría decirlo otra vez con sus propias palabras, para asegurarme de haber entendido lo que quiso decir?";
        return phrase.Length > 0
            ? $"You said \"{Clip(phrase, 80)}\". What did you mean by that, in your own words?"
            : "Could you say that again in your own words, so I'm sure I understood what you meant?";
    }

    private static string LearningTurn(string prompt)
    {
        var move = Line(prompt, "Move:");
        var concept = Line(prompt, "Concept:");
        if (concept.Length == 0) concept = "this idea";
        if (IsSpanish(prompt))
            return move == "GuidedExplanation"
                ? $"Veamos {concept} juntos, con los apuntes como guía. Piense en un ejemplo pequeño que conozca bien. ¿Cómo lo usaría para explicar {concept} con sus propias palabras?"
                : $"Le doy una pista: piense en qué representa cada parte de su ejemplo. Con eso en mente, ¿cómo explicaría {concept} ahora?";
        if (IsKorean(prompt))
            return move == "GuidedExplanation"
                ? $"노트를 길잡이로 {concept}을(를) 함께 살펴볼게요. 잘 아는 작은 예를 하나 떠올려 보세요. 그 예로 {concept}을(를) 자신의 말로 어떻게 설명하시겠어요?"
                : $"힌트를 드릴게요. 예의 각 부분이 무엇을 나타내는지 생각해 보세요. 그렇다면 이제 {concept}을(를) 어떻게 설명하시겠어요?";
        return move == "GuidedExplanation"
            ? $"Let's look at {concept} together, using the notes as our guide. Take one small example you know well. How would you use it to explain {concept} in your own words?"
            : $"Here is a hint: think about what each part of your example stands for. With that in mind, how would you explain {concept} now?";
    }

    // ------------------------------------------------------------------ response analysis (evidence per objective)

    private static string Analysis(string prompt)
    {
        var answer = StudentAfter(prompt, "Student's answer:");
        var objectiveKeys = Section(prompt, "Learning objectives (key: title [dimensions]):", "The question examined objective:")
            .Split('\n').Select(l => l.Trim().TrimStart('-', ' ')).Where(l => l.Contains(':'))
            .Select(l => (Key: l[..l.IndexOf(':')].Trim(), Title: l[(l.IndexOf(':') + 1)..].Trim())).ToList();
        var current = Line(prompt, "The question examined objective:");
        var currentKey = objectiveKeys.FirstOrDefault(o => current.StartsWith(o.Key, StringComparison.Ordinal)).Key
            ?? (current.Contains(':') ? current[..current.IndexOf(':')].Trim() : current);
        var rubric = Section(prompt, "Rubric points:", "Student's earlier claims:");
        var expected = Keywords(rubric + " " + current + " " + Line(prompt, "Concept:"));

        if (SaysDontKnow(answer))
        {
            return JsonSerializer.Serialize(new
            {
                claims = Array.Empty<object>(), misconceptions = Array.Empty<object>(), uncertainties = Array.Empty<string>(),
                evidence = Array.Empty<object>(), questionRevealedAnswer = false,
            }, Json);
        }

        var sentences = Sentences(answer).Where(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= 3 and <= 30).ToList();
        var claims = sentences.Where(s => !s.EndsWith('?') && Keywords(s).Count >= 2).Select(s => new { quote = s, claim = s.TrimEnd('.', '!', ' ') + "." }).ToList();
        var lower = answer.ToLowerInvariant();
        var vague = VagueMarkers.FirstOrDefault(m => Regex.IsMatch(lower, $@"\b{Regex.Escape(m)}\b"));
        var misconceptionLines = rubric.Split('\n').SkipWhile(l => !l.Contains("Misconceptions to watch for", StringComparison.Ordinal)).Skip(1)
            .Select(l => l.Trim()).Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..]).ToList();
        var answerWords = Keywords(answer);
        List<string> shown = HasNegation(answer) ? [] : misconceptionLines.Where(m => Coverage(Keywords(m), answerWords) >= 0.6).ToList();

        var evidence = sentences.Select(s =>
        {
            var coverage = Coverage(expected, Keywords(s));
            var stance = vague is not null && s.Contains(vague, StringComparison.OrdinalIgnoreCase) ? "uncertain"
                : shown.Count > 0 && shown.Any(m => Coverage(Keywords(m), Keywords(s)) >= 0.6) ? "misconception"
                : coverage >= 0.08 ? "supports" : "uncertain";
            var dimension = ReasoningMarkers(s) > 0 ? "logical-reasoning" : "conceptual-understanding";
            return new { objective = currentKey, stance, dimension, quote = s, note = $"{Marker} Keyword overlap {coverage:P0} with the rubric; simulated, not an AI reading." };
        }).Take(4).ToList();

        // A withdrawal ("I was wrong earlier", GAD-T441): the earlier claim sharing the most key words with the answer.
        var retractions = new List<object>();
        if (Retraction().Match(answer) is { Success: true } said)
        {
            var earlier = Regex.Matches(Section(prompt, "Student's earlier claims:", "Source passages:"), @"(?m)^\s*\[turn \d+\]\s*(.+)$")
                .Select(m => Regex.Replace(m.Groups[1].Value.Trim(), @"\s*\(withdrawn by the student at turn \d+\)$", "")).ToList();
            var target = earlier.Select(e => (Text: e, Score: Coverage(Keywords(e), answerWords))).Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score).Select(x => x.Text).FirstOrDefault();
            if (target is not null) retractions.Add(new { quote = said.Value, claim = target });
        }

        return JsonSerializer.Serialize(new
        {
            claims,
            retractions,
            misconceptions = shown.Select(m => new { objective = currentKey, misconception = m, quote = sentences.FirstOrDefault() ?? answer }),
            uncertainties = vague is null ? Array.Empty<string>() : [$"{Marker} \"{vague}\" could refer to different things."],
            evidence,
            questionRevealedAnswer = false,
        }, Json);
    }

    // ------------------------------------------------------------------ claims, aporia

    private static string Claims(string prompt)
    {
        var answer = StudentAfter(prompt, "Answer:");
        if (SaysDontKnow(answer)) return "[]";
        var claims = Sentences(answer)
            .Where(s => !s.EndsWith('?') && s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4 && Keywords(s).Count >= 2)
            .Select(s => new { quote = s, claim = s.TrimEnd('.', '!', ' ') + "." })
            .ToList();
        return JsonSerializer.Serialize(claims, Json);
    }

    /// <summary>The student says they don't know and offers (almost) nothing else.</summary>
    internal static bool SaysDontKnow(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return true;
        if (!IsAporia(answer)) return false;
        var rest = DontKnowPhrase().Replace(answer, " ");
        // Korean (GAD-T537): with the "I don't know" phrases gone, only a few words (a 저는, a 잘, a 죄송해요) may be left.
        if (Gadfly.Engine.LanguageText.HasHangul(answer))
            return Gadfly.Engine.LanguageText.WordCount(Gadfly.Engine.LanguageText.WithoutKoreanDontKnow(rest)) <= 4;
        // Spanish (GAD-T610): with "no lo sé" and the like gone, only a few words (a lo siento, a la verdad, a perdón) may be left.
        if (Gadfly.Engine.LanguageText.SaysDontKnowSpanish(answer))
            return Gadfly.Engine.LanguageText.WordCount(Gadfly.Engine.LanguageText.WithoutSpanishDontKnow(rest)) <= 4;
        return Keywords(rest).Count <= 3;
    }

    [GeneratedRegex(@"\b(i\s+was\s+wrong(\s+(earlier|before))?|i\s+take\s+(that|it)\s+back|scratch\s+that|i\s+no\s+longer\s+think|i\s+made\s+a\s+mistake(\s+(earlier|before))?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Retraction();

    [GeneratedRegex(@"\b(i\s+(really\s+)?(do\s*n[o']?t|dont|don't)\s+know|no\s+idea|not\s+sure|can'?t\s+answer|cannot\s+answer|i\s+dunno|aporia|i\s+have\s+no\s+clue|sorry|honestly|really)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DontKnowPhrase();

    // ------------------------------------------------------------------ judging an answer (PanelAnswerJudge)

    private static string Judge(string prompt)
    {
        var conceptLine = Line(prompt, "Concept:");
        var rubric = Section(prompt, "Rubric points:", "Student's earlier claims:");
        var points = new List<string>();
        var misconceptions = new List<string>();
        var inMisconceptions = false;
        foreach (var raw in rubric.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Misconceptions to watch for", StringComparison.Ordinal)) { inMisconceptions = true; continue; }
            if (line.StartsWith("Also acceptable", StringComparison.Ordinal)) { inMisconceptions = false; continue; }
            if (!line.StartsWith("- ", StringComparison.Ordinal)) continue;
            var text = Regex.Replace(line[2..], @"^\(weight [^)]*\)\s*", "");
            (inMisconceptions ? misconceptions : points).Add(text);
        }
        var answer = StudentAfter(prompt, "Student's answer:");
        var hasPassages = Regex.IsMatch(Section(prompt, "Source passages:", "Question:"), @"(?m)^\s*\[1\]");
        int[] cite = hasPassages ? [1] : [];
        var confronted = Line(prompt, "Question move:") == nameof(Gadfly.Engine.MoveKind.Contradiction);

        if (SaysDontKnow(answer))
        {
            return JudgeJson(0, 0, confronted ? 0 : null, 0, cite, "the student said they do not know",
                $"{Marker} The student said they do not know; no understanding was shown in this answer.");
        }

        var answerWords = Keywords(answer);
        var expected = new HashSet<string>(Keywords(conceptLine));
        foreach (var p in points) expected.UnionWith(Keywords(p));
        var coverage = Coverage(expected, answerWords);
        var hits = points.Count(p => Keywords(p) is var k && k.Count > 0 && (Coverage(k, answerWords) >= 0.34 || k.Count(answerWords.Contains) >= 3));
        var wrong = misconceptions.Any(m => !HasNegation(answer) && Coverage(Keywords(m), answerWords) >= 0.6);
        var markers = ReasoningMarkers(answer);
        var wordCount = answer.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var korean = Gadfly.Engine.LanguageText.HasHangul(answer);
        var spanish = !korean && IsSpanish(prompt);
        if (spanish)
        {
            // A Spanish answer against the English rubric (GAD-T610): English keyword overlap only catches cognates and English terms,
            // so the mock also credits how much the student said and whether they gave a reason, like Korean; still a simulation.
            coverage = Math.Max(coverage, Math.Min(0.5, wordCount / 70.0));
            hits = Math.Max(hits, (int)Math.Round(points.Count * Math.Min(1.0, wordCount / 45.0)));
            markers += Gadfly.Engine.LanguageText.SpanishReasonOrExample(answer) ? 2 : 0;
        }
        if (korean)
        {
            // A Korean answer against the English rubric (GAD-T536): no English keyword overlap to count, so the mock credits the
            // English technical terms the student used plus how much they said and whether they gave a reason; still a simulation.
            wordCount = Gadfly.Engine.LanguageText.WordCount(answer);
            coverage = Math.Max(coverage, Math.Min(0.5, wordCount / 60.0));
            hits = Math.Max(hits, (int)Math.Round(points.Count * Math.Min(1.0, wordCount / 40.0)));
            markers += Gadfly.Engine.LanguageText.KoreanReasonOrExample(answer) ? 2 : 0;
        }

        var accuracy = Math.Clamp((int)Math.Round(2 + 16 * coverage), 0, 10) - (wrong ? 4 : 0);
        var reasoning = coverage == 0 ? Math.Min(2, markers) : Math.Clamp(2 + 3 * markers, 0, 10);
        var completeness = points.Count == 0 ? (int)Math.Round(10 * Math.Min(1, coverage * 2)) : (int)Math.Round(10.0 * hits / points.Count);
        int? consistency = confronted ? 8 : null;
        if (wordCount < 4)
        {
            accuracy = Math.Min(accuracy, 3);
            reasoning = Math.Min(reasoning, 2);
            completeness = Math.Min(completeness, 2);
        }
        accuracy = Math.Clamp(accuracy, 0, 10);
        var coveredText = string.Create(CultureInfo.InvariantCulture, $"{coverage:P0} of the rubric's key terms");
        var verdict = (korean ? $"{Marker} Korean answer: scored from its length, its reasons and any English terms it uses. " : "")
            + (spanish ? $"{Marker} Spanish answer: scored from its length, its reasons and any English terms or cognates it uses. " : "")
            + $"{Marker} Simulated from keyword overlap, not an AI evaluation: the answer uses {coveredText}"
            + (points.Count > 0 ? $" and touches {hits} of {points.Count} rubric points" : "")
            + (wrong ? "; it repeats a listed misconception." : ".");
        return JudgeJson(accuracy, reasoning, consistency, completeness, cite, coveredText, verdict);
    }

    /// <summary>The judge's reply; <paramref name="consistency"/> is null on any move but Contradiction, which leaves it out (GAD-T816).</summary>
    private static string JudgeJson(int accuracy, int reasoning, int? consistency, int completeness, int[] cite, string basis, string verdict)
    {
        var criteria = new List<object>
        {
            new { criterion = "accuracy", score = accuracy, rationale = $"{Marker} Accuracy estimated from {basis}.", passages = cite },
            new { criterion = "reasoning", score = reasoning, rationale = $"{Marker} Reasoning estimated from explanatory words (because, so, therefore).", passages = cite },
            new { criterion = "completeness", score = completeness, rationale = $"{Marker} Completeness estimated from rubric points whose key terms appear.", passages = cite },
        };
        if (consistency is { } c)
            criteria.Add(new { criterion = "consistency", score = c, rationale = $"{Marker} The resolution is not checked in mock mode; a neutral score is given.", passages = Array.Empty<int>() });
        return JsonSerializer.Serialize(new { criteria, verdict }, Json);
    }

    // ------------------------------------------------------------------ objective grading (AiObjectiveGrader)

    private static string ObjectiveGrade(string prompt)
    {
        var objective = string.Join(' ', Line(prompt, "Learning objective:"), Line(prompt, "Description:"), Line(prompt, "Concept:"));
        var objectiveWords = Keywords(objective);
        var criteria = Regex.Matches(prompt, @"(?m)^\s*- ""(.+?)"" \((\w+)\)").Select(m => (Name: m.Groups[1].Value, Dimension: m.Groups[2].Value)).ToList();
        var evidenceSection = Section(prompt, "Evidence (", "Rules:");
        var evidence = new List<(int Number, string Answer)>();
        var starts = Regex.Matches(evidenceSection, @"(?m)^\s*\[(\d+)\] Question:").ToList();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : evidenceSection.Length;
            var chunk = evidenceSection[starts[i].Index..end];
            evidence.Add((int.Parse(starts[i].Groups[1].Value, CultureInfo.InvariantCulture), StudentTexts(chunk).FirstOrDefault() ?? ""));
        }
        var known = Line(prompt, "Common misconceptions to watch for:").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var usable = evidence.Where(e => !SaysDontKnow(e.Answer)).ToList();
        var scored = usable.Select(e => (e.Number, e.Answer, Coverage: Coverage(objectiveWords, Keywords(e.Answer)), Markers: ReasoningMarkers(e.Answer))).ToList();
        var best = scored.OrderByDescending(s => s.Coverage).FirstOrDefault();
        var cited = scored.Where(s => s.Coverage > 0).Select(s => s.Number).DefaultIfEmpty(best.Number).Where(n => n > 0).ToArray();
        var reasoningSeen = scored.Any(s => s.Markers > 0);

        var results = criteria.Select(c =>
        {
            if (usable.Count == 0)
                return new { criterion = c.Name, level = 0, rationale = $"{Marker} No usable evidence: insufficient to judge.", evidence = Array.Empty<int>(), quote = "" };
            var level = best.Coverage switch { < 0.1 => 1, < 0.25 => 2, < 0.45 => 3, < 0.65 => 4, _ => 5 };
            if (c.Dimension is "LogicalReasoning" or "Application" or "KnowledgeTransfer")
                level = Math.Clamp(level + (reasoningSeen ? 0 : -1), 1, 5);
            return new
            {
                criterion = c.Name,
                level,
                rationale = $"{Marker} Level simulated from keyword overlap ({best.Coverage:P0}) with the objective; not an AI judgement.",
                evidence = cited,
                quote = QuoteFrom(best.Answer),
            };
        }).ToList();

        var shown = known.Where(m => usable.Any(e => !HasNegation(e.Answer) && Coverage(Keywords(m), Keywords(e.Answer)) >= 0.6)).ToList();
        return JsonSerializer.Serialize(new
        {
            criteria = results,
            explanation = usable.Count == 0
                ? $"{Marker} Simulated evaluation: no usable evidence on this objective."
                : $"{Marker} Simulated evaluation from keyword overlap, not an AI judgement: the strongest answer [{best.Number}] uses {best.Coverage:P0} of the objective's key terms.",
            misconceptions = shown,
            ambiguities = Array.Empty<string>(),
        }, Json);
    }

    /// <summary>The first words of an answer, copied exactly (a quote the grader can verify).</summary>
    private static string QuoteFrom(string answer)
    {
        var text = answer.Trim();
        var spaces = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == ' ' && ++spaces == 8) return text[..i];
        return text;
    }

    // ------------------------------------------------------------------ verification and clarification (ResponseVerifier)

    private static readonly string[] VagueMarkers = ["the thing", "that thing", "this thing", "stuff", "kind of", "sort of", "or something", "whatever", "you know"];
    private static readonly string[] DanglingEnds = ["and", "because", "so", "but", "or", "the", "a", "which", "that", "then"];

    private static string Verification(string prompt)
    {
        var answer = StudentAfter(prompt, "Student's answer:");
        string? trigger = null, phrase = null, reason;
        var lower = answer.ToLowerInvariant();
        var vague = VagueMarkers.FirstOrDefault(m => Regex.IsMatch(lower, $@"\b{Regex.Escape(m)}\b"));
        var lastWord = Regex.Match(lower.TrimEnd(), @"[\p{L}']+$").Value;
        if (answer.TrimEnd().EndsWith("...", StringComparison.Ordinal) || (DanglingEnds.Contains(lastWord) && !Regex.IsMatch(answer.TrimEnd(), @"[.!?]$")))
        {
            trigger = "incomplete-sentence";
            phrase = LastWords(answer, 8);
            reason = "The answer stops mid-thought, so its point cannot be determined.";
        }
        else if (vague is not null)
        {
            trigger = "ambiguous-reference";
            phrase = WordsAround(answer, vague, 8);
            reason = $"\"{vague}\" could refer to different things, and the grade depends on which.";
        }
        else
        {
            reason = "No vague reference or unfinished sentence was found.";
        }
        return JsonSerializer.Serialize(new
        {
            clarify = trigger is not null,
            trigger = trigger ?? "none",
            phrase = phrase ?? "",
            material = trigger is not null,
            reason = $"{Marker} {reason}",
        }, Json);
    }

    private static string LastWords(string text, int n)
    {
        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Skip(Math.Max(0, words.Length - n)));
    }

    /// <summary>Up to <paramref name="n"/> words of the answer around <paramref name="marker"/>, copied from the answer.</summary>
    private static string WordsAround(string text, string marker, int n)
    {
        var at = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return LastWords(text, n);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var before = text[..at].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var from = Math.Max(0, before - 3);
        return string.Join(' ', words.Skip(from).Take(n)).Trim(',', '.', ';');
    }

    // ------------------------------------------------------------------ concept map and rubric (source processing)

    private static readonly Regex SideHeading = new("misconception|summary|overview|introduction|references|further reading", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string ConceptMap(string prompt)
    {
        var passages = Passages(Section(prompt, "Passages:"));
        var byConcept = new List<(string Title, List<(int Number, string Text, bool Misconception)> Items)>();
        var anyH2 = passages.Any(p => HeadingPath(p.Location).Count >= 2);
        foreach (var (number, location, text) in passages)
        {
            var path = HeadingPath(location);
            string? title = anyH2 ? (path.Count >= 2 ? path[1] : null) : path.FirstOrDefault();
            if (title is null || SideHeading.IsMatch(title)) continue;
            var misconception = path.Skip(2).Any(h => SideHeading.IsMatch(h));
            var entry = byConcept.FindIndex(c => c.Title == title);
            if (entry < 0) { byConcept.Add((title, [])); entry = byConcept.Count - 1; }
            byConcept[entry].Items.Add((number, StripHeadings(text, path), misconception));
        }
        if (byConcept.Count == 0 && passages.Count > 0)
            byConcept.Add(("Key ideas", passages.Select(p => (p.Number, p.Text, false)).ToList()));

        var concepts = byConcept.Select(c =>
        {
            var body = c.Items.Where(i => !i.Misconception).SelectMany(i => Sentences(i.Text)).Where(s => s.Length > 15).ToList();
            var wrong = c.Items.Where(i => i.Misconception).SelectMany(i => Bullets(i.Text)).ToList();
            return new
            {
                key = Slug(c.Title),
                title = c.Title,
                definition = body.FirstOrDefault() ?? c.Title,
                keyFacts = body.Skip(1).Take(3).ToList(),
                misconceptions = wrong,
                passages = c.Items.Select(i => i.Number).Distinct().ToList(),
            };
        }).ToList();
        var relations = concepts.Zip(concepts.Skip(1)).Select(p => new { from = p.First.key, to = p.Second.key, kind = "prerequisite" }).ToList();
        return JsonSerializer.Serialize(new { concepts, relations }, Json);
    }

    private static string Rubric(string prompt)
    {
        var concept = Line(prompt, "Concept:");
        var definition = Line(prompt, "Definition:");
        var focus = Keywords(concept + " " + definition);
        var passages = Passages(Section(prompt, "Passages:"));
        var understanding = new List<(string Text, int Number, int Score)>();
        var misconceptions = new List<(string Text, int Number)>();
        foreach (var (number, location, text) in passages)
        {
            var path = HeadingPath(location);
            var body = StripHeadings(text, path);
            if (path.Skip(2).Any(h => SideHeading.IsMatch(h)))
            {
                misconceptions.AddRange(Bullets(body).Select(b => (b, number)));
                continue;
            }
            foreach (var s in Sentences(body).Where(s => s.Length > 25 && !s.EndsWith('?')))
                understanding.Add((s, number, Keywords(s).Count(focus.Contains)));
        }
        var top = understanding.OrderByDescending(u => u.Score).ThenBy(u => u.Number).Take(3).ToList();
        int[] weights = [3, 2, 2];
        return JsonSerializer.Serialize(new
        {
            understanding = top.Select((u, i) => new { text = $"{Marker} {u.Text}", weight = weights[i], passages = new[] { u.Number } }),
            acceptableVariants = Array.Empty<object>(),
            misconceptions = misconceptions.Take(3).Select(m => new { text = $"{Marker} {m.Text}", passages = new[] { m.Number } }),
        }, Json);
    }

    /// <summary>"notes.md > Title > Concept > Sub" → ["Title", "Concept", "Sub"] (the file name dropped).</summary>
    private static List<string> HeadingPath(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return [];
        var parts = location.Split(" > ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && Regex.IsMatch(parts[0], @"\.(md|markdown|txt|text)$", RegexOptions.IgnoreCase)) parts.RemoveAt(0);
        return parts;
    }

    /// <summary>A passage starts with its heading lines; drop them so definitions are real sentences.</summary>
    private static string StripHeadings(string text, List<string> path)
    {
        var t = text.Trim();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var h in path)
            {
                if (t.StartsWith(h, StringComparison.Ordinal))
                {
                    t = t[h.Length..].TrimStart(' ', '\n', '\r', ':', '.');
                    changed = true;
                }
            }
        }
        return t;
    }

    private static List<string> Bullets(string text) =>
        Regex.Split(text, @"(?:^|\s)[-*]\s+").Select(s => s.Trim().TrimEnd('.')).Where(s => s.Length > 10).Select(s => s + ".").ToList();

    private static string Slug(string title)
    {
        var words = Regex.Matches(title.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Take(6);
        var slug = string.Join("-", words);
        return slug.Length == 0 ? "concept" : slug;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "...";
}
