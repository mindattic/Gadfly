using System.Diagnostics;

namespace Gadfly.Tour;

public enum Outcome { Pass, Fail, Skip }

/// <summary>A stage whose feature is not built yet: printed as SKIP, never as a failure.</summary>
public sealed class NotBuiltException(string ticket, string what) : Exception($"not built: {ticket}{(what.Length > 0 ? " - " + what : "")}")
{
    public string Ticket { get; } = ticket;
}

/// <summary>A failed expectation, with the reason printed on the checklist.</summary>
public sealed class TourAssertException(string message) : Exception(message);

/// <summary>The tour's checklist: one line per stage (✓ / ✗ / SKIP) and the reasons.</summary>
public sealed class Checklist
{
    private readonly List<(string Stage, Outcome Outcome, string Detail, TimeSpan Took)> _rows = [];

    public bool AnyFailed => _rows.Any(r => r.Outcome == Outcome.Fail);

    public async Task<bool> RunAsync(string stage, Func<Task<string>> body)
    {
        var watch = Stopwatch.StartNew();
        Outcome outcome;
        string detail;
        try
        {
            detail = await body();
            outcome = Outcome.Pass;
        }
        catch (NotBuiltException nb)
        {
            outcome = Outcome.Skip;
            detail = nb.Message;
        }
        catch (Exception ex)
        {
            outcome = Outcome.Fail;
            detail = ex is TourAssertException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
        }
        _rows.Add((stage, outcome, detail, watch.Elapsed));
        Print(stage, outcome, detail, watch.Elapsed);
        return outcome == Outcome.Pass;
    }

    /// <summary>Records a stage that can't run because an earlier one it depends on failed.</summary>
    public void Blocked(string stage, string because)
    {
        _rows.Add((stage, Outcome.Fail, "blocked: " + because, TimeSpan.Zero));
        Print(stage, Outcome.Fail, "blocked: " + because, TimeSpan.Zero);
    }

    private static void Print(string stage, Outcome outcome, string detail, TimeSpan took)
    {
        var (mark, color) = outcome switch
        {
            Outcome.Pass => ("✓", ConsoleColor.Green),
            Outcome.Fail => ("✗", ConsoleColor.Red),
            _ => ("SKIP", ConsoleColor.DarkYellow),
        };
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write($"{mark,-5}");
        Console.ForegroundColor = previous;
        Console.WriteLine($"{stage} ({took.TotalSeconds:0.0}s)");
        if (detail.Length > 0) Console.WriteLine($"      {detail}");
    }

    public void Summary()
    {
        Console.WriteLine();
        Console.WriteLine($"Tour: {_rows.Count(r => r.Outcome == Outcome.Pass)} passed, {_rows.Count(r => r.Outcome == Outcome.Fail)} failed, " +
            $"{_rows.Count(r => r.Outcome == Outcome.Skip)} skipped (not built yet).");
    }
}

public static class Expect
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new TourAssertException(message);
    }

    public static T NotNull<T>(T? value, string message) where T : class =>
        value ?? throw new TourAssertException(message);

    public static T Value<T>(T? value, string message) where T : struct =>
        value ?? throw new TourAssertException(message);
}
