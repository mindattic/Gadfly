using System.Text;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Review;

/// <summary>
/// Downloads for instructor review (GAD-T54): a session's turns as CSV and its report as Markdown.
/// Instructor policy; the request's tenant scopes the data. A PDF export is planned on top of the
/// Markdown report. Wire with <c>ReviewExportEndpoints.Map(app);</c> in Program.cs.
/// </summary>
public static class ReviewExportEndpoints
{
    public static string CsvPath(Guid sessionId) => $"/review/{sessionId:D}/turns.csv";
    public static string MarkdownPath(Guid sessionId) => $"/review/{sessionId:D}/report.md";

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/review/{sessionId:guid}/turns.csv", async (Guid sessionId, GadflyDbContext db, ITenantContext tenant, CancellationToken ct) =>
            {
                var view = await LoadAsync(db, tenant, sessionId, ct);
                return view is null ? Results.NotFound()
                    : Results.File(Utf8Bom(ReviewService.ToCsv(view)), "text/csv; charset=utf-8", ReviewService.FileStem(view) + ".csv");
            })
            .RequireAuthorization(GadflyPolicies.Instructor);

        app.MapGet("/review/{sessionId:guid}/report.md", async (Guid sessionId, GadflyDbContext db, ITenantContext tenant, CancellationToken ct) =>
            {
                var view = await LoadAsync(db, tenant, sessionId, ct);
                return view is null ? Results.NotFound()
                    : Results.File(Encoding.UTF8.GetBytes(ReviewService.ToMarkdown(view)), "text/markdown; charset=utf-8", ReviewService.FileStem(view) + "-report.md");
            })
            .RequireAuthorization(GadflyPolicies.Instructor);
    }

    private static async Task<SessionReviewView?> LoadAsync(GadflyDbContext db, ITenantContext tenant, Guid sessionId, CancellationToken ct) =>
        tenant.OrganizationId is { } org ? await new ReviewService(db, org).GetAsync(sessionId, ct) : null;

    // Excel reads UTF-8 CSV correctly only with a byte-order mark.
    private static byte[] Utf8Bom(string text) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];
}
