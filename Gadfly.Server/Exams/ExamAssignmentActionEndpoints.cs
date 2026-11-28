using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Antiforgery;

namespace Gadfly.Server.Exams;

/// <summary>
/// Thin HTTP action over <see cref="ExamService.SetTypedAnswerAccommodationAsync"/> (GAD-T260), so an instructor can grant or
/// withdraw a student's typed-answer accommodation without the Blazor assignment pages (GAD-T858: the demo tour, GAD-T105,
/// needs a real instructor action it can drive over HTTP, the way <see cref="Gadfly.Server.Grading.GradingActionEndpoints"/>
/// drives the grading workflow). Signed-in cookie users only; every POST must carry the antiforgery token from
/// <c>GET /exams/actions/antiforgery</c> in the <c>RequestVerificationToken</c> header. <see cref="Gadfly.Assessment.Permission.AssignExaminations"/>
/// is the service's own rule (audited on refusal); this file only translates HTTP to the service call and the result to JSON.
/// Wiring: <c>app.MapExamAssignmentActions();</c> next to <c>app.MapGrading();</c>.
/// </summary>
public static class ExamAssignmentActionEndpoints
{
    public const string Prefix = "/exams/actions";

    public static WebApplication MapExamAssignmentActions(this WebApplication app)
    {
        var group = app.MapGroup(Prefix).RequireAuthorization(GadflyPolicies.Member);

        group.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { headerName = tokens.HeaderName, token = tokens.RequestToken });
        });

        group.MapPost("/{assignmentId:guid}/typed-answer-accommodation", TypedAnswerAccommodationAsync)
            .AddEndpointFilter(ValidateAntiforgeryAsync);

        return app;
    }

    public sealed record TypedAnswerBody(bool On);

    private static async Task<IResult> TypedAnswerAccommodationAsync(Guid assignmentId, TypedAnswerBody body, ExamService exams, CancellationToken ct)
    {
        var result = await exams.SetTypedAnswerAccommodationAsync(assignmentId, body.On, ct);
        var reply = new { ok = result.Ok, message = result.Message };
        if (result.Ok) return Results.Json(reply);
        var code = result.Message.StartsWith("Your role in this organization does not allow", StringComparison.Ordinal)
            ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest;
        return Results.Json(reply, statusCode: code);
    }

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
}
