using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Sources.Grading;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>
/// The panel calibration recorded for one exam: the latest evaluated calibration set and whether the panel it
/// describes was accepted (GAD-T270). It expires when any panel member's model id differs from the ids it covered
/// (GAD-T429), or when the grading prompt-set version differs from the one it was measured under (GAD-T469): grades are provisional again and a gated exam grades on the human alone until it is re-run.
/// </summary>
public sealed record PanelCalibrationStatus(Guid ExamId, string Panel, bool Accepted, CalibrationReport Report, DateTimeOffset At, Guid? ByUserId)
{
    /// <summary>Provider → model id of every member the calibration covered; null for calibrations recorded before GAD-T429.</summary>
    public IReadOnlyDictionary<string, string>? Models { get; init; }
    /// <summary>The grading prompt-set version it was measured under (<see cref="AiObjectiveGrader.PromptVersion"/>, GAD-T469).</summary>
    public int GradingPromptVersion { get; init; } = 1;
    /// <summary>"api" (levels posted from outside) or "double-marking" (built in the app from blind second marks, GAD-T428).</summary>
    public string Source { get; init; } = PanelCalibrationService.SourceApi;
    /// <summary>The calibration no longer describes the panel: its models changed (or were never recorded).</summary>
    public bool Expired { get; init; }
    public IReadOnlyList<string> ExpiryReasons { get; init; } = [];
    /// <summary>Older than the re-run interval: prompt a new calibration and bias probes (nothing runs by itself).</summary>
    public bool RecheckDue { get; init; }
    /// <summary>Accepted and still describing the panel in use: only then may a gated exam give the panel weight.</summary>
    public bool Effective => Accepted && !Expired;

    /// <summary>The short form frozen into a session's <see cref="PolicySnapshot"/> when AI grading starts.</summary>
    public PanelCalibrationNote ToNote() => new(Panel, Accepted, Report.Items, Report.PanelHumanQwk, Report.HumanHumanQwk, Report.Smd, At)
    {
        Expired = Expired, ExpiryReasons = ExpiryReasons, Models = Models, GradingPromptVersion = GradingPromptVersion,
    };
}

/// <summary>A session's record of the panel calibration in force when its AI grading started (null fields when none was recorded).</summary>
public sealed record PanelCalibrationNote(string Panel, bool Accepted, int Items, double? PanelHumanQwk, double? HumanHumanQwk, double? Smd, DateTimeOffset At)
{
    /// <summary>The calibration had expired when grading started (GAD-T429): the panel's models had changed since.</summary>
    public bool Expired { get; init; }
    public IReadOnlyList<string> ExpiryReasons { get; init; } = [];
    public IReadOnlyDictionary<string, string>? Models { get; init; }
    /// <summary>The grading prompt-set version the calibration was measured under (GAD-T469).</summary>
    public int GradingPromptVersion { get; init; } = 1;
    public bool Effective => Accepted && !Expired;
}

/// <summary>
/// How a calibration is described to graders (GAD-T427; ElenchusAtScale Section 5.8.3, EU AI Act Art. 14(4)(b)). A
/// "calibrated" label invites deference to the machine, so the wording says what the calibration measured (agreement over a
/// set of answers) and what it did not (whether this answer's grade is right).
/// </summary>
public static class PanelCalibrationWording
{
    public static string Describe(PanelCalibrationNote? note) => note switch
    {
        null => "No calibration recorded for this exam: the panel's agreement with human markers is unknown, so its grades are provisional.",
        { Expired: true } => $"The calibration expired ({string.Join("; ", note.ExpiryReasons)}): grades are provisional again until it is re-run.",
        { Accepted: true } => $"Met the calibration thresholds on {note.Items} double-marked answers (agreement with the markers QWK {note.PanelHumanQwk:0.00}). " +
                              "That describes agreement over a set of answers, not whether this grade is right: judge it on its own evidence.",
        _ => "Did not meet the calibration thresholds on its last calibration: grades are provisional.",
    };
}

/// <summary>
/// Per-exam acceptance of a grading panel (GAD-T270; research note 05; docs/PILOT.md). A panel is accepted for an
/// exam only after a blind, double-marked calibration set meets <see cref="CalibrationThresholds"/>: panel-human
/// QWK at least .70, no more than .10 below the human-human QWK, and |SMD| under .15. Each evaluation is stored as
/// an append-only audit event (<c>panel.calibration.evaluated</c>, entity <c>GradingPanel</c>, id = exam id); the
/// latest one is the exam's status. Until a panel is accepted, its grades for that exam are provisional: the
/// status is frozen into each session's policy snapshot and shown to graders, and the AI grade is never final
/// either way (a human grades blind, disagreements are reconciled, a human approves).
/// </summary>
public sealed class PanelCalibrationService(GadflyDbContext db, ITenantContext tenant, TimeProvider clock, PanelModelInventory models, IConfiguration config)
{
    public const string EvaluatedAction = "panel.calibration.evaluated";
    public const string EntityType = "GradingPanel";
    public const string SourceApi = "api";
    public const string SourceDoubleMarking = "double-marking";

    /// <summary>Days after which a calibration (and the bias probes) should be re-run: a prompt only, nothing runs by itself (GAD-T429).</summary>
    public static int RecheckDays(IConfiguration config) => Math.Clamp(config.GetValue("Gadfly:Grading:RecalibrateAfterDays", 180), 7, 3650);

    private GradingActor Actor => new(tenant.UserId, tenant.Role);
    private bool Can(Permission p) => tenant.OrganizationId is not null && Actor.Has(p);

    /// <summary>The exam's latest recorded calibration, or null when none was recorded (or the exam is not visible to the caller).</summary>
    public async Task<PanelCalibrationStatus?> StatusAsync(Guid examId, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || !Can(Permission.ViewTranscripts)) return null;
        if (!await db.Exams.AsNoTracking().AnyAsync(e => e.Id == examId && e.OrganizationId == org, ct)) return null;
        return await LatestAsync(db, org, examId, models.Current, clock.GetUtcNow(), RecheckDays(config), ct);
    }

    /// <summary>
    /// The latest recorded calibration of an exam, read directly (background grading and the snapshot use this), checked against
    /// <paramref name="currentModels"/>: it is expired when any member's model id differs, a member joined or left, or the
    /// calibration predates model recording (GAD-T429). Null <paramref name="currentModels"/> skips the check.
    /// </summary>
    public static async Task<PanelCalibrationStatus?> LatestAsync(GadflyDbContext db, Guid organizationId, Guid examId,
        IReadOnlyDictionary<string, string>? currentModels, DateTimeOffset now, int recheckDays, CancellationToken ct)
    {
        var id = examId.ToString();
        var row = await db.AuditEvents.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.Action == EvaluatedAction && a.EntityType == EntityType && a.EntityId == id)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        var stored = GradingJson.Read<StoredCalibration>(row?.AfterJson);
        if (row is null || stored?.Report is null) return null;
        var reasons = currentModels is null ? []
            : stored.Models is null ? ["it was recorded before Gadfly kept the panel's model ids, so it cannot show it covers the models in use"]
            : PanelModelSet.Differences(stored.Models, currentModels).ToList();
        // The grading prompts too (GAD-T469): a calibration measured under one grading prompt does not carry to the next.
        if (currentModels is not null) reasons = [.. reasons, .. AiObjectiveGrader.PromptChanges(stored.GradingPromptVersion)];
        return new PanelCalibrationStatus(examId, stored.Panel, stored.Report.Accepted, stored.Report, row.At, row.ActorUserId)
        {
            Models = stored.Models, GradingPromptVersion = stored.GradingPromptVersion ?? 1,
            Source = stored.Source ?? SourceApi, Expired = reasons.Count > 0, ExpiryReasons = reasons,
            RecheckDue = now - row.At > TimeSpan.FromDays(recheckDays),
        };
    }

    /// <summary>
    /// Evaluates a calibration set for the exam and records the result. The panel is accepted only when every
    /// threshold holds; a failing set is recorded too (it replaces any earlier acceptance, since the panel no longer
    /// meets the rules on the latest evidence). The model id of every panel member is recorded with it (GAD-T429); a
    /// calibration of the mock panel, or built from mock grades, is never accepted. Needs <see cref="Permission.ApproveGradingPolicyChange"/>.
    /// </summary>
    public async Task<(GradingResult Result, CalibrationReport? Report)> EvaluateAsync(Guid examId, string? panel, IReadOnlyList<CalibrationItem> items, CancellationToken ct = default,
        string source = SourceApi, bool fromMockGrades = false)
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
        var current = models.Current;
        panel = string.IsNullOrWhiteSpace(panel) ? PanelModelSet.Describe(current) : panel.Trim();
        if (panel.Length > 400) return (GradingResult.Fail("Name the panel being calibrated in up to 400 characters."), null);
        if (items.Count > 5000) return (GradingResult.Fail("Send at most 5,000 calibration answers."), null);
        if (items.Select(i => i.ItemId).Distinct(StringComparer.Ordinal).Count() != items.Count) return (GradingResult.Fail("Each calibration answer needs a distinct itemId."), null);

        CalibrationReport report;
        try { report = PanelCalibration.Evaluate(items); }
        catch (ArgumentException e) { return (GradingResult.Fail(e.Message), null); }
        if (fromMockGrades || models.IsMock)
            report = report with
            {
                Accepted = false,
                Failures = [.. report.Failures, "The panel levels come from the mock panel, which is not an AI evaluation: a mock calibration never accepts a panel."],
            };

        GradingAudit.Add(db, org, Actor, EvaluatedAction, EntityType, examId.ToString(), null, now,
            after: new StoredCalibration(panel, report, current, source, AiObjectiveGrader.PromptVersion),
            reason: report.Accepted ? $"The panel meets the calibration thresholds for this exam ({PanelModelSet.Describe(current)})." : string.Join(" ", report.Failures));
        await db.SaveChangesAsync(ct);
        return (report.Accepted
            ? GradingResult.Done($"Panel accepted for this exam: QWK {report.PanelHumanQwk:0.00} (human-human {report.HumanHumanQwk:0.00}), SMD {report.Smd:0.00}, {report.Items} answers. " +
                                 $"Recorded against {PanelModelSet.Describe(current)} and grading prompts version {AiObjectiveGrader.PromptVersion}; it expires if any of these models or the grading prompts change.")
            : GradingResult.Fail("Panel not accepted for this exam. " + string.Join(" ", report.Failures)), report);
    }

    private sealed record StoredCalibration(string Panel, CalibrationReport Report, IReadOnlyDictionary<string, string>? Models = null, string? Source = null,
        int? GradingPromptVersion = null);
}

/// <summary>
/// HTTP for panel calibration (GAD-T270): <c>GET /grading/calibration/{examId}</c> reads the exam's latest
/// calibration (with the model ids it covered and whether it expired, GAD-T429); <c>POST</c> (antiforgery token in the
/// RequestVerificationToken header, from <c>GET /grading/actions/antiforgery</c>) evaluates and records a calibration set:
/// <c>{ panel?, items: [{ itemId, panelLevel, humanALevel, humanBLevel }] }</c>, levels 0..5. The panel name defaults to the
/// configured models.
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
                ? Results.Json(new
                {
                    examId, status.Panel, status.Accepted, status.Report, status.At, status.Models, status.GradingPromptVersion, status.Source,
                    status.Expired, status.ExpiryReasons, status.RecheckDue, effective = status.Effective,
                })
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
            var (result, report) = await s.EvaluateAsync(examId, body.Panel, items, ct);
            var reply = new { ok = result.Ok, message = result.Message, accepted = report?.Accepted ?? false, report };
            if (result.Ok || report is not null) return Results.Json(reply);   // a recorded, failing calibration is a valid answer
            return Results.Json(reply, statusCode: result.Message.StartsWith("Your role", StringComparison.Ordinal) ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest);
        });

        return app;
    }
}
