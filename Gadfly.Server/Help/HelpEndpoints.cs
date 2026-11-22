using Microsoft.AspNetCore.StaticFiles;

namespace Gadfly.Server.Help;

/// <summary>Registration of the in-app help center (GAD-T292).</summary>
public static class HelpEndpoints
{
    /// <summary>The help routes a signed-out visitor may reach (on <see cref="GadflyEndpoints.AnonymousRoutes"/>).</summary>
    public static readonly IReadOnlyList<string> AnonymousRoutes = ["help", "help/search", "help/{Slug}", "help/media/{**path}"];

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>The guides are read, rendered and indexed once, while the app starts, not on the first visit to /help.</summary>
    public static IServiceCollection AddHelpCenter(this IServiceCollection services) => services.AddSingleton(new HelpLibrary());

    /// <summary>Images and other files the guides refer to, served from the copies embedded in the app.</summary>
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/help/media/{**path}", (string path, HelpLibrary help, HttpContext http) =>
            {
                var stream = help.OpenMedia(path);
                if (stream is null) return Results.NotFound();
                if (!ContentTypes.TryGetContentType(path, out var type)) type = "application/octet-stream";
                http.Response.Headers.CacheControl = "public, max-age=3600";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.Stream(stream, type);
            })
            .AllowAnonymous().ExcludeFromDescription();
    }
}
