namespace Gadfly.Engine;

/// <summary>
/// Words the speech recognizer was unsure of in a spoken answer (GAD-T598). For a second-language speaker the judges are told these
/// words may be misheard, and when the answer was not convincing (so a misheard word may carry it) the examiner asks about one such
/// word, a brief word-level clarification inside the Socratic exchange, instead of the judges scoring it as wrong. The whole-answer
/// read-back stays removed (owner decision GAD-T405): only the one word is asked about, quoting what the recognizer heard, never
/// suggesting what it should have been.
/// </summary>
public static class UncertainWords
{
    /// <summary>Below this recognizer confidence a word is uncertain (the same floor as the response verifier's, GAD-T265).</summary>
    public const double Threshold = 0.6;

    /// <summary>At most this many word checks in one exam, so they never crowd out the examination.</summary>
    public const int MaxChecksPerSession = 3;

    /// <summary>The <see cref="QuestionAuthorship"/> source of a word-check question.</summary>
    public const string Source = "word-check";

    /// <summary>The uncertain words of <paramref name="answer"/>, in order, distinct: content-sized words only (not "a", "um", "은").</summary>
    public static IReadOnlyList<string> In(Answer? answer)
    {
        if (answer is not { Mode: "voice" } a || a.Words.Count == 0) return [];
        return a.Words.Where(w => w.Confidence < Threshold).Select(w => w.Text.Trim().Trim('.', ',', '?', '!', '"', '\'', '“', '”'))
            .Where(IsContentSized).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsContentSized(string word) =>
        LanguageText.HasHangul(word) ? word.Count(LanguageText.IsHangul) >= 2 : word.Count(char.IsLetterOrDigit) > 2;

    /// <summary>The note appended to a judge prompt when an answer has uncertain words (second-language speakers only).</summary>
    public static string JudgeNote(IReadOnlyList<string> words) =>
        $"\n\nSpeech recognition was unsure of these words in the answer: {string.Join(", ", words.Take(8).Select(w => $"\"{w}\""))}. "
        + "They may be misheard: never treat them as wrong content or a misconception. Judge on the reliable words, and if a score depends on one of these words, say so in the rationale.";

    /// <summary>The word-check question, quoting only the word as heard (no suggested meaning, nothing to lead with).</summary>
    public static string Question(string word, string? language) => ExamLanguage.IsKorean(language)
        ? $"한 단어를 제대로 들었는지 확인하고 싶어요. “{word}”라고 들렸는데, 어떤 단어였는지 다시 말씀해 주시거나 다른 말로 설명해 주시겠어요?"
        : ExamLanguage.IsSpanish(language)   // GAD-T610: usted, only the word as heard
        ? $"Quiero asegurarme de haber oído bien una palabra. Sonó como «{word}». ¿Podría repetir esa palabra o decirla de otra manera?"
        : $"I want to be sure I heard one word correctly. It sounded like “{word}”. Could you say that word again, or say it another way?";

    /// <summary>
    /// The word the examiner should ask about after <paramref name="last"/>, or null: a spoken answer with an uncertain word whose
    /// judgment is not yet convincing (the word may carry the answer), when the last question was not itself a word check and the
    /// exam's word checks are not used up.
    /// </summary>
    public static string? ToAsk(SocraticSession session, Turn? last)
    {
        if (last is not { Answer: { } answer } || last.QuestionAuthor?.How == Source) return null;
        if (session.Turns.Count(t => t.QuestionAuthor?.How == Source) >= MaxChecksPerSession) return null;
        if (SocraticSession.SteeringJudgment(last) is not { } judged || judged.OverallScore >= ObjectiveAssessor.PositiveAtOrAbove) return null;
        if (ObjectiveAssessor.IsAporia(judged) || ObjectiveAssessor.IsUnjudged(judged)) return null;
        return In(answer).FirstOrDefault();
    }
}
