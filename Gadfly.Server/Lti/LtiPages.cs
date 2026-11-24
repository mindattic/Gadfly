using System.Security.Cryptography;
using System.Text.Encodings.Web;

namespace Gadfly.Server.Lti;

/// <summary>
/// The small standalone pages the LTI flow shows (GAD-T312): errors, the exam picker, the self-submitting deep-linking
/// response and the "open the exam" hand-off. They are plain HTML outside the Blazor app because each is a one-shot
/// step inside a platform-driven flow, and each sets its own security headers in place of the app-wide ones
/// (<see cref="Platform.SecurityHeadersMiddleware"/>): an LMS shows the content picker (and may show a launch) inside
/// a frame, so these pages may be framed by an https page, and the deep-linking response posts to the platform, so its
/// <c>form-action</c> names that platform. Scripts run only with a per-response nonce.
/// </summary>
public static class LtiPages
{
    private static readonly HtmlEncoder Html = HtmlEncoder.Default;

    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <summary>Replaces the app-wide CSP and frame headers for this one response.</summary>
    /// <param name="frameAncestors">Who may frame the page, e.g. <c>https:</c> or a platform origin.</param>
    /// <param name="formAction">Where its forms may post (always includes <c>'self'</c>).</param>
    /// <param name="scriptNonce">The nonce its one inline script carries, or null for a script-free page.</param>
    public static void ApplyHeaders(HttpContext http, string frameAncestors = "https:", string? formAction = null, string? scriptNonce = null)
    {
        var csp = "default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:; base-uri 'none'; "
            + $"form-action 'self'{(formAction is { Length: > 0 } f ? " " + f : "")}; frame-ancestors {frameAncestors}"
            + (scriptNonce is null ? "" : $"; script-src 'nonce-{scriptNonce}'");
        http.Response.Headers.ContentSecurityPolicy = csp;
        http.Response.Headers.Remove("X-Frame-Options");
        http.Response.Headers.CacheControl = "no-store";
    }

    /// <summary>The origin (<c>https://host[:port]</c>) of an absolute https URL, or null.</summary>
    public static string? OriginOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u.GetLeftPart(UriPartial.Authority) : null;

    /// <summary>A plain message page. <paramref name="message"/> is text (encoded here); <paramref name="extraHtml"/> is trusted markup.</summary>
    public static IResult Message(HttpContext http, string title, string message, string? extraHtml = null, int status = StatusCodes.Status200OK)
    {
        ApplyHeaders(http);
        return Results.Content(Document(title, $"<h1>{Html.Encode(title)}</h1><p>{Html.Encode(message)}</p>{extraHtml}"),
            "text/html; charset=utf-8", System.Text.Encoding.UTF8, status);
    }

    public static string Encode(string? s) => Html.Encode(s ?? "");

    public static string Document(string title, string body, string? headExtra = null) => $"""
        <!DOCTYPE html><html lang="en"><head><meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" />
        <meta name="robots" content="noindex" /><title>{Html.Encode(title)} · Gadfly Exams</title><style>{Css}</style>{headExtra}</head><body><main>
        {body}
        </main></body></html>
        """;

    public const string Css = """
        :root{color-scheme:light;--bg:#f6f0e4;--card:#fffdf8;--ink:#1b2430;--muted:#4d5866;--line:#dccfb6;--accent:#1f5aa6}
        body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
        main{max-width:640px;margin:32px auto;background:var(--card);padding:28px 32px;border:1px solid var(--line);border-radius:6px;box-shadow:0 1px 4px rgba(60,45,20,.12)}
        @media (max-width:700px){main{margin:0;border-radius:0;padding:20px 16px}}
        h1{font:600 24px/1.25 "Palatino Linotype","Book Antiqua",Palatino,Georgia,serif;margin:0 0 12px}
        a{color:var(--accent)}
        .pick{display:flex;gap:10px;align-items:flex-start;padding:10px 0;border-bottom:1px solid var(--line)}
        .hint{color:var(--muted);font-size:14px}
        .notice{background:#fdf3d8;color:#4a3700;border:1px solid #e4cf8a;padding:12px 14px;border-radius:6px}
        button{margin-top:18px;font:600 16px system-ui,sans-serif;padding:9px 20px;border-radius:6px;border:0;background:var(--accent);color:#fff;cursor:pointer}
        """;
}
