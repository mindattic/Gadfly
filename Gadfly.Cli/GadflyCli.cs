namespace Gadfly.Cli;

public static class GadflyCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return command switch
        {
            "ingest" => await IngestCommand.RunAsync(rest),
            "import" => await ImportCommand.RunAsync(rest),
            "corpus" => await CorpusStatusCommand.RunAsync(rest),
            "interrogate" => await InterrogateCommand.RunAsync(rest),
            "reflect" => await ReflectCommand.RunAsync(rest),
            "examine" => await ExamineCommand.RunAsync(rest),
            "version" => PrintVersion(),
            _ => Unknown(command),
        };
    }

    internal static bool IsHelp(string a) => a is "-h" or "--help" or "help" or "/?";

    private static int PrintVersion()
    {
        Console.WriteLine("gadfly 1.0.0");
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"gadfly: unknown command '{command}'. Run 'gadfly --help' for usage.");
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly <command> [opts]");
        Console.WriteLine();
        Console.WriteLine("  Mines your own Claude Code transcripts and claude.ai export into a corpus, then");
        Console.WriteLine("  cross-examines you Socrates-style through MindAttic.Legion's multi-LLM voting");
        Console.WriteLine("  until you reach Aporia.");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  ingest [opts]        Scan ~/.claude/projects/**/*.jsonl into the corpus.");
        Console.WriteLine("  import <zip> [opts]  Import a claude.ai data export.");
        Console.WriteLine("  corpus status        Show corpus message counts and digest-cache freshness.");
        Console.WriteLine("  interrogate [opts]   Prompt 1 — Socratic Elenchus REPL until you reach Aporia.");
        Console.WriteLine("  reflect [opts]       Prompt 2 — one-shot Final Reflection.");
        Console.WriteLine("  examine [opts]       Prompt 3 — Socrates/Confucius/Buddha, three perspectives.");
        Console.WriteLine("  version              Print the gadfly version.");
        Console.WriteLine();
        Console.WriteLine("Run 'gadfly <command> --help' for command-specific options.");
    }
}
