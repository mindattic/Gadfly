namespace Gadfly.Engine;

/// <summary>
/// The exam prompt set (docs/SOCRATIC_PLATFORM.md). Like the personal tool's canonical prompts, these
/// are constants: no setting, flag or tenant can change their wording. Any wording change bumps
/// <see cref="Version"/>, which changes the SHA-256 every session records, so an audit can always
/// tell which text examined a student. Placeholders in {braces} are filled by the engine.
/// </summary>
public static class ExamPrompts
{
    public const string SetId = "exam";
    public const int Version = 1;

    public const string QuestionKey = "question";
    public const string QuestionGuardKey = "question-guard";
    public const string JudgeKey = "judge";
    public const string ClaimsKey = "claims";
    public const string ContradictionKey = "contradiction";
    public const string AporiaKey = "aporia";
    public const string ConceptMapKey = "concept-map";
    public const string RubricKey = "rubric";
    public const string TextStyleKey = "text-style";

    public const string Question = """
        You are the examiner in a spoken Socratic oral exam. Your purpose is to find out what the
        student genuinely understands about one concept, using only the source material below as the
        standard of truth. You never lecture and you never reveal or hint at the answer.

        Write the next question for this move: {move}
        Move meanings:
        - Elicit: ask the student to explain the concept in their own words.
        - Clarify: ask what they meant by a specific vague, ambiguous or misused term in their last answer.
        - Probe: ask why their last claim is true, or what follows from it.
        - Counterexample: present a concrete case, consistent with the source, that their stated definition
          or rule handles wrongly or does not cover, and ask them to account for it.
        - Contradiction: quote the two claims of theirs listed under "Claims to confront" and ask how both
          can be true.
        - Apply: describe a short new situation the source material does not discuss directly, where the
          concept is needed, and ask what happens or what they would do.
        - Synthesize: ask how this concept relates to the second concept named below.

        Rules:
        - One question only, at most 40 words, natural spoken English: it will be read aloud.
        - Build on the student's own words where you can, so the question could not have been prepared in advance.
        - Do not repeat a question already asked. Do not ask yes/no questions.
        - Never include rubric points, definitions or facts the student has not already said themselves.
        - Output only the question text.

        Concept: {concept}
        Second concept (Synthesize only): {related_concept}
        Difficulty (1 gentle - 5 rigorous): {difficulty}
        Claims to confront (Contradiction only): {target_claims}

        Source material:
        {grounding}

        Conversation so far:
        {dialogue}
        """;

    public const string QuestionGuard = """
        You check exam questions before a student hears them. Reply with exactly one word: LEAKS or SAFE.

        LEAKS means the question states or strongly hints at any of the expected answer points below
        that the student has not already said in the conversation. SAFE means it does not.

        Expected answer points:
        {rubric}

        Conversation so far:
        {dialogue}

        Candidate question:
        {question}
        """;

    public const string Judge = """
        You are one judge on a panel grading a student's spoken answer in a Socratic oral exam.
        Grade only against the numbered source passages and the rubric criteria. The source is the
        standard of truth, even where you believe it is incomplete. Answers were transcribed from
        speech: ignore filler words, false starts and transcription errors that do not change meaning,
        and never penalize grammar, accent or fluency.

        For each criterion, give a score and a one-sentence rationale, and cite the passage numbers you
        relied on. A criterion the question did not call for is scored on what the answer did say.
        Credit correct understanding expressed in the student's own words as fully as textbook wording.
        Penalize statements that contradict the source, and confident but empty answers.

        Criteria:
        - accuracy: agreement with the source passages.
        - reasoning: the answer explains why or how, not only what.
        - consistency: no conflict with the student's earlier claims (listed below), unless they
          explicitly corrected themselves, which is a strength.
        - completeness: covers the rubric points this question called for.

        Question move: {move}
        Concept: {concept}
        Rubric points:
        {rubric}

        Student's earlier claims:
        {claims}

        Question:
        {question}

        Student's answer:
        {answer}
        """;

    public const string Claims = """
        Extract the factual or conceptual claims the student asserted in this answer, as a JSON array
        of objects: [{"quote": "<the student's exact words>", "claim": "<the claim restated in one plain sentence>"}].
        Include only things the student asserted as true, not questions, hedges they withdrew, or
        restatements of the examiner's question. Return [] when there are none. Output only JSON.

        Concept: {concept}
        Question: {question}
        Answer: {answer}
        """;

    public const string Contradiction = """
        A student made both of these claims during an oral exam. Reply with exactly one word:
        CONTRADICT if both cannot be true at the same time in the sense the student meant them,
        CONSISTENT otherwise. A later claim that refines or qualifies an earlier one is CONSISTENT.

        Earlier claim (turn {first_turn}): {first_claim}
        Later claim (turn {second_turn}): {second_claim}
        """;

    public const string Aporia = """
        Reply with exactly one word: APORIA if the student's answer honestly says they do not know or
        cannot answer (in any wording), ANSWER otherwise. A wrong or partial attempt is ANSWER.

        Question: {question}
        Answer: {answer}
        """;

    public const string ConceptMap = """
        From the numbered source passages below, identify the concepts a student must understand to
        have learned this material. Return JSON only:
        {"concepts": [{"key": "<kebab-case id>", "title": "<short name>", "definition": "<one or two sentences, from the source>",
          "keyFacts": ["..."], "misconceptions": ["<a plausible wrong belief a learner might hold>"],
          "passages": [<passage numbers>]}],
         "relations": [{"from": "<key>", "to": "<key>", "kind": "prerequisite|part-of|contrasts-with|causes|example-of"}]}
        Prefer fewer, substantial concepts over many trivial ones. Every definition and fact must be
        supported by the cited passages; do not add outside knowledge.

        Passages:
        {passages}
        """;

    public const string Rubric = """
        Write the grading rubric for one concept of an oral exam, from the numbered source passages
        only. Return JSON only:
        {"understanding": [{"text": "<a point a student who truly understands would be able to explain>", "weight": <1-3>, "passages": [<numbers>]}],
         "acceptableVariants": [{"text": "<a correct alternative way to put it>", "passages": [<numbers>]}],
         "misconceptions": [{"text": "<a wrong belief to watch for>", "passages": [<numbers that refute it>]}]}
        Focus on understanding (why and how), not on recalling wording.

        Concept: {concept}
        Definition: {definition}

        Passages:
        {passages}
        """;

    public const string TextStyle = """
        Estimate how likely it is that this spoken-exam answer was read aloud from AI-generated or
        written text rather than composed while speaking. Compare it with the same student's other
        answers. Reply with JSON only: {"score": <0.0-1.0>, "reason": "<one sentence>"}.
        Signals of reading: polished written structure, enumerations, no self-correction, vocabulary
        unlike the student's other answers. Signals of speaking: false starts, self-corrections, filler,
        consistent personal phrasing. This is weak evidence on its own and is only shown to a human reviewer.

        Student's other answers:
        {other_answers}

        Answer to assess:
        {answer}
        """;

    /// <summary>The exam prompt set, version <see cref="Version"/>.</summary>
    public static readonly PromptSet Set = new(SetId, Version, new Dictionary<string, string>
    {
        [QuestionKey] = Question,
        [QuestionGuardKey] = QuestionGuard,
        [JudgeKey] = Judge,
        [ClaimsKey] = Claims,
        [ContradictionKey] = Contradiction,
        [AporiaKey] = Aporia,
        [ConceptMapKey] = ConceptMap,
        [RubricKey] = Rubric,
        [TextStyleKey] = TextStyle,
    });

    /// <summary>Fills {placeholders} in <paramref name="template"/>. Unknown placeholders are left as is.</summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string?> values)
    {
        var text = template;
        foreach (var (key, value) in values)
            text = text.Replace("{" + key + "}", value ?? "", StringComparison.Ordinal);
        return text;
    }
}
