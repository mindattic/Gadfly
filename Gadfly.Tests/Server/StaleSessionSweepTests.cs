using Gadfly.Engine;
using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// A session nobody ever comes back to closes itself once it has sat well past its own deadline plus the sweep's grace
/// window (GAD-T306); a session staff are actively holding is left alone for them to resolve (GAD-T299).
/// </summary>
[TestFixture]
public class StaleSessionSweepTests : ServerTest
{
    private static readonly SessionPolicy ShortExam = new() { ConceptIds = ["photosynthesis"], MaxTurns = 6, TimeBudget = TimeSpan.FromMinutes(20) };

    private static SessionOrchestrator Orchestrator => Services.GetRequiredService<SessionOrchestrator>();
    private static StaleSessionSweep Sweep => Services.GetRequiredService<StaleSessionSweep>();

    /// <summary>A session past consent with its first question asked and the clock started, with nobody touching it again.</summary>
    private static async Task<(Seed.ExamWorld World, Guid SessionId)> RunningAsync()
    {
        var w = await Seed.ExamAsync(policy: ShortExam);
        var id = await Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, "test-browser");
        await Orchestrator.RecordConsentAsync(w.Org, id, w.StudentId, new string('c', 64));
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        await Orchestrator.StartClockAsync(w.Org, id, w.StudentId);
        return (w, id);
    }

    [Test]
    public async Task AbandonedSession_PastItsDeadlinePlusGrace_IsClosedAndAudited()
    {
        var (_, id) = await RunningAsync();
        Clock.Advance(ShortExam.TimeBudget + StaleSessionSweep.Grace + TimeSpan.FromMinutes(1));

        var closed = await Sweep.SweepAsync();

        Assert.That(closed, Is.EqualTo(1));
        await using var db = SystemDb();
        var row = await db.ExamSessions.AsNoTracking().FirstAsync(s => s.Id == id);
        Assert.That(row.State, Is.EqualTo(SessionState.Ended));
        Assert.That(row.EndReason, Is.EqualTo(nameof(EndReason.TimeUp)), "graded fairly, the same as a real time-up");
        var audit = await db.AuditEvents.AsNoTracking().FirstAsync(a => a.SessionId == id && a.Action == LiveSessionService.AutoClosedAction);
        Assert.That(audit.Reason, Does.Contain("abandoned past time limit"));
        Assert.That(audit.ActorUserId, Is.Null, "closed by the sweep, not by a staff member");
    }

    [Test]
    public async Task AbandonedSession_WithinTheGraceWindow_IsLeftAlone()
    {
        var (_, id) = await RunningAsync();
        // Past its own deadline, but well inside the grace window: not yet counted as abandoned.
        Clock.Advance(ShortExam.TimeBudget + TimeSpan.FromMinutes(5));

        var closed = await Sweep.SweepAsync();

        Assert.That(closed, Is.EqualTo(0));
        await using var db = SystemDb();
        var row = await db.ExamSessions.AsNoTracking().FirstAsync(s => s.Id == id);
        Assert.That(row.State, Is.Not.EqualTo(SessionState.Ended));
    }

    [Test]
    public async Task StaffHeldSession_IsNeverClosedBySweep_EvenLongAfterItsDeadline()
    {
        var (w, id) = await RunningAsync();
        await Orchestrator.StaffStepAsync(w.Org, id, w.StudentId, (s, now) => s.Hold(now));
        Clock.Advance(ShortExam.TimeBudget + StaleSessionSweep.Grace + TimeSpan.FromHours(2));

        var closed = await Sweep.SweepAsync();

        Assert.That(closed, Is.EqualTo(0), "a staff pause is a deliberate, visible action (GAD-T299), not an abandonment");
        await using var db = SystemDb();
        var row = await db.ExamSessions.AsNoTracking().FirstAsync(s => s.Id == id);
        Assert.That(row.State, Is.EqualTo(SessionState.Paused));
    }
}
