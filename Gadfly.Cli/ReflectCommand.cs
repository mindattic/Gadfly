using Gadfly.Corpus;
using Gadfly.Mining;
using Gadfly.Socrates;
using MindAttic.Legion;

namespace Gadfly.Cli;

public static class ReflectCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(GadflyCli.IsHelp))
        {
            PrintUsage();
            return 0;
        }

        var tier = LegionTierResolver.DefaultTier;
        var useLast = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--tier":
                    if (i + 1 < args.Length && Enum.TryParse<ModelTier>(args[++i], ignoreCase: true, out var parsedTier)) tier = parsedTier;
                    break;
                case "--last":
                    useLast = true;
                    break;
            }
        }

        var config = GadflyConfiguration.Build(tier);
        using var client = new LegionVotingClient(config);

        var repository = new CorpusRepository(new CorpusStore());
        if (repository.GetHumanMessages().Count == 0)
        {
            Console.Error.WriteLine("reflect: corpus is empty. Run 'gadfly ingest' (and/or 'gadfly import <zip>') first.");
            return 1;
        }

        var digest = await new ContextBuilder(repository, new CorpusSummarizer(client)).BuildCorpusDigestAsync();

        IReadOnlyList<InterrogationTurn>? priorInterrogation = null;
        if (useLast)
        {
            priorInterrogation = InterrogationSession.LoadLastTranscript();
            if (priorInterrogation.Count == 0)
                Console.Error.WriteLine("reflect: --last requested but no saved interrogation session was found; reflecting on the corpus alone.");
        }

        var reflection = await new ReflectionRunner(client).RunAsync(digest, priorInterrogation);
        Console.WriteLine(reflection);
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly reflect [opts]");
        Console.WriteLine();
        Console.WriteLine("  Prompt 2 — The Final Reflection. One-shot.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --tier <t>   low | medium | high | higher | highest (default high).");
        Console.WriteLine("  --last       Fold in the most recently saved 'gadfly interrogate' transcript.");
    }
}
