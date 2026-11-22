using Gadfly.Assessment;
using Gadfly.Server.Analytics;
using NUnit.Framework;

namespace Gadfly.Tests.Analytics;

/// <summary>
/// Score gaps by group (GAD-T433): the panel's and the human's levels per group, the panel-minus-human gap as a standardized mean
/// difference against 0.15, small groups withheld, and who may see it. Pure.
/// </summary>
[TestFixture]
public class ScoreGapsTests
{
    private static IEnumerable<LevelPair> Group(string name, int students, int panel, int human, int perStudent = 2)
    {
        for (var s = 0; s < students; s++)
        {
            var id = Guid.NewGuid();
            for (var i = 0; i < perStudent; i++)
                // Alternate levels a little so each set varies and the standardized difference is defined.
                yield return new LevelPair(name, id, Math.Clamp(panel + (i % 2), 1, 5), Math.Clamp(human + (i % 2), 1, 5));
        }
    }

    [Test]
    public void Smd_is_the_mean_gap_over_the_pooled_deviation()
    {
        Assert.That(ScoreGaps.Smd([3, 4, 3, 4], [2, 3, 2, 3]), Is.EqualTo(1 / Math.Sqrt(1.0 / 3)).Within(1e-9));
        Assert.That(ScoreGaps.Smd([3, 3], [3, 3]), Is.EqualTo(0), "no variation and no gap");
        Assert.That(ScoreGaps.Smd([4, 4], [3, 3]), Is.Null, "a gap with no variation has no standardized size");
        Assert.That(ScoreGaps.Smd([4], [3]), Is.Null, "one value each says nothing");
    }

    [Test]
    public void Groups_under_five_students_are_withheld()
    {
        var groups = ScoreGaps.Summarize(Group("A", 5, 3, 3).Concat(Group("B", 4, 3, 3)));
        Assert.That(groups.Single(g => g.Group == "A").TooFew, Is.False);
        var b = groups.Single(g => g.Group == "B");
        Assert.That(b.TooFew, Is.True);
        Assert.That(b.MeanPanel, Is.Null, "a withheld group keeps only its name and size");
        Assert.That(b.Students, Is.EqualTo(4));
    }

    [Test]
    public void Equal_gaps_raise_no_warning()
    {
        var groups = ScoreGaps.Summarize(Group("A", 6, 3, 3).Concat(Group("B", 6, 2, 2)));
        Assert.That(groups.All(g => g.MeanGap == 0), Is.True);
        Assert.That(ScoreGaps.Warnings(groups), Is.Empty, "groups differing in level alone are not a fairness warning");
    }

    [Test]
    public void A_gap_in_one_group_is_warned()
    {
        var groups = ScoreGaps.Summarize(Group("A", 6, 3, 3).Concat(Group("B", 6, 4, 3)));
        var b = groups.Single(g => g.Group == "B");
        Assert.That(b.MeanGap, Is.EqualTo(1));
        Assert.That(b.Smd, Is.GreaterThan(ScoreGaps.SmdThreshold));
        var w = ScoreGaps.Warnings(groups);
        Assert.That(w, Has.Some.Contains("For B the panel's levels sit above"));
        Assert.That(w, Has.Some.Contains("differs between groups"));
    }

    [Test]
    public void Level_zero_is_counted_apart_and_left_out_of_the_means()
    {
        var pairs = Group("A", 5, 3, 3).Append(new LevelPair("A", Guid.NewGuid(), 0, 4)).ToList();
        var a = ScoreGaps.Summarize(pairs).Single();
        Assert.That(a.Pairs, Is.EqualTo(11));
        Assert.That(a.Conclusive, Is.EqualTo(10));
        Assert.That(a.MeanGap, Is.EqualTo(0));
        Assert.That(a.PanelInconclusive, Is.EqualTo(1 / 11.0).Within(1e-9));
    }

    [Test]
    public void One_usable_group_cannot_be_compared()
    {
        var w = ScoreGaps.Warnings(ScoreGaps.Summarize(Group("A", 6, 3, 3)));
        Assert.That(w, Has.Count.EqualTo(1).And.Some.Contains("can't be compared yet"));
    }

    [Test]
    public void Only_owners_and_admins_see_group_fairness()
    {
        Assert.That(GroupFairness.ViewerAllowed(AssessmentRoles.Owner), Is.True);
        Assert.That(GroupFairness.ViewerAllowed(AssessmentRoles.InstitutionAdmin), Is.True);
        Assert.That(GroupFairness.ViewerAllowed(AssessmentRoles.Teacher), Is.False, "an instructor may be grading blind");
        Assert.That(GroupFairness.ViewerAllowed(AssessmentRoles.HumanExaminer), Is.False);
        Assert.That(GroupFairness.ViewerAllowed(AssessmentRoles.SecondReviewer), Is.False);
        Assert.That(GroupFairness.ViewerAllowed(null), Is.False);
    }

    [Test]
    public void Accommodation_labels_name_every_declared_accommodation()
    {
        Assert.That(GroupFairness.AccommodationLabel(false, false, 1m), Is.EqualTo(GroupFairness.NoAccommodation));
        Assert.That(GroupFairness.AccommodationLabel(true, false, 1.5m), Is.EqualTo("Typed answers + Extra time"));
        Assert.That(GroupFairness.AccommodationLabel(false, true, 1m), Is.EqualTo(GroupFairness.EyeContact));
    }

    [Test]
    public void Interval_is_mean_plus_or_minus_two_standard_errors()
    {
        var i = GroupFairness.Interval([1, 2, 3, 4])!;
        Assert.That(i.Mean, Is.EqualTo(2.5));
        Assert.That(i.High - i.Mean, Is.EqualTo(1.96 * Math.Sqrt((5.0 / 3) / 4)).Within(1e-9));
        Assert.That(GroupFairness.Interval([1]), Is.Null);
    }
}
