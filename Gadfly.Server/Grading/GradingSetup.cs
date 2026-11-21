using System.Text;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Grading;

/// <summary>
/// Registration of the grading workflow (GAD-T94..T99). Program.cs: <c>builder.Services.AddGrading();</c> and
/// <c>app.MapGrading();</c> (after the Razor components are mapped, like the other endpoint groups).
/// </summary>
public static class GradingSetup
{
    public static IServiceCollection AddGrading(this IServiceCollection services)
    {
        services.AddScoped<GradingService>();
        services.AddScoped<PanelCalibrationService>();   // per-exam panel acceptance (GAD-T270)
        services.AddJobHandler<AiGradingJob>();
        services.AddHostedService<GradingSweeper>();
        return services;
    }

    public static string ReportMarkdownPath(Guid sessionId) => $"/grading/{sessionId:D}/report.md";
    public static string ReportCsvPath(Guid sessionId) => $"/grading/{sessionId:D}/report.csv";
    public static string StudentReportMarkdownPath(Guid sessionId) => $"/my-results/{sessionId:D}/report.md";

    /// <summary>Report downloads: reviewers (ViewTranscripts) any session; a student only their own published result.</summary>
    public static IEndpointRouteBuilder MapGrading(this IEndpointRouteBuilder app)
    {
        app.MapGet("/grading/{sessionId:guid}/report.md", async (Guid sessionId, GradingService grading, CancellationToken ct) =>
            await ReviewerReportAsync(grading, sessionId, md: true, ct)).RequireAuthorization(GadflyPolicies.Member);
        app.MapGet("/grading/{sessionId:guid}/report.csv", async (Guid sessionId, GradingService grading, CancellationToken ct) =>
            await ReviewerReportAsync(grading, sessionId, md: false, ct)).RequireAuthorization(GadflyPolicies.Member);
        app.MapGet("/my-results/{sessionId:guid}/report.md", async (Guid sessionId, GradingService grading, CancellationToken ct) =>
        {
            var w = await grading.StudentWorkspaceAsync(sessionId, ct);
            if (w is null) return Results.NotFound();
            var report = AssessmentReport.Build(w, forStudent: true);
            return Results.File(Encoding.UTF8.GetBytes(report.ToMarkdown()), "text/markdown; charset=utf-8", report.FileStem() + ".md");
        }).RequireAuthorization(GadflyPolicies.Student);
        app.MapPanelCalibration();   // GAD-T270
        return app;
    }

    private static async Task<IResult> ReviewerReportAsync(GradingService grading, Guid sessionId, bool md, CancellationToken ct)
    {
        var w = await grading.WorkspaceAsync(sessionId, ct);
        if (w is null) return Results.NotFound();
        var report = AssessmentReport.Build(w, forStudent: false);
        return md
            ? Results.File(Encoding.UTF8.GetBytes(report.ToMarkdown()), "text/markdown; charset=utf-8", report.FileStem() + ".md")
            : Results.File([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(report.ToCsv())], "text/csv; charset=utf-8", report.FileStem() + ".csv");
    }
}

/// <summary>Runs grading work from a Blazor component in its own DI scope acting as the circuit's tenant (GAD-T73).</summary>
public static class GradingDb
{
    public static async Task<T> RunAsync<T>(IServiceScopeFactory scopes, ITenantContext tenant, Func<GradingService, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.Of(tenant));
        return await work(scope.ServiceProvider.GetRequiredService<GradingService>());
    }
}
