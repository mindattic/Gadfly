using Gadfly.Engine;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>
/// Reliability by objective count (GAD-T443): the person x objective x rater variance components from a double-marked pilot,
/// the coefficients they imply and the fewest objectives reaching a target.
/// </summary>
[TestFixture]
public class ReliabilityStudyTests
{
    /// <summary>Score = student effect + student x objective effect, identical for both raters.</summary>
    private static List<PilotGrade> Additive(double[] person, double[,] personObjective, int raters = 2)
    {
        var rows = new List<PilotGrade>();
        for (var p = 0; p < person.Length; p++)
            for (var o = 0; o < personObjective.GetLength(1); o++)
                for (var r = 0; r < raters; r++)
                    rows.Add(new PilotGrade($"s{p}", $"o{o}", $"r{r}", person[p] + personObjective[p, o]));
        return rows;
    }

    [Test]
    public void ScoresThatOnlySeparateStudents_AreFullyReliable()
    {
        var rows = Additive([1, 2, 3, 4], new double[4, 3]);
        var result = ReliabilityStudy.Analyse(rows);
        Assert.That(result.Components.Person, Is.GreaterThan(0));
        Assert.That(result.Components.PersonObjective, Is.EqualTo(0).Within(1e-9));
        Assert.That(result.Components.Residual, Is.EqualTo(0).Within(1e-9));
        Assert.That(result.Projection[0].Relative, Is.EqualTo(1).Within(1e-9));
        Assert.That(result.MinimumObjectives, Is.EqualTo(1));
    }

    [Test]
    public void ContentSpecificity_IsTheStudentObjectiveComponent()
    {
        // Student effects +-1 (variance 4/3 with n-1), crossed with a balanced +-1 interaction pattern.
        double[] person = [-1, 1, -1, 1];
        var po = new double[,] { { 1, -1 }, { 1, -1 }, { -1, 1 }, { -1, 1 } };
        var result = ReliabilityStudy.Analyse(Additive(person, po));
        var v = result.Components;
        Assert.That(v.Rater, Is.EqualTo(0).Within(1e-9));
        Assert.That(v.Residual, Is.EqualTo(0).Within(1e-9));
        Assert.That(v.PersonObjective, Is.GreaterThan(0));
        // Two raters agreeing exactly: relative error is the content term alone, so rho(m) = p / (p + po/m).
        var expected = v.Person / (v.Person + v.PersonObjective / 3);
        Assert.That(ReliabilityStudy.Relative(v, 3, 2), Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void MinimumObjectives_IsTheFirstReachingTheTarget()
    {
        // The Section 3.7 illustration: 0.41 students, 0.32 content, 0.27 error -> about 0.78 at five objectives, 0.87 at ten.
        var v = new VarianceComponents { Person = 0.41, PersonObjective = 0.32, Residual = 0.27 };
        Assert.That(ReliabilityStudy.Relative(v, 1, 1), Is.EqualTo(0.41).Within(1e-9));
        Assert.That(ReliabilityStudy.Relative(v, 5, 1), Is.EqualTo(0.78).Within(0.01));
        Assert.That(ReliabilityStudy.Relative(v, 10, 1), Is.EqualTo(0.87).Within(0.01));
        var first = Enumerable.Range(1, 20).First(m => ReliabilityStudy.Relative(v, m, 1) >= 0.8);
        Assert.That(first, Is.EqualTo(6));
    }

    [Test]
    public void AbsoluteCoefficient_IsNeverAboveTheRelativeOne()
    {
        var v = new VarianceComponents { Person = 1, Objective = 0.5, Rater = 0.2, PersonObjective = 0.4, PersonRater = 0.1, ObjectiveRater = 0.1, Residual = 0.3 };
        for (var m = 1; m <= 10; m++)
            Assert.That(ReliabilityStudy.Absolute(v, m, 2), Is.LessThanOrEqualTo(ReliabilityStudy.Relative(v, m, 2)));
    }

    [Test]
    public void IncompleteStudents_AreLeftOutAndNamed()
    {
        var rows = Additive([1, 2, 3, 4], new double[4, 3]);
        rows.Add(new PilotGrade("late", "o0", "r0", 2)); // marked by one rater on one objective only
        var result = ReliabilityStudy.Analyse(rows);
        Assert.That(result.Students, Is.EqualTo(4));
        Assert.That(result.Dropped, Is.EqualTo(new[] { "student late" }));
    }

    [Test]
    public void OneRater_IsRefused()
    {
        var rows = Additive([1, 2, 3], new double[3, 2], raters: 1);
        Assert.Throws<InvalidOperationException>(() => ReliabilityStudy.Analyse(rows));
    }

    [Test]
    public void Csv_ReadsColumnsInAnyOrderAndReportsBadLines()
    {
        var csv = "score,rater,objective,student\n3,r1,o1,s1\nx,r1,o2,s1\n4,r2,o1,s1\n";
        var rows = ReliabilityStudy.FromCsv(csv, out var problems);
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0], Is.EqualTo(new PilotGrade("s1", "o1", "r1", 3)));
        Assert.That(problems, Has.Count.EqualTo(1));
    }
}
