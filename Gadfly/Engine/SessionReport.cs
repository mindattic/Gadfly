using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gadfly.Engine;

/// <summary>Per-concept result inside a <see cref="SessionReport"/>.</summary>
public sealed record ConceptReport
{
    public required string ConceptId { get; init; }
    public string Title { get; init; } = "";
    public required double Mastery { get; init; }
    public required string Status { get; init; }
    public int Turns { get; init; }
    public double? AverageScore { get; init; }
    /// <summary>The student's own words that best show (or fail to show) understanding.</summary>
    public IReadOnlyList<EvidenceQuote> Evidence { get; init; } = [];
    /// <summary>Turns where the judges disagreed enough to need a human look.</summary>
    public IReadOnlyList<int> DisputedTurns { get; init; } = [];
}

/// <summary>A quoted answer with the judgment that scored it and its source citations.</summary>
public sealed record EvidenceQuote(int TurnIndex, MoveKind Move, string Question, string Quote, double Score,
    string JudgeSummary, IReadOnlyList<Citation> Citations);

/// <summary>
/// The AI's recommendation for one session: mastery per concept, quoted evidence with source
/// citations, and the judges' dissent. A recommendation only; an instructor finalizes the grade
/// (decision D13). Rendered as JSON for machines and Markdown for people.
/// </summary>
public sealed record SessionReport
{
    public required Guid SessionId { get; init; }
    public required string SubjectId { get; init; }
    public required string PromptSetSha256 { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public required EndReason EndReason { get; init; }
    /// <summary>"mastered", "partial" or "not-demonstrated".</summary>
    public required string Recommendation { get; init; }
    /// <summary>Mean mastery over examined concepts, 0..1.</summary>
    public required double OverallScore { get; init; }
    public required IReadOnlyList<ConceptReport> Concepts { get; init; }
    public int TurnsJudged { get; init; }
    public int TurnsNeedingReview { get; init; }

    public const string Mastered = "mastered";
    public const string Partial = "partial";
    public const string NotDemonstrated = "not-demonstrated";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static SessionReport? FromJson(string json) => JsonSerializer.Deserialize<SessionReport>(json, Json);

    /// <summary>The same report with every quote of the student's words removed (retention redaction).</summary>
    public SessionReport WithoutStudentWords() => this with
    {
        Concepts = Concepts.Select(c => c with
        {
            Evidence = c.Evidence.Select(e => e with { Quote = "[redacted after the retention period]", JudgeSummary = "" }).ToList(),
        }).ToList(),
    };

    /// <summary>
    /// Builds the report from a session. <paramref name="conceptTitles"/> maps concept ids to
    /// display titles (missing ids show the id). <paramref name="evidencePerConcept"/> caps quotes.
    /// </summary>
    public static SessionReport From(SocraticSession session, IReadOnlyDictionary<string, string>? conceptTitles = null,
        int evidencePerConcept = 3)
    {
        ArgumentNullException.ThrowIfNull(session);
        var p = session.Policy;
        var conceptIds = session.Turns.Select(t => t.ConceptId).Distinct(StringComparer.Ordinal).ToList();
        foreach (var id in p.ConceptIds.Where(id => !conceptIds.Contains(id, StringComparer.Ordinal)))
            conceptIds.Add(id);

        var concepts = conceptIds.Select(id =>
        {
            var turns = session.Turns.Where(t => t.ConceptId == id).ToList();
            var judged = turns.Where(t => t.Judgment is not null && t.Answer is not null).ToList();
            var mastery = session.Mastery.TryGetValue(id, out var m) ? m.Estimate : (judged.Count == 0 ? 0 : MasteryModel.Prior);
            var status = turns.Count == 0 ? "not-examined" : QuestionPlanner.StatusOf(session, id).ToString().ToLowerInvariant();
            // Strongest and weakest answers both make good evidence: show the extremes first.
            var evidence = judged
                .OrderByDescending(t => Math.Abs(t.Judgment!.OverallScore - 0.5))
                .ThenBy(t => t.Index)
                .Take(Math.Max(0, evidencePerConcept))
                .OrderBy(t => t.Index)
                .Select(t => new EvidenceQuote(t.Index, t.Move, t.Question, t.Answer!.Text, t.Judgment!.OverallScore,
                    t.Judgment.Summary, t.Judgment.Criteria.SelectMany(c => c.Citations).DistinctBy(c => c.ChunkId).ToList()))
                .ToList();
            return new ConceptReport
            {
                ConceptId = id,
                Title = conceptTitles is not null && conceptTitles.TryGetValue(id, out var title) ? title : id,
                Mastery = Math.Round(mastery, 3),
                Status = status,
                Turns = turns.Count,
                AverageScore = judged.Count == 0 ? null : Math.Round(judged.Average(t => t.Judgment!.OverallScore), 3),
                Evidence = evidence,
                DisputedTurns = judged.Where(t => t.Judgment!.NeedsHumanReview).Select(t => t.Index).ToList(),
            };
        }).ToList();

        var examined = concepts.Where(c => c.Turns > 0).ToList();
        var overall = examined.Count == 0 ? 0 : examined.Average(c => c.Mastery);
        var recommendation =
            examined.Count > 0 && concepts.All(c => c.Mastery >= p.MasteryThreshold) ? Mastered
            : overall >= (p.MasteryThreshold + p.AporiaThreshold) / 2 ? Partial
            : NotDemonstrated;

        var judgedTurns = session.Turns.Where(t => t.Judgment is not null).ToList();
        return new SessionReport
        {
            SessionId = session.Id,
            SubjectId = session.SubjectId,
            PromptSetSha256 = session.PromptSetSha256,
            StartedAt = session.StartedAt,
            EndedAt = session.EndedAt,
            EndReason = session.EndReason,
            Recommendation = recommendation,
            OverallScore = Math.Round(overall, 3),
            Concepts = concepts,
            TurnsJudged = judgedTurns.Count,
            TurnsNeedingReview = judgedTurns.Count(t => t.Judgment!.NeedsHumanReview),
        };
    }

    /// <summary>A readable report for the instructor.</summary>
    public string ToMarkdown()
    {
        var ci = CultureInfo.InvariantCulture;
        string Pct(double v) => (v * 100).ToString("0", ci) + "%";
        var sb = new StringBuilder();
        sb.AppendLine("# Socratic session report").AppendLine();
        sb.AppendLine($"**Recommendation: {Recommendation}** (overall mastery {Pct(OverallScore)}). " +
                      "This is the AI panel's recommendation; the instructor decides the grade.").AppendLine();
        sb.AppendLine("| | |").AppendLine("|---|---|");
        sb.AppendLine($"| Session | `{SessionId}` |");
        sb.AppendLine($"| Subject | {SubjectId} |");
        sb.AppendLine($"| Started | {StartedAt:u} |");
        if (EndedAt is { } end) sb.AppendLine($"| Ended | {end:u} ({EndReason}) |");
        sb.AppendLine($"| Answers judged | {TurnsJudged} |");
        sb.AppendLine($"| Answers needing human review | {TurnsNeedingReview} |");
        sb.AppendLine($"| Prompt set | `{PromptSetSha256[..Math.Min(12, PromptSetSha256.Length)]}` |").AppendLine();

        sb.AppendLine("## Concepts").AppendLine();
        sb.AppendLine("| Concept | Mastery | Status | Turns | Avg. score |").AppendLine("|---|---|---|---|---|");
        foreach (var c in Concepts)
            sb.AppendLine($"| {Escape(c.Title)} | {Pct(c.Mastery)} | {c.Status} | {c.Turns} | {(c.AverageScore is { } a ? Pct(a) : "-")} |");
        sb.AppendLine();

        foreach (var c in Concepts.Where(c => c.Evidence.Count > 0))
        {
            sb.AppendLine($"### {c.Title}").AppendLine();
            if (c.DisputedTurns.Count > 0)
                sb.AppendLine($"> Judges disagreed on turn(s) {string.Join(", ", c.DisputedTurns.Select(i => i + 1))}: review those answers yourself.").AppendLine();
            foreach (var e in c.Evidence)
            {
                sb.AppendLine($"**Turn {e.TurnIndex + 1} ({e.Move}), score {Pct(e.Score)}**").AppendLine();
                sb.AppendLine($"- Q: {e.Question}");
                sb.AppendLine($"- A: \"{e.Quote}\"");
                if (!string.IsNullOrWhiteSpace(e.JudgeSummary)) sb.AppendLine($"- Judges: {e.JudgeSummary}");
                if (e.Citations.Count > 0) sb.AppendLine($"- Sources: {string.Join(", ", e.Citations.Select(x => x.ChunkId))}");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("|", "\\|");
}
