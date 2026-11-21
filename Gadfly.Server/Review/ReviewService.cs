using Gadfly.Server.Tenancy;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Integrity;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Review;

/// <summary>
/// Instructor review and grading (GAD-T54, decision D13: the AI recommends, the instructor finalizes).
/// Reads what <see cref="Sessions.SessionOrchestrator"/> persisted for each session and records grade
/// decisions. Every query names the organization explicitly on top of GadflyDbContext's tenant filter.
/// <paramref name="viewerRole"/> is the caller's role: while a session's grading is blind, a viewer who can enter human
/// scores gets no AI or exam-time judge data (<see cref="BlindGrading"/>, decision D25); null means no redaction.
/// </summary>
public sealed class ReviewService(GadflyDbContext db, Guid organizationId, TimeProvider? clock = null, string? viewerRole = null)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>The exams that have at least one session, for the list's exam filter.</summary>
    public async Task<IReadOnlyList<(Guid Id, string Title)>> ExamsWithSessionsAsync(CancellationToken ct = default) =>
        (await db.Exams.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId && db.ExamSessions.Any(s => s.ExamId == e.Id && s.OrganizationId == organizationId))
            .OrderBy(e => e.Title).Select(e => new { e.Id, e.Title }).ToListAsync(ct))
        .Select(e => (e.Id, e.Title)).ToList();

    /// <summary>Sessions to review, newest first.</summary>
    public async Task<IReadOnlyList<ReviewListRow>> ListAsync(ReviewListFilter filter, int take = 500, CancellationToken ct = default)
    {
        var q = db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == organizationId);
        if (filter.ExamId is { } examId) q = q.Where(s => s.ExamId == examId);
        var sessions = await q.OrderByDescending(s => s.EndedAt ?? s.StartedAt ?? s.CreatedAt).Take(take)
            .Select(s => new { s.Id, s.ExamId, s.StudentUserId, s.Attempt, s.State, s.EndedAt }).ToListAsync(ct);
        if (sessions.Count == 0) return [];

        var ids = sessions.Select(s => s.Id).ToList();
        var examIds = sessions.Select(s => s.ExamId).Distinct().ToList();
        var studentIds = sessions.Select(s => s.StudentUserId).Distinct().ToList();

        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == organizationId && examIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Title, ct);
        var names = await UserNamesAsync(studentIds, ct);
        var disagreed = await (from j in db.Judgments
                               join t in db.SessionTurns on j.TurnId equals t.Id
                               where j.OrganizationId == organizationId && j.NeedsHumanReview && ids.Contains(t.SessionId)
                               group j by t.SessionId into g
                               select new { SessionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SessionId, x => x.Count, ct);
        var flags = await db.IntegrityEvents.Where(e => e.OrganizationId == organizationId && ids.Contains(e.SessionId))
            .Select(e => new { e.SessionId, e.Type, e.Source, e.Severity }).ToListAsync(ct);
        var flagCounts = flags.Where(f => f.Severity != IntegritySeverity.Info && !IntegrityExplanations.For(f.Type, f.Source).Normal)
            .GroupBy(f => f.SessionId).ToDictionary(g => g.Key, g => g.Count());
        var reports = (await db.SessionReports.AsNoTracking().Where(r => r.OrganizationId == organizationId && ids.Contains(r.SessionId))
                .Select(r => new { r.SessionId, r.Recommendation, r.OverallScore, r.CreatedAt }).ToListAsync(ct))
            .GroupBy(r => r.SessionId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First());
        var grades = (await db.GradeDecisions.AsNoTracking().Where(g => g.OrganizationId == organizationId && ids.Contains(g.SessionId)).ToListAsync(ct))
            .GroupBy(g => g.SessionId).ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.DecidedAt).First());

        var blind = await BlindAsync(ids, ct);
        var rows = sessions.Select(s => new ReviewListRow(
                s.Id, s.ExamId, exams.GetValueOrDefault(s.ExamId) ?? "(deleted exam)",
                s.StudentUserId, names.GetValueOrDefault(s.StudentUserId) ?? "(unknown student)",
                s.Attempt, s.State, s.EndedAt,
                !blind.Contains(s.Id) && reports.TryGetValue(s.Id, out var r) ? r.Recommendation : null,
                !blind.Contains(s.Id) && reports.TryGetValue(s.Id, out var r2) ? r2.OverallScore : null,
                blind.Contains(s.Id) ? 0 : disagreed.GetValueOrDefault(s.Id), flagCounts.GetValueOrDefault(s.Id),
                grades.GetValueOrDefault(s.Id)))
            .Where(r => !filter.NeedsGradeOnly || r.NeedsGrade)
            .Where(r => !filter.NeedsHumanReviewOnly || r.JudgesDisagreed > 0)
            .ToList();
        return rows;
    }

    /// <summary>One session's full review, or null when it isn't in this organization.</summary>
    public async Task<SessionReviewView?> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == organizationId, ct);
        if (session is null) await db.RefuseForeignAsync(OwnershipScope.Kind.Session, sessionId, "Opening a session review", ct);
        if (session is null) return null;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == session.ExamId && e.OrganizationId == organizationId, ct);
        var assignment = await db.ExamAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == session.AssignmentId && a.OrganizationId == organizationId, ct);

        var titles = exam is null ? new Dictionary<string, string>()
            : await db.Concepts.AsNoTracking().Where(c => c.OrganizationId == organizationId && c.CollectionId == exam.CollectionId)
                .ToDictionaryAsync(c => c.Key, c => c.Title, ct);
        string Title(string key) => titles.TryGetValue(key, out var t) && !string.IsNullOrWhiteSpace(t) ? t : key;

        var blinded = (await BlindAsync([sessionId], ct)).Contains(sessionId);
        var turns = await db.SessionTurns.AsNoTracking().Where(t => t.SessionId == sessionId && t.OrganizationId == organizationId)
            .OrderBy(t => t.Index).ToListAsync(ct);
        var turnIds = turns.Select(t => t.Id).ToList();
        var judgments = (blinded ? [] : await db.Judgments.AsNoTracking().Include(j => j.Votes)
                .Where(j => j.OrganizationId == organizationId && turnIds.Contains(j.TurnId)).ToListAsync(ct))
            .GroupBy(j => j.TurnId).ToDictionary(g => g.Key, g => g.OrderByDescending(j => j.CreatedAt).First());
        var claims = blinded ? [] : await db.SessionClaims.AsNoTracking().Where(c => c.SessionId == sessionId && c.OrganizationId == organizationId).ToListAsync(ct);
        var claimsById = claims.ToDictionary(c => c.Id);

        // Every criterion's citations, resolved to the passage text in one query.
        var criteria = judgments.ToDictionary(kv => kv.Key, kv => ParseCriteria(kv.Value.CriteriaJson));
        var chunkIds = criteria.Values.SelectMany(c => c).SelectMany(c => c.Citations)
            .Select(c => Guid.TryParse(c.ChunkId, out var g) ? g : (Guid?)null).OfType<Guid>().Distinct().ToList();
        var chunks = chunkIds.Count == 0 ? new Dictionary<Guid, (string Text, string? Location, string? Document)>()
            : (await db.SourceChunks.AsNoTracking().Where(c => c.OrganizationId == organizationId && chunkIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Text, c.Location, Document = c.Document!.FileName }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => (c.Text, c.Location, (string?)c.Document));

        var turnViews = turns.Select(t =>
        {
            JudgmentView? jv = null;
            if (judgments.TryGetValue(t.Id, out var j))
            {
                var crit = criteria[t.Id].Select(c => new CriterionView(c.Criterion, c.Score, c.Rationale,
                    c.Citations.Select(cite => Resolve(cite, chunks)).ToList())).ToList();
                jv = new JudgmentView(j.OverallScore, j.Agreement, j.NeedsHumanReview, j.Summary, crit,
                    j.Votes.OrderBy(v => v.ProviderId).Select(v => new VoteView(v.ProviderId, v.Model, v.OverallScore, v.Verdict,
                        Dissents: Math.Abs(v.OverallScore - j.OverallScore) >= 0.25)).ToList(),
                    j.TextStyleScore);
            }
            var contradictions = claims.Where(c => c.TurnId == t.Id && c.ContradictsClaimId is { } other && claimsById.ContainsKey(other))
                .Select(c => new ContradictionView(c.Quote, claimsById[c.ContradictsClaimId!.Value].Quote)).ToList();
            var metrics = ParseMetrics(t.SpeechMetricsJson);
            return new TurnView(t.Id, t.Index, t.ConceptKey, Title(t.ConceptKey), t.Move, t.Question, blinded ? null : t.PlannerReason, t.AskedAt,
                t.AnswerText, t.AnswerMode, t.AnswerStartedAt, t.AnswerCompletedAt, !string.IsNullOrEmpty(t.AudioRef), blinded ? null : t.MasteryAfter,
                metrics, jv, contradictions)
            {
                // Voice strain (GAD-T232) is kept from a blinded grader like the planner's reasoning: it must not prime the grade.
                Strain = blinded || t.AnswerMode != "voice" ? null : Gadfly.Server.Voice.VoiceStrain.Describe(metrics),
            };
        }).ToList();

        var integrity = (await db.IntegrityEvents.AsNoTracking().Where(e => e.SessionId == sessionId && e.OrganizationId == organizationId)
                .OrderBy(e => e.StartedAt).ToListAsync(ct))
            .Select(e =>
            {
                var (label, explanation, normal) = IntegrityExplanations.For(e.Type, e.Source);
                return new IntegrityView(e.Id, e.Source, e.Type, label, explanation, normal, e.StartedAt, e.EndedAt, e.Confidence,
                    e.Severity, e.Summary, e.TurnId, Ongoing: WebcamIntegrityRecorder.EpisodeData.Parse(e.DataJson)?.Ongoing ?? false,
                    SessionOver: session.State is SessionState.Ended or SessionState.Failed);
            }).ToList();

        var mastery = (blinded ? [] : await db.ConceptMastery.AsNoTracking().Where(m => m.SessionId == sessionId && m.OrganizationId == organizationId).ToListAsync(ct))
            .Select(m => new MasteryView(m.ConceptKey, Title(m.ConceptKey), m.Estimate, m.TurnsOnConcept, m.Status))
            .OrderBy(m => turnViews.FindIndex(t => t.ConceptKey == m.ConceptKey) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(m => m.ConceptTitle).ToList();

        var report = blinded ? null : await db.SessionReports.AsNoTracking().Where(r => r.SessionId == sessionId && r.OrganizationId == organizationId)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);

        var grades = await db.GradeDecisions.AsNoTracking().Where(g => g.SessionId == sessionId && g.OrganizationId == organizationId)
            .OrderByDescending(g => g.DecidedAt).ToListAsync(ct);
        var names = await UserNamesAsync([session.StudentUserId, .. grades.Select(g => g.DecidedByUserId)], ct);

        return new SessionReviewView(session, exam?.Title ?? "(deleted exam)", exam?.CollectionId ?? Guid.Empty,
            names.GetValueOrDefault(session.StudentUserId) ?? "(unknown student)", assignment,
            turnViews, integrity, BuildTimeline(turnViews, integrity), mastery, report,
            grades.Select(g => (g, names.GetValueOrDefault(g.DecidedByUserId) ?? "(unknown)")).ToList()) { Blinded = blinded };
    }

    /// <summary>The sessions among <paramref name="ids"/> whose AI and exam-time judgments are hidden from this viewer (D25).</summary>
    private async Task<HashSet<Guid>> BlindAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        BlindGrading.AppliesToRole(viewerRole) ? await BlindGrading.BlindSessionsAsync(db, organizationId, ids, _clock.GetUtcNow(), ct) : [];

    /// <summary>
    /// Records a grade decision. Earlier decisions are kept (history); the latest one is the grade.
    /// Whether it overrides the AI recommendation is computed, not asked.
    /// </summary>
    public async Task<ReviewResult> SaveGradeAsync(Guid sessionId, Guid decidedByUserId, GradeInput input, CancellationToken ct = default)
    {
        var grade = (input.Grade ?? "").Trim();
        if (grade.Length is 0 or > 40) return new(false, "Enter a grade (up to 40 characters), for example A, B+, Pass or mastered.");
        if (input.Score is { } sc && (double.IsNaN(sc) || sc < 0 || sc > 100)) return new(false, "The score is a percentage from 0 to 100.");
        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (comment is { Length: > 4000 }) return new(false, "Keep the comment under 4,000 characters.");

        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == organizationId, ct);
        if (session is null) await db.RefuseForeignAsync(OwnershipScope.Kind.Session, sessionId, "Opening a session review", ct);
        if (session is null) return new(false, "That session was not found in this organization.");
        if (session.State != SessionState.Ended) return new(false, "This session hasn't ended yet; grade it once it has.");
        var report = await db.SessionReports.AsNoTracking().Where(r => r.SessionId == sessionId && r.OrganizationId == organizationId)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);

        db.GradeDecisions.Add(new GradeDecision
        {
            OrganizationId = organizationId,
            SessionId = sessionId,
            DecidedByUserId = decidedByUserId,
            Grade = grade,
            Score = input.Score is { } s ? Math.Round(s, 2) : null,
            Comment = comment,
            OverridesRecommendation = GradePolicy.Overrides(grade, input.Score, report?.Recommendation, report?.OverallScore),
            DecidedAt = _clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return new(true, "Grade saved.");
    }

    // ------------------------------------------------------------ exports

    /// <summary>The session as CSV, one row per turn.</summary>
    public static string ToCsv(SessionReviewView v)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string F(double? d) => d is { } x ? x.ToString("0.###", ci) : "";
        string T(DateTimeOffset? d) => d is { } x ? x.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", ci) : "";
        void Row(params string?[] cells) => sb.AppendJoin(',', cells.Select(Csv)).Append("\r\n");

        Row("session_id", "student", "exam", "attempt", "turn", "concept_key", "concept", "move", "question", "answer", "answer_mode",
            "asked_at", "answer_started_at", "answer_completed_at", "latency_s", "overall_score", "agreement", "judges_disagreed",
            "judge_summary", "criteria", "votes", "text_style_score", "mastery_after", "integrity_events");
        foreach (var t in v.Turns)
        {
            var j = t.Judgment;
            var events = v.Integrity.Where(e => e.TurnId == t.TurnId).Select(e => e.Type);
            Row(v.Session.Id.ToString(), v.StudentName, v.ExamTitle, v.Session.Attempt.ToString(ci), t.Index.ToString(ci), t.ConceptKey,
                t.ConceptTitle, t.Move, t.Question, t.AnswerText, t.AnswerMode, T(t.AskedAt), T(t.AnswerStartedAt), T(t.AnswerCompletedAt),
                F(t.LatencySeconds), F(j?.OverallScore), F(j?.Agreement), j is null ? "" : j.NeedsHumanReview ? "yes" : "no", j?.Summary,
                j is null ? "" : string.Join(" | ", j.Criteria.Select(c => $"{c.Criterion}={F(c.Score)}")),
                j is null ? "" : string.Join(" | ", j.Votes.Select(x => $"{x.ProviderId}={F(x.OverallScore)}")),
                F(j?.TextStyleScore), F(t.MasteryAfter), string.Join(" | ", events));
        }
        return sb.ToString();
    }

    /// <summary>The AI report as Markdown, with the instructor's decision appended when there is one.</summary>
    public static string ToMarkdown(SessionReviewView v)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<!-- Gadfly session {v.Session.Id}: {v.StudentName}, {v.ExamTitle}, attempt {v.Session.Attempt} -->");
        if (v.Report is { Markdown.Length: > 0 } r) sb.AppendLine(r.Markdown.TrimEnd());
        else if (v.Blinded) sb.AppendLine("# Socratic session report").AppendLine().AppendLine("The AI report is hidden until the human evaluation is submitted (blind grading).");
        else sb.AppendLine("# Socratic session report").AppendLine().AppendLine("No AI report yet: the session hasn't ended.");
        if (v.LatestGrade is { } g)
        {
            var ci = CultureInfo.InvariantCulture;
            sb.AppendLine().AppendLine("## Instructor decision").AppendLine();
            sb.AppendLine($"- Grade: **{g.Grade}**" + (g.Score is { } s ? $" ({s.ToString("0.##", ci)}%)" : ""));
            sb.AppendLine($"- Decided by {v.Grades[0].DecidedBy} on {g.DecidedAt.UtcDateTime.ToString("u", ci)}");
            if (!v.Blinded) sb.AppendLine(g.OverridesRecommendation ? "- Overrides the AI recommendation" : "- Agrees with the AI recommendation");
            if (!string.IsNullOrWhiteSpace(g.Comment)) sb.AppendLine().AppendLine(g.Comment);
        }
        return sb.ToString();
    }

    /// <summary>A file-name-safe slug for exports.</summary>
    public static string FileStem(SessionReviewView v)
    {
        var raw = $"{v.StudentName}-{v.ExamTitle}-attempt{v.Session.Attempt}";
        var clean = new string(raw.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        while (clean.Contains("--")) clean = clean.Replace("--", "-");
        return clean.Length == 0 ? v.Session.Id.ToString("N") : clean[..Math.Min(clean.Length, 80)];
    }

    // ------------------------------------------------------------ helpers

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        return await db.AuthUsers.AsNoTracking().Where(u => distinct.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
    }

    private static List<CriterionScore> ParseCriteria(string json)
    {
        try { return JsonSerializer.Deserialize<List<CriterionScore>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyDictionary<string, double> ParseMetrics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, double>();
        try { return JsonSerializer.Deserialize<Dictionary<string, double>>(json, Json) ?? new(); }
        catch (JsonException) { return new Dictionary<string, double>(); }
    }

    private static CitedPassage Resolve(Citation cite, IReadOnlyDictionary<Guid, (string Text, string? Location, string? Document)> chunks) =>
        Guid.TryParse(cite.ChunkId, out var id) && chunks.TryGetValue(id, out var c)
            ? new CitedPassage(cite.ChunkId, cite.Quote, c.Text, c.Document, c.Location)
            : new CitedPassage(cite.ChunkId, cite.Quote, null, null, null);

    /// <summary>
    /// Turns in order, with each integrity event attached to its turn (by TurnId, else by falling inside
    /// the turn's question-to-answer window) or placed on its own between turns by time.
    /// </summary>
    internal static IReadOnlyList<TimelineItem> BuildTimeline(IReadOnlyList<TurnView> turns, IReadOnlyList<IntegrityView> events)
    {
        var during = turns.ToDictionary(t => t.TurnId, _ => new List<IntegrityView>());
        var loose = new List<IntegrityView>();
        foreach (var e in events)
        {
            var owner = e.TurnId is { } tid && during.ContainsKey(tid) ? turns.First(t => t.TurnId == tid)
                : turns.FirstOrDefault(t => e.StartedAt >= t.AskedAt && e.StartedAt <= (t.AnswerCompletedAt ?? t.AskedAt));
            if (owner is not null) during[owner.TurnId].Add(e);
            else loose.Add(e);
        }
        var items = turns.Select(t => new TimelineItem(t.AskedAt, t, null, during[t.TurnId]))
            .Concat(loose.Select(e => new TimelineItem(e.StartedAt, null, e, [])))
            .OrderBy(i => i.At).ThenBy(i => i.Turn is null ? 1 : 0).ToList();
        return items;
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Neutralize spreadsheet formula injection, then quote when needed.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

/// <summary>Whether an instructor's grade differs from the AI recommendation (decision D13).</summary>
public static class GradePolicy
{
    // Conventional grades that agree with each recommendation. Anything else counts as an override, so
    // the instructor's own words always win and the record says so.
    private static readonly Dictionary<string, string[]> Agreeing = new(StringComparer.Ordinal)
    {
        [Engine.SessionReport.Mastered] = ["mastered", "pass", "passed", "a+", "a", "a-", "excellent", "proficient"],
        [Engine.SessionReport.Partial] = ["partial", "b+", "b", "b-", "c+", "c", "c-", "developing", "satisfactory"],
        [Engine.SessionReport.NotDemonstrated] = ["not-demonstrated", "not demonstrated", "fail", "failed", "d+", "d", "d-", "f", "incomplete"],
    };

    /// <summary>The grades that would agree with a recommendation, for the form's hint.</summary>
    public static IReadOnlyList<string> AgreeingGrades(string? recommendation) =>
        recommendation is not null && Agreeing.TryGetValue(recommendation, out var g) ? g : [];

    /// <summary>
    /// True when the grade names a different outcome than the recommendation, or the score is more than
    /// 10 points from the AI's overall mastery. False when there is no recommendation to override.
    /// </summary>
    public static bool Overrides(string grade, double? score, string? recommendation, double? aiScore)
    {
        if (string.IsNullOrWhiteSpace(recommendation)) return false;
        var g = grade.Trim().ToLowerInvariant();
        var gradeAgrees = Agreeing.TryGetValue(recommendation, out var ok) ? ok.Contains(g) : g == recommendation.ToLowerInvariant();
        var scoreAgrees = score is not { } s || aiScore is not { } ai || Math.Abs(s - ai * 100) <= 10;
        return !gradeAgrees || !scoreAgrees;
    }
}
