using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using MindAttic.Authentication.Web;

namespace Gadfly.Server.Platform;

/// <summary>
/// Security headers for Gadfly's own pages (GAD-T385). MindAtticAuthAppExtensions.UseMindAtticAuthentication already
/// sends a strict, nonce-based Content-Security-Policy on the auth surface (/login, /mfa, /account, /_ma-auth) and
/// deliberately stops there so it never clobbers a host app's pages. This fills in the rest of Gadfly: the admin
/// console, the student and instructor pages, the public site and the voice exam room, none of which sent a CSP (or
/// any of the other headers below) before this ticket.
/// </summary>
/// <remarks>
/// The policy is looser than the auth surface's because the exam room genuinely needs it (docs/VOICE.md's own note):
/// <c>wasm-unsafe-eval</c> for the self-hosted MediaPipe webcam model, <c>worker-src blob:</c> for the microphone
/// worklet and that same model's internal worker, and <c>connect-src wss:</c> for the SignalR hubs (voice relay,
/// session hub) and the speech/avatar services. <c>style-src 'unsafe-inline'</c> covers the handful of literal
/// <c>style="…"</c> attributes in the markup (Blazor has no practical way to nonce those); nothing here reaches
/// script-src, which stays nonce-only. Nothing is loaded from a CDN (GAD-T53, GAD-T57): default-src 'self' covers it.
/// </remarks>
public static class SecurityHeadersMiddleware
{
    private const string Policy =
        "default-src 'self'; " +
        // blob: in script-src: an AudioWorklet module is checked against script-src, not worker-src, and the microphone
        // capture loads its worklet from a blob: URL, so without it the exam room's microphone never opens (GAD-T493).
        "script-src 'self' 'wasm-unsafe-eval' blob: 'nonce-{0}'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self'; " +
        "media-src 'self' blob:; " +
        // {1}: the examiner avatar's storage origin when Gadfly:Voice:AvatarModelUrl points off-site (GAD-T493). blob: is the
        // model read back from Cache Storage (model-cache.js, GAD-T698), which the 3D loader fetches.
        "connect-src 'self' wss: blob:{1}; " +
        "worker-src 'self' blob:; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    private const string PermissionsPolicy =
        "camera=(self), microphone=(self), window-management=(self), " +
        "geolocation=(), payment=(), usb=(), bluetooth=(), interest-cohort=()";

    /// <summary>" https://host" for an absolute off-site avatar model URL, else empty, for connect-src.</summary>
    internal static string AvatarOrigin(string? avatarModelUrl) =>
        Uri.TryCreate(avatarModelUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
            ? " " + u.GetLeftPart(UriPartial.Authority) : "";

    public static IApplicationBuilder UseGadflySecurityHeaders(this IApplicationBuilder app)
    {
        var config = app.ApplicationServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var avatarOrigin = AvatarOrigin(config["Gadfly:Voice:AvatarModelUrl"]);
        return app.Use(async (ctx, next) =>
    {
        // The auth surface already set its own stricter CSP (with its own nonce) before reaching here; never
        // overwrite it. Every other path gets this one, with a nonce the Razor host page (App.razor) also reads
        // through the same ctx.Items key, so its inline scripts stay nonce-matched wherever they render.
        if (!ctx.Response.Headers.ContainsKey("Content-Security-Policy"))
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            ctx.Items[MindAtticAuthAppExtensions.CspNonceItem] = nonce;
            ctx.Response.Headers["Content-Security-Policy"] = string.Format(Policy, nonce, avatarOrigin);
        }
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        ctx.Response.Headers["Permissions-Policy"] = PermissionsPolicy;
        ctx.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        await next();
    });
    }
}
