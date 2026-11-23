using System.Text.RegularExpressions;
using Gadfly.Engine;
using Gadfly.Socrates;

namespace Gadfly.Sources.Verification;

/// <summary>Why a response needs a clarification (master plan Part 4.5).</summary>
public enum ClarificationTrigger
{
    None,
    IncompleteSentence,
    AmbiguousReference,
    MultipleInterpretations,
    UnrecognizedTerminology,
    StudentRequest,
    /// <summary>Voice only: the speech recognizer was unsure of the words (a transcription question, not a meaning one).</summary>
    LowSpeechConfidence,
}

/// <summary>What the verifier looks at for one answer.</summary>
public sealed record VerificationInput
{
    public required string Question { get; init; }
    public required string AnswerText { get; init; }
    /// <summary>"text" or "voice".</summary>
    public string Modality { get; init; } = "text";
    /// <summary>The attempt's exam language (GAD-T535): the clarification is worded in it.</summary>
    public string Language { get; init; } = ExamLanguage.English;
    public double? SpeechConfidence { get; init; }
    public IReadOnlyList<TimedWord> Words { get; init; } = [];
    public required string ConceptTitle { get; init; }
    /// <summary>Source material for the concept, used only to recognize terminology (never shown to the student).</summary>
    public string Grounding { get; init; } = "";
}

/// <summary>One cheap deterministic finding (no LLM).</summary>
public sealed record VerificationHint(ClarificationTrigger Trigger, string? Phrase, string Reason);

/// <summary>The verifier's decision for one answer. <see cref="Reason"/> is recorded with the response.</summary>
public sealed record VerificationDecision
{
    public required bool NeedsClarification { get; init; }
    public ClarificationTrigger Trigger { get; init; }
    /// <summary>The student's own words that are unclear (always a substring of the answer), or null.</summary>
    public string? Phrase { get; init; }
    public required string Reason { get; init; }
    /// <summary>Voters whose reply parsed; 0 when the decision rests on deterministic checks alone.</summary>
    public int Voters { get; init; }
    public static VerificationDecision Clear(string reason, int voters = 0) => new() { NeedsClarification = false, Reason = reason, Voters = voters };
}

/// <summary>A worded clarification question and how it was produced.</summary>
/// <param name="NonLeadingChecked">True when the question passed both leak checks, or is the fixed template that only quotes the student.</param>
/// <param name="Outcome">"drafted", or "template: ..." with why the draft was not used.</param>
public sealed record ClarificationQuestion(string Text, bool NonLeadingChecked, string Outcome);

/// <summary>
/// The response verification engine (master plan Component C, Parts 4.3-4.5). After each answer it
/// decides whether a neutral clarification is needed, and words one that cannot lead the student:
/// cheap deterministic checks first (incomplete sentence, explicit request, low speech confidence),
/// then the panel (schema-checked JSON, majority vote) for ambiguity, multiple interpretations and
/// unrecognized terminology, asking only when the uncertainty could materially affect grading.
/// Clarification questions come from a dedicated prompt that forbids technical content, then pass a
/// deterministic new-words check and the question leak guard (LEAKS/SAFE); anything that fails falls
/// back to a template that only quotes the student's own words.
/// </summary>
public sealed partial class ResponseVerifier(ISocraticVotingClient panel, PromptSet? prompts = null)
{
    private readonly PromptSet _prompts = prompts ?? ExamPrompts.Set;

    /// <summary>Below this recognizer confidence a voice answer's wording is treated as uncertain.</summary>
    public const double LowSpeechConfidenceThreshold = 0.6;

    /// <summary>Answers shorter than this (in characters) are not checked for ambiguity.</summary>
    public const int MinCheckedChars = 12;

    private static readonly HashSet<string> Triggers = new(StringComparer.OrdinalIgnoreCase)
    {
        "incomplete-sentence", "ambiguous-reference", "multiple-interpretations", "unrecognized-terminology", "student-request",
    };

    // ------------------------------------------------------------------ deterministic checks

    /// <summary>The cheap checks: no LLM, deterministic, unit-tested.</summary>
    public static IReadOnlyList<VerificationHint> Hints(VerificationInput input)
    {
        var hints = new List<VerificationHint>();
        var text = (input.AnswerText ?? "").Trim();
        if (text.Length == 0 || IsAporiaWord(text)) return hints;

        var ask = StudentAsks().Match(text);
        if (!ask.Success && LanguageText.HasHangul(text)) ask = KoreanStudentAsks().Match(text);
        if (!ask.Success && LanguageText.LooksSpanish(text)) ask = SpanishStudentAsks().Match(text);   // GAD-T610
        if (ask.Success)
            hints.Add(new(ClarificationTrigger.StudentRequest, null, $"The student asked to clarify (\"{ask.Value.Trim()}\")."));

        if (IsIncomplete(text))
            hints.Add(new(ClarificationTrigger.IncompleteSentence, Tail(text, 8), "The answer stops mid-sentence."));

        if (input.Modality == "voice")
        {
            var low = input.Words.Where(w => w.Confidence < LowSpeechConfidenceThreshold && w.Text.Trim().Length > 2).Select(w => w.Text.Trim()).ToList();
            var meanLow = input.Words.Count > 0 && input.Words.Average(w => w.Confidence) < LowSpeechConfidenceThreshold;
            if (input.SpeechConfidence is { } c && c < LowSpeechConfidenceThreshold || meanLow || low.Count > 0)
                hints.Add(new(ClarificationTrigger.LowSpeechConfidence, low.Count == 0 ? null : string.Join(" ", low.Take(6)),
                    "Speech recognition was unsure of some words."));
        }
        return hints;
    }

    /// <summary>True when the answer visibly stops partway (trailing ellipsis or dash, or a dangling connective).</summary>
    public static bool IsIncomplete(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return false;
        if (LanguageText.IsIncompleteKorean(t) is { } korean) return korean;   // a Korean last word: Korean endings decide (GAD-T537)
        if (LanguageText.IsIncompleteSpanish(t) is { } spanish) return spanish;   // Spanish text: a trailing comma or connective (GAD-T610)
        if (t.EndsWith("...", StringComparison.Ordinal) || t.EndsWith('…') || t.EndsWith('-') || t.EndsWith('—') || t.EndsWith('–')) return true;
        if (".!?)\"'”".Contains(t[^1])) return false;
        var last = WordRx().Matches(t).Select(m => m.Value.ToLowerInvariant()).LastOrDefault();
        return last is not null && DanglingWords.Contains(last);
    }

    private static readonly HashSet<string> DanglingWords = new(StringComparer.Ordinal)
    {
        "and", "or", "but", "because", "so", "the", "a", "an", "of", "to", "with", "which", "that", "is", "are", "was", "were",
        "when", "if", "then", "than", "into", "for", "by", "from", "as", "its", "their", "whereas", "while", "although", "since",
    };

    public static bool IsAporiaWord(string text) => text.Trim().Equals(SourceExamEngine.AporiaWord, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ decision

    /// <summary>
    /// Decides whether the answer needs a clarification. An explicit student request always does; low
    /// speech confidence is reported (the caller turns it into transcript confirmation); otherwise a
    /// majority of the panel's valid votes must say the uncertainty is real and material. With no usable
    /// vote, an incomplete sentence still asks (the safe side: it costs one question, a wrong reading costs a grade).
    /// </summary>
    public async Task<VerificationDecision> DecideAsync(VerificationInput input, CancellationToken ct = default)
    {
        var text = (input.AnswerText ?? "").Trim();
        if (text.Length == 0 || IsAporiaWord(text)) return VerificationDecision.Clear("The student said they do not know.");
        var hints = Hints(input);
        if (hints.FirstOrDefault(h => h.Trigger == ClarificationTrigger.StudentRequest) is { } asked)
            return new() { NeedsClarification = true, Trigger = asked.Trigger, Phrase = asked.Phrase, Reason = asked.Reason };
        if (text.Length < MinCheckedChars && hints.Count == 0)
            return VerificationDecision.Clear("The answer is too short to be ambiguous.");

        var prompt = ExamPrompts.Fill(_prompts[ExamPrompts.VerificationKey], new Dictionary<string, string?>
        {
            ["concept"] = input.ConceptTitle,
            ["hints"] = hints.Count == 0 ? "(none)" : string.Join("; ", hints.Select(h => h.Reason)),
            ["grounding"] = Clip(input.Grounding, 4000) is { Length: > 0 } g ? g : "(none)",
            ["question"] = input.Question,
            ["answer"] = ExamPrompts.Student(text),
        });
        IReadOnlyList<PanelReply> replies;
        try { replies = await panel.FanOutAsync(PanelJson.JsonOnlySystem, prompt, maxTokens: 300, temperature: 0.1, ct: ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { replies = []; }

        var votes = new List<Vote>();
        foreach (var r in replies.Where(r => !r.IsError))
            if (ParseVote(r.Text, text) is { } v) votes.Add(v);

        if (votes.Count == 0)
        {
            if (hints.FirstOrDefault(h => h.Trigger == ClarificationTrigger.IncompleteSentence) is { } inc)
                return new() { NeedsClarification = true, Trigger = inc.Trigger, Phrase = inc.Phrase, Reason = inc.Reason + " (no panel vote was usable)" };
            return VerificationDecision.Clear("No panel vote was usable and the simple checks found nothing.");
        }

        var yes = votes.Where(v => v.Clarify && v.Material && v.Trigger != ClarificationTrigger.None).ToList();
        if (yes.Count * 2 <= votes.Count)
        {
            var why = votes.Where(v => !v.Clarify || !v.Material).Select(v => v.Reason).FirstOrDefault(r => r.Length > 0);
            return VerificationDecision.Clear($"Panel: no material ambiguity ({votes.Count - yes.Count} of {votes.Count}){(why is null ? "" : ": " + why)}", votes.Count);
        }
        // The most common trigger among the yes votes; its most common quoted phrase.
        var trigger = yes.GroupBy(v => v.Trigger).OrderByDescending(g => g.Count()).First();
        var phrase = trigger.Where(v => v.Phrase is not null).GroupBy(v => v.Phrase!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
            ?? hints.FirstOrDefault(h => h.Trigger == trigger.Key)?.Phrase;
        return new()
        {
            NeedsClarification = true,
            Trigger = trigger.Key,
            Phrase = phrase,
            Reason = $"Panel ({yes.Count} of {votes.Count}): {trigger.First().Reason}",
            Voters = votes.Count,
        };
    }

    internal sealed record Vote(bool Clarify, bool Material, ClarificationTrigger Trigger, string? Phrase, string Reason);

    /// <summary>Schema check of one voter's JSON. Null when it does not match; a phrase not found in the answer is dropped.</summary>
    internal static Vote? ParseVote(string raw, string answer)
    {
        using var doc = PanelJson.ParseObject(raw);
        if (doc is null) return null;
        var e = doc.RootElement;
        if (!e.TryGetProperty("clarify", out var c) || c.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)) return null;
        var material = e.TryGetProperty("material", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.True;
        var triggerText = PanelJson.Str(e, "trigger").Trim();
        var trigger = Triggers.Contains(triggerText) ? ParseTrigger(triggerText) : ClarificationTrigger.None;
        var phrase = PanelJson.Str(e, "phrase").Trim().Trim('"', '\'', '“', '”');
        if (phrase.Length == 0 || phrase.Length > 200 || answer.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) < 0) phrase = "";
        else phrase = answer.Substring(answer.IndexOf(phrase, StringComparison.OrdinalIgnoreCase), phrase.Length);   // the student's own casing
        return new Vote(c.ValueKind == System.Text.Json.JsonValueKind.True, material, trigger, phrase.Length == 0 ? null : phrase,
            Clip(PanelJson.Str(e, "reason"), 300));
    }

    private static ClarificationTrigger ParseTrigger(string t) => t.ToLowerInvariant() switch
    {
        "incomplete-sentence" => ClarificationTrigger.IncompleteSentence,
        "ambiguous-reference" => ClarificationTrigger.AmbiguousReference,
        "multiple-interpretations" => ClarificationTrigger.MultipleInterpretations,
        "unrecognized-terminology" => ClarificationTrigger.UnrecognizedTerminology,
        "student-request" => ClarificationTrigger.StudentRequest,
        _ => ClarificationTrigger.None,
    };

    public static string TriggerName(ClarificationTrigger t) => t switch
    {
        ClarificationTrigger.IncompleteSentence => "incomplete-sentence",
        ClarificationTrigger.AmbiguousReference => "ambiguous-reference",
        ClarificationTrigger.MultipleInterpretations => "multiple-interpretations",
        ClarificationTrigger.UnrecognizedTerminology => "unrecognized-terminology",
        ClarificationTrigger.StudentRequest => "student-request",
        ClarificationTrigger.LowSpeechConfidence => "low-speech-confidence",
        _ => "none",
    };

    // ------------------------------------------------------------------ wording

    /// <summary>
    /// Words a neutral clarification question (Part 4.4). The draft must pass the deterministic
    /// new-words check (<see cref="IntroducesNewTerms"/>) and the panel's leak guard against the
    /// rubric; otherwise the template (<see cref="Template"/>) is used, which only quotes the student.
    /// </summary>
    public async Task<ClarificationQuestion> WordAsync(VerificationInput input, VerificationDecision decision, string rubricForGuard,
        string dialogue, CancellationToken ct = default)
    {
        var template = Template(decision.Trigger, decision.Phrase, input.Language);
        if (decision.Trigger == ClarificationTrigger.StudentRequest)
            return new(template, true, "template: the student asked to clarify");
        var phrase = decision.Phrase ?? Tail(input.AnswerText, 10);
        var prompt = ExamPrompts.Fill(_prompts[ExamPrompts.ClarificationKey], new Dictionary<string, string?>
        {
            ["trigger"] = TriggerName(decision.Trigger),
            ["phrase"] = ExamPrompts.Student(phrase),
            ["question"] = input.Question,
            ["answer"] = ExamPrompts.Student(input.AnswerText),
        });
        string draft;
        try
        {
            var result = await panel.AskAsync(prompt, "", MindAttic.Legion.Quorum.Plurality, ct);
            draft = CleanDraft(!string.IsNullOrWhiteSpace(result.Consensus) ? result.Consensus
                : result.IndividualVotes.Where(v => !v.IsError).Select(v => v.Decision).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { draft = ""; }

        if (draft.Length == 0) return new(template, true, "template: no usable draft");
        if (draft.Length > 300 || !draft.Contains('?')) return new(template, true, "template: the draft was not a single short question");
        if (LeadingForm().IsMatch(draft)) return new(template, true, $"template: the draft offered an interpretation (\"{draft}\")");
        if (IntroducesNewTerms(draft, input.AnswerText, input.Question) is { Count: > 0 } added)
            return new(template, true, $"template: the draft added words the student did not use ({string.Join(", ", added.Take(5))})");
        if (!string.IsNullOrWhiteSpace(rubricForGuard))
        {
            var guard = ExamPrompts.Fill(_prompts[ExamPrompts.QuestionGuardKey], new Dictionary<string, string?>
            {
                ["rubric"] = rubricForGuard,
                ["dialogue"] = string.IsNullOrWhiteSpace(dialogue) ? "(none)" : dialogue,
                ["question"] = draft,
            });
            try
            {
                var d = await panel.DecideAsync(guard, [QuestionGenerator.Leaks, QuestionGenerator.Safe], "", ct);
                if (!string.Equals(d.Choice, QuestionGenerator.Safe, StringComparison.OrdinalIgnoreCase))
                    return new(template, true, string.IsNullOrEmpty(d.Choice) ? "template: the leak guard reached no decision" : $"template: the leak guard found a leak (\"{draft}\")");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new(template, true, "template: the leak guard could not run");
            }
        }
        return new(draft, true, "drafted");
    }

    /// <summary>The fixed fallback: only the student's own words, nothing to lead with.</summary>
    public static string Template(ClarificationTrigger trigger, string? phrase, string? language = null)
    {
        var p = phrase is null ? null : Clip(phrase.Trim().Trim('"', '\'', '“', '”'), 120);
        if (ExamLanguage.IsKorean(language))   // the same templates in Korean (GAD-T535): only the student's own words, nothing to lead with
        {
            if (trigger == ClarificationTrigger.StudentRequest) return "더 설명하고 싶으시군요. 답변에 덧붙이거나 설명하고 싶은 것을 말씀해 주세요.";
            if (string.IsNullOrWhiteSpace(p)) return "어떤 뜻이었는지 자신의 말로 조금 더 설명해 주시겠어요?";
            if (trigger == ClarificationTrigger.IncompleteSentence) return $"답변이 “{p}”에서 멈춘 것 같아요. 그 생각을 어떻게 마무리하시겠어요?";
            return $"“{p}”라고 하셨는데, 무슨 뜻인가요?";
        }
        if (ExamLanguage.IsSpanish(language))   // the same templates in Spanish (GAD-T610), usted: only the student's own words
        {
            if (trigger == ClarificationTrigger.StudentRequest) return "Quiere aclarar algo. Adelante: ¿qué le gustaría agregar o explicar sobre su respuesta?";
            if (string.IsNullOrWhiteSpace(p)) return "¿Podría decir un poco más sobre lo que quiso decir, con sus propias palabras?";
            if (trigger == ClarificationTrigger.IncompleteSentence) return $"Su respuesta parece detenerse después de «{p}». ¿Cómo terminaría esa idea?";
            return $"Cuando dice «{p}», ¿qué quiere decir?";
        }
        if (trigger == ClarificationTrigger.StudentRequest)
            return "You asked to clarify. Go ahead: what would you like to add or explain about your answer?";
        if (string.IsNullOrWhiteSpace(p))
            return "Could you say a little more about what you meant, in your own words?";
        if (trigger == ClarificationTrigger.IncompleteSentence)
            return $"Your answer seems to stop after \"{p}\". How would you finish that thought?";
        return $"When you say \"{p}\", what do you mean?";
    }

    /// <summary>
    /// Content words (5+ letters) in <paramref name="candidate"/> that are neither the student's own words
    /// nor the original question's nor neutral asking words: a clarification that adds terms can teach.
    /// </summary>
    public static IReadOnlyList<string> IntroducesNewTerms(string candidate, string answer, string question)
    {
        var allowed = Words(answer).Concat(Words(question)).Select(Stem).ToHashSet(StringComparer.Ordinal);
        return Words(candidate).Where(w => w.Length >= 5 && !NeutralWords.Contains(w) && !allowed.Contains(Stem(w)))
            .Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> Words(string? s) => WordRx().Matches((s ?? "").ToLowerInvariant()).Select(m => m.Value);

    /// <summary>A crude stem so "variables" matches "variable" and "affected" matches "affect".</summary>
    private static string Stem(string w)
    {
        foreach (var suffix in new[] { "ing", "ed", "es", "s", "ly" })
        {
            if (w.Length <= suffix.Length + 3 || !w.EndsWith(suffix, StringComparison.Ordinal)) continue;
            // "es" is a plural ending only after s, x, z, ch, sh; elsewhere ("enzymes") it is "e" + "s", handled by the "s" rule.
            if (suffix == "es" && !(w.EndsWith("ses", StringComparison.Ordinal) || w.EndsWith("xes", StringComparison.Ordinal) || w.EndsWith("zes", StringComparison.Ordinal)
                                    || w.EndsWith("ches", StringComparison.Ordinal) || w.EndsWith("shes", StringComparison.Ordinal))) continue;
            return DropFinalE(w[..^suffix.Length]);
        }
        return DropFinalE(w);
    }

    // So "enzyme" and "enzymes" (or "divide" and "dividing") reach the same stem.
    private static string DropFinalE(string w) => w.Length > 4 && w.EndsWith('e') ? w[..^1] : w;

    private static readonly HashSet<string> NeutralWords = new(StringComparer.Ordinal)
    {
        "about", "again", "answer", "anything", "based", "before", "being", "clarify", "clearer", "could", "describe", "describing",
        "detail", "details", "differently", "elaborate", "example", "exactly", "explain", "explaining", "finish", "further", "happen",
        "happens", "idea", "ideas", "intended", "little", "meant", "meaning", "mention", "mentioned", "might", "phrase", "please",
        "point", "precisely", "rather", "refer", "referring", "relationship", "saying", "sentence", "should", "something", "specific",
        "specifically", "start", "started", "still", "talking", "there", "these", "thing", "things", "think", "thinking", "those",
        "thought", "through", "unclear", "understand", "where", "which", "while", "whole", "words", "would", "write", "wrote",
        "written", "yourself", "already", "complete", "completed", "continue", "earlier", "first", "rephrase", "simply", "sure",
        "trying", "using", "wanted", "means", "mind", "other", "their", "there", "they're", "doesn", "isn't", "describes", "connection",
        // Spanish asking words (GAD-T610): never English words, so English drafts are checked exactly as before.
        "podría", "podria", "puede", "quiere", "quiso", "quería", "decir", "decirlo", "explicar", "explique", "explicarlo", "palabras",
        "propias", "significa", "refiere", "refería", "respuesta", "pregunta", "terminar", "terminaría", "pensamiento", "mencionó",
        "dicho", "parece", "usted", "cuando", "exactamente", "entender", "entendí", "seguro", "segura", "aclarar", "detalle", "detalles",
        "manera", "forma", "otra", "frase", "ideas", "agregar", "añadir", "sobre", "poco", "decía", "estaba", "hablar", "habla",
        "habló", "idea", "después", "antes", "todavía",
    };

    // ------------------------------------------------------------------ helpers

    private static string Tail(string? text, int words)
    {
        var w = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", w.TakeLast(words)).TrimEnd('.', '…', '-', '—', '–', ' ');
    }

    /// <summary>The first non-empty line without labels or wrapping quotes.</summary>
    internal static string CleanDraft(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var line = raw.Trim().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        foreach (var label in new[] { "Question:", "Examiner:", "Q:", "Clarification:" })
            if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase)) line = line[label.Length..].Trim();
        return line.Trim('"', '“', '”', '*', ' ');
    }

    private static string Clip(string? s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex WordRx();

    [GeneratedRegex(@"\b(can|could|may)\s+i\s+(clarify|rephrase|re-?word|explain\s+(that|this|it)\s+(better|again|differently))\b|\blet\s+me\s+(clarify|rephrase)\b|\bi\s+(want|would\s+like|'d\s+like)\s+to\s+(clarify|rephrase)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex StudentAsks();

    /// <summary>The student asks, in Korean, to clarify or correct their own answer (다시 설명해도 될까요, 정정할게요, 다시 말할게요).</summary>
    [GeneratedRegex(@"(다시|좀\s*더)\s*(설명|말)(해도|할게|하겠)|정정(할게|하겠|해도)|고쳐\s*(말|말할게|말하겠)|제\s*말은")]
    private static partial Regex KoreanStudentAsks();

    /// <summary>The student asks, in Spanish, to clarify or correct their own answer (quiero aclarar, déjeme corregir, me corrijo, quiero decir).</summary>
    [GeneratedRegex(@"\b(quiero|quisiera|me\s+gustar[íi]a)\s+(aclarar|corregir|reformular|explicar(lo)?\s+mejor)\b|\bd[ée]jeme\s+(aclarar|corregir|reformular|explicar(lo)?\s+mejor)\b|\bme\s+corrijo\b|\b(puedo|podr[íi]a)\s+(aclarar|corregir|reformular)\b|\blo\s+que\s+quiero\s+decir\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishStudentAsks();

    /// <summary>Confirmation-seeking forms that put an interpretation in the student's mouth.</summary>
    [GeneratedRegex(@"^\s*(so\s+)?(do|did|are|were|is|was)\s+you\s+(mean|saying|suggesting|implying|referring)\b|\bdo\s+you\s+mean\b|\bin\s+other\s+words\b|\bis\s+it\s+that\b|\b(or|versus)\b.{0,60}\?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex LeadingForm();
}
