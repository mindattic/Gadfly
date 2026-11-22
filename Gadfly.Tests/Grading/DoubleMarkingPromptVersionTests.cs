using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Sources.Grading;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>
/// GAD-T484: a calibration recorded from double marks records the grading prompt-set version, so only AI grades made under the
/// current version may feed it; grades from before the version was kept count as version 1. Pure.
/// </summary>
[TestFixture]
public class DoubleMarkingPromptVersionTests
{
    [Test]
    public void A_grade_under_the_current_prompts_counts_and_one_under_other_prompts_does_not()
    {
        Assert.That(DoubleMarkingService.GradedUnderCurrentPrompts(AiObjectiveGrader.PromptVersion), Is.True);
        Assert.That(DoubleMarkingService.GradedUnderCurrentPrompts(AiObjectiveGrader.PromptVersion + 1), Is.False);
        Assert.That(DoubleMarkingService.GradedUnderCurrentPrompts(1, current: 2), Is.False);
    }

    [Test]
    public void An_existing_grade_counts_as_version_one()
    {
        Assert.That(new AiObjectiveGrade().GradingPromptVersion, Is.EqualTo(1));
    }

    [Test]
    public void The_refusal_names_why_no_answer_counts()
    {
        Assert.That(DoubleMarkingService.StaleRefusal(0, 0), Does.Contain("No answer has a panel grade"));
        Assert.That(DoubleMarkingService.StaleRefusal(2, 0), Does.Contain("2 were graded by earlier models").And.Not.Contain("earlier grading prompts"));
        Assert.That(DoubleMarkingService.StaleRefusal(0, 3), Does.Contain("3 were graded under earlier grading prompts").And.Not.Contain("earlier models"));
        var both = DoubleMarkingService.StaleRefusal(1, 4);
        Assert.That(both, Does.Contain("1 were graded by earlier models").And.Contain("4 were graded under earlier grading prompts"));
    }
}
