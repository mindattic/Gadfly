using Gadfly.Corpus;
using Gadfly.Mining;
using Gadfly.Socrates;
using MindAttic.Legion;

namespace Gadfly.Cli;

public static class InterrogateCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(GadflyCli.IsHelp))
        {
            PrintUsage();
            return 0;
        }

        var tier = LegionTierResolver.DefaultTier;
        var maxTurns = 20;
        var quorum = Quorum.Plurality;
        List<string>? providerOverride = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--tier":
                    if (i + 1 < args.Length && Enum.TryParse<ModelTier>(args[++i], ignoreCase: true, out var parsedTier)) tier = parsedTier;
                    break;
                case "--max-turns":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var mt) && mt > 0) maxTurns = mt;
                    break;
                case "--quorum":
                    if (i + 1 < args.Length && Enum.TryParse<Quorum>(args[++i], ignoreCase: true, out var q)) quorum = q;
                    break;
                case "--voters":
                    if (i + 1 < args.Length)
                        providerOverride = args[++i].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                    break;
                case "--single-voter":
                    if (i + 1 < args.Length) providerOverride = new List<string> { args[++i] };
                    break;
            }
        }

        var config = GadflyConfiguration.Build(tier, providerOverride: providerOverride);
        using var client = new LegionVotingClient(config);

        var repository = new CorpusRepository(new CorpusStore());
        if (repository.GetHumanMessages().Count == 0)
        {
            Console.Error.WriteLine("interrogate: corpus is empty. Run 'gadfly ingest' (and/or 'gadfly import <zip>') first.");
            return 1;
        }

        Console.WriteLine("Building corpus digest… (cached after the first run on an unchanged corpus)");
        var digest = await new ContextBuilder(repository, new CorpusSummarizer(client)).BuildCorpusDigestAsync();

        var session = new InterrogationSession(client, quorum, maxTurns);
        Console.WriteLine();
        Console.WriteLine("Type your reply after each question. Declare \"Aporia\" when you reach it.");
        Console.WriteLine();

        await session.RunAsync(
            digest,
            writeQuestion: q =>
            {
                Console.WriteLine($"Socrates: {q}");
                Console.WriteLine();
                return Task.CompletedTask;
            },
            readHumanReply: () =>
            {
                Console.Write("You: ");
                return Task.FromResult(Console.ReadLine() ?? "");
            });

        var path = session.SaveTranscript();
        Console.WriteLine();
        Console.WriteLine($"Session saved to {path}. Run 'gadfly reflect --last' to continue.");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly interrogate [opts]");
        Console.WriteLine();
        Console.WriteLine("  Prompt 1 — The Socratic Interrogation. A multi-turn terminal REPL: each turn");
        Console.WriteLine("  is a real Legion vote across the panel, drafting and fusing the next probing");
        Console.WriteLine("  question against your mined corpus and the growing dialogue. Ends when you");
        Console.WriteLine("  type \"Aporia\" or --max-turns is reached.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --tier <t>          low | medium | high | higher | highest (default high).");
        Console.WriteLine("  --max-turns N       Stop after N questions if Aporia isn't reached (default 20).");
        Console.WriteLine("  --quorum <q>        plurality | simplemajority | twothirds | unanimous (default plurality).");
        Console.WriteLine("  --voters a,b,c      Narrow the panel within the trusted set (claude, openai, gemini, deepseek).");
        Console.WriteLine("  --single-voter <id> Cheap escape hatch: run the whole interrogation through one provider.");
    }
}
