using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Socrates.Mock;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Admin;

/// <summary>The AI mode the platform admin last chose (GAD-T802), or none when it was never switched.</summary>
public sealed record StoredAiMode(string Mode, Guid? ChangedBy, DateTimeOffset ChangedAt);

/// <summary>What a switch did, in plain words for the admin screen.</summary>
public sealed record AiModeSwitchResult(bool Ok, string Message);

/// <summary>
/// The platform admin's switch between the mock and the live AI panel (GAD-T802), with no restart. The choice is kept as
/// append-only audit events (<c>platform.ai-mode.switched</c>, entity <c>AiMode</c>, no organization; the latest is in force),
/// like an exam's panel settings, so every flip records who made it, from what and to what. <c>Gadfly:AiMode</c> is only the
/// default until the first flip. Exam sessions keep the mode they started in (<see cref="ExamSession.AiMode"/>, <see cref="PanelPin"/>);
/// new sessions, and everything not tied to a session (email included, <see cref="Notifications.EmailDeliveryGate"/>), follow the switch.
/// </summary>
public sealed class AiModeSwitch(IServiceProvider services, IServiceScopeFactory scopes, TimeProvider clock, IConfiguration configuration, ILogger<AiModeSwitch> log)
{
    public const string Action = "platform.ai-mode.switched";
    public const string EntityType = "AiMode";

    private SelectedPanel? Panel => services.GetService<SelectedPanel>();
    private bool _warnedNoKeys;

    /// <summary>The mode in force on this server now: "live" or "mock".</summary>
    public string CurrentMode => Panel is { } p ? (p.ServerIsMock ? PanelModes.Mock : PanelModes.Live)
        : services.GetService<IPanelInfo>()?.Mode ?? PanelModes.Mock;

    /// <summary>The latest stored choice, or null when no admin has switched yet (the configured default is in force).</summary>
    public static async Task<StoredAiMode?> LatestAsync(GadflyDbContext db, CancellationToken ct = default)
    {
        var row = await db.AuditEvents.AsNoTracking().IgnoreQueryFilters()
            .Where(a => a.OrganizationId == null && a.Action == Action && a.EntityType == EntityType)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Select(a => new { a.AfterJson, a.ActorUserId, a.At }).FirstOrDefaultAsync(ct);
        return row is not null && ModeOf(row.AfterJson) is { } mode ? new StoredAiMode(mode, row.ActorUserId, row.At) : null;
    }

    /// <summary>The latest stored choice, read in a system scope.</summary>
    public async Task<StoredAiMode?> LatestAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.System);
        return await LatestAsync(scope.ServiceProvider.GetRequiredService<GadflyDbContext>(), ct);
    }

    /// <summary>
    /// Puts the stored choice in force on this process (at start, and every <see cref="AiModeSync.Interval"/>, so the worker and
    /// every other instance follow a flip made on one). Returns true when the mode changed.
    /// </summary>
    public async Task<bool> ApplyStoredAsync(CancellationToken ct = default)
    {
        if (Panel is not { } panel || await LatestAsync(ct) is not { } stored) return false;
        var mock = stored.Mode == PanelModes.Mock;
        if (!mock && panel.ServerIsMock && LiveRefusal() is { } refusal)
        {
            // Stored live but this process has no keys: it stays mock rather than failing every call.
            if (!_warnedNoKeys) log.LogWarning("The platform admin chose the live AI panel, but this process stays in mock mode: {Refusal}", refusal);
            _warnedNoKeys = true;
            return false;
        }
        var changed = panel.Switch(mock, $"The platform admin switched the AI panel to {stored.Mode} at {stored.ChangedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC.");
        if (changed) log.LogWarning("Gadfly AI panel switched to {Mode} by the platform admin's stored choice.", stored.Mode.ToUpperInvariant());
        return changed;
    }

    /// <summary>Why the live panel can't be switched on (a provider key is missing), or null when it can. No provider is called.</summary>
    public string? LiveRefusal()
    {
        var keyed = Panel?.KeyedProviders ?? [];
        if (keyed.Count == 0)
            return "No AI provider key was found (MindAttic Vault's LLM providers). Add the keys, then switch to live.";
        var liveTurn = LiveTurnSettings.Provider(configuration[LiveTurnSettings.ProviderSetting]);
        if (!keyed.Contains(liveTurn, StringComparer.OrdinalIgnoreCase))
            return $"The examiner's live-turn model runs on {liveTurn}, which has no key (keys found: {string.Join(", ", keyed)}). Add its key, then switch to live.";
        return null;
    }

    /// <summary>
    /// Switches the AI panel to <paramref name="mode"/> now, for the platform admin only. Refused when switching to live without
    /// the provider keys. Every flip is an audit event (who, from, to).
    /// </summary>
    public async Task<AiModeSwitchResult> SwitchAsync(ITenantContext actor, string mode, CancellationToken ct = default)
    {
        if (!actor.IsSuperAdmin) return new(false, "Only the platform admin can switch the AI panel between mock and live.");
        if (mode is not (PanelModes.Live or PanelModes.Mock)) return new(false, $"\"{mode}\" is not an AI mode; use live or mock.");
        if (Panel is not { } panel) return new(false, "This server's AI panel can't be switched.");
        var before = CurrentMode;
        if (before == mode) return new(true, $"The AI panel is already {mode}.");
        if (mode == PanelModes.Live && LiveRefusal() is { } refusal) return new(false, refusal);

        var now = clock.GetUtcNow();
        await using (var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.System))
        {
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            db.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = null, ActorUserId = actor.UserId, ActorRole = "platform-admin",
                Action = Action, EntityType = EntityType, EntityId = "platform",
                BeforeJson = Json(before), AfterJson = Json(mode), At = now,
                Reason = $"AI panel switched from {before} to {mode}. Exam sessions already started keep their mode; new ones use {mode}.",
            });
            await db.SaveChangesAsync(ct);
        }
        panel.Switch(mode == PanelModes.Mock, $"The platform admin switched the AI panel to {mode} at {now.UtcDateTime:yyyy-MM-dd HH:mm} UTC.");
        log.LogWarning("Gadfly AI panel switched from {Before} to {After} by user {User}.", before.ToUpperInvariant(), mode.ToUpperInvariant(), actor.UserId);
        return new(true, mode == PanelModes.Live
            ? "The AI panel is live: new exams call the AI providers and spend credit. Exams already in progress stay in mock mode."
            : "The AI panel is mock: new exams are simulated and no email is sent. Exams already in progress stay live.");
    }

    private static string Json(string mode) => JsonSerializer.Serialize(new { mode });

    private static string? ModeOf(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("mode", out var m) && PanelPin.IsMockMode(m.GetString()) is { } mock
                ? (mock ? PanelModes.Mock : PanelModes.Live) : null;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Keeps this process on the platform admin's stored AI mode (GAD-T802): at start, then every <see cref="Interval"/>.</summary>
public sealed class AiModeSync(AiModeSwitch aiMode, ILogger<AiModeSync> log) : BackgroundService
{
    public static TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await aiMode.ApplyStoredAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                // The database may not be migrated or reachable yet: the configured mode stays in force until it is.
                log.LogDebug(ex, "Could not read the stored AI mode; trying again shortly.");
            }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
