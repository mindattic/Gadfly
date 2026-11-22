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
        services.AddScoped<ClassSummaryService>();   // class summary PDF for instructors (GAD-T311/GAD-T378)
        services.AddJobHandler<AiGradingJob>();
        services.AddHostedService<GradingSweeper>();
        return services;
    }

    public static string ReportMarkdownPath(Guid sessionId) => $"/grading/{sessionId:D}/report.md";
    public static string ReportCsvPath(Guid sessionId) => $"/grading/{sessionId:D}/report.csv";
    public static string ReportPdfPath(Guid sessionId) => $"/grading/{sessionId:D}/report.pdf";
    public static string StudentReportMarkdownPath(Guid sessionId) => $"/my-results/{sessionId:D}/report.md";
    public static string StudentReportPdfPath(Guid sessionId) => $"/my-results/{sessionId:D}/report.pdf";

    /// <summary>Report downloads: reviewers (ViewTranscripts) any session; a student only their own published result.</summary>
    public static IEndpointRouteBuilder MapGrading(this IEndpointRouteBuilder app)
    {
        app.MapGet("/grading/{sessionId:guid}/report.md", async (Guid sessionId, GradingService grading, CancellationToken ct) =>
            await ReviewerReportAsync(grading, sessionId, "md", ct)).RequireAuthorization(GadflyPolicies.Member);
        app.MapGet("/grading/{sessionId:guid}/report.csv", async (Guid sessionId, GradingService grading, CancellationToken ct) =>
            await ReviewerReportAsync(grading, sessionId, "csv", ct)).RequireAuthorization(GadflyPolicies.Member);
        app.MapGet("/grading/{sessionId:guid}/report.pdf", async (Guid sessionId, GradingService grading, TimeProvider clock, CancellationToken ct) =>
            await ReviewerReportAsync(grading, sessionId, "pdf", ct, clock)).RequireAuthorization(GadflyPolicies.Member);
        app.MapGet("/my-results/{sessionId:guid}/report.md", async (Guid sessionId, GradingService grading, CancellationToken ct) =>
            await StudentReportAsync(grading, sessionId, "md", ct)).RequireAuthorization(GadflyPolicies.Student);
        app.MapGet("/my-results/{sessionId:guid}/report.pdf", async (Guid sessionId, GradingService grading, TimeProvider clock, CancellationToken ct) =>
            await StudentReportAsync(grading, sessionId, "pdf", ct, clock)).RequireAuthorization(GadflyPolicies.Student);
        app.MapGet("/grading/exam/{examId:guid}/class-summary.pdf", async (Guid examId, ClassSummaryService summary, TimeProvider clock, CancellationToken ct) =>
        {
            var report = await summary.BuildAsync(examId, ct);
            if (report is null) return Results.NotFound();
            var bytes = ClassSummaryPdf.Render(report, clock.GetUtcNow());
            return Results.File(bytes, "application/pdf", report.FileStem() + ".pdf");
        }).RequireAuthorization(GadflyPolicies.Member);
        app.MapPanelCalibration();   // GAD-T270
        return app;
    }

    private static async Task<IResult> ReviewerReportAsync(GradingService grading, Guid sessionId, string format, CancellationToken ct, TimeProvider? clock = null)
    {
        var w = await grading.WorkspaceAsync(sessionId, ct);
        if (w is null) return Results.NotFound();
        var report = AssessmentReport.Build(w, forStudent: false);
        return Render(report, format, clock);
    }

    private static async Task<IResult> StudentReportAsync(GradingService grading, Guid sessionId, string format, CancellationToken ct, TimeProvider? clock = null)
    {
        var w = await grading.StudentWorkspaceAsync(sessionId, ct);
        if (w is null) return Results.NotFound();
        var report = AssessmentReport.Build(w, forStudent: true);
        return Render(report, format, clock);
    }

    /// <summary>The report in the format asked for: Markdown and CSV beside the PDF (GAD-T311), which reuses the transcript
    /// downloads' hand-written PDF engine (<see cref="GradingReportPdf"/>) rather than a new PDF library.</summary>
    private static IResult Render(AssessmentReport report, string format, TimeProvider? clock) => format switch
    {
        "csv" => Results.File([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(report.ToCsv())], "text/csv; charset=utf-8", report.FileStem() + ".csv"),
        "pdf" => Results.File(GradingReportPdf.Render(report, (clock ?? TimeProvider.System).GetUtcNow()), "application/pdf", report.FileStem() + ".pdf"),
        _ => Results.File(Encoding.UTF8.GetBytes(report.ToMarkdown()), "text/markdown; charset=utf-8", report.FileStem() + ".md"),
    };
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
