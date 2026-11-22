using Gadfly.Server.Data;
using Gadfly.Server.Review;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The session's integrity summary (GAD-T60, GAD-T446): a reading aid for the reviewer, never a verdict. Ordinary behaviour
/// is listed apart and never raises the level; the level only says how much there is to look at.
/// </summary>
[TestFixture]
public class IntegritySummaryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    private static IntegrityView Event(string label, IntegritySeverity severity, Guid? turn = null, bool normal = false, double seconds = 0,
        string source = "browser") =>
        new(Guid.NewGuid(), source, label.ToLowerInvariant(), label, $"What {label} means.", normal, T0,
            seconds > 0 ? T0.AddSeconds(seconds) : null, 0.5, severity, label, turn);

    private static TurnView Turn(int index) =>
        new(Guid.NewGuid(), index, "c", "Concept", "Elicit", "Q?", null, T0, "A.", "voice", null, null, false, null,
            new Dictionary<string, double>(), null, []);

    [Test]
    public void NoEvents_IsNothing()
    {
        var s = IntegritySummary.From([], []);
        Assert.That(s.Level, Is.EqualTo(IntegritySummary.Nothing));
        Assert.That(s.Headline, Is.EqualTo("No integrity signals worth attention."));
        Assert.That(s.Lines, Is.Empty);
        Assert.That(s.TurnsWithSignals, Is.Zero);
    }

    [Test]
    public void OnlyInfoSignals_AreNothing()
    {
        var s = IntegritySummary.From([Event("Focus", IntegritySeverity.Info)], []);
        Assert.That(s.Level, Is.EqualTo(IntegritySummary.Nothing));
        Assert.That(s.Lines, Has.Count.EqualTo(1));
    }

    [Test]
    public void NormalBehaviour_IsListedApartAndNeverRaisesTheLevel()
    {
        var events = Enumerable.Range(0, 8).Select(_ => Event("Brief look away", IntegritySeverity.High, normal: true, source: "webcam")).ToList();
        var s = IntegritySummary.From(events, []);
        Assert.That(s.Level, Is.EqualTo(IntegritySummary.Nothing));
        Assert.That(s.Lines, Is.Empty);
        Assert.That(s.Normal, Has.Count.EqualTo(1));
        Assert.That(s.Normal[0].Count, Is.EqualTo(8));
    }

    [Test]
    public void AFewLowSignals_AreMinorAndNameTheTurns()
    {
        var t1 = Turn(0);
        var t2 = Turn(3);
        var s = IntegritySummary.From([Event("Tab hidden", IntegritySeverity.Low, t1.TurnId), Event("Tab hidden", IntegritySeverity.Low, t2.TurnId)], [t1, t2]);
        Assert.That(s.Level, Is.EqualTo(IntegritySummary.Minor));
        Assert.That(s.TurnsWithSignals, Is.EqualTo(2));
        Assert.That(s.Lines.Single().Turns, Is.EqualTo(new[] { 1, 4 }));   // turn numbers start at 1
        Assert.That(s.Headline, Does.Contain("around 2 turns"));
    }

    [Test]
    public void FiveOrMoreLowSignals_AskForAReview()
    {
        var events = Enumerable.Range(0, 5).Select(_ => Event("Paste", IntegritySeverity.Low)).ToList();
        var s = IntegritySummary.From(events, []);
        Assert.That(s.Level, Is.EqualTo(IntegritySummary.Review));
        Assert.That(s.Headline, Does.Contain("nothing here is proof"));
    }

    [Test]
    public void AMediumSignal_AsksForAReview()
    {
        var s = IntegritySummary.From([Event("Second voice", IntegritySeverity.Medium, source: "audio")], []);
        Assert.That(s.Level, Is.EqualTo(IntegritySummary.Review));
    }

    [Test]
    public void Lines_AreGroupedByLabelAndSource_HighestFirst_WithSummedDurations()
    {
        var s = IntegritySummary.From(
        [
            Event("Tab hidden", IntegritySeverity.Low, seconds: 30),
            Event("Tab hidden", IntegritySeverity.Low, seconds: 45),
            Event("Tab hidden", IntegritySeverity.Low, source: "webcam"),
            Event("Second voice", IntegritySeverity.High, source: "audio"),
        ], []);
        Assert.That(s.Lines.Select(l => (l.Label, l.Source)), Is.EqualTo(new[]
        {
            ("Second voice", "audio"), ("Tab hidden", "browser"), ("Tab hidden", "webcam"),
        }));
        Assert.That(s.Lines[1].Count, Is.EqualTo(2));
        Assert.That(s.Lines[1].TotalDuration, Is.EqualTo(TimeSpan.FromSeconds(75)));
        Assert.That(s.Lines[0].Highest, Is.EqualTo(IntegritySeverity.High));
    }

    [Test]
    public void EventsOnUnknownTurns_AreCountedButNotPlaced()
    {
        var s = IntegritySummary.From([Event("Paste", IntegritySeverity.Low, Guid.NewGuid())], []);
        Assert.That(s.Lines.Single().Turns, Is.Empty);
        Assert.That(s.TurnsWithSignals, Is.Zero);
        Assert.That(s.Headline, Does.Not.Contain("around"));
    }

    [Test]
    public void Duration_IsBlankForNothingAndReadableOtherwise()
    {
        Assert.That(IntegritySummary.Duration(TimeSpan.Zero), Is.EqualTo(""));
        Assert.That(IntegritySummary.Duration(TimeSpan.FromSeconds(42)), Is.EqualTo("42 s"));
        Assert.That(IntegritySummary.Duration(TimeSpan.FromSeconds(125)), Is.EqualTo("2 min 5 s"));
    }
}
