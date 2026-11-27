using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Platform;

/// <summary>
/// On-demand hosting (GAD-T775): the site is started by hand (/kickstart gadfly) and stops itself once nobody is using it and
/// nothing is left to do. When no user has touched the site for <c>Gadfly:IdleShutdown:Minutes</c> (default 20; 0 turns it off;
/// always off in Development) and <see cref="IdleShutdownPendingWork"/> finds no pending background work, the watchdog stops its
/// own App Service site through Azure Resource Manager with the site's system-assigned managed identity (Website Contributor on
/// that site only; the Azure provisioning script went with the 2026-11-26 decommission, GAD-T804). The serverless SQL database then auto-pauses on its own delay.
/// Pending work keeps the site up, so a student who finishes an exam and walks away still gets it scored, posted to the inbox
/// and emailed. The site reads its own identity from App Service's environment (WEBSITE_SITE_NAME, WEBSITE_RESOURCE_GROUP,
/// WEBSITE_OWNER_NAME for the subscription, WEBSITE_SLOT_NAME); without them (not on App Service) the watchdog stays off.
/// </summary>
public static class IdleShutdownSetup
{
    public const string MinutesSetting = "Gadfly:IdleShutdown:Minutes";
    public const int DefaultMinutes = 20;

    public static WebApplicationBuilder AddIdleShutdown(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IdleActivityTracker>();
        // Every hub (the Blazor circuit's own and the voice exam's): a call from the browser is a person using the site.
        builder.Services.Configure<HubOptions>(o => o.AddFilter<IdleActivityHubFilter>());
        builder.Services.AddSingleton<IdleActivityHubFilter>();
        var minutes = builder.Configuration.GetValue(MinutesSetting, DefaultMinutes);
        if (minutes > 0 && !builder.Environment.IsDevelopment())
        {
            builder.Services.AddHttpClient(IdleShutdownWatchdog.HttpClientName);
            builder.Services.AddHostedService<IdleShutdownWatchdog>();
        }
        return builder;
    }

    /// <summary>Marks each user request as activity. Placed early so even a request that ends in a redirect or 401 counts.</summary>
    public static IApplicationBuilder UseIdleActivityTracking(this IApplicationBuilder app)
    {
        var tracker = app.ApplicationServices.GetRequiredService<IdleActivityTracker>();
        return app.Use(async (ctx, next) =>
        {
            var counts = IdleActivityTracker.IsUserRequest(ctx.Request.Path, ctx.Request.Headers.UserAgent.ToString());
            if (counts) tracker.Touch();
            try { await next(); }
            finally { if (counts) tracker.Touch(); }
        });
    }
}

/// <summary>When a person last used the site: an HTTP request or a call from a browser over a hub (GAD-T775).</summary>
public sealed class IdleActivityTracker(TimeProvider clock)
{
    private long _lastTicks = clock.GetUtcNow().UtcTicks;   // the start counts as activity: someone just kickstarted it

    public DateTimeOffset LastActivity => new(Interlocked.Read(ref _lastTicks), TimeSpan.Zero);

    public void Touch() => Interlocked.Exchange(ref _lastTicks, clock.GetUtcNow().UtcTicks);

    // App Service's own traffic: health check, Always On, warm-up and instance readiness pings.
    private static readonly string[] PlatformAgents = ["HealthCheck", "AlwaysOn", "SiteWarmup", "ReadyForRequest", "HostingSyncWorker", "Azure Traffic Manager"];

    /// <summary>
    /// False for health probes (<c>/healthz*</c>) and App Service's own pings, which must not keep an unused site running.
    /// Kudu answers on the separate scm host and never reaches this app; the watchdog's own calls go to ARM, not here.
    /// </summary>
    public static bool IsUserRequest(PathString path, string? userAgent)
    {
        if (path.StartsWithSegments("/healthz", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWithSegments("/robots.txt", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(userAgent))
            foreach (var a in PlatformAgents)
                if (userAgent.Contains(a, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}

/// <summary>
/// Counts a browser's hub calls as activity. A Blazor circuit is one long WebSocket request, so the HTTP middleware sees it only
/// open and close; the clicks and keystrokes inside it arrive here. The circuit's own bookkeeping replies (render acks, JS
/// interop results) don't count, so a page that refreshes itself on a timer doesn't keep an unattended tab's site running.
/// </summary>
public sealed class IdleActivityHubFilter(IdleActivityTracker tracker) : IHubFilter
{
    private static readonly HashSet<string> Bookkeeping = new(StringComparer.Ordinal) { "OnRenderCompleted", "EndInvokeJSFromDotNet", "ReceiveByteArray", "ReceiveJSDataChunk" };

    public ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (!Bookkeeping.Contains(invocationContext.HubMethodName)) tracker.Touch();
        return next(invocationContext);
    }
}

/// <summary>What still has to happen without anyone watching (GAD-T775). Null when nothing is pending, else the first reason found.</summary>
public static class IdleShutdownPendingWork
{
    /// <summary>A queued job due within this window keeps the site up (the worker's longest retry backoff is an hour); reminders queued for days ahead don't.</summary>
    public static readonly TimeSpan JobHorizon = TimeSpan.FromHours(1);

    public static async Task<string?> FindAsync(GadflyDbContext db, IConfiguration config, DateTimeOffset now, CancellationToken ct)
    {
        // The job queue: grading, notification emails (EmailOutbox stages them as jobs), ingestion, reports...
        var horizon = now + JobHorizon;
        var jobs = await db.Jobs.AsNoTracking()
            .CountAsync(j => j.State == JobState.Running || (j.State == JobState.Queued && j.RunAfter <= horizon), ct);
        if (jobs > 0) return $"{jobs} background job(s) queued or running";

        // Ended attempts the grading sweeper has yet to pick up (it starts those within Gadfly:Grading:AutoStartWithinDays).
        var since = now - TimeSpan.FromDays(Math.Max(0, config.GetValue("Gadfly:Grading:AutoStartWithinDays", 3)));
        var ungraded = await db.ExamSessions.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(s => s.State == SessionState.Ended && s.EndedAt >= since && !db.FinalGrades.IgnoreQueryFilters().Any(g => g.SessionId == s.Id), ct);
        if (ungraded > 0) return $"{ungraded} submitted attempt(s) not yet graded";

        // Grades waiting on the AI panel with no job yet (the sweeper re-queues them). Grades waiting on a person don't count.
        var aiPending = await db.FinalGrades.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(g => g.Status == GradeStatus.ExaminationCompleted || g.Status == GradeStatus.AiGradingPending, ct);
        if (aiPending > 0) return $"{aiPending} attempt(s) waiting for AI grading";

        // Released results an LTI gradebook hasn't been sent yet, unless the send ran out of tries (that waits for an admin).
        var passback = await (
            from link in db.LtiAssignmentLinks.IgnoreQueryFilters().AsNoTracking()
            join rl in db.LtiResourceLinks.IgnoreQueryFilters().AsNoTracking() on link.ResourceLinkId equals rl.Id
            join reg in db.LtiRegistrations.IgnoreQueryFilters().AsNoTracking() on rl.RegistrationId equals reg.Id
            join s in db.ExamSessions.IgnoreQueryFilters().AsNoTracking() on link.AssignmentId equals s.AssignmentId
            join g in db.FinalGrades.IgnoreQueryFilters().AsNoTracking() on s.Id equals g.SessionId
            where reg.Enabled
                  && ((rl.LineItemUrl != null && rl.LineItemUrl != "") || (rl.LineItemsUrl != null && rl.LineItemsUrl != ""))
                  && s.Attempt == db.ExamSessions.IgnoreQueryFilters().Where(o => o.AssignmentId == link.AssignmentId).Max(o => o.Attempt)
                  && g.FinalPercent != null
                  && (g.Status == GradeStatus.Published || g.Status == GradeStatus.UnderAppeal || g.Status == GradeStatus.Amended)
                  && (link.PassbackSentAt == null || link.LastPassbackPercent != g.FinalPercent)
                  && (link.PassbackPendingPercent == null || link.PassbackPendingPercent != g.FinalPercent || link.PassbackFailedAt == null)
            select link.AssignmentId).Distinct().CountAsync(ct);
        if (passback > 0) return $"{passback} LTI grade passback(s) not yet sent";

        return null;
    }
}

/// <summary>Stops the site once it is idle and nothing is pending (GAD-T775). See <see cref="IdleShutdownSetup"/>.</summary>
public sealed class IdleShutdownWatchdog(
    IdleActivityTracker tracker, IServiceScopeFactory scopes, IHttpClientFactory http, IConfiguration config,
    TimeProvider clock, ILogger<IdleShutdownWatchdog> log) : BackgroundService
{
    public const string HttpClientName = "idle-shutdown";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(10);

    /// <summary>The ARM id of this site (or its slot), from App Service's environment; null when not running on App Service.</summary>
    public static string? OwnResourceId(Func<string, string?> env)
    {
        var site = env("WEBSITE_SITE_NAME");
        var group = env("WEBSITE_RESOURCE_GROUP");
        var owner = env("WEBSITE_OWNER_NAME");   // "{subscriptionId}+{resourceGroup}-{region}webspace"
        if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(owner)) return null;
        var plus = owner.IndexOf('+');
        var subscription = plus > 0 ? owner[..plus] : owner;
        if (!Guid.TryParse(subscription, out _)) return null;
        var id = $"/subscriptions/{subscription}/resourceGroups/{group}/providers/Microsoft.Web/sites/{site}";
        var slot = env("WEBSITE_SLOT_NAME");
        if (!string.IsNullOrWhiteSpace(slot) && !slot.Equals("Production", StringComparison.OrdinalIgnoreCase))
            id += $"/slots/{slot}";
        return id;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var minutes = config.GetValue(IdleShutdownSetup.MinutesSetting, IdleShutdownSetup.DefaultMinutes);
        var resourceId = OwnResourceId(Environment.GetEnvironmentVariable);
        if (resourceId is null)
        {
            log.LogInformation("Idle shutdown is off: not running on App Service (WEBSITE_SITE_NAME, WEBSITE_RESOURCE_GROUP or WEBSITE_OWNER_NAME is missing).");
            return;
        }
        var idleLimit = TimeSpan.FromMinutes(minutes);
        log.LogInformation("Idle shutdown is on: {ResourceId} stops after {Minutes} idle minutes with no pending work.", resourceId, minutes);
        var credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
        string? lastHold = null;

        while (!stop.IsCancellationRequested)
        {
            var wait = CheckEvery;
            try
            {
                var idleFor = clock.GetUtcNow() - tracker.LastActivity;
                if (idleFor >= idleLimit)
                {
                    // The database is only asked once the site has been idle long enough, so the watchdog adds no load before then.
                    string? pending;
                    using (var scope = scopes.CreateSystemScope())
                        pending = await IdleShutdownPendingWork.FindAsync(scope.ServiceProvider.GetRequiredService<GadflyDbContext>(), config, clock.GetUtcNow(), stop);
                    if (pending is not null)
                    {
                        if (pending != lastHold)
                            log.LogInformation("Idle shutdown held: no user activity for {IdleMinutes:F0} minutes, but {Pending}.", idleFor.TotalMinutes, pending);
                        lastHold = pending;
                    }
                    else if (tracker.LastActivity <= clock.GetUtcNow() - idleLimit)   // nobody arrived during the check
                    {
                        log.LogWarning("Idle shutdown: no user activity for {IdleMinutes:F0} minutes (limit {Minutes}) and no pending background work; stopping {ResourceId}.",
                            idleFor.TotalMinutes, minutes, resourceId);
                        await StopSiteAsync(credential, resourceId, stop);
                        // On success the platform ends this process shortly; on failure, don't hammer ARM.
                        wait = RetryAfterFailure;
                    }
                }
                else lastHold = null;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Idle shutdown check failed; trying again in {Minutes} minutes.", RetryAfterFailure.TotalMinutes);
                wait = RetryAfterFailure;
            }
            try { await Task.Delay(wait, clock, stop); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task StopSiteAsync(TokenCredential credential, string resourceId, CancellationToken ct)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]), ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://management.azure.com{resourceId}/stop?api-version=2023-12-01");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            log.LogWarning("Idle shutdown: Azure accepted the stop of {ResourceId} ({Status}).", resourceId, (int)response.StatusCode);
            return;
        }
        var body = await response.Content.ReadAsStringAsync(ct);
        log.LogError("Idle shutdown: Azure refused to stop {ResourceId} ({Status}): {Body}. Does the site's managed identity have Website Contributor on it?",
            resourceId, (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
    }
}
