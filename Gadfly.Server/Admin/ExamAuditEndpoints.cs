using System.Globalization;
using System.Text;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace Gadfly.Server.Admin;

/// <summary>
/// Downloads for the admin area: an exam's definition as JSON (GAD-T297) and the audit log as CSV or JSON, streamed (GAD-T301).
/// Each checks its own permission in its service (owners and admins; platform staff for any organization's audit log), so a
/// signed-in caller without it gets 403 and the refusal is audited where the service audits refusals.
/// Wired with <c>Admin.ExamAuditEndpoints.Map(app);</c> in GadflyEndpoints.
/// </summary>
public static class ExamAuditEndpoints
{
    public static string ExamExportPath(Guid examId) => $"admin/exams/{examId:D}/export.json";

    /// <summary>The export address for the audit log with the viewer's filters (relative, for an href).</summary>
    public static string AuditExportPath(bool json, Guid? organizationId, bool platform, AuditFilter f)
    {
        var q = new List<string>();
        void Add(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) q.Add($"{key}={Uri.EscapeDataString(value)}"); }
        if (platform) Add("platform", "1");
        else Add("org", organizationId?.ToString());
        Add("actor", f.ActorUserId?.ToString());
        Add("action", f.Action);
        Add("student", f.StudentUserId?.ToString());
        Add("exam", f.ExamId?.ToString());
        Add("session", f.SessionId?.ToString());
        Add("from", f.From?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        Add("to", f.To?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        Add("q", f.Search);
        return $"admin/audit/export.{(json ? "json" : "csv")}" + (q.Count > 0 ? "?" + string.Join('&', q) : "");
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/exams/{examId:guid}/export.json", async (Guid examId, IServiceProvider services, CancellationToken ct) =>
            {
                var svc = ActivatorUtilities.CreateInstance<ExamAdminService>(services);
                if (!svc.CanManage) { await svc.Guard.RequireAsync(Gadfly.Assessment.Permission.ManageAssessments, "Exporting an exam", "Exam", examId.ToString(), ct); return Results.Forbid(); }
                var export = await svc.ExportAsync(examId, ct);
                return export is null ? Results.NotFound()
                    : Results.File(Encoding.UTF8.GetBytes(export.Json), "application/json; charset=utf-8", export.FileName);
            })
            .RequireAuthorization(GadflyPolicies.Instructor);

        app.MapGet("/admin/audit/export.csv", (HttpContext http, CancellationToken ct) => ExportAuditAsync(http, json: false, ct)).RequireAuthorization();
        app.MapGet("/admin/audit/export.json", (HttpContext http, CancellationToken ct) => ExportAuditAsync(http, json: true, ct)).RequireAuthorization();
    }

    private static async Task ExportAuditAsync(HttpContext http, bool json, CancellationToken ct)
    {
        var tenant = TenantSnapshot.Of(http.RequestServices.GetRequiredService<ITenantContext>());
        var query = http.Request.Query;
        var target = query["platform"] == "1" ? new AuditTarget(null)
            : new AuditTarget(Guid.TryParse(query["org"], out var o) ? o : tenant.OrganizationId);
        if (target.OrganizationId is null && !target.IsPlatform || !AuditQueryService.MayRead(tenant, target))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        var filter = ReadFilter(query);
        var scopes = http.RequestServices.GetRequiredService<IServiceScopeFactory>();
        await using var scope = scopes.CreateAsyncScopeFor(AuditQueryService.NeedsSystemScope(tenant, target) ? TenantSnapshot.System : tenant);
        var svc = new AuditQueryService(scope.ServiceProvider.GetRequiredService<GadflyDbContext>(), tenant, http.RequestServices.GetRequiredService<TimeProvider>());
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();
        http.Response.ContentType = json ? "application/json; charset=utf-8" : "text/csv; charset=utf-8";
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{AuditQueryService.ExportFileName(json, clock.GetUtcNow())}\"";
        http.Response.Headers.CacheControl = "no-store";
        await svc.ExportAsync(target, filter, json, http.Response.Body, ct);
    }

    /// <summary>The filter from an export address (the same names <see cref="AuditExportPath"/> writes).</summary>
    public static AuditFilter ReadFilter(IQueryCollection query) => new()
    {
        ActorUserId = Guid.TryParse(query["actor"], out var actor) ? actor : null,
        Action = string.IsNullOrWhiteSpace(query["action"]) ? null : query["action"].ToString(),
        StudentUserId = Guid.TryParse(query["student"], out var student) ? student : null,
        ExamId = Guid.TryParse(query["exam"], out var exam) ? exam : null,
        SessionId = Guid.TryParse(query["session"], out var session) ? session : null,
        From = DateTimeOffset.TryParse(query["from"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var from) ? from : null,
        To = DateTimeOffset.TryParse(query["to"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var to) ? to : null,
        Search = string.IsNullOrWhiteSpace(query["q"]) ? null : query["q"].ToString(),
    };
}
