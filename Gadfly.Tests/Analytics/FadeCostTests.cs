using Gadfly.Server.Analytics;
using NUnit.Framework;

namespace Gadfly.Tests.Analytics;

/// <summary>
/// What the gaze fade costs (GAD-T448): within each student, faded answers against the same student's other answers on the judged
/// score and length, the next answer against clean ones, students without both kinds left out, small groups withheld, and the
/// warnings. Pure.
/// </summary>
[TestFixture]
public class FadeCostTests
{
    /// <summary>One student: a faded answer, the answer after it, and two clean answers.</summary>
    private static IEnumerable<FadeAnswer> Student(string group, double fadedScore, double afterScore, double cleanScore,
        double fadedSeconds = 60, double cleanSeconds = 40)
    {
        var id = Guid.NewGuid();
        yield return new FadeAnswer(group, id, false, false, cleanScore, cleanSeconds);
        yield return new FadeAnswer(group, id, true, false, fadedScore, fadedSeconds);
        yield return new FadeAnswer(group, id, false, true, afterScore, cleanSeconds);
        yield return new FadeAnswer(group, id, false, false, cleanScore, cleanSeconds);
    }

    private static List<FadeAnswer> Students(string group, int n, double faded, double after, double clean) =>
        Enumerable.Range(0, n).SelectMany(i => Student(group, faded + i * 0.01, after + i * 0.01, clean + i * 0.01)).ToList();

    [Test]
    public void Faded_answers_are_compared_with_the_same_students_other_answers()
    {
        var r = FadeCost.Compare(Students("A", 5, 0.5, 0.7, 0.7));
        var o = r.Overall;
        Assert.That(o.Compared, Is.EqualTo(5));
        Assert.That(o.FadedAnswers, Is.EqualTo(5));
        // faded 0.5 against the mean of the three unfaded (0.7 each) = -0.2
        Assert.That(o.ScoreDiff!.Mean, Is.EqualTo(-0.2).Within(1e-9));
        Assert.That(o.SecondsDiff!.Mean, Is.EqualTo(20).Within(1e-9));
        Assert.That(o.AfterScoreDiff!.Mean, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void A_clear_cost_is_warned()
    {
        var answers = Students("A", 6, 0.5, 0.6, 0.7);
        var w = FadeCost.Compare(answers).Warnings;
        Assert.That(w, Has.Some.Contains("judged 0.20 lower").Or.Some.Contains("lower (of 1)"));
        Assert.That(w, Has.Some.Contains("right after a faded one"));
    }

    [Test]
    public void No_cost_raises_no_warning()
    {
        Assert.That(FadeCost.Compare(Students("A", 6, 0.7, 0.7, 0.7)).Warnings, Is.Empty);
    }

    [Test]
    public void Students_without_both_kinds_of_answer_are_left_out()
    {
        var id = Guid.NewGuid();
        var answers = Students("A", 5, 0.5, 0.7, 0.7);
        answers.Add(new FadeAnswer("A", id, true, false, 0.1, 30));
        answers.Add(new FadeAnswer("A", id, false, false, null, 30));   // not judged: no comparison for this student
        var o = FadeCost.Compare(answers).Overall;
        Assert.That(o.Students, Is.EqualTo(6));
        Assert.That(o.Compared, Is.EqualTo(5));
    }

    [Test]
    public void Too_few_compared_students_are_withheld()
    {
        var r = FadeCost.Compare(Students("A", 4, 0.5, 0.7, 0.7));
        Assert.That(r.Overall.TooFew, Is.True);
        Assert.That(r.Overall.ScoreDiff, Is.Null);
        Assert.That(r.Warnings, Has.Some.Contains("can't be estimated yet"));
    }

    /// <summary>GAD-T480: the overall row minus a shown group would give a withheld group's figure, so it is withheld too.</summary>
    [Test]
    public void A_withheld_group_withholds_the_overall_figures()
    {
        var answers = Students("Shown", 6, 0.6, 0.7, 0.7).Concat(Students("Small", 1, 0.1, 0.2, 0.7)).ToList();
        var r = FadeCost.Compare(answers);
        Assert.That(r.Groups.Single(g => g.Group == "Small").ScoreDiff, Is.Null);
        Assert.That(r.Groups.Single(g => g.Group == "Shown").ScoreDiff, Is.Not.Null);
        Assert.That(r.Overall.TooFew, Is.False);
        Assert.That(r.Overall.ScoreDiff, Is.Null);
        Assert.That(r.Overall.SecondsDiff, Is.Null);
        Assert.That(r.Overall.AfterScoreDiff, Is.Null);
        Assert.That(r.Overall.Compared, Is.EqualTo(7));
        Assert.That(r.Warnings, Has.Some.Contains("withheld"));
    }

    [Test]
    public void A_cost_that_differs_between_groups_is_warned()
    {
        var answers = Students("A", 5, 0.7, 0.7, 0.7).Concat(Students("B", 5, 0.55, 0.7, 0.7)).ToList();
        var r = FadeCost.Compare(answers);
        Assert.That(r.Groups, Has.Count.EqualTo(2));
        Assert.That(r.Warnings, Has.Some.Contains("differs between groups"));
    }
}
