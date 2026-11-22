using Gadfly.Socrates.Mock;
using NUnit.Framework;

namespace Gadfly.Tests.Socrates;

/// <summary>
/// The mock panel's prompt reader (GAD-T100): purely textual and forgiving, so a field that can't be found comes back empty
/// and the mock never throws on an unfamiliar prompt.
/// </summary>
[TestFixture]
public class MockPromptReaderTests
{
    private const string Prompt = """
        Concept: Multiplication as equal groups
          Move: Elicit
        Question asked:
        <student_text>
        What is 3 x 4?
        </student_text>
        Student answer:
        <student_text>
        Three groups of four, so twelve.
        </student_text>
        SOURCE PASSAGES
        [1] (notes.md, p. 2) Multiplication counts equal groups.
        [2] Repeated addition gives the same total.
        END
        """;

    [Test]
    public void StudentTexts_AreUnwrappedInOrder()
    {
        Assert.That(MockPromptReader.StudentTexts(Prompt), Is.EqualTo(new[] { "What is 3 x 4?", "Three groups of four, so twelve." }));
    }

    [Test]
    public void StudentAfter_FindsTheBlockAfterTheLabel()
    {
        Assert.That(MockPromptReader.StudentAfter(Prompt, "Student answer:"), Is.EqualTo("Three groups of four, so twelve."));
        Assert.That(MockPromptReader.StudentAfter(Prompt, "Missing label:"), Is.EqualTo(""));
    }

    [Test]
    public void Line_ReadsTheRestOfAnIndentedLine()
    {
        Assert.That(MockPromptReader.Line(Prompt, "Move:"), Is.EqualTo("Elicit"));
        Assert.That(MockPromptReader.Line(Prompt, "Nope:"), Is.EqualTo(""));
    }

    [Test]
    public void Section_StopsAtTheNearestEnd()
    {
        Assert.That(MockPromptReader.Section(Prompt, "SOURCE PASSAGES", "END", "[2]"), Is.EqualTo("[1] (notes.md, p. 2) Multiplication counts equal groups."));
        Assert.That(MockPromptReader.Section(Prompt, "NOT THERE", "END"), Is.EqualTo(""));
    }

    [Test]
    public void Passages_AreNumberedWithOptionalLocation()
    {
        var p = MockPromptReader.Passages(MockPromptReader.Section(Prompt, "SOURCE PASSAGES", "END"));
        Assert.That(p, Has.Count.EqualTo(2));
        Assert.That(p[0], Is.EqualTo((1, (string?)"notes.md, p. 2", "Multiplication counts equal groups.")));
        Assert.That(p[1], Is.EqualTo((2, (string?)null, "Repeated addition gives the same total.")));
    }

    [Test]
    public void Sentences_SplitOnTerminalPunctuation()
    {
        Assert.That(MockPromptReader.Sentences("One. Two!\nThree? "), Is.EqualTo(new[] { "One.", "Two!", "Three?" }));
    }

    [Test]
    public void Keywords_DropStopWordsAndShortWords_AndSingularize()
    {
        var k = MockPromptReader.Keywords("The groups of berries and the boxes is ok");
        Assert.That(k, Is.EquivalentTo(new[] { "group", "berry", "boxe" }));   // crude: "boxes" drops only the s
        Assert.That(k, Does.Not.Contain("the"));
        Assert.That(k, Does.Not.Contain("ok"));
    }

    [Test]
    public void Coverage_AndJaccard()
    {
        var expected = MockPromptReader.Keywords("equal groups total");
        Assert.That(MockPromptReader.Coverage(expected, MockPromptReader.Keywords("the total of equal groups")), Is.EqualTo(1.0));
        Assert.That(MockPromptReader.Coverage(expected, MockPromptReader.Keywords("nothing relevant")), Is.Zero);
        Assert.That(MockPromptReader.Coverage([], MockPromptReader.Keywords("anything")), Is.Zero);
        Assert.That(MockPromptReader.Jaccard("equal groups", "equal groups"), Is.EqualTo(1.0));
        Assert.That(MockPromptReader.Jaccard("", "equal groups"), Is.Zero);
    }

    [TestCase("I don't know", true)]
    [TestCase("no idea, sorry", true)]
    [TestCase("", true)]
    [TestCase("Multiplication is repeated addition.", false)]
    public void IsAporia(string answer, bool expected) => Assert.That(MockPromptReader.IsAporia(answer), Is.EqualTo(expected));

    [Test]
    public void LongAnswerMentioningNotKnowing_IsNotAporia()
    {
        var answer = "I don't know the exact name, but " + string.Concat(Enumerable.Repeat("three groups of four make twelve because each group is equal. ", 4));
        Assert.That(MockPromptReader.IsAporia(answer), Is.False);
    }

    [Test]
    public void ReasoningMarkersAndNegation()
    {
        Assert.That(MockPromptReader.ReasoningMarkers("It is twelve because the groups are equal, so it works."), Is.EqualTo(2));
        Assert.That(MockPromptReader.HasNegation("It is not twelve"), Is.True);
        Assert.That(MockPromptReader.HasNegation("It is twelve"), Is.False);
        Assert.That(MockPromptReader.WithoutNegation("It is not twelve"), Does.Not.Contain("not"));
    }
}
