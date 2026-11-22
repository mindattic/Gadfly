using Gadfly.Assessment;
using Gadfly.Server.Grading;
using Gadfly.Sources.Grading;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>
/// GAD-T480: a calibration recorded from double marks is stored against the models the panel runs now, so only answers those
/// models graded may feed it; a grade from before a model change would otherwise renew an expired calibration. Pure.
/// </summary>
[TestFixture]
public class DoubleMarkingModelsTests
{
    private static VoterGrade Voter(string provider, string model) => new(provider, model, 80, MasteryLevel.Application, "", false);

    private static readonly Dictionary<string, string> Now = new(StringComparer.OrdinalIgnoreCase) { ["claude"] = "claude-sonnet-5", ["openai"] = "gpt-5" };

    [Test]
    public void A_grade_from_the_current_models_counts()
    {
        Assert.That(DoubleMarkingService.GradedByCurrentModels([Voter("claude", "claude-sonnet-5"), Voter("OpenAI", "GPT-5")], Now), Is.True);
    }

    [Test]
    public void A_grade_from_an_earlier_model_does_not_count()
    {
        Assert.That(DoubleMarkingService.GradedByCurrentModels([Voter("claude", "claude-sonnet-4"), Voter("openai", "gpt-5")], Now), Is.False);
    }

    [Test]
    public void A_grade_from_a_member_that_left_the_panel_does_not_count()
    {
        Assert.That(DoubleMarkingService.GradedByCurrentModels([Voter("gemini", "gemini-3")], Now), Is.False);
    }

    [Test]
    public void A_grade_without_voter_detail_does_not_count_but_an_unknown_panel_skips_the_check()
    {
        Assert.That(DoubleMarkingService.GradedByCurrentModels([], Now), Is.False);
        Assert.That(DoubleMarkingService.GradedByCurrentModels([Voter("claude", "anything")], null), Is.True);
    }
}
