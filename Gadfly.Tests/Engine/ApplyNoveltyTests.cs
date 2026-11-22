using Gadfly.Assessment;
using Gadfly.Engine;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>
/// The Apply novelty check (GAD-T442): an Apply question that reuses a source example or a situation the student already
/// discussed is recorded as extrapolation, never transfer; a new situation is credited as transfer.
/// </summary>
[TestFixture]
public class ApplyNoveltyTests
{
    private const string Source =
        "Photosynthesis turns light into chemical energy. For example, a sunflower in a field turns its leaves toward the morning sun " +
        "to catch more light. Plants store the sugar they make as starch.";

    [Test]
    public void ReusedSourceExample_IsNotNovel()
    {
        var check = ApplyNovelty.Check("Imagine a sunflower in a field turning its leaves toward the morning sun. What is going on?",
            Source, [], "Photosynthesis");
        Assert.That(check.Novel, Is.False);
        Assert.That(check.ReusedFrom, Is.EqualTo("source"));
        Assert.That(check.Describe(), Does.Contain("extrapolation"));
    }

    [Test]
    public void SituationTheStudentAlreadyDiscussed_IsNotNovel()
    {
        var check = ApplyNovelty.Check("Suppose your grandmother's tomato greenhouse lost its glass roof in winter. What would happen?",
            Source, [(2, "My grandmother grows tomato plants in a greenhouse with a glass roof, even in winter.")], "Photosynthesis");
        Assert.That(check.Novel, Is.False);
        Assert.That(check.ReusedFrom, Is.EqualTo("answer"));
        Assert.That(check.MatchTurn, Is.EqualTo(2));
    }

    [Test]
    public void NewSituation_IsNovel()
    {
        var check = ApplyNovelty.Check("A deep-sea submarine crew wants to grow lettuce on a six-month voyage. What would they need to provide, and why?",
            Source, [(1, "Plants use light to make sugar.")], "Photosynthesis");
        Assert.That(check.Novel, Is.True);
        Assert.That(check.Checked, Is.True);
    }

    [Test]
    public void TooFewSituationWords_CountsAsNovelUnchecked()
    {
        var check = ApplyNovelty.Check("Imagine a situation where photosynthesis matters. What would happen?", Source, [], "Photosynthesis");
        Assert.That(check.Novel, Is.True);
        Assert.That(check.Checked, Is.False);
    }

    [Test]
    public void NotNovelApply_DoesNotCreditTransfer()
    {
        var turn = new Turn
        {
            Index = 0, ConceptId = "c", Move = MoveKind.Apply, Question = "q", AskedAt = DateTimeOffset.UnixEpoch,
            Novelty = new ApplyNoveltyCheck(false, 0.8, 5, "source", "x"),
        };
        Assert.That(MoveProfiles.CreditsTransfer(turn), Is.False);
        Assert.That(MoveProfiles.CreditedDimension(turn, AssessmentDimension.KnowledgeTransfer), Is.EqualTo(AssessmentDimension.Application));
        var novel = turn with { Novelty = new ApplyNoveltyCheck(true, 0.1, 1) };
        Assert.That(MoveProfiles.CreditsTransfer(novel), Is.True);
        Assert.That(MoveProfiles.CreditsTransfer(turn with { Novelty = null }), Is.True, "older transcripts keep the move's rule");
    }
}
