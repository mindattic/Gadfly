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
    /// <summary>"not-examined" or <see cref="ConceptStatusNames.ReportName"/>; reports saved earlier may say "mastered", "aporia" or "exhausted".</summary>
    public required string Status { get; init; }
    public int Turns { get; init; }
    public double? AverageScore { get; init; }
    /// <summary>The student's own words that best show (or fail to show) understanding.</summary>
    public IReadOnlyList<EvidenceQuote> Evidence { get; init; } = [];
    /// <summary>Turns where the judges disagreed enough to need a human look.</summary>
    public IReadOnlyList<int> DisputedTurns { get; init; } = [];
}

/// <summary>
/// Per-objective result (GAD-T89): the level the evidence supports, or Level 0 (Insufficient Evidence for
/// Reliable Assessment) when it is not sufficient, with what was shown and what remains uncertain.
/// </summary>
public sealed record ObjectiveReport
{
    public required string ObjectiveKey { get; init; }
    public string Title { get; init; } = "";
    /// <summary>"sufficient", "inconclusive", "open" or "not-started".</summary>
    public required string Status { get; init; }
    /// <summary>0-5; 0 is Insufficient Evidence, never zero understanding.</summary>
    public required int Level { get; init; }
    public required string LevelLabel { get; init; }
    /// <summary>What the independent evidence points to, for the reviewer, even when the level is 0.</summary>
    public int ProvisionalLevel { get; init; }
    /// <summary>Learning Mode: the level including answers after assistance (reported separately).</summary>
    public int? AssistedLevel { get; init; }
    public int Questions { get; init; }
    /// <summary>Follow-up questions after the opening one (GAD-T267), so a reviewer sees how hard each objective was probed.</summary>
    public int FollowUps { get; init; }
    /// <summary>True when this score is formative only: see <see cref="SessionReport.ScoreUse"/> (GAD-T267).</summary>
    public bool Formative { get; init; }
    public int IndependentAnswers { get; init; }
    public int SupportingAnswers { get; init; }
    public int ContradictingAnswers { get; init; }
    public int ContaminatedAnswers { get; init; }
    public int AssistedAnswers { get; init; }
    /// <summary>
    /// Learning Mode: rungs of the objective's fixed hint ladder given (GAD-T271), reported beside the unaided
    /// <see cref="Level"/>, never folded into it. No gain or "learning potential" score is derived from it.
    /// </summary>
    public int HintsGiven { get; init; }
    /// <summary>
    /// No unresolved contradiction was detected (GAD-T440). Kept for older readers; it is not proof of consistency, so a report
    /// states <see cref="Consistency"/> instead ("no contradiction detected" when the detector found none).
    /// </summary>
    public bool Consistent { get; init; } = true;
    /// <summary>What may be said about consistency: "no contradiction detected", "contradiction resolved", and so on (GAD-T440).
    /// Null in reports made before GAD-T440: <see cref="ConsistencyText"/> reads those from <see cref="Consistent"/>.</summary>
    public string? Consistency { get; init; }
    public int ContradictionsDetected { get; init; }

    /// <summary>The consistency wording to show, for this report or an older one.</summary>
    [JsonIgnore]
    public string ConsistencyText => Consistency ?? (Consistent ? ObjectiveAssessor.NoContradictionDetected : "contradiction not resolved");
    public IReadOnlyList<string> UnresolvedMisconceptions { get; init; } = [];
    public IReadOnlyList<string> Uncertainties { get; init; } = [];
    public IReadOnlyList<int> LeakedTurns { get; init; } = [];
    public string Explanation { get; init; } = "";

    public static ObjectiveReport From(ObjectiveAssessment a) => new()
    {
        ObjectiveKey = a.ObjectiveKey,
        Title = a.Title,
        Status = a.Status switch
        {
            ObjectiveStatus.Sufficient => "sufficient",
            ObjectiveStatus.Inconclusive => "inconclusive",
            ObjectiveStatus.Open => "open",
            _ => "not-started",
        },
        Level = (int)a.Level,
        LevelLabel = Gadfly.Assessment.MasteryLevels.Label(a.Level),
        ProvisionalLevel = (int)a.ProvisionalLevel,
        AssistedLevel = a.AssistedLevel is { } al ? (int)al : null,
        Questions = a.Questions,
        FollowUps = a.FollowUps,
        IndependentAnswers = a.IndependentAnswers,
        SupportingAnswers = a.SupportingAnswers,
        ContradictingAnswers = a.ContradictingAnswers,
        ContaminatedAnswers = a.ContaminatedAnswers,
        AssistedAnswers = a.AssistedAnswers,
        HintsGiven = a.AssistsGiven,
        Consistent = a.Consistent,
        Consistency = a.Consistency,
        ContradictionsDetected = a.ContradictionsDetected,
        UnresolvedMisconceptions = a.UnresolvedMisconceptions,
        Uncertainties = a.Uncertainties,
        LeakedTurns = a.LeakedTurns,
        Explanation = a.Explanation,
    };
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
    /// <summary>
    /// "mastered", "partial" or "not-demonstrated", from the running estimate. A steering aid kept for older readers and the
    /// API; never shown as the student's result (GAD-T425): <see cref="Objectives"/> levels are.
    /// </summary>
    public required string Recommendation { get; init; }
    /// <summary>Mean running estimate over examined concepts, 0..1. A steering aid, never a score (GAD-T425).</summary>
    public required double OverallScore { get; init; }
    public required IReadOnlyList<ConceptReport> Concepts { get; init; }
    public int TurnsJudged { get; init; }
    public int TurnsNeedingReview { get; init; }
    /// <summary>Per-objective levels and evidence sufficiency (GAD-T89); empty in reports made before objectives.</summary>
    public IReadOnlyList<ObjectiveReport> Objectives { get; init; } = [];
    /// <summary>"Examination" or "Learning": learning-session evidence is never examination evidence (Part 3).</summary>
    public string Mode { get; init; } = nameof(Gadfly.Assessment.AssessmentMode.Examination);
    /// <summary>
    /// "summative" or "formative" (GAD-T267): a Learning session, or an examination that examined fewer objectives than
    /// the policy's minimum, gives formative per-objective scores. Reports made before GAD-T267 read as summative.
    /// </summary>
    public string ScoreUse { get; init; } = ExamObjectives.Summative;
    /// <summary>Why <see cref="ScoreUse"/> is what it is, in plain words.</summary>
    public string ScoreUseReason { get; init; } = "";
    /// <summary>Objectives that received at least one question.</summary>
    public int ObjectivesExamined { get; init; }
    /// <summary>The policy's <see cref="SessionPolicy.MinSummativeObjectives"/> when the report was made.</summary>
    public int MinSummativeObjectives { get; init; }

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
        int evidencePerConcept = 3, IReadOnlyList<ObjectiveAssessment>? objectives = null)
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
            // A recited answer (GAD-T242) is left out of the average, as it is of mastery and evidence.
            var graded = judged.Where(t => !ObjectiveAssessor.IsUnjudged(t.Judgment!) && !ObjectiveAssessor.IsRecited(t)).ToList();
            var mastery = session.Mastery.TryGetValue(id, out var m) ? m.Estimate : (judged.Count == 0 ? 0 : MasteryModel.Prior);
            var status = turns.Count == 0 ? "not-examined" : ConceptStatusNames.ReportName(QuestionPlanner.StatusOf(session, id));
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
                // A turn no judge could grade is no evidence, not a zero answer (GAD-T194); an honest "I don't know" is still 0.
                AverageScore = graded.Count == 0 ? null : Math.Round(graded.Average(t => t.Judgment!.OverallScore), 3),
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
        var objectiveReports = (objectives ?? ObjectiveAssessor.AssessAll(session, ExamObjectives.Resolve(p, conceptIds,
                id => conceptTitles is not null && conceptTitles.TryGetValue(id, out var t) ? t : id)))
            .Select(ObjectiveReport.From).ToList();
        var objectivesExamined = objectiveReports.Count > 0
            ? objectiveReports.Count(o => o.Questions > 0)
            : session.Turns.Select(ExamObjectives.KeyOf).Distinct(StringComparer.Ordinal).Count();
        var (scoreUse, scoreUseReason) = ExamObjectives.ScoreUse(p, objectivesExamined);
        var formative = scoreUse == ExamObjectives.Formative;
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
            Objectives = objectiveReports.Select(o => o with { Formative = formative }).ToList(),
            Mode = p.Mode.ToString(),
            ScoreUse = scoreUse,
            ScoreUseReason = scoreUseReason,
            ObjectivesExamined = objectivesExamined,
            MinSummativeObjectives = p.MinSummativeObjectives,
        };
    }

    /// <summary>A readable report for the instructor.</summary>
    public string ToMarkdown()
    {
        var ci = CultureInfo.InvariantCulture;
        string Pct(double v) => (v * 100).ToString("0", ci) + "%";
        var sb = new StringBuilder();
        sb.AppendLine("# Socratic session report").AppendLine();
        // GAD-T425 (ElenchusAtScale Prop. 3.3, Prop. 3.4(b), M9, M10, Section 4.5): what is reported is the corroborated level
        // of each learning objective, Level 0 meaning insufficient evidence. The running estimate (Recommendation,
        // OverallScore, each concept's Mastery) only steers the questions and is never stated as the result.
        sb.AppendLine(Objectives.Count > 0
            ? "**Result: the level for each learning objective, below.** Level 0 means the evidence was insufficient to assess " +
              "reliably, not zero understanding. This is the AI panel's report; the instructor decides the grade."
            : "**No per-objective levels:** this report was written before learning objectives, and the instructor decides the grade " +
              "from the evidence below.").AppendLine();
        sb.AppendLine("| | |").AppendLine("|---|---|");
        sb.AppendLine($"| Session | `{SessionId}` |");
        sb.AppendLine($"| Subject | {SubjectId} |");
        sb.AppendLine($"| Started | {StartedAt:u} |");
        if (EndedAt is { } end) sb.AppendLine($"| Ended | {end:u} ({EndReason}) |");
        sb.AppendLine($"| Answers judged | {TurnsJudged} |");
        sb.AppendLine($"| Answers needing human review | {TurnsNeedingReview} |");
        sb.AppendLine($"| Prompt set | `{PromptSetSha256[..Math.Min(12, PromptSetSha256.Length)]}` |").AppendLine();

        if (Objectives.Count > 0)
        {
            sb.AppendLine($"## Learning objectives ({Mode} mode)").AppendLine();
            if (ScoreUse == ExamObjectives.Formative)
                sb.AppendLine($"> **Formative scores.** {ScoreUseReason}").AppendLine();
            sb.AppendLine("| Objective | Level | Status | Questions | Follow-ups | Independent answers | Notes |").AppendLine("|---|---|---|---|---|---|---|");
            foreach (var o in Objectives)
            {
                var notes = new List<string>();
                if (o.Level == 0 && o.ProvisionalLevel > 0) notes.Add($"evidence points to level {o.ProvisionalLevel}, not yet sufficient");
                if (o.HintsGiven > 0 || o.AssistedLevel is not null)
                    notes.Add($"unaided level {o.Level}; after {o.HintsGiven} hint{(o.HintsGiven == 1 ? "" : "s")} from the fixed ladder: " +
                        (o.AssistedLevel is { } al ? $"assisted level {al}" : "no assisted level yet"));
                if (o.ContaminatedAnswers > 0) notes.Add($"{o.ContaminatedAnswers} contaminated answer(s) excluded");
                notes.Add(o.ConsistencyText);
                if (o.UnresolvedMisconceptions.Count > 0) notes.Add("misconception: " + string.Join("; ", o.UnresolvedMisconceptions));
                sb.AppendLine($"| {Escape(o.Title)} | {o.Level} {o.LevelLabel} | {o.Status} | {o.Questions} | {o.FollowUps} | {o.IndependentAnswers} | {Escape(string.Join("; ", notes))} |");
            }
            sb.AppendLine();
            sb.AppendLine("> \"No contradiction detected\" means the contradiction check found none among the pairs of claims it compared. " +
                          "It is not proof that the answers are consistent.").AppendLine();
            foreach (var o in Objectives.Where(o => o.Status == "inconclusive"))
                sb.AppendLine($"> **{Escape(o.Title)}: Level 0, Insufficient Evidence.** {o.Explanation} Follow the institution's procedure (human review or further examination).").AppendLine();
        }

        sb.AppendLine("## Concepts").AppendLine();
        sb.AppendLine("> The running estimate is the examiner's steering aid for choosing the next question. It is not a score, a level " +
                      "or a grade, and no average of answer scores stands in for a level.").AppendLine();
        sb.AppendLine("| Concept | Running estimate (steering only) | Status | Turns |").AppendLine("|---|---|---|---|");
        foreach (var c in Concepts)
            sb.AppendLine($"| {Escape(c.Title)} | {Pct(c.Mastery)} | {c.Status} | {c.Turns} |");
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
