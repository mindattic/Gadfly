using Gadfly.Corpus;
using Gadfly.Ingestion;

namespace Gadfly.Cli;

public static class IngestCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Any(GadflyCli.IsHelp))
        {
            PrintUsage();
            return Task.FromResult(0);
        }

        string? projectFilter = null;
        DateTimeOffset? since = null;
        var dryRun = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--project":
                    if (i + 1 < args.Length) projectFilter = ClaudeCodeProjectScanner.FolderNameFor(args[++i]);
                    break;
                case "--since":
                    if (i + 1 < args.Length && DateTimeOffset.TryParse(args[++i], out var parsedSince)) since = parsedSince;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
            }
        }

        var store = new CorpusStore();
        var totalFound = 0;
        var totalAppended = 0;

        foreach (var project in ClaudeCodeProjectScanner.ScanAll())
        {
            if (projectFilter is not null && !string.Equals(project.ProjectFolder, projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var jsonlPath in project.JsonlPaths)
            {
                IEnumerable<CorpusMessage> messages = ClaudeCodeTranscriptReader.ReadFile(jsonlPath);
                if (since is { } s) messages = messages.Where(m => m.TimestampUtc >= s);

                var list = messages.ToList();
                totalFound += list.Count;

                if (!dryRun)
                    totalAppended += store.AppendNew(list);
            }
        }

        if (dryRun)
            Console.WriteLine($"ingest --dry-run: found {totalFound} human/assistant messages across scanned transcripts (nothing written).");
        else
            Console.WriteLine($"ingest: found {totalFound} messages, appended {totalAppended} new (skipped {totalFound - totalAppended} already-ingested).");

        return Task.FromResult(0);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly ingest [opts]");
        Console.WriteLine();
        Console.WriteLine("  Scan ~/.claude/projects/**/*.jsonl and append new messages to the corpus.");
        Console.WriteLine("  Idempotent — safe to re-run; already-ingested messages are skipped.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --project <cwd>   Only ingest the project transcript folder matching this working directory.");
        Console.WriteLine("  --since <date>    Only ingest messages timestamped on/after this date.");
        Console.WriteLine("  --dry-run         Report counts without writing to the corpus.");
    }
}
