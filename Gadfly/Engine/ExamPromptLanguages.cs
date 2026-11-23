namespace Gadfly.Engine;

/// <summary>
/// The exam prompt set in each exam language, and with the plain-language examiner for a non-native speaker (GAD-T535, GAD-T536,
/// GAD-T555). Built from <see cref="ExamPrompts"/> by appending fixed language rules to the prompts a live exam uses; the English
/// wording itself is never edited. An English exam by an English-primary student (<see cref="AttemptLanguage.IsDefault"/>) runs on
/// the English set itself, byte for byte, with its own SHA-256.
/// <para>
/// Each variant is its own prompt set: id <c>exam-ko</c>, <c>exam-es</c>, <c>exam-en-plain</c>, <c>exam-ko-plain</c>, <c>exam-es-plain</c>,
/// the base version, and a hash
/// over its own texts, so every session records exactly which wording examined the student and an attempt can never resume on a
/// different language's prompts. <see cref="RulesVersion"/> is bumped whenever any rule text here changes.
/// </para>
/// <para>
/// Research note esl-and-korean (R5.1, R6.5): the judges are never told the student is a second-language speaker (labelling made
/// scoring worse); for them only the exam language matters. The plain-language rules go to the examiner's question prompts only.
/// Korean answers are judged directly against the English rubric (no machine translation of the answer), with each judge quoting the
/// student's Korean words and glossing them in English so a human can check the reading.
/// </para>
/// </summary>
public static class ExamPromptLanguages
{
    /// <summary>
    /// 1: Korean exams (questions, guard and judging in Korean over the English source) and the plain-language examiner. Spanish
    /// (GAD-T610) was added under the same version: its blocks are new and no existing block's text changed, so the English, Korean and
    /// plain sets keep their hashes.
    /// </summary>
    public const int RulesVersion = 1;

    /// <summary>The line every language rule block starts with; the mock panel reads the tag after it.</summary>
    public const string LanguageLine = "Exam language:";

    /// <summary>The line the plain-language block starts with.</summary>
    public const string PlainLine = "Plain-language examiner:";

    /// <summary>The prompt set an attempt in <paramref name="language"/> runs on: the base set itself for <see cref="AttemptLanguage.IsDefault"/>.</summary>
    public static PromptSet For(PromptSet baseSet, AttemptLanguage? language)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        if (language is null || language.IsDefault) return baseSet;
        return Cache.GetOrAdd((baseSet.Sha256, language.Exam, language.IsNonNative), _ => Build(baseSet, language.Exam, language.IsNonNative));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string, bool), PromptSet> Cache = new();

    private static PromptSet Build(PromptSet baseSet, string exam, bool plain)
    {
        var english = exam == ExamLanguage.English;
        var prompts = baseSet.Prompts.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        foreach (var key in prompts.Keys.ToList())
        {
            var rules = new List<string>();
            if (!english && ForeignRules(key, exam) is { } foreign) rules.Add(foreign);
            if (plain && PlainRules(key, exam) is { } simple) rules.Add(simple);
            if (rules.Count > 0) prompts[key] = prompts[key] + "\n\n" + string.Join("\n\n", rules);
        }
        var id = $"{baseSet.Id}-{exam}{(plain ? "-plain" : "")}";
        return new PromptSet(id, baseSet.Version, prompts);
    }

    /// <summary>The examiner-side prompts: the question writers and the clarification writer.</summary>
    private static bool Examiner(string key) => key is ExamPrompts.QuestionKey or ExamPrompts.LearningQuestionKey or ExamPrompts.ClarificationKey;

    private static string? ForeignRules(string key, string exam)
    {
        var name = ExamLanguage.EnglishName(exam);
        var header = $"{LanguageLine} {exam} ({name}), language rules version {RulesVersion}.";
        var source = $"The source material, concept titles and rubric are in English and remain the only standard of truth. The student hears the examiner and answers in {name}.";
        var register = exam switch
        {
            ExamLanguage.Korean => " Use the polite 해요체 register (for example \"설명해 주시겠어요?\", \"왜 그렇게 생각하세요?\") consistently, as one examiner speaking to one student.",
            ExamLanguage.Spanish => " Use neutral Latin American Spanish, understood in every Spanish-speaking country (no vosotros, no regional slang), and address the student as usted consistently (for example \"¿Podría explicar...?\", \"¿Por qué piensa eso?\"), never tú. Avoid wording that assumes the student's gender. Use the opening ¿ and ¡ marks.",
            _ => "",
        };
        var terms = $" Translate technical terms into the established {name} term; when the field usually keeps the English term, or {name} has no settled equivalent, say the {name} term followed by the English term in parentheses once.";
        return key switch
        {
            ExamPrompts.QuestionKey or ExamPrompts.LearningQuestionKey => $"""
                {header}
                {source} Write the question in natural spoken {name}, as a native {name}-speaking examiner would say it aloud.{register}{terms}
                Every rule above still applies in {name}: never reveal, hint at or confirm an answer point, and never translate a rubric point into the question.
                Reply with the {name} question only.
                """,
            ExamPrompts.ClarificationKey => $"""
                {header}
                The student's answer is in {name}. Write the clarification question in natural spoken {name}.{register} Quote the student's own {name} words exactly; add no term they did not use.
                """,
            ExamPrompts.QuestionGuardKey => $"""
                {header}
                The question and the conversation are in {name}; the rubric is in English. Judge meaning across the two languages: a {name} question that states, translates, paraphrases or presupposes an English rubric point the student has not said LEAKS, exactly as it would in English.
                Reply with the English word LEAKS or SAFE.
                """,
            ExamPrompts.JudgeKey => $"""
                {header}
                {source} Judge the meaning of the {name} answer directly against the English rubric and passages: a correct idea earns the same credit in {name} as in English, and a wrong one loses the same. Do not translate the answer first.
                Wherever you quote the student, copy their {name} words exactly and give your English gloss in brackets after the quote, so a human grader can check your reading. Write rationales and the verdict in English. Keep the JSON keys and criterion names in English.
                """,
            ExamPrompts.AnalysisKey => $"""
                {header}
                {source} Read the {name} answer directly against the English rubric and objectives. Every "quote" field must copy the student's {name} words exactly; write every other text field (claims, notes, misconceptions, uncertainties) in English, giving an English gloss of the quoted words. Keep the JSON keys and values like stance names in English.
                """,
            ExamPrompts.ClaimsKey => $"""
                {header}
                The answer is in {name}. Each "quote" must copy the student's {name} words exactly; write each "claim" in English, so it can be compared with the English rubric and with the student's other claims.
                """,
            ExamPrompts.ContradictionKey => $"""
                {header}
                The claims may be in {name} or in English. Compare their meaning across languages. Reply with the English word CONTRADICT or CONSISTENT.
                """,
            ExamPrompts.AporiaKey => exam == ExamLanguage.Korean
                ? $"""
                    {header}
                    The answer is in Korean. Korean ways of saying "I don't know" include 모르겠어요, 모르겠습니다, 잘 몰라요, 기억이 안 나요 and 생각이 안 나요; a hedge such as 아마 or 것 같아요 followed by an attempt is an answer. Reply with the English word APORIA or ANSWER.
                    """
                : exam == ExamLanguage.Spanish
                ? $"""
                    {header}
                    The answer is in Spanish. Spanish ways of saying "I don't know" include no lo sé, no sé, no me acuerdo, no lo recuerdo, ni idea and no tengo idea; a hedge such as creo que, tal vez or a lo mejor followed by an attempt is an answer. Reply with the English word APORIA or ANSWER.
                    """
                : $"""
                    {header}
                    The answer is in {name}. Reply with the English word APORIA or ANSWER.
                    """,
            ExamPrompts.VerificationKey => $"""
                {header}
                The answer is in {name}. Any "phrase" must copy the student's {name} words exactly; write the "reason" in English. Keep the JSON keys and trigger names in English.
                """,
            ExamPrompts.TextStyleKey => $"""
                {header}
                The answer is in {name}; judge its style as {name} speech or writing. Write the reason in English.
                """,
            _ => null,   // the concept map and rubric are built from the English source at ingestion, never per attempt
        };
    }

    /// <summary>
    /// The plain-language examiner (GAD-T555; research note esl-and-korean R4.2, after ISO 24495-1): only the question writers and the
    /// clarification writer get it. It never mentions the student's first language.
    /// </summary>
    private static string? PlainRules(string key, string exam)
    {
        if (!Examiner(key)) return null;
        var name = ExamLanguage.EnglishName(exam);
        var examples = exam == ExamLanguage.English
            ? " (no \"figure out\", \"come up with\", \"wouldn't it be...?\")"
            : "";
        return $"""
            {PlainLine} yes, plain-language rules version {RulesVersion}.
            Ask in plain {name}: one question only, at most about 20 words; keep the subject's technical terms, but use common everyday words for everything else; no idioms, phrasal verbs, slang, sarcasm, rhetorical questions or negative questions{examples}; name the thing you mean instead of "this" or "that one"; say what kind of answer you want ("Give one reason.", "Explain the steps.").
            Never correct or repeat back the student's grammar or pronunciation, and never comment on their language.
            """;
    }
}
