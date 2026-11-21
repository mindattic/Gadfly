using System.Globalization;
using System.Text.Json;
using Gadfly.Engine;

namespace Gadfly.Cli;

/// <summary>
/// gadfly readout-rate (GAD-T272): measures how often the recitation check's read-out pattern (a long, fluent spoken answer
/// after a long silent wait) fires over session transcripts. Fed transcripts from students known to be answering honestly,
/// every fire is a false flag, so the rate printed is the pattern's false flag rate. No model calls.
/// </summary>
public static class ReadOutRateCommand
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
                Console.Error.WriteLine($"gadfly readout-rate: '{arg}' is not a file or folder.");
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
            Console.Error.WriteLine("gadfly readout-rate: no readable session transcripts.");
            return Task.FromResult(1);
        }

        var m = RecitationCheck.Measure(sessions);
        Console.WriteLine($"Read-out pattern over {m.Sessions} session(s), {m.VoiceAnswers} spoken answer(s):");
        Console.WriteLine($"  long enough to check ({RecitationCheck.ReadOutMinWords}+ words): {m.EligibleAnswers}");
        Console.WriteLine($"  fired:                                {m.Fires} ({Pct(m.FireRate)} of checked answers)");
        Console.WriteLine($"  sessions with a fire:                 {m.SessionsFlagged} ({Pct(m.SessionRate)} of sessions)");
        Console.WriteLine($"  of the fires, suppressed (check off or accommodation): {m.SuppressedFires}");
        Console.WriteLine($"  students with extra time: {m.AccommodatedFires} fire(s) over {m.AccommodatedEligible} checked answer(s) ({Pct(m.AccommodatedFireRate)})");
        Console.WriteLine();
        Console.WriteLine("If these students were answering honestly, every fire is a false flag. The source-overlap check is not measured here.");
        return Task.FromResult(0);
    }

    private static string Pct(double? rate) => rate is { } r ? (r * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "n/a";

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly readout-rate <transcript.session.json | folder> ...");
        Console.WriteLine();
        Console.WriteLine("  Measures how often the recitation check's read-out pattern (a long, fluent spoken answer after a");
        Console.WriteLine("  long silent wait) fires. Give it transcripts from students known to be answering honestly (a pilot,");
        Console.WriteLine("  a practice run, an observed exam): every fire is then a false flag. Folders are searched for");
        Console.WriteLine("  *.session.json. Counts fires even where the check was off or suppressed by an accommodation.");
    }
}
