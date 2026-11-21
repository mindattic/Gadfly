using System.Diagnostics;

namespace Gadfly.Tour;

/// <summary>
/// Starts a real Gadfly.Server for the tour: Development environment, the tour's own database, mock (or live) AI,
/// and <c>--seed-demo</c>. The exe is copied to <c>gadfly-tour-host.exe</c> first, so housekeeping that stops
/// processes named Gadfly.Server never kills the tour's server (and the tour never kills anyone else's).
/// </summary>
public sealed class ServerProcess : IAsyncDisposable
{
    private Process? _process;
    public string LogPath { get; } = Path.Combine(Path.GetTempPath(), $"gadfly-tour-server-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    private StreamWriter? _log;

    public static string RepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Gadfly.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException("Run the tour from inside the Gadfly repository (Gadfly.slnx not found).");
    }

    public async Task StartAsync(TourOptions o, CancellationToken ct)
    {
        var repo = RepoRoot();
        var exe = o.ServerExe ?? Path.Combine(repo, "Gadfly.Server", "bin", "Debug", "net10.0", "Gadfly.Server.exe");
        if (Directory.Exists(exe)) exe = Path.Combine(exe, "Gadfly.Server.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException($"No server build at {exe}. Build Gadfly.Server first, or pass --server <path to Gadfly.Server.exe>.");
        var host = Path.Combine(Path.GetDirectoryName(exe)!, "gadfly-tour-host.exe");
        if (!File.Exists(host) || File.GetLastWriteTimeUtc(host) < File.GetLastWriteTimeUtc(exe)) File.Copy(exe, host, overwrite: true);

        var psi = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(host)!,
        };
        // Settings travel as environment variables; the only arguments are the content root and the seed switch (last,
        // so the command-line configuration provider can't take another argument as its value).
        psi.ArgumentList.Add("--contentRoot");
        psi.ArgumentList.Add(Path.Combine(repo, "Gadfly.Server"));
        psi.ArgumentList.Add("--seed-demo");
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["ASPNETCORE_URLS"] = o.BaseUrl;
        psi.Environment["ConnectionStrings__Gadfly"] = o.ConnectionString;
        psi.Environment["Gadfly__AiMode"] = o.Live ? "live" : "mock";
        psi.Environment["Gadfly__Demo__Password"] = o.Password;
        psi.Environment["Gadfly__Demo__ApiKey"] = o.ApiKey;

        _log = new StreamWriter(LogPath) { AutoFlush = true };
        _process = Process.Start(psi) ?? throw new InvalidOperationException("The server did not start.");
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (_log) _log.WriteLine(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (_log) _log.WriteLine("ERR " + e.Data); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public bool HasExited => _process is { HasExited: true };

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (Exception) { /* already gone */ }
        }
        _process?.Dispose();
        if (_log is not null) await _log.DisposeAsync();
    }

    /// <summary>Lets the server keep running after the tour exits (<c>--keep</c>).</summary>
    public void Detach() => _process = null;
}
