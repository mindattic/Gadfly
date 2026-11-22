using System.Globalization;
using System.Text.Json;
using Gadfly.Engine;

namespace Gadfly.Cli;

/// <summary>
/// gadfly contradiction-audit (GAD-T440): estimates the contradiction detector's false-flag and miss rates on pilot data. First
/// run it over saved claim ledgers (*.ledger.json: the CLI writes one beside each transcript, the server keeps one per session)
/// to write a stratified sample of claim pairs for a person to label; then run it again with --labels on the labelled file to
/// get the rates. No model calls.
/// </summary>
public static class ContradictionAuditCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || GadflyCli.IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string? labelsPath = null, outPath = null;
        var perStratum = 50;
        var seed = 1;
        var inputs = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--labels" when i + 1 < args.Length: labelsPath = args[++i]; break;
                case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
                case "--sample" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0:
                    perStratum = n; i++; break;
                case "--seed" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s):
                    seed = s; i++; break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"gadfly contradiction-audit: unknown or incomplete option '{args[i]}'.");
                        return 1;
                    }
                    inputs.Add(args[i]);
                    break;
            }
        }

        var files = new List<string>();
        foreach (var arg in inputs)
        {
            if (Directory.Exists(arg)) files.AddRange(Directory.EnumerateFiles(arg, "*.ledger.json", SearchOption.AllDirectories));
            else if (File.Exists(arg)) files.Add(arg);
            else
            {
                Console.Error.WriteLine($"gadfly contradiction-audit: '{arg}' is not a file or folder.");
                return 1;
            }
        }
        if (files.Count == 0)
        {
            Console.Error.WriteLine("gadfly contradiction-audit: give at least one *.ledger.json file or a folder of them.");
            return 1;
        }

        var pairs = new List<ClaimPairRow>();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            try { pairs.AddRange(ContradictionAudit.Pairs(ClaimLedger.FromJson(await File.ReadAllTextAsync(file)), Path.GetFileName(file))); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"  skipped {file}: {e.Message}");
            }
        }

        if (labelsPath is null)
        {
            var sample = ContradictionAudit.Sample(pairs, perStratum, seed);
            outPath ??= "contradiction-sample.csv";
            await File.WriteAllTextAsync(outPath, ContradictionAudit.ToCsv(sample));
            Console.WriteLine($"{pairs.Count} claim pair(s) over {files.Count} ledger(s):");
            foreach (var s in Enum.GetValues<ContradictionStratum>())
                Console.WriteLine($"  {ContradictionAudit.StratumName(s),-14} {pairs.Count(p => p.Stratum == s)}");
            Console.WriteLine();
            Console.WriteLine($"Wrote {sample.Count} pair(s) to {outPath}. Have a person who did not see the detector's verdict fill");
            Console.WriteLine("the human_contradiction column with yes or no (can both claims be true at once, in the sense the student");
            Console.WriteLine("meant them?), then run this again with the same ledgers and --labels <that file>.");
            return 0;
        }

        IReadOnlyList<ClaimPairRow> labelled;
        IReadOnlyList<string> problems;
        try { labelled = ContradictionAudit.FromCsv(await File.ReadAllTextAsync(labelsPath), out problems); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"gadfly contradiction-audit: cannot read {labelsPath}: {e.Message}");
            return 1;
        }
        foreach (var p in problems) Console.Error.WriteLine($"  {p}");
        var rates = ContradictionAudit.Rates(pairs, labelled);
        Console.WriteLine($"Contradiction detector over {pairs.Count} claim pair(s), {labelled.Count(l => l.HumanSaysContradiction is not null)} labelled:");
        foreach (var s in Enum.GetValues<ContradictionStratum>())
        {
            var (n, yes) = rates.Labels[s];
            Console.WriteLine($"  {ContradictionAudit.StratumName(s),-14} {rates.Population[s],6} pair(s), {n} labelled, {yes} called a contradiction");
        }
        Console.WriteLine();
        Console.WriteLine($"  false-flag rate (flagged pairs that are not contradictions): {Pct(rates.FalseFlagRate)}");
        Console.WriteLine($"  miss rate (real contradictions the detector did not flag):   {Pct(rates.MissRate)}");
        if (rates.MissRate is null)
            Console.WriteLine("  The miss rate needs labels from every stratum that has pairs.");
        Console.WriteLine();
        Console.WriteLine("The miss rate weights each stratum's labelled sample by its size; with small samples it is a rough estimate.");
        return 0;
    }

    private static string Pct(double? rate) => rate is { } r ? (r * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "n/a";

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly contradiction-audit <ledger.json | folder> ... [--sample N] [--seed S] [--out sample.csv]");
        Console.WriteLine("gadfly contradiction-audit <ledger.json | folder> ... --labels labelled.csv");
        Console.WriteLine();
        Console.WriteLine("  Estimates how often the contradiction detector flags a pair that is not a contradiction (false flags)");
        Console.WriteLine("  and how often it stays silent on one that is (misses). The first form writes every flagged and possible");
        Console.WriteLine("  pair plus N (default 50) random checked-clear and N never-checked pairs to a CSV for a person to label;");
        Console.WriteLine("  the second reads the labels back and prints the rates. Folders are searched for *.ledger.json.");
    }
}
