using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Sources;
using Gadfly.Socrates;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;
using MindAttic.Vault.Configuration;

// Gadfly.Server — the hosted Socratic platform (docs/SOCRATIC_PLATFORM.md, GAD-T39).
var builder = WebApplication.CreateBuilder(args);

// Secrets: on a dev machine MindAttic.Vault's files (the shared LLM provider keys Legion reads, the
// Security bucket the auth stack will need); in Azure, App Service settings / Key Vault references.
builder.Configuration.AddMindAtticVaultFiles(o => o.Buckets = new[] { "LLM", "Security" });

var connectionString = GadflyDatabase.Resolve(builder.Configuration);
builder.Services.AddDbContext<GadflyDbContext>(o => GadflyDatabase.Configure(o, connectionString));
builder.Services.AddHealthChecks().AddDbContextCheck<GadflyDbContext>("database");

// Background work (ingestion, concept extraction, reports) on the SQL job table (GAD-T43).
builder.Services.AddGadflyJobs();

// The Legion panel every question, judgment, concept map and rubric goes through.
builder.Services.AddSingleton<ISocraticVotingClient>(_ => new LegionVotingClient(Gadfly.GadflyConfiguration.Build()));

// Source material: text extractors (PDF/DOCX/EPUB/HTML join with Tutor.Ingestion) and processing jobs.
builder.Services.AddSingleton<IDocumentTextExtractor, PlainTextExtractor>();
builder.Services.AddJobHandler<SourceProcessingJob>();

var app = builder.Build();

// Development convenience: bring LocalDB up to the latest migration on start. Production applies
// migrations as a deploy step (GAD-T63), never from a running web instance.
if (app.Environment.IsDevelopment() || args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<GadflyDbContext>().Database.MigrateAsync();
}

app.MapHealthChecks("/healthz");
app.MapGet("/", () => Results.Text("Gadfly.Server is running. See /healthz."));

app.Run();
