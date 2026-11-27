namespace Gadfly.Worker;

/// <summary>
/// Stops the host when a WebJobs-style shutdown signal appears: a host that creates the file named by
/// WEBJOBS_SHUTDOWN_FILE and waits before killing the process gets a clean stop instead. SIGTERM and Ctrl+C are the
/// host's own console lifetime.
/// </summary>
public sealed class ShutdownFileWatcher(IHostApplicationLifetime lifetime, ILogger<ShutdownFileWatcher> log) : BackgroundService
{
    public const string Variable = "WEBJOBS_SHUTDOWN_FILE";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var path = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(path)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!File.Exists(path)) continue;
                log.LogInformation("Gadfly.Worker: the shutdown file appeared; stopping.");
                lifetime.StopApplication();
                return;
            }
        }
        catch (OperationCanceledException) { }
    }
}
