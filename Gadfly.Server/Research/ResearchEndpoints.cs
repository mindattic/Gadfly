using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Research;

/// <summary>
/// The de-identified research export (GAD-T451): <c>GET /admin/research/export.zip[?exam={id}]</c>, owners and admins only, audited
/// as <c>research.exported</c>. Wired with <c>Research.ResearchEndpoints.Map(app)</c> in GadflyEndpoints.
/// </summary>
public static class ResearchEndpoints
{
    public const string ExportPath = "/admin/research/export.zip";

    /// <summary>The export address (relative, for an href), optionally for one exam.</summary>
    public static string ExportHref(Guid? examId) => ExportPath.TrimStart('/') + (examId is { } e ? $"?exam={e:D}" : "");

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(ExportPath, async (Guid? exam, ITenantContext tenant, GadflyDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                if (tenant.OrganizationId is null) return Results.Forbid();
                var research = new ResearchService(db, clock);
                var result = await research.ExportAsync(TenantSnapshot.Of(tenant), exam, ct);
                if (result is null)
                    return Results.Problem("Research is not set up for this organization. Turn it on under Organization, Research first.", statusCode: StatusCodes.Status404NotFound);
                return Results.File(result.Value.Zip, "application/zip", $"gadfly-research-{clock.GetUtcNow().UtcDateTime:yyyyMMdd-HHmm}.zip");
            })
            .RequireAuthorization(GadflyPolicies.OrgAdmin);
    }
}
