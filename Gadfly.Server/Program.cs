using Gadfly.Server;
using Gadfly.Server.Components;
using Gadfly.Server.Data;
using Gadfly.Server.Desk;
using Gadfly.Server.Analytics;
using Gadfly.Server.Verification;
using Gadfly.Server.Assessments;
using Gadfly.Server.Grading;
using Gadfly.Server.Jobs;
using Gadfly.Server.Platform;
using Gadfly.Server.Privacy;
using Gadfly.Server.Sessions;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Voice;
using Gadfly.Socrates;
using Gadfly.Socrates.Mock;
using Gadfly.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MindAttic.Authentication.Services;
using MindAttic.Authentication.Web;
using MindAttic.Log.Extensions;
using MindAttic.Vault.Configuration;

// Gadfly.Server — the hosted Socratic platform and the Gadfly Exams web app (docs/SOCRATIC_PLATFORM.md).
var builder = WebApplication.CreateBuilder(args);
// On Azure App Service, application logs go to LogFiles/Application and the log stream (no-op elsewhere).
builder.Logging.AddAzureWebAppDiagnostics();
// The provider's own defaults (10MB per file, 2 files retained) cap out fast once a live exam is running and then look
// like logging "stopped" (GAD-T411, a recurrence of the GAD-T225 go-live logging gap): a bigger, longer-rolling budget
// keeps it flowing. MindAttic.Log's own SQLite log (GAD-T62) has no such cap; this only widens the Azure file mirror.
builder.Services.Configure<Microsoft.Extensions.Logging.AzureAppServices.AzureFileLoggerOptions>(o =>
{
    o.FileSizeLimit = 50 * 1024 * 1024;
    o.RetainedFileCountLimit = 10;
});

// Secrets: on a dev machine MindAttic.Vault's files (the shared LLM provider keys Legion reads, the
// Security bucket the auth stack needs: pepper, bootstrap-token); in Azure, App Service settings / Key Vault.
builder.Configuration.AddMindAtticVaultFiles(o => o.Buckets = new[] { "LLM", "Security", "Notifications" });
// Legion reads its provider keys through this: App Service settings (Key Vault references) first, the vault files second.
// Without it a deployed app sees no LLM keys and the live panel finds nothing (GAD-T225).
MindAttic.Legion.MindAtticCredentialStore.UseConfiguration(builder.Configuration);

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
// A live page re-syncs from the server once its own dropped connection comes back, rather than carrying on stale (GAD-T306).
builder.Services.AddScoped<CircuitReconnectSignal>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, ReconnectCircuitHandler>());

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
// Organizations, memberships and email invitations come from the library (GAD-T156, M.AUTH-T34/T35): its augmentor stamps
// the same "gadfly:org" claims at sign-in, and staff roles are flagged for MFA enrollment like OrgRoleHandler requires.
builder.Services.AddMindAtticOrganizations<GadflyDbContext>(builder.Configuration, o =>
{
    o.OwnerRole = OrgRoles.Owner;
    o.Roles = [.. OrgRoles.All];
    o.MembershipClaimType = GadflyClaims.Membership;
    o.MfaRequiredRoles = OrgRoleHandler.MfaRequired ? [.. OrgRoles.All.Where(OrgRoles.RequiresMfa)] : [];
});
builder.Services.AddScoped<IOrganizationDirectory, OrganizationDirectory>();
// Plans, seats and the exam allowance (GAD-T309).
builder.Services.AddSingleton<Gadfly.Server.Platform.PlanEnforcement>();
// Optional card checkout (GAD-T372): stays off (PaymentSettings.Available false) unless Stripe keys are configured in Vault.
builder.Services.AddSingleton<Gadfly.Server.Platform.PaymentSettings>();
builder.Services.AddScoped<Gadfly.Server.Platform.CheckoutService>();
builder.Services.AddHttpClient("StripeCheckout");

// Background work (ingestion, concept extraction, reports) on the SQL job table (GAD-T43).
builder.Services.AddGadflyJobs();
builder.Services.AddJobHandler<Gadfly.Server.Notifications.EmailDeliveryJob>();
builder.Services.AddJobHandler<Gadfly.Server.Notifications.ReminderSweepJob>();
builder.Services.AddHostedService<Gadfly.Server.Notifications.ReminderScheduler>();

// The AI panel: live Legion or the clearly labelled mock (Gadfly:AiMode = live | mock | auto) (GAD-T100).
builder.Services.AddGadflyPanel(builder.Configuration, builder.Environment.IsDevelopment());
// How long every outside call on a live exam takes, in gadfly.ApiCallTimings and the log (GAD-T413).
Gadfly.Server.Diagnostics.ApiCallTimingServices.AddApiCallTimings(builder.Services);

// Source material: text extractors (.txt/.md, and PDF/DOCX/EPUB/HTML through Tutor.Ingestion, GAD-T155) and processing jobs.
builder.Services.AddSingleton<IDocumentTextExtractor, PlainTextExtractor>();
builder.Services.AddSingleton<IDocumentTextExtractor, TutorIngestionExtractor>();
builder.Services.AddJobHandler<SourceProcessingJob>();

// Live exam sessions: one engine per active session, persisted after every step (GAD-T44).
builder.Services.AddSingleton<SessionOrchestrator>();
builder.Services.AddScoped<HubIntegrityRecorder>();
builder.Services.AddScoped<Gadfly.Server.Integrity.IntegrityRecorder>();
// A session a student abandons outright (no pause, never comes back) is closed once well past its own deadline (GAD-T306).
// Singleton: it holds no scoped state (it opens its own system and organization scopes), and like the singleton
// SessionOrchestrator it depends on, it can be resolved from the root provider (GAD-T422).
builder.Services.AddSingleton<StaleSessionSweep>();
builder.Services.AddJobHandler<StaleSessionJob>();
builder.Services.AddHostedService<StaleSessionScheduler>();
builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 256 * 1024);
// Voice exam room (GAD-T53): speech recognition relay and the examiner avatar, behind Gadfly:Voice:Mode (mock | live | off).
builder.AddVoiceExam();
// Recognizer latency and unsure-word rate, in memory, for the status page (GAD-T361).
Gadfly.Server.Diagnostics.VoiceRecognitionTimingsServices.AddVoiceRecognitionTimings(builder.Services);

// Platform API (GAD-T46): organization API keys, webhooks with signed deliveries.
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddSingleton<UsageMeter>();
builder.Services.AddScoped<WebhookService>();
builder.Services.AddJobHandler<WebhookDeliveryJob>();
// Deliveries only reach public https hosts, checked after DNS resolution, with no redirects (GAD-T75).
WebhookTargets.AllowPrivate = builder.Environment.IsDevelopment() && builder.Configuration[WebhookTargets.AllowPrivateSetting] == "true";
builder.Services.AddHttpClient("webhooks", c => c.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(WebhookTargets.CreateHandler);
// LMS connection, LTI 1.3 (GAD-T312): launch, deep linking, roster, grade passback.
Gadfly.Server.Lti.LtiSetup.AddLti(builder.Services);
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(RateLimits.Configure);   // GAD-T78: API by key, hub by user, anonymous posts by IP

// Privacy (GAD-T61): daily retention redaction, per-student export and erasure.
builder.Services.AddScoped<PrivacyService>();
builder.Services.AddScoped<Gadfly.Server.Privacy.LogRetention>();   // logs kept at least six months, apart from transcripts (GAD-T466)
builder.Services.AddJobHandler<RetentionJob>();
builder.Services.AddHostedService<RetentionScheduler>();

// Instructor and student pages' services (GAD-T49, GAD-T50).
builder.Services.AddScoped<SourceLibraryService>();
builder.Services.AddAnalytics();
builder.Services.AddAssessments();
builder.Services.AddGrading();
builder.Services.AddVerification();   // backfills original-response records for older turns (GAD-T86)
builder.Services.AddScoped<Gadfly.Server.Exams.ExamService>();
// The admin desk at /admin (GAD-T246): its Admin-role policy, behind Gadfly:AdminDesk:Enabled.
builder.Services.AddAdminDesk();
// The public site (GAD-T308): landing, pricing, contact form with its spam guard, legal pages.
Gadfly.Server.Site.PublicSite.AddPublicSite(builder.Services);
// The in-app help center at /help (GAD-T292): the guides in docs/guides, embedded at build time and indexed at startup.
Gadfly.Server.Help.HelpEndpoints.AddHelpCenter(builder.Services);

// The Gadfly Exams web app (GAD-T48): Blazor Server.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// Brotli (preferred) then gzip for every compressible response, including over HTTPS: admin pages, the public
// site and the per-feature stylesheets are all plain text that shrinks well, and none of it is per-request secret
// content reflected back to an attacker, so the BREACH-style risk response compression otherwise carries does not
// apply here (GAD-T317). Fingerprinted static assets (below) carry their own precompressed files instead.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});

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

_ = app.Services.GetRequiredService<IPanelInfo>();   // decide live/mock at startup (GAD-T100)
// Demo institution for pilots and the tour (GAD-T103); --seed-demo must be the last argument.
if (args.Contains(Gadfly.Server.Demo.DemoSeeder.SeedArgument)) await Gadfly.Server.Demo.DemoSeeder.RunAsync(app);
// Classroom test accounts: Teacher_A..C, Student_A..Z across their assessments (GAD-T151). --then-exit makes it a one-shot.
if (args.Contains(Gadfly.Server.Demo.ClassroomSeeder.SeedArgument) || app.Configuration["Gadfly:Demo:SeedClassroomOnStartup"] == "true")
{
    // Test data must never stop the site from starting: a failure is logged and the server carries on.
    async Task SeedClassroomAsync()
    {
        try { await Gadfly.Server.Demo.ClassroomSeeder.RunAsync(app); }
        catch (Exception ex) { app.Logger.LogError(ex, "Classroom seeding failed; the server starts without it."); }
    }
    if (args.Contains("--then-exit")) { await SeedClassroomAsync(); return; }
    // As a server, seed once it is listening: 30 Argon2id hashes take minutes on a small App Service plan, longer than
    // its startup time limit, which would otherwise restart the app mid-seed forever (GAD-T157).
    app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(SeedClassroomAsync));
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
app.UseResponseCompression();   // brotli/gzip every compressible response (GAD-T317); must run before static files
app.UseStaticFiles(VoiceServices.StaticFiles());   // adds .glb (the examiner avatar) and .mjs (GAD-T53)
app.MapStaticAssets();   // fingerprinted, precompressed, long-cached URLs for assets referenced via @Assets[...] (GAD-T317)
app.UseMindAtticAuthentication();   // authn + authz + forced steps (change password, enroll MFA) + CSP
app.UseGadflySecurityHeaders();   // CSP and the rest of the security headers for every other page (GAD-T385)
app.UseRateLimiter();
app.UseAntiforgery();

GadflyEndpoints.Map(app);
app.MapVoiceExam();
app.MapGrading();
app.MapGradingActions();   // HTTP grading workflow for integrators and the demo tour (GAD-T105)

app.Run();

/// <summary>Program's partial, so Razor components can name the app assembly.</summary>
public partial class Program;
