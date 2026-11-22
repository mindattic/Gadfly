using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Analytics;

// Typed versus spoken answers (GAD-T438; ElenchusAtScale Section 5.7.1 "What would test it" and Appendix A.10 "the typed group";
// docs/EVIDENCE_MAP.md owner decision (a)). Typed answers are an instructor-granted accommodation (GAD-T260), so the typed group
// chose itself: its students differ from the rest in the very needs that earned the grant. The comparison is made objective by
// objective, so a difference is not just harder objectives on one side, and it reports levels (human and panel), how often the
// panel and the human agree, and the integrity-note rate. It measures the typed group; it does not change decision (a).

/// <summary>One objective's latest panel and human levels for one session (either may be missing).</summary>
public sealed record ModeGrade(bool Typed, Guid Student, Guid ObjectiveId, string Objective, int? PanelLevel, int? HumanLevel);

/// <summary>One answered session: its answering minutes and its integrity notes at Low or above.</summary>
public sealed record ModeSession(bool Typed, Guid Student, double AnswerMinutes, int Notes);

/// <summary>One side (typed or spoken) of the comparison. Means use levels 1 to 5; level 0 is left out.</summary>
public sealed record ModeSide(int Students, int Sessions, int Graded, double? MeanHuman, double? MeanPanel, double? SameLevel, double? MeanAbsGap,
    double? NotesPer10Min, double? SessionsWithNotes)
{
    public bool TooFew => Students < GroupFairness.MinStudents;

    public static readonly ModeSide Withheld = new(0, 0, 0, null, null, null, null, null, null);
}

/// <summary>One objective's typed and spoken sides; a side under five students is withheld (its size kept).</summary>
public sealed record ObjectiveModeRow(string Objective, ModeSide Typed, ModeSide Spoken);

/// <param name="HumanGapSameObjective">Typed minus spoken mean human level, averaged over objectives where both sides can be reported.</param>
public sealed record TypedSpokenReport(ModeSide Typed, ModeSide Spoken, IReadOnlyList<ObjectiveModeRow> Objectives, double? HumanGapSameObjective,
    double? PanelGapSameObjective, IReadOnlyList<string> Warnings);

public sealed class TypedSpoken(GadflyDbContext db, Guid organizationId)
{
    /// <summary>Same-level agreement that differs by this much between typed and spoken is warned.</summary>
    public const double AgreementGap = 0.15;

    /// <summary>Integrity-note rates that differ by more than this ratio are warned (as the gaze rates).</summary>
    public const double DisparityRatio = 1.25;

    public const string Caveat =
        "Typed answers are an accommodation an instructor grants for a documented need, so the typed group chose itself and differs " +
        "from the rest in more than how it answered: a difference here is not the effect of typing. Levels are compared objective by " +
        "objective. Integrity notes differ in kind as well as number (a typed answer can carry a paste note; only a spoken one can be " +
        "read out), so compare the rates with care.";

    public async Task<TypedSpokenReport> BuildAsync(IReadOnlyList<FairSession> sessions, CancellationToken ct = default)
    {
        var byId = sessions.ToDictionary(s => s.Id);
        var ids = byId.Keys.ToList();
        var ai = new List<(Guid SessionId, Guid ObjectiveId, int Level, DateTimeOffset At)>();
        var human = new List<(Guid Id, Guid SessionId, Guid ObjectiveId, int Level, Guid? Supersedes, DateTimeOffset At)>();
        var turns = new List<(Guid SessionId, DateTimeOffset AskedAt, DateTimeOffset? Started, DateTimeOffset Completed)>();
        var notes = new List<Guid>();
        foreach (var chunk in ids.Chunk(500))
        {
            ai.AddRange((await db.AiObjectiveGrades.AsNoTracking()
                    .Where(g => g.OrganizationId == organizationId && chunk.Contains(g.SessionId) && !g.IsMock)
                    .Select(g => new { g.SessionId, g.ObjectiveId, g.Level, g.CreatedAt }).ToListAsync(ct))
                .Select(g => (g.SessionId, g.ObjectiveId, (int)g.Level, g.CreatedAt)));
            human.AddRange((await db.HumanObjectiveGrades.AsNoTracking()
                    .Where(g => g.OrganizationId == organizationId && chunk.Contains(g.SessionId))
                    .Select(g => new { g.Id, g.SessionId, g.ObjectiveId, g.Level, g.SupersedesId, g.SubmittedAt }).ToListAsync(ct))
                .Select(g => (g.Id, g.SessionId, g.ObjectiveId, (int)g.Level, g.SupersedesId, g.SubmittedAt)));
            turns.AddRange((await db.SessionTurns.AsNoTracking()
                    .Where(t => t.OrganizationId == organizationId && chunk.Contains(t.SessionId) && t.AnswerCompletedAt != null)
                    .Select(t => new { t.SessionId, t.AskedAt, t.AnswerStartedAt, t.AnswerCompletedAt }).ToListAsync(ct))
                .Select(t => (t.SessionId, t.AskedAt, t.AnswerStartedAt, t.AnswerCompletedAt!.Value)));
            notes.AddRange(await db.IntegrityEvents.AsNoTracking()
                .Where(e => e.OrganizationId == organizationId && chunk.Contains(e.SessionId) && e.Severity >= IntegritySeverity.Low)
                .Select(e => e.SessionId).ToListAsync(ct));
        }
        var objectiveIds = ai.Select(g => g.ObjectiveId).Concat(human.Select(g => g.ObjectiveId)).Distinct().ToList();
        var titles = (await db.LearningObjectives.AsNoTracking()
                .Where(o => o.OrganizationId == organizationId && objectiveIds.Contains(o.Id))
                .Select(o => new { o.Id, o.Title, o.Key }).ToListAsync(ct))
            .ToDictionary(o => o.Id, o => string.IsNullOrWhiteSpace(o.Title) ? o.Key : o.Title);

        var aiLatest = ai.GroupBy(g => (g.SessionId, g.ObjectiveId)).ToDictionary(g => g.Key, g => g.MaxBy(x => x.At).Level);
        var superseded = human.Where(g => g.Supersedes is not null).Select(g => g.Supersedes!.Value).ToHashSet();
        var humanLatest = human.Where(g => !superseded.Contains(g.Id)).GroupBy(g => (g.SessionId, g.ObjectiveId))
            .ToDictionary(g => g.Key, g => g.MaxBy(x => x.At).Level);
        var grades = aiLatest.Keys.Union(humanLatest.Keys).Where(k => byId.ContainsKey(k.SessionId)).Select(k =>
        {
            var s = byId[k.SessionId];
            return new ModeGrade(s.TypedAnswers, s.StudentUserId, k.ObjectiveId, titles.GetValueOrDefault(k.ObjectiveId, "Objective"),
                aiLatest.TryGetValue(k, out var p) ? p : null, humanLatest.TryGetValue(k, out var h) ? h : null);
        }).ToList();

        var minutes = turns.GroupBy(t => t.SessionId).ToDictionary(g => g.Key,
            g => g.Sum(t => Math.Clamp((t.Completed - (t.Started ?? t.AskedAt)).TotalSeconds, 0, 3600)) / 60);
        var noteCount = notes.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        var rows = minutes.Where(m => m.Value > 0 && byId.ContainsKey(m.Key)).Select(m =>
            new ModeSession(byId[m.Key].TypedAnswers, byId[m.Key].StudentUserId, m.Value, noteCount.GetValueOrDefault(m.Key))).ToList();
        return Compare(grades, rows);
    }

    /// <summary>The comparison over plain rows.</summary>
    public static TypedSpokenReport Compare(IReadOnlyList<ModeGrade> grades, IReadOnlyList<ModeSession> sessions)
    {
        var typed = Side(grades.Where(g => g.Typed), sessions.Where(s => s.Typed));
        var spoken = Side(grades.Where(g => !g.Typed), sessions.Where(s => !s.Typed));
        var objectives = grades.GroupBy(g => g.ObjectiveId).Select(g => new ObjectiveModeRow(g.First().Objective,
                Side(g.Where(x => x.Typed), []), Side(g.Where(x => !x.Typed), [])))
            .Where(r => r.Typed.Students > 0 && r.Spoken.Students > 0)
            .OrderBy(r => r.Objective, StringComparer.OrdinalIgnoreCase).ToList();
        var both = objectives.Where(r => !r.Typed.TooFew && !r.Spoken.TooFew).ToList();
        double? Gap(Func<ModeSide, double?> mean)
        {
            var d = both.Where(r => mean(r.Typed) is not null && mean(r.Spoken) is not null)
                .Select(r => (Diff: mean(r.Typed)!.Value - mean(r.Spoken)!.Value, Weight: r.Typed.Graded)).ToList();
            var weight = d.Sum(x => x.Weight);
            return weight == 0 ? null : d.Sum(x => x.Diff * x.Weight) / weight;
        }
        return new TypedSpokenReport(typed, spoken, objectives, Gap(s => s.MeanHuman), Gap(s => s.MeanPanel), Warnings(typed, spoken));
    }

    /// <summary>One side's figures; under five students only the counts are kept.</summary>
    public static ModeSide Side(IEnumerable<ModeGrade> grades, IEnumerable<ModeSession> sessions)
    {
        var g = grades.ToList();
        var s = sessions.ToList();
        var students = g.Select(x => x.Student).Concat(s.Select(x => x.Student)).Distinct().Count();
        if (students < GroupFairness.MinStudents) return ModeSide.Withheld with { Students = students };
        var humanLevels = g.Where(x => x.HumanLevel is >= 1 and <= 5).Select(x => (double)x.HumanLevel!.Value);
        var panelLevels = g.Where(x => x.PanelLevel is >= 1 and <= 5).Select(x => (double)x.PanelLevel!.Value);
        var pairs = g.Where(x => x.HumanLevel is >= 1 and <= 5 && x.PanelLevel is >= 1 and <= 5).ToList();
        var minutes = s.Sum(x => x.AnswerMinutes);
        return new ModeSide(students, s.Count, g.Count, GroupFairness.Mean(humanLevels), GroupFairness.Mean(panelLevels),
            pairs.Count == 0 ? null : pairs.Count(x => x.HumanLevel == x.PanelLevel) / (double)pairs.Count,
            pairs.Count == 0 ? null : pairs.Average(x => (double)Math.Abs(x.HumanLevel!.Value - x.PanelLevel!.Value)),
            minutes <= 0 ? null : s.Sum(x => x.Notes) / minutes * 10, s.Count == 0 ? null : s.Count(x => x.Notes > 0) / (double)s.Count);
    }

    public static IReadOnlyList<string> Warnings(ModeSide typed, ModeSide spoken)
    {
        var w = new List<string>();
        if (typed.TooFew || spoken.TooFew)
        {
            w.Add($"Typed and spoken answers can be compared once each side has {GroupFairness.MinStudents} or more students " +
                  $"(typed {typed.Students}, spoken {spoken.Students}).");
            return w;
        }
        if (typed.SameLevel is { } ts && spoken.SameLevel is { } ss && Math.Abs(ts - ss) >= AgreementGap)
            w.Add($"The panel and the human agree on {ts:0%} of typed answers' objectives against {ss:0%} of spoken ones. " +
                  "One of the two graders reads the typed answers differently; compare the evidence before relying on the panel for them.");
        if (typed.NotesPer10Min is { } tn && spoken.NotesPer10Min is { } sn && Math.Max(tn, sn) > 0
            && (Math.Min(tn, sn) <= 0 || Math.Max(tn, sn) / Math.Min(tn, sn) > DisparityRatio))
            w.Add($"Integrity notes come {(tn > sn ? "more" : "less")} often with typed answers ({tn:0.0} against {sn:0.0} per 10 answering minutes). " +
                  "The two channels raise different notes; read each kind before treating either group as riskier.");
        return w;
    }
}
