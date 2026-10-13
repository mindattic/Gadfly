using System.Text.Json;
using Gadfly.Corpus;

namespace Gadfly.Cli;

public static class CorpusStatusCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || GadflyCli.IsHelp(args[0]))
        {
            PrintUsage();
            return Task.FromResult(args.Length == 0 ? 1 : 0);
        }

        if (!args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("gadfly corpus: unknown subcommand. Try 'gadfly corpus status'.");
            PrintUsage();
            return Task.FromResult(1);
        }

        var verbose = args.Skip(1).Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase));

        var repository = new CorpusRepository(new CorpusStore());
        var all = repository.GetAll();
        var bySource = all.GroupBy(m => m.Source).ToDictionary(g => g.Key, g => g.Count());

        Console.WriteLine($"corpus: {all.Count} total messages");
        foreach (var (source, count) in bySource)
            Console.WriteLine($"  {source}: {count}");

        if (all.Count > 0)
            Console.WriteLine($"  last message timestamp: {all.Max(m => m.TimestampUtc):u}");

        var digestCachePath = Path.Combine(CorpusStore.DefaultRoot, "digest-cache.json");
        var cacheFresh = false;
        if (File.Exists(digestCachePath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(digestCachePath));
            cacheFresh = all.Count > 0 && doc.RootElement.TryGetProperty(repository.CorpusHash(), out _);
        }
        Console.WriteLine($"  digest cache: {(cacheFresh ? "fresh" : "stale or missing")}");

        if (verbose)
            Console.WriteLine($"  corpus hash: {repository.CorpusHash()}");

        return Task.FromResult(0);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly corpus status [--verbose]");
        Console.WriteLine();
        Console.WriteLine("  Show corpus message counts by source, last-ingest time, and digest-cache freshness.");
    }
}
