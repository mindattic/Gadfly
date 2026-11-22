using System.Globalization;
using System.Text;

namespace Gadfly.Engine;

/// <summary>Where a pair of claims sits in the contradiction detector's record (GAD-T440).</summary>
public enum ContradictionStratum
{
    /// <summary>The panel compared the pair and a majority found a contradiction (put to the student).</summary>
    Flagged,
    /// <summary>The panel compared the pair and was split on a contradiction (shown to the reviewer as possible).</summary>
    Possible,
    /// <summary>The panel compared the pair and found it consistent (or could not decide).</summary>
    CheckedClear,
    /// <summary>The pair was never compared: too little word overlap, or over the per-answer cap.</summary>
    NotChecked,
}

/// <summary>One pair of a session's claims, as the detector saw it, with an optional human label.</summary>
public sealed record ClaimPairRow
{
    public required string Session { get; init; }
    public required string FirstClaimId { get; init; }
    public required string SecondClaimId { get; init; }
    public int FirstTurn { get; init; }
    public int SecondTurn { get; init; }
    public string FirstText { get; init; } = "";
    public string SecondText { get; init; } = "";
    public bool CrossConcept { get; init; }
    public double Overlap { get; init; }
    public required ContradictionStratum Stratum { get; init; }
    public double Confidence { get; init; }
    /// <summary>A human's reading: true contradiction, false not one, null not labelled.</summary>
    public bool? HumanSaysContradiction { get; init; }
}

/// <summary>The detector's error rates estimated from human labels, with the counts they rest on.</summary>
public sealed record ContradictionRates
{
    /// <summary>Pairs in each stratum (the population the labels are a sample of).</summary>
    public required IReadOnlyDictionary<ContradictionStratum, int> Population { get; init; }
    /// <summary>Labelled pairs in each stratum, and how many of them the human called contradictions.</summary>
    public required IReadOnlyDictionary<ContradictionStratum, (int Labelled, int Contradictions)> Labels { get; init; }
    /// <summary>Of the flagged pairs a human labelled, the share that were not contradictions; null with no labels.</summary>
    public double? FalseFlagRate { get; init; }
    /// <summary>
    /// The estimated share of all real contradictions the detector did not flag, weighting each stratum's labelled sample by
    /// the stratum's size; null when a stratum with pairs has no labels.
    /// </summary>
    public double? MissRate { get; init; }
    /// <summary>Estimated real contradictions in each unflagged stratum (its labelled share times its size).</summary>
    public IReadOnlyDictionary<ContradictionStratum, double> EstimatedMissed { get; init; } = new Dictionary<ContradictionStratum, double>();
}

/// <summary>
/// Estimates the contradiction detector's false-flag and miss rates on pilot data (GAD-T440; ElenchusAtScale 3.4, 4.4: the
/// detector's silence is "no contradiction detected", so how often it is silent on a real contradiction has to be measured).
/// <see cref="Pairs"/> lists every pair of a session's claims from different turns with what the detector did with it;
/// <see cref="Sample"/> draws a stratified sample for a human to label; <see cref="Rates"/> turns the labels into rates.
/// Pure: no model calls.
/// </summary>
public static class ContradictionAudit
{
    /// <summary>Every pair of claims from different turns in <paramref name="ledger"/>, with the detector's record of it.</summary>
    public static IReadOnlyList<ClaimPairRow> Pairs(ClaimLedger ledger, string session)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var checks = ledger.Checks.GroupBy(c => Key(c.FirstClaimId, c.SecondClaimId)).ToDictionary(g => g.Key, g => g.Last());
        var found = ledger.Contradictions.GroupBy(c => Key(c.FirstClaimId, c.SecondClaimId)).ToDictionary(g => g.Key, g => g.Last());
        var rows = new List<ClaimPairRow>();
        var claims = ledger.Claims.OrderBy(c => c.TurnIndex).ToList();
        for (var i = 0; i < claims.Count; i++)
        {
            for (var j = i + 1; j < claims.Count; j++)
            {
                var (a, b) = (claims[i], claims[j]);
                if (a.TurnIndex == b.TurnIndex) continue;
                var key = Key(a.Id, b.Id);
                var stratum = found.TryGetValue(key, out var f) ? (ClaimLedger.IsFirm(f) ? ContradictionStratum.Flagged : ContradictionStratum.Possible)
                    : checks.ContainsKey(key) ? ContradictionStratum.CheckedClear
                    : ContradictionStratum.NotChecked;
                rows.Add(new ClaimPairRow
                {
                    Session = session, FirstClaimId = a.Id, SecondClaimId = b.Id, FirstTurn = a.TurnIndex + 1, SecondTurn = b.TurnIndex + 1,
                    FirstText = a.Text, SecondText = b.Text, CrossConcept = a.ConceptId != b.ConceptId,
                    Overlap = checks.TryGetValue(key, out var c) ? c.Overlap : Math.Round(ClaimAnalyzer.Jaccard(a.Text, b.Text), 3),
                    Stratum = stratum, Confidence = f?.Confidence ?? c?.Confidence ?? 0,
                });
            }
        }
        return rows;
    }

    /// <summary>
    /// A sample for human labelling: every flagged and possible pair, and up to <paramref name="perStratum"/> pairs drawn at
    /// random (fixed <paramref name="seed"/>) from the checked-clear and the not-checked pairs, the not-checked ones taken
    /// mostly from the closest in wording so the sample is not all obvious non-contradictions.
    /// </summary>
    public static IReadOnlyList<ClaimPairRow> Sample(IReadOnlyList<ClaimPairRow> pairs, int perStratum, int seed = 1)
    {
        var random = new Random(seed);
        var picked = pairs.Where(p => p.Stratum is ContradictionStratum.Flagged or ContradictionStratum.Possible).ToList();
        picked.AddRange(pairs.Where(p => p.Stratum == ContradictionStratum.CheckedClear).OrderBy(_ => random.Next()).Take(perStratum));
        picked.AddRange(pairs.Where(p => p.Stratum == ContradictionStratum.NotChecked).OrderBy(_ => random.Next()).Take(perStratum));
        return picked;
    }

    /// <summary>The rates from the labelled rows in <paramref name="labelled"/>, against the population <paramref name="all"/>.</summary>
    public static ContradictionRates Rates(IReadOnlyList<ClaimPairRow> all, IReadOnlyList<ClaimPairRow> labelled)
    {
        var population = Enum.GetValues<ContradictionStratum>().ToDictionary(s => s, s => all.Count(p => p.Stratum == s));
        var labels = Enum.GetValues<ContradictionStratum>().ToDictionary(s => s, s =>
        {
            var l = labelled.Where(p => p.Stratum == s && p.HumanSaysContradiction is not null).ToList();
            return (l.Count, l.Count(p => p.HumanSaysContradiction == true));
        });
        var flagged = labels[ContradictionStratum.Flagged];
        double? falseFlag = flagged.Count == 0 ? null : (double)(flagged.Count - flagged.Item2) / flagged.Count;

        // Real contradictions per stratum: the labelled share scaled to the stratum's size.
        var estimated = new Dictionary<ContradictionStratum, double>();
        var unknown = false;
        foreach (var s in Enum.GetValues<ContradictionStratum>())
        {
            if (population[s] == 0) { estimated[s] = 0; continue; }
            var (n, yes) = labels[s];
            if (n == 0) { unknown = true; continue; }
            estimated[s] = (double)yes / n * population[s];
        }
        double? miss = null;
        if (!unknown)
        {
            var caught = estimated[ContradictionStratum.Flagged];
            var missed = estimated[ContradictionStratum.Possible] + estimated[ContradictionStratum.CheckedClear] + estimated[ContradictionStratum.NotChecked];
            miss = caught + missed <= 0 ? null : missed / (caught + missed);
        }
        return new ContradictionRates
        {
            Population = population,
            Labels = labels,
            FalseFlagRate = falseFlag,
            MissRate = miss,
            EstimatedMissed = estimated.Where(kv => kv.Key != ContradictionStratum.Flagged).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
    }

    public static readonly string[] CsvColumns =
        ["session", "first_claim", "second_claim", "first_turn", "second_turn", "first_text", "second_text", "cross_concept", "overlap",
         "detector", "confidence", "human_contradiction"];

    /// <summary>The rows as CSV, with an empty human_contradiction column to fill with yes or no.</summary>
    public static string ToCsv(IEnumerable<ClaimPairRow> rows)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder().AppendLine(string.Join(',', CsvColumns));
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                r.Session, r.FirstClaimId, r.SecondClaimId, r.FirstTurn.ToString(ci), r.SecondTurn.ToString(ci), r.FirstText, r.SecondText,
                r.CrossConcept ? "yes" : "no", r.Overlap.ToString("0.###", ci), StratumName(r.Stratum), r.Confidence.ToString("0.###", ci),
                r.HumanSaysContradiction is { } h ? (h ? "yes" : "no") : "",
            }.Select(Csv)));
        }
        return sb.ToString();
    }

    /// <summary>Reads rows written by <see cref="ToCsv"/> (with the human column filled in); bad lines are reported, not thrown.</summary>
    public static IReadOnlyList<ClaimPairRow> FromCsv(string text, out IReadOnlyList<string> problems)
    {
        var list = new List<ClaimPairRow>();
        var errors = new List<string>();
        var lines = ParseCsv(text);
        if (lines.Count == 0) { problems = ["The file is empty."]; return list; }
        var header = lines[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string name) => header.IndexOf(name);
        var missing = CsvColumns.Where(c => Col(c) < 0).ToList();
        if (missing.Count > 0) { problems = [$"Missing column(s): {string.Join(", ", missing)}."]; return list; }
        var ci = CultureInfo.InvariantCulture;
        for (var i = 1; i < lines.Count; i++)
        {
            var f = lines[i];
            if (f.Count == 1 && string.IsNullOrWhiteSpace(f[0])) continue;
            string Get(string name) => Col(name) < f.Count ? f[Col(name)].Trim() : "";
            if (ParseStratum(Get("detector")) is not { } stratum) { errors.Add($"Line {i + 1}: unknown detector value '{Get("detector")}'."); continue; }
            var human = Get("human_contradiction").ToLowerInvariant() switch
            {
                "yes" or "y" or "true" or "1" => true,
                "no" or "n" or "false" or "0" => (bool?)false,
                "" => null,
                var other => Bad(other),
            };
            bool? Bad(string v) { errors.Add($"Line {i + 1}: human_contradiction must be yes, no or empty (got '{v}')."); return null; }
            list.Add(new ClaimPairRow
            {
                Session = Get("session"), FirstClaimId = Get("first_claim"), SecondClaimId = Get("second_claim"),
                FirstTurn = int.TryParse(Get("first_turn"), NumberStyles.Integer, ci, out var ft) ? ft : 0,
                SecondTurn = int.TryParse(Get("second_turn"), NumberStyles.Integer, ci, out var st) ? st : 0,
                FirstText = Get("first_text"), SecondText = Get("second_text"), CrossConcept = Get("cross_concept") == "yes",
                Overlap = double.TryParse(Get("overlap"), NumberStyles.Float, ci, out var o) ? o : 0,
                Stratum = stratum,
                Confidence = double.TryParse(Get("confidence"), NumberStyles.Float, ci, out var c) ? c : 0,
                HumanSaysContradiction = human,
            });
        }
        problems = errors;
        return list;
    }

    public static string StratumName(ContradictionStratum s) => s switch
    {
        ContradictionStratum.Flagged => "flagged",
        ContradictionStratum.Possible => "possible",
        ContradictionStratum.CheckedClear => "checked-clear",
        _ => "not-checked",
    };

    public static ContradictionStratum? ParseStratum(string s) => s.Trim().ToLowerInvariant() switch
    {
        "flagged" => ContradictionStratum.Flagged,
        "possible" => ContradictionStratum.Possible,
        "checked-clear" => ContradictionStratum.CheckedClear,
        "not-checked" => ContradictionStratum.NotChecked,
        _ => null,
    };

    private static string Key(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";

    private static string Csv(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;

    /// <summary>RFC 4180 CSV: quoted fields may hold commas, quotes ("") and line breaks.</summary>
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
