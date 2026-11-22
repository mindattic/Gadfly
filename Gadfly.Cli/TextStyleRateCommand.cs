using System.Globalization;
using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Sources;

namespace Gadfly.Cli;

/// <summary>
/// gadfly textstyle-rate (GAD-T284): measures how often the text-style estimate (the panel's "reads like written or
/// AI-generated text read aloud" score) reaches the threshold at which a reviewer sees it as an integrity event. Fed
/// transcripts from students known to be answering honestly, every such answer is a false flag, so the rate printed is the
/// estimate's false-flag rate. Reads the estimates already stored in the transcripts; no model calls.
/// </summary>
public static class TextStyleRateCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || GadflyCli.IsHelp(args[0]))
        {
            PrintUsage();
            return Task.FromResult(args.Length == 0 ? 1 : 0);
        }

        var files = new List<string>();
        foreach (var arg in args)
        {
            if (Directory.Exists(arg)) files.AddRange(Directory.EnumerateFiles(arg, "*.session.json", SearchOption.AllDirectories));
            else if (File.Exists(arg)) files.Add(arg);
            else
            {
                Console.Error.WriteLine($"gadfly textstyle-rate: '{arg}' is not a file or folder.");
                return Task.FromResult(1);
            }
        }

        var sessions = new List<SocraticSession>();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            try { sessions.Add(SessionTranscriptSerializer.Deserialize(File.ReadAllText(file))); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"  skipped {file}: {e.Message}");
            }
        }
        if (sessions.Count == 0)
        {
            Console.Error.WriteLine("gadfly textstyle-rate: no readable session transcripts.");
            return Task.FromResult(1);
        }

        var m = TextStyleAssessor.Measure(sessions);
        var threshold = TextStyleAssessor.ReviewerThreshold.ToString("0.0#", CultureInfo.InvariantCulture);
        Console.WriteLine($"Text-style estimate over {m.Sessions} session(s), {m.Answers} answer(s):");
        Console.WriteLine($"  answers with an estimate:              {m.AssessedAnswers} (in {m.SessionsAssessed} session(s))");
        Console.WriteLine($"  at or above {threshold} (shown to reviewers): {m.Flagged} ({Pct(m.FlagRate)} of answers with an estimate)");
        Console.WriteLine($"  0.4 to below {threshold}:                  {m.Middling}");
        Console.WriteLine($"  sessions with an answer at {threshold}+:      {m.SessionsFlagged} ({Pct(m.SessionRate)} of sessions with an estimate)");
        Console.WriteLine();
        Console.WriteLine("If these students were answering honestly, every answer at or above the threshold is a false flag.");
        Console.WriteLine("Run it once per group (for example, by first language) to compare rates between groups.");
        return Task.FromResult(0);
    }

    private static string Pct(double? rate) => rate is { } r ? (r * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "n/a";

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly textstyle-rate <transcript.session.json | folder> ...");
        Console.WriteLine();
        Console.WriteLine("  Measures how often the text-style estimate (an unvalidated panel estimate that an answer was read aloud");
        Console.WriteLine("  from written or AI-generated text) reaches the threshold at which reviewers see it. Give it transcripts");
        Console.WriteLine("  from students known to be answering honestly (a pilot, a practice run, an observed exam): every answer");
        Console.WriteLine("  at or above the threshold is then a false flag. Folders are searched for *.session.json. Reads the");
        Console.WriteLine("  stored estimates; makes no model calls.");
    }
}
