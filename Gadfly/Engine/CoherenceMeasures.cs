using System.Globalization;
using System.Text;

namespace Gadfly.Engine;

/// <summary>
/// One session's coherence across turns, from its claim ledger and transcript (GAD-T449), for the relay arm of the pilot study.
/// Every count is of what the session recorded; nothing is inferred about the student.
/// </summary>
public sealed record SessionCoherence
{
    public required string Session { get; init; }
    /// <summary>The study condition the session was run under (for example "honest" or "relay"); empty when untagged.</summary>
    public string Condition { get; init; } = "";
    /// <summary>Judged answers that are evidence for a level (not a confrontation, a closing turn or an answer without a verdict).</summary>
    public int Answers { get; init; }
    /// <summary>Mean overall score of those answers, 0..1: the "equal quality of the individual answers" H1 conditions on.</summary>
    public double? MeanAnswerScore { get; init; }
    /// <summary>Claims the student asserted (including any they later withdrew).</summary>
    public int Claims { get; init; }
    public int WithdrawnClaims { get; init; }
    /// <summary>Contradictions a majority of the panel found (the kind put to the student).</summary>
    public int Contradictions { get; init; }
    /// <summary>Contradictions a split panel found: kept for the reviewer, never put to the student.</summary>
    public int PossibleContradictions { get; init; }
    /// <summary>Contradictions put to the student and judged.</summary>
    public int Confronted { get; init; }
    /// <summary>Of those, resolved coherently (the judge's consistency score at 0.5 or more).</summary>
    public int Resolved { get; init; }
    /// <summary>Of those, not resolved (below 0.5): a "failed resolution" in H1's terms.</summary>
    public int FailedResolutions { get; init; }
    /// <summary>Contradictions the student settled by withdrawing a claim before they were put to them.</summary>
    public int SettledByWithdrawal { get; init; }
    /// <summary>Contradictions never put to the student, or put but not answered or judged.</summary>
    public int NotConfronted { get; init; }

    /// <summary>Contradictions per ten claims: the rate H1 predicts is higher under relay. Null with no claims.</summary>
    public double? ContradictionsPerTenClaims => Claims == 0 ? null : Contradictions * 10.0 / Claims;
    /// <summary>Share of judged confrontations resolved; null when none were judged.</summary>
    public double? ResolutionRate => Confronted == 0 ? null : (double)Resolved / Confronted;
}

/// <summary>One study condition's sessions summarised: counts and means, nothing more.</summary>
public sealed record CoherenceSummary(string Condition, int Sessions, int SessionsWithClaims, double? MeanAnswerScore,
    double? MeanContradictionsPerTenClaims, int Confronted, int FailedResolutions, double? ResolutionRate);

/// <summary>
/// Ledger-based coherence measures for testing hypothesis H1 (GAD-T449; ElenchusAtScale Sections 3.3, 5.5, 6.8): that answers
/// relayed live from another source are less coherent across turns (more contradictions in the ledger, more failed resolutions)
/// than answers from students who understand the material, at equal quality of the individual answers.
/// <para>
/// This is a research instrument, not an integrity signal. A webcam and a microphone cannot detect a hidden earpiece, so a relayed
/// answer spoken in the student's own voice is countered only by follow-up questions built from the student's own earlier answers,
/// and whether that works is exactly what H1 asks. These measures let a pilot compare sessions run honestly with sessions in which
/// participants relay answers, by condition. They are never computed for a live student, never shown to a reviewer as a note about
/// a student, and never enter a grade: a contradiction shows that two claims cannot both stand, not that anyone was dishonest.
/// </para>
/// Pure: no model calls, deterministic.
/// </summary>
public static class CoherenceMeasures
{
    /// <summary>The measures for one session from its transcript and claim ledger.</summary>
    public static SessionCoherence Measure(SocraticSession session, ClaimLedger ledger, string name, string condition = "")
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ledger);
        var answers = session.Turns
            .Where(t => t.Answer is not null && t.Judgment is { } j && !ObjectiveAssessor.IsUnjudged(j)
                        && t.Move is not (MoveKind.Contradiction or MoveKind.Close))
            .Select(t => t.Judgment!.OverallScore).ToList();

        var confronting = session.Turns.Where(t => t.Move == MoveKind.Contradiction).ToList();
        int firm = 0, possible = 0, confronted = 0, resolved = 0, failed = 0, withdrawn = 0, notConfronted = 0;
        foreach (var c in ledger.Contradictions)
        {
            if (!ClaimLedger.IsFirm(c)) { possible++; continue; }
            firm++;
            var asked = confronting.Where(t => t.TargetClaimIds.Contains(c.FirstClaimId) && t.TargetClaimIds.Contains(c.SecondClaimId)).ToList();
            var judged = asked.FirstOrDefault(t => t.Answer is not null && t.Judgment is { } tj && !ObjectiveAssessor.IsUnjudged(tj));
            var settled = ledger.IsWithdrawn(c.FirstClaimId) || ledger.IsWithdrawn(c.SecondClaimId);
            if (asked.Count == 0 && settled) withdrawn++;
            else if (judged is null) notConfronted++;
            else
            {
                confronted++;
                if (ObjectiveAssessor.ResolutionScore(judged.Judgment!) < 0.5) failed++;
                else resolved++;
            }
        }

        return new SessionCoherence
        {
            Session = name,
            Condition = condition,
            Answers = answers.Count,
            MeanAnswerScore = answers.Count == 0 ? null : answers.Average(),
            Claims = ledger.Claims.Count,
            WithdrawnClaims = ledger.Claims.Count(c => ledger.IsWithdrawn(c.Id)),
            Contradictions = firm,
            PossibleContradictions = possible,
            Confronted = confronted,
            Resolved = resolved,
            FailedResolutions = failed,
            SettledByWithdrawal = withdrawn,
            NotConfronted = notConfronted,
        };
    }

    /// <summary>Per-condition counts and means, conditions in name order. Sessions with no claims add nothing to the claim rate.</summary>
    public static IReadOnlyList<CoherenceSummary> Summarise(IReadOnlyList<SessionCoherence> sessions) =>
        sessions.GroupBy(s => s.Condition, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var scored = g.Where(s => s.MeanAnswerScore is not null).Select(s => s.MeanAnswerScore!.Value).ToList();
            var rates = g.Where(s => s.ContradictionsPerTenClaims is not null).Select(s => s.ContradictionsPerTenClaims!.Value).ToList();
            var confronted = g.Sum(s => s.Confronted);
            var failed = g.Sum(s => s.FailedResolutions);
            return new CoherenceSummary(g.Key, g.Count(), rates.Count, scored.Count == 0 ? null : scored.Average(),
                rates.Count == 0 ? null : rates.Average(), confronted, failed, confronted == 0 ? null : (double)(confronted - failed) / confronted);
        }).ToList();

    /// <summary>One row per session, for the study's own analysis.</summary>
    public static string ToCsv(IEnumerable<SessionCoherence> sessions)
    {
        var sb = new StringBuilder();
        sb.Append("session,condition,answers,mean_answer_score,claims,withdrawn_claims,contradictions,possible_contradictions,")
          .Append("confronted,resolved,failed_resolutions,settled_by_withdrawal,not_confronted,contradictions_per_10_claims,resolution_rate\r\n");
        foreach (var s in sessions)
        {
            sb.AppendJoin(',', Csv(s.Session), Csv(s.Condition), I(s.Answers), D(s.MeanAnswerScore), I(s.Claims), I(s.WithdrawnClaims),
                I(s.Contradictions), I(s.PossibleContradictions), I(s.Confronted), I(s.Resolved), I(s.FailedResolutions),
                I(s.SettledByWithdrawal), I(s.NotConfronted), D(s.ContradictionsPerTenClaims), D(s.ResolutionRate)).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string D(double? v) => v is { } x ? x.ToString("0.####", CultureInfo.InvariantCulture) : "";

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
