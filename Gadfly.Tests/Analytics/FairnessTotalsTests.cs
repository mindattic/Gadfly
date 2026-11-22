using Gadfly.Server.Analytics;
using NUnit.Framework;

namespace Gadfly.Tests.Analytics;

/// <summary>
/// GAD-T486: a total of pairs, answers or sessions shown beside the groups would give a withheld group's count away (the total
/// minus the groups shown), so it is withheld whenever any group is, on every fairness view and in the fairness CSV. Pure.
/// </summary>
[TestFixture]
public class FairnessTotalsTests
{
    private static GazeRateGroup Gaze(string group, int students, int sessions) =>
        new(group, students, sessions, sessions * 10.0, 0, 0, 0, 0, 0, 0);

    private static IEnumerable<FadeAnswer> Student(string group, double faded, double clean)
    {
        var id = Guid.NewGuid();
        yield return new FadeAnswer(group, id, false, false, clean, 40);
        yield return new FadeAnswer(group, id, true, false, faded, 60);
    }

    private static List<FadeAnswer> Students(string group, int n) =>
        Enumerable.Range(0, n).SelectMany(i => Student(group, 0.5 + i * 0.01, 0.7 + i * 0.01)).ToList();

    [Test]
    public void The_total_is_shown_when_no_group_is_withheld_and_withheld_otherwise()
    {
        Assert.That(GroupFairness.Total(42, false, "answer(s)"), Is.EqualTo("42 answer(s)"));
        var hidden = GroupFairness.Total(42, true, "answer(s)");
        Assert.That(hidden, Does.Not.Contain("42"));
        Assert.That(hidden, Does.Contain("withheld"));
    }

    [Test]
    public void A_withheld_gaze_group_withholds_the_session_total()
    {
        Assert.That(new GazeRatesReport([Gaze("A", 6, 12), Gaze("B", 7, 9)], 21, 0, []).TotalWithheld, Is.False);
        Assert.That(new GazeRatesReport([Gaze("A", 6, 12), Gaze("B", 2, 3)], 15, 0, []).TotalWithheld, Is.True);
    }

    [Test]
    public void Empty_reports_have_nothing_to_withhold()
    {
        Assert.That(new ScoreGapsReport([], 0, 0, []).TotalWithheld, Is.False);
        Assert.That(new PathDriversReport([], 0, []).TotalWithheld, Is.False);
        Assert.That(new SpeechAuditReport([], 0, 0, []).TotalWithheld, Is.False);
    }

    [Test]
    public void A_withheld_fade_group_withholds_the_all_students_count_of_faded_answers()
    {
        var shown = FadeCost.Compare(Students("Shown", 6).Concat(Students("Other", 5)).ToList());
        Assert.That(shown.OverallCountWithheld, Is.False);

        var r = FadeCost.Compare(Students("Shown", 6).Concat(Students("Small", 1)).ToList());
        Assert.That(r.Overall.TooFew, Is.False);
        Assert.That(r.OverallCountWithheld, Is.True);

        var csv = FairnessCsv.ToCsv(new FairnessTables(new ScoreGapsReport([], 0, 0, []), new PathDriversReport([], 0, []),
            new TypedSpokenReport(ModeSide.Withheld, ModeSide.Withheld, [], null, null, []), new GazeRatesReport([], 0, 0, []), r));
        var overallRow = csv.Split("\r\n").Single(l => l.StartsWith("All students,"));
        var cells = overallRow.Split(',');
        Assert.That(cells[3], Is.EqualTo(FairnessCsv.Withheld), "faded_answers of the all-students row");
        Assert.That(cells[2], Is.EqualTo("7"), "the count of students compared stays, as a group's size does");
    }
}
