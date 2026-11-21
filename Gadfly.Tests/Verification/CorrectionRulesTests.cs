using Gadfly.Sources.Verification;
using NUnit.Framework;

namespace Gadfly.Tests.Verification;

/// <summary>Correction integrity (Part 4.7, GAD-T88): a typo fix can't become a rewritten answer.</summary>
[TestFixture]
public sealed class CorrectionRulesTests
{
    private const string Original = "The coeficient changes depending on another varible in the model.";

    [Test]
    public void Spelling_fixes_are_allowed()
    {
        var check = CorrectionRules.Check(Original, "The coefficient changes depending on another variable in the model.", voice: false);
        Assert.Multiple(() =>
        {
            Assert.That(check.Allowed, Is.True, check.Reason);
            Assert.That(check.ChangedWords, Is.EqualTo(2));
        });
    }

    [Test]
    public void Replacing_a_word_with_a_different_word_is_not_a_typo()
    {
        var check = CorrectionRules.Check(Original, "The coeficient changes depending on another factor in the model.", voice: false);
        Assert.Multiple(() =>
        {
            Assert.That(check.Allowed, Is.False);
            Assert.That(check.Reason, Does.Contain("different word"));
        });
    }

    [Test]
    public void Rewriting_the_answer_is_refused_and_points_to_a_revision()
    {
        var check = CorrectionRules.Check(Original,
            "The effect of one independent variable depends on the value of another independent variable.", voice: false);
        Assert.Multiple(() =>
        {
            Assert.That(check.Allowed, Is.False);
            Assert.That(check.Reason, Does.Contain("I want to change my answer"));
        });
    }

    [TestCase("The coeficient does not change depending on another varible in the model.")]
    [TestCase("The coeficient never changes depending on another varible in the model.")]
    [TestCase("The coeficient doesn't change depending on another varible in the model.")]
    public void Adding_a_negation_is_refused_even_when_small(string corrected)
    {
        var typed = CorrectionRules.Check(Original, corrected, voice: false);
        var voice = CorrectionRules.Check(Original, corrected, voice: true);
        Assert.Multiple(() =>
        {
            Assert.That(typed.Allowed, Is.False);
            Assert.That(voice.Allowed, Is.False);
        });
    }

    [Test]
    public void Voice_corrections_may_replace_misheard_words()
    {
        var check = CorrectionRules.Check("The eye gen value tells you the scaling factor.", "The eigenvalue tells you the scaling factor.", voice: true);
        Assert.That(check.Allowed, Is.True, check.Reason);
    }

    [Test]
    public void The_same_text_or_an_empty_text_is_not_a_correction()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorrectionRules.Check(Original, Original.ToUpperInvariant(), voice: false).Allowed, Is.False);
            Assert.That(CorrectionRules.Check(Original, "  ", voice: false).Allowed, Is.False);
        });
    }

    [Test]
    public void Long_answers_get_a_proportional_allowance()
    {
        var words = Enumerable.Range(0, 40).Select(i => "word" + i).ToList();
        var original = string.Join(" ", words);
        var fixedFour = string.Join(" ", words.Select((w, i) => i % 10 == 0 ? w + "x" : w));
        Assert.That(CorrectionRules.Check(original, fixedFour, voice: false).Allowed, Is.True, "4 of 40 words is within 10%");
    }

    [Test]
    public void Distances()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorrectionRules.CharDistance("varible", "variable"), Is.EqualTo(1));
            Assert.That(CorrectionRules.WordDistance(["a", "b", "c"], ["a", "x", "c", "d"]), Is.EqualTo(2));
        });
    }
}
