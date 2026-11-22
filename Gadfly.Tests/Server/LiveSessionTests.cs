using Gadfly.Engine;
using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Review;
using Gadfly.Server.Sessions;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The live sessions monitor (GAD-T299): staff extra time and pauses in the engine, the monitor's view, the staff actions (reason required,
/// audited, on the review timeline), the student-side guard while paused, and tenant isolation.
/// </summary>
[TestFixture]
public class LiveSessionTests : ServerTest
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private static SessionOrchestrator Orchestrator => Services.GetRequiredService<SessionOrchestrator>();

    private static LiveSessionService Service(GadflyDbContext db, TenantSnapshot tenant, LiveSessionFeed? feed = null) =>
        new(db, new FixedTenant(tenant), Clock, Orchestrator, feed ?? new LiveSessionFeed());

    /// <summary>A session past consent with its first question asked and the clock started.</summary>
    private static async Task<Guid> RunningAsync(Seed.ExamWorld w)
    {
        var id = await Orchestrator.CreateOrResumeAsync(w.Org, w.AssignmentId, w.StudentId, "test-browser");
        await Orchestrator.RecordConsentAsync(w.Org, id, w.StudentId, new string('c', 64));
        await Orchestrator.CurrentAsync(w.Org, id, w.StudentId);
        await Orchestrator.StartClockAsync(w.Org, id, w.StudentId);
        return id;
    }

    // ------------------------------------------------------------------ engine

    [Test]
    public void ExtraTime_AddsToTheTimeLeft_UpToTheCap()
    {
        var s = new SocraticSession("x", new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(20) }, ExamPrompts.Set, T0);
        Assert.That(s.ExtendTime(TimeSpan.FromMinutes(10)), Is.EqualTo(TimeSpan.FromMinutes(10)));
        Assert.That(s.Remaining(T0), Is.EqualTo(TimeSpan.FromMinutes(30)));
        var rest = s.ExtendTime(TimeSpan.FromHours(5));
        Assert.That(rest, Is.EqualTo(SocraticSession.MaxExtraTime - TimeSpan.FromMinutes(10)), "capped in all");
        Assert.That(s.ExtendTime(TimeSpan.FromMinutes(1)), Is.EqualTo(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.ExtendTime(TimeSpan.Zero));
    }

    [Test]
    public void StaffPause_StopsTheClock_IsNotEndedByAnAnswerAllowance_AndAddsTheTimeBack()
    {
        var s = new SocraticSession("x", new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(20) }, ExamPrompts.Set, T0);
        Assert.That(s.Hold(T0.AddMinutes(5)), Is.True);
        Assert.That(s.Hold(T0.AddMinutes(6)), Is.False, "already paused");
        Assert.That(s.PauseClock(T0.AddMinutes(6)), Is.False, "no device pause on top of a staff pause");
        // Thirty minutes paused: longer than the device pause allowance, still all added back.
        Assert.That(s.Remaining(T0.AddMinutes(35)), Is.EqualTo(TimeSpan.FromMinutes(15)));
        Assert.That(s.Release(T0.AddMinutes(35)), Is.True);
        Assert.That(s.Remaining(T0.AddMinutes(40)), Is.EqualTo(TimeSpan.FromMinutes(10)));
        Assert.That(s.Deadline(T0.AddMinutes(40)), Is.EqualTo(T0.AddMinutes(50)));
    }

    [Test]
    public void StaffPause_EndsAnOpenDevicePause_AndEndingReleasesIt()
    {
        var s = new SocraticSession("x", new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(20) }, ExamPrompts.Set, T0);
        s.PauseClock(T0.AddMinutes(2));
        s.Hold(T0.AddMinutes(4));
        Assert.That(s.IsPaused, Is.False);
        Assert.That(s.PausedTotal, Is.EqualTo(TimeSpan.FromMinutes(2)));
        s.End(EndReason.Abandoned, T0.AddMinutes(10));
        Assert.That(s.IsHeld, Is.False);
        Assert.That(s.HeldTotal, Is.EqualTo(TimeSpan.FromMinutes(6)));
    }

    [Test]
    public void ExtraTimeAndStaffPause_SurviveTheTranscript()
    {
        var s = new SocraticSession("x", new SessionPolicy(), ExamPrompts.Set, T0);
        s.ExtendTime(TimeSpan.FromMinutes(7));
        s.Hold(T0.AddMinutes(1));
        s.Release(T0.AddMinutes(4));
        s.Hold(T0.AddMinutes(6));
        var back = SessionTranscriptSerializer.Deserialize(SessionTranscriptSerializer.Serialize(s));
        Assert.That(back.ExtraTime, Is.EqualTo(TimeSpan.FromMinutes(7)));
        Assert.That(back.HeldTotal, Is.EqualTo(TimeSpan.FromMinutes(3)));
        Assert.That(back.HeldSince, Is.EqualTo(T0.AddMinutes(6)));
        Assert.That(back.Remaining(T0.AddMinutes(10)), Is.EqualTo(s.Remaining(T0.AddMinutes(10))));
    }

    // ------------------------------------------------------------------ monitor and actions

    [Test]
    public async Task Monitor_ListsTheOrganizationsExamsInProgress_Only()
    {
        var w = await Seed.ExamAsync();
        var other = await Seed.ExamAsync();
        var mine = await RunningAsync(w);
        await RunningAsync(other);
        var admin = Member(w.Org, Guid.NewGuid(), OrgRoles.Admin);
        await using var db = Db(admin);
        var view = await Service(db, admin, LiveSessionFeed.Shared).LoadAsync();
        Assert.That(view.Sessions.Select(s => s.SessionId), Is.EqualTo(new[] { mine }), "another organization's exams never show");
        var row = view.Sessions[0];
        Assert.That(row.ClockStarted, Is.True);
        Assert.That(row.QuestionNumber, Is.EqualTo(1));
        Assert.That(row.Remaining, Is.Not.Null);
        Assert.That(row.CanAct, Is.True);
    }

    [Test]
    public async Task Monitor_AfterARestart_ReadsTheSavedTranscript()
    {
        var w = await Seed.ExamAsync();
        var id = await RunningAsync(w);
        var admin = Member(w.Org, Guid.NewGuid(), OrgRoles.Owner);
        await using var db = Db(admin);
        var view = await Service(db, admin, new LiveSessionFeed()).LoadAsync();   // a feed that has seen nothing
        var row = view.Sessions.Single(s => s.SessionId == id);
        Assert.That(row.Remaining, Is.Not.Null);
        Assert.That(row.QuestionNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task Actions_NeedAReason_AndAnOwnerOrAdmin()
    {
        var w = await Seed.ExamAsync();
        var id = await RunningAsync(w);
        var admin = Member(w.Org, Guid.NewGuid(), OrgRoles.Admin);
        await using (var db = Db(admin))
        {
            var r = await Service(db, admin).PauseAsync(id, "  ");
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Message, Does.Contain("Say why"));
            Assert.That((await Service(db, admin).ExtendAsync(id, 0, "x")).Ok, Is.False);
            Assert.That((await Service(db, admin).ExtendAsync(id, 61, "x")).Ok, Is.False);
        }
        var instructor = Member(w.Org, Guid.NewGuid(), OrgRoles.Instructor);
        await using (var db = Db(instructor))
        {
            var r = await Service(db, instructor).PauseAsync(id, "fire alarm");
            Assert.That(r.Ok, Is.False);
            Assert.That(await db.AuditEvents.AnyAsync(a => a.Action == "access.denied" && a.EntityId == id.ToString()), Is.True);
        }
    }

    [Test]
    public async Task Pause_HoldsTheExam_StudentAnswersWait_Resume_AddsTheTimeBack_AllAudited_AndOnTheTimeline()
    {
        var w = await Seed.ExamAsync();
        var id = await RunningAsync(w);
        var admin = Member(w.Org, Guid.NewGuid(), OrgRoles.Admin);
        await using var db = Db(admin);
        var svc = Service(db, admin);

        Assert.That((await svc.PauseAsync(id, "Fire alarm in the building")).Ok, Is.True);
        await using (var sys = SystemDb())
            Assert.That((await sys.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id)).State, Is.EqualTo(SessionState.Paused));
        var refused = Assert.ThrowsAsync<SessionRefusedException>(() =>
            Orchestrator.AnswerAsync(w.Org, id, w.StudentId, new Answer { Text = "an answer", CompletedAt = Clock.GetUtcNow() }));
        Assert.That(refused!.Message, Is.EqualTo(SessionOrchestrator.HeldByStaffMessage));
        var step = await Orchestrator.ResumeClockAsync(w.Org, id, w.StudentId);
        Assert.That(step.HeldByStaff, Is.True, "the exam room's own resume never ends a staff pause");

        Assert.That((await svc.ResumeAsync(id, "All clear")).Ok, Is.True);
        Assert.That((await svc.ExtendAsync(id, 5, "Lost ten minutes to the alarm")).Ok, Is.True);
        Assert.That((await svc.SendNoticeAsync(id, "Carry on when you are ready.")).Ok, Is.True);

        var actions = await db.AuditEvents.AsNoTracking().Where(a => a.SessionId == id && a.Action.StartsWith(LiveSessionService.AuditPrefix))
            .OrderBy(a => a.Id).Select(a => a.Action).ToListAsync();
        Assert.That(actions, Is.EqualTo(new[]
        {
            LiveSessionService.PausedAction, LiveSessionService.ResumedAction, LiveSessionService.ExtendedAction, LiveSessionService.NoticeAction,
        }));

        var review = await new ReviewService(db, w.Org, Clock, OrgRoles.Admin).GetAsync(id);
        var staff = review!.Timeline.Where(t => t.Action is not null).Select(t => t.Action!).ToList();
        Assert.That(staff, Has.Count.EqualTo(4));
        Assert.That(staff[0].Reason, Is.EqualTo("Fire alarm in the building"));
        Assert.That(staff[2].Detail, Does.Contain("5 minutes"));
    }

    [Test]
    public async Task End_GoesThroughTheNormalEndPath_AndTheAttemptIsReported()
    {
        var w = await Seed.ExamAsync();
        var id = await RunningAsync(w);
        var admin = Member(w.Org, Guid.NewGuid(), OrgRoles.Admin);
        await using var db = Db(admin);
        var r = await Service(db, admin).EndAsync(id, "The student was taken ill");
        Assert.That(r.Ok, Is.True, r.Message);
        await using var sys = SystemDb();
        var row = await sys.ExamSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.That(row.State, Is.EqualTo(SessionState.Ended));
        Assert.That(row.EndReason, Is.EqualTo(nameof(EndReason.Abandoned)));
        Assert.That(await sys.SessionReports.AnyAsync(x => x.SessionId == id), Is.True, "graded like any other ended attempt");
        Assert.That((await Service(db, admin).PauseAsync(id, "too late")).Ok, Is.False);
    }

    [Test]
    public async Task TimeUpFromAnOldCountdown_DoesNotEndAnExamThatStillHasTime()
    {
        var w = await Seed.ExamAsync();
        var id = await RunningAsync(w);
        var step = await Orchestrator.EndAsync(w.Org, id, w.StudentId, EndReason.TimeUp);
        Assert.That(step.State, Is.Not.EqualTo(SessionState.Ended));
        Assert.That(step.Question, Is.Not.Null);
    }

    [Test]
    public async Task Actions_OnAnotherOrganizationsExam_AreRefused()
    {
        var w = await Seed.ExamAsync();
        var id = await RunningAsync(w);
        var otherOrg = await Seed.OrganizationAsync();
        var stranger = Member(otherOrg, Guid.NewGuid(), OrgRoles.Owner);
        await using var db = Db(stranger);
        var r = await Service(db, stranger).ExtendAsync(id, 5, "sneaky");
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("not found"));
        Assert.That((await Service(db, stranger).SendNoticeAsync(id, "hello")).Ok, Is.False);
    }

    [Test]
    public void Feed_KeepsTheLatestSnapshot_CountsRooms_AndForgetsEndedExams()
    {
        var feed = new LiveSessionFeed();
        var org = Guid.NewGuid();
        var session = Guid.NewGuid();
        var seen = new List<LiveEventKind>();
        using var sub = feed.Subscribe(e => seen.Add(e.Kind));
        feed.Stepped(org, Guid.NewGuid(), new SessionStep { SessionId = session, State = SessionState.Running, Remaining = TimeSpan.FromMinutes(9) }, T0);
        Assert.That(feed.Snapshot(org, session)!.RemainingAt(T0.AddMinutes(1)), Is.EqualTo(TimeSpan.FromMinutes(8)));
        Assert.That(feed.Snapshot(Guid.NewGuid(), session), Is.Null, "another organization never reads it");
        var room = feed.RoomOpened(org, session, T0);
        Assert.That(feed.RoomsOpen(org, session), Is.EqualTo(1));
        Assert.That(feed.RoomsOpen(Guid.NewGuid(), session), Is.Zero, "another organization never counts its rooms");
        room.Dispose();
        room.Dispose();
        Assert.That(feed.RoomsOpen(org, session), Is.Zero);
        feed.Notice(org, session, "hello", T0);
        Assert.That(feed.RecentNotice(org, session, T0.AddMinutes(1))?.Text, Is.EqualTo("hello"));
        Assert.That(feed.RecentNotice(Guid.NewGuid(), session, T0.AddMinutes(1)), Is.Null, "another organization never reads the notice");
        Assert.That(feed.RecentNotice(org, session, T0 + LiveSessionFeed.NoticeKeptFor + TimeSpan.FromSeconds(1)), Is.Null);
        feed.Stepped(org, Guid.NewGuid(), new SessionStep { SessionId = session, State = SessionState.Ended }, T0);
        Assert.That(feed.Snapshot(org, session), Is.Null);
        Assert.That(seen, Does.Contain(LiveEventKind.Room).And.Contain(LiveEventKind.Notice).And.Contain(LiveEventKind.Stepped));
    }
}
