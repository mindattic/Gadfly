using Gadfly.Engine;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>
/// Learning Mode's fixed hint ladder (GAD-T271): hints are the instructor's rungs word for word, in order, the same for every
/// student; an objective without a ladder gets the fixed default one; rungs are never repeated past the policy's limit.
/// </summary>
[TestFixture]
public class HintLadderTests
{
    private static readonly ExamObjective Written = new()
    {
        Key = "o1", Title = "Photosynthesis",
        HintLadder = ["Think about where the plant gets its energy.", "What does the leaf take in from the air?"],
    };

    [Test]
    public void Rungs_are_the_instructors_text_in_order_with_the_fixed_retry_question()
    {
        var first = HintLadders.Rung(Written, Written.Title, 0, MoveKind.Hint);
        Assert.That(first.Text, Is.EqualTo("Think about where the plant gets its energy. " + HintLadders.Retry(MoveKind.Hint, "Photosynthesis")));
        Assert.That((first.Step, first.Of, first.InstructorWritten), Is.EqualTo((1, 2, true)));

        var second = HintLadders.Rung(Written, Written.Title, 1, MoveKind.GuidedExplanation);
        Assert.That(second.Text, Is.EqualTo("What does the leaf take in from the air?"), "a rung that is already a question is said as written");
        Assert.That(second.Step, Is.EqualTo(2));
    }

    [Test]
    public void An_objective_without_a_ladder_gets_the_fixed_default_ladder()
    {
        var none = new ExamObjective { Key = "o2", Title = "Osmosis" };
        var a = HintLadders.Rung(none, none.Title, 0, MoveKind.Hint);
        var b = HintLadders.Rung(null, "Osmosis", 0, MoveKind.Hint);
        Assert.That(a.InstructorWritten, Is.False);
        Assert.That(a.Text, Is.EqualTo(b.Text), "the default ladder is the same for every student");
        Assert.That(a.Text, Does.StartWith(HintLadders.Default("Osmosis")[0]));
    }

    [Test]
    public void Assists_are_capped_by_the_ladder_length_and_the_policy()
    {
        Assert.That(HintLadders.MaxAssists(new SessionPolicy { MaxAssistsPerObjective = 5 }, Written, Written.Title), Is.EqualTo(2));
        Assert.That(HintLadders.MaxAssists(new SessionPolicy { MaxAssistsPerObjective = 1 }, Written, Written.Title), Is.EqualTo(1));
    }

    [Test]
    public void Parse_takes_one_rung_per_line_and_refuses_too_many_or_too_long()
    {
        var (ladder, error) = HintLadders.Parse(" first \r\n\r\nsecond\n");
        Assert.That(error, Is.Null);
        Assert.That(ladder, Is.EqualTo(new[] { "first", "second" }));
        Assert.That(HintLadders.Parse(string.Join("\n", Enumerable.Range(1, HintLadders.MaxRungs + 1).Select(i => $"hint {i}"))).Ladder, Is.Null);
        Assert.That(HintLadders.Parse(new string('x', HintLadders.MaxRungLength + 1)).Ladder, Is.Null);
    }
}
