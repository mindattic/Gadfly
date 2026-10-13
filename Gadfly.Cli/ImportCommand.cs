using Gadfly.Corpus;
using Gadfly.Ingestion;

namespace Gadfly.Cli;

public static class ImportCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || GadflyCli.IsHelp(args[0]))
        {
            PrintUsage();
            return Task.FromResult(args.Length == 0 ? 1 : 0);
        }

        var zipPath = args[0];
        var dryRun = args.Skip(1).Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<CorpusMessage> messages;
        try
        {
            messages = ClaudeAiExportImporter.Import(zipPath);
        }
        catch (ExportSchemaException ex)
        {
            Console.Error.WriteLine($"import: export schema mismatch — {ex.Message}");
            return Task.FromResult(2);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"import: {ex.Message}");
            return Task.FromResult(1);
        }

        if (dryRun)
        {
            Console.WriteLine($"import --dry-run: parsed {messages.Count} messages from {Path.GetFileName(zipPath)} (nothing written).");
            return Task.FromResult(0);
        }

        var store = new CorpusStore();
        var appended = store.AppendNew(messages);
        Console.WriteLine($"import: parsed {messages.Count} messages, appended {appended} new.");
        return Task.FromResult(0);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly import <zip> [opts]");
        Console.WriteLine();
        Console.WriteLine("  Import a claude.ai account data export (Settings → Privacy → Export data).");
        Console.WriteLine("  There is no live API for claude.ai conversation history — this export file is");
        Console.WriteLine("  the only ingestion path.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --dry-run   Report counts without writing to the corpus.");
    }
}
