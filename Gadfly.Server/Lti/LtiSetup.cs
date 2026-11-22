using Gadfly.Server.Jobs;
using Gadfly.Server.Platform;

namespace Gadfly.Server.Lti;

/// <summary>
/// Registration of the LTI 1.3 integration (GAD-T312). Program.cs: <c>builder.Services.AddLti();</c>;
/// GadflyEndpoints: <c>app.MapLti();</c>.
/// </summary>
public static class LtiSetup
{
    /// <summary>The outgoing client for every call to a platform (its key set, token endpoint, roster and gradebook). Same
    /// guard as webhooks (GAD-T75): public hosts only, checked after DNS resolution, no redirects, so an LMS connection's
    /// addresses can't be used to reach Gadfly's own network.</summary>
    public const string HttpClientName = "lti";

    public static IServiceCollection AddLti(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient(HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(20);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("GadflyExams-LTI/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(WebhookTargets.CreateHandler);
        services.AddScoped<LtiKeys>();
        services.AddScoped<LtiPlatformKeys>();
        services.AddScoped<LtiTokenClient>();
        services.AddScoped<LtiLaunchService>();
        services.AddScoped<LtiUserProvisioner>();
        services.AddScoped<LtiDeepLinking>();
        services.AddScoped<LtiRosterService>();
        services.AddScoped<LtiGradePassbackService>();
        services.AddScoped<LtiAdminService>();
        services.AddScoped<LtiAccountLinks>();
        services.AddHostedService<LtiPassbackSweeper>();
        // GAD-T458: scheduled roster refresh through the job queue.
        services.AddJobHandler<LtiRosterRefreshJob>();
        services.AddHostedService<LtiRosterRefreshScheduler>();
        return services;
    }

    public static IEndpointRouteBuilder MapLti(this IEndpointRouteBuilder app)
    {
        LtiEndpoints.Map(app);
        return app;
    }
}
