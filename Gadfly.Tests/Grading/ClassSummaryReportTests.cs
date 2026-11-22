using Gadfly.Server.Grading;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>The class summary's figures (GAD-T378): mean, median, ten-point distribution and a safe file name.</summary>
[TestFixture]
public class ClassSummaryReportTests
{
    private static ClassSummaryReport Report(IReadOnlyList<decimal> scores, string title = "Multiplication check") => new()
    {
        ExamId = Guid.Parse("11111111-2222-3333-4444-555555555555"), ExamTitle = title, TotalAssigned = 10, Released = scores.Count,
        CompletedNotReleased = 0, NotStarted = [], Scores = scores, Objectives = [],
    };

    [Test]
    public void NoReleasedScores_HaveNoMeanOrMedian_AndAnEmptyDistribution()
    {
        var r = Report([]);
        Assert.That(r.Mean, Is.Null);
        Assert.That(r.Median, Is.Null);
        Assert.That(r.Distribution().Sum(b => b.Count), Is.Zero);
        Assert.That(r.Distribution(), Has.Count.EqualTo(10));
    }

    [Test]
    public void Mean_IsRoundedToOneDecimal()
    {
        Assert.That(Report([70m, 80m, 81m]).Mean, Is.EqualTo(77m));
        Assert.That(Report([10m, 20m, 25m]).Mean, Is.EqualTo(18.3m));
    }

    [Test]
    public void Median_OddAndEvenCounts()
    {
        Assert.That(Report([90m, 10m, 50m]).Median, Is.EqualTo(50m));
        Assert.That(Report([10m, 20m, 30m, 45m]).Median, Is.EqualTo(25m));
    }

    [Test]
    public void Distribution_IsHighestFirst_AndPutsOneHundredInTheTopBucket()
    {
        var d = Report([100m, 95m, 89.9m, 0m, -5m, 42m]).Distribution();
        Assert.That(d[0], Is.EqualTo(new ClassSummaryBucket("90-100%", 2)));
        Assert.That(d[1], Is.EqualTo(new ClassSummaryBucket("80-89%", 1)));
        Assert.That(d.Single(b => b.Range == "40-49%").Count, Is.EqualTo(1));
        Assert.That(d[^1], Is.EqualTo(new ClassSummaryBucket("0-9%", 2)));
    }

    [Test]
    public void FileStem_KeepsLettersDigitsAndDashes()
    {
        Assert.That(Report([], "Demo: Understanding multiplication!").FileStem(), Is.EqualTo("Demo-Understanding-multiplication-class-summary"));
    }

    [Test]
    public void FileStem_IsCappedAt90Characters()
    {
        Assert.That(Report([], new string('a', 200)).FileStem(), Has.Length.EqualTo(90));
    }
}
