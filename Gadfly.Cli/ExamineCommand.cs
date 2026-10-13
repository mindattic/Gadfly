using Gadfly.Corpus;
using Gadfly.Mining;
using Gadfly.Socrates;
using MindAttic.Legion;

namespace Gadfly.Cli;

public static class ExamineCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(GadflyCli.IsHelp))
        {
            PrintUsage();
            return 0;
        }

        var tier = LegionTierResolver.DefaultTier;
        List<string>? providerOverride = null;
        string? outPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--tier":
                    if (i + 1 < args.Length && Enum.TryParse<ModelTier>(args[++i], ignoreCase: true, out var parsedTier)) tier = parsedTier;
                    break;
                case "--voters":
                    if (i + 1 < args.Length)
                    {
                        var requested = args[++i].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        if (requested.Length == 3) providerOverride = requested.ToList();
                        else Console.Error.WriteLine("examine: --voters needs exactly 3 provider ids (Socrates, Confucius, Buddha in order); ignoring, using defaults.");
                    }
                    break;
                case "--out":
                    if (i + 1 < args.Length) outPath = args[++i];
                    break;
            }
        }

        var config = GadflyConfiguration.Build(tier, providerOverride: providerOverride);
        using var client = new LegionVotingClient(config);

        var repository = new CorpusRepository(new CorpusStore());
        if (repository.GetHumanMessages().Count == 0)
        {
            Console.Error.WriteLine("examine: corpus is empty. Run 'gadfly ingest' (and/or 'gadfly import <zip>') first.");
            return 1;
        }

        var digest = await new ContextBuilder(repository, new CorpusSummarizer(client)).BuildCorpusDigestAsync();
        var perspectives = await new MasterExaminationRunner(client).RunAsync(digest, providerOverride);

        var sb = new System.Text.StringBuilder();
        foreach (var p in perspectives)
        {
            sb.AppendLine($"=== {p.Perspective} ({p.ProviderId}) ===");
            sb.AppendLine(p.Text);
            sb.AppendLine();
        }

        var output = sb.ToString();
        Console.WriteLine(output);

        if (outPath is not null)
        {
            await File.WriteAllTextAsync(outPath, output);
            Console.WriteLine($"Written to {outPath}");
        }

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly examine [opts]");
        Console.WriteLine();
        Console.WriteLine("  Prompt 3 — The Master Prompt. Examines you through three perspectives —");
        Console.WriteLine("  Socrates, Confucius, Buddha — each a separate LLM voter reading the same");
        Console.WriteLine("  corpus digest. Prints all three individual answers (no synthesized consensus).");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --tier <t>      low | medium | high | higher | highest (default high).");
        Console.WriteLine("  --voters a,b,c  Provider ids for Socrates,Confucius,Buddha in order (default claude,openai,gemini).");
        Console.WriteLine("  --out <file>    Also write the combined output to a file.");
    }
}
