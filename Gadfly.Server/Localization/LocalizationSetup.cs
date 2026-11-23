using Microsoft.AspNetCore.Localization;

namespace Gadfly.Server.Localization;

/// <summary>
/// UI localization wiring (GAD-T543, docs/LOCALIZATION.md). A request's culture comes from the language cookie the language
/// switch writes, then a <c>?culture=</c> query value; never from the browser's Accept-Language, so a visitor who has not
/// chosen a language keeps today's English experience. The Blazor circuit is opened by a request carrying the same cookie, so
/// it starts in the same language. Dates and numbers follow the language (en-US, ko-KR).
/// </summary>
public static class LocalizationSetup
{
    /// <summary>The cookie the language switch writes: ASP.NET Core's own culture cookie ("c=ko|uic=ko").</summary>
    public static string CookieName => CookieRequestCultureProvider.DefaultCookieName;

    /// <summary>The route of the string dictionary scripts fetch (the exam room, GAD-T544).</summary>
    public const string StringsRoute = "/ui-strings/{language}";

    public static IServiceCollection AddGadflyLocalization(this IServiceCollection services)
    {
        services.AddLocalization();
        services.AddSingleton<UiCatalog>();
        services.AddScoped<UiText>();
        return services;
    }

    public static IApplicationBuilder UseGadflyLocalization(this IApplicationBuilder app)
    {
        var cultures = UiCatalog.Languages.Select(UiCatalog.CultureFor).ToList();
        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(UiCatalog.CultureFor(UiCatalog.English)),
            SupportedCultures = cultures,
            SupportedUICultures = cultures,
            FallBackToParentCultures = true,
            FallBackToParentUICultures = true,
        };
        // Cookie first, then the query string; Accept-Language is deliberately left out (English stays the default).
        options.RequestCultureProviders =
        [
            new CookieRequestCultureProvider(),
            new QueryStringRequestCultureProvider(),
        ];
        return app.UseRequestLocalization(options);
    }

    /// <summary>The cookie value for a UI language ("c=ko-KR|uic=ko-KR").</summary>
    public static string CookieValue(string language)
    {
        var name = UiCatalog.CultureFor(language).Name;
        return CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(name, name));
    }

    /// <summary>The route the public site's language links use (GAD-T592).</summary>
    public const string ChooseRoute = "/language/{language}";

    /// <summary>
    /// GET /language/{language}?returnUrl=/pricing remembers the visitor's UI language in the culture cookie (a year) and goes back
    /// to the page they were on. The public site renders without a circuit or script, so its English / 한국어 links need this
    /// instead of the interactive language switch. Anonymous; only a local return address is followed (anything else goes home).
    /// </summary>
    public static void MapLanguageChoice(this IEndpointRouteBuilder app) =>
        app.MapGet(ChooseRoute, (string language, string? returnUrl, HttpContext http) =>
        {
            var lang = UiCatalog.Normalize(language);
            http.Response.Cookies.Append(CookieName, CookieValue(lang), new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Lax,
                Secure = http.Request.IsHttps,
                IsEssential = true,
            });
            var local = !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/'
                && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'));
            return Results.LocalRedirect(local ? returnUrl! : "/");
        }).AllowAnonymous();

    /// <summary>
    /// GET /ui-strings/{language}?prefix=examRoom. returns the UI strings under a prefix in one language, English filling untranslated
    /// keys, as a flat JSON object. Signed-in users only (the default-deny fallback policy); the strings are not secret, but
    /// nothing outside the app needs them.
    /// </summary>
    public static void MapUiStrings(this IEndpointRouteBuilder app) =>
        app.MapGet(StringsRoute, (string language, string? prefix, UiCatalog catalog, HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "private, max-age=300";
            return Results.Json(catalog.Dictionary(language, prefix ?? ""));
        });
}
