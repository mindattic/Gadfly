using System.Runtime.CompilerServices;
using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Sources;
using Gadfly.Tests.Fakes;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>
/// GAD-T508: on the live turn one fast model judges the answer provisionally and steers the next move; the panel's full judgment
/// runs off the turn, is the one graded, replaces the provisional one for planning, and records a material disagreement.
/// </summary>
[TestFixture]
public class ProvisionalJudgmentTests
{
    /// <summary>A single fast model that answers the judge prompt with <see cref="Score"/> (0..10 per criterion).</summary>
    private sealed class ScriptedFastJudge : ILiveTurnModel
    {
        public double Score { get; set; } = 8;
        public bool Fail { get; set; }
        public int JudgeCalls { get; private set; }
        public string ProviderId => "claude";
        public string Model => "claude-sonnet-5";

        public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            if (userMessage.Contains("LEAKS or SAFE", StringComparison.Ordinal)) { yield return "SAFE"; yield break; }
            if (userMessage.Contains("You are one judge on a panel", StringComparison.Ordinal))
            {
                JudgeCalls++;
                if (Fail) throw new HttpRequestException("provider down");
                yield return ScriptedPanel.JudgeJson(Score, "Fast verdict.");
                yield break;
            }
            yield return "What does a plant do with the light it absorbs?";
        }
    }

    private static async Task<(SourceExamEngine Engine, ScriptedPanel Panel, ScriptedFastJudge Fast)> StartAsync(double panelScore = 8, double fastScore = 8)
    {
        var panel = new ScriptedPanel { JudgeScore = panelScore };
        var fast = new ScriptedFastJudge { Score = fastScore };
        var engine = await SourceExamEngine.StartAsync(panel, EngineKit.Biology(), new SessionPolicy { ConceptIds = ["photosynthesis"] },
            new ManualClock(EngineKit.T0));
        engine.AnalyzeResponses = false;
        engine.AssessTextStyle = false;
        engine.LiveTurnModel = fast;
        engine.DeferFullJudgment = true;
        return (engine, panel, fast);
    }

    private static Answer Said(string text, int second = 30) => new() { Text = text, CompletedAt = EngineKit.T0.AddSeconds(second) };

    [Test]
    public async Task TheLiveTurn_JudgesProvisionallyWithTheFastModel_AndNeverCallsThePanelJudges()
    {
        var (engine, panel, fast) = await StartAsync();
        await engine.NextQuestionAsync();
        var turn = await engine.SubmitAnswerAsync(Said("Plants use light energy to make glucose from carbon dioxide and water."));

        Assert.That(fast.JudgeCalls, Is.EqualTo(1));
        Assert.That(panel.Count(ScriptedPanel.Kind.Judge), Is.Zero);
        Assert.That(turn.Judgment, Is.Null, "the graded judgment is the panel's, still to come");
        Assert.That(turn.Provisional, Is.Not.Null);
        Assert.That(turn.Provisional!.Model, Is.EqualTo("claude-sonnet-5"));
        Assert.That(engine.Session.AwaitingJudgment, Is.Null, "a provisionally judged answer lets the exam plan on");
        Assert.That(engine.Session.PendingFullJudgments.Select(t => t.Index), Is.EqualTo(new[] { 0 }));

        var next = await engine.NextQuestionAsync();
        Assert.That(next, Is.Not.Null);
        Assert.That(next!.Index, Is.EqualTo(1));
    }

    [Test]
    public async Task TheFullJudgment_RunsOffTheTurn_IsTheOneGraded_AndReplacesTheProvisionalForPlanning()
    {
        var (engine, panel, _) = await StartAsync(panelScore: 3, fastScore: 9);
        await engine.NextQuestionAsync();
        await engine.SubmitAnswerAsync(Said("Plants use light energy to make glucose from carbon dioxide and water."));
        var provisionalMastery = engine.Session.Mastery["photosynthesis"].Estimate;

        var task = engine.StartFullJudgment();
        Assert.That(task, Is.Not.Null);
        Assert.That(engine.StartFullJudgment(), Is.SameAs(task), "one full judgment at a time, started once");
        var result = await task!;
        Assert.That(result.Error, Is.Null);
        Assert.That(await engine.ApplyFinishedJudgmentAsync(), Is.True);

        var judged = engine.Session.Turns[0];
        Assert.That(panel.Count(ScriptedPanel.Kind.Judge), Is.EqualTo(1));
        Assert.That(judged.Judgment!.OverallScore, Is.EqualTo(0.3).Within(1e-9));
        Assert.That(judged.Judgment.Votes, Has.Count.EqualTo(2), "the multi-model panel");
        Assert.That(judged.Provisional!.FullScore, Is.EqualTo(0.3).Within(1e-9));
        Assert.That(judged.Provisional.Disagreement, Is.Not.Null, "0.9 against 0.3 is a material disagreement");
        Assert.That(engine.Session.PendingFullJudgments, Is.Empty);
        Assert.That(engine.Session.Mastery["photosynthesis"].Estimate, Is.LessThan(provisionalMastery), "mastery is replayed from the panel's score");
        Assert.That(engine.Session.ForPlanning(), Is.SameAs(engine.Session), "nothing provisional is left to plan from");

        var finished = engine.TakeFinishedJudgments();
        Assert.That(finished, Has.Count.EqualTo(1));
        Assert.That(finished[0].Applied, Is.True);
        Assert.That(finished[0].Disagreement, Is.Not.Null);
        Assert.That(engine.TakeFinishedJudgments(), Is.Empty);
    }

    [Test]
    public async Task ThePlannerSeesTheProvisionalJudgment_WhileTheFullOneIsPending()
    {
        var (engine, _, _) = await StartAsync(fastScore: 2);
        await engine.NextQuestionAsync();
        await engine.SubmitAnswerAsync(Said("Plants make glucose somehow, I think, from the soil."));
        var view = engine.Session.ForPlanning();
        Assert.That(view, Is.Not.SameAs(engine.Session));
        Assert.That(view.Turns[0].Judgment!.OverallScore, Is.EqualTo(0.2).Within(1e-9));
        Assert.That(engine.Session.Turns[0].Judgment, Is.Null);
    }

    [Test]
    public async Task AFailingFastModel_PutsThePanelBackOnTheTurn()
    {
        var (engine, panel, fast) = await StartAsync();
        fast.Fail = true;
        await engine.NextQuestionAsync();
        var turn = await engine.SubmitAnswerAsync(Said("Plants use light energy to make glucose from carbon dioxide and water."));
        Assert.That(turn.Judgment, Is.Not.Null);
        Assert.That(turn.Provisional, Is.Null);
        Assert.That(panel.Count(ScriptedPanel.Kind.Judge), Is.EqualTo(1));
        Assert.That(engine.Session.PendingFullJudgments, Is.Empty);
    }

    [Test]
    public async Task EndingTheExam_WaitsForEveryFullJudgmentFirst()
    {
        var (engine, panel, _) = await StartAsync();
        await engine.NextQuestionAsync();
        await engine.SubmitAnswerAsync(Said("Plants use light energy to make glucose from carbon dioxide and water."));
        await engine.EndAsync(EndReason.Abandoned);
        Assert.That(engine.Session.IsEnded, Is.True);
        Assert.That(engine.Session.Turns[0].Judgment, Is.Not.Null);
        Assert.That(panel.Count(ScriptedPanel.Kind.Judge), Is.EqualTo(1));
        Assert.That(engine.TakeFinishedJudgments().Single().Applied, Is.True);
    }

    [Test]
    public async Task APendingTurn_SurvivesTheTranscript_AndResumes()
    {
        var (engine, panel, fast) = await StartAsync();
        await engine.NextQuestionAsync();
        await engine.SubmitAnswerAsync(Said("Plants use light energy to make glucose from carbon dioxide and water."));
        await engine.NextQuestionAsync();

        var restored = SessionTranscriptSerializer.Deserialize(SessionTranscriptSerializer.Serialize(engine.Session));
        Assert.That(restored.Turns[0].Provisional, Is.Not.Null);
        Assert.That(restored.PendingFullJudgments.Select(t => t.Index), Is.EqualTo(new[] { 0 }));
        Assert.That(restored.OpenTurn!.Index, Is.EqualTo(1));

        var resumed = await SourceExamEngine.ResumeAsync(panel, engine.Subject, restored, engine.Ledger, new ManualClock(EngineKit.T0));
        resumed.AnalyzeResponses = false;
        resumed.AssessTextStyle = false;
        resumed.LiveTurnModel = fast;
        resumed.DeferFullJudgment = true;
        Assert.That(await resumed.FinishFullJudgmentsAsync(), Is.EqualTo(1));
        Assert.That(resumed.Session.Turns[0].Judgment, Is.Not.Null);
    }

    [Test]
    public async Task ASpeculativeCopy_NeverRunsThePanelOnAPendingAnswer()
    {
        var (engine, _, fast) = await StartAsync();
        await engine.NextQuestionAsync();
        await engine.SubmitAnswerAsync(Said("Plants use light energy to make glucose from carbon dioxide and water."));
        await engine.NextQuestionAsync();
        engine.Speculative = true;
        fast.Fail = true;
        Assert.ThrowsAsync<InvalidOperationException>(() => engine.SubmitAnswerAsync(Said("Chlorophyll absorbs light in the leaves.", 90)));
    }

    [TestCase(0.9, 0.3, true)]
    [TestCase(0.8, 0.75, false)]
    [TestCase(0.62, 0.55, true)]   // positive against mixed: a different side of the evidence threshold
    public void AMaterialDisagreement_IsAScoreGapOrADifferentResult(double provisional, double full, bool disagrees) =>
        Assert.That(SourceExamEngine.Disagreement(EngineKit.Judgment(provisional), EngineKit.Judgment(full)) is not null, Is.EqualTo(disagrees));

    [Test]
    public void AnAporiaMismatch_IsADisagreement()
    {
        var aporia = new AnswerJudgment { Criteria = [], OverallScore = 0, Summary = "aporia" };
        Assert.That(SourceExamEngine.Disagreement(EngineKit.Judgment(0.4), aporia), Is.Not.Null);
    }
}
