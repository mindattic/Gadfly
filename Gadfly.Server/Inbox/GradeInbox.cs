using System.Text;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Localization;
using Gadfly.Sources.Grading;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Inbox;

/// <summary>
/// The student's inbox messages about their grade (GAD-T702), written from <see cref="GradingEvents"/> so the web process and the
/// grading worker (system scope, no HttpContext) behave the same. Rows are added to the caller's context and saved with the grading
/// change that caused them; an existing message of the same kind for the session is left as it is. The text is built from what
/// grading already stored (no model call), in the student's primary language (the one their emails use), English otherwise.
/// </summary>
internal static class GradeInbox
{
    /// <summary>
    /// The AI panel's result, labelled preliminary: sent when a person still decides the grade (human-only and combined grading, or
    /// an AI-only attempt held for instructor review). Never linked: the result page shows nothing until the grade is published.
    /// </summary>
    public static async Task PreliminaryAsync(GadflyDbContext db, FinalGrade grade, PolicySnapshot policy, DateTimeOffset at, CancellationToken ct)
    {
        if (policy.Rules.Mode == GradingMode.AiOnly && !await Review.InstructorReview.IsHeldAsync(db, grade.OrganizationId, grade.SessionId, ct)) return;
        var c = await ContextAsync(db, grade, InboxKinds.GradePreliminary, ct);
        if (c is null) return;

        var body = new StringBuilder();
        body.AppendLine(c.T("inbox.preliminary.intro"));
        if (c.Ai.Values.Any(a => a.IsMock)) body.AppendLine().AppendLine(c.T("inbox.mock"));
        body.AppendLine().AppendLine(c.T("inbox.preliminary.objectives"));
        foreach (var o in c.Objectives)
        {
            if (!c.Ai.TryGetValue(o.Id, out var ai)) continue;
            body.AppendLine().AppendLine(o.Title);
            body.AppendLine(c.Level(ai.Level));
            body.AppendLine(c.T("inbox.confidence", c.T($"inbox.confidence.{ai.Confidence}")));
            AppendImpressions(body, c, ai);
        }
        Add(db, grade, c, InboxKinds.GradePreliminary, c.T("inbox.preliminary.title", c.ExamTitle), body, null, at);
    }

    /// <summary>The published grade, in any grading mode, linked to the student's result page.</summary>
    public static async Task FinalAsync(GadflyDbContext db, FinalGrade grade, DateTimeOffset at, CancellationToken ct)
    {
        var c = await ContextAsync(db, grade, InboxKinds.GradeFinal, ct);
        if (c is null) return;
        var aiOnly = (GradingJson.Read<PolicySnapshot>(grade.PolicyJson)?.Rules.Mode ?? GradingMode.Combined) == GradingMode.AiOnly;
        var human = aiOnly ? [] : (await db.HumanObjectiveGrades.AsNoTracking()
                .Where(h => h.SessionId == grade.SessionId && h.OrganizationId == grade.OrganizationId).ToListAsync(ct))
            .GroupBy(h => h.ObjectiveId).ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.SubmittedAt).First());
        var scores = GradingJson.Read<StoredResult>(grade.ResultJson)?.Result.Objectives.ToDictionary(o => o.ObjectiveKey) ?? [];

        var body = new StringBuilder();
        body.AppendLine(c.T(aiOnly ? "inbox.final.introAi" : "inbox.final.intro", c.ExamTitle));
        var final = grade.LetterOrLabel is { } label && grade.FinalPercent is { } p ? $"{c.Number(p)}% ({label})"
            : grade.LetterOrLabel ?? (grade.FinalPercent is { } q ? $"{c.Number(q)}%" : null);
        if (final is not null) body.AppendLine(c.T("inbox.final.grade", final));

        var levels = new List<(string Title, MasteryLevel Level)>();
        body.AppendLine().AppendLine(c.T("inbox.final.objectives"));
        foreach (var o in c.Objectives)
        {
            var h = human.GetValueOrDefault(o.Id);
            var ai = c.Ai.GetValueOrDefault(o.Id);
            if (h is null && ai is null) continue;
            var level = h?.Level ?? ai!.Level;
            levels.Add((o.Title, level));
            body.AppendLine().AppendLine(o.Title);
            body.AppendLine(c.Level(level));
            if (scores.TryGetValue(o.Key, out var s) && s.Combined is { } combined && s.MaxScore > 0)
                body.AppendLine(c.T("inbox.final.score", $"{c.Number(combined / s.MaxScore * 100m)}%"));
            if (h is not null)
            {
                if (!string.IsNullOrWhiteSpace(h.Comment)) body.AppendLine(c.T("inbox.final.comments", h.Comment.Trim()));
            }
            else AppendImpressions(body, c, ai!);
        }

        var conclusive = levels.Where(l => MasteryLevels.IsConclusive(l.Level)).ToList();
        if (conclusive.Count > 1)
        {
            var best = conclusive.MaxBy(l => l.Level);
            var worst = conclusive.MinBy(l => l.Level);
            body.AppendLine().AppendLine(best.Level == worst.Level
                ? c.T("inbox.final.overallEven", c.Level(best.Level))
                : c.T("inbox.final.overall", best.Title, worst.Title));
        }
        body.AppendLine().AppendLine(c.T("inbox.final.more"));
        Add(db, grade, c, InboxKinds.GradeFinal, c.T("inbox.final.title", c.ExamTitle), body, $"/my-results/{grade.SessionId:D}", at);
    }

    /// <summary>"Impressions of your understanding", from the AI grade's stored explanation, misconceptions and evidence.</summary>
    private static void AppendImpressions(StringBuilder body, Context c, AiObjectiveGrade ai)
    {
        if (!string.IsNullOrWhiteSpace(ai.Explanation)) body.AppendLine(c.T("inbox.impressions", Clip(ai.Explanation.Trim(), 800)));
        var misconceptions = GradingJson.ReadList<NotedMisconception>(ai.MisconceptionsJson).Select(m => m.Text.Trim()).Where(t => t.Length > 0).Take(3).ToList();
        if (misconceptions.Count > 0) body.AppendLine(c.T("inbox.misconceptions", string.Join("; ", misconceptions)));
        var evidence = GradingJson.ReadList<Guid>(ai.EvidenceRefsJson).Count;
        body.AppendLine(evidence > 0 ? c.T("inbox.evidence", evidence) : c.T("inbox.evidenceNone"));
    }

    private static void Add(GadflyDbContext db, FinalGrade grade, Context c, string kind, string title, StringBuilder body, string? link, DateTimeOffset at) =>
        db.InboxMessages.Add(new InboxMessage
        {
            OrganizationId = grade.OrganizationId, RecipientUserId = c.StudentUserId, Kind = kind, Title = Clip(title, 200),
            Body = body.ToString().TrimEnd(), LinkUrl = link, SessionId = grade.SessionId, CreatedAt = at,
        });

    private static async Task<Context?> ContextAsync(GadflyDbContext db, FinalGrade grade, string kind, CancellationToken ct)
    {
        var info = await (from s in db.ExamSessions.AsNoTracking()
                          join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
                          where s.Id == grade.SessionId && s.OrganizationId == grade.OrganizationId
                          select new { s.StudentUserId, s.ExamId, e.Title }).FirstOrDefaultAsync(ct);
        if (info is null) return null;
        if (await db.InboxMessages.AnyAsync(m => m.OrganizationId == grade.OrganizationId && m.RecipientUserId == info.StudentUserId
                && m.Kind == kind && m.SessionId == grade.SessionId, ct)
            || db.InboxMessages.Local.Any(m => m.Kind == kind && m.SessionId == grade.SessionId && m.RecipientUserId == info.StudentUserId)) return null;
        var language = await db.StudentLanguageProfiles.AsNoTracking()
            .Where(p => p.OrganizationId == grade.OrganizationId && p.UserId == info.StudentUserId)
            .Select(p => p.PrimaryLanguage).FirstOrDefaultAsync(ct);
        var objectives = await db.LearningObjectives.AsNoTracking()
            .Where(o => o.ExamId == info.ExamId && o.OrganizationId == grade.OrganizationId).OrderBy(o => o.Ordinal).ToListAsync(ct);
        var ai = (await db.AiObjectiveGrades.AsNoTracking().Where(a => a.SessionId == grade.SessionId && a.OrganizationId == grade.OrganizationId).ToListAsync(ct))
            .GroupBy(a => a.ObjectiveId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.CreatedAt).First());
        return new Context(info.StudentUserId, info.Title, UiCatalog.Normalize(language), objectives, ai);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    private sealed record Context(Guid StudentUserId, string ExamTitle, string Language, IReadOnlyList<LearningObjective> Objectives,
        IReadOnlyDictionary<Guid, AiObjectiveGrade> Ai)
    {
        public string T(string key, params object[] args) => new StudentText(key, args).Render(UiCatalog.Embedded, Language);
        public string Level(MasteryLevel level) => T("inbox.level", (int)level, T($"inbox.level.{(int)level}"));
        public string Number(decimal n) => n.ToString("0.##", UiCatalog.CultureFor(Language));
    }
}
