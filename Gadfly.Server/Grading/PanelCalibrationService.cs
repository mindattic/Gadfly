using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>
/// The panel calibration recorded for one exam: the latest evaluated calibration set and whether the panel it
/// describes was accepted (GAD-T270).
/// </summary>
public sealed record PanelCalibrationStatus(Guid ExamId, string Panel, bool Accepted, CalibrationReport Report, DateTimeOffset At, Guid? ByUserId)
{
    /// <summary>The short form frozen into a session's <see cref="PolicySnapshot"/> when AI grading starts.</summary>
    public PanelCalibrationNote ToNote() => new(Panel, Accepted, Report.Items, Report.PanelHumanQwk, Report.HumanHumanQwk, Report.Smd, At);
}

/// <summary>A session's record of the panel calibration in force when its AI grading started (null fields when none was recorded).</summary>
public sealed record PanelCalibrationNote(string Panel, bool Accepted, int Items, double? PanelHumanQwk, double? HumanHumanQwk, double? Smd, DateTimeOffset At);

/// <summary>
/// Per-exam acceptance of a grading panel (GAD-T270; research note 05; docs/PILOT.md). A panel is accepted for an
/// exam only after a blind, double-marked calibration set meets <see cref="CalibrationThresholds"/>: panel-human
/// QWK at least .70, no more than .10 below the human-human QWK, and |SMD| under .15. Each evaluation is stored as
/// an append-only audit event (<c>panel.calibration.evaluated</c>, entity <c>GradingPanel</c>, id = exam id); the
/// latest one is the exam's status. Until a panel is accepted, its grades for that exam are provisional: the
/// status is frozen into each session's policy snapshot and shown to graders, and the AI grade is never final
/// either way (a human grades blind, disagreements are reconciled, a human approves).
/// </summary>
public sealed class PanelCalibrationService(GadflyDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public const string EvaluatedAction = "panel.calibration.evaluated";
    public const string EntityType = "GradingPanel";

    private GradingActor Actor => new(tenant.UserId, tenant.Role);
    private bool Can(Permission p) => tenant.OrganizationId is not null && Actor.Has(p);

    /// <summary>The exam's latest recorded calibration, or null when none was recorded (or the exam is not visible to the caller).</summary>
    public async Task<PanelCalibrationStatus?> StatusAsync(Guid examId, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || !Can(Permission.ViewTranscripts)) return null;
        if (!await db.Exams.AsNoTracking().AnyAsync(e => e.Id == examId && e.OrganizationId == org, ct)) return null;
        return await LatestAsync(db, org, examId, ct);
    }

    /// <summary>The latest recorded calibration of an exam, read directly (background grading and the snapshot use this).</summary>
    public static async Task<PanelCalibrationStatus?> LatestAsync(GadflyDbContext db, Guid organizationId, Guid examId, CancellationToken ct)
    {
        var id = examId.ToString();
        var row = await db.AuditEvents.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.Action == EvaluatedAction && a.EntityType == EntityType && a.EntityId == id)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        var stored = GradingJson.Read<StoredCalibration>(row?.AfterJson);
        return row is null || stored?.Report is null ? null
            : new PanelCalibrationStatus(examId, stored.Panel, stored.Report.Accepted, stored.Report, row.At, row.ActorUserId);
    }

    /// <summary>
    /// Evaluates a calibration set for the exam and records the result. The panel is accepted only when every
    /// threshold holds; a failing set is recorded too (it replaces any earlier acceptance, since the panel no longer
    /// meets the rules on the latest evidence). Needs <see cref="Permission.ApproveGradingPolicyChange"/>.
    /// </summary>
    public async Task<(GradingResult Result, CalibrationReport? Report)> EvaluateAsync(Guid examId, string panel, IReadOnlyList<CalibrationItem> items, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        if (tenant.OrganizationId is not { } org) return (GradingResult.Fail("No active organization."), null);
        if (!Can(Permission.ApproveGradingPolicyChange))
        {
            await GradingAudit.DeniedAsync(db, org, Actor, Permission.ApproveGradingPolicyChange, "Recording a grading panel calibration", null, now, ct);
            return (GradingResult.Fail($"Your role in this organization does not allow this ({Permission.ApproveGradingPolicyChange}). The attempt was recorded."), null);
        }
        if (!await db.Exams.AsNoTracking().AnyAsync(e => e.Id == examId && e.OrganizationId == org, ct))
            return (GradingResult.Fail("That exam was not found in this organization."), null);
        if (string.IsNullOrWhiteSpace(panel) || panel.Length > 400) return (GradingResult.Fail("Name the panel being calibrated (its models, up to 400 characters)."), null);
        if (items.Count > 5000) return (GradingResult.Fail("Send at most 5,000 calibration answers."), null);
        if (items.Select(i => i.ItemId).Distinct(StringComparer.Ordinal).Count() != items.Count) return (GradingResult.Fail("Each calibration answer needs a distinct itemId."), null);

        CalibrationReport report;
        try { report = PanelCalibration.Evaluate(items); }
        catch (ArgumentException e) { return (GradingResult.Fail(e.Message), null); }

        GradingAudit.Add(db, org, Actor, EvaluatedAction, EntityType, examId.ToString(), null, now,
            after: new StoredCalibration(panel.Trim(), report),
            reason: report.Accepted ? "The panel meets the calibration thresholds for this exam." : string.Join(" ", report.Failures));
        await db.SaveChangesAsync(ct);
        return (report.Accepted
            ? GradingResult.Done($"Panel accepted for this exam: QWK {report.PanelHumanQwk:0.00} (human-human {report.HumanHumanQwk:0.00}), SMD {report.Smd:0.00}, {report.Items} answers.")
            : GradingResult.Fail("Panel not accepted for this exam. " + string.Join(" ", report.Failures)), report);
    }

    private sealed record StoredCalibration(string Panel, CalibrationReport Report);
}

/// <summary>
/// HTTP for panel calibration (GAD-T270): <c>GET /grading/calibration/{examId}</c> reads the exam's latest
/// calibration; <c>POST</c> (antiforgery token in the RequestVerificationToken header, from
/// <c>GET /grading/actions/antiforgery</c>) evaluates and records a calibration set:
/// <c>{ panel, items: [{ itemId, panelLevel, humanALevel, humanBLevel }] }</c>, levels 0..5.
/// </summary>
public static class PanelCalibrationEndpoints
{
    public const string Prefix = "/grading/calibration";

    public sealed record ItemBody(string? ItemId, int PanelLevel, int HumanALevel, int HumanBLevel);
    public sealed record CalibrationBody(string? Panel, IReadOnlyList<ItemBody>? Items);

    public static IEndpointRouteBuilder MapPanelCalibration(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Prefix).RequireAuthorization(GadflyPolicies.Member);

        group.MapGet("/{examId:guid}", async (Guid examId, PanelCalibrationService s, CancellationToken ct) =>
            await s.StatusAsync(examId, ct) is { } status
                ? Results.Json(new { examId, status.Panel, status.Accepted, status.Report, status.At })
                : Results.NotFound(new { ok = false, message = "No calibration is recorded for that exam (or it is not visible to you)." }));

        group.MapPost("/{examId:guid}", async (Guid examId, CalibrationBody body, PanelCalibrationService s, HttpContext http, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            try { await antiforgery.ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException)
            {
                return Results.Json(new { ok = false, message = $"Missing or invalid antiforgery token: GET {GradingActionEndpoints.Prefix}/antiforgery and send it in the RequestVerificationToken header." },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            if (body.Items is not { Count: > 0 }) return Results.BadRequest(new { ok = false, message = "Send items: [{ itemId, panelLevel, humanALevel, humanBLevel }]." });
            var items = body.Items.Select((i, n) => new CalibrationItem(string.IsNullOrWhiteSpace(i.ItemId) ? $"#{n + 1}" : i.ItemId.Trim(), i.PanelLevel, i.HumanALevel, i.HumanBLevel)).ToList();
            var (result, report) = await s.EvaluateAsync(examId, body.Panel ?? "", items, ct);
            var reply = new { ok = result.Ok, message = result.Message, accepted = report?.Accepted ?? false, report };
            if (result.Ok || report is not null) return Results.Json(reply);   // a recorded, failing calibration is a valid answer
            return Results.Json(reply, statusCode: result.Message.StartsWith("Your role", StringComparison.Ordinal) ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest);
        });

        return app;
    }
}
