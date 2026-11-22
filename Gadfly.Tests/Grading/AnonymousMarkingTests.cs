using Gadfly.Assessment;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using NUnit.Framework;
using GradingSnapshot = Gadfly.Server.Grading.PolicySnapshot;

namespace Gadfly.Tests.Grading;

/// <summary>GAD-T439: anonymous human marking hides the student's name until the human score is submitted.</summary>
[TestFixture]
public class AnonymousMarkingTests
{
    private static GradingSnapshot Policy(bool anonymous) => new() { Rules = new GradingPolicyRules { AnonymousMarking = anonymous } };

    [TestCase(null)]
    [TestCase(GradeStatus.ExaminationCompleted)]
    [TestCase(GradeStatus.AiGradingPending)]
    [TestCase(GradeStatus.HumanReviewPending)]
    public void HidesTheName_UntilTheHumanScoreIsIn(GradeStatus? status) =>
        Assert.That(AnonymousMarking.Hides(Policy(true), status), Is.True);

    [TestCase(GradeStatus.ReconciliationRequired)]
    [TestCase(GradeStatus.AwaitingFinalApproval)]
    [TestCase(GradeStatus.Finalized)]
    [TestCase(GradeStatus.Published)]
    public void ShowsTheName_OnceTheHumanScoreIsIn(GradeStatus status) =>
        Assert.That(AnonymousMarking.Hides(Policy(true), status), Is.False);

    [Test]
    public void NeverHides_WhenThePolicyDoesNotAskForIt() =>
        Assert.That(AnonymousMarking.Hides(Policy(false), GradeStatus.HumanReviewPending), Is.False);

    [Test]
    public void TheCode_IsStableAndCarriesNoName()
    {
        var id = Guid.Parse("5a7d9c2e-3f1b-4e6a-8c0d-b2e4f6a8c1d3");
        Assert.That(AnonymousMarking.Code(id), Is.EqualTo("Session 5A7D-9C2E"));
        Assert.That(AnonymousMarking.NameOrCode(true, id, "Ada Lovelace"), Is.EqualTo("Session 5A7D-9C2E"));
        Assert.That(AnonymousMarking.NameOrCode(false, id, "Ada Lovelace"), Is.EqualTo("Ada Lovelace"));
    }

    [Test]
    public void TheOption_IsOffByDefault_AndTravelsWithThePolicy()
    {
        Assert.That(new GradingPolicy().AnonymousMarking, Is.False);
        var p = new GradingPolicy { AnonymousMarking = true };
        Assert.That(p.ToRules().AnonymousMarking, Is.True);
        Assert.That(Gadfly.Server.Assessments.PolicySnapshot.Of(p).AnonymousMarking, Is.True);
        Assert.That(PolicyDraft.From(Gadfly.Server.Assessments.PolicySnapshot.Of(p)).ToSnapshot().ToRules().AnonymousMarking, Is.True);
    }
}
