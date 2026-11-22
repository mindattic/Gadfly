using Gadfly.Server.Tenancy;
using System.Globalization;
using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Analytics;

// Educational analytics (docs/MASTER-PLAN.md Part 12.3, GAD-T102). Every figure is computed here in plain
// application code from stored rows: no LLM is involved, nothing is estimated, and aggregates name no student.
// Mock-mode AI grades (AiObjectiveGrade.IsMock) are never counted as genuine AI evaluations.

/// <summary>What the analytics cover: one assessment, one course's assessments, or all of the organization's.</summary>
public sealed record AnalyticsScope(Guid? ExamId = null, Guid? CourseId = null)
{
    public static readonly AnalyticsScope All = new();
}

/// <summary>A choice for the scope picker.</summary>
public sealed record ScopeOption(string Kind, Guid Id, string Title);

/// <summary>Where an objective-level comes from: the latest human examiner score, or the latest (non-mock) AI preliminary score.</summary>
public enum LevelSource { Human, Ai }

public sealed record CompletionRow(Guid ExamId, string ExamTitle, int Assigned, int Started, int Completed, int InProgress, int Failed)
{
    public double? Rate => Assigned == 0 ? null : (double)Completed / Assigned;
}

/// <summary>One learning objective's level distribution (index = level 0..5) from one source.</summary>
public sealed record ObjectiveMasteryRow(Guid ObjectiveId, string Objective, string ExamTitle, LevelSource Source, int[] Counts)
{
    public int Total => Counts.Sum();
    /// <summary>Grades at levels 1..5. Level 0 is "insufficient evidence", never "no understanding".</summary>
    public int Conclusive => Total - Counts[0];
    public double? MeanConclusiveLevel => Conclusive == 0 ? null : Enumerable.Range(1, 5).Sum(l => l * Counts[l]) / (double)Conclusive;
    /// <summary>Share of conclusive grades at Recognition or Basic Explanation (levels 1-2).</summary>
    public double? LowShare => Conclusive == 0 ? null : (Counts[1] + Counts[2]) / (double)Conclusive;
    public double? InsufficientShare => Total == 0 ? null : Counts[0] / (double)Total;
}

/// <summary>The Socratic engine's running mastery estimate for a concept (0..1), from existing sessions.</summary>
public sealed record ConceptMasteryRow(string ConceptKey, string Concept, string ExamTitle, int Sessions, double MeanEstimate,
    int Mastered, int Open, int Aporia, int Exhausted, int NotStarted);

public sealed record MisconceptionRow(string Text, string Objective, int Sessions, int Mentions, string Sources);

public sealed record GapRow(string Area, string ExamTitle, string Basis, string Measure, int Evidence);

public sealed record AgreementObjectiveRow(string Objective, int Pairs, double ExactLevel, double WithinOneLevel, double MeanSignedPoints, double MeanAbsPoints);

public sealed record AgreementSummary(int Pairs, double? ExactLevel, double? WithinOneLevel, double? MeanSignedPoints, double? MeanAbsPoints,
    IReadOnlyList<AgreementObjectiveRow> ByObjective, int Disagreements, int AdditionalReview, int MandatoryReconciliation,
    int Resolved, int Reconciliations);

public sealed record CorrectionSummary(int Responses, int ResponsesWithCorrections, IReadOnlyDictionary<string, int> VersionsByKind,
    IReadOnlyDictionary<string, int> ClarificationsByType, int ClarificationsAnswered, IReadOnlyDictionary<string, int> VerificationStates);

public sealed record ReliabilitySummary(int AiGrades, int AiInconclusive, int HumanGrades, int HumanInconclusive, int MockAiGradesExcluded,
    int Pairs, int Reconciliations, int MandatoryReconciliation, int JudgedAnswers, double? MeanJudgeAgreement, int JudgesDisagreed,
    int[] JudgeAgreementBuckets, int SessionsEnded, IReadOnlyDictionary<string, int> EndReasons)
{
    public double? AiInconclusiveRate => AiGrades == 0 ? null : AiInconclusive / (double)AiGrades;
    public double? HumanInconclusiveRate => HumanGrades == 0 ? null : HumanInconclusive / (double)HumanGrades;
    public double? ReconciliationRate => Pairs == 0 ? null : MandatoryReconciliation / (double)Pairs;
    public double? JudgeDisagreementRate => JudgedAnswers == 0 ? null : JudgesDisagreed / (double)JudgedAnswers;
}

public sealed record AnalyticsReport(
    string ScopeTitle,
    int Assessments,
    int Sessions,
    IReadOnlyList<CompletionRow> Completion,
    IReadOnlyList<ObjectiveMasteryRow> ObjectiveMastery,
    IReadOnlyList<ConceptMasteryRow> ConceptMastery,
    IReadOnlyList<MisconceptionRow> Misconceptions,
    IReadOnlyList<GapRow> Gaps,
    AgreementSummary Agreement,
    CorrectionSummary Corrections,
    ReliabilitySummary Reliability,
    DateTimeOffset GeneratedAt)
{
    public int Assigned => Completion.Sum(c => c.Assigned);
    public int Completed => Completion.Sum(c => c.Completed);
    public double? CompletionRate => Assigned == 0 ? null : (double)Completed / Assigned;
}

/// <summary>
/// Computes the Part 12.3 analytics for one organization. Every query names the organization explicitly on
/// top of GadflyDbContext's tenant filter. Rows are loaded per scope and aggregated in memory.
/// </summary>
/// <remarks>
/// <paramref name="viewerRole"/> is the caller's role (null: no redaction). A viewer who can enter human scores is a potential
/// blind examiner (<see cref="Grading.BlindGrading"/>, decision D25): the AI preliminary grades, the exam-time judge's notes and
/// verdicts and the engine's mastery estimates of sessions whose grading is still blind are left out of what they see, since an
/// assessment with one session waiting would otherwise show that session's AI levels (GAD-T315).
/// </remarks>
public sealed class AnalyticsService(GadflyDbContext db, Guid organizationId, TimeProvider? clock = null, string? viewerRole = null)
{
    /// <summary>Plan text (Part 12.3, 15.3) shown beside every agreement figure.</summary>
    public const string AgreementCaveat =
        "Agreement between the AI and human examiners is not evidence that either score is valid. Two raters can agree and both be wrong; " +
        "validity needs the structured studies in Part 15.3 (comparison with qualified examiners, repeated-scoring consistency, " +
        "subgroup analysis, false mastery and false deficiency review).";

    /// <summary>A conclusive objective is a class-wide gap when at least this share of its grades sit at levels 1-2.</summary>
    public const double GapLowShare = 0.5;
    /// <summary>An engine concept is a gap when the mean mastery estimate across sessions is below this.</summary>
    public const double GapConceptEstimate = 0.5;
    /// <summary>An objective needs more evidence (not a knowledge gap) when this share of grades is level 0.</summary>
    public const double EvidenceGapShare = 0.3;

    public async Task<IReadOnlyList<ScopeOption>> ScopeOptionsAsync(CancellationToken ct = default)
    {
        var courses = await db.Courses.AsNoTracking().Where(c => c.OrganizationId == organizationId && !c.IsArchived)
            .OrderBy(c => c.Title).Select(c => new { c.Id, c.Title }).ToListAsync(ct);
        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == organizationId && e.Status != ExamStatus.Draft)
            .OrderBy(e => e.Title).Select(e => new { e.Id, e.Title }).ToListAsync(ct);
        return [.. courses.Select(c => new ScopeOption("course", c.Id, c.Title)), .. exams.Select(e => new ScopeOption("exam", e.Id, e.Title))];
    }

    public async Task<AnalyticsReport> BuildAsync(AnalyticsScope scope, CancellationToken ct = default)
    {
        var examQuery = db.Exams.AsNoTracking().Where(e => e.OrganizationId == organizationId && e.Status != ExamStatus.Draft);
        string scopeTitle = "All assessments";
        if (scope.ExamId is { } examId)
        {
            examQuery = examQuery.Where(e => e.Id == examId);
            var found = await examQuery.Select(e => e.Title).FirstOrDefaultAsync(ct);
            if (found is null) await db.RefuseForeignAsync(OwnershipScope.Kind.Exam, examId, "Opening analytics for an assessment", ct);
            scopeTitle = found ?? "Unknown assessment";
        }
        else if (scope.CourseId is { } courseId)
        {
            examQuery = examQuery.Where(e => e.CourseId == courseId);
            var courseTitle = await db.Courses.AsNoTracking().Where(c => c.OrganizationId == organizationId && c.Id == courseId)
                .Select(c => c.Title).FirstOrDefaultAsync(ct);
            if (courseTitle is null) await db.RefuseForeignAsync(OwnershipScope.Kind.Course, courseId, "Opening analytics for a course", ct);
            scopeTitle = courseTitle is { } t ? "Course: " + t : "Unknown course";
        }
        var exams = await examQuery.Select(e => new { e.Id, e.Title, e.CollectionId }).ToListAsync(ct);
        var examIds = exams.Select(e => e.Id).ToList();
        var examTitle = exams.ToDictionary(e => e.Id, e => e.Title);

        var assignments = await db.ExamAssignments.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && examIds.Contains(a.ExamId))
            .Select(a => new { a.Id, a.ExamId }).ToListAsync(ct);
        var sessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && examIds.Contains(s.ExamId))
            .Select(s => new { s.Id, s.ExamId, s.AssignmentId, s.State, s.StartedAt, s.EndReason }).ToListAsync(ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var sessionExam = sessions.ToDictionary(s => s.Id, s => s.ExamId);
        // Sessions whose AI and exam-time evaluation is hidden from this viewer (blind grading): counted in completion only.
        var blind = Grading.BlindGrading.AppliesToRole(viewerRole)
            ? await Grading.BlindGrading.BlindSessionsAsync(db, organizationId, sessionIds, (clock ?? TimeProvider.System).GetUtcNow(), ct)
            : [];
        var openIds = blind.Count == 0 ? sessionIds : sessionIds.Where(id => !blind.Contains(id)).ToList();

        // ---- completion
        var completion = exams.Select(e =>
        {
            var asg = assignments.Where(a => a.ExamId == e.Id).Select(a => a.Id).ToHashSet();
            var ss = sessions.Where(s => s.ExamId == e.Id && asg.Contains(s.AssignmentId)).ToList();
            var completed = ss.Where(s => s.State == SessionState.Ended).Select(s => s.AssignmentId).Distinct().Count();
            var started = ss.Where(s => s.StartedAt is not null || s.State is SessionState.Running or SessionState.Paused or SessionState.Ended)
                .Select(s => s.AssignmentId).Distinct().Count();
            var inProgress = ss.Where(s => s.State is SessionState.Created or SessionState.ConsentGiven or SessionState.Running or SessionState.Paused)
                .Select(s => s.AssignmentId).Distinct().Count();
            var failed = ss.Count(s => s.State == SessionState.Failed);
            return new CompletionRow(e.Id, e.Title, asg.Count, started, completed, inProgress, failed);
        }).OrderBy(r => r.ExamTitle).ToList();

        // ---- objectives and grades
        var objectives = await db.LearningObjectives.AsNoTracking()
            .Where(o => o.OrganizationId == organizationId && examIds.Contains(o.ExamId))
            .OrderBy(o => o.ExamId).ThenBy(o => o.Ordinal)
            .Select(o => new { o.Id, o.ExamId, o.Title, o.Key }).ToListAsync(ct);
        var objectiveTitle = objectives.ToDictionary(o => o.Id, o => string.IsNullOrWhiteSpace(o.Title) ? o.Key : o.Title);

        var aiAll = await db.AiObjectiveGrades.AsNoTracking()
            .Where(g => g.OrganizationId == organizationId && openIds.Contains(g.SessionId))
            .Select(g => new { g.Id, g.SessionId, g.ObjectiveId, g.Level, g.Score, g.MaxScore, g.MisconceptionsJson, g.IsMock, g.CreatedAt })
            .ToListAsync(ct);
        var mockExcluded = aiAll.Count(g => g.IsMock);
        var aiLatest = aiAll.Where(g => !g.IsMock).GroupBy(g => (g.SessionId, g.ObjectiveId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());

        var humanAll = await db.HumanObjectiveGrades.AsNoTracking()
            .Where(g => g.OrganizationId == organizationId && sessionIds.Contains(g.SessionId))
            .Select(g => new { g.Id, g.SessionId, g.ObjectiveId, g.Level, g.Score, g.MaxScore, g.SupersedesId, g.SubmittedAt })
            .ToListAsync(ct);
        var superseded = humanAll.Where(g => g.SupersedesId is not null).Select(g => g.SupersedesId!.Value).ToHashSet();
        var humanLatest = humanAll.Where(g => !superseded.Contains(g.Id)).GroupBy(g => (g.SessionId, g.ObjectiveId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SubmittedAt).First());

        var mastery = new List<ObjectiveMasteryRow>();
        foreach (var o in objectives)
        {
            var exam = examTitle.GetValueOrDefault(o.ExamId, "");
            var human = new int[6];
            foreach (var g in humanLatest.Values.Where(g => g.ObjectiveId == o.Id)) human[LevelIndex(g.Level)]++;
            var ai = new int[6];
            foreach (var g in aiLatest.Values.Where(g => g.ObjectiveId == o.Id)) ai[LevelIndex(g.Level)]++;
            mastery.Add(new ObjectiveMasteryRow(o.Id, objectiveTitle[o.Id], exam, LevelSource.Human, human));
            mastery.Add(new ObjectiveMasteryRow(o.Id, objectiveTitle[o.Id], exam, LevelSource.Ai, ai));
        }

        // ---- engine concept mastery (existing sessions, before objective grading exists)
        var conceptRecords = await db.ConceptMastery.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && openIds.Contains(c.SessionId))
            .Select(c => new { c.SessionId, c.ConceptKey, c.Estimate, c.Status }).ToListAsync(ct);
        var collectionIds = exams.Select(e => e.CollectionId).Distinct().ToList();
        var conceptTitles = (await db.Concepts.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId && collectionIds.Contains(c.CollectionId))
                .Select(c => new { c.CollectionId, c.Key, c.Title }).ToListAsync(ct))
            .GroupBy(c => (c.CollectionId, c.Key)).ToDictionary(g => g.Key, g => g.First().Title);
        var examCollection = exams.ToDictionary(e => e.Id, e => e.CollectionId);
        var conceptRows = conceptRecords
            .GroupBy(c => (Exam: sessionExam[c.SessionId], c.ConceptKey))
            .Select(g => new ConceptMasteryRow(g.Key.ConceptKey,
                conceptTitles.GetValueOrDefault((examCollection[g.Key.Exam], g.Key.ConceptKey), g.Key.ConceptKey),
                examTitle[g.Key.Exam], g.Select(x => x.SessionId).Distinct().Count(), g.Average(x => x.Estimate),
                g.Count(x => x.Status == "Mastered"), g.Count(x => x.Status == "Open"), g.Count(x => x.Status == "Aporia"),
                g.Count(x => x.Status == "Exhausted"), g.Count(x => x.Status == "NotStarted")))
            .OrderBy(r => r.ExamTitle).ThenBy(r => r.MeanEstimate).ToList();

        // ---- misconceptions: AI grades' identified misconceptions + evidence recorded with stance Misconception
        var evidence = await db.AssessmentEvidence.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId && openIds.Contains(e.SessionId) && e.Stance == EvidenceStance.Misconception)
            .Select(e => new { e.SessionId, e.ObjectiveId, e.Note }).ToListAsync(ct);
        var mentions = new List<(string Text, Guid ObjectiveId, Guid SessionId, string Source)>();
        foreach (var g in aiLatest.Values)
            foreach (var text in ParseTextList(g.MisconceptionsJson))
                mentions.Add((text, g.ObjectiveId, g.SessionId, "AI grade"));
        foreach (var e in evidence.Where(e => !string.IsNullOrWhiteSpace(e.Note)))
            mentions.Add((e.Note.Trim(), e.ObjectiveId, e.SessionId, "evidence"));
        var misconceptions = mentions
            .GroupBy(m => (Key: Normalize(m.Text), m.ObjectiveId))
            .Select(g => new MisconceptionRow(g.First().Text, objectiveTitle.GetValueOrDefault(g.Key.ObjectiveId, "(unknown objective)"),
                g.Select(m => m.SessionId).Distinct().Count(), g.Count(), string.Join(", ", g.Select(m => m.Source).Distinct().Order())))
            .OrderByDescending(r => r.Sessions).ThenByDescending(r => r.Mentions).ThenBy(r => r.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ---- class-wide gaps
        var gaps = new List<GapRow>();
        foreach (var o in objectives)
        {
            var row = mastery.FirstOrDefault(m => m.ObjectiveId == o.Id && m.Source == LevelSource.Human && m.Conclusive > 0)
                ?? mastery.First(m => m.ObjectiveId == o.Id && m.Source == LevelSource.Ai);
            var basis = row.Source == LevelSource.Human ? "human scores" : "AI preliminary scores";
            if (row.LowShare is { } low && low >= GapLowShare)
                gaps.Add(new GapRow(row.Objective, row.ExamTitle, basis,
                    $"{Pct(low)} at Recognition or Basic Explanation (mean level {row.MeanConclusiveLevel:0.0})", row.Conclusive));
            if (row.InsufficientShare is { } ins && ins >= EvidenceGapShare)
                gaps.Add(new GapRow(row.Objective, row.ExamTitle, basis + " (evidence gap)",
                    $"{Pct(ins)} Insufficient Evidence: more questioning needed, not a knowledge deficit", row.Total));
        }
        foreach (var c in conceptRows.Where(c => c.MeanEstimate < GapConceptEstimate))
            gaps.Add(new GapRow(c.Concept, c.ExamTitle, "engine mastery estimate",
                $"mean estimate {c.MeanEstimate:0.00} of 1", c.Sessions));

        // ---- AI-human agreement
        var pairs = humanLatest.Where(h => aiLatest.ContainsKey(h.Key))
            .Select(h => (Key: h.Key, Ai: aiLatest[h.Key], Human: h.Value)).ToList();
        double Points(decimal score, decimal max) => max <= 0 ? 0 : (double)(score / max * 100m);
        var pairStats = pairs.Select(p => (p.Key.ObjectiveId,
            LevelDiff: Math.Abs((int)p.Ai.Level - (int)p.Human.Level),
            Signed: Points(p.Ai.Score, p.Ai.MaxScore) - Points(p.Human.Score, p.Human.MaxScore))).ToList();
        var byObjective = pairStats.GroupBy(p => p.ObjectiveId)
            .Select(g => new AgreementObjectiveRow(objectiveTitle.GetValueOrDefault(g.Key, "(unknown objective)"), g.Count(),
                g.Count(x => x.LevelDiff == 0) / (double)g.Count(), g.Count(x => x.LevelDiff <= 1) / (double)g.Count(),
                g.Average(x => x.Signed), g.Average(x => Math.Abs(x.Signed))))
            .OrderBy(r => r.Objective).ToList();
        var disagreements = await db.GradeDisagreements.AsNoTracking()
            .Where(d => d.OrganizationId == organizationId && sessionIds.Contains(d.SessionId))
            .Select(d => new { d.Id, d.Band, d.ResolvedAt }).ToListAsync(ct);
        var disagreementIds = disagreements.Select(d => d.Id).ToList();
        var reconciliations = await db.GradeReconciliations.AsNoTracking()
            .CountAsync(r => r.OrganizationId == organizationId && disagreementIds.Contains(r.DisagreementId), ct);
        var agreement = new AgreementSummary(pairStats.Count,
            pairStats.Count == 0 ? null : pairStats.Count(x => x.LevelDiff == 0) / (double)pairStats.Count,
            pairStats.Count == 0 ? null : pairStats.Count(x => x.LevelDiff <= 1) / (double)pairStats.Count,
            pairStats.Count == 0 ? null : pairStats.Average(x => x.Signed),
            pairStats.Count == 0 ? null : pairStats.Average(x => Math.Abs(x.Signed)),
            byObjective, disagreements.Count, disagreements.Count(d => d.Band == DisagreementBand.AdditionalReview),
            disagreements.Count(d => d.Band == DisagreementBand.MandatoryReconciliation), disagreements.Count(d => d.ResolvedAt is not null),
            reconciliations);

        // ---- transcript corrections and clarifications
        var responses = await db.StudentResponses.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && sessionIds.Contains(r.SessionId))
            .Select(r => new { r.Id, r.VerificationState }).ToListAsync(ct);
        var responseIds = responses.Select(r => r.Id).ToList();
        var versions = await db.TranscriptVersions.AsNoTracking()
            .Where(v => v.OrganizationId == organizationId && responseIds.Contains(v.ResponseId) && v.CorrectionKind != null)
            .Select(v => new { v.ResponseId, v.CorrectionKind }).ToListAsync(ct);
        var clarifications = await db.ClarificationEvents.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && sessionIds.Contains(c.SessionId))
            .Select(c => new { c.Type, c.AnsweredAt }).ToListAsync(ct);
        var corrections = new CorrectionSummary(responses.Count, versions.Select(v => v.ResponseId).Distinct().Count(),
            Enum.GetValues<CorrectionKind>().ToDictionary(k => CorrectionLabel(k), k => versions.Count(v => v.CorrectionKind == k)),
            Enum.GetValues<ClarificationType>().ToDictionary(t => t.ToString(), t => clarifications.Count(c => c.Type == t)),
            clarifications.Count(c => c.AnsweredAt is not null),
            Enum.GetValues<VerificationState>().ToDictionary(s => s.ToString(), s => responses.Count(r => r.VerificationState == s)));

        // ---- reliability
        var judgments = await (from j in db.Judgments.AsNoTracking()
                               join t in db.SessionTurns.AsNoTracking() on j.TurnId equals t.Id
                               where j.OrganizationId == organizationId && openIds.Contains(t.SessionId)
                               select new { j.Agreement, j.NeedsHumanReview }).ToListAsync(ct);
        var buckets = new int[5];
        foreach (var j in judgments) buckets[Math.Clamp((int)(j.Agreement * 5), 0, 4)]++;
        var ended = sessions.Where(s => s.State == SessionState.Ended).ToList();
        var reliability = new ReliabilitySummary(aiLatest.Count, aiLatest.Values.Count(g => g.Level == MasteryLevel.InsufficientEvidence),
            humanLatest.Count, humanLatest.Values.Count(g => g.Level == MasteryLevel.InsufficientEvidence), mockExcluded,
            pairStats.Count, reconciliations, disagreements.Count(d => d.Band == DisagreementBand.MandatoryReconciliation),
            judgments.Count, judgments.Count == 0 ? null : judgments.Average(j => j.Agreement), judgments.Count(j => j.NeedsHumanReview),
            buckets, ended.Count, ended.GroupBy(s => string.IsNullOrWhiteSpace(s.EndReason) ? "(not recorded)" : s.EndReason!)
                .OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count()));

        return new AnalyticsReport(scopeTitle, exams.Count, sessions.Count, completion, mastery, conceptRows, misconceptions, gaps,
            agreement, corrections, reliability, (clock ?? TimeProvider.System).GetUtcNow());
    }

    public static string CorrectionLabel(CorrectionKind k) => k switch
    {
        CorrectionKind.Transcription => "Transcription correction",
        CorrectionKind.MeaningClarification => "Meaning clarification",
        CorrectionKind.SubstantiveRevision => "Substantive revision",
        _ => k.ToString(),
    };

    public static string Pct(double? v) => v is { } x ? (x * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "—";

    private static int LevelIndex(MasteryLevel level) => Math.Clamp((int)level, 0, 5);

    private static string Normalize(string text) =>
        string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim('.', ',', ';', ':', '!', '?', ' ');

    /// <summary>
    /// Reads a JSON array of misconceptions: strings, or objects with a text-like property
    /// ("text", "misconception", "description", "summary", "statement"). Anything unreadable is skipped.
    /// </summary>
    internal static IReadOnlyList<string> ParseTextList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var list = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string? text = item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Object => new[] { "text", "misconception", "description", "summary", "statement", "label" }
                        .Select(n => item.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)))
                        .Where(p => p.Value.ValueKind == JsonValueKind.String).Select(p => p.Value.GetString()).FirstOrDefault(),
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(text)) list.Add(text.Trim().Length > 500 ? text.Trim()[..500] : text.Trim());
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
