using Gadfly.Server;
using Gadfly.Server.Components;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Privacy;
using Gadfly.Server.Sessions;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;
using MindAttic.Authentication.Web;
using MindAttic.Log.Extensions;
using MindAttic.Vault.Configuration;

// Gadfly.Server — the hosted Socratic platform and the Gadfly Exams web app (docs/SOCRATIC_PLATFORM.md).
var builder = WebApplication.CreateBuilder(args);

// Secrets: on a dev machine MindAttic.Vault's files (the shared LLM provider keys Legion reads, the
// Security bucket the auth stack needs: pepper, bootstrap-token); in Azure, App Service settings / Key Vault.
builder.Configuration.AddMindAtticVaultFiles(o => o.Buckets = new[] { "LLM", "Security", "Notifications" });

// Durable structured logs in MindAttic.Log's per-app database (GAD-T62); ILogger<T> call sites unchanged.
builder.Services.AddMindAtticLog(o =>
{
    o.Application = "Gadfly.Server";
    o.DbPath = MindAttic.Vault.Paths.VaultPaths.LoggingDbPath("Gadfly.Server");
});

// Who is calling, and in which organization (GAD-T41/T42). Scoped: one per request or circuit.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

// One SQL Server database: Gadfly's "gadfly" schema plus MindAttic.Authentication's "auth" schema.
var connectionString = GadflyDatabase.Resolve(builder.Configuration);
builder.Services.AddDbContext<GadflyDbContext>(o => GadflyDatabase.Configure(o, connectionString));
builder.Services.AddHealthChecks().AddDbContextCheck<GadflyDbContext>("database");

// Accounts (GAD-T41): MindAttic.Authentication (Argon2id, TOTP MFA, lockout, audit) over the same
// database; organization memberships ride on the sign-in cookie; default-deny with Gadfly's role policies.
OrgRoleHandler.MfaRequired = !builder.Environment.IsDevelopment() || builder.Configuration["Gadfly:RequireMfaInDevelopment"] == "true";
builder.Services.AddMindAtticAuthentication<GadflyDbContext>(builder.Configuration, o =>
{
    o.AppName = "Gadfly";
    o.IsProduction = !builder.Environment.IsDevelopment();
    o.ConfigureAdditionalPolicies = TenancyAuthorization.Configure;
    // Production Data Protection (Blob + Key Vault) is wired with the Azure deployment (GAD-T63); until
    // then a production start fails closed, which is what the library intends.
});
builder.Services.AddScoped<IAuthorizationHandler, OrgRoleHandler>();
builder.Services.AddScoped<IOrganizationDirectory, OrganizationDirectory>();
builder.Services.AddScoped<IMaClaimsAugmentor, OrganizationClaimsAugmentor>();

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

// Privacy (GAD-T61): daily retention redaction, per-student export and erasure.
builder.Services.AddScoped<PrivacyService>();
builder.Services.AddJobHandler<RetentionJob>();
builder.Services.AddHostedService<RetentionScheduler>();

// The Gadfly Exams web app (GAD-T48): Blazor Server.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

// Startup: migrate (development; production migrates as a deploy step, GAD-T63), then seed the first
// MindAttic SuperAdmin from Vault Security:bootstrap-token if no account exists yet.
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    if (app.Environment.IsDevelopment() || args.Contains("--migrate"))
        await sp.GetRequiredService<GadflyDbContext>().Database.MigrateAsync();
    await sp.GetRequiredService<AuthBootstrapper>().SeedAdminAsync();
}

var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseMindAtticAuthentication();   // authn + authz + forced steps (change password, enroll MFA) + CSP
app.UseAntiforgery();

GadflyEndpoints.Map(app);

app.Run();

/// <summary>Program's partial, so Razor components can name the app assembly.</summary>
public partial class Program;
