using System.Globalization;
using Gadfly.Engine;

namespace Gadfly.Cli;

/// <summary>
/// gadfly reliability (GAD-T443): from a double-marked pilot (every rater marks every student on every objective), estimates
/// how much of the score variance is the students, the objectives, the raters and their interactions, and reports the
/// smallest number of objectives whose average reaches a chosen reliability: the evidence for
/// SessionPolicy.MinSummativeObjectives (ElenchusAtScale 3.7, 7.1). No model calls.
/// </summary>
public static class ReliabilityCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || GadflyCli.IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var ci = CultureInfo.InvariantCulture;
        string? path = null;
        var target = ReliabilityStudy.DefaultTarget;
        int? raters = null;
        var max = ReliabilityStudy.DefaultMaxObjectives;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--target" when i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, ci, out var t) && t is > 0 and < 1:
                    target = t; i++; break;
                case "--raters" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, ci, out var r) && r >= 1:
                    raters = r; i++; break;
                case "--max-objectives" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, ci, out var m) && m >= 1:
                    max = m; i++; break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal) || path is not null)
                    {
                        Console.Error.WriteLine($"gadfly reliability: unknown or incomplete option '{args[i]}'. --target takes a number between 0 and 1; --raters and --max-objectives take whole numbers of at least 1.");
                        return 1;
                    }
                    path = args[i];
                    break;
            }
        }
        if (path is null || !File.Exists(path))
        {
            Console.Error.WriteLine($"gadfly reliability: give a CSV of pilot grades{(path is null ? "" : $" ('{path}' was not found)")}.");
            return 1;
        }

        string text;
        try { text = await File.ReadAllTextAsync(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"gadfly reliability: cannot read {path}: {e.Message}");
            return 1;
        }
        var grades = ReliabilityStudy.FromCsv(text, out var problems);
        foreach (var p in problems) Console.Error.WriteLine($"  {p}");

        ReliabilityResult result;
        try { result = ReliabilityStudy.Analyse(grades, target, raters, max); }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine($"gadfly reliability: {e.Message}");
            return 1;
        }

        var v = result.Components;
        Console.WriteLine($"Pilot: {result.Students} students x {result.Objectives} objectives x {result.Raters} raters " +
                          $"({result.RowsUsed} scores used of {result.RowsRead} rows read).");
        if (result.Dropped.Count > 0) Console.WriteLine($"Left out to make the design complete: {string.Join(", ", result.Dropped)}.");
        Console.WriteLine();
        Console.WriteLine("Where the score differences come from (variance components):");
        var total = v.Total;
        void Row(string name, double value, string meaning) =>
            Console.WriteLine($"  {name,-22} {value.ToString("0.####", ci),10}  {(total > 0 ? (value / total * 100).ToString("0", ci) : "0"),3}%  {meaning}");
        Row("student", v.Person, "real differences between students (the signal)");
        Row("objective", v.Objective, "some objectives are harder for everyone");
        Row("rater", v.Rater, "some raters mark everyone higher or lower");
        Row("student x objective", v.PersonObjective, "students strong on one objective, weak on another");
        Row("student x rater", v.PersonRater, "raters who favour particular students");
        Row("objective x rater", v.ObjectiveRater, "raters harsh on particular objectives");
        Row("residual", v.Residual, "everything else, including plain marking error");
        Console.WriteLine();

        Console.WriteLine($"Reliability of the average over N objectives, each marked by {result.ProjectedRaters} rater(s):");
        Console.WriteLine("  objectives   relative   absolute (Phi)");
        foreach (var pt in result.Projection)
        {
            var mark = pt.Objectives == result.MinimumObjectives ? "  <- first to reach the target" : "";
            Console.WriteLine($"  {pt.Objectives,10}   {pt.Relative.ToString("0.00", ci),8}   {pt.Absolute.ToString("0.00", ci),8}{mark}");
        }
        Console.WriteLine();

        var targetText = target.ToString("0.##", ci);
        if (result.MinimumObjectives is { } min)
        {
            Console.WriteLine($"Smallest number of objectives reaching {targetText} (relative, for ranking students): {min}.");
            Console.WriteLine(result.MinimumObjectivesAbsolute is { } abs
                ? $"For a fixed pass mark (absolute, Phi) it is {abs}."
                : $"The absolute coefficient (Phi, for a fixed pass mark) does not reach {targetText} within {max} objectives.");
            Console.WriteLine($"Suggested minimum for summative use (SessionPolicy.MinSummativeObjectives): {Math.Max(min, result.MinimumObjectivesAbsolute ?? min)}" +
                              (result.MinimumObjectivesAbsolute is null ? $" at least (Phi needs more than {max})" : "") +
                              $"; the current default is {ExamObjectives.DefaultMinSummativeObjectives}.");
        }
        else
        {
            Console.WriteLine($"No number of objectives up to {max} reaches {targetText}. Examinations from this system should be reported as formative " +
                              "until the marking or the objectives change; try --max-objectives with a larger number to see how far off it is.");
        }
        Console.WriteLine();
        Console.WriteLine("Caveats:");
        foreach (var c in result.Caveats) Console.WriteLine($"  - {c}");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("gadfly reliability <grades.csv> [--target 0.8] [--raters N] [--max-objectives 20]");
        Console.WriteLine();
        Console.WriteLine("  Estimates, from a double-marked pilot, how many objectives an examination needs for its score to reach a");
        Console.WriteLine("  chosen reliability, to set the policy minimum for summative use. The CSV needs the columns student,");
        Console.WriteLine("  objective, rater and score, one row per rater per answer; every rater should mark every student on every");
        Console.WriteLine("  objective (students, objectives or raters with gaps are left out and named). At least 2 of each are needed.");
        Console.WriteLine("  --raters projects for a different number of raters per answer than the pilot had.");
    }
}
