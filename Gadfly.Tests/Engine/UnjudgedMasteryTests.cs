using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Sources;
using Gadfly.Tests.Fakes;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>GAD-T176: a failed panel is not a zero answer, so it must not lower the concept's mastery estimate.</summary>
[TestFixture]
public class UnjudgedMasteryTests
{
    [Test]
    public async Task AnAnswerNoJudgeCouldGrade_LeavesTheMasteryEstimateAtThePrior()
    {
        var panel = new ScriptedPanel
        {
            FanOut = (kind, _) => kind == ScriptedPanel.Kind.Judge ? [new PanelReply("alpha", "", "provider down")] : null,
        };
        var subject = EngineKit.Biology();
        var engine = await SourceExamEngine.StartAsync(panel, subject, new SessionPolicy { ConceptIds = ["photosynthesis"] },
            new ManualClock(EngineKit.T0));
        engine.AnalyzeResponses = false;
        engine.AssessTextStyle = false;

        var turn = await engine.NextQuestionAsync();
        Assert.That(turn, Is.Not.Null);
        var judged = await engine.SubmitAnswerAsync(new Answer
        {
            Text = "Plants use light energy to make glucose from carbon dioxide and water.",
            CompletedAt = EngineKit.T0.AddSeconds(30),
        });

        Assert.That(judged.Judgment!.NeedsHumanReview, Is.True);
        Assert.That(engine.Session.Mastery["photosynthesis"].Estimate, Is.EqualTo(MasteryModel.Prior));
    }
}
