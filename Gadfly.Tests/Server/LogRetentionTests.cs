using Gadfly.Server.Data;
using Gadfly.Server.Privacy;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The six-month log floor (GAD-T466): the arithmetic, without a database.</summary>
[TestFixture]
public class LogRetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [TestCase(null, 365)]
    [TestCase(0, 365)]
    [TestCase(-5, 365)]
    [TestCase(7, LogRetentionPolicy.MinimumDays)]
    [TestCase(90, LogRetentionPolicy.MinimumDays)]
    [TestCase(184, 184)]
    [TestCase(730, 730)]
    public void CallTimingDays_NeverFallsBelowSixMonths(int? configured, int expected) =>
        Assert.That(LogRetentionPolicy.CallTimingDays(configured), Is.EqualTo(expected));

    [Test]
    public void TheFloor_IsAtLeastSixCalendarMonths() =>
        Assert.That(Now.AddDays(-LogRetentionPolicy.MinimumDays), Is.LessThanOrEqualTo(Now.AddMonths(-6)));

    [Test]
    public void MayDelete_OnlyRowsOlderThanThePeriod()
    {
        Assert.That(LogRetentionPolicy.MayDelete(Now.AddDays(-183), Now, 7), Is.False, "a 7-day setting cannot shorten a log");
        Assert.That(LogRetentionPolicy.MayDelete(Now.AddDays(-185), Now, 7), Is.True);
        Assert.That(LogRetentionPolicy.MayDelete(Now.AddDays(-300), Now, null), Is.False, "a year by default");
        Assert.That(LogRetentionPolicy.MayDelete(Now.AddDays(-366), Now, null), Is.True);
    }
}

/// <summary>
/// The logs on LocalDB (GAD-T466): the organization's transcript retention never removes an audit or call-timing row, the
/// call timings are pruned only past their own period, and an erasure keeps the rows but takes the student's detail out.
/// </summary>
[TestFixture]
public class LogRetentionTests : ServerTest
{
    private const string Words = "my very private answer about chlorophyll";

    private static ApiCallTiming Timing(Guid? org, Guid? session, DateTimeOffset at, string? error = null) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = org, SessionId = session, CallName = "panel.judge", StartedAtUtc = at, DurationMs = 1200,
        Succeeded = error is null, ErrorSummary = error,
    };

    [Test]
    public async Task ShortTranscriptRetention_KeepsEveryAuditAndTimingRow()
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync(Words, endedAt: DateTimeOffset.UtcNow.AddDays(-10));
        await using (var db = SystemDb())
        {
            (await db.OrganizationSettings.SingleAsync(s => s.OrganizationId == w.Org)).RetentionDays = 7;
            db.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = w.Org, SessionId = sessionId, ActorUserId = w.StudentId, ActorRole = "Student", Action = "response.submitted",
                EntityType = "StudentResponse", EntityId = Guid.NewGuid().ToString(), AfterJson = $"{{\"text\":\"{Words}\"}}", At = DateTimeOffset.UtcNow.AddDays(-10),
            });
            db.ApiCallTimings.Add(Timing(w.Org, sessionId, DateTimeOffset.UtcNow.AddDays(-10)));
            await db.SaveChangesAsync();
        }

        await using (var scope = Scope(TenantSnapshot.System))
        {
            await scope.ServiceProvider.GetRequiredService<PrivacyService>().ApplyRetentionAsync();
            await scope.ServiceProvider.GetRequiredService<LogRetention>().PruneCallTimingsAsync();
        }

        await using var check = SystemDb();
        Assert.That((await check.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).RedactedAt, Is.Not.Null, "the transcript went after 7 days");
        var audit = await check.AuditEvents.AsNoTracking().SingleAsync(a => a.SessionId == sessionId && a.Action == "response.submitted");
        Assert.That(audit.AfterJson, Is.Null, "the student's words left the event with the transcript");
        Assert.That(audit.ActorUserId, Is.EqualTo(w.StudentId), "who did what, and when, is kept");
        Assert.That(await check.ApiCallTimings.CountAsync(t => t.SessionId == sessionId), Is.EqualTo(1), "the timing row is kept");
    }

    [Test]
    public async Task CallTimings_ArePrunedOnlyPastTheirOwnPeriod()
    {
        var session = Guid.NewGuid();
        await using (var db = SystemDb())
        {
            db.ApiCallTimings.AddRange(
                Timing(null, session, DateTimeOffset.UtcNow.AddDays(-30)),
                Timing(null, session, DateTimeOffset.UtcNow.AddDays(-200)),
                Timing(null, session, DateTimeOffset.UtcNow.AddDays(-400)));
            await db.SaveChangesAsync();
        }
        await using (var scope = Scope(TenantSnapshot.System))
            await scope.ServiceProvider.GetRequiredService<LogRetention>().PruneCallTimingsAsync();

        await using var check = SystemDb();
        var left = await check.ApiCallTimings.AsNoTracking().Where(t => t.SessionId == session).Select(t => t.StartedAtUtc).ToListAsync();
        Assert.That(left, Has.Count.EqualTo(2), "a year by default: the 400-day-old row goes, the others stay");
        Assert.That(left.All(t => t > DateTimeOffset.UtcNow.AddDays(-365)), Is.True);
    }

    [Test]
    public async Task Erasure_KeepsTheLogRows_WithoutTheStudentsDetail_AndRecordsItself()
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync(Words);
        await using (var db = SystemDb())
        {
            db.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = w.Org, ActorUserId = w.StudentId, ActorRole = "Student", Action = "research.consent.recorded",
                EntityType = "ResearchConsent", EntityId = Guid.NewGuid().ToString(), AfterJson = $"{{\"note\":\"{Words}\"}}", At = DateTimeOffset.UtcNow,
            });
            db.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = w.Org, ActorUserId = Guid.NewGuid(), ActorRole = "Admin", Action = "member.updated",
                EntityType = "Member", EntityId = w.StudentId.ToString(), BeforeJson = $"{{\"name\":\"{Words}\"}}", At = DateTimeOffset.UtcNow,
            });
            db.ApiCallTimings.Add(Timing(w.Org, sessionId, DateTimeOffset.UtcNow, error: $"HttpRequestException: {Words}"));
            await db.SaveChangesAsync();
        }

        await using (var scope = Scope(TenantSnapshot.ForOrganization(w.Org)))
            await scope.ServiceProvider.GetRequiredService<PrivacyService>().DeleteStudentDataAsync(w.Org, w.StudentId);

        await using var check = SystemDb();
        var key = w.StudentId.ToString();
        var rows = await check.AuditEvents.AsNoTracking()
            .Where(a => a.OrganizationId == w.Org && (a.ActorUserId == w.StudentId || a.EntityId == key) && a.Action != PrivacyService.ErasedAction).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(2), "the events themselves are kept");
        Assert.That(rows.All(r => r.BeforeJson is null && r.AfterJson is null), Is.True, "their detail is gone");
        var timing = await check.ApiCallTimings.AsNoTracking().SingleAsync(t => t.SessionId == sessionId);
        Assert.That(timing.ErrorSummary, Is.Null);
        Assert.That(timing.DurationMs, Is.EqualTo(1200), "the timing itself is kept");
        var erased = await check.AuditEvents.AsNoTracking().SingleAsync(a => a.OrganizationId == w.Org && a.Action == PrivacyService.ErasedAction && a.EntityId == key);
        Assert.That(erased.AfterJson, Does.Contain("attemptsErased"));
        Assert.That(erased.AfterJson, Does.Not.Contain("chlorophyll"));
    }

    [Test]
    public async Task Erasure_LeavesOtherStudentsLogsAlone()
    {
        var target = await Seed.EndedSessionAsync(Words);
        var bystander = await Seed.EndedSessionAsync("bystander");
        await using (var db = SystemDb())
        {
            db.ApiCallTimings.Add(Timing(bystander.World.Org, bystander.SessionId, DateTimeOffset.UtcNow, error: "HTTP 500"));
            await db.SaveChangesAsync();
        }
        await using (var scope = Scope(TenantSnapshot.ForOrganization(target.World.Org)))
            await scope.ServiceProvider.GetRequiredService<PrivacyService>().DeleteStudentDataAsync(target.World.Org, target.World.StudentId);

        await using var check = SystemDb();
        Assert.That((await check.ApiCallTimings.AsNoTracking().SingleAsync(t => t.SessionId == bystander.SessionId)).ErrorSummary, Is.EqualTo("HTTP 500"));
    }
}
