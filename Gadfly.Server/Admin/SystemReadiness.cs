using Gadfly.Server.Data;
using Gadfly.Server.Diagnostics;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Voice;
using Gadfly.Socrates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gadfly.Server.Admin;

/// <summary>How one readiness check came out. Only <see cref="Failing"/> makes the server unready.</summary>
public enum CheckState { Ok, Warning, Failing }

/// <summary>One thing the system status page and <c>/healthz/ready</c> check (GAD-T302, GAD-T353, GAD-T356).</summary>
/// <param name="Name">A stable id (database, migrations, ai-panel, voice, jobs, webhooks, errors).</param>
/// <param name="Title">The plain-English heading the page shows.</param>
/// <param name="Summary">What was found, in counts and states only (never student data, never error text).</param>
public sealed record ReadinessCheck(string Name, string Title, CheckState State, string Summary);

/// <summary>One AI panel provider's recent record, as the page shows it. <see cref="LastError"/> is only filled for the platform admin.</summary>
public sealed record ProviderLine(string ProviderId, string Model, bool Failing, int ConsecutiveFailures, int TotalCalls, int TotalFailures,
    DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt, string? LastError);

/// <summary>The latest outcome of a speech call (recognizer or examiner voice), from the per-call timings (GAD-T413).</summary>
public sealed record VoiceCallLine(string CallName, DateTimeOffset At, bool Succeeded, int DurationMs, string? Error);

/// <summary>The platform side of the system status (GAD-T353): version, AI and voice mode, providers, database and migrations, and the checks.</summary>
public sealed record PlatformStatus(
    DateTimeOffset GeneratedAt,
    string Version,
    string Environment,
    string AiMode,
    string AiModeReason,
    string ConfiguredAiMode,
    IReadOnlyList<ProviderLine> Providers,
    string VoiceMode,
    string SpeechToText,
    string TextToSpeech,
    IReadOnlyList<VoiceCallLine> VoiceCalls,
    bool DatabaseHealthy,
    int MigrationsApplied,
    string? LatestMigration,
    IReadOnlyList<string> PendingMigrations,
    long? DatabaseBytes,
    IReadOnlyList<ReadinessCheck> Checks,
    IReadOnlyList<LoggedError> RecentErrors)
{
    /// <summary>Unready when any check fails; warnings leave it ready.</summary>
    public bool Ready => Checks.All(c => c.State != CheckState.Failing);
}

/// <summary>
/// Reads the platform's state for the system status page and <c>/healthz/ready</c> (GAD-T302; GAD-T353, GAD-T356). It calls no
/// AI or speech provider: provider health is what the live panel has already recorded, and speech state is the latest recorded
/// call. Database, migrations, jobs and webhooks are read across the platform from a system scope of its own. Error text (the
/// providers' last errors and the server's recent log errors) is filled only when <c>includeErrorText</c> is true, which the page
/// passes for the platform admin alone.
/// </summary>
public sealed class SystemReadinessService(IServiceScopeFactory scopes, HealthCheckService health, IConfiguration config, IHostEnvironment env,
    TimeProvider clock, IServiceProvider services)
{
    /// <summary>A queued job waiting longer than this suggests the background worker is stalled.</summary>
    public static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(15);

    public async Task<PlatformStatus> LoadAsync(bool includeErrorText, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var since = now - TimeSpan.FromHours(24);
        using var scope = scopes.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();

        var report = await health.CheckHealthAsync(ct);
        var dbHealthy = report.Entries.TryGetValue("database", out var entry) && entry.Status == HealthStatus.Healthy;

        IReadOnlyList<string> applied = [], pending = [];
        var migrationsRead = false;
        long? dbBytes = null;
        if (dbHealthy)
        {
            try
            {
                applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
                pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                migrationsRead = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            dbBytes = await DatabaseBytesAsync(db, ct);
        }

        var (jobsFailed, oldestQueued, deliveriesGaveUp, deliveriesFailing, voiceCalls) = dbHealthy
            ? await CountsAsync(db, since, ct) : (0, (DateTimeOffset?)null, 0, 0, (IReadOnlyList<VoiceCallLine>)[]);

        // The AI panel: the selected panel's mode (live or mock, and why), and each live provider's recorded health.
        var panelInfo = services.GetService<IPanelInfo>();
        var selected = services.GetService<Gadfly.Socrates.Mock.SelectedPanel>();
        var live = services.GetService<LegionVotingClient>();
        var models = services.GetService<Grading.PanelModelInventory>();
        var mock = panelInfo?.IsMock ?? true;
        var providers = (live?.Health.Snapshot() ?? []).Select(p => new ProviderLine(p.ProviderId, models?.ModelOf(p.ProviderId) ?? "", p.Failing,
            p.ConsecutiveFailures, p.TotalCalls, p.TotalFailures, p.LastSuccessAt, p.LastFailureAt, includeErrorText ? p.LastError : null)).ToList();

        var voice = services.GetService<VoiceSettings>() ?? VoiceSettings.Disabled;
        var stt = voice.Mode switch
        {
            VoiceMode.Off => "not in use",
            VoiceMode.Mock => "simulated (placeholder words, nothing sent out)",
            _ => config["MindAttic:SpeechRecognition:Provider"] is { Length: > 0 } p && !p.Equals("Simulated", StringComparison.OrdinalIgnoreCase)
                ? p : "Fallback (ElevenLabs Scribe, then OpenAI)",
        };
        var tts = voice.Mode switch
        {
            VoiceMode.Off => "not in use",
            VoiceMode.Mock => "simulated (in-process fake voice, nothing sent out)",
            _ => $"ElevenLabs, model {(config["MindAttic:Avatar:ElevenLabs:ModelId"] is { Length: > 0 } m ? m : voice.TtsModel)}",
        };
        var errors = services.GetService<RecentErrorLog>();

        var checks = Evaluate(new ReadinessInputs(dbHealthy, migrationsRead, pending.Count, mock, providers.Count, providers.Count(p => p.Failing),
            voice.Mode, voiceCalls, jobsFailed, oldestQueued is { } q ? now - q : null, deliveriesGaveUp, deliveriesFailing,
            errors?.CountSince(now - TimeSpan.FromHours(1)) ?? 0, env.IsDevelopment()));

        return new PlatformStatus(now, AppVersion.Label, env.EnvironmentName, panelInfo?.Mode ?? "unknown", panelInfo?.Reason ?? "",
            selected?.ConfiguredMode ?? config[PanelModes.Setting] ?? "", providers, voice.Mode.ToString().ToLowerInvariant(), stt, tts,
            includeErrorText ? voiceCalls : voiceCalls.Select(c => c with { Error = null }).ToList(),
            dbHealthy, applied.Count, applied.LastOrDefault(), pending, dbBytes, checks,
            includeErrorText ? errors?.Recent() ?? [] : []);
    }

    private static async Task<(int JobsFailed, DateTimeOffset? OldestQueued, int GaveUp, int Failing, IReadOnlyList<VoiceCallLine> Voice)> CountsAsync(
        GadflyDbContext db, DateTimeOffset since, CancellationToken ct)
    {
        var jobsFailed = await db.Jobs.AsNoTracking().CountAsync(j => j.State == JobState.Failed && (j.CompletedAt ?? j.CreatedAt) >= since, ct);
        var oldest = await db.Jobs.AsNoTracking().Where(j => j.State == JobState.Queued && j.Attempts == 0)
            .OrderBy(j => j.RunAfter).Select(j => (DateTimeOffset?)j.RunAfter).FirstOrDefaultAsync(ct);
        var gaveUp = await db.WebhookDeliveries.AsNoTracking().CountAsync(d => d.Status == DeliveryStatus.GaveUp && d.CreatedAt >= since, ct);
        var failing = await db.WebhookDeliveries.AsNoTracking().CountAsync(d => d.Status == DeliveryStatus.Failed, ct);
        // The latest outcome of each speech call kind (the recognizer's start, commit and stop; the examiner's voice).
        string[] names = ["stt.startSession", "stt.commit", "stt.stopSession", "tts.elevenlabs"];
        var voice = new List<VoiceCallLine>();
        foreach (var name in names)
        {
            var row = await db.ApiCallTimings.AsNoTracking().Where(t => t.CallName == name && t.StartedAtUtc >= since)
                .OrderByDescending(t => t.StartedAtUtc).Select(t => new { t.StartedAtUtc, t.Succeeded, t.DurationMs, t.ErrorSummary }).FirstOrDefaultAsync(ct);
            if (row is not null) voice.Add(new VoiceCallLine(name, row.StartedAtUtc, row.Succeeded, row.DurationMs, row.ErrorSummary));
        }
        return (jobsFailed, oldest, gaveUp, failing, voice);
    }

    /// <summary>The database's allocated size (SQL Server only; null where the server does not allow the query).</summary>
    private static async Task<long?> DatabaseBytesAsync(GadflyDbContext db, CancellationToken ct)
    {
        try
        {
            return await db.Database.SqlQueryRaw<long>("SELECT CAST(SUM(CAST(size AS bigint)) * 8192 AS bigint) AS [Value] FROM sys.database_files").SingleAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }

    /// <summary>What the checks are worked out from (kept apart so the rules can be tested without a server).</summary>
    public sealed record ReadinessInputs(bool DatabaseHealthy, bool MigrationsRead, int PendingMigrations, bool MockPanel, int Providers,
        int FailingProviders, VoiceMode Voice, IReadOnlyList<VoiceCallLine> VoiceCalls, int JobsFailed24h, TimeSpan? OldestQueuedWait,
        int DeliveriesGaveUp24h, int DeliveriesRetrying, int ErrorsLastHour, bool Development);

    /// <summary>
    /// The readiness rules. Only a database that does not answer, or migrations not applied, fail readiness (the app cannot serve
    /// exams correctly then); everything else is a warning the page explains: a mock panel or simulated voice outside development,
    /// every live provider failing, a speech call that failed last time, failed or stalled jobs, webhook deliveries given up, and
    /// errors in the log.
    /// </summary>
    public static IReadOnlyList<ReadinessCheck> Evaluate(ReadinessInputs i)
    {
        var checks = new List<ReadinessCheck>
        {
            i.DatabaseHealthy
                ? new("database", "Database", CheckState.Ok, "Answering.")
                : new("database", "Database", CheckState.Failing, "Not answering: exams cannot start or save."),
            !i.DatabaseHealthy ? new("migrations", "Database migrations", CheckState.Failing, "Unknown while the database is not answering.")
            : !i.MigrationsRead ? new("migrations", "Database migrations", CheckState.Warning, "Could not read the migration history.")
            : i.PendingMigrations > 0 ? new("migrations", "Database migrations", CheckState.Failing, $"{i.PendingMigrations} migration(s) not applied yet: the database is behind this version of the app.")
            : new("migrations", "Database migrations", CheckState.Ok, "Up to date."),
        };

        checks.Add(i.MockPanel
            ? new("ai-panel", "AI panel", i.Development ? CheckState.Ok : CheckState.Warning, "Mock panel: every AI output is simulated and labelled, and no provider is called.")
            : i.Providers == 0 ? new("ai-panel", "AI panel", CheckState.Ok, "Live; no provider call recorded since the server started.")
            : i.FailingProviders >= i.Providers ? new("ai-panel", "AI panel", CheckState.Warning, $"Live, but all {i.Providers} provider(s) are failing: AI grading and questions will fall back or stall.")
            : i.FailingProviders > 0 ? new("ai-panel", "AI panel", CheckState.Warning, $"Live; {i.FailingProviders} of {i.Providers} provider(s) failing, so the panel runs without them.")
            : new("ai-panel", "AI panel", CheckState.Ok, $"Live; {i.Providers} provider(s) answering."));

        var lastFailed = i.VoiceCalls.Where(c => !c.Succeeded).Select(c => c.CallName).ToList();
        checks.Add(i.Voice switch
        {
            VoiceMode.Off => new("voice", "Voice", CheckState.Warning, "Voice is off: the spoken exam room is not available."),
            VoiceMode.Mock => new("voice", "Voice", i.Development ? CheckState.Ok : CheckState.Warning, "Simulated speech recognition and examiner voice: nothing is sent to a speech provider."),
            _ when lastFailed.Count > 0 => new("voice", "Voice", CheckState.Warning, $"Live; the latest {string.Join(", ", lastFailed)} call(s) failed."),
            _ => new("voice", "Voice", CheckState.Ok, i.VoiceCalls.Count == 0 ? "Live; no speech call in the last 24 hours." : "Live; the latest speech calls succeeded."),
        });

        var stalled = i.OldestQueuedWait is { } wait && wait > StalledAfter;
        checks.Add(stalled
            ? new("jobs", "Background jobs", CheckState.Warning, $"A job has waited {Minutes(i.OldestQueuedWait!.Value)} to start: the background worker may be stalled.")
            : i.JobsFailed24h > 0 ? new("jobs", "Background jobs", CheckState.Warning, $"{i.JobsFailed24h} job(s) failed in the last 24 hours.")
            : new("jobs", "Background jobs", CheckState.Ok, "Running; none failed in the last 24 hours."));

        checks.Add(i.DeliveriesGaveUp24h > 0
            ? new("webhooks", "Webhook deliveries", CheckState.Warning, $"{i.DeliveriesGaveUp24h} delivery(ies) given up in the last 24 hours; {i.DeliveriesRetrying} waiting to retry.")
            : i.DeliveriesRetrying > 0 ? new("webhooks", "Webhook deliveries", CheckState.Ok, $"{i.DeliveriesRetrying} delivery(ies) waiting to retry.")
            : new("webhooks", "Webhook deliveries", CheckState.Ok, "Nothing waiting or given up."));

        checks.Add(i.ErrorsLastHour > 0
            ? new("errors", "Errors in the log", CheckState.Warning, $"{i.ErrorsLastHour} error(s) logged on this server in the last hour.")
            : new("errors", "Errors in the log", CheckState.Ok, "None on this server in the last hour."));
        return checks;
    }

    private static string Minutes(TimeSpan t) => t.TotalHours >= 1 ? $"{t.TotalHours:0.#} hours" : $"{Math.Max(1, (int)t.TotalMinutes)} minutes";

    /// <summary>
    /// <c>GET /healthz/ready</c> (GAD-T356): 200 when ready, 503 when a check fails. Anonymous and data-free, like <c>/healthz</c>:
    /// each check's name and state only, never counts, provider names or error text.
    /// </summary>
    public static RouteHandlerBuilder MapReady(IEndpointRouteBuilder app) =>
        app.MapGet("/healthz/ready", async (IServiceProvider sp, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var status = await ActivatorUtilities.CreateInstance<SystemReadinessService>(sp).LoadAsync(includeErrorText: false, ct);
            var body = new
            {
                status = status.Ready ? "ready" : "unready",
                checks = status.Checks.Select(c => new { c.Name, state = c.State.ToString().ToLowerInvariant() }),
            };
            return Results.Json(body, statusCode: status.Ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });
}
