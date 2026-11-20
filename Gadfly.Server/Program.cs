using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Sessions;
using Gadfly.Server.Sources;
using Gadfly.Socrates;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;
using MindAttic.Log.Extensions;
using MindAttic.Vault.Configuration;

// Gadfly.Server — the hosted Socratic platform (docs/SOCRATIC_PLATFORM.md, GAD-T39).
var builder = WebApplication.CreateBuilder(args);

// Secrets: on a dev machine MindAttic.Vault's files (the shared LLM provider keys Legion reads, the
// Security bucket the auth stack will need); in Azure, App Service settings / Key Vault references.
builder.Configuration.AddMindAtticVaultFiles(o => o.Buckets = new[] { "LLM", "Security" });

// Durable structured logs in MindAttic.Log's per-app database (GAD-T62); ILogger<T> call sites unchanged.
builder.Services.AddMindAtticLog(o =>
{
    o.Application = "Gadfly.Server";
    o.DbPath = MindAttic.Vault.Paths.VaultPaths.LoggingDbPath("Gadfly.Server");
});

var connectionString = GadflyDatabase.Resolve(builder.Configuration);
builder.Services.AddDbContext<GadflyDbContext>(o => GadflyDatabase.Configure(o, connectionString));
builder.Services.AddHealthChecks().AddDbContextCheck<GadflyDbContext>("database");

// Background work (ingestion, concept extraction, reports) on the SQL job table (GAD-T43).
builder.Services.AddGadflyJobs();

// The Legion panel every question, judgment, concept map and rubric goes through.
builder.Services.AddSingleton<LegionVotingClient>(sp =>
{
    var client = new LegionVotingClient(Gadfly.GadflyConfiguration.Build());
    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Gadfly.Panel");
    client.Health.BecameFailing += s => log.LogError(
        "LLM provider {Provider} has failed {Count} calls in a row; the panel is running without it. Last error: {Error}",
        s.ProviderId, s.ConsecutiveFailures, s.LastError);
    return client;
});
builder.Services.AddSingleton<ISocraticVotingClient>(sp => sp.GetRequiredService<LegionVotingClient>());

// Source material: text extractors (PDF/DOCX/EPUB/HTML join with Tutor.Ingestion) and processing jobs.
builder.Services.AddSingleton<IDocumentTextExtractor, PlainTextExtractor>();
builder.Services.AddJobHandler<SourceProcessingJob>();

// Live exam sessions: one engine per active session, persisted after every step (GAD-T44).
builder.Services.AddSingleton<SessionOrchestrator>();

var app = builder.Build();

// Development convenience: bring LocalDB up to the latest migration on start. Production applies
// migrations as a deploy step (GAD-T63), never from a running web instance.
if (app.Environment.IsDevelopment() || args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<GadflyDbContext>().Database.MigrateAsync();
}

app.MapHealthChecks("/healthz");
app.MapGet("/healthz/providers", (LegionVotingClient panel) => Results.Json(panel.Health.Snapshot()));
app.MapGet("/", () => Results.Text("Gadfly.Server is running. See /healthz."));

app.Run();
