using Gadfly.Assessment;

namespace Gadfly.Engine;

/// <summary>One rubric criterion of an objective: which Part 5.1 dimension it assesses and how much it weighs.</summary>
public sealed record ObjectiveCriterion(string Name, AssessmentDimension Dimension, decimal Weight = 1m, string? Description = null);

/// <summary>
/// A learning objective the examination control engine examines (GAD-T89, Component G). It is grounded in one
/// or more source-material concepts (questions and judging draw on their passages and rubrics) and assessed
/// on its rubric criteria. Exams without objectives get one implicit objective per concept.
/// </summary>
public sealed record ExamObjective
{
    /// <summary>Stable key within the assessment (LearningObjective.Key, or the concept key when implicit).</summary>
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    /// <summary>Concepts the objective is grounded in. The engine fills it from the title when it is empty.</summary>
    public IReadOnlyList<string> ConceptKeys { get; init; } = [];
    public IReadOnlyList<ObjectiveCriterion> Criteria { get; init; } = [];
    public decimal Weight { get; init; } = 1m;
    /// <summary>
    /// Learning Mode's fixed hint ladder (GAD-T271): the instructor's rungs, weakest first, given in order to every student who
    /// needs help. Empty uses <see cref="HintLadders.Default"/>. No model writes or rewords a hint.
    /// </summary>
    public IReadOnlyList<string> HintLadder { get; init; } = [];
    /// <summary>True when made from a concept because the exam defines no objectives.</summary>
    public bool Implicit { get; init; }

    /// <summary>The dimensions this objective is assessed on (its criteria's, or the default set).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<AssessmentDimension> Dimensions =>
        Criteria.Count > 0 ? Criteria.Select(c => c.Dimension).Distinct().ToList() : ExamObjectives.DefaultDimensions;
}

public static class ExamObjectives
{
    /// <summary>Dimensions an objective without rubric criteria is assessed on.</summary>
    public static readonly IReadOnlyList<AssessmentDimension> DefaultDimensions =
    [
        AssessmentDimension.FactualKnowledge,
        AssessmentDimension.ConceptualUnderstanding,
        AssessmentDimension.LogicalReasoning,
        AssessmentDimension.Application,
    ];

    /// <summary>
    /// The objectives a session examines: the policy's own, or one implicit objective per concept (the
    /// policy's <see cref="SessionPolicy.ConceptIds"/>, else <paramref name="fallbackConceptIds"/>).
    /// </summary>
    public static IReadOnlyList<ExamObjective> Resolve(SessionPolicy policy, IEnumerable<string> fallbackConceptIds,
        Func<string, string>? conceptTitle = null)
    {
        if (policy.Objectives.Count > 0) return policy.Objectives;
        var ids = policy.ConceptIds.Count > 0 ? policy.ConceptIds : fallbackConceptIds.ToList();
        return ids.Select(id => Implicit(id, conceptTitle?.Invoke(id) ?? id)).ToList();
    }

    public static ExamObjective Implicit(string conceptId, string title) =>
        new() { Key = conceptId, Title = title, ConceptKeys = [conceptId], Implicit = true };

    /// <summary>The objective a turn belongs to (older transcripts have none recorded: their concept is the objective).</summary>
    public static string KeyOf(Turn turn) => turn.ObjectiveKey ?? turn.ConceptId;

    /// <summary>Default for <see cref="SessionPolicy.MinSummativeObjectives"/> (GAD-T267).</summary>
    public const int DefaultMinSummativeObjectives = 4;

    public const string Summative = "summative";
    public const string Formative = "formative";

    /// <summary>
    /// Whether a session's per-objective scores may be used summatively (GAD-T267). Learning Mode is always formative.
    /// An examination that examined fewer than <see cref="SessionPolicy.MinSummativeObjectives"/> objectives is formative
    /// too: one short oral samples too little content to be reliable whatever the scorers (research note 02).
    /// </summary>
    public static (string Use, string Reason) ScoreUse(SessionPolicy policy, int objectivesExamined)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.Mode == AssessmentMode.Learning)
            return (Formative, "Learning Mode session: scores include help given and are for practice, not grading.");
        var min = Math.Max(1, policy.MinSummativeObjectives);
        if (objectivesExamined < min)
            return (Formative, $"Only {objectivesExamined} of the minimum {min} objectives were examined. One short oral samples too " +
                               "little content for a reliable grade, so treat these per-objective scores as formative feedback.");
        return (Summative, $"{objectivesExamined} objectives examined (minimum {min} for summative use).");
    }
}

/// <summary>
/// What each move can find out (deterministic, used by the planner and the evidence assessor): the
/// understanding level it probes (Part 5.3), the dimensions it gathers evidence on, and whether it is
/// instruction (Learning Mode) rather than a question.
/// </summary>
public static class MoveProfiles
{
    /// <summary>The level a good answer to <paramref name="move"/> is evidence for; 0 when it probes no level.</summary>
    public static int ProbedLevel(MoveKind move) => move switch
    {
        MoveKind.Elicit => 2,
        MoveKind.IndependentExplanation => 3,
        MoveKind.Probe => 3,
        MoveKind.CausalReasoning => 3,
        MoveKind.Apply => 4,
        MoveKind.ScenarioVariation => 4,
        MoveKind.Counterexample => 5,
        MoveKind.AssumptionTesting => 5,
        MoveKind.Synthesize => 5,
        _ => 0, // Clarify, Contradiction, Close, Hint, GuidedExplanation
    };

    /// <summary>The dimensions an answer to <paramref name="move"/> mainly shows.</summary>
    public static IReadOnlyList<AssessmentDimension> Dimensions(MoveKind move) => move switch
    {
        MoveKind.Elicit => [AssessmentDimension.FactualKnowledge, AssessmentDimension.ConceptualUnderstanding],
        MoveKind.IndependentExplanation => [AssessmentDimension.ConceptualUnderstanding],
        MoveKind.Probe => [AssessmentDimension.ConceptualUnderstanding, AssessmentDimension.LogicalReasoning],
        MoveKind.CausalReasoning => [AssessmentDimension.LogicalReasoning],
        MoveKind.Apply => [AssessmentDimension.Application, AssessmentDimension.KnowledgeTransfer],
        MoveKind.ScenarioVariation => [AssessmentDimension.Application, AssessmentDimension.LogicalReasoning],
        MoveKind.Counterexample => [AssessmentDimension.MisconceptionRecognition, AssessmentDimension.LogicalReasoning],
        MoveKind.AssumptionTesting => [AssessmentDimension.MisconceptionRecognition, AssessmentDimension.Metacognition],
        // Connecting two objectives shows relational understanding, not transfer: only Apply credits transfer (GAD-T268).
        MoveKind.Synthesize => [AssessmentDimension.ConceptualUnderstanding, AssessmentDimension.LogicalReasoning],
        MoveKind.Contradiction => [AssessmentDimension.Consistency],
        _ => [],
    };

    /// <summary>
    /// True only for Apply: a situation new to the student is the one move that probes transfer (GAD-T268). A what-if inside
    /// a situation already discussed (ScenarioVariation) is extrapolation, which Bloom puts under Comprehension, and
    /// connecting two objectives (Synthesize) is relational understanding (research note 03: Bloom1956, BarnettCeci2002).
    /// </summary>
    public static bool CreditsTransfer(MoveKind move) => move == MoveKind.Apply;

    /// <summary>
    /// The dimension an evidence item may be credited on, given the move that drew it (GAD-T268): knowledge transfer is
    /// credited only from Apply. From ScenarioVariation it is extrapolation, credited as application; from any other move,
    /// as conceptual understanding. Every other dimension stands as the panel gave it.
    /// </summary>
    public static AssessmentDimension CreditedDimension(MoveKind move, AssessmentDimension dimension) =>
        dimension != AssessmentDimension.KnowledgeTransfer || CreditsTransfer(move) ? dimension
        : move == MoveKind.ScenarioVariation ? AssessmentDimension.Application
        : AssessmentDimension.ConceptualUnderstanding;

    /// <summary>
    /// True for a follow-up question: any examining move after the objective's opening Elicit (not Close, and not Learning
    /// Mode's hints). Counted per objective so a long thread is visible to reviewers instead of quietly shaping a score (GAD-T267).
    /// </summary>
    public static bool IsFollowUp(MoveKind move) => move is not (MoveKind.Elicit or MoveKind.Close) && !IsAssistance(move);

    /// <summary>True for Learning Mode's instruction moves: what follows them is assisted evidence.</summary>
    public static bool IsAssistance(MoveKind move) => move is MoveKind.Hint or MoveKind.GuidedExplanation;

    /// <summary>The questioning strategies the planner chooses among by information value (Part 2.3 A, B, C, D, E, G, H).</summary>
    public static readonly IReadOnlyList<MoveKind> Strategies =
    [
        MoveKind.IndependentExplanation,
        MoveKind.Probe,
        MoveKind.CausalReasoning,
        MoveKind.Apply,
        MoveKind.ScenarioVariation,
        MoveKind.Counterexample,
        MoveKind.AssumptionTesting,
    ];

    /// <summary>Plain description of what a move tests, for reasons shown to reviewers.</summary>
    public static string Tests(MoveKind move) => move switch
    {
        MoveKind.Elicit => "an explanation in the student's own words",
        MoveKind.Clarify => "what the student meant",
        MoveKind.Probe => "the reasoning behind the answer (conceptual probing)",
        MoveKind.CausalReasoning => "cause and effect",
        MoveKind.Counterexample => "whether the explanation survives a challenging case",
        MoveKind.Contradiction => "two of the student's own claims that conflict",
        MoveKind.Apply => "transfer to a situation new to the student",
        MoveKind.ScenarioVariation => "consistent reasoning when one condition changes (extrapolation within a situation already discussed, not transfer)",
        MoveKind.AssumptionTesting => "the assumptions behind the conclusion",
        MoveKind.IndependentExplanation => "an explanation rebuilt from the student's own reasoning",
        MoveKind.Synthesize => "how two objectives connect",
        MoveKind.Hint => "a retry after a hint (assisted)",
        MoveKind.GuidedExplanation => "understanding after a guided explanation (assisted)",
        _ => move.ToString(),
    };
}
