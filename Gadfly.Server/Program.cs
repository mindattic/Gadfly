using Gadfly.Server;
using Gadfly.Server.Components;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Server.Platform;
using Gadfly.Server.Privacy;
using Gadfly.Server.Sessions;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
builder.Services.AddScoped<TenantScope>();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
// A circuit pins its tenant when it opens and never falls back to the (often absent) HttpContext (GAD-T73).
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, TenantCircuitHandler>());

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
    if (o.IsProduction)
    {
        // Production key ring (GAD-T63): persisted in Blob Storage and wrapped by a Key Vault key, reached
        // with the App Service's managed identity. Missing settings stop startup (fail closed).
        o.ConfigureDataProtection = dp =>
        {
            var cred = new Azure.Identity.DefaultAzureCredential();
            var blobUri = builder.Configuration["DataProtection:BlobUri"]
                ?? throw new InvalidOperationException("DataProtection:BlobUri is required in production.");
            var keyId = builder.Configuration["DataProtection:KeyVaultKeyId"]
                ?? throw new InvalidOperationException("DataProtection:KeyVaultKeyId is required in production.");
            dp.PersistKeysToAzureBlobStorage(new Uri(blobUri), cred).ProtectKeysWithAzureKeyVault(new Uri(keyId), cred);
        };
    }
});
builder.Services.AddScoped<IAuthorizationHandler, OrgRoleHandler>();
builder.Services.AddAuthentication().AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationHandler.SchemeName, null);
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
builder.Services.AddScoped<HubIntegrityRecorder>();
builder.Services.AddScoped<Gadfly.Server.Integrity.IntegrityRecorder>();
builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 256 * 1024);

// Platform API (GAD-T46): organization API keys, webhooks with signed deliveries.
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddSingleton<UsageMeter>();
builder.Services.AddScoped<WebhookService>();
builder.Services.AddJobHandler<WebhookDeliveryJob>();
// Deliveries only reach public https hosts, checked after DNS resolution, with no redirects (GAD-T75).
WebhookTargets.AllowPrivate = builder.Environment.IsDevelopment() && builder.Configuration[WebhookTargets.AllowPrivateSetting] == "true";
builder.Services.AddHttpClient("webhooks", c => c.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(WebhookTargets.CreateHandler);
builder.Services.AddOpenApi();

// Privacy (GAD-T61): daily retention redaction, per-student export and erasure.
builder.Services.AddScoped<PrivacyService>();
builder.Services.AddJobHandler<RetentionJob>();
builder.Services.AddHostedService<RetentionScheduler>();

// Instructor and student pages' services (GAD-T49, GAD-T50).
builder.Services.AddScoped<SourceLibraryService>();
builder.Services.AddScoped<Gadfly.Server.Exams.ExamService>();

// The Gadfly Exams web app (GAD-T48): Blazor Server.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

// Startup: migrate (development; production migrates as a deploy step, GAD-T63), then seed the first
// MindAttic SuperAdmin from Vault Security:bootstrap-token if no account exists yet.
using (var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateSystemScope())
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
