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
        MoveKind.Synthesize => [AssessmentDimension.KnowledgeTransfer],
        MoveKind.Contradiction => [AssessmentDimension.Consistency],
        _ => [],
    };

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
        MoveKind.Apply => "transfer to an unfamiliar situation",
        MoveKind.ScenarioVariation => "consistent reasoning when one condition changes",
        MoveKind.AssumptionTesting => "the assumptions behind the conclusion",
        MoveKind.IndependentExplanation => "an explanation rebuilt from the student's own reasoning",
        MoveKind.Synthesize => "how two objectives connect",
        MoveKind.Hint => "a retry after a hint (assisted)",
        MoveKind.GuidedExplanation => "understanding after a guided explanation (assisted)",
        _ => move.ToString(),
    };
}
