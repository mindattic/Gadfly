using Gadfly.Engine;

namespace Gadfly.Assessment;

/// <summary>
/// One turn's fast-versus-panel comparison, taken from <see cref="Turn.Provisional"/> (GAD-T508).
/// </summary>
/// <param name="Subject">The exam's subject (e.g. "Biology"); blank groups as "(none)".</param>
/// <param name="Move">The kind of question the examiner asked on the turn.</param>
/// <param name="ProviderId">The fast model's provider (e.g. "anthropic").</param>
/// <param name="Model">The fast model's model id.</param>
/// <param name="ProvisionalScore">The fast model's 0..1 overall score.</param>
/// <param name="FullScore">The full panel's 0..1 overall score; null while it is still pending.</param>
/// <param name="Disagreed">True when the full judgment recorded a material disagreement with the provisional one.</param>
public sealed record ProvisionalObservation(
    string Subject,
    MoveKind Move,
    string ProviderId,
    string Model,
    double ProvisionalScore,
    double? FullScore,
    bool Disagreed)
{
    /// <summary>The observation for one turn, or null when the turn had no provisional judgment.</summary>
    public static ProvisionalObservation? From(Turn turn, string subject)
    {
        if (turn.Provisional is not { } p) return null;
        return new ProvisionalObservation(subject, turn.Move, p.ProviderId, p.Model, p.Judgment.OverallScore, p.FullScore,
            !string.IsNullOrWhiteSpace(p.Disagreement));
    }
}

/// <summary>One group's agreement between the fast model and the panel.</summary>
/// <param name="Key">The group's value (a subject, a move kind, or "provider/model"); "all" for the overall row.</param>
/// <param name="Turns">Turns with a provisional judgment in the group.</param>
/// <param name="Compared">Of those, turns whose full panel score has arrived (the rest are pending and left out of the figures).</param>
/// <param name="Disagreements">Compared turns the panel marked as a material disagreement.</param>
/// <param name="DisagreementRate">Disagreements / Compared; null when nothing was compared.</param>
/// <param name="RateLow">Lower bound of the rate's 95% Wilson interval.</param>
/// <param name="RateHigh">Upper bound of the rate's 95% Wilson interval.</param>
/// <param name="MeanSignedGap">Mean of provisional minus panel score (positive: the fast model scores higher).</param>
/// <param name="MeanAbsoluteGap">Mean of the absolute score difference.</param>
/// <param name="TooFew">True when fewer than <see cref="ProvisionalAgreement.MinTurns"/> turns were compared.</param>
public sealed record ProvisionalAgreementRow(
    string Key,
    int Turns,
    int Compared,
    int Disagreements,
    double? DisagreementRate,
    double? RateLow,
    double? RateHigh,
    double? MeanSignedGap,
    double? MeanAbsoluteGap,
    bool TooFew);

/// <summary>The analysis: the overall row and the rows grouped by subject, by move kind and by provider/model.</summary>
public sealed record ProvisionalAgreementReport(
    ProvisionalAgreementRow Overall,
    IReadOnlyList<ProvisionalAgreementRow> BySubject,
    IReadOnlyList<ProvisionalAgreementRow> ByMove,
    IReadOnlyList<ProvisionalAgreementRow> ByProviderModel);

/// <summary>
/// How well the live turn's fast model (GAD-T508) agrees with the full Legion panel (GAD-T644): for every turn with a
/// provisional judgment whose panel score has arrived, it counts the material disagreements the panel recorded and the score
/// gap, overall and grouped by subject, by the question's move kind and by the fast provider/model, so a reviewer can see
/// where the fast model steers the conversation off. Pure local arithmetic: no model calls. Groups with fewer than
/// <see cref="MinTurns"/> compared turns are flagged as too few to read.
/// </summary>
public static class ProvisionalAgreement
{
    public const int MinTurns = 10;
    private const double Z = 1.96;

    public static ProvisionalAgreementReport Analyze(IEnumerable<ProvisionalObservation> observations)
    {
        var all = observations.ToList();
        return new ProvisionalAgreementReport(
            Row("all", all),
            Group(all, o => string.IsNullOrWhiteSpace(o.Subject) ? "(none)" : o.Subject.Trim()),
            Group(all, o => o.Move.ToString()),
            Group(all, o => ProviderModelKey(o.ProviderId, o.Model)));
    }

    /// <summary>The analysis over a set of turns, skipping turns without a provisional judgment.</summary>
    public static ProvisionalAgreementReport Analyze(IEnumerable<(Turn Turn, string Subject)> turns) =>
        Analyze(turns.Select(t => ProvisionalObservation.From(t.Turn, t.Subject)).OfType<ProvisionalObservation>());

    /// <summary>"provider/model" with blanks shown as "?".</summary>
    public static string ProviderModelKey(string providerId, string model)
    {
        var p = string.IsNullOrWhiteSpace(providerId) ? "?" : providerId.Trim().ToLowerInvariant();
        var m = string.IsNullOrWhiteSpace(model) ? "?" : model.Trim();
        return $"{p}/{m}";
    }

    /// <summary>The 95% Wilson score interval for k successes in n trials; null when n is 0.</summary>
    public static (double Low, double High)? Wilson(int k, int n)
    {
        if (n <= 0) return null;
        var p = (double)k / n;
        var z2 = Z * Z;
        var denom = 1 + z2 / n;
        var centre = (p + z2 / (2 * n)) / denom;
        var half = Z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denom;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    private static IReadOnlyList<ProvisionalAgreementRow> Group(List<ProvisionalObservation> all, Func<ProvisionalObservation, string> key) =>
        all.GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => Row(g.Key, g.ToList()))
            .ToList();

    private static ProvisionalAgreementRow Row(string key, List<ProvisionalObservation> group)
    {
        var compared = group.Where(o => o.FullScore is not null).ToList();
        var n = compared.Count;
        var k = compared.Count(o => o.Disagreed);
        var gaps = compared.Select(o => o.ProvisionalScore - o.FullScore!.Value).ToList();
        var ci = Wilson(k, n);
        return new ProvisionalAgreementRow(
            key,
            group.Count,
            n,
            k,
            n == 0 ? null : Math.Round((double)k / n, 4),
            ci is { } c ? Math.Round(c.Low, 4) : null,
            ci is { } d ? Math.Round(d.High, 4) : null,
            n == 0 ? null : Math.Round(gaps.Average(), 4),
            n == 0 ? null : Math.Round(gaps.Average(Math.Abs), 4),
            n < MinTurns);
    }
}
