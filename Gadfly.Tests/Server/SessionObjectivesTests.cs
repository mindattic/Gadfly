using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Sessions;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// An assessment's learning objectives become what the engine examines (GAD-T120): one objective per row in the
/// instructor's order, grounded in its concept, assessed on its criteria in order, with its fixed hint ladder (GAD-T271).
/// </summary>
[TestFixture]
public class SessionObjectivesTests
{
    private static LearningObjective Objective(string key, int ordinal, string? concept = null, string hints = "[]") => new()
    {
        Key = key, Title = "Title " + key, Description = null, ConceptKey = concept, Weight = 2m, Ordinal = ordinal, HintLadderJson = hints,
        Criteria =
        [
            new RubricCriterion { Name = "Reasoning", Dimension = AssessmentDimension.ConceptualUnderstanding, Weight = 1m, Ordinal = 2 },
            new RubricCriterion { Name = "Accuracy", Dimension = AssessmentDimension.FactualKnowledge, Weight = 3m, Ordinal = 1, Description = "Correct facts" },
        ],
    };

    [Test]
    public void Objectives_FollowTheInstructorsOrder_WithTheirCriteriaInOrder()
    {
        var result = SessionObjectives.From([Objective("b", 2, "concept-b"), Objective("a", 1, "concept-a")]);
        Assert.That(result.Select(o => o.Key), Is.EqualTo(new[] { "a", "b" }));
        var a = result[0];
        Assert.That(a.Title, Is.EqualTo("Title a"));
        Assert.That(a.Description, Is.EqualTo(""));
        Assert.That(a.ConceptKeys, Is.EqualTo(new[] { "concept-a" }));
        Assert.That(a.Weight, Is.EqualTo(2m));
        Assert.That(a.Criteria.Select(c => c.Name), Is.EqualTo(new[] { "Accuracy", "Reasoning" }));
        Assert.That(a.Criteria[0], Is.EqualTo(new Gadfly.Engine.ObjectiveCriterion("Accuracy", AssessmentDimension.FactualKnowledge, 3m, "Correct facts")));
    }

    [Test]
    public void ObjectiveWithoutAConcept_HasNoConceptKeys()
    {
        Assert.That(SessionObjectives.From([Objective("a", 0, "  ")]).Single().ConceptKeys, Is.Empty);
        Assert.That(SessionObjectives.From([Objective("a", 0, null)]).Single().ConceptKeys, Is.Empty);
    }

    [Test]
    public void NoObjectives_IsAnEmptyList()
    {
        Assert.That(SessionObjectives.From([]), Is.Empty);
    }

    [Test]
    public void HintLadder_IsTrimmed_AndBlankRungsDropped()
    {
        var ladder = SessionObjectives.HintLadder(Objective("a", 0, hints: "[\"  Think of groups \", \"\", \"   \", \"Count them\"]"));
        Assert.That(ladder, Is.EqualTo(new[] { "Think of groups", "Count them" }));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not json")]
    [TestCase("{\"a\":1}")]
    [TestCase("null")]
    public void UnreadableOrEmptyHintLadder_IsEmpty(string json)
    {
        Assert.That(SessionObjectives.HintLadder(Objective("a", 0, hints: json)), Is.Empty);
    }
}
