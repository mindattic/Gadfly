namespace Gadfly.Worker;

/// <summary>
/// Stops the host when Azure asks a continuous WebJob to stop: it creates the file named by WEBJOBS_SHUTDOWN_FILE and
/// waits settings.job's stopping_wait_time before killing the process. SIGTERM and Ctrl+C are the host's own console lifetime.
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
                log.LogInformation("Gadfly.Worker: the WebJob shutdown file appeared; stopping.");
                lifetime.StopApplication();
                return;
            }
        }
        catch (OperationCanceledException) { }
    }
}
