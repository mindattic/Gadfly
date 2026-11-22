using System.Text;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gadfly.Server.Site;

/// <summary>
/// The public site's wiring (GAD-T308): its services, the signed-out routes it adds to the default-deny allow-list, the
/// robots.txt and sitemap.xml endpoints, and the branded error page the production exception handler shows.
/// </summary>
public static class PublicSite
{
    /// <summary>The public pages, in sitemap order; each is a static page with <c>[AllowAnonymous]</c>.</summary>
    public static readonly IReadOnlyList<string> Pages =
        ["", "how-it-works", "science", "pricing", "contact", "security", "privacy", "terms", "accessibility"];

    /// <summary>Every signed-out route the site adds: its pages, robots and sitemap, and the not-found catch-all.</summary>
    public static readonly IReadOnlyList<string> AnonymousRoutes = [.. Pages, "robots.txt", "sitemap.xml", NotFoundRoute];

    /// <summary>The catch-all page's route pattern (the branded 404), as the endpoint table spells it.</summary>
    public const string NotFoundRoute = "{*path}";

    public static IServiceCollection AddPublicSite(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ContactSpamGuard>();
        services.AddScoped<ContactRequestService>();
        return services;
    }

    public static void MapPublicSite(this WebApplication app)
    {
        app.MapGet("/robots.txt", (HttpContext http) =>
            {
                http.Response.Headers.CacheControl = "public, max-age=3600";
                var b = new StringBuilder();
                b.Append("User-agent: *\n");
                foreach (var area in DisallowedAreas) b.Append("Disallow: /").Append(area).Append('\n');
                b.Append("Allow: /\n\nSitemap: ").Append(BaseUrl(http)).Append("/sitemap.xml\n");
                return Results.Text(b.ToString(), "text/plain; charset=utf-8");
            })
            .AllowAnonymous().ExcludeFromDescription();

        app.MapGet("/sitemap.xml", (HttpContext http) =>
            {
                http.Response.Headers.CacheControl = "public, max-age=3600";
                var root = BaseUrl(http);
                var b = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
                foreach (var page in Pages)
                    b.Append("  <url><loc>").Append(System.Security.SecurityElement.Escape(root + "/" + page)).Append("</loc></url>\n");
                b.Append("</urlset>\n");
                return Results.Text(b.ToString(), "application/xml; charset=utf-8");
            })
            .AllowAnonymous().ExcludeFromDescription();
    }

    /// <summary>App areas crawlers have no reason to visit (they need a sign-in anyway).</summary>
    private static readonly string[] DisallowedAreas =
        ["admin", "account", "api", "analytics", "assessments", "exam", "exams", "grading", "launch", "login", "mfa",
         "my-exams", "my-results", "org", "platform", "privacy/", "review", "sources", "_ma-auth", "_blazor"];

    private static string BaseUrl(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}".TrimEnd('/');

    /// <summary>The page <c>UseExceptionHandler</c> re-executes into: static, data-free, in the site's look.</summary>
    public const string ErrorPageHtml =
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />" +
        "<meta name=\"robots\" content=\"noindex\" /><title>Something went wrong · Gadfly Exams</title>" +
        "<link rel=\"icon\" href=\"/favicon.ico\" sizes=\"any\" /><link rel=\"stylesheet\" href=\"/app.css\" /><link rel=\"stylesheet\" href=\"/site.css\" /></head>" +
        "<body><div class=\"site\"><header class=\"site-head\"><div class=\"bar\"><a class=\"site-brand\" href=\"/\">" +
        "<img class=\"fly\" src=\"/assets/site/logo-192.png\" alt=\"\" width=\"40\" height=\"40\" />Gadfly <span>Exams</span></a></div></header>" +
        "<main class=\"site-main\"><section class=\"lost\"><div class=\"wrap\"><img class=\"fly\" src=\"/assets/site/logo.png\" alt=\"\" width=\"192\" height=\"192\" />" +
        "<div><h1>Something went wrong on our side</h1><p class=\"lede\">Gadfly hit an unexpected problem. Nothing you typed was lost on your device, " +
        "and an exam in progress keeps its place.</p><p>Go back and try again. If it keeps happening, tell your instructor or administrator.</p>" +
        "<p><a class=\"btn\" href=\"/\">Back to Gadfly</a></p></div></div></section></main></div></body></html>";
}
