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
// The services the grading worker shares (GadflyServices, GAD-T700). Gadfly:Jobs:RunInWeb=false leaves the job queue and its
// sweepers to Gadfly.Worker, the WebJob beside the site, and this process only enqueues (docs/DEPLOY.md "The worker").
builder.AddGadflyServices(runBackgroundWork: builder.Configuration.GetValue(GadflyServices.RunJobsInWebSetting, true));

// A circuit pins its tenant when it opens and never falls back to the (often absent) HttpContext (GAD-T73).
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, TenantCircuitHandler>());
// A live page re-syncs from the server once its own dropped connection comes back, rather than carrying on stale (GAD-T306).
builder.Services.AddScoped<CircuitReconnectSignal>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, ReconnectCircuitHandler>());

builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 256 * 1024);
// On-demand hosting (GAD-T775): the site stops itself after Gadfly:IdleShutdown:Minutes with no user and no pending work.
builder.AddIdleShutdown();
// Voice exam room (GAD-T53): speech recognition relay and the examiner avatar, behind Gadfly:Voice:Mode (mock | live | off).
builder.AddVoiceExam();
// Recognizer latency and unsure-word rate, in memory, for the status page (GAD-T361).
Gadfly.Server.Diagnostics.VoiceRecognitionTimingsServices.AddVoiceRecognitionTimings(builder.Services);
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(RateLimits.Configure);   // GAD-T78: API by key, hub by user, anonymous posts by IP

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
    // The first database touch of a cold start: a serverless Azure SQL database that auto-paused takes up to a
    // minute to resume and resets or drops the first logins (SqlException 35 on Linux), which EF's short
    // per-command retry does not outlast. Retry the whole step so the host waits for the resume instead of
    // crashing and being restarted by the platform (GAD-T787).
    for (var attempt = 1; ; attempt++)
    {
        try { await sp.GetRequiredService<AuthBootstrapper>().SeedAdminAsync(); break; }
        catch (Exception ex) when (attempt < 8 && ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException or TimeoutException)
        {
            app.Logger.LogWarning(ex, "Database not ready at startup (attempt {Attempt} of 8); retrying in 15 s.", attempt);
            await Task.Delay(TimeSpan.FromSeconds(15));
        }
    }
}

_ = app.Services.GetRequiredService<IPanelInfo>();   // decide live/mock at startup (GAD-T100)
// Demo institution for pilots and the tour (GAD-T103); --seed-demo must be the last argument.
if (args.Contains(Gadfly.Server.Demo.DemoSeeder.SeedArgument)) await Gadfly.Server.Demo.DemoSeeder.RunAsync(app);
// Pilot accounts: Erin's own organization (Erin owner, Ryan student), GAD-T226; one-shot with --then-exit.
if (args.Contains(Gadfly.Server.Demo.PilotSeeder.SeedArgument))
{
    try { await Gadfly.Server.Demo.PilotSeeder.RunAsync(app); }
    catch (Exception ex) { app.Logger.LogError(ex, "Pilot seeding failed."); }
    if (args.Contains("--then-exit")) return;
}
// Exam catalog: categories and open, published exams from Demo/Catalog (GAD-T659); one-shot with --then-exit.
if (args.Contains(Gadfly.Server.Demo.CatalogSeeder.SeedArgument))
{
    try { await Gadfly.Server.Demo.CatalogSeeder.RunAsync(app, args); }
    catch (Exception ex) { app.Logger.LogError(ex, "Catalog seeding failed."); }
    if (args.Contains("--then-exit")) return;
}
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

// X-Forwarded-For only from the App Service front end, never from the internet, so per-address rate limits hold (GAD-T476)
app.UseForwardedHeaders(Gadfly.Server.Platform.ForwardedHeadersSetup.Create(app.Configuration));
app.UseIdleActivityTracking();   // user requests (not health probes or App Service pings) keep the site up (GAD-T775)
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseResponseCompression();   // brotli/gzip every compressible response (GAD-T317); must run before static files
app.UseStaticFiles(VoiceServices.StaticFiles());   // adds .glb (the examiner avatar) and .mjs (GAD-T53)
// Fingerprinted, precompressed, long-cached URLs for assets referenced via @Assets[...] (GAD-T317). These are endpoints, so
// the default-deny fallback policy would send every stylesheet and image to the login page without AllowAnonymous (GAD-T492).
app.MapStaticAssets().AllowAnonymous();
Gadfly.Server.Localization.LocalizationSetup.UseGadflyLocalization(app);   // the request's UI language from the language cookie (GAD-T543)
app.UseMindAtticAuthentication();   // authn + authz + forced steps (change password, enroll MFA) + CSP
app.UseGadflySecurityHeaders();   // CSP and the rest of the security headers for every other page (GAD-T385)
app.UseRateLimiter();
app.UseAntiforgery();

GadflyEndpoints.Map(app);
Gadfly.Server.Localization.LocalizationSetup.MapUiStrings(app);   // UI strings for scripts (GAD-T543)
Gadfly.Server.Localization.LocalizationSetup.MapLanguageChoice(app);   // the public site's English / 한국어 links (GAD-T592)
app.MapVoiceExam();
app.MapGrading();
app.MapGradingActions();   // HTTP grading workflow for integrators and the demo tour (GAD-T105)

app.Run();

/// <summary>Program's partial, so Razor components can name the app assembly.</summary>
public partial class Program;
