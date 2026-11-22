using Gadfly.Engine;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>A step prepared from the student's words while they were still answering is used only for the same words (GAD-T497).</summary>
[TestFixture]
public class PrefetchAnswerTests
{
    [TestCase("Plants make sugar, from light!", "plants make sugar from light")]
    [TestCase("  It’s   the CHLOROPHYLL. ", "it's the chlorophyll")]
    [TestCase("", "")]
    public void WordsIgnoreCasePunctuationAndSpacing(string text, string words) =>
        Assert.That(SocraticSession.Words(text), Is.EqualTo(words));

    [Test]
    public void SameWords_DifferentWordsAreNot()
    {
        Assert.That(SocraticSession.SameWords("Light becomes sugar.", "light becomes sugar"), Is.True);
        Assert.That(SocraticSession.SameWords("Light becomes sugar", "Light becomes sugar eventually"), Is.False);
    }

    [Test]
    public void TheSubmittedAnswerReplacesTheSpeculativeOne_KeepingTheJudgment()
    {
        var s = Session();
        s.Judged("photosynthesis", MoveKind.Elicit, 0.5, 0.5, answer: "light becomes sugar");
        var judged = s.Turns[0].Judgment;
        var started = DateTimeOffset.UtcNow.AddSeconds(-9);
        var final = new Answer { Text = "Light becomes sugar.", Mode = "voice", StartedAt = started, CompletedAt = started.AddSeconds(4) };
        s.ReplaceAnswerDetails(0, final);
        Assert.That(s.Turns[0].Answer, Is.EqualTo(final));
        Assert.That(s.Turns[0].Judgment, Is.SameAs(judged));
    }

    [Test]
    public void OtherWordsAreNeverPutOnAJudgedTurn()
    {
        var s = Session();
        s.Judged("photosynthesis", MoveKind.Elicit, 0.5, 0.5, answer: "light becomes sugar");
        Assert.Throws<InvalidOperationException>(() =>
            s.ReplaceAnswerDetails(0, new Answer { Text = "light becomes sugar and oxygen", Mode = "voice", CompletedAt = DateTimeOffset.UtcNow }));
    }
}
