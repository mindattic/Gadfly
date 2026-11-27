using Gadfly.Server;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Worker;

// Gadfly.Worker — the job queue, AI grading and the sweepers that feed them, outside the web process (GAD-T700). Runs beside
// the site with the same settings (environment variables and its own copy of the site's appsettings.json) and database
// (docs/DEPLOY.md "The worker").
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    // The site sets ASPNETCORE_ENVIRONMENT; a generic host only reads DOTNET_ENVIRONMENT on its own.
    EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
});
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "yyyy-MM-dd HH:mm:ss "; });
builder.AddGadflyServices(runBackgroundWork: true);
builder.Services.AddHostedService<ShutdownFileWatcher>();
// Time for a running job to release its lease before a host (a service manager, or a WebJob-style shutdown file) kills it.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(50));

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gadfly.Worker");

// Every handler built once up front: a missing registration stops the worker here, not on its first job.
string[] kinds;
using (var scope = host.Services.GetRequiredService<IServiceScopeFactory>().CreateSystemScope())
    kinds = scope.ServiceProvider.GetServices<IJobHandler>().Select(h => h.Kind).Order().ToArray();
var panel = host.Services.GetRequiredService<IPanelInfo>();
var database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(GadflyDatabase.Resolve(builder.Configuration));
var background = host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name);
log.LogInformation("Gadfly.Worker {Version} {Commit} started: environment {Environment}, database {Database} on {Server}, AI panel {Mode} ({Reason}), " +
    "job kinds [{Kinds}], background services [{Services}], web app runs jobs too: {RunInWeb}.",
    AppVersion.Label, AppVersion.Commit, builder.Environment.EnvironmentName, database.InitialCatalog, database.DataSource, panel.Mode, panel.Reason,
    string.Join(", ", kinds), string.Join(", ", background), builder.Configuration.GetValue(GadflyServices.RunJobsInWebSetting, true));

await host.RunAsync();
log.LogInformation("Gadfly.Worker stopped.");
