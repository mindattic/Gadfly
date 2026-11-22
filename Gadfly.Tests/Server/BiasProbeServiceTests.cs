using Gadfly.Server.Grading;
using Gadfly.Server.Tenancy;
using Gadfly.Sources.Grading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Bias probes (GAD-T432) through the real service on LocalDB. Nothing here ever runs a probe against the panel: the tests cover
/// the cost screen (who may see it, which probes it counts), the refusals before anything is queued, the human probe's checks,
/// and the blind rotation of answer forms between markers.
/// </summary>
[TestFixture]
public class BiasProbeServiceTests : ServerTest
{
    private static async Task<T> Probes<T>(Guid org, string role, Func<BiasProbeService, Task<T>> act, Guid? user = null)
    {
        await using var scope = Scope(Member(org, user ?? Guid.NewGuid(), role));
        return await act(scope.ServiceProvider.GetRequiredService<BiasProbeService>());
    }

    private static async Task<int> Count(Guid org, string action, Guid? actor = null)
    {
        await using var db = SystemDb();
        return await db.AuditEvents.AsNoTracking().CountAsync(a => a.OrganizationId == org && a.Action == action && (actor == null || a.ActorUserId == actor));
    }

    // ------------------------------------------------------------------ the cost screen

    [Test]
    public async Task ThePlan_IsShownToThoseWhoApproveOrReadAnalytics_Only()
    {
        var w = await Seed.ExamAsync();
        Assert.That(await Probes(w.Org, OrgRoles.Examiner, s => s.PlanAsync(w.ExamId)), Is.Null);
        Assert.That(await Probes(w.Org, OrgRoles.Student, s => s.PlanAsync(w.ExamId)), Is.Null);
        Assert.That(await Probes(w.Org, OrgRoles.Instructor, s => s.PlanAsync(w.ExamId)), Is.Not.Null);
        Assert.That(await Probes(w.Org, OrgRoles.Owner, s => s.PlanAsync(w.ExamId)), Is.Not.Null);
    }

    [Test]
    public async Task AnExamOfAnotherOrganization_HasNoPlan()
    {
        var w = await Seed.ExamAsync();
        var other = await Seed.OrganizationAsync();
        Assert.That(await Probes(other, OrgRoles.Owner, s => s.PlanAsync(w.ExamId)), Is.Null);
    }

    [Test]
    public async Task ThePlan_KeepsOnlyKnownProbes_OnceEach()
    {
        var w = await Seed.ExamAsync();
        var plan = await Probes(w.Org, OrgRoles.Owner, s => s.PlanAsync(w.ExamId, 10, [PanelBiasCheck.Padded, "not-a-probe", PanelBiasCheck.Padded]));
        Assert.That(plan!.Probes, Is.EqualTo(new[] { PanelBiasCheck.Padded }));
        var all = await Probes(w.Org, OrgRoles.Owner, s => s.PlanAsync(w.ExamId));
        Assert.That(all!.Probes, Is.EquivalentTo(PanelBiasCheck.AllProbes));
    }

    [Test]
    public async Task AnExamWithNothingGraded_HasAnEmptyPlanCostingNothing()
    {
        var w = await Seed.ExamAsync();
        var plan = await Probes(w.Org, OrgRoles.Owner, s => s.PlanAsync(w.ExamId));
        Assert.That(plan!.Items, Is.Empty);
        Assert.That(plan.CallsPerMember, Is.Zero);
        Assert.That(plan.EstimatedUsd, Is.Zero);
    }

    // ------------------------------------------------------------------ starting (refusals only: no run is ever started here)

    [Test]
    public async Task StartingNeedsApproveGradingPolicyChange_AndARefusalIsAudited()
    {
        var w = await Seed.ExamAsync();
        var instructor = Guid.NewGuid();
        var r = await Probes(w.Org, OrgRoles.Instructor, s => s.StartAsync(w.ExamId, 10, null, 100m), instructor);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("ApproveGradingPolicyChange"));
        Assert.That(await Count(w.Org, "access.denied", instructor), Is.EqualTo(1));
        Assert.That(await Count(w.Org, BiasProbeService.PanelRequested), Is.Zero);
    }

    [Test]
    public async Task StartingWithNothingToProbe_IsRefused_AndQueuesNothing()
    {
        var w = await Seed.ExamAsync();
        var r = await Probes(w.Org, OrgRoles.Owner, s => s.StartAsync(w.ExamId, 10, null, 100m));
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("nothing to probe"));
        Assert.That(await Count(w.Org, BiasProbeService.PanelRequested), Is.Zero);
        await using var db = SystemDb();
        Assert.That(await db.Jobs.AsNoTracking().CountAsync(j => j.OrganizationId == w.Org && j.Kind == BiasProbeJob.JobKind), Is.Zero);
        Assert.That(Panel.Calls, Is.Empty, "the panel is never called while planning or refusing");
    }

    [Test]
    public async Task StartingOnAnUnknownExam_IsRefused()
    {
        var w = await Seed.ExamAsync();
        var r = await Probes(w.Org, OrgRoles.Owner, s => s.StartAsync(Guid.NewGuid(), 10, null, 100m));
        Assert.That(r.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task Runs_AreEmptyBeforeAnyRun_AndHiddenFromAStudent()
    {
        var w = await Seed.ExamAsync();
        Assert.That(await Probes(w.Org, OrgRoles.Owner, s => s.RunsAsync(w.ExamId)), Is.Empty);
        Assert.That(await Probes(w.Org, OrgRoles.Student, s => s.RunsAsync(w.ExamId)), Is.Null);
    }

    // ------------------------------------------------------------------ human probes

    [Test]
    public async Task AHumanProbe_NeedsThreeGradedAnswers()
    {
        var w = await Seed.ExamAsync();
        var r = await Probes(w.Org, OrgRoles.Owner, s => s.CreateHumanProbeAsync(w.ExamId, 10));
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Message, Does.Contain("at least three answers"));
        Assert.That(await Count(w.Org, BiasProbeService.HumanCreated), Is.Zero);
    }

    [Test]
    public async Task CreatingAHumanProbe_NeedsApproveGradingPolicyChange()
    {
        var w = await Seed.ExamAsync();
        var examiner = Guid.NewGuid();
        var r = await Probes(w.Org, OrgRoles.Examiner, s => s.CreateHumanProbeAsync(w.ExamId, 10), examiner);
        Assert.That(r.Ok, Is.False);
        Assert.That(await Count(w.Org, "access.denied", examiner), Is.EqualTo(1));
    }

    [Test]
    public async Task MarkingAHumanProbe_ChecksRoleLevelAndItem()
    {
        var w = await Seed.ExamAsync();
        var student = Guid.NewGuid();
        var refused = await Probes(w.Org, OrgRoles.Student, s => s.MarkHumanAsync(w.ExamId, Guid.NewGuid(), 0, 3), student);
        Assert.That(refused.Message, Does.Contain("does not include grading"));
        Assert.That(await Count(w.Org, "access.denied", student), Is.EqualTo(1));

        var badLevel = await Probes(w.Org, OrgRoles.Examiner, s => s.MarkHumanAsync(w.ExamId, Guid.NewGuid(), 0, 7));
        Assert.That(badLevel.Message, Does.Contain("Levels run from 0 to 5"));
        var unknown = await Probes(w.Org, OrgRoles.Examiner, s => s.MarkHumanAsync(w.ExamId, Guid.NewGuid(), 0, 3));
        Assert.That(unknown.Message, Does.Contain("not found"));
        Assert.That(await Count(w.Org, BiasProbeService.HumanMarked), Is.Zero);
    }

    [Test]
    public async Task AHumanProbeSheet_IsRefusedToNonMarkers_AndForUnknownProbes()
    {
        var w = await Seed.ExamAsync();
        var (items, refusal) = await Probes(w.Org, OrgRoles.Student, s => s.HumanSheetAsync(w.ExamId, Guid.NewGuid()));
        Assert.That(items, Is.Null);
        Assert.That(refusal, Does.Contain("does not include grading"));
        var (none, notFound) = await Probes(w.Org, OrgRoles.Examiner, s => s.HumanSheetAsync(w.ExamId, Guid.NewGuid()));
        Assert.That(none, Is.Null);
        Assert.That(notFound, Does.Contain("not found"));
    }

    // ------------------------------------------------------------------ blind rotation (no database)

    [Test]
    public void EachMarker_SeesOneFormPerAnswer_Deterministically_AndEveryFormIsUsed()
    {
        var run = Guid.NewGuid();
        var marker = Guid.NewGuid();
        var forms = Enumerable.Range(0, 9).Select(i => HumanProbes.VariantFor(marker, run, i)).ToList();
        Assert.That(forms, Is.EqualTo(Enumerable.Range(0, 9).Select(i => HumanProbes.VariantFor(marker, run, i)).ToList()), "the same marker always sees the same form");
        Assert.That(forms.Distinct(), Is.EquivalentTo(HumanProbe.Variants), "consecutive items rotate through every form");
        Assert.That(forms.All(HumanProbe.Variants.Contains), Is.True);
    }

    [Test]
    public void DifferentMarkers_AreSpreadAcrossTheForms()
    {
        var run = Guid.NewGuid();
        var forms = Enumerable.Range(0, 60).Select(_ => HumanProbes.VariantFor(Guid.NewGuid(), run, 0)).Distinct().ToList();
        Assert.That(forms, Has.Count.EqualTo(HumanProbe.Variants.Count));
    }

    [Test]
    public void Labels_ArePlainWords()
    {
        Assert.That(HumanProbes.Label(HumanProbes.Original), Is.EqualTo("Original answers"));
        Assert.That(HumanProbes.Label(PanelBiasCheck.Padded), Does.Contain("Padded"));
        Assert.That(HumanProbes.Label(PanelBiasCheck.Disfluent), Does.Contain("Less fluent"));
        Assert.That(HumanProbes.Label("other"), Is.EqualTo("other"));
    }
}
