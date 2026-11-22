using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server.Scenarios;

/// <summary>GAD-T444: the reconciler-approver rule itself, without a database.</summary>
[TestFixture]
public class ApproverSeparationRuleTests
{
    [TestCase(ApproverSeparationSetting.Automatic)]
    [TestCase(ApproverSeparationSetting.Always)]
    [TestCase(ApproverSeparationSetting.Off)]
    public void AnApproverWhoReconciledNothing_IsNeverAffected(ApproverSeparationSetting setting)
    {
        var d = ApproverSeparation.Decide(setting, approverReconciled: false, singleGraderAllowed: false, anotherApproverAvailable: true);
        Assert.That(d, Is.EqualTo(ApproverSeparationDecision.NotApplicable));
    }

    [Test]
    public void Automatic_RefusesTheReconciler_WhenSomeoneElseCanApprove()
    {
        var d = ApproverSeparation.Decide(ApproverSeparationSetting.Automatic, true, false, anotherApproverAvailable: true);
        Assert.That((d.Applies, d.Refused, d.Waiver), Is.EqualTo((true, true, (string?)null)));
    }

    [Test]
    public void Automatic_LetsTheReconcilerApprove_WhenNoOneElseCan_WithAWaiver()
    {
        var d = ApproverSeparation.Decide(ApproverSeparationSetting.Automatic, true, false, anotherApproverAvailable: false);
        Assert.That(d.Refused, Is.False);
        Assert.That(d.Waiver, Does.Contain("No one else"));
    }

    [Test]
    public void Automatic_FollowsTheSingleGraderSetting_WithAWaiver()
    {
        var d = ApproverSeparation.Decide(ApproverSeparationSetting.Automatic, true, singleGraderAllowed: true, anotherApproverAvailable: true);
        Assert.That(d.Refused, Is.False);
        Assert.That(d.Waiver, Does.Contain("single-grader"));
    }

    [Test]
    public void Always_RefusesTheReconciler_EvenWhenNoOneElseCanApprove()
    {
        Assert.That(ApproverSeparation.Decide(ApproverSeparationSetting.Always, true, true, false).Refused, Is.True);
        Assert.That(ApproverSeparation.Decide(ApproverSeparationSetting.Always, true, false, false).Refused, Is.True);
    }

    [Test]
    public void Off_LetsTheReconcilerApprove_ButStillRecordsAWaiver()
    {
        var d = ApproverSeparation.Decide(ApproverSeparationSetting.Off, true, false, true);
        Assert.That(d.Applies, Is.True);
        Assert.That(d.Refused, Is.False);
        Assert.That(d.Waiver, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Automatic_IsTheDefault_ForANewOrganization() =>
        Assert.That(new OrganizationSettings().ApproverSeparation, Is.EqualTo(ApproverSeparationSetting.Automatic));
}

/// <summary>
/// Separate reconciler and approver (GAD-T444; ElenchusAtScale M13, Section 3.8, correction 47), through the real
/// GradingService on LocalDB: the person who reconciled a grade's disagreement cannot also approve it while someone else
/// could, and every exception the organization's setting allows is audited.
/// </summary>
[TestFixture]
[Category("Scenario")]
public class ReconcilerApproverTests : ServerTest
{
    private sealed record World(Guid Org, Guid SessionId, Guid DisagreementId);

    private GradingService As(Guid org, string role, Guid user, out GadflyDbContext db)
    {
        var tenant = Member(org, user, role);
        db = Db(tenant);
        return new GradingService(db, new FixedTenant(tenant), Clock, new JobQueue(db, Clock));
    }

    /// <summary>A grade whose human score (by someone outside the organization's member list) disagrees with the AI enough to need reconciling.</summary>
    private async Task<World> NeedsReconciliation()
    {
        var (w, sessionId, _) = await Seed.EndedSessionAsync();
        var objective = new LearningObjective { OrganizationId = w.Org, ExamId = w.ExamId, Key = "understanding", Title = "Understanding", MaxScore = 100, Weight = 1 };
        await using (var db = OrgDb(w.Org))
        {
            db.LearningObjectives.Add(objective);
            db.AiObjectiveGrades.Add(new AiObjectiveGrade
            {
                OrganizationId = w.Org, SessionId = sessionId, ObjectiveId = objective.Id, Level = MasteryLevel.Application, Score = 95, MaxScore = 100,
                Explanation = "AI rationale", Confidence = "high", CreatedAt = Clock.GetUtcNow(),
            });
            db.FinalGrades.Add(new FinalGrade { OrganizationId = w.Org, SessionId = sessionId, Status = GradeStatus.HumanReviewPending, UpdatedAt = Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        var scorer = As(w.Org, OrgRoles.Instructor, Guid.NewGuid(), out var sdb);
        await using (sdb)
        {
            var r = await scorer.SubmitHumanGradesAsync(sessionId, [new HumanGradeInput(objective.Id, MasteryLevel.Application, 60, null, null)]);
            Assert.That(r.Ok, Is.True, r.Message);
        }
        await using var sys = SystemDb();
        var disagreement = await sys.GradeDisagreements.AsNoTracking().Where(x => x.SessionId == sessionId && x.ResolvedAt == null).Select(x => x.Id).SingleAsync();
        return new World(w.Org, sessionId, disagreement);
    }

    private static async Task Set(Guid org, ApproverSeparationSetting setting)
    {
        await using var sys = SystemDb();
        await sys.OrganizationSettings.Where(s => s.OrganizationId == org).ExecuteUpdateAsync(u => u.SetProperty(s => s.ApproverSeparation, setting));
    }

    private async Task Reconcile(World w, Guid reconcilerId)
    {
        var reconciler = As(w.Org, OrgRoles.Instructor, reconcilerId, out var db);
        await using (db)
        {
            var r = await reconciler.ReconcileAsync(w.SessionId, w.DisagreementId, ReconciliationCauses.All[0].Code, "The transcript shows partial application.", null, 70, null);
            Assert.That(r.Ok, Is.True, r.Message);
        }
    }

    private async Task<GradingResult> Approve(World w, string role, Guid user)
    {
        var approver = As(w.Org, role, user, out var db);
        await using (db) return await approver.ApproveAsync(w.SessionId, null, null);
    }

    private static async Task<GradeStatus> Status(Guid sessionId)
    {
        await using var db = SystemDb();
        return await db.FinalGrades.AsNoTracking().Where(g => g.SessionId == sessionId).Select(g => g.Status).SingleAsync();
    }

    private static async Task<int> Audited(Guid sessionId, string action, Guid actor)
    {
        await using var db = SystemDb();
        return await db.AuditEvents.AsNoTracking().CountAsync(a => a.SessionId == sessionId && a.Action == action && a.ActorUserId == actor);
    }

    [Test]
    public async Task Automatic_TheReconcilerCannotApprove_WhileAnAdminCan_AndTheAdminDoes()
    {
        var w = await NeedsReconciliation();
        var reconcilerId = await Seed.MemberAsync(Services, w.Org, OrgRoles.Instructor);
        var adminId = await Seed.MemberAsync(Services, w.Org, OrgRoles.Admin);
        await Reconcile(w, reconcilerId);
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.AwaitingFinalApproval));

        var view = As(w.Org, OrgRoles.Instructor, reconcilerId, out var vdb);
        await using (vdb)
        {
            var ws = await view.WorkspaceAsync(w.SessionId);
            Assert.That(ws!.ReconcilerApproval.Applies, Is.True);
            Assert.That(ws.ReconcilerBlocksViewerApproval, Is.True);
        }

        var refused = await Approve(w, OrgRoles.Instructor, reconcilerId);
        Assert.That(refused.Ok, Is.False);
        Assert.That(refused.Message, Does.StartWith("Refused:"), "the endpoints answer 409 for this");
        Assert.That(refused.Message, Does.Contain("reconciled"));
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.AwaitingFinalApproval), "nothing moved");
        Assert.That(await Audited(w.SessionId, ApproverSeparation.RefusedAction, reconcilerId), Is.EqualTo(1));

        var approved = await Approve(w, OrgRoles.Admin, adminId);
        Assert.That(approved.Ok, Is.True, approved.Message);
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.Finalized));
        Assert.That(await Audited(w.SessionId, ApproverSeparation.WaivedAction, adminId), Is.Zero, "the admin reconciled nothing");
    }

    [Test]
    public async Task Automatic_ATwoPersonTeam_LetsTheReconcilerApprove_AndAuditsIt()
    {
        var w = await NeedsReconciliation();
        var reconcilerId = await Seed.MemberAsync(Services, w.Org, OrgRoles.Instructor);   // the only member besides the scorer
        await Reconcile(w, reconcilerId);

        var approved = await Approve(w, OrgRoles.Instructor, reconcilerId);
        Assert.That(approved.Ok, Is.True, approved.Message);
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.Finalized));
        Assert.That(await Audited(w.SessionId, ApproverSeparation.WaivedAction, reconcilerId), Is.EqualTo(1));
        Assert.That(await Audited(w.SessionId, ApproverSeparation.RefusedAction, reconcilerId), Is.Zero);
    }

    [Test]
    public async Task Always_RefusesTheReconciler_EvenWithNoOneElseToApprove()
    {
        var w = await NeedsReconciliation();
        await Set(w.Org, ApproverSeparationSetting.Always);
        var reconcilerId = await Seed.MemberAsync(Services, w.Org, OrgRoles.Instructor);
        await Reconcile(w, reconcilerId);

        var refused = await Approve(w, OrgRoles.Instructor, reconcilerId);
        Assert.That(refused.Ok, Is.False);
        Assert.That(await Status(w.SessionId), Is.EqualTo(GradeStatus.AwaitingFinalApproval));
        Assert.That(await Audited(w.SessionId, ApproverSeparation.RefusedAction, reconcilerId), Is.EqualTo(1));
        Assert.That(await Audited(w.SessionId, ApproverSeparation.WaivedAction, reconcilerId), Is.Zero);
    }

    [Test]
    public async Task Off_LetsTheReconcilerApprove_WithAnAuditedWaiver()
    {
        var w = await NeedsReconciliation();
        await Set(w.Org, ApproverSeparationSetting.Off);
        var reconcilerId = await Seed.MemberAsync(Services, w.Org, OrgRoles.Instructor);
        await Seed.MemberAsync(Services, w.Org, OrgRoles.Admin);   // someone else could approve, but the organization chose not to require it
        await Reconcile(w, reconcilerId);

        var approved = await Approve(w, OrgRoles.Instructor, reconcilerId);
        Assert.That(approved.Ok, Is.True, approved.Message);
        Assert.That(await Audited(w.SessionId, ApproverSeparation.WaivedAction, reconcilerId), Is.EqualTo(1));
    }
}
