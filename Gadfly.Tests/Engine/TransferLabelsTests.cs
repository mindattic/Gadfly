using Gadfly.Engine;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>
/// Transfer labels on Apply questions (GAD-T268): each Apply question on an objective takes the next context in the
/// Barnett and Ceci order (nearest first), recomputed from the transcript alone, and only Apply probes transfer.
/// </summary>
[TestFixture]
public class TransferLabelsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    private static SocraticSession NewSession() => new("subject", new SessionPolicy(), ExamPrompts.Set, T0);

    private static Turn AskAndAnswer(SocraticSession s, MoveKind move, string objective, int minute)
    {
        var turn = s.Ask(objective, move, $"Question {minute} on {objective}?", T0.AddMinutes(minute), objectiveKey: objective);
        s.RecordAnswer(new Answer { Text = "An answer.", CompletedAt = T0.AddMinutes(minute).AddSeconds(30) });
        return turn;
    }

    [Test]
    public void FirstApplyOnAnObjective_IsFunctional()
    {
        var s = NewSession();
        Assert.That(TransferLabels.Next(s, "o1"), Is.EqualTo(TransferContext.Functional));
    }

    [Test]
    public void ApplyQuestions_MoveThroughTheOrderPerObjective()
    {
        var s = NewSession();
        var a1 = AskAndAnswer(s, MoveKind.Apply, "o1", 1);
        AskAndAnswer(s, MoveKind.Elicit, "o1", 2);        // not Apply: does not advance the order
        var b1 = AskAndAnswer(s, MoveKind.Apply, "o2", 3); // another objective keeps its own count
        var a2 = AskAndAnswer(s, MoveKind.Apply, "o1", 4);

        Assert.That(TransferLabels.Of(s, a1), Is.EqualTo(TransferContext.Functional));
        Assert.That(TransferLabels.Of(s, b1), Is.EqualTo(TransferContext.Functional));
        Assert.That(TransferLabels.Of(s, a2), Is.EqualTo(TransferContext.Physical));
        Assert.That(TransferLabels.Next(s, "o1"), Is.EqualTo(TransferContext.Social));
        Assert.That(TransferLabels.Next(s, "o2"), Is.EqualTo(TransferContext.Physical));
    }

    [Test]
    public void OrderWrapsAroundAfterTheLastContext()
    {
        var s = NewSession();
        for (var i = 0; i < TransferLabels.Order.Count; i++) AskAndAnswer(s, MoveKind.Apply, "o1", i + 1);
        Assert.That(TransferLabels.Next(s, "o1"), Is.EqualTo(TransferLabels.Order[0]));
    }

    [Test]
    public void NonApplyTurn_HasNoTransferLabel()
    {
        var s = NewSession();
        var elicit = AskAndAnswer(s, MoveKind.Elicit, "o1", 1);
        Assert.That(TransferLabels.Of(s, elicit), Is.Null);
    }

    [Test]
    public void OrderGoesNearToFar()
    {
        Assert.That(TransferLabels.Order[0], Is.EqualTo(TransferContext.Functional));
        Assert.That(TransferLabels.Order[^1], Is.EqualTo(TransferContext.KnowledgeDomain));
        Assert.That(TransferLabels.Order.Distinct().Count(), Is.EqualTo(Enum.GetValues<TransferContext>().Length));
    }

    [TestCase(TransferContext.Functional, "near")]
    [TestCase(TransferContext.Physical, "near")]
    [TestCase(TransferContext.Social, "near")]
    [TestCase(TransferContext.Temporal, "near")]
    [TestCase(TransferContext.Modality, "far")]
    [TestCase(TransferContext.KnowledgeDomain, "far")]
    public void Distance_IsNearForSettingAndFarForFormOrSubject(TransferContext context, string expected)
    {
        Assert.That(TransferLabels.Distance(context), Is.EqualTo(expected));
        Assert.That(TransferLabels.Label(context), Does.StartWith(expected + " transfer: "));
    }

    [Test]
    public void EveryContext_HasPlainWordsAndAnInstructionCarryingItsLabel()
    {
        foreach (var c in Enum.GetValues<TransferContext>())
        {
            Assert.That(TransferLabels.Difference(c), Does.StartWith("different "), c.ToString());
            Assert.That(TransferLabels.Instruction(c), Does.EndWith($"({TransferLabels.Label(c)})"), c.ToString());
        }
        Assert.That(TransferLabels.Label(TransferContext.Physical), Is.EqualTo("near transfer: different physical setting"));
    }

    [Test]
    public void NullArguments_Throw()
    {
        var s = NewSession();
        Assert.Throws<ArgumentNullException>(() => TransferLabels.Next(null!, "o1"));
        Assert.Throws<ArgumentNullException>(() => TransferLabels.Of(s, null!));
    }

    [Test]
    public void Caveat_SaysTransferIsSequestered()
    {
        Assert.That(TransferLabels.Caveat, Does.Contain("without resources or hints"));
    }
}
