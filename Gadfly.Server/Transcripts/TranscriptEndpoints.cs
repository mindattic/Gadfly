using System.Text;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace Gadfly.Server.Transcripts;

/// <summary>
/// The transcript maker's endpoints (GAD-T295).
/// <list type="bullet">
/// <item><c>GET /transcripts/{sessionId}/{format}</c>: signed-in members; the copy is chosen on the server
/// (<see cref="TranscriptAccess"/>), <c>?copy=</c> may ask for a lesser one. Every download is audited and registered.</item>
/// <item><c>GET|POST /transcripts/verify</c>: anyone, rate-limited per address; says whether a code or an uploaded JSON
/// download matches Gadfly's record, and nothing about what the transcript says.</item>
/// <item><c>GET /api/v1/sessions/{id}/transcript</c>: the platform API (mapped from <see cref="ApiEndpoints"/>).</item>
/// </list>
/// </summary>
public static class TranscriptEndpoints
{
    public const string VerifyPath = "/transcripts/verify";
    private const long MaxUploadBytes = 4 * 1024 * 1024;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/transcripts/{sessionId:guid}/{format}", async (Guid sessionId, string format, string? copy, HttpContext http, GadflyDbContext db,
                ITenantContext tenant, IConfiguration config, GradingService grading, TimeProvider clock, CancellationToken ct) =>
            {
                if (TranscriptFormats.Parse(format) is not { } f) return Results.NotFound();
                if (tenant.OrganizationId is not { } org) return Results.Forbid();
                var now = clock.GetUtcNow();
                var entitled = await TranscriptAccess.EntitledAsync(db, tenant, http.User, config, grading, sessionId, now, ct);
                // Not entitled and not found look the same, so a session id from elsewhere reveals nothing.
                if (entitled is null) return Results.NotFound();
                var audience = TranscriptAudiences.AtMost(entitled.Value, TranscriptAudiences.Parse(copy));
                // GAD-T480: a grader's copy names the session code while anonymous marking hides the student's name.
                var anonymous = await TranscriptAccess.HidesStudentAsync(db, tenant, sessionId, audience, now, ct);
                var document = await new TranscriptBuilder(db).BuildAsync(org, sessionId, audience, ct, anonymous);
                if (document is null) return Results.NotFound();
                var stamp = await TranscriptRegistry.IssueAsync(db, tenant, document, f, BaseUrl(http.Request), now, ct, anonymous);
                return File(http, f, document, stamp);
            })
            .RequireAuthorization(GadflyPolicies.Member)
            .ExcludeFromDescription();

        app.MapGet(VerifyPath, async (string? code, IServiceScopeFactory scopes, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(code)) return Page(null, null);
                await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.System);
                var outcome = await VerifyCodeEitherAsync(scope.ServiceProvider.GetRequiredService<GadflyDbContext>(), code, ct);
                return Page(outcome, code);
            })
            .AllowAnonymous().RequireRateLimiting(RateLimits.Verify).ExcludeFromDescription();

        app.MapPost(VerifyPath, async (HttpRequest request, IServiceScopeFactory scopes, CancellationToken ct) =>
            {
                if (!request.HasFormContentType) return Page(new TranscriptRegistry.Outcome(TranscriptRegistry.Verdict.Unreadable), null);
                if (request.ContentLength > MaxUploadBytes + 64 * 1024) return Page(null, null, "That file is too large to be a transcript download (the limit is 4 MB).");
                var form = await request.ReadFormAsync(ct);
                await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.System);
                var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                if (form.Files.GetFile("file") is { Length: > 0 } file)
                {
                    if (file.Length > MaxUploadBytes) return Page(null, null, "That file is too large to be a transcript download (the limit is 4 MB).");
                    await using var stream = file.OpenReadStream();
                    return Page(await TranscriptRegistry.VerifyFileAsync(db, stream, ct), null, uploaded: file.FileName);
                }
                var typed = form["code"].ToString();
                if (string.IsNullOrWhiteSpace(typed)) return Page(null, null, "Type the verification code printed on the transcript or certificate, or choose the transcript's JSON file.");
                return Page(await VerifyCodeEitherAsync(db, typed, ct), typed);
            })
            .AllowAnonymous().DisableAntiforgery().RequireRateLimiting(RateLimits.Verify).ExcludeFromDescription();
    }

    /// <summary>
    /// A typed code may name a transcript or a certificate (GAD-T311): tries the transcript registry first (by far the
    /// more common download), and only asks <see cref="Reports.CertificateRegistry"/> when that comes back not-found, so
    /// one shared verify page needs no separate <c>/certificates/verify</c>.
    /// </summary>
    private static async Task<TranscriptRegistry.Outcome> VerifyCodeEitherAsync(GadflyDbContext db, string? typed, CancellationToken ct)
    {
        var outcome = await TranscriptRegistry.VerifyCodeAsync(db, typed, ct);
        return outcome.Verdict == TranscriptRegistry.Verdict.NotFound ? await Reports.CertificateRegistry.VerifyCodeAsync(db, typed, ct) : outcome;
    }

    /// <summary>The platform API's transcript call, on the <c>/api/v1</c> group (API key, rate limits and tenant already applied).</summary>
    public static void MapApi(RouteGroupBuilder api)
    {
        api.MapGet("/sessions/{id:guid}/transcript", async (Guid id, string? format, string? audience, HttpContext http, GadflyDbContext db,
                ITenantContext tenant, TimeProvider clock, CancellationToken ct) =>
            {
                if (tenant.OrganizationId is not { } org) return Results.Forbid();
                var f = format is null ? TranscriptFormat.Json : TranscriptFormats.Parse(format);
                if (f is null) return Results.Json(new ApiEndpoints.ApiError("format must be one of json, pdf, docx, html, txt, vtt, srt."), statusCode: 400);
                var a = audience is null ? TranscriptAudience.Reviewer : TranscriptAudiences.Parse(audience);
                if (a is null) return Results.Json(new ApiEndpoints.ApiError("audience must be reviewer, blind-grader or student."), statusCode: 400);
                if (a == TranscriptAudience.Student && !await TranscriptAccess.PublishedToStudentAsync(db, org, id, ct))
                    return Results.Json(new ApiEndpoints.ApiError("The student copy is available once the result has been published to the student."), statusCode: 409);
                var document = await new TranscriptBuilder(db).BuildAsync(org, id, a.Value, ct);
                if (document is null) return Results.NotFound();
                var now = clock.GetUtcNow();
                var stamp = await TranscriptRegistry.IssueAsync(db, tenant, document, f.Value, BaseUrl(http.Request), now, ct);
                return File(http, f.Value, document, stamp);
            })
            .WithSummary("A session's official transcript")
            .WithDescription("The transcript built from the response ledger, with its SHA-256 and verification code. format: json (default), pdf, docx, html, txt, vtt, srt. "
                + "audience: reviewer (default), blind-grader or student (only once the result is published).")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).Produces<ApiEndpoints.ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiEndpoints.ApiError>(StatusCodes.Status409Conflict);
    }

    private static IResult File(HttpContext http, TranscriptFormat f, TranscriptDocument document, TranscriptStamp stamp)
    {
        var bytes = TranscriptFormats.Render(f, document, stamp);
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["X-Transcript-Code"] = stamp.Code;
        http.Response.Headers["X-Transcript-Sha256"] = stamp.Sha256;
        if (f == TranscriptFormat.Html)
        {
            // Opened in a browser tab to read or print (Ctrl+P saves it as PDF too); "Save as" keeps the file name.
            http.Response.Headers.ContentSecurityPolicy = TranscriptHtml.ContentSecurityPolicy;
            http.Response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition { Inline = true, FileName = TranscriptFormats.FileStem(document) + ".html" }.ToString();
            return Results.Bytes(bytes, TranscriptFormats.ContentType(f));
        }
        return Results.File(bytes, TranscriptFormats.ContentType(f), TranscriptFormats.FileStem(document) + "." + TranscriptFormats.Extension(f));
    }

    private static string BaseUrl(HttpRequest r) => $"{r.Scheme}://{r.Host}{r.PathBase}";

    // ------------------------------------------------------------------ the verify page

    private static IResult Page(TranscriptRegistry.Outcome? outcome, string? typed, string? problem = null, string? uploaded = null)
    {
        var e = TranscriptHtml.E;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />")
          .Append("<meta name=\"robots\" content=\"noindex\" /><title>Check a transcript or certificate · Gadfly Exams</title><style>").Append(VerifyCss).Append("</style></head><body><main>")
          .Append("<h1>Check a Gadfly transcript or certificate</h1>")
          .Append("<p>Every transcript and completion certificate Gadfly issues carries a verification code (for example <code>7K3M-9QXA-B2CD-EFGH</code>) and a SHA-256 hash. ")
          .Append("Type the code, or choose a transcript's JSON download, to see whether it matches Gadfly's record. ")
          .Append("This page never shows what a transcript says or a certificate's exam: only whether the copy you hold is genuine and unchanged.</p>");

        if (problem is not null) sb.Append("<p class=\"result bad\" role=\"alert\">").Append(e(problem)).Append("</p>");
        if (outcome is not null) sb.Append(Result(outcome, uploaded));

        sb.Append("<form method=\"post\" action=\"").Append(VerifyPath).Append("\" enctype=\"multipart/form-data\">")
          .Append("<label for=\"code\">Verification code</label>")
          .Append("<input id=\"code\" name=\"code\" autocomplete=\"off\" spellcheck=\"false\" maxlength=\"40\" placeholder=\"XXXX-XXXX-XXXX-XXXX\" value=\"")
          .Append(e(typed ?? "")).Append("\" />")
          .Append("<p class=\"or\">or</p>")
          .Append("<label for=\"file\">The transcript's JSON file</label><input id=\"file\" name=\"file\" type=\"file\" accept=\".json,application/json\" />")
          .Append("<button type=\"submit\">Check</button></form>")
          .Append("<p class=\"hint\">A PDF, Word or printed copy is checked by its code. The JSON file is checked by its content, so even a re-saved or re-indented file verifies, and any changed word does not.</p>")
          .Append("</main></body></html>");
        var result = Results.Content(sb.ToString(), "text/html; charset=utf-8", Encoding.UTF8);
        return new VerifyPageResult(result);
    }

    private static string Result(TranscriptRegistry.Outcome o, string? uploaded)
    {
        var e = TranscriptHtml.E;
        var noun = o.Kind;   // "transcript" or "certificate" (GAD-T311)
        var issued = o.FirstIssuedAt is { } at ? $" It was first issued on {TranscriptLayout.Stamp(at)}" : "";
        var copy = o.Audience is { } a ? $" as the {TranscriptLayout.CopyLabel(a).ToLowerInvariant()}" : "";
        var what = uploaded is { Length: > 0 } ? $"The file {e(Path.GetFileName(uploaded))}" : o.Code is { } c ? $"The code {e(c)}" : "That";
        return o.Verdict switch
        {
            TranscriptRegistry.Verdict.Matches =>
                $"<p class=\"result ok\" role=\"status\"><strong>Genuine and unchanged.</strong> {what} matches a {noun} Gadfly issued{e(copy)}, and Gadfly's record still says exactly the same.{e(issued)}.</p>",
            TranscriptRegistry.Verdict.RecordChangedSince =>
                $"<p class=\"result warn\" role=\"status\"><strong>Genuine, but the record has changed since.</strong> {what} matches a {noun} Gadfly issued{e(copy)}.{e(issued)}. "
                + "The record has been updated since then (for example a later correction, or words removed under the organization's retention rules), so a new download would differ.</p>",
            TranscriptRegistry.Verdict.NoLongerHeld =>
                $"<p class=\"result warn\" role=\"status\"><strong>Issued, but no longer held.</strong> {what} matches a {noun} Gadfly issued{e(copy)}.{e(issued)}. "
                + "The exam session has since been deleted, so it can no longer be compared with the record.</p>",
            TranscriptRegistry.Verdict.FileAltered =>
                $"<p class=\"result bad\" role=\"alert\"><strong>Changed after it was issued.</strong> {what} names a code Gadfly issued{e(copy)}, but its content is not what was issued: something in it was edited.</p>",
            TranscriptRegistry.Verdict.NotFound =>
                $"<p class=\"result bad\" role=\"alert\"><strong>Not on record.</strong> Gadfly has not issued a transcript or certificate with {(uploaded is { Length: > 0 } ? "this content" : "this code")}. Check the code was typed correctly.</p>",
            _ => "<p class=\"result bad\" role=\"alert\"><strong>Not readable.</strong> That is not a Gadfly verification code (16 letters and digits) or a transcript JSON download.</p>",
        };
    }

    /// <summary>The verify page with its own headers: no caching, no framing, no scripts.</summary>
    private sealed class VerifyPageResult(IResult inner) : IResult
    {
        public Task ExecuteAsync(HttpContext http)
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
            return inner.ExecuteAsync(http);
        }
    }

    private const string VerifyCss = """
        :root{color-scheme:light dark;--bg:#f4f3ef;--card:#fff;--ink:#1d1d1f;--muted:#555;--line:#d8d6cf}
        @media (prefers-color-scheme:dark){:root{--bg:#18181a;--card:#232326;--ink:#ececec;--muted:#aaa;--line:#3a3a3e}}
        body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
        main{max-width:640px;margin:32px auto;background:var(--card);padding:28px 32px;border-radius:10px;box-shadow:0 1px 4px rgba(0,0,0,.12)}
        @media (max-width:700px){main{margin:0;border-radius:0;padding:20px 16px}}
        h1{font-size:24px;margin:0 0 12px}
        label{display:block;font-weight:600;margin:14px 0 4px}
        input:not([type=file]){width:100%;box-sizing:border-box;font:18px ui-monospace,Consolas,monospace;padding:8px 10px;border:1px solid var(--line);border-radius:6px;background:transparent;color:inherit;letter-spacing:.06em}
        button{margin-top:16px;font:600 16px system-ui,sans-serif;padding:9px 20px;border-radius:6px;border:0;background:#2f5d50;color:#fff;cursor:pointer}
        .or{margin:10px 0 0;color:var(--muted)}
        .hint{color:var(--muted);font-size:14px;margin-top:20px}
        code{font-family:ui-monospace,Consolas,monospace}
        .result{padding:12px 14px;border-radius:6px;margin:16px 0}
        .ok{background:#e3f2e8;color:#14361f;border:1px solid #9cc9aa}
        .warn{background:#fdf3d8;color:#4a3700;border:1px solid #e4cf8a}
        .bad{background:#fbe4e2;color:#5a1610;border:1px solid #e3a59f}
        """;
}
