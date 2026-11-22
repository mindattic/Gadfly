using System.Globalization;
using System.Text.Json;
using Gadfly.Engine;

namespace Gadfly.Cli;

/// <summary>
/// gadfly coherence (GAD-T449): the relay arm of the pilot study. Reads saved sessions (*.session.json, each with its
/// *.ledger.json beside it) grouped by study condition, writes one row of ledger-based coherence measures per session and prints
/// each condition's counts and means, so hypothesis H1 (relayed answers are less coherent across turns at equal answer quality)
/// can be tested. A research instrument for consenting pilot participants: never an integrity signal about a student. No model calls.
/// </summary>
public static class CoherenceCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || GadflyCli.IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string outPath = "coherence.csv";
        var inputs = new List<(string Condition, string Path)>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) { outPath = args[++i]; continue; }
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"gadfly coherence: unknown or incomplete option '{args[i]}'.");
                return 1;
            }
            // condition=path tags a folder or file with its study condition; a bare path is untagged.
            var eq = args[i].IndexOf('=');
            var tagged = eq > 0 && !Path.IsPathRooted(args[i][..eq]) && !args[i][..eq].Contains(Path.DirectorySeparatorChar)
                         && !args[i][..eq].Contains(Path.AltDirectorySeparatorChar);
            inputs.Add(tagged ? (args[i][..eq].Trim(), args[i][(eq + 1)..]) : ("", args[i]));
        }

        var files = new List<(string Condition, string Session)>();
        foreach (var (condition, path) in inputs)
        {
            if (Directory.Exists(path))
                files.AddRange(Directory.EnumerateFiles(path, "*.session.json", SearchOption.AllDirectories).Select(f => (condition, f)));
            else if (File.Exists(path)) files.Add((condition, path));
            else
            {
                Console.Error.WriteLine($"gadfly coherence: '{path}' is not a file or folder.");
                return 1;
            }
        }
        if (files.Count == 0)
        {
            Console.Error.WriteLine("gadfly coherence: give at least one *.session.json file or a folder of them.");
            return 1;
        }

        var rows = new List<SessionCoherence>();
        foreach (var (condition, file) in files.Distinct().OrderBy(f => f.Condition, StringComparer.Ordinal).ThenBy(f => f.Session, StringComparer.OrdinalIgnoreCase))
        {
            var ledgerPath = LedgerFor(file);
            if (!File.Exists(ledgerPath))
            {
                Console.Error.WriteLine($"  skipped {file}: no claim ledger beside it ({Path.GetFileName(ledgerPath)}).");
                continue;
            }
            try
            {
                var session = SessionTranscriptSerializer.Deserialize(await File.ReadAllTextAsync(file));
                var ledger = ClaimLedger.FromJson(await File.ReadAllTextAsync(ledgerPath));
                rows.Add(CoherenceMeasures.Measure(session, ledger, Path.GetFileName(file), condition));
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                Console.Error.WriteLine($"  skipped {file}: {e.Message}");
            }
        }
        if (rows.Count == 0)
        {
            Console.Error.WriteLine("gadfly coherence: no session could be read.");
            return 1;
        }

        await File.WriteAllTextAsync(outPath, CoherenceMeasures.ToCsv(rows));
        Console.WriteLine($"{rows.Count} session(s); one row each written to {outPath}.");
        Console.WriteLine();
        Console.WriteLine($"  {"condition",-12} {"sessions",8} {"mean answer",12} {"contr./10 claims",17} {"confronted",11} {"failed",7} {"resolved",9}");
        foreach (var s in CoherenceMeasures.Summarise(rows))
        {
            Console.WriteLine($"  {(s.Condition.Length == 0 ? "(untagged)" : s.Condition),-12} {s.Sessions,8} {F(s.MeanAnswerScore),12} " +
                              $"{F(s.MeanContradictionsPerTenClaims),17} {s.Confronted,11} {s.FailedResolutions,7} {Pct(s.ResolutionRate),9}");
        }
        Console.WriteLine();
        Console.WriteLine("Descriptive only. H1 compares conditions at equal answer quality, so match or adjust on mean answer score");
        Console.WriteLine("before reading a difference in contradictions; the study's analysis plan decides the test.");
        return 0;
    }

    /// <summary>"20261122-101500.session.json" → "20261122-101500.ledger.json"; any other name gets ".ledger.json" in place of ".json".</summary>
    internal static string LedgerFor(string sessionFile)
    {
        var dir = Path.GetDirectoryName(sessionFile) ?? "";
        var name = Path.GetFileName(sessionFile);
        var stem = name.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase) ? name[..^".session.json".Length]
            : name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name[..^".json".Length] : name;
        return Path.Combine(dir, stem + ".ledger.json");
    }

    private static string F(double? v) => v is { } x ? x.ToString("0.00", CultureInfo.InvariantCulture) : "n/a";
    private static string Pct(double? v) => v is { } x ? (x * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "n/a";

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly coherence [condition=]<session.json | folder> ... [--out coherence.csv]");
        Console.WriteLine();
        Console.WriteLine("  For the pilot's relay arm (hypothesis H1): ledger-based coherence of each saved session, by study condition.");
        Console.WriteLine("  Tag each folder with its condition, for example: gadfly coherence honest=runs/honest relay=runs/relay");
        Console.WriteLine("  Folders are searched for *.session.json; each needs its *.ledger.json beside it (gadfly exam writes both).");
        Console.WriteLine("  Writes one row per session (claims, contradictions, confrontations, failed resolutions, mean answer score)");
        Console.WriteLine("  and prints each condition's counts and means. A research measure, never a finding about a student.");
    }
}
