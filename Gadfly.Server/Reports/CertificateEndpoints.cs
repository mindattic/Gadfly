using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Transcripts;

namespace Gadfly.Server.Reports;

/// <summary>
/// The completion certificate's one endpoint (GAD-T311/GAD-T379): <c>GET /certificates/{sessionId}/pdf</c>, for the
/// student's own published result or, for staff, any session they may view (the same entitlement check the
/// transcript download uses, <see cref="TranscriptAccess.EntitledAsync"/>). Verification rides on the transcript's own
/// page (<see cref="TranscriptEndpoints.VerifyPath"/>; <see cref="CertificateRegistry"/>), so there is no
/// <c>/certificates/verify</c>.
/// </summary>
public static class CertificateEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/certificates/{sessionId:guid}/pdf", async (Guid sessionId, HttpContext http, GadflyDbContext db, ITenantContext tenant,
                IConfiguration config, GradingService grading, TimeProvider clock, CancellationToken ct) =>
            {
                if (tenant.OrganizationId is not { } org) return Results.Forbid();
                var now = clock.GetUtcNow();
                var entitled = await TranscriptAccess.EntitledAsync(db, tenant, http.User, config, grading, sessionId, now, ct);
                // Not entitled and not found look the same, so a session id from elsewhere reveals nothing (same discipline as the transcript download).
                if (entitled is null) return Results.NotFound();
                var document = await CertificateBuilder.BuildAsync(db, org, sessionId, ct);
                if (document is null)
                    return Results.Json(new ApiEndpoints.ApiError("No certificate is available: the result has not been released to the student yet."),
                        statusCode: StatusCodes.Status409Conflict);
                var stamp = await CertificateRegistry.IssueAsync(db, tenant, document, BaseUrl(http.Request), now, ct);
                var bytes = CertificatePdf.Render(document, stamp);
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                http.Response.Headers["X-Certificate-Code"] = stamp.Code;
                http.Response.Headers["X-Certificate-Sha256"] = stamp.Sha256;
                return Results.File(bytes, "application/pdf", FileStem(document) + ".pdf");
            })
            .RequireAuthorization(GadflyPolicies.Member)
            .ExcludeFromDescription();
    }

    private static string FileStem(CertificateDocument d)
    {
        var raw = $"{d.StudentName}-{d.ExamTitle}-attempt{d.Attempt}-certificate";
        var clean = new string(raw.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        while (clean.Contains("--")) clean = clean.Replace("--", "-");
        return clean.Length == 0 ? d.SessionId.ToString("N") : clean[..Math.Min(clean.Length, 90)];
    }

    private static string BaseUrl(HttpRequest r) => $"{r.Scheme}://{r.Host}{r.PathBase}";
}
