using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Socrates.Mock;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>
/// The models the grading panel would run now (GAD-T429), read from configuration: the selected panel's members (the
/// mock's in mock mode). No provider is called.
/// </summary>
public sealed class PanelModelInventory(IServiceProvider services)
{
    private IPanelModels? Source =>
        services.GetService<SelectedPanel>() as IPanelModels ?? services.GetService<ISocraticVotingClient>() as IPanelModels;

    /// <summary>Provider → model id; null when the host's panel does not say (then nothing is compared).</summary>
    public IReadOnlyDictionary<string, string>? Current => Source?.ConfiguredModels;

    /// <summary>Whether the panel in use is the mock (its grades are never evidence about a real panel).</summary>
    public bool IsMock => (services.GetService<SelectedPanel>() as IPanelInfo ?? services.GetService<ISocraticVotingClient>() as IPanelInfo) is { IsMock: true };

    /// <summary>The model a member runs, or "" when unknown.</summary>
    public string ModelOf(string providerId) => Current?.GetValueOrDefault(providerId) ?? "";
}

/// <summary>
/// An exam's panel settings (GAD-T427, GAD-T428). Kept as append-only audit events (<c>panel.settings.updated</c>, entity
/// <c>GradingPanel</c>, id = exam id; the latest is in force), like the calibration itself, so no schema change was needed
/// and every change is audited by construction.
/// </summary>
/// <param name="GateUntilCalibrated">
/// The panel counts for nothing (AI weight 0, human weight 1) in sessions whose AI grading starts while the exam has no
/// accepted, unexpired calibration (ElenchusAtScale Section 3.8: the thresholds decide whether the panel may be used at all).
/// </param>
/// <param name="DoubleMarking">A second examiner grades the exam's sessions blind to the panel and to the first examiner.</param>
public sealed record ExamPanelSettings(bool GateUntilCalibrated = false, bool DoubleMarking = false)
{
    public static ExamPanelSettings Default { get; } = new();
}

/// <summary>What a session's frozen policy says about the calibration gate (GAD-T427).</summary>
/// <param name="Applied">True when the gate set the panel's weight to 0 for this session.</param>
public sealed record PanelGateNote(bool Applied, string Reason);

/// <summary>One exam on the panel calibration index.</summary>
public sealed record PanelExamRow(Guid ExamId, string Title, int EndedSessions, ExamPanelSettings Settings, PanelCalibrationStatus? Calibration);

/// <summary>Runs panel-governance work from a Blazor component in its own DI scope acting as the circuit's tenant.</summary>
public static class PanelDb
{
    public static async Task<T> RunAsync<T>(IServiceScopeFactory scopes, ITenantContext tenant, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.Of(tenant));
        return await work(scope.ServiceProvider);
    }
}

/// <summary>Reads and changes an exam's <see cref="ExamPanelSettings"/>, and applies the calibration gate to a policy.</summary>
public sealed class PanelGovernanceService(GadflyDbContext db, ITenantContext tenant, TimeProvider clock, PanelModelInventory models, IConfiguration config)
{
    public const string SettingsAction = "panel.settings.updated";

    private GradingActor Actor => new(tenant.UserId, tenant.Role);

    /// <summary>The organization's exams with their calibration and panel settings; null when the caller cannot read transcripts.</summary>
    public async Task<IReadOnlyList<PanelExamRow>?> ExamsAsync(CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || !Actor.Has(Permission.ViewTranscripts)) return null;
        const string notAnAttempt = nameof(Gadfly.Engine.EndReason.PromptSetChanged);
        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && e.AssessmentMode == AssessmentMode.Examination)
            .Select(e => new
            {
                e.Id, e.Title,
                Ended = db.ExamSessions.Count(s => s.ExamId == e.Id && s.OrganizationId == org && s.State == SessionState.Ended && s.EndReason != notAnAttempt),
            })
            .OrderBy(e => e.Title).ToListAsync(ct);
        var now = clock.GetUtcNow();
        var days = PanelCalibrationService.RecheckDays(config);
        var rows = new List<PanelExamRow>();
        foreach (var e in exams)
            rows.Add(new PanelExamRow(e.Id, e.Title, e.Ended, await LatestAsync(db, org, e.Id, ct),
                await PanelCalibrationService.LatestAsync(db, org, e.Id, models.Current, now, days, ct)));
        return rows;
    }

    /// <summary>The exam's settings (defaults when never set), or null when the exam is not visible to the caller.</summary>
    public async Task<ExamPanelSettings?> SettingsAsync(Guid examId, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || !Actor.Has(Permission.ViewTranscripts)) return null;
        if (!await db.Exams.AsNoTracking().AnyAsync(e => e.Id == examId && e.OrganizationId == org, ct)) return null;
        return await LatestAsync(db, org, examId, ct);
    }

    public static async Task<ExamPanelSettings> LatestAsync(GadflyDbContext db, Guid organizationId, Guid examId, CancellationToken ct)
    {
        var id = examId.ToString();
        var json = await db.AuditEvents.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.Action == SettingsAction && a.EntityType == PanelCalibrationService.EntityType && a.EntityId == id)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Select(a => a.AfterJson).FirstOrDefaultAsync(ct);
        return GradingJson.Read<ExamPanelSettings>(json) ?? ExamPanelSettings.Default;
    }

    /// <summary>
    /// Changes the settings. The gate decides how much the panel counts, so it needs <see cref="Permission.ApproveGradingPolicyChange"/>
    /// (the same as recording a calibration); double marking needs <see cref="Permission.ManageAssessments"/>. Sessions already
    /// graded keep the policy frozen when their grading started.
    /// </summary>
    public async Task<GradingResult> SaveAsync(Guid examId, ExamPanelSettings settings, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        if (tenant.OrganizationId is not { } org) return GradingResult.Fail("No active organization.");
        if (!await db.Exams.AsNoTracking().AnyAsync(e => e.Id == examId && e.OrganizationId == org, ct))
            return GradingResult.Fail("That exam was not found in this organization.");
        var before = await LatestAsync(db, org, examId, ct);
        if (before == settings) return GradingResult.Done("Nothing changed.");
        if (before.GateUntilCalibrated != settings.GateUntilCalibrated && !Actor.Has(Permission.ApproveGradingPolicyChange))
        {
            await GradingAudit.DeniedAsync(db, org, Actor, Permission.ApproveGradingPolicyChange, "Changing the calibration gate on the panel's weight", null, now, ct);
            return GradingResult.Fail($"Changing the calibration gate needs the {Permission.ApproveGradingPolicyChange} permission. The attempt was recorded.");
        }
        if (before.DoubleMarking != settings.DoubleMarking && !Actor.Has(Permission.ManageAssessments))
        {
            await GradingAudit.DeniedAsync(db, org, Actor, Permission.ManageAssessments, "Turning double marking on or off", null, now, ct);
            return GradingResult.Fail($"Turning double marking on or off needs the {Permission.ManageAssessments} permission. The attempt was recorded.");
        }
        GradingAudit.Add(db, org, Actor, SettingsAction, PanelCalibrationService.EntityType, examId.ToString(), null, now, before, settings,
            reason: "Panel settings changed; sessions already in grading keep the policy frozen when their grading started.");
        await db.SaveChangesAsync(ct);
        return GradingResult.Done("Saved. It applies to sessions whose AI grading starts from now on.");
    }

    /// <summary>
    /// The policy a session is graded under, with the calibration gate applied (GAD-T427): when the exam gates the panel and its
    /// calibration is missing, not accepted or expired, the AI weight is 0 and the human weight 1. The panel's grade is still
    /// shown to reviewers after the human grades, and a large difference still asks for a second look, but it adds nothing to the mark.
    /// </summary>
    public static PolicySnapshot ApplyGate(PolicySnapshot policy, ExamPanelSettings settings, PanelCalibrationStatus? calibration)
    {
        if (!settings.GateUntilCalibrated) return policy;
        if (calibration is { Effective: true })
            return policy with { Gate = new PanelGateNote(false, "The exam gates the panel on calibration; its calibration is accepted and current, so the policy's weights apply.") };
        var why = calibration is null ? "no calibration is recorded for this exam"
            : calibration.Expired ? $"its calibration expired ({string.Join("; ", calibration.ExpiryReasons)})"
            : "its last calibration did not meet the thresholds";
        return policy with
        {
            Rules = policy.Rules with { AiWeight = 0m, HumanWeight = 1m },
            Gate = new PanelGateNote(true, $"Human-only grading: the exam gives the panel weight only once a calibration is accepted, and {why}."),
        };
    }
}
