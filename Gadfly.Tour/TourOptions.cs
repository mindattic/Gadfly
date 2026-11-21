using System.Security.Cryptography;

namespace Gadfly.Tour;

/// <summary>Command-line options (docs/TOUR.md).</summary>
public sealed class TourOptions
{
    public const string TourDatabase = "GadflyTour";

    /// <summary>Target an already running server instead of starting one.</summary>
    public string? Url { get; set; }

    /// <summary>Drop and re-create the tour's LocalDB database before starting the server.</summary>
    public bool Fresh { get; set; }

    /// <summary>Real LLM providers instead of the mock panel.</summary>
    public bool Live { get; set; }

    public int Port { get; set; } = 7375;

    /// <summary>The Gadfly.Server.exe to start (default: the repo's Gadfly.Server/bin/Debug/net10.0 build).</summary>
    public string? ServerExe { get; set; }

    /// <summary>Connection string the tour asserts against (and hands the server it starts).</summary>
    public string ConnectionString { get; set; } =
        $@"Server=(localdb)\MSSQLLocalDB;Database={TourDatabase};Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";

    /// <summary>Password of the demo accounts (generated per run when the tour starts the server).</summary>
    public string Password { get; set; } = "Tour-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(10)) + "-Pass!";

    /// <summary>The demo organization's API key (generated per run when the tour starts the server).</summary>
    public string ApiKey { get; set; } =
        $"gfk_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

    /// <summary>Leave the started server running after the tour (to look around in a browser).</summary>
    public bool Keep { get; set; }

    /// <summary>Print every HTTP call and hub step.</summary>
    public bool Verbose { get; set; }

    public string BaseUrl => (Url ?? $"https://localhost:{Port}").TrimEnd('/');

    public static TourOptions Parse(string[] args)
    {
        var o = new TourOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--url": o.Url = Next(); break;
                case "--fresh": o.Fresh = true; break;
                case "--live": o.Live = true; break;
                case "--port":
                    var portText = Next();
                    if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
                        throw new ArgumentException($"--port needs a number from 1 to 65535, not '{portText}'.");
                    o.Port = port;
                    break;
                case "--server": o.ServerExe = Next(); break;
                case "--db": o.ConnectionString = Next(); break;
                case "--password": o.Password = Next(); break;
                case "--api-key": o.ApiKey = Next(); break;
                case "--keep": o.Keep = true; break;
                case "--verbose": o.Verbose = true; break;
                case "-h" or "--help": throw new HelpRequested();
                default: throw new ArgumentException($"Unknown option {args[i]}.");
            }
        }
        if (o.Url is not null && o.Fresh)
            throw new ArgumentException("--fresh drops the tour database under a server the tour starts itself; it can't be combined with --url.");
        return o;
    }

    public const string Help = """
        Gadfly demo tour (GAD-T105): seeds the demo institution and drives the whole workflow end to end.

          Gadfly.Tour [--fresh] [--live] [--port 7375] [--server <Gadfly.Server.exe>] [--keep] [--verbose]
          Gadfly.Tour --url https://host:port --password <demo password> --api-key <demo key> [--db <connection string>]

          --fresh      drop and re-create the LocalDB database GadflyTour first
          --live       real LLM providers (default: the clearly labelled mock panel)
          --url        target a running server (started with --seed-demo and the same password/API key)
          --server     the Gadfly.Server.exe to start (default: Gadfly.Server/bin/Debug/net10.0)
          --keep       leave the started server running afterwards
        Exit code 0 when every stage passed or was skipped as not built yet; 1 when any stage failed.
        """;
}

public sealed class HelpRequested : Exception;
