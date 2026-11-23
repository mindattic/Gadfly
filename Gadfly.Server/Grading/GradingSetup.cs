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
        services.AddScoped<PanelModelInventory>();   // the panel's model ids, for calibration expiry (GAD-T429)
        services.AddScoped<PanelGovernanceService>();   // calibration gate and double-marking settings (GAD-T427, GAD-T428)
        services.AddScoped<DoubleMarkingService>();   // blind second marking and the in-app calibration set (GAD-T428)
        services.AddScoped<SelfPreferenceService>();   // graders vs. the authors of the questions (GAD-T431)
        services.AddScoped<BiasProbeService>();   // panel and human bias probes, started by a person (GAD-T432)
        services.AddJobHandler<BiasProbeJob>();
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
        app.MapGet("/my-results/{sessionId:guid}/report.md", async (Guid sessionId, GradingService grading, IServiceScopeFactory scopes, CancellationToken ct) =>
            await StudentReportAsync(grading, scopes, sessionId, "md", ct)).RequireAuthorization(GadflyPolicies.Student);
        app.MapGet("/my-results/{sessionId:guid}/report.pdf", async (Guid sessionId, GradingService grading, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct) =>
            await StudentReportAsync(grading, scopes, sessionId, "pdf", ct, clock)).RequireAuthorization(GadflyPolicies.Student);
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

    private static async Task<IResult> StudentReportAsync(GradingService grading, IServiceScopeFactory scopes, Guid sessionId, string format, CancellationToken ct, TimeProvider? clock = null)
    {
        var w = await grading.StudentWorkspaceAsync(sessionId, ct);
        if (w is null) return Results.NotFound();
        return Render(await StudentDownloadReportAsync(scopes, w), format, clock);
    }

    /// <summary>
    /// The student's own result download (Markdown and PDF): the report the result page shows, with the exam and objective
    /// titles in the attempt's language when that language's texts are approved, else the English (GAD-T634, as GAD-T630).
    /// Staff exports stay in English.
    /// </summary>
    public static Task<AssessmentReport> StudentDownloadReportAsync(IServiceScopeFactory scopes, GradingWorkspace w) =>
        Student.StudentExamTexts.LocalizeAsync(scopes, w.Session.OrganizationId, w.Session.ExamId, w.Session.Language,
            AssessmentReport.Build(w, forStudent: true));

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
