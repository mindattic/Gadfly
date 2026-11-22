using Gadfly.Server.Analytics;
using Gadfly.Server.Integrity;
using NUnit.Framework;

namespace Gadfly.Tests.Analytics;

/// <summary>
/// Camera episode rates by group (GAD-T447): absent, second and changed-face episodes per 10 answering minutes, the tracking
/// quality score as a grouping and a per-group distribution, and the warnings when they differ between groups. Pure.
/// </summary>
[TestFixture]
public class CameraEpisodeRatesTests
{
    private static GazeRateGroup Group(string name, int students = 6, double minutes = 100, int absent = 0, int second = 0, int changed = 0,
        int lookAways = 10, double? medianQuality = null) =>
        new(name, students, students, minutes, lookAways, 0, 5, 0, 0, 0, absent, second, changed,
            medianQuality is null ? 0 : students, medianQuality, null, null);

    [Test]
    public void Face_episode_rates_are_per_ten_answering_minutes()
    {
        var g = Group("A", absent: 5, second: 2, changed: 1);
        Assert.That(g.AbsentFacesPer10Min, Is.EqualTo(0.5));
        Assert.That(g.SecondFacesPer10Min, Is.EqualTo(0.2));
        Assert.That(g.ChangedFacesPer10Min, Is.EqualTo(0.1));
        Assert.That(Group("B", minutes: 0).AbsentFacesPer10Min, Is.Null);
    }

    [Test]
    public void Absent_faces_more_often_in_one_group_are_warned()
    {
        var w = GazeFlagRates.Warnings([Group("A", absent: 5), Group("B", absent: 10)]);
        Assert.That(w, Has.Some.Contains("Absent-face episodes differ between groups"));
        Assert.That(GazeFlagRates.Warnings([Group("A", absent: 5), Group("B", absent: 5)]), Has.None.Contains("Absent-face"));
    }

    [Test]
    public void Tracking_quality_that_differs_between_groups_is_warned()
    {
        var w = GazeFlagRates.Warnings([Group("A", medianQuality: 72), Group("B", medianQuality: 55)]);
        Assert.That(w, Has.Some.Contains("Eye-tracking quality differs between groups").And.Some.Contains("for B"));
        Assert.That(GazeFlagRates.Warnings([Group("A", medianQuality: 72), Group("B", medianQuality: 66)]), Has.None.Contains("quality"));
    }

    [Test]
    public void Small_groups_are_left_out_of_the_comparison()
    {
        var w = GazeFlagRates.Warnings([Group("A", absent: 5), Group("B", students: 4, absent: 50)]);
        Assert.That(w, Has.Some.Contains("can't be compared yet"));
    }

    [TestCase(85, "Tracking good")]
    [TestCase(70, "Tracking good")]
    [TestCase(55, "Tracking fair")]
    [TestCase(39, "Tracking poor")]
    public void Quality_groups_follow_the_quality_bands(int score, string band) =>
        Assert.That(GazeFlagRates.QualityGroup(new GazeQuality(score, true, 3)), Does.StartWith(band));

    [Test]
    public void A_session_without_a_quality_score_has_its_own_group()
    {
        Assert.That(GazeFlagRates.QualityGroup(null), Does.StartWith("No quality score"));
        Assert.That(GazeFlagRates.SessionQuality([null, null]), Is.Null);
    }

    [Test]
    public void A_sessions_quality_is_its_weakest_episode()
    {
        var q = GazeFlagRates.SessionQuality([new GazeQuality(80, true, 2), null, new GazeQuality(45, false, null)]);
        Assert.That(q!.Score, Is.EqualTo(45));
    }

    [Test]
    public void Median_of_quality_scores()
    {
        Assert.That(GazeFlagRates.Median([40, 80, 60]), Is.EqualTo(60));
        Assert.That(GazeFlagRates.Median([40, 80]), Is.EqualTo(60));
        Assert.That(GazeFlagRates.Median([]), Is.Null);
    }
}
