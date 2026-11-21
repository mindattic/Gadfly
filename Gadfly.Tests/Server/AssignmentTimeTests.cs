using Gadfly.Server.Exams;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Assignment windows typed in the instructor's local time (GAD-T214): every date uses the zone's own offset, not today's.</summary>
[TestFixture]
public class AssignmentTimeTests
{
    private static readonly TimeZoneInfo NewYork = AssignmentTime.Find("America/New_York")!;

    [Test]
    public void TheBrowsersZoneIsFoundByItsIanaId_AndAnUnknownOneIsNull()
    {
        Assert.That(NewYork, Is.Not.Null);
        Assert.That(AssignmentTime.Find("Mars/Olympus_Mons"), Is.Null);
        Assert.That(AssignmentTime.Find(""), Is.Null);
        Assert.That(AssignmentTime.Find(null), Is.Null);
    }

    [Test]
    public void ASummerDateAndAWinterDate_GetTheirOwnOffsets_WhateverTheCurrentOffsetIs()
    {
        var summer = AssignmentTime.ToUtc(new DateTime(2026, 7, 1, 17, 0, 0), NewYork, TimeSpan.FromHours(-5));
        var winter = AssignmentTime.ToUtc(new DateTime(2026, 11, 20, 17, 0, 0), NewYork, TimeSpan.FromHours(-4));
        Assert.That(summer.UtcDateTime, Is.EqualTo(new DateTime(2026, 7, 1, 21, 0, 0)), "17:00 EDT is 21:00Z");
        Assert.That(winter.UtcDateTime, Is.EqualTo(new DateTime(2026, 11, 20, 22, 0, 0)), "17:00 EST is 22:00Z");
    }

    [Test]
    public void AWindowAcrossTheFallBackChange_KeepsItsLocalClosingTime()
    {
        // Entered while daylight time is on (today's offset -4), closing after it ends: the old conversion stored 21:00Z, which is 16:00 local.
        var closes = AssignmentTime.ToUtc(new DateTime(2026, 11, 20, 17, 0, 0), NewYork, TimeSpan.FromHours(-4));
        Assert.That(AssignmentTime.ToLocal(closes, NewYork, TimeSpan.FromHours(-4)).DateTime, Is.EqualTo(new DateTime(2026, 11, 20, 17, 0, 0)));
    }

    [Test]
    public void WithoutAZone_TheFallbackOffsetIsUsed_BothWays()
    {
        var at = AssignmentTime.ToUtc(new DateTime(2026, 3, 1, 9, 30, 0), null, TimeSpan.FromHours(2));
        Assert.That(at.UtcDateTime, Is.EqualTo(new DateTime(2026, 3, 1, 7, 30, 0)));
        Assert.That(AssignmentTime.ToLocal(at, null, TimeSpan.FromHours(2)).DateTime, Is.EqualTo(new DateTime(2026, 3, 1, 9, 30, 0)));
    }

    [Test]
    public void ATimeThatDoesNotExist_StillConvertsInsteadOfThrowing()
    {
        // 2026-03-08 02:30 does not exist in New York (clocks jump from 02:00 to 03:00).
        Assert.DoesNotThrow(() => AssignmentTime.ToUtc(new DateTime(2026, 3, 8, 2, 30, 0), NewYork, TimeSpan.Zero));
    }
}
