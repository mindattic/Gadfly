using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>GAD-T95/T96: blind visibility, superseding human grades, and combined scoring rules.</summary>
[TestFixture]
public class GradingRulesTests
{
    private static LearningObjective Obj(string key, decimal weight = 1m, decimal max = 100m) => new() { Key = key, Title = key, Weight = weight, MaxScore = max };

    [TestCase(GradeStatus.HumanReviewPending, true, false)]
    [TestCase(GradeStatus.AiGradingPending, true, false)]
    [TestCase(GradeStatus.ReconciliationRequired, true, true)]
    [TestCase(GradeStatus.AwaitingFinalApproval, true, true)]
    [TestCase(GradeStatus.Published, true, true)]
    [TestCase(GradeStatus.HumanReviewPending, false, true)]
    public void BlindGrading_HidesTheAiUntilTheHumanSubmits(GradeStatus status, bool blind, bool visible) =>
        Assert.That(GradingService.AiVisible(new PolicySnapshot { Rules = new GradingPolicyRules { BlindHumanGrading = blind } }, status), Is.EqualTo(visible));

    [Test]
    public void CurrentHuman_IsTheNewestNotSuperseded()
    {
        var o = Guid.NewGuid();
        var first = new HumanObjectiveGrade { ObjectiveId = o, Score = 50, SubmittedAt = DateTimeOffset.UnixEpoch };
        var second = new HumanObjectiveGrade { ObjectiveId = o, Score = 70, SupersedesId = first.Id, SubmittedAt = DateTimeOffset.UnixEpoch.AddMinutes(1) };
        Assert.That(GradingService.CurrentHuman([second, first], o), Is.SameAs(second));
    }

    [Test]
    public void Scenario_G_95vs60_RequiresReconciliation()
    {
        var o = Obj("x");
        var (r, _) = GradingService.TryCompute(new PolicySnapshot(),
            [(o, new AiObjectiveGrade { Score = 95, Level = MasteryLevel.TransferAndCriticalReasoning }, new HumanObjectiveGrade { Score = 60 }, null)]);
        Assert.That(r!.RequiresReconciliation, Is.True);
        Assert.That(r.Objectives[0].DifferencePoints, Is.EqualTo(35m));
    }

    [Test]
    public void Scenario_F_Agreement_YieldsTheWeightedScore()
    {
        var (r, _) = GradingService.TryCompute(new PolicySnapshot(),
            [(Obj("x"), new AiObjectiveGrade { Score = 85, Level = MasteryLevel.Application }, new HumanObjectiveGrade { Score = 90 }, null)]);
        Assert.That(r!.RequiresReconciliation, Is.False);
        Assert.That(r.FinalPercent, Is.EqualTo(88m));
    }

    [Test]
    public void ResolvedScores_ReplaceTheOriginals()
    {
        var rec = new GradeReconciliation { ResolvedAiScore = 62, ResolvedHumanScore = 60 };
        var (r, _) = GradingService.TryCompute(new PolicySnapshot(),
            [(Obj("x"), new AiObjectiveGrade { Score = 95, Level = MasteryLevel.Application }, new HumanObjectiveGrade { Score = 60 }, rec)]);
        Assert.That(r!.RequiresReconciliation, Is.False);
        Assert.That(r.Objectives[0].Combined, Is.EqualTo(60.8m));
    }

    [Test]
    public void InsufficientAiEvidence_LeavesTheHumanScoreAlone()
    {
        var (r, excluded) = GradingService.TryCompute(new PolicySnapshot(),
            [(Obj("x"), new AiObjectiveGrade { Score = 0, Level = MasteryLevel.InsufficientEvidence }, new HumanObjectiveGrade { Score = 70 }, null)]);
        Assert.That(excluded, Does.Contain("x"));
        Assert.That(r!.Objectives[0].Combined, Is.EqualTo(70m));
        Assert.That(r.RequiresReconciliation, Is.False);
    }

    [Test]
    public void ObjectiveWeights_ShapeTheFinalPercent()
    {
        var (r, _) = GradingService.TryCompute(new PolicySnapshot { Rules = new GradingPolicyRules { AiWeight = 0.5m, HumanWeight = 0.5m } },
        [
            (Obj("a", 3m), new AiObjectiveGrade { Score = 100, Level = MasteryLevel.Application }, new HumanObjectiveGrade { Score = 100 }, null),
            (Obj("b", 1m, 20m), new AiObjectiveGrade { Score = 0, Level = MasteryLevel.Recognition }, new HumanObjectiveGrade { Score = 0 }, null),
        ]);
        Assert.That(r!.FinalPercent, Is.EqualTo(75m));
    }

    [Test]
    public void MissingHumanScore_GivesNoFinalPercent()
    {
        var (r, _) = GradingService.TryCompute(new PolicySnapshot(),
            [(Obj("x"), new AiObjectiveGrade { Score = 80, Level = MasteryLevel.Application }, null, null)]);
        Assert.That(r!.FinalPercent, Is.Null);
        Assert.That(r.MissingScores, Does.Contain("x"));
    }
}
