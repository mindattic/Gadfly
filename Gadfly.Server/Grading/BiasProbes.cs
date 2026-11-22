using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Sources.Grading;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>One answer a probe run uses: a session's objective.</summary>
public sealed record ProbeItem(Guid SessionId, Guid ObjectiveId);

/// <summary>A member's share of a probe run's estimated cost.</summary>
public sealed record ProbeCostLine(string Provider, string Model, long InputTokens, long OutputTokens, decimal Usd);

/// <summary>What a panel probe run would do and cost, shown before anyone starts it (GAD-T432).</summary>
public sealed record PanelProbePlan(Guid ExamId, IReadOnlyList<ProbeItem> Items, IReadOnlyList<string> Probes, int CallsPerMember, IReadOnlyList<ProbeCostLine> Cost,
    bool IsMock)
{
    public decimal EstimatedUsd => Cost.Sum(c => c.Usd);
}

/// <summary>A panel probe run, as recorded.</summary>
public sealed record PanelProbeRun(Guid RunId, DateTimeOffset RequestedAt, Guid? RequestedBy, int Items, decimal EstimatedUsd, bool IsMock, string State,
    IReadOnlyList<PanelBiasReport> Reports, decimal? MeteredUsd, string? Error, IReadOnlyDictionary<string, string>? Models)
{
    /// <summary>Mean shift per probe, in percent of the objective's maximum (positive = scored higher).</summary>
    public IReadOnlyDictionary<string, double> MeanShiftPercent => Reports.SelectMany(r => r.Probes.Select(p => (p.Probe, Pct: r.MaxScore > 0 ? (double)(p.Shift / r.MaxScore * 100m) : 0)))
        .GroupBy(x => x.Probe).ToDictionary(g => g.Key, g => Math.Round(g.Average(x => x.Pct), 2));
    public IReadOnlyDictionary<string, int> BiasedCount => Reports.SelectMany(r => r.Probes).GroupBy(p => p.Probe).ToDictionary(g => g.Key, g => g.Count(p => p.Biased));
}

/// <summary>A human paraphrase probe (GAD-T432): the same answers in their original, less fluent and padded forms, marked blind.</summary>
public sealed record HumanProbe(Guid RunId, DateTimeOffset CreatedAt, IReadOnlyList<ProbeItem> Items, IReadOnlyList<HumanProbeMark> Marks)
{
    public static readonly IReadOnlyList<string> Variants = [HumanProbes.Original, PanelBiasCheck.Disfluent, PanelBiasCheck.Padded];
}

public sealed record HumanProbeMark(int Item, string Variant, int Level, Guid? By, DateTimeOffset At);

/// <summary>One item of a human probe as a marker sees it: no variant name, no student, no grades.</summary>
public sealed record HumanProbeSheetItem(int Item, string Objective, string? Description, IReadOnlyList<SecondMarkCriterion> Criteria, IReadOnlyList<SecondMarkEvidence> Evidence, int? MyLevel);

/// <summary>A human probe's result: the mean level of each variant and its shift from the original, beside the panel's shift.</summary>
public sealed record HumanProbeVariantResult(string Variant, int Marks, double? MeanLevel, double? ShiftLevels, double? PanelShiftLevels);
public sealed record HumanProbeResult(Guid RunId, int Items, int Markers, int MarksTotal, IReadOnlyList<HumanProbeVariantResult> Variants, IReadOnlyList<string> Findings);

public static class HumanProbes
{
    public const string Original = "original";

    /// <summary>
    /// Which form of item <paramref name="item"/> a marker sees: one per item per marker, rotated by the marker so no one marks two
    /// forms of the same answer and each form is marked by different people (a between-marker design).
    /// </summary>
    public static string VariantFor(Guid marker, Guid runId, int item)
    {
        var h = 17;
        foreach (var b in marker.ToByteArray().Concat(runId.ToByteArray())) h = unchecked(h * 31 + b);
        return HumanProbe.Variants[(int)((uint)(h + item) % (uint)HumanProbe.Variants.Count)];
    }

    public static string Label(string probe) => probe switch
    {
        Original => "Original answers",
        PanelBiasCheck.Disfluent => "Less fluent (same content)",
        PanelBiasCheck.Padded => "Padded with restatement (no new content)",
        PanelBiasCheck.Injection => "Spoken instruction to the grader",
        _ => probe,
    };
}

/// <summary>The "grading.bias-probe" job's payload.</summary>
public sealed record BiasProbePayload(Guid RunId, Guid ExamId, Guid OrganizationId, IReadOnlyList<ProbeItem> Items, IReadOnlyList<string> Probes);

/// <summary>
/// Runnable bias probes for the grading panel and for human graders (GAD-T432; ElenchusAtScale Sections 4.6.1, 5.3.2, 6.8). A
/// panel run grades a sample of an exam's answers again as they are, padded, less fluent and with a spoken instruction to the
/// grader (<see cref="PanelBiasCheck"/>). It runs only when a person starts it, after seeing the estimated provider cost, as a
/// background job; it never runs on a schedule. A human probe puts the original and the less fluent and padded forms of the
/// same answers in front of the exam's examiners, each marker seeing one form per answer, blind to which; comparing their
/// shift with the panel's shows how much of a fluency or length effect is the panel's and how much is shared with people
/// (Section 5.3.2, Joughin 2010). Everything is recorded as append-only audit events (entity <c>BiasProbe</c>, id = run id).
/// </summary>
public sealed class BiasProbeService(GadflyDbContext db, ITenantContext tenant, TimeProvider clock, PanelModelInventory models, UsageMeter meter, JobQueue queue)
{
    public const string Entity = "BiasProbe";
    public const string PanelRequested = "panel.bias-probe.requested";
    public const string PanelCompleted = "panel.bias-probe.completed";
    public const string PanelFailed = "panel.bias-probe.failed";
    public const string HumanCreated = "bias.human-probe.created";
    public const string HumanMarked = "bias.human-probe.marked";
    public const int MaxItems = 40;
    /// <summary>Output tokens assumed per grading reply for the estimate (the reply is capped at 2,500).</summary>
    public const int AssumedOutputTokens = 900;

    private GradingActor Actor => new(tenant.UserId, tenant.Role);
    private bool CanRun => Actor.Has(Permission.ApproveGradingPolicyChange);
    private bool CanSee => Actor.Has(Permission.ApproveGradingPolicyChange) || Actor.Has(Permission.ViewAnalytics);
    private bool CanMark => Actor.Has(Permission.EnterHumanScore) || Actor.Has(Permission.ActAsSecondReviewer);

    private sealed record StoredRequest(Guid ExamId, IReadOnlyList<ProbeItem> Items, IReadOnlyList<string> Probes, decimal EstimatedUsd, bool IsMock,
        IReadOnlyDictionary<string, string>? Models);
    private sealed record StoredResult(IReadOnlyList<PanelBiasReport> Reports, decimal MeteredUsd);
    private sealed record StoredHuman(Guid ExamId, IReadOnlyList<ProbeItem> Items);
    private sealed record StoredMark(int Item, string Variant, int Level);

    // ------------------------------------------------------------------ panel probes

    /// <summary>The run a person would start: up to <paramref name="maxItems"/> recently graded answers, and its estimated cost.</summary>
    public async Task<PanelProbePlan?> PlanAsync(Guid examId, int maxItems = 10, IReadOnlyList<string>? probes = null, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || !CanSee) return null;
        if (!await db.Exams.AsNoTracking().AnyAsync(e => e.Id == examId && e.OrganizationId == org, ct)) return null;
        var chosen = (probes is { Count: > 0 } ? probes : PanelBiasCheck.AllProbes).Where(PanelBiasCheck.AllProbes.Contains).Distinct().ToList();
        var inputs = await SampleAsync(org, examId, Math.Clamp(maxItems, 1, MaxItems), ct);
        var members = models.Current ?? new Dictionary<string, string>();
        var mock = models.IsMock;
        var promptChars = inputs.Sum(i => (long)PanelBiasCheck.PromptCharacters(i.Input, chosen));
        var callsPerMember = inputs.Count * PanelBiasCheck.CallsPerObjective(chosen);
        var cost = members.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase).Select(m =>
        {
            var input = PanelUsage.Tokens(promptChars);
            var output = (long)callsPerMember * AssumedOutputTokens;
            var price = meter.PriceOf(m.Key, m.Value);
            var usd = mock ? 0m : Math.Round((input * price.InputPerMillion + output * price.OutputPerMillion) / 1_000_000m, 4);
            return new ProbeCostLine(m.Key, m.Value, input, output, usd);
        }).ToList();
        return new PanelProbePlan(examId, inputs.Select(i => i.Item).ToList(), chosen, callsPerMember, cost, mock);
    }

    /// <summary>
    /// Starts a panel probe run as a background job. <paramref name="acceptedEstimateUsd"/> is the estimate the person was shown;
    /// the run is refused if the estimate has grown since (re-plan and confirm again). Needs ApproveGradingPolicyChange.
    /// </summary>
    public async Task<GradingResult> StartAsync(Guid examId, int maxItems, IReadOnlyList<string>? probes, decimal acceptedEstimateUsd, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        if (tenant.OrganizationId is not { } org) return GradingResult.Fail("No active organization.");
        if (!CanRun)
        {
            await GradingAudit.DeniedAsync(db, org, Actor, Permission.ApproveGradingPolicyChange, "Starting a panel bias probe", null, now, ct);
            return GradingResult.Fail($"Starting a probe run needs the {Permission.ApproveGradingPolicyChange} permission. The attempt was recorded.");
        }
        var plan = await PlanAsync(examId, maxItems, probes, ct);
        if (plan is null) return GradingResult.Fail("That exam was not found in this organization.");
        if (plan.Items.Count == 0) return GradingResult.Fail("No answer of this exam has been graded by the panel with usable evidence yet, so there is nothing to probe.");
        if (plan.EstimatedUsd > acceptedEstimateUsd + 0.005m)
            return GradingResult.Fail($"The estimate is now ${plan.EstimatedUsd:0.00}, more than the ${acceptedEstimateUsd:0.00} you accepted. Review it and start again.");
        if (await db.Jobs.AsNoTracking().AnyAsync(j => j.Kind == BiasProbeJob.JobKind && j.OrganizationId == org && (j.State == JobState.Queued || j.State == JobState.Running), ct))
            return GradingResult.Fail("A probe run is already queued or running for this organization; wait for it to finish.");
        var runId = Guid.NewGuid();
        GradingAudit.Add(db, org, Actor, PanelRequested, Entity, runId.ToString(), null, now,
            after: new StoredRequest(examId, plan.Items, plan.Probes, plan.EstimatedUsd, plan.IsMock, models.Current),
            reason: $"Panel bias probe on {plan.Items.Count} answer(s), {plan.CallsPerMember} grading call(s) per member, estimated ${plan.EstimatedUsd:0.00}{(plan.IsMock ? " (mock panel: no provider cost)" : "")}.");
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(BiasProbeJob.JobKind, new BiasProbePayload(runId, examId, org, plan.Items, plan.Probes), org, maxAttempts: 1, ct: ct);
        return GradingResult.Done(plan.IsMock
            ? "Probe run started on the mock panel (no provider cost). Its results show the workflow, not a real panel."
            : $"Probe run started: estimated ${plan.EstimatedUsd:0.00}. The results appear here when it finishes.");
    }

    /// <summary>An exam's panel probe runs, newest first.</summary>
    public async Task<IReadOnlyList<PanelProbeRun>?> RunsAsync(Guid examId, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || !CanSee) return null;
        var rows = await db.AuditEvents.AsNoTracking()
            .Where(a => a.OrganizationId == org && a.EntityType == Entity && (a.Action == PanelRequested || a.Action == PanelCompleted || a.Action == PanelFailed))
            .OrderBy(a => a.At).ThenBy(a => a.Id).Select(a => new { a.Action, a.EntityId, a.AfterJson, a.Reason, a.At, a.ActorUserId }).ToListAsync(ct);
        var runs = new List<PanelProbeRun>();
        foreach (var g in rows.GroupBy(r => r.EntityId))
        {
            var req = g.FirstOrDefault(r => r.Action == PanelRequested);
            var stored = GradingJson.Read<StoredRequest>(req?.AfterJson);
            if (req is null || stored is null || stored.ExamId != examId || !Guid.TryParse(g.Key, out var runId)) continue;
            var done = g.LastOrDefault(r => r.Action == PanelCompleted);
            var failed = g.LastOrDefault(r => r.Action == PanelFailed);
            var result = GradingJson.Read<StoredResult>(done?.AfterJson);
            runs.Add(new PanelProbeRun(runId, req.At, req.ActorUserId, stored.Items.Count, stored.EstimatedUsd, stored.IsMock,
                done is not null ? "finished" : failed is not null ? "failed" : "queued or running", result?.Reports ?? [], result?.MeteredUsd, failed?.Reason, stored.Models));
        }
        return runs.OrderByDescending(r => r.RequestedAt).ToList();
    }

    /// <summary>When the organization last finished a panel probe run on any exam (for the re-run prompt, GAD-T429).</summary>
    public static Task<DateTimeOffset?> LastRunAsync(GadflyDbContext db, Guid organizationId, CancellationToken ct) =>
        db.AuditEvents.AsNoTracking().Where(a => a.OrganizationId == organizationId && a.Action == PanelCompleted && a.EntityType == Entity)
            .MaxAsync(a => (DateTimeOffset?)a.At, ct);

    // ------------------------------------------------------------------ human probes

    /// <summary>Creates a human paraphrase probe over up to <paramref name="maxItems"/> graded answers. Needs ApproveGradingPolicyChange.</summary>
    public async Task<GradingResult> CreateHumanProbeAsync(Guid examId, int maxItems, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        if (tenant.OrganizationId is not { } org) return GradingResult.Fail("No active organization.");
        if (!CanRun)
        {
            await GradingAudit.DeniedAsync(db, org, Actor, Permission.ApproveGradingPolicyChange, "Creating a human paraphrase probe", null, now, ct);
            return GradingResult.Fail($"Creating a human probe needs the {Permission.ApproveGradingPolicyChange} permission. The attempt was recorded.");
        }
        if (!await db.Exams.AsNoTracking().AnyAsync(e => e.Id == examId && e.OrganizationId == org, ct)) return GradingResult.Fail("That exam was not found in this organization.");
        var inputs = await SampleAsync(org, examId, Math.Clamp(maxItems, 3, MaxItems), ct);
        if (inputs.Count < 3) return GradingResult.Fail("A human probe needs at least three answers the panel has graded with usable evidence.");
        var runId = Guid.NewGuid();
        GradingAudit.Add(db, org, Actor, HumanCreated, Entity, runId.ToString(), null, now, after: new StoredHuman(examId, inputs.Select(i => i.Item).ToList()),
            reason: $"Human paraphrase probe on {inputs.Count} answers: each examiner marks one form (original, less fluent or padded) of each, blind to which.");
        await db.SaveChangesAsync(ct);
        return GradingResult.Done($"Human probe created with {inputs.Count} answers. Examiners mark it from the bias probes page; the more examiners take part, the clearer the result.");
    }

    /// <summary>An exam's human probes, newest first.</summary>
    public async Task<IReadOnlyList<HumanProbe>?> HumanProbesAsync(Guid examId, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || !(CanSee || CanMark)) return null;
        var created = await db.AuditEvents.AsNoTracking().Where(a => a.OrganizationId == org && a.EntityType == Entity && a.Action == HumanCreated)
            .Select(a => new { a.EntityId, a.AfterJson, a.At }).ToListAsync(ct);
        var probes = new List<HumanProbe>();
        foreach (var c in created)
        {
            var stored = GradingJson.Read<StoredHuman>(c.AfterJson);
            if (stored is null || stored.ExamId != examId || !Guid.TryParse(c.EntityId, out var runId)) continue;
            probes.Add(new HumanProbe(runId, c.At, stored.Items, await MarksAsync(org, runId, ct)));
        }
        return probes.OrderByDescending(p => p.CreatedAt).ToList();
    }

    /// <summary>The caller's blind sheet for a human probe: one form of each answer, chosen for this marker.</summary>
    public async Task<(IReadOnlyList<HumanProbeSheetItem>? Items, string? Refusal)> HumanSheetAsync(Guid examId, Guid runId, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || tenant.UserId is not { } me) return (null, "No active organization.");
        if (!CanMark) return (null, "Your role in this organization does not include grading.");
        var probe = (await HumanProbesAsync(examId, ct))?.FirstOrDefault(p => p.RunId == runId);
        if (probe is null) return (null, "That probe was not found.");
        var exam = await db.Exams.AsNoTracking().FirstAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        var objectives = (await GradingInputs.LoadObjectivesAsync(db, examId, org, ct)).ToDictionary(o => o.Id);
        var policy = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == examId && p.OrganizationId == org, ct);
        var mine = probe.Marks.Where(m => m.By == me).GroupBy(m => m.Item).ToDictionary(g => g.Key, g => g.OrderBy(m => m.At).Last().Level);
        var items = new List<HumanProbeSheetItem>();
        for (var i = 0; i < probe.Items.Count; i++)
        {
            var item = probe.Items[i];
            var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == item.SessionId && s.OrganizationId == org, ct);
            if (session is null || !objectives.TryGetValue(item.ObjectiveId, out var o)) continue;
            var responses = await db.StudentResponses.AsNoTracking().Where(r => r.SessionId == session.Id && r.OrganizationId == org).OrderBy(r => r.Sequence).ToListAsync(ct);
            var input = await GradingInputs.BuildInputAsync(db, session, exam, o, responses, policy?.AllowAssistedEvidence ?? false, ct);
            var variant = HumanProbes.VariantFor(me, runId, i);
            var change = variant == HumanProbes.Original ? (Func<string, string>)(s => s) : PanelBiasCheck.RewriteFor(variant);
            var evidence = AiObjectiveGrader.UsableEvidence(input).Select(e => new SecondMarkEvidence(e.Sequence, e.Question, change(e.VerifiedText))).ToList();
            if (evidence.Count == 0) continue;   // the answers were removed (retention) since the probe was made
            var criteria = o.Criteria.OrderBy(c => c.Ordinal).Select(c => new SecondMarkCriterion(c.Name, c.Dimension.ToString(), c.Description,
                Enum.GetValues<MasteryLevel>().Select(l => new SecondMarkLevel((int)l, MasteryLevels.Label(l),
                    c.Levels.FirstOrDefault(x => x.Level == l)?.Descriptor is { Length: > 0 } d ? d : MasteryLevels.DefaultDescriptor(l))).ToList())).ToList();
            items.Add(new HumanProbeSheetItem(i, o.Title, o.Description, criteria, evidence, mine.TryGetValue(i, out var lv) ? lv : null));
        }
        return (items, null);
    }

    /// <summary>Records the caller's mark on one probe item (the form they were shown is recorded with it, never shown to them).</summary>
    public async Task<GradingResult> MarkHumanAsync(Guid examId, Guid runId, int item, int level, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        if (tenant.OrganizationId is not { } org || tenant.UserId is not { } me) return GradingResult.Fail("No active organization.");
        if (!CanMark)
        {
            await GradingAudit.DeniedAsync(db, org, Actor, Permission.EnterHumanScore, "Marking a human bias probe", null, now, ct);
            return GradingResult.Fail("Your role in this organization does not include grading. The attempt was recorded.");
        }
        if (level is < PanelCalibration.MinLevel or > PanelCalibration.MaxLevel) return GradingResult.Fail("Levels run from 0 to 5.");
        var probe = (await HumanProbesAsync(examId, ct))?.FirstOrDefault(p => p.RunId == runId);
        if (probe is null || item < 0 || item >= probe.Items.Count) return GradingResult.Fail("That probe item was not found.");
        GradingAudit.Add(db, org, Actor, HumanMarked, Entity, runId.ToString(), probe.Items[item].SessionId, now,
            after: new StoredMark(item, HumanProbes.VariantFor(me, runId, item), level), reason: "Blind mark on a human paraphrase probe item; it does not change any grade.");
        await db.SaveChangesAsync(ct);
        return GradingResult.Done("Mark recorded.");
    }

    /// <summary>
    /// A human probe's result: mean level per form and its shift from the original, beside the panel's shift on the same answers
    /// (from the exam's latest finished panel run that covered them).
    /// </summary>
    public async Task<HumanProbeResult?> HumanResultAsync(Guid examId, Guid runId, CancellationToken ct = default)
    {
        if (!CanSee) return null;
        var probe = (await HumanProbesAsync(examId, ct))?.FirstOrDefault(p => p.RunId == runId);
        if (probe is null) return null;
        var latest = probe.Marks.GroupBy(m => (m.Item, m.By)).Select(g => g.OrderBy(m => m.At).Last()).ToList();
        var original = latest.Where(m => m.Variant == HumanProbes.Original).Select(m => (double)m.Level).ToList();
        var panelRun = (await RunsAsync(examId, ct))?.FirstOrDefault(r => r.State == "finished");
        var findings = new List<string>();
        var variants = HumanProbe.Variants.Select(v =>
        {
            var levels = latest.Where(m => m.Variant == v).Select(m => (double)m.Level).ToList();
            double? mean = levels.Count == 0 ? null : Math.Round(levels.Average(), 2);
            double? shift = v == HumanProbes.Original || mean is null || original.Count == 0 ? null : Math.Round(mean.Value - original.Average(), 2);
            double? panelShift = null;
            if (v != HumanProbes.Original && panelRun is not null)
            {
                var shifts = panelRun.Reports.SelectMany(r => r.Probes.Where(p => p.Probe == v).Select(p => r.MaxScore > 0 ? (double)(p.Shift / r.MaxScore * 5m) : 0)).ToList();
                panelShift = shifts.Count == 0 ? null : Math.Round(shifts.Average(), 2);
            }
            return new HumanProbeVariantResult(v, levels.Count, mean, shift, panelShift);
        }).ToList();
        foreach (var v in variants.Where(v => v.Variant != HumanProbes.Original))
        {
            if (v.Marks < 10 || original.Count < 10) { findings.Add($"{HumanProbes.Label(v.Variant)}: {v.Marks} mark(s) against {original.Count} on the originals; at least 10 each are needed to read a shift."); continue; }
            var who = v.ShiftLevels is { } hs && Math.Abs(hs) >= 0.5 ? $"Examiners marked this form {hs:+0.00;-0.00} levels against the originals" : $"Examiners' marks hardly moved ({v.ShiftLevels:+0.00;-0.00} levels)";
            var panel = v.PanelShiftLevels is { } ps ? $"; the panel moved {ps:+0.00;-0.00} levels on the same kind of rewrite" : "; no finished panel probe covers this form yet";
            findings.Add($"{HumanProbes.Label(v.Variant)}: {who}{panel}.");
        }
        return new HumanProbeResult(runId, probe.Items.Count, latest.Select(m => m.By).Distinct().Count(), latest.Count, variants, findings);
    }

    // ------------------------------------------------------------------ helpers

    private sealed record Sampled(ProbeItem Item, ObjectiveGradingInput Input);

    /// <summary>The most recently graded (session, objective) pairs with usable evidence, at most one objective per session first.</summary>
    private async Task<List<Sampled>> SampleAsync(Guid org, Guid examId, int max, CancellationToken ct)
    {
        var exam = await db.Exams.AsNoTracking().FirstAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        var objectives = (await GradingInputs.LoadObjectivesAsync(db, examId, org, ct)).ToDictionary(o => o.Id);
        var sessionIds = await db.ExamSessions.AsNoTracking().Where(s => s.ExamId == examId && s.OrganizationId == org && s.State == SessionState.Ended && s.RedactedAt == null)
            .Select(s => s.Id).ToListAsync(ct);
        var graded = await db.AiObjectiveGrades.AsNoTracking().Where(g => g.OrganizationId == org && sessionIds.Contains(g.SessionId))
            .OrderByDescending(g => g.CreatedAt).Select(g => new { g.SessionId, g.ObjectiveId, g.Level }).Take(max * 6).ToListAsync(ct);
        var policy = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == examId && p.OrganizationId == org, ct);
        var ordered = graded.Where(g => g.Level != MasteryLevel.InsufficientEvidence && objectives.ContainsKey(g.ObjectiveId))
            .GroupBy(g => g.SessionId).SelectMany(g => g.Select((x, i) => (x, i))).OrderBy(t => t.i).Select(t => t.x).ToList();
        var result = new List<Sampled>();
        foreach (var g in ordered)
        {
            if (result.Count >= max) break;
            var session = await db.ExamSessions.AsNoTracking().FirstAsync(s => s.Id == g.SessionId, ct);
            var responses = await db.StudentResponses.AsNoTracking().Where(r => r.SessionId == session.Id && r.OrganizationId == org).OrderBy(r => r.Sequence).ToListAsync(ct);
            var input = await GradingInputs.BuildInputAsync(db, session, exam, objectives[g.ObjectiveId], responses, policy?.AllowAssistedEvidence ?? false, ct);
            if (AiObjectiveGrader.UsableEvidence(input).Count == 0) continue;
            result.Add(new Sampled(new ProbeItem(g.SessionId, g.ObjectiveId), input));
        }
        return result;
    }

    private async Task<List<HumanProbeMark>> MarksAsync(Guid org, Guid runId, CancellationToken ct)
    {
        var id = runId.ToString();
        return (await db.AuditEvents.AsNoTracking().Where(a => a.OrganizationId == org && a.EntityType == Entity && a.Action == HumanMarked && a.EntityId == id)
                .OrderBy(a => a.At).ThenBy(a => a.Id).Select(a => new { a.AfterJson, a.ActorUserId, a.At }).ToListAsync(ct))
            .Select(a => (m: GradingJson.Read<StoredMark>(a.AfterJson), a)).Where(x => x.m is not null)
            .Select(x => new HumanProbeMark(x.m!.Item, x.m.Variant, x.m.Level, x.a.ActorUserId, x.a.At)).ToList();
    }

    /// <summary>The job's input for one item, read fresh (the answers may have changed since the run was planned).</summary>
    internal static async Task<ObjectiveGradingInput?> InputAsync(GadflyDbContext db, Guid org, Guid examId, ProbeItem item, CancellationToken ct)
    {
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == item.SessionId && s.OrganizationId == org, ct);
        var objective = (await GradingInputs.LoadObjectivesAsync(db, examId, org, ct)).FirstOrDefault(o => o.Id == item.ObjectiveId);
        if (exam is null || session is null || objective is null) return null;
        var policy = await db.GradingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ExamId == examId && p.OrganizationId == org, ct);
        var responses = await db.StudentResponses.AsNoTracking().Where(r => r.SessionId == session.Id && r.OrganizationId == org).OrderBy(r => r.Sequence).ToListAsync(ct);
        return await GradingInputs.BuildInputAsync(db, session, exam, objective, responses, policy?.AllowAssistedEvidence ?? false, ct);
    }
}

/// <summary>
/// Runs a panel bias probe that a person started (GAD-T432). One attempt only: a failed run is recorded and not retried, so a
/// provider outage never multiplies its cost. The panel calls are metered to the organization like any other.
/// </summary>
public sealed class BiasProbeJob(GadflyDbContext db, ISocraticVotingClient panel, UsageMeter meter, TimeProvider clock) : IJobHandler
{
    public const string JobKind = "grading.bias-probe";
    public string Kind => JobKind;

    public async Task RunAsync(Job job, IJobProgress progress, CancellationToken ct)
    {
        var p = JobQueue.ReadPayload<BiasProbePayload>(job);
        var reports = new List<PanelBiasReport>();
        IReadOnlyList<PanelCall> calls;
        string? error = null;
        using (PanelUsage.Track(out calls))
        {
            try
            {
                var check = new PanelBiasCheck(panel);
                for (var i = 0; i < p.Items.Count; i++)
                {
                    await progress.ReportAsync((double)i / p.Items.Count, $"Probing answer {i + 1} of {p.Items.Count}", ct);
                    if (await BiasProbeService.InputAsync(db, p.OrganizationId, p.ExamId, p.Items[i], ct) is not { } input) continue;
                    reports.Add(await check.RunAsync(input, PanelBiasCheck.DefaultTolerancePercent, ct, p.Probes));
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                error = e.Message;
            }
        }
        db.ChangeTracker.Clear();
        meter.Stage(db, p.OrganizationId, null, calls, "bias-probe");
        List<PanelCall> snapshot;
        lock (calls) snapshot = [.. calls];
        var usd = snapshot.Sum(c => { var price = meter.PriceOf(c.ProviderId, c.Model); return (c.InputTokens * price.InputPerMillion + c.OutputTokens * price.OutputPerMillion) / 1_000_000m; });
        GradingAudit.Add(db, p.OrganizationId, GradingActor.System, error is null ? BiasProbeService.PanelCompleted : BiasProbeService.PanelFailed, BiasProbeService.Entity,
            p.RunId.ToString(), null, clock.GetUtcNow(),
            after: error is null ? new { reports, meteredUsd = Math.Round(usd, 4) } : null,
            reason: error is null ? $"{reports.Count} answer(s) probed; {reports.Count(r => r.AnyBias)} showed a shift past the tolerance." : $"The probe run failed: {error}");
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
