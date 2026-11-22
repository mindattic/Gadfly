using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>
/// Thin HTTP actions over <see cref="GradingService"/> for the human grading workflow (GAD-T95..T98; docs/API.md
/// "Grading workflow"): blind human grades, reconciliation, approval, publication, appeals and amendments, plus a
/// status read. They exist so the workflow can be driven without the Blazor pages (the demo tour, GAD-T105, and
/// integrators acting for a signed-in person). Signed-in cookie users only (the API key scheme is not accepted:
/// every action is a person's accountable act), and every POST must carry the antiforgery token from
/// <c>GET /grading/actions/antiforgery</c> in the <c>RequestVerificationToken</c> header. Every rule (permissions,
/// <see cref="PermissionMatrix"/> refusals audited as <c>access.denied</c>, status transitions, the blind rule,
/// audit events) is the service's; this file only translates HTTP to service calls and results to JSON.
/// Wiring: <c>app.MapGradingActions();</c> next to <c>app.MapGrading();</c>.
/// </summary>
public static class GradingActionEndpoints
{
    public const string Prefix = "/grading/actions";

    public static WebApplication MapGradingActions(this WebApplication app)
    {
        var group = app.MapGroup(Prefix).RequireAuthorization(GadflyPolicies.Member);

        // The token a client sends back in the RequestVerificationToken header (cookie-bound, user-bound).
        group.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { headerName = tokens.HeaderName, token = tokens.RequestToken });
        });

        group.MapGet("/{sessionId:guid}", StatusAsync);

        var post = group.MapGroup("/{sessionId:guid}").AddEndpointFilter(ValidateAntiforgeryAsync);

        post.MapPost("/ai-grading", async (Guid sessionId, GradingService g, GadflyDbContext db, CancellationToken ct) =>
            await ReplyAsync(await g.StartAiGradingAsync(sessionId, ct), db, sessionId, ct));

        post.MapPost("/human-grades", async (Guid sessionId, GradesBody body, GradingService g, GadflyDbContext db, CancellationToken ct) =>
        {
            var (inputs, error) = await InputsAsync(db, sessionId, body.Grades, ct);
            if (error is not null) return Results.BadRequest(new { ok = false, message = error });
            return await ReplyAsync(await g.SubmitHumanGradesAsync(sessionId, inputs, ct), db, sessionId, ct);
        });

        post.MapPost("/human-grades/reopen", async (Guid sessionId, ReasonBody body, GradingService g, GadflyDbContext db, CancellationToken ct) =>
            await ReplyAsync(await g.ReopenHumanReviewAsync(sessionId, body.Reason ?? "", ct), db, sessionId, ct));

        post.MapPost("/second-review", async (Guid sessionId, SecondReviewBody body, GradingService g, GadflyDbContext db, CancellationToken ct) =>
            await ReplyAsync(await g.RequestSecondReviewAsync(sessionId, body.DisagreementId, body.ReviewerUserId, ct), db, sessionId, ct));

        post.MapPost("/reconciliations", async (Guid sessionId, ReconcileBody body, GradingService g, GadflyDbContext db, CancellationToken ct) =>
            await ReplyAsync(await g.ReconcileAsync(sessionId, body.DisagreementId, body.Cause ?? "", body.Resolution ?? "", body.ResolvedAiScore,
                body.ResolvedHumanScore, body.SecondReviewerUserId, ct), db, sessionId, ct));

        post.MapPost("/approve", async (Guid sessionId, ApproveBody body, GradingService g, GadflyDbContext db, CancellationToken ct) =>
            await ReplyAsync(await g.ApproveAsync(sessionId, body.Label, body.Note, ct), db, sessionId, ct));

        post.MapPost("/publish", async (Guid sessionId, NoteBody body, GradingService g, GadflyDbContext db, CancellationToken ct) =>
            await ReplyAsync(await g.PublishAsync(sessionId, body.Note, ct), db, sessionId, ct));

        post.MapPost("/appeals", async (Guid sessionId, ReasonBody body, GradingService g, GadflyDbContext db, CancellationToken ct) =>
            await ReplyAsync(await g.AppealAsync(sessionId, body.Reason ?? "", ct), db, sessionId, ct));

        post.MapPost("/appeals/{appealId:guid}/resolve", async (Guid sessionId, Guid appealId, ResolveBody body, GradingService g, GadflyDbContext db, CancellationToken ct) =>
        {
            if (!Enum.TryParse<AppealState>(body.Outcome, ignoreCase: true, out var outcome) || outcome is not (AppealState.Upheld or AppealState.Dismissed))
                return Results.BadRequest(new { ok = false, message = "outcome must be Upheld or Dismissed (to change the grade, POST /amend with the appealId)." });
            return await ReplyAsync(await g.ResolveAppealAsync(sessionId, appealId, outcome, body.Resolution ?? "", ct), db, sessionId, ct);
        });

        post.MapPost("/amend", async (Guid sessionId, AmendBody body, GradingService g, GadflyDbContext db, CancellationToken ct) =>
        {
            var (inputs, error) = await InputsAsync(db, sessionId, body.Grades, ct);
            if (error is not null) return Results.BadRequest(new { ok = false, message = error });
            return await ReplyAsync(await g.AmendAsync(sessionId, inputs, body.Reason ?? "", body.AppealId, ct), db, sessionId, ct);
        });

        return app;
    }

    // ------------------------------------------------------------------ bodies

    public sealed record GradeBody(Guid? ObjectiveId, string? ObjectiveKey, string? Level, decimal? Score, string? Comment, string? EvidenceNote);
    public sealed record GradesBody(IReadOnlyList<GradeBody>? Grades);
    public sealed record AmendBody(IReadOnlyList<GradeBody>? Grades, string? Reason, Guid? AppealId);
    public sealed record ReasonBody(string? Reason);
    public sealed record NoteBody(string? Note);
    public sealed record ApproveBody(string? Label, string? Note);
    public sealed record SecondReviewBody(Guid DisagreementId, Guid ReviewerUserId);
    public sealed record ReconcileBody(Guid DisagreementId, string? Cause, string? Resolution, decimal? ResolvedAiScore, decimal? ResolvedHumanScore, Guid? SecondReviewerUserId);
    public sealed record ResolveBody(string? Outcome, string? Resolution);

    // ------------------------------------------------------------------ plumbing

    private static async ValueTask<object?> ValidateAntiforgeryAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var antiforgery = ctx.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(ctx.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Json(new { ok = false, message = $"Missing or invalid antiforgery token: GET {Prefix}/antiforgery and send it in the RequestVerificationToken header." },
                statusCode: StatusCodes.Status400BadRequest);
        }
        return await next(ctx);
    }

    /// <summary>The service's refusal of a permission (already audited as access.denied by the service).</summary>
    private static bool IsDenied(string message) =>
        message.StartsWith("Your role in this organization does not allow", StringComparison.Ordinal)
        || message.StartsWith("You can appeal only your own published result", StringComparison.Ordinal);

    private static async Task<IResult> ReplyAsync(GradingResult result, GadflyDbContext db, Guid sessionId, CancellationToken ct)
    {
        var status = await db.FinalGrades.AsNoTracking().Where(g => g.SessionId == sessionId).Select(g => (GradeStatus?)g.Status).FirstOrDefaultAsync(ct);
        var body = new { ok = result.Ok, message = result.Message, status = status?.ToString() };
        if (result.Ok) return Results.Json(body);
        var code = IsDenied(result.Message) ? StatusCodes.Status403Forbidden
            : result.Message.StartsWith("Refused:", StringComparison.Ordinal) ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;
        return Results.Json(body, statusCode: code);
    }

    /// <summary>Grade inputs from the body: objectives by id or key (of the session's exam), levels by name or number.</summary>
    private static async Task<(List<HumanGradeInput> Inputs, string? Error)> InputsAsync(GadflyDbContext db, Guid sessionId, IReadOnlyList<GradeBody>? grades, CancellationToken ct)
    {
        if (grades is not { Count: > 0 }) return ([], "Send grades: [{ objectiveId or objectiveKey, level, score, comment?, evidenceNote? }].");
        var examId = await db.ExamSessions.AsNoTracking().Where(s => s.Id == sessionId).Select(s => (Guid?)s.ExamId).FirstOrDefaultAsync(ct);
        var keys = examId is null ? [] : await db.LearningObjectives.AsNoTracking().Where(o => o.ExamId == examId).ToDictionaryAsync(o => o.Key, o => o.Id, StringComparer.Ordinal, ct);
        var inputs = new List<HumanGradeInput>();
        foreach (var g in grades)
        {
            var id = g.ObjectiveId ?? (g.ObjectiveKey is { } k && keys.TryGetValue(k, out var byKey) ? byKey : (Guid?)null);
            if (id is null) return ([], $"Unknown objective {g.ObjectiveKey ?? "(none)"}: give objectiveId or an objectiveKey of this session's exam.");
            if (!Enum.TryParse<MasteryLevel>(g.Level, ignoreCase: true, out var level) || !Enum.IsDefined(level))
                return ([], $"Level \"{g.Level}\" is not a mastery level (0-5 or {string.Join(", ", Enum.GetNames<MasteryLevel>())}).");
            inputs.Add(new HumanGradeInput(id.Value, level, g.Score, g.Comment, g.EvidenceNote));
        }
        return (inputs, null);
    }

    // ------------------------------------------------------------------ status

    /// <summary>
    /// Reviewers (ViewTranscripts) get the workspace; while blind grading hides the AI's evaluation (the service's
    /// <see cref="GradingService.AiVisible"/>), no AI score, combined score, disagreement or final percentage is
    /// returned. A student gets only their own result, and only once it is approved and published.
    /// </summary>
    private static async Task<IResult> StatusAsync(Guid sessionId, GradingService grading, CancellationToken ct)
    {
        if (grading.Can(Permission.ViewTranscripts))
        {
            var w = await grading.WorkspaceAsync(sessionId, ct);
            return w is null ? Results.NotFound() : Results.Json(ReviewerView(w));
        }
        if (grading.Can(Permission.ViewOwnPublishedResults))
        {
            var w = await grading.StudentWorkspaceAsync(sessionId, ct);   // audits a refusal
            return w is null ? Results.NotFound() : Results.Json(StudentView(w));
        }
        await grading.WorkspaceAsync(sessionId, ct);   // records the access.denied audit event
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static object ReviewerView(GradingWorkspace w)
    {
        var visible = w.AiVisible;
        var p = w.Policy.Rules;
        return new
        {
            sessionId = w.Session.Id, examTitle = w.ExamTitle, student = w.StudentName, attempt = w.Session.Attempt,
            status = w.Status?.ToString(), statusLabel = w.Status is { } s ? GradeStatusMachine.Label(s) : "Not started",
            aiVisible = visible, isMock = visible && w.AnyMock,
            // GAD-T285: whether the caller entered this grade's human score, and so may not reconcile or approve it.
            separationOfDuties = new { youScoredThisGrade = w.ViewerScoredHuman, singleGraderAllowed = w.SingleGraderAllowed, blocksYou = w.SeparationBlocksViewer },
            finalPercent = visible ? w.Grade?.FinalPercent : null, label = visible ? w.Grade?.LetterOrLabel : null,
            policy = new { aiWeight = p.AiWeight, humanWeight = p.HumanWeight, additionalReviewAt = p.AdditionalReviewAt,
                mandatoryReconciliationAt = p.MandatoryReconciliationAt, blindHumanGrading = p.BlindHumanGrading, source = w.Policy.Source },
            aiGradingJob = new { state = w.JobState, error = w.JobError },
            responsesNeedingHuman = w.ResponsesNeedingHuman,
            objectives = w.Objectives.Select(o => new
            {
                id = o.Objective.Id, key = o.Objective.Key, title = o.Objective.Title, maxScore = o.Objective.MaxScore, weight = o.Objective.Weight,
                aiRecorded = o.Ai is not null,
                ai = visible && o.Ai is { } ai ? new
                {
                    level = ai.Row.Level.ToString(), levelNumber = (int)ai.Row.Level, score = ai.Row.Score, maxScore = ai.Row.MaxScore,
                    confidence = ai.Row.Confidence, isMock = ai.Row.IsMock, explanation = ai.Row.Explanation, excluded = o.AiExcluded,
                } : null,
                human = o.Human is { } h ? new
                {
                    id = h.Id, level = h.Level.ToString(), levelNumber = (int)h.Level, score = h.Score, maxScore = h.MaxScore, wasBlind = h.WasBlind,
                    reviewer = w.Name(h.ReviewerUserId), reviewerRole = h.ReviewerRole, comment = h.Comment, submittedAt = h.SubmittedAt,
                    supersedes = h.SupersedesId,
                } : null,
                humanHistory = o.HumanHistory.Count,
                combined = visible && o.Combined is { } c ? new
                {
                    aiScore = c.AiScore, humanScore = c.HumanScore, combined = c.Combined, differencePoints = c.DifferencePoints, band = c.Band?.ToString(),
                } : null,
            }).ToList(),
            disagreements = !visible ? new List<object>() : w.Objectives.Where(o => o.Disagreement is not null).Select(o => (object)new
            {
                id = o.Disagreement!.Id, objectiveId = o.Objective.Id, objectiveKey = o.Objective.Key, aiScore = o.Disagreement.AiScore,
                humanScore = o.Disagreement.HumanScore, differencePoints = o.Disagreement.DifferencePoints, band = o.Disagreement.Band.ToString(),
                resolved = o.Disagreement.ResolvedAt is not null, resolvedAt = o.Disagreement.ResolvedAt, needsReconciliation = o.NeedsReconciliation,
                reconciliations = o.Reconciliations.Select(r => new
                {
                    id = r.Id, cause = r.Cause, resolution = r.Resolution, reviewer = w.Name(r.ReviewerUserId),
                    secondReviewer = r.SecondReviewerUserId is { } sr ? w.Name(sr) : null, resolvedAiScore = r.ResolvedAiScore,
                    resolvedHumanScore = r.ResolvedHumanScore, at = r.At,
                }).ToList(),
            }).ToList(),
            approvals = w.Approvals.Select(a => new { action = a.Record.Action.ToString(), actor = a.Actor, role = a.Record.ActorRole, note = a.Record.Note, at = a.Record.At }).ToList(),
            appeals = w.Appeals.Select(a => new
            {
                id = a.Appeal.Id, state = a.Appeal.State.ToString(), reason = a.Appeal.Reason, resolution = a.Appeal.Resolution,
                openedAt = a.Appeal.OpenedAt, resolvedAt = a.Appeal.ResolvedAt,
            }).ToList(),
            secondReviewers = w.SecondReviewers.Select(r => new { userId = r.UserId, name = r.Name }).ToList(),
        };
    }

    /// <summary>The student's view: what the student's assessment report shows (no reviewer names, voters or audit).</summary>
    private static object StudentView(GradingWorkspace w)
    {
        var report = AssessmentReport.Build(w, forStudent: true);
        return new
        {
            sessionId = w.Session.Id, examTitle = w.ExamTitle, attempt = w.Session.Attempt,
            status = w.Status?.ToString(), statusLabel = report.Status, finalPercent = report.FinalPercent, label = report.Label, isMock = report.IsMock,
            objectives = report.Objectives.Select(o => new
            {
                key = o.Key, title = o.Title, maxScore = o.MaxScore, aiScore = o.AiScore, aiLevel = o.AiLevel, humanScore = o.HumanScore,
                humanLevel = o.HumanLevel, combined = o.Combined,
            }).ToList(),
            knowledgeGaps = report.KnowledgeGaps,
            approvals = report.Approvals,
            appeals = w.Appeals.Select(a => new { id = a.Appeal.Id, state = a.Appeal.State.ToString(), resolution = a.Appeal.Resolution, openedAt = a.Appeal.OpenedAt, resolvedAt = a.Appeal.ResolvedAt }).ToList(),
        };
    }
}
