using System.Globalization;
using System.Text;

namespace Gadfly.Engine;

/// <summary>One double-marked pilot grade: a rater's score for one student on one objective.</summary>
public sealed record PilotGrade(string Student, string Objective, string Rater, double Score);

/// <summary>
/// Estimated variance components of a person x objective x rater design (GAD-T443). Negative estimates are set to 0 (the
/// usual convention); <see cref="Negative"/> names the ones that were, since a large negative estimate is itself a sign the
/// pilot is too small or the design was not crossed.
/// </summary>
public sealed record VarianceComponents
{
    /// <summary>Students: real differences in general standing (the signal).</summary>
    public double Person { get; init; }
    /// <summary>Objectives: some objectives are harder for everyone (matters only for absolute decisions).</summary>
    public double Objective { get; init; }
    /// <summary>Raters: some raters mark everyone higher (matters only for absolute decisions).</summary>
    public double Rater { get; init; }
    /// <summary>Student x objective: content specificity, a student strong on one objective and weak on another.</summary>
    public double PersonObjective { get; init; }
    /// <summary>Student x rater: a rater who favours some students.</summary>
    public double PersonRater { get; init; }
    /// <summary>Objective x rater: a rater who is harsh on some objectives.</summary>
    public double ObjectiveRater { get; init; }
    /// <summary>The three-way interaction confounded with all remaining error.</summary>
    public double Residual { get; init; }
    public IReadOnlyList<string> Negative { get; init; } = [];

    public double Total => Person + Objective + Rater + PersonObjective + PersonRater + ObjectiveRater + Residual;
}

/// <summary>Reliability of the average over <see cref="Objectives"/> objectives, each marked by <see cref="Raters"/> raters.</summary>
public sealed record ReliabilityPoint(int Objectives, int Raters, double Relative, double Absolute);

/// <summary>What <see cref="ReliabilityStudy.Analyse"/> found: the design actually used, the components and the projection.</summary>
public sealed record ReliabilityResult
{
    public required int Students { get; init; }
    public required int Objectives { get; init; }
    public required int Raters { get; init; }
    /// <summary>Rows read, and rows left out to make the design fully crossed.</summary>
    public int RowsRead { get; init; }
    public int RowsUsed { get; init; }
    /// <summary>Students, objectives or raters dropped because their cells were incomplete.</summary>
    public IReadOnlyList<string> Dropped { get; init; } = [];
    public required VarianceComponents Components { get; init; }
    /// <summary>Coefficients for 1..max objectives at the projected number of raters.</summary>
    public required IReadOnlyList<ReliabilityPoint> Projection { get; init; }
    public required double Target { get; init; }
    public required int ProjectedRaters { get; init; }
    /// <summary>Smallest number of objectives whose relative coefficient reaches <see cref="Target"/>; null if none up to the maximum.</summary>
    public int? MinimumObjectives { get; init; }
    /// <summary>The same for the absolute coefficient (Phi).</summary>
    public int? MinimumObjectivesAbsolute { get; init; }
    /// <summary>Plain warnings about how far to trust the estimate.</summary>
    public IReadOnlyList<string> Caveats { get; init; } = [];
}

/// <summary>
/// Sets the minimum number of objectives for summative use from a double-marked pilot (GAD-T443; ElenchusAtScale 3.7, M11 and
/// 7.1: the variance components can come only from a pilot of the system itself, and the minimum is the smallest m with
/// rho(m, r) &gt;= rho*). A generalizability study of a fully crossed person x objective x rater random design: every student
/// answers the same objectives and every rater marks every answer. Components are estimated from the ANOVA mean squares
/// (expected-mean-square equations), negative estimates set to 0. The relative coefficient is
/// p / (p + po/m + pr/r + por,e/(m r)), the form of Section 3.7 with the student x rater term added; the absolute one (Phi)
/// also counts the objective, rater and objective x rater terms, for criterion-referenced decisions such as a pass mark.
/// <para>
/// Missing cells: the ANOVA estimator needs a balanced design, so incomplete data are made complete by dropping, not
/// imputing. Several grades in one cell are averaged. Then, while any cell is empty, the student, objective or rater with the
/// largest share of empty cells is dropped (students first on a tie, since a pilot has more of them). What was dropped is
/// reported. Pure: no model calls.
/// </para>
/// </summary>
public static class ReliabilityStudy
{
    public const double DefaultTarget = 0.8;
    public const int DefaultMaxObjectives = 20;

    public static ReliabilityResult Analyse(IReadOnlyList<PilotGrade> grades, double target = DefaultTarget, int? raters = null,
        int maxObjectives = DefaultMaxObjectives)
    {
        ArgumentNullException.ThrowIfNull(grades);
        if (target is <= 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(target), "The target reliability must be between 0 and 1.");
        maxObjectives = Math.Max(1, maxObjectives);

        // Average duplicates into one score per cell.
        var cells = grades
            .Where(g => !string.IsNullOrWhiteSpace(g.Student) && !string.IsNullOrWhiteSpace(g.Objective) && !string.IsNullOrWhiteSpace(g.Rater)
                        && double.IsFinite(g.Score))
            .GroupBy(g => (g.Student.Trim(), g.Objective.Trim(), g.Rater.Trim()))
            .ToDictionary(g => g.Key, g => g.Average(x => x.Score));
        var students = cells.Keys.Select(k => k.Item1).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var objectives = cells.Keys.Select(k => k.Item2).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var raterIds = cells.Keys.Select(k => k.Item3).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var dropped = new List<string>();

        while (students.Count > 0 && objectives.Count > 0 && raterIds.Count > 0)
        {
            double Missing(Func<(string, string, string), bool> of, int size) =>
                (double)(size - cells.Keys.Count(k => of(k) && students.Contains(k.Item1) && objectives.Contains(k.Item2) && raterIds.Contains(k.Item3))) / size;
            var worstStudent = students.Select(s => (Id: s, Share: Missing(k => k.Item1 == s, objectives.Count * raterIds.Count))).MaxBy(x => x.Share);
            var worstObjective = objectives.Select(o => (Id: o, Share: Missing(k => k.Item2 == o, students.Count * raterIds.Count))).MaxBy(x => x.Share);
            var worstRater = raterIds.Select(r => (Id: r, Share: Missing(k => k.Item3 == r, students.Count * objectives.Count))).MaxBy(x => x.Share);
            var worst = Math.Max(worstStudent.Share, Math.Max(worstObjective.Share, worstRater.Share));
            if (worst <= 0) break;
            if (worstStudent.Share >= worst) { students.Remove(worstStudent.Id); dropped.Add($"student {worstStudent.Id}"); }
            else if (worstObjective.Share >= worst) { objectives.Remove(worstObjective.Id); dropped.Add($"objective {worstObjective.Id}"); }
            else { raterIds.Remove(worstRater.Id); dropped.Add($"rater {worstRater.Id}"); }
        }

        int np = students.Count, no = objectives.Count, nr = raterIds.Count;
        if (np < 2 || no < 2 || nr < 2)
            throw new InvalidOperationException(
                $"A fully crossed design needs at least 2 students, 2 objectives and 2 raters with every rater marking every student on " +
                $"every objective; after dropping incomplete rows there are {np} student(s), {no} objective(s) and {nr} rater(s).");

        var x = new double[np, no, nr];
        for (var p = 0; p < np; p++)
            for (var o = 0; o < no; o++)
                for (var r = 0; r < nr; r++)
                    x[p, o, r] = cells[(students[p], objectives[o], raterIds[r])];

        var components = Estimate(x);
        var projectedRaters = Math.Max(1, raters ?? nr);
        var projection = Enumerable.Range(1, maxObjectives)
            .Select(m => new ReliabilityPoint(m, projectedRaters, Relative(components, m, projectedRaters), Absolute(components, m, projectedRaters)))
            .ToList();

        var caveats = new List<string>();
        if (np < 30) caveats.Add($"Only {np} students: variance components from a pilot this small are rough, so treat the minimum as a first estimate and re-run it as more double-marked exams come in.");
        if (no < 4) caveats.Add($"Only {no} objectives in the pilot: the student x objective term, which usually decides the answer, rests on few objectives.");
        if (components.Negative.Count > 0) caveats.Add($"Negative estimates set to 0: {string.Join(", ", components.Negative)}. Usually a sign of a small pilot.");
        if (components.Person <= 0) caveats.Add("No variance between students was found, so no number of objectives reaches the target: the pilot's scores do not separate students.");
        if (dropped.Count > 0) caveats.Add($"{dropped.Count} student(s), objective(s) or rater(s) were left out because not every rater marked every answer of theirs.");
        caveats.Add("The projection assumes new objectives and raters behave like the pilot's (random samples from the same kinds), and that raters mark independently, without seeing each other's or the panel's scores.");

        return new ReliabilityResult
        {
            Students = np, Objectives = no, Raters = nr,
            RowsRead = grades.Count, RowsUsed = np * no * nr, Dropped = dropped,
            Components = components, Projection = projection, Target = target, ProjectedRaters = projectedRaters,
            MinimumObjectives = projection.FirstOrDefault(pt => pt.Relative >= target)?.Objectives,
            MinimumObjectivesAbsolute = projection.FirstOrDefault(pt => pt.Absolute >= target)?.Objectives,
            Caveats = caveats,
        };
    }

    /// <summary>Relative (norm-referenced) generalizability coefficient for <paramref name="objectives"/> objectives and <paramref name="raters"/> raters.</summary>
    public static double Relative(VarianceComponents v, int objectives, int raters)
    {
        double m = Math.Max(1, objectives), r = Math.Max(1, raters);
        var error = v.PersonObjective / m + v.PersonRater / r + v.Residual / (m * r);
        return v.Person + error <= 0 ? 0 : v.Person / (v.Person + error);
    }

    /// <summary>Absolute (criterion-referenced) coefficient, Phi.</summary>
    public static double Absolute(VarianceComponents v, int objectives, int raters)
    {
        double m = Math.Max(1, objectives), r = Math.Max(1, raters);
        var error = v.Objective / m + v.Rater / r + v.ObjectiveRater / (m * r)
                    + v.PersonObjective / m + v.PersonRater / r + v.Residual / (m * r);
        return v.Person + error <= 0 ? 0 : v.Person / (v.Person + error);
    }

    /// <summary>ANOVA estimates of the seven components of a balanced p x o x r array.</summary>
    public static VarianceComponents Estimate(double[,,] x)
    {
        int np = x.GetLength(0), no = x.GetLength(1), nr = x.GetLength(2);
        if (np < 2 || no < 2 || nr < 2) throw new ArgumentException("Each facet needs at least two levels.", nameof(x));
        var mp = new double[np]; var mo = new double[no]; var mr = new double[nr];
        var mpo = new double[np, no]; var mpr = new double[np, nr]; var mor = new double[no, nr];
        double grand = 0;
        for (var p = 0; p < np; p++)
            for (var o = 0; o < no; o++)
                for (var r = 0; r < nr; r++)
                {
                    var v = x[p, o, r];
                    grand += v; mp[p] += v; mo[o] += v; mr[r] += v; mpo[p, o] += v; mpr[p, r] += v; mor[o, r] += v;
                }
        grand /= np * no * nr;
        for (var p = 0; p < np; p++) mp[p] /= no * nr;
        for (var o = 0; o < no; o++) mo[o] /= np * nr;
        for (var r = 0; r < nr; r++) mr[r] /= np * no;
        for (var p = 0; p < np; p++) for (var o = 0; o < no; o++) mpo[p, o] /= nr;
        for (var p = 0; p < np; p++) for (var r = 0; r < nr; r++) mpr[p, r] /= no;
        for (var o = 0; o < no; o++) for (var r = 0; r < nr; r++) mor[o, r] /= np;

        double ssP = 0, ssO = 0, ssR = 0, ssPO = 0, ssPR = 0, ssOR = 0, ssRes = 0;
        for (var p = 0; p < np; p++) ssP += Sq(mp[p] - grand);
        for (var o = 0; o < no; o++) ssO += Sq(mo[o] - grand);
        for (var r = 0; r < nr; r++) ssR += Sq(mr[r] - grand);
        ssP *= no * nr; ssO *= np * nr; ssR *= np * no;
        for (var p = 0; p < np; p++) for (var o = 0; o < no; o++) ssPO += Sq(mpo[p, o] - mp[p] - mo[o] + grand);
        for (var p = 0; p < np; p++) for (var r = 0; r < nr; r++) ssPR += Sq(mpr[p, r] - mp[p] - mr[r] + grand);
        for (var o = 0; o < no; o++) for (var r = 0; r < nr; r++) ssOR += Sq(mor[o, r] - mo[o] - mr[r] + grand);
        ssPO *= nr; ssPR *= no; ssOR *= np;
        for (var p = 0; p < np; p++)
            for (var o = 0; o < no; o++)
                for (var r = 0; r < nr; r++)
                    ssRes += Sq(x[p, o, r] - mpo[p, o] - mpr[p, r] - mor[o, r] + mp[p] + mo[o] + mr[r] - grand);

        double msP = ssP / (np - 1), msO = ssO / (no - 1), msR = ssR / (nr - 1);
        double msPO = ssPO / ((np - 1) * (no - 1)), msPR = ssPR / ((np - 1) * (nr - 1)), msOR = ssOR / ((no - 1) * (nr - 1));
        double msRes = ssRes / ((np - 1) * (no - 1) * (nr - 1));

        var negative = new List<string>();
        double Clamp(double v, string name) { if (v < -1e-12) negative.Add(name); return Math.Max(0, v); }
        return new VarianceComponents
        {
            Residual = Clamp(msRes, "residual"),
            PersonObjective = Clamp((msPO - msRes) / nr, "student x objective"),
            PersonRater = Clamp((msPR - msRes) / no, "student x rater"),
            ObjectiveRater = Clamp((msOR - msRes) / np, "objective x rater"),
            Person = Clamp((msP - msPO - msPR + msRes) / (no * nr), "student"),
            Objective = Clamp((msO - msPO - msOR + msRes) / (np * nr), "objective"),
            Rater = Clamp((msR - msPR - msOR + msRes) / (np * no), "rater"),
            Negative = negative,
        };
    }

    private static double Sq(double v) => v * v;

    /// <summary>
    /// Reads a CSV with the columns student, objective, rater and score (any order, a header row, extra columns ignored).
    /// Bad lines are reported in <paramref name="problems"/>, not thrown.
    /// </summary>
    public static IReadOnlyList<PilotGrade> FromCsv(string text, out IReadOnlyList<string> problems)
    {
        var errors = new List<string>();
        var rows = new List<PilotGrade>();
        var lines = ParseCsv(text ?? "");
        if (lines.Count == 0) { problems = ["The file is empty."]; return rows; }
        var header = lines[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        string[] needed = ["student", "objective", "rater", "score"];
        var missing = needed.Where(n => !header.Contains(n)).ToList();
        if (missing.Count > 0) { problems = [$"Missing column(s): {string.Join(", ", missing)}."]; return rows; }
        int s = header.IndexOf("student"), o = header.IndexOf("objective"), r = header.IndexOf("rater"), sc = header.IndexOf("score");
        for (var i = 1; i < lines.Count; i++)
        {
            var f = lines[i];
            if (f.All(string.IsNullOrWhiteSpace)) continue;
            string Get(int c) => c < f.Count ? f[c].Trim() : "";
            if (Get(s).Length == 0 || Get(o).Length == 0 || Get(r).Length == 0)
            {
                errors.Add($"Line {i + 1}: student, objective and rater must all be filled in.");
                continue;
            }
            if (!double.TryParse(Get(sc), NumberStyles.Float, CultureInfo.InvariantCulture, out var score) || !double.IsFinite(score))
            {
                errors.Add($"Line {i + 1}: score '{Get(sc)}' is not a number.");
                continue;
            }
            rows.Add(new PilotGrade(Get(s), Get(o), Get(r), score));
        }
        problems = errors;
        return rows;
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n': row.Add(field.ToString()); field.Clear(); rows.Add(row); row = []; break;
                default: field.Append(ch); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
