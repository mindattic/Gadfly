using System.Text.Json;
using System.Text.Json.Serialization;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Tenancy;
using Gadfly.Sources.Grading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using GradingSnapshot = Gadfly.Server.Grading.PolicySnapshot;

namespace Gadfly.Tests.Server;

/// <summary>
/// Per-exam panel calibration (GAD-T270) through the real service on LocalDB: who may record one, what is accepted, mock grades
/// never accepting a panel, and expiry when the panel's models (GAD-T429) or the grading prompt version (GAD-T469) change;
/// plus the calibration gate on the panel's weight (GAD-T427) and the wording shown to graders.
/// </summary>
[TestFixture]
public class PanelCalibrationServiceTests : ServerTest
{
    private static readonly JsonSerializerOptions StoredJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly IReadOnlyDictionary<string, string> Models = new Dictionary<string, string> { ["alpha"] = "alpha-1", ["beta"] = "beta-2" };

    /// <summary>36 answers on which the panel and both markers agree exactly, across every level: accepted.</summary>
    private static List<CalibrationItem> Agreeing(int count = 36) =>
        Enumerable.Range(0, count).Select(i => new CalibrationItem($"item-{i}", i % 6, i % 6, i % 6)).ToList();

    private static async Task<T> Calibration<T>(Guid org, string role, Func<PanelCalibrationService, Task<T>> act, Guid? user = null)
    {
        await using var scope = Scope(Member(org, user ?? Guid.NewGuid(), role));
        return await act(scope.ServiceProvider.GetRequiredService<PanelCalibrationService>());
    }

    private static Task<(GradingResult Result, CalibrationReport? Report)> Record(Seed.ExamWorld w, IReadOnlyList<CalibrationItem> items, bool mock = false,
        string role = OrgRoles.Owner, Guid? user = null) =>
        Calibration(w.Org, role, s => s.EvaluateAsync(w.ExamId, "Tour panel", items, default, PanelCalibrationService.SourceApi, mock), user);

    /// <summary>A calibration event written as the service writes it, with chosen models and grading prompt version.</summary>
    private static async Task Store(Seed.ExamWorld w, IReadOnlyDictionary<string, string>? models, int? promptVersion, DateTimeOffset at)
    {
        await using var db = OrgDb(w.Org);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = w.Org, Action = PanelCalibrationService.EvaluatedAction, EntityType = PanelCalibrationService.EntityType, EntityId = w.ExamId.ToString(),
            AfterJson = JsonSerializer.Serialize(new { panel = "alpha+beta", report = PanelCalibration.Evaluate(Agreeing()), models, source = "api", gradingPromptVersion = promptVersion },
                StoredJson),
            At = at,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<PanelCalibrationStatus?> Latest(Seed.ExamWorld w, IReadOnlyDictionary<string, string>? current, int recheckDays = 180)
    {
        await using var db = OrgDb(w.Org);
        return await PanelCalibrationService.LatestAsync(db, w.Org, w.ExamId, current, DateTimeOffset.UtcNow, recheckDays, CancellationToken.None);
    }

    // ------------------------------------------------------------------ recording

    [Test]
    public async Task AnAgreeingSet_IsAccepted_AndRecordedWithTheGradingPromptVersion()
    {
        var w = await Seed.ExamAsync();
        var (result, report) = await Record(w, Agreeing());
        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(report!.Accepted, Is.True);
        Assert.That(result.Message, Does.Contain($"grading prompts version {AiObjectiveGrader.PromptVersion}"));

        var status = await Calibration(w.Org, OrgRoles.Examiner, s => s.StatusAsync(w.ExamId));
        Assert.That(status, Is.Not.Null);
        Assert.That(status!.Accepted, Is.True);
        Assert.That(status.Effective, Is.True);
        Assert.That(status.Panel, Is.EqualTo("Tour panel"));
        Assert.That(status.GradingPromptVersion, Is.EqualTo(AiObjectiveGrader.PromptVersion));
        Assert.That(status.Source, Is.EqualTo(PanelCalibrationService.SourceApi));
    }

    [Test]
    public async Task RecordingNeedsApproveGradingPolicyChange_AndARefusalIsAudited()
    {
        var w = await Seed.ExamAsync();
        var instructor = Guid.NewGuid();
        var (result, report) = await Record(w, Agreeing(), role: OrgRoles.Instructor, user: instructor);
        Assert.That(result.Ok, Is.False);
        Assert.That(report, Is.Null);
        Assert.That(result.Message, Does.StartWith("Your role"));
        await using var db = SystemDb();
        Assert.That(await db.AuditEvents.AsNoTracking().CountAsync(a => a.Action == "access.denied" && a.ActorUserId == instructor), Is.EqualTo(1));
        Assert.That(await db.AuditEvents.AsNoTracking().CountAsync(a => a.Action == PanelCalibrationService.EvaluatedAction && a.EntityId == w.ExamId.ToString()), Is.Zero);
    }

    [Test]
    public async Task ASmallSet_IsRecordedButNotAccepted_AndReplacesAnEarlierAcceptance()
    {
        var w = await Seed.ExamAsync();
        Assert.That((await Record(w, Agreeing())).Report!.Accepted, Is.True);
        Clock.Now = Clock.Now.AddSeconds(1);   // the latest calibration is the one in force
        var (result, report) = await Record(w, Agreeing(10));
        Assert.That(result.Ok, Is.False);
        Assert.That(report, Is.Not.Null, "a failing calibration is still recorded");
        Assert.That(report!.Failures, Has.Some.Contains("at least 30"));
        var status = await Calibration(w.Org, OrgRoles.Owner, s => s.StatusAsync(w.ExamId));
        Assert.That(status!.Accepted, Is.False);
        Assert.That(status.Effective, Is.False);
    }

    [Test]
    public async Task MockGrades_NeverAcceptAPanel()
    {
        var w = await Seed.ExamAsync();
        var (result, report) = await Record(w, Agreeing(), mock: true);
        Assert.That(result.Ok, Is.False);
        Assert.That(report!.Accepted, Is.False);
        Assert.That(report.Failures, Has.Some.Contains("mock panel"));
    }

    [Test]
    public async Task UnusableSets_AreRefusedWithoutRecording()
    {
        var w = await Seed.ExamAsync();
        var duplicate = Agreeing();
        duplicate[1] = duplicate[1] with { ItemId = duplicate[0].ItemId };
        var (dup, dupReport) = await Record(w, duplicate);
        Assert.That(dup.Message, Does.Contain("distinct itemId"));
        Assert.That(dupReport, Is.Null);

        var outOfRange = Agreeing();
        outOfRange[0] = outOfRange[0] with { PanelLevel = 9 };
        Assert.That((await Record(w, outOfRange)).Result.Message, Does.Contain("outside"));

        var (unknown, _) = await Calibration(w.Org, OrgRoles.Owner, s => s.EvaluateAsync(Guid.NewGuid(), null, Agreeing()));
        Assert.That(unknown.Message, Does.Contain("not found"));
        Assert.That(await Calibration(w.Org, OrgRoles.Owner, s => s.StatusAsync(w.ExamId)), Is.Null, "nothing was recorded");
    }

    [Test]
    public async Task Status_IsHiddenFromAStudent_AndNullForAnExamOfAnotherOrganization()
    {
        var w = await Seed.ExamAsync();
        await Record(w, Agreeing());
        Assert.That(await Calibration(w.Org, OrgRoles.Student, s => s.StatusAsync(w.ExamId)), Is.Null);
        var other = await Seed.OrganizationAsync();
        Assert.That(await Calibration(other, OrgRoles.Owner, s => s.StatusAsync(w.ExamId)), Is.Null);
    }

    // ------------------------------------------------------------------ expiry (GAD-T429, GAD-T469)

    [Test]
    public async Task SameModelsAndPromptVersion_StaysCurrent()
    {
        var w = await Seed.ExamAsync();
        await Store(w, Models, AiObjectiveGrader.PromptVersion, DateTimeOffset.UtcNow);
        var s = await Latest(w, Models);
        Assert.That(s!.Expired, Is.False);
        Assert.That(s.Effective, Is.True);
        Assert.That(s.ExpiryReasons, Is.Empty);
    }

    [Test]
    public async Task AnotherGradingPromptVersion_ExpiresTheCalibration()
    {
        var w = await Seed.ExamAsync();
        await Store(w, Models, AiObjectiveGrader.PromptVersion + 1, DateTimeOffset.UtcNow);
        var s = await Latest(w, Models);
        Assert.That(s!.Expired, Is.True);
        Assert.That(s.Effective, Is.False);
        Assert.That(s.GradingPromptVersion, Is.EqualTo(AiObjectiveGrader.PromptVersion + 1));
        Assert.That(s.ExpiryReasons, Has.Some.Contains("grading prompts changed"));
        Assert.That(PanelCalibrationWording.Describe(s.ToNote()), Does.Contain("expired"));
    }

    [Test]
    public async Task ACalibrationWithoutAPromptVersion_CountsAsVersionOne()
    {
        var w = await Seed.ExamAsync();
        await Store(w, Models, null, DateTimeOffset.UtcNow);
        var s = await Latest(w, Models);
        Assert.That(s!.GradingPromptVersion, Is.EqualTo(1));
        Assert.That(s.Expired, Is.EqualTo(AiObjectiveGrader.PromptVersion != 1));
    }

    [Test]
    public async Task AChangedModel_ExpiresTheCalibration()
    {
        var w = await Seed.ExamAsync();
        await Store(w, Models, AiObjectiveGrader.PromptVersion, DateTimeOffset.UtcNow);
        var s = await Latest(w, new Dictionary<string, string> { ["alpha"] = "alpha-1", ["beta"] = "beta-3" });
        Assert.That(s!.Expired, Is.True);
        Assert.That(s.ExpiryReasons, Is.Not.Empty);
    }

    [Test]
    public async Task ACalibrationWithoutModelIds_CannotShowItCoversThePanel()
    {
        var w = await Seed.ExamAsync();
        await Store(w, null, AiObjectiveGrader.PromptVersion, DateTimeOffset.UtcNow);
        var s = await Latest(w, Models);
        Assert.That(s!.Expired, Is.True);
        Assert.That(s.ExpiryReasons, Has.Some.Contains("before Gadfly kept the panel's model ids"));
    }

    [Test]
    public async Task UnknownCurrentModels_SkipTheExpiryCheck()
    {
        var w = await Seed.ExamAsync();
        await Store(w, null, AiObjectiveGrader.PromptVersion + 5, DateTimeOffset.UtcNow);
        Assert.That((await Latest(w, null))!.Expired, Is.False);
    }

    [Test]
    public async Task AnOldCalibration_AsksForARecheck_ButStaysInForce()
    {
        var w = await Seed.ExamAsync();
        await Store(w, Models, AiObjectiveGrader.PromptVersion, DateTimeOffset.UtcNow.AddDays(-200));
        var s = await Latest(w, Models, recheckDays: 180);
        Assert.That(s!.RecheckDue, Is.True);
        Assert.That(s.Effective, Is.True);
    }

    [Test]
    public async Task NoCalibration_IsNull()
    {
        var w = await Seed.ExamAsync();
        Assert.That(await Latest(w, Models), Is.Null);
    }

    // ------------------------------------------------------------------ the gate and the wording (no database)

    [Test]
    public void TheGate_IsOffByDefault()
    {
        var policy = new GradingSnapshot { Rules = new GradingPolicyRules() };
        Assert.That(PanelGovernanceService.ApplyGate(policy, ExamPanelSettings.Default, null), Is.SameAs(policy));
    }

    [Test]
    public void TheGate_WithoutAnEffectiveCalibration_GradesOnTheHumanAlone()
    {
        var policy = new GradingSnapshot { Rules = new GradingPolicyRules() };
        var gated = PanelGovernanceService.ApplyGate(policy, new ExamPanelSettings(GateUntilCalibrated: true), null);
        Assert.That((gated.Rules.AiWeight, gated.Rules.HumanWeight), Is.EqualTo((0m, 1m)));
        Assert.That(gated.Gate!.Applied, Is.True);
        Assert.That(gated.Gate.Reason, Does.Contain("no calibration is recorded"));
    }

    [Test]
    public void TheGate_WithAnEffectiveCalibration_KeepsThePolicysWeights()
    {
        var policy = new GradingSnapshot { Rules = new GradingPolicyRules() };
        var accepted = new PanelCalibrationStatus(Guid.NewGuid(), "p", true, PanelCalibration.Evaluate(Agreeing()), DateTimeOffset.UtcNow, null);
        var kept = PanelGovernanceService.ApplyGate(policy, new ExamPanelSettings(GateUntilCalibrated: true), accepted);
        Assert.That((kept.Rules.AiWeight, kept.Rules.HumanWeight), Is.EqualTo((0.4m, 0.6m)));
        Assert.That(kept.Gate!.Applied, Is.False);

        var expired = accepted with { Expired = true, ExpiryReasons = ["the grading prompts changed"] };
        var gated = PanelGovernanceService.ApplyGate(policy, new ExamPanelSettings(GateUntilCalibrated: true), expired);
        Assert.That(gated.Rules.AiWeight, Is.EqualTo(0m));
        Assert.That(gated.Gate!.Reason, Does.Contain("expired (the grading prompts changed)"));
    }

    [Test]
    public void TheWording_NeverCallsAGradeRight()
    {
        Assert.That(PanelCalibrationWording.Describe(null), Does.Contain("provisional"));
        var note = new PanelCalibrationNote("p", true, 36, 0.95, 0.96, 0.01, DateTimeOffset.UtcNow);
        Assert.That(PanelCalibrationWording.Describe(note), Does.Contain("not whether this grade is right"));
        Assert.That(PanelCalibrationWording.Describe(note with { Accepted = false }), Does.Contain("Did not meet"));
        Assert.That(PanelCalibrationWording.Describe(note with { Expired = true, ExpiryReasons = ["x"] }), Does.Contain("expired (x)"));
    }
}
