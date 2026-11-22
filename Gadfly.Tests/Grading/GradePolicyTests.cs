using Gadfly.Server.Data;
using Gadfly.Server.Review;
using NUnit.Framework;
using SessionReport = Gadfly.Engine.SessionReport;

namespace Gadfly.Tests.Grading;

/// <summary>
/// GAD-T460: the review pages' "(override)" flag compares the instructor's grade with the objective levels (the reported result
/// since GAD-T425), never with the running-estimate recommendation.
/// </summary>
[TestFixture]
public class GradePolicyTests
{
    private static IReadOnlyList<ObjectiveLevel> Levels(params int[] levels) =>
        levels.Select((l, i) => new ObjectiveLevel($"Objective {i + 1}", l, l == 0 ? "Insufficient evidence" : $"Level {l}")).ToList();

    [Test]
    public void Outcome_IsReadFromEveryObjectivesLevel_NotAnAverage()
    {
        Assert.That(GradePolicy.Outcome(Levels(3, 4, 5)), Is.EqualTo(SessionReport.Mastered));
        Assert.That(GradePolicy.Outcome(Levels(5, 5, 2)), Is.EqualTo(SessionReport.Partial), "one objective below Level 3 is not mastered");
        Assert.That(GradePolicy.Outcome(Levels(4, 0)), Is.EqualTo(SessionReport.Partial), "an objective without enough evidence is not mastered");
        Assert.That(GradePolicy.Outcome(Levels(1, 1, 0)), Is.EqualTo(SessionReport.NotDemonstrated));
    }

    [Test]
    public void NoLevels_OrOnlyInsufficientEvidence_HaveNothingToOverride()
    {
        Assert.That(GradePolicy.Outcome([]), Is.Null);
        Assert.That(GradePolicy.Outcome(Levels(0, 0)), Is.Null);
        Assert.That(GradePolicy.Overrides("F", 10, Levels(0, 0)), Is.False);
        Assert.That(GradePolicy.Overrides("A", 95, []), Is.False);
    }

    [Test]
    public void AGradeNamingAnotherOutcome_IsAnOverride()
    {
        Assert.That(GradePolicy.Overrides("A", null, Levels(4, 4)), Is.False);
        Assert.That(GradePolicy.Overrides("pass", null, Levels(4, 4)), Is.False);
        Assert.That(GradePolicy.Overrides("C", null, Levels(4, 4)), Is.True);
        Assert.That(GradePolicy.Overrides("B", null, Levels(4, 2)), Is.False);
        Assert.That(GradePolicy.Overrides("F", null, Levels(1, 1)), Is.False);
        Assert.That(GradePolicy.Overrides("A", null, Levels(1, 1)), Is.True);
    }

    [Test]
    public void AScoreFarOutsideTheLevelsRange_IsAnOverride()
    {
        // Levels 2 and 4 span 40% to 80%; 10 points either side still agrees.
        Assert.That(GradePolicy.Overrides("B", 35, Levels(2, 4)), Is.False);
        Assert.That(GradePolicy.Overrides("B", 89, Levels(2, 4)), Is.False);
        Assert.That(GradePolicy.Overrides("B", 25, Levels(2, 4)), Is.True);
        Assert.That(GradePolicy.Overrides("B", 95, Levels(2, 4)), Is.True);
    }

    [Test]
    public void TheShownFlag_IsWorkedOutFromTheLevels_NotTheStoredOldFlag()
    {
        var oldRow = new GradeDecision { Grade = "A", Score = 90, OverridesRecommendation = true };
        Assert.That(GradePolicy.Overrides(oldRow, Levels(5, 4)), Is.False);
        Assert.That(GradePolicy.Overrides(new GradeDecision { Grade = "A", OverridesRecommendation = false }, Levels(1, 2)), Is.True);
    }
}
