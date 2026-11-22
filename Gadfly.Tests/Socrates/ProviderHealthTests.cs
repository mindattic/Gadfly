using Gadfly.Socrates;
using NUnit.Framework;

namespace Gadfly.Tests.Socrates;

/// <summary>Provider failure tracking (GAD-T62): three failures in a row mark a provider failing, once per streak.</summary>
[TestFixture]
public class ProviderHealthTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    [Test]
    public void NothingRecorded_IsAnEmptySnapshot()
    {
        Assert.That(new ProviderHealth().Snapshot(), Is.Empty);
    }

    [Test]
    public void ThreeFailuresInARow_MarkTheProviderFailing_AndRaiseTheEventOnce()
    {
        var health = new ProviderHealth();
        var raised = new List<ProviderStatus>();
        health.BecameFailing += raised.Add;
        for (var i = 0; i < 5; i++) health.RecordFailure("alpha", "402 out of credit", T0.AddMinutes(i));

        var s = health.Snapshot().Single();
        Assert.That(s.Failing, Is.True);
        Assert.That(s.ConsecutiveFailures, Is.EqualTo(5));
        Assert.That(s.TotalFailures, Is.EqualTo(5));
        Assert.That(s.LastFailureAt, Is.EqualTo(T0.AddMinutes(4)));
        Assert.That(raised, Has.Count.EqualTo(1));
        Assert.That(raised[0].ConsecutiveFailures, Is.EqualTo(ProviderHealth.FailingAfter));
    }

    [Test]
    public void ASuccess_EndsTheStreak_AndANewStreakRaisesAgain()
    {
        var health = new ProviderHealth();
        var raised = 0;
        health.BecameFailing += _ => raised++;
        for (var i = 0; i < 3; i++) health.RecordFailure("alpha", "timeout", T0);
        health.RecordSuccess("alpha", T0.AddMinutes(1));
        var s = health.Snapshot().Single();
        Assert.That(s.Failing, Is.False);
        Assert.That(s.ConsecutiveFailures, Is.Zero);
        Assert.That(s.TotalCalls, Is.EqualTo(4));
        Assert.That(s.TotalFailures, Is.EqualTo(3));
        Assert.That(s.LastSuccessAt, Is.EqualTo(T0.AddMinutes(1)));
        Assert.That(s.LastError, Is.EqualTo("timeout"), "the last error is kept for the status page");

        for (var i = 0; i < 3; i++) health.RecordFailure("alpha", "timeout", T0.AddMinutes(2));
        Assert.That(raised, Is.EqualTo(2));
    }

    [Test]
    public void TwoFailures_AreNotYetFailing()
    {
        var health = new ProviderHealth();
        health.RecordFailure("alpha", "x", T0);
        health.RecordFailure("alpha", "x", T0);
        Assert.That(health.Snapshot().Single().Failing, Is.False);
    }

    [Test]
    public void LongErrors_AreCutTo500Characters_OnLaterFailures()
    {
        var health = new ProviderHealth();
        health.RecordFailure("alpha", "first", T0);
        health.RecordFailure("alpha", new string('e', 2000), T0);
        Assert.That(health.Snapshot().Single().LastError, Has.Length.EqualTo(500));
    }

    [Test]
    public void Providers_AreTrackedSeparately_AndListedByName()
    {
        var health = new ProviderHealth();
        health.RecordSuccess("zeta", T0);
        health.RecordFailure("alpha", "x", T0);
        var snapshot = health.Snapshot();
        Assert.That(snapshot.Select(p => p.ProviderId), Is.EqualTo(new[] { "alpha", "zeta" }));
        Assert.That(snapshot[1].TotalFailures, Is.Zero);
        Assert.That(snapshot[1].TotalCalls, Is.EqualTo(1));
    }
}
