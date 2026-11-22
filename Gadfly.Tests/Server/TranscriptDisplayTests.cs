using Gadfly.Server.Voice;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Display-only number and punctuation normalization (GAD-T360). Never touches what is stored or graded.</summary>
[TestFixture]
public class TranscriptDisplayTests
{
    [Test]
    public void Spelled_out_numbers_become_digits()
    {
        Assert.That(TranscriptDisplay.ForDisplay("twenty six students"), Is.EqualTo("26 students."));
        Assert.That(TranscriptDisplay.ForDisplay("one hundred and three"), Is.EqualTo("103."));
        Assert.That(TranscriptDisplay.ForDisplay("two thousand twenty six"), Is.EqualTo("2026."));
        Assert.That(TranscriptDisplay.ForDisplay("nine"), Is.EqualTo("9."));
    }

    [Test]
    public void Spoken_punctuation_words_become_marks_with_no_space_before_them()
    {
        Assert.That(TranscriptDisplay.ForDisplay("mitosis comma not meiosis period"), Is.EqualTo("Mitosis, not meiosis."));
        Assert.That(TranscriptDisplay.ForDisplay("is it question mark"), Is.EqualTo("Is it?"));
        Assert.That(TranscriptDisplay.ForDisplay("wait full stop really"), Is.EqualTo("Wait. really."));
    }

    [Test]
    public void Result_is_capitalized_and_gets_a_closing_full_stop()
    {
        Assert.That(TranscriptDisplay.ForDisplay("the groups are equal sized"), Is.EqualTo("The groups are equal sized."));
        Assert.That(TranscriptDisplay.ForDisplay("already ends with a question mark?"), Does.EndWith("?"));
    }

    [Test]
    public void Blank_or_oversized_input_is_returned_unchanged()
    {
        Assert.That(TranscriptDisplay.ForDisplay(null), Is.EqualTo(""));
        Assert.That(TranscriptDisplay.ForDisplay("   "), Is.EqualTo(""));
        var huge = new string('a', TranscriptDisplay.MaxLength + 1);
        Assert.That(TranscriptDisplay.ForDisplay(huge), Is.EqualTo(huge));
    }

    [Test]
    public void Ordinary_words_that_merely_look_like_number_words_are_left_as_numbers_anyway()
    {
        // "one" standing alone as a pronoun ("the one I meant") still becomes "1": a deliberate, documented limitation
        // (display only, never affects grading), simpler and more predictable than guessing intent from context.
        Assert.That(TranscriptDisplay.ForDisplay("the one I meant"), Is.EqualTo("The 1 I meant."));
    }
}
