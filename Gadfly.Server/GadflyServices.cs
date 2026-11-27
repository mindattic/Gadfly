using Gadfly.Server.Analytics;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Desk;
using Gadfly.Server.Grading;
using Gadfly.Server.Jobs;
using Gadfly.Server.Platform;
using Gadfly.Server.Privacy;
using Gadfly.Server.Sessions;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Verification;
using Gadfly.Socrates.Mock;
using Gadfly.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using MindAttic.Authentication.Web;
using MindAttic.Log.Extensions;
using MindAttic.Vault.Configuration;

namespace Gadfly.Server;

/// <summary>
/// The services both hosts share (GAD-T700): the web app (Program.cs) and the grading worker (Gadfly.Worker, its own
/// process beside the site, docs/DEPLOY.md "The worker"). Same configuration keys, same database, same job
/// handlers; only the web host adds the UI, hubs and voice on top.
/// </summary>
public static class GadflyServices
{
    /// <summary>When false, the web app only enqueues work and leaves the job queue and its sweepers to Gadfly.Worker.</summary>
    public const string RunJobsInWebSetting = "Gadfly:Jobs:RunInWeb";

    /// <summary>The queue-driven background services: the job worker and the sweepers and schedulers that feed it.</summary>
    public static readonly Type[] BackgroundWork =
    [
        typeof(JobWorker),
        typeof(GradingSweeper),
        typeof(Notifications.ReminderScheduler),
        typeof(StaleSessionScheduler),
        typeof(RetentionScheduler),
        typeof(Lti.LtiPassbackSweeper),
        typeof(Lti.LtiRosterRefreshScheduler),
        typeof(ResponseBackfillService),
    ];

    /// <summary>
    /// Registers everything the job handlers and pages have in common. With <paramref name="runBackgroundWork"/> false the
    /// <see cref="BackgroundWork"/> hosted services are left out, so this process enqueues jobs but never runs them.
    /// </summary>
    public static void AddGadflyServices(this IHostApplicationBuilder builder, bool runBackgroundWork)
    {
        // On Azure App Service, application logs go to LogFiles/Application and the log stream (no-op elsewhere).
        builder.Logging.AddAzureWebAppDiagnostics();
        // The last errors this server logged, in memory, for the platform admin on the system status page (GAD-T355).
        var recentErrors = new Diagnostics.RecentErrorLog();
        builder.Logging.AddProvider(recentErrors);
        builder.Services.AddSingleton(recentErrors);
        // The provider's own defaults (10MB per file, 2 files retained) cap out fast once a live exam is running and then look
        // like logging "stopped" (GAD-T411, a recurrence of the GAD-T225 go-live logging gap): a bigger, longer-rolling budget
        // keeps it flowing. MindAttic.Log's own SQLite log (GAD-T62) has no such cap; this only widens the Azure file mirror.
        builder.Services.Configure<Microsoft.Extensions.Logging.AzureAppServices.AzureFileLoggerOptions>(o =>
        {
            o.FileSizeLimit = 50 * 1024 * 1024;
            o.RetainedFileCountLimit = 10;
        });

        // Secrets: on a dev machine MindAttic.Vault's files (the shared LLM provider keys Legion reads, the
        // Security bucket the auth stack needs: pepper, bootstrap-token; the Payments bucket PaymentSettings reads);
        // in Azure, App Service settings / Key Vault.
        builder.Configuration.AddMindAtticVaultFiles(o => o.Buckets = new[] { "LLM", "Security", "Notifications", "Payments" });
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
            // Key ring: the library's default unless DataProtection:KeysPath names a folder to keep it in
            // (the Azure Blob/Key Vault key ring went with the Azure decommission, 2026-11-26). It must be an
            // absolute path shared by the web app and Gadfly.Worker, or each process ends up with its own ring
            // and cannot unprotect what the other protected.
            var keysPath = builder.Configuration["DataProtection:KeysPath"];
            if (o.IsProduction && !string.IsNullOrWhiteSpace(keysPath))
            {
                if (!Path.IsPathRooted(keysPath))
                    throw new InvalidOperationException("DataProtection:KeysPath must be an absolute path.");
                o.ConfigureDataProtection = dp => dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
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
        builder.Services.AddSingleton<PlanEnforcement>();
        // Optional card checkout (GAD-T372): stays off (PaymentSettings.Available false) unless Stripe keys are configured in Vault.
        builder.Services.AddSingleton<PaymentSettings>();
        builder.Services.AddScoped<CheckoutService>();
        builder.Services.AddHttpClient("StripeCheckout");

        // Background work (ingestion, concept extraction, reports) on the SQL job table (GAD-T43).
        builder.Services.AddGadflyJobs();
        builder.Services.AddJobHandler<Notifications.EmailDeliveryJob>();
        builder.Services.AddJobHandler<Notifications.ReminderSweepJob>();
        builder.Services.AddHostedService<Notifications.ReminderScheduler>();

        // The AI panel: live Legion or the clearly labelled mock (Gadfly:AiMode = live | mock | auto) (GAD-T100).
        builder.Services.AddGadflyPanel(builder.Configuration, builder.Environment.IsDevelopment());
        // How long every outside call on a live exam takes, in gadfly.ApiCallTimings and the log (GAD-T413).
        Diagnostics.ApiCallTimingServices.AddApiCallTimings(builder.Services);
        // The platform admin's runtime switch between mock and live, stored and followed by every process (GAD-T802).
        builder.Services.AddSingleton<Admin.AiModeSwitch>();
        builder.Services.AddHostedService<Admin.AiModeSync>();

        // Source material: text extractors (.txt/.md, and PDF/DOCX/EPUB/HTML through Tutor.Ingestion, GAD-T155) and processing jobs.
        builder.Services.AddSingleton<IDocumentTextExtractor, PlainTextExtractor>();
        builder.Services.AddSingleton<IDocumentTextExtractor, TutorIngestionExtractor>();
        builder.Services.AddJobHandler<SourceProcessingJob>();

        // Live exam sessions: one engine per active session, persisted after every step (GAD-T44).
        builder.Services.AddSingleton<SessionOrchestrator>();
        builder.Services.AddScoped<HubIntegrityRecorder>();
        builder.Services.AddScoped<Integrity.IntegrityRecorder>();
        // A session a student abandons outright (no pause, never comes back) is closed once well past its own deadline (GAD-T306).
        // Singleton: it holds no scoped state (it opens its own system and organization scopes), and like the singleton
        // SessionOrchestrator it depends on, it can be resolved from the root provider (GAD-T422).
        builder.Services.AddSingleton<StaleSessionSweep>();
        builder.Services.AddJobHandler<StaleSessionJob>();
        builder.Services.AddHostedService<StaleSessionScheduler>();

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
        Lti.LtiSetup.AddLti(builder.Services);

        // Privacy (GAD-T61): daily retention redaction, per-student export and erasure.
        builder.Services.AddScoped<PrivacyService>();
        builder.Services.AddScoped<LogRetention>();   // logs kept at least six months, apart from transcripts (GAD-T466)
        builder.Services.AddJobHandler<RetentionJob>();
        builder.Services.AddHostedService<RetentionScheduler>();

        // Instructor and student pages' services (GAD-T49, GAD-T50).
        builder.Services.AddScoped<SourceLibraryService>();
        builder.Services.AddAnalytics();
        builder.Services.AddAssessments();
        builder.Services.AddGrading();
        builder.Services.AddVerification();   // backfills original-response records for older turns (GAD-T86)
        builder.Services.AddScoped<Exams.ExamService>();
        // The admin desk at /admin (GAD-T246): its Admin-role policy, behind Gadfly:AdminDesk:Enabled.
        builder.Services.AddAdminDesk();
        // UI languages (GAD-T543): English and Korean interface text, the language cookie, dates and numbers per language.
        Localization.LocalizationSetup.AddGadflyLocalization(builder.Services);

        if (!runBackgroundWork)
            builder.Services.RemoveWhere(d => d.ServiceType == typeof(IHostedService) && BackgroundWork.Contains(d.ImplementationType));
    }

    private static void RemoveWhere(this IServiceCollection services, Func<ServiceDescriptor, bool> match)
    {
        for (var i = services.Count - 1; i >= 0; i--)
            if (match(services[i])) services.RemoveAt(i);
    }
}
