using Gadfly.Assessment;
using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The admin dashboard (GAD-T300): what needs attention, the 7- and 30-day numbers, the getting-started checklist, tenant isolation,
/// and who may load it.
/// </summary>
[TestFixture]
public class AdminDashboardTests : ServerTest
{
    private static AdminDashboardService Service(GadflyDbContext db, TenantSnapshot tenant) => new(db, new FixedTenant(tenant), Clock);

    private static async Task<AdminDashboard> LoadAsync(Guid org, string role = OrgRoles.Admin)
    {
        var me = Member(org, Guid.NewGuid(), role);
        await using var db = Db(me);
        return await Service(db, me).LoadAsync();
    }

    private static AttentionGroup Group(AdminDashboard d, AttentionKind kind) => d.Attention.Single(g => g.Kind == kind);

    private static ExamSession Session(Seed.ExamWorld w, SessionState state, DateTimeOffset? started, DateTimeOffset? ended, string? endReason = null, int attempt = 1) => new()
    {
        OrganizationId = w.Org, AssignmentId = w.AssignmentId, ExamId = w.ExamId, StudentUserId = w.StudentId, Attempt = attempt, State = state,
        CreatedAt = started ?? Clock.GetUtcNow(), StartedAt = started, EndedAt = ended, EndReason = endReason,
    };

    [Test]
    public async Task BrandNewOrganization_HasNothingToAttend_EmptyNumbers_AndAnOpenChecklist()
    {
        var org = await Seed.OrganizationAsync();
        var d = await LoadAsync(org);

        Assert.That(d.AttentionTotal, Is.Zero);
        Assert.That(d.Attention.Select(g => g.Kind), Is.EquivalentTo(Enum.GetValues<AttentionKind>()), "every group is always reported, empty or not");
        Assert.That(d.Last7.ExamsTaken, Is.Zero);
        Assert.That(d.Last30.CompletionRate, Is.Null);
        Assert.That(d.Last30.MedianDuration, Is.Null);
        Assert.That(d.Last30.MedianTurnaround, Is.Null);
        Assert.That(d.IsNew, Is.True);
        Assert.That(d.Setup.Select(s => s.Key), Is.EqualTo(new[] { "exam", "students", "watch" }));
        Assert.That(d.Setup.All(s => !s.Done), Is.True);
    }

    [Test]
    public async Task Checklist_TicksOffAsTheOrganizationSetsUp()
    {
        var w = await Seed.ExamAsync();
        await Seed.MemberAsync(Services, w.Org, OrgRoles.Student);
        var d = await LoadAsync(w.Org);
        Assert.That(d.Setup.Single(s => s.Key == "exam").Done, Is.True);
        Assert.That(d.Setup.Single(s => s.Key == "students").Done, Is.True);
        Assert.That(d.Setup.Single(s => s.Key == "watch").Done, Is.False, "no interview has ended yet");

        var now = Clock.GetUtcNow();
        await using (var db = SystemDb())
        {
            db.ExamSessions.Add(Session(w, SessionState.Ended, now.AddMinutes(-30), now.AddMinutes(-10), "Mastery"));
            await db.SaveChangesAsync();
        }
        d = await LoadAsync(w.Org);
        Assert.That(d.IsNew, Is.False);
    }

    [Test]
    public async Task NeedsAttention_FindsEachKind_AndLinksStraightToWhereToAct()
    {
        var now = Clock.GetUtcNow();
        var w = await Seed.ExamAsync(closes: now.AddDays(2));
        var other = await Seed.ExamAsync(organization: w.Org);
        Guid ungraded, reconcile, approve, appeal;
        await using (var db = SystemDb())
        {
            db.ExamSessions.Add(Session(other, SessionState.Running, now.AddMinutes(-12), null));
            var s1 = Session(other, SessionState.Ended, now.AddHours(-5), now.AddHours(-4), "Mastery", attempt: 2);
            var s2 = Session(other, SessionState.Ended, now.AddHours(-6), now.AddHours(-5), "TimeUp", attempt: 3);
            var s3 = Session(other, SessionState.Ended, now.AddHours(-7), now.AddHours(-6), "Mastery", attempt: 4);
            var s4 = Session(other, SessionState.Ended, now.AddHours(-8), now.AddHours(-7), "Mastery", attempt: 5);
            db.ExamSessions.AddRange(s1, s2, s3, s4);
            (ungraded, reconcile, approve, appeal) = (s1.Id, s2.Id, s3.Id, s4.Id);
            var objective = new LearningObjective { OrganizationId = other.Org, ExamId = other.ExamId, Key = "obj", Title = "Objective", MaxScore = 100, Weight = 1 };
            db.LearningObjectives.Add(objective);
            db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = s2.Id, Status = GradeStatus.ReconciliationRequired, UpdatedAt = now });
            db.GradeDisagreements.Add(new GradeDisagreement
            {
                OrganizationId = w.Org, SessionId = s2.Id, ObjectiveId = objective.Id, AiScore = 1, HumanScore = 3, DifferencePoints = 50,
                Band = DisagreementBand.MandatoryReconciliation, DetectedAt = now.AddHours(-1),
            });
            db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = s3.Id, Status = GradeStatus.AwaitingFinalApproval, FinalPercent = 72, UpdatedAt = now });
            db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = s4.Id, Status = GradeStatus.UnderAppeal, UpdatedAt = now });
            db.GradeAppeals.Add(new GradeAppeal { OrganizationId = w.Org, SessionId = s4.Id, StudentUserId = other.StudentId, Reason = "r", State = AppealState.Open, OpenedAt = now });
            db.Jobs.Add(new Job { OrganizationId = w.Org, Kind = "grading.ai", State = JobState.Failed, Attempts = 4, Error = "boom", CreatedAt = now.AddHours(-2), CompletedAt = now.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        var d = await LoadAsync(w.Org);

        Assert.That(Group(d, AttentionKind.InProgress).Count, Is.EqualTo(1));
        Assert.That(Group(d, AttentionKind.InProgress).Items.Single().Href, Is.EqualTo("admin/live"));
        Assert.That(Group(d, AttentionKind.AwaitingGrading).Items.Select(i => i.Href), Is.EqualTo(new[] { $"grading/{ungraded}" }));
        Assert.That(Group(d, AttentionKind.Reconciliation).Items.Single().Href, Is.EqualTo($"grading/{reconcile}"));
        Assert.That(Group(d, AttentionKind.Approval).Items.Single().Href, Is.EqualTo($"grading/{approve}"));
        Assert.That(Group(d, AttentionKind.Appeal).Items.Single().Href, Is.EqualTo($"grading/{appeal}"));
        Assert.That(Group(d, AttentionKind.NotStartedNearDue).Items.Single().Href, Is.EqualTo($"admin/students/{w.StudentId}"));
        Assert.That(Group(d, AttentionKind.FailedJob).Count, Is.EqualTo(1));
        Assert.That(Group(d, AttentionKind.FailedJob).Items.Single().Title, Is.EqualTo("AI preliminary grading"));
        Assert.That(d.Attention.SelectMany(g => g.Items).All(i => !i.Title.Contains('\u0001')), Is.True, "every student placeholder is replaced by a name");
    }

    [Test]
    public async Task NotStarted_IgnoresStartedAssignments_FarDueDates_AndPastDueDates()
    {
        var now = Clock.GetUtcNow();
        var started = await Seed.ExamAsync(closes: now.AddDays(1));
        await Seed.ExamAsync(organization: started.Org, closes: now.AddDays(10));
        await Seed.ExamAsync(organization: started.Org, closes: now.AddDays(-1));
        await using (var db = SystemDb())
        {
            db.ExamSessions.Add(Session(started, SessionState.Running, now.AddMinutes(-3), null));
            await db.SaveChangesAsync();
        }
        var d = await LoadAsync(started.Org);
        Assert.That(Group(d, AttentionKind.NotStartedNearDue).Count, Is.Zero);
    }

    [Test]
    public async Task Numbers_CountTakenCompletedMedianDurationAndTurnaround_PerWindow()
    {
        var now = Clock.GetUtcNow();
        var w = await Seed.ExamAsync();
        await using (var db = SystemDb())
        {
            // Within 7 days: two proper ends (10 and 20 minutes) and one abandoned (30 minutes).
            var a = Session(w, SessionState.Ended, now.AddDays(-1).AddMinutes(-10), now.AddDays(-1), "Mastery", 1);
            var b = Session(w, SessionState.Ended, now.AddDays(-2).AddMinutes(-20), now.AddDays(-2), "TimeUp", 2);
            var c = Session(w, SessionState.Ended, now.AddDays(-3).AddMinutes(-30), now.AddDays(-3), "Abandoned", 3);
            // 8..30 days back: one more proper end; and an interview still running, which is in neither rate.
            var e = Session(w, SessionState.Ended, now.AddDays(-20).AddMinutes(-40), now.AddDays(-20), "Mastery", 4);
            var running = Session(w, SessionState.Running, now.AddMinutes(-5), null, null, 5);
            // A session that never counted as an attempt is left out everywhere.
            var stale = Session(w, SessionState.Ended, now.AddDays(-1).AddMinutes(-2), now.AddDays(-1), "PromptSetChanged", 6);
            db.ExamSessions.AddRange(a, b, c, e, running, stale);
            // Grade a approved 2 hours after its end; e approved 1 day after.
            db.ApprovalRecords.Add(new ApprovalRecord { OrganizationId = w.Org, SessionId = a.Id, Action = ApprovalAction.Finalize, ActorRole = "Admin", At = now.AddDays(-1).AddHours(2) });
            db.ApprovalRecords.Add(new ApprovalRecord { OrganizationId = w.Org, SessionId = e.Id, Action = ApprovalAction.Finalize, ActorRole = "Admin", At = now.AddDays(-19) });
            await db.SaveChangesAsync();
        }

        var d = await LoadAsync(w.Org);

        Assert.That(d.Last7.ExamsTaken, Is.EqualTo(3));
        Assert.That(d.Last7.Started, Is.EqualTo(3));
        Assert.That(d.Last7.Completed, Is.EqualTo(2));
        Assert.That(d.Last7.CompletionRate, Is.EqualTo(2.0 / 3).Within(1e-9));
        Assert.That(d.Last7.MedianDuration, Is.EqualTo(TimeSpan.FromMinutes(20)));
        Assert.That(d.Last7.GradesApproved, Is.EqualTo(1));
        Assert.That(d.Last7.MedianTurnaround, Is.EqualTo(TimeSpan.FromHours(2)));

        Assert.That(d.Last30.ExamsTaken, Is.EqualTo(4));
        Assert.That(d.Last30.Completed, Is.EqualTo(3));
        Assert.That(d.Last30.MedianDuration, Is.EqualTo(TimeSpan.FromMinutes(25)));
        Assert.That(d.Last30.GradesApproved, Is.EqualTo(2));
    }

    [Test]
    public async Task AnotherOrganizationsWork_NeverShows()
    {
        var now = Clock.GetUtcNow();
        var mine = await Seed.OrganizationAsync();
        var theirs = await Seed.ExamAsync(closes: now.AddDays(1));
        await using (var db = SystemDb())
        {
            db.ExamSessions.Add(Session(theirs, SessionState.Running, now.AddMinutes(-3), null));
            db.ExamSessions.Add(Session(theirs, SessionState.Ended, now.AddHours(-2), now.AddHours(-1), "Mastery", 2));
            db.Jobs.Add(new Job { OrganizationId = theirs.Org, Kind = "source.process", State = JobState.Failed, CreatedAt = now, CompletedAt = now });
            await db.SaveChangesAsync();
        }
        var d = await LoadAsync(mine);
        Assert.That(d.AttentionTotal, Is.Zero);
        Assert.That(d.Last30.ExamsTaken, Is.Zero);
        Assert.That(d.Exams, Is.Zero);
        Assert.That(d.Assignments, Is.Zero);
    }

    [TestCase(OrgRoles.Instructor)]
    [TestCase(OrgRoles.Examiner)]
    [TestCase(OrgRoles.SecondReviewer)]
    [TestCase(OrgRoles.Student)]
    public async Task OnlyOwnersAndAdmins_MayLoadIt(string role)
    {
        var org = await Seed.OrganizationAsync();
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => LoadAsync(org, role));
    }

    [Test]
    public async Task Owner_MayLoadIt()
    {
        var org = await Seed.OrganizationAsync();
        Assert.That((await LoadAsync(org, OrgRoles.Owner)).Setup, Is.Not.Empty);
    }
}

/// <summary>The dashboard's and admin map's pure helpers (GAD-T300): no database needed.</summary>
[TestFixture]
public class AdminDashboardHelperTests
{
    [Test]
    public void Median_OddEvenEmpty_AndIgnoresNegativeSpans()
    {
        Assert.That(AdminDashboardService.Median([]), Is.Null);
        Assert.That(AdminDashboardService.Median([TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(9)]), Is.EqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(AdminDashboardService.Median([TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20)]), Is.EqualTo(TimeSpan.FromMinutes(15)));
        Assert.That(AdminDashboardService.Median([TimeSpan.FromMinutes(-3), TimeSpan.FromMinutes(4)]), Is.EqualTo(TimeSpan.FromMinutes(4)));
    }

    [TestCase(0.5, "30 s")]
    [TestCase(18, "18 min")]
    [TestCase(65, "1 h 5 min")]
    [TestCase(600, "10 h")]
    [TestCase(60 * 24 * 3, "3 days")]
    public void Duration_ReadsPlainly(double minutes, string expected) =>
        Assert.That(AdminDashboardService.Duration(TimeSpan.FromMinutes(minutes)), Is.EqualTo(expected));

    [Test]
    public void Duration_None_IsADash() => Assert.That(AdminDashboardService.Duration(null), Is.EqualTo("—"));

    [Test]
    public void Setup_IsDoneOnlyWhenEveryStepIs()
    {
        Assert.That(AdminDashboardService.Setup(1, 1, true).All(s => s.Done), Is.True);
        Assert.That(AdminDashboardService.Setup(1, 0, true).Single(s => s.Key == "students").Done, Is.False);
    }

    [Test]
    public void AdminArea_ShowsEachRoleOnlyWhatItMayOpen()
    {
        static IEnumerable<string> Hrefs(string? role, bool platform = false, bool desk = false) =>
            AdminArea.GroupsFor(role, platform, desk).SelectMany(g => g.Links).Select(l => l.Href);

        var admin = Hrefs(OrgRoles.Admin).ToList();
        Assert.That(admin, Does.Contain("admin").And.Contain("admin/students").And.Contain("admin/exams")
            .And.Contain("admin/live").And.Contain("admin/audit").And.Contain("admin/status").And.Contain("org/settings").And.Contain("grading"));
        Assert.That(admin, Does.Not.Contain("platform/organizations").And.Not.Contain("admin/desk").And.Not.Contain("admin/assignments"));
        Assert.That(Hrefs(OrgRoles.Owner), Is.EquivalentTo(admin));

        var instructor = Hrefs(OrgRoles.Instructor).ToList();
        Assert.That(instructor, Does.Contain("review").And.Contain("analytics").And.Contain("grading"));
        Assert.That(instructor, Does.Not.Contain("admin").And.Not.Contain("org/settings").And.Not.Contain("admin/students"));

        Assert.That(Hrefs(OrgRoles.Examiner), Is.EqualTo(new[] { "grading" }));
        Assert.That(Hrefs(OrgRoles.Student), Is.Empty);
        Assert.That(Hrefs(null, platform: true), Is.EqualTo(new[] { "platform/organizations" }));
        Assert.That(Hrefs(OrgRoles.Admin, desk: true), Does.Contain("admin/desk"));
        Assert.That(AdminArea.GroupsFor(OrgRoles.Examiner, false, false).Select(g => g.Name), Is.EqualTo(new[] { "Results" }), "an empty group is dropped");
    }

    [TestCase("admin", true)]
    [TestCase("admin/students/3f2a", true)]
    [TestCase("org/members?x=1", true)]
    [TestCase("/grading/abc", true)]
    [TestCase("my-exams", false)]
    [TestCase("exam/abc", false)]
    [TestCase("", false)]
    [TestCase("help", false)]
    public void AdminArea_KnowsWhichPagesAreAdminPages(string path, bool expected) =>
        Assert.That(AdminArea.Contains(path), Is.EqualTo(expected));
}
