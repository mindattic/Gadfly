using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Jobs;

/// <summary>Worker tuning.</summary>
public sealed class JobWorkerOptions
{
    /// <summary>How long a claimed job stays locked before another worker may take it over.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(10);
    /// <summary>Idle wait between polls when the queue is empty.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Jobs run at the same time by this instance.</summary>
    public int Concurrency { get; set; } = 2;
}

/// <summary>
/// Runs queued jobs. Claiming is an atomic conditional UPDATE on the row (state + lease), so
/// several app instances can share one queue without double-running a job. A job whose lease
/// expires (its worker died) is claimed again; failures retry with exponential backoff until
/// <see cref="Job.MaxAttempts"/>, then the job is marked Failed with its last error.
/// </summary>
public sealed class JobWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    JobWorkerOptions options,
    ILogger<JobWorker> log) : BackgroundService
{
    private readonly string _workerId = Truncate($"{Environment.MachineName}/{Environment.ProcessId}/{Guid.NewGuid():N}", 120);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[^max..];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lanes = Enumerable.Range(0, Math.Max(1, options.Concurrency)).Select(_ => LaneAsync(stoppingToken));
        await Task.WhenAll(lanes);
    }

    private async Task LaneAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            bool ran;
            try
            {
                ran = await TryRunOneAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Job worker lane failed; pausing before the next poll.");
                ran = false;
            }
            if (!ran)
            {
                try { await Task.Delay(options.PollInterval, clock, stop); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Claims and runs one due job. Returns false when nothing was due.</summary>
    internal async Task<bool> TryRunOneAsync(CancellationToken stop)
    {
        using var scope = scopes.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var now = clock.GetUtcNow();

        var candidate = await db.Jobs.AsNoTracking()
            .Where(j => (j.State == JobState.Queued && j.RunAfter <= now)
                     || (j.State == JobState.Running && j.LockedUntil < now))
            .OrderBy(j => j.RunAfter)
            .Select(j => new { j.Id, j.State, j.LockedUntil })
            .FirstOrDefaultAsync(stop);
        if (candidate is null) return false;

        // Atomic claim: only succeeds if nobody changed the row since we read it.
        var leaseUntil = now + options.LeaseDuration;
        var claimed = await db.Jobs
            .Where(j => j.Id == candidate.Id && j.State == candidate.State && j.LockedUntil == candidate.LockedUntil)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, JobState.Running)
                .SetProperty(j => j.LockedBy, _workerId)
                .SetProperty(j => j.LockedUntil, leaseUntil)
                .SetProperty(j => j.Attempts, j => j.Attempts + 1), stop);
        if (claimed == 0) return true; // lost the race; poll again immediately

        var job = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == candidate.Id, stop);
        var handler = scope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(h => h.Kind == job.Kind);
        if (handler is null)
        {
            await FinishAsync(db, job.Id, JobState.Failed, $"No handler is registered for job kind '{job.Kind}'.", stop);
            return true;
        }

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var progress = new RowProgress(scopes, clock, job.Id, _workerId, options.LeaseDuration, jobCts);
        try
        {
            log.LogInformation("Running job {JobId} ({Kind}), attempt {Attempt}.", job.Id, job.Kind, job.Attempts);
            await handler.RunAsync(job, progress, jobCts.Token);
            await FinishAsync(db, job.Id, JobState.Succeeded, null, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Shutting down: release the lease so another instance picks it up promptly.
            await db.Jobs.Where(j => j.Id == job.Id && j.LockedBy == _workerId)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, JobState.Queued)
                                          .SetProperty(j => j.LockedBy, (string?)null)
                                          .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null), CancellationToken.None);
        }
        catch (OperationCanceledException) when (progress.CancelledByUser)
        {
            log.LogInformation("Job {JobId} was cancelled.", job.Id);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Job {JobId} ({Kind}) failed on attempt {Attempt}.", job.Id, job.Kind, job.Attempts);
            if (job.Attempts >= job.MaxAttempts)
            {
                await FinishAsync(db, job.Id, JobState.Failed, ex.Message, CancellationToken.None);
            }
            else
            {
                var backoff = TimeSpan.FromSeconds(Math.Min(3600, 15 * Math.Pow(2, job.Attempts - 1)));
                var retryAt = clock.GetUtcNow() + backoff;
                var error = ex.Message;
                await db.Jobs.Where(j => j.Id == job.Id && j.LockedBy == _workerId)
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, JobState.Queued)
                                              .SetProperty(j => j.RunAfter, retryAt)
                                              .SetProperty(j => j.Error, error)
                                              .SetProperty(j => j.LockedBy, (string?)null)
                                              .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null), CancellationToken.None);
            }
        }
        return true;
    }

    private async Task FinishAsync(GadflyDbContext db, Guid jobId, JobState state, string? error, CancellationToken ct)
    {
        var at = clock.GetUtcNow();
        await db.Jobs.Where(j => j.Id == jobId && j.LockedBy == _workerId && j.State == JobState.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, state)
                                      .SetProperty(j => j.Error, error)
                                      .SetProperty(j => j.Progress, state == JobState.Succeeded ? 1.0 : 0.0)
                                      .SetProperty(j => j.CompletedAt, at)
                                      .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null), ct);
    }

    /// <summary>Writes progress, renews the lease, and notices a user cancellation.</summary>
    private sealed class RowProgress(IServiceScopeFactory scopes, TimeProvider clock, Guid jobId, string workerId,
        TimeSpan lease, CancellationTokenSource jobCts) : IJobProgress
    {
        public bool CancelledByUser { get; private set; }

        public async Task ReportAsync(double fraction, string? message = null, CancellationToken ct = default)
        {
            using var scope = scopes.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var until = clock.GetUtcNow() + lease;
            var p = Math.Clamp(fraction, 0, 1);
            var msg = message is { Length: > 400 } ? message[..400] : message;
            var n = await db.Jobs.Where(j => j.Id == jobId && j.LockedBy == workerId && j.State == JobState.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Progress, p)
                                          .SetProperty(j => j.ProgressMessage, msg)
                                          .SetProperty(j => j.LockedUntil, until), ct);
            if (n == 0)
            {
                // Cancelled by a user, or the lease was taken over: stop working on it.
                CancelledByUser = true;
                await jobCts.CancelAsync();
                jobCts.Token.ThrowIfCancellationRequested();
            }
        }
    }
}

public static class JobServiceCollectionExtensions
{
    /// <summary>Adds the job queue and its background worker.</summary>
    public static IServiceCollection AddGadflyJobs(this IServiceCollection services, Action<JobWorkerOptions>? configure = null)
    {
        var options = new JobWorkerOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<JobQueue>();
        services.AddHostedService<JobWorker>();
        return services;
    }

    public static IServiceCollection AddJobHandler<THandler>(this IServiceCollection services) where THandler : class, IJobHandler =>
        services.AddScoped<IJobHandler, THandler>();
}
