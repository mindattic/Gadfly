using Gadfly.Server.Data;
using Gadfly.Server.Integrity;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Analytics;

// What the gaze fade costs (GAD-T448; ElenchusAtScale Section 5.7.3 "What would test it"; docs/EVIDENCE_MAP.md mechanic 4 and
// owner decision (c)). The fade acts on the student during the answer, so it could change the evidence even though the gaze
// record never enters the score. This compares, within each student, the judged quality (the exam-time panel's overall score,
// 0 to 1) and the length of the answers during which the screen faded with that student's other answers, and the answer right
// after a faded one with the student's clean answers. Within-student differences cancel out who the student is; they do not
// cancel out the question (a hard question may both draw a look away and get a weaker answer), so a difference is a reason to
// look, not proof of a cost. Measurement only: the fade itself is the owner's decision (c) and is not changed here.

/// <summary>One answered question: whether the screen faded during it, whether the one before it faded, its judged score and length.</summary>
public sealed record FadeAnswer(string Group, Guid Student, bool Faded, bool AfterFade, double? Score, double Seconds);

/// <summary>One group's within-student differences (faded minus the same students' other answers).</summary>
/// <param name="Compared">Students with at least one judged faded answer and one judged answer without a fade.</param>
public sealed record FadeCostGroup(string Group, int Students, int Compared, int FadedAnswers, MeanInterval? ScoreDiff, MeanInterval? SecondsDiff,
    int ComparedAfter, MeanInterval? AfterScoreDiff)
{
    public bool TooFew => Compared < GroupFairness.MinStudents;
}

public sealed record FadeCostReport(FadeCostGroup Overall, IReadOnlyList<FadeCostGroup> Groups, int Answers, IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// The all-students row's count of faded answers is not shown while any group is withheld (GAD-T486): with the other groups
    /// shown it would give that group's count away, as its figures would (GAD-T480).
    /// </summary>
    public bool OverallCountWithheld => GroupFairness.WithholdsTotal(Groups, g => g.TooFew);
}

public sealed class FadeCost(GadflyDbContext db, Guid organizationId)
{
    /// <summary>A judged-score difference this large (on the 0 to 1 score), with an interval clear of zero, is warned.</summary>
    public const double ScoreThreshold = 0.05;

    public const string Caveat =
        "Each figure compares a student's answers during which the screen faded with the same student's other answers, so who the " +
        "student is cancels out; the question does not (a hard question can both draw a look away and get a weaker answer). A lower " +
        "score while faded is a reason to look at the fade, not proof that it costs anything. Scores are the exam-time panel's overall " +
        "reading of each answer (0 to 1). Intervals are approximate 95% ranges. The fade itself is unchanged.";

    public async Task<FadeCostReport> BuildAsync(IReadOnlyList<FairSession> sessions, Func<FairSession, string?> groupOf, CancellationToken ct = default)
    {
        var placed = sessions.Select(s => (Session: s, Group: groupOf(s))).Where(x => x.Group is not null).ToDictionary(x => x.Session.Id, x => x);
        var answers = new List<FadeAnswer>();
        foreach (var chunk in placed.Keys.Chunk(300))
        {
            var turns = await db.SessionTurns.AsNoTracking()
                .Where(t => t.OrganizationId == organizationId && chunk.Contains(t.SessionId) && t.AnswerCompletedAt != null)
                .Select(t => new { t.Id, t.SessionId, t.Index, t.AskedAt, t.AnswerStartedAt, t.AnswerCompletedAt }).ToListAsync(ct);
            var turnIds = turns.Select(t => t.Id).ToList();
            var scores = new Dictionary<Guid, double>();
            foreach (var tc in turnIds.Chunk(1000))
                foreach (var j in (await db.Judgments.AsNoTracking()
                             .Where(j => j.OrganizationId == organizationId && tc.Contains(j.TurnId))
                             .Select(j => new { j.TurnId, j.OverallScore, j.CreatedAt }).ToListAsync(ct))
                         .GroupBy(j => j.TurnId))
                    scores[j.Key] = j.MaxBy(x => x.CreatedAt)!.OverallScore;
            var fades = await db.IntegrityEvents.AsNoTracking()
                .Where(e => e.OrganizationId == organizationId && chunk.Contains(e.SessionId) && e.Source == WebcamIntegrityRecorder.Source
                    && e.Type == "examiner-fade")
                .Select(e => new { e.SessionId, e.TurnId, e.StartedAt }).ToListAsync(ct);
            var fadesBySession = fades.GroupBy(f => f.SessionId).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var session in turns.GroupBy(t => t.SessionId))
            {
                var (fair, group) = placed[session.Key];
                var sessionFades = fadesBySession.GetValueOrDefault(session.Key) ?? [];
                var previousFaded = false;
                foreach (var t in session.OrderBy(t => t.Index))
                {
                    var start = t.AnswerStartedAt is { } st && st < t.AnswerCompletedAt!.Value ? st : t.AskedAt;
                    var faded = sessionFades.Any(f => f.TurnId == t.Id || (f.TurnId is null && f.StartedAt >= start && f.StartedAt <= t.AnswerCompletedAt!.Value));
                    var seconds = Math.Clamp((t.AnswerCompletedAt!.Value - start).TotalSeconds, 0, 3600);
                    answers.Add(new FadeAnswer(group!, fair.StudentUserId, faded, previousFaded && !faded,
                        scores.TryGetValue(t.Id, out var sc) ? sc : null, seconds));
                    previousFaded = faded;
                }
            }
        }
        return Compare(answers);
    }

    /// <summary>The comparison over plain rows: overall and per group.</summary>
    public static FadeCostReport Compare(IReadOnlyList<FadeAnswer> answers)
    {
        var overall = Summarize("All students", answers);
        var groups = GroupFairness.Ordered(answers.GroupBy(a => a.Group, StringComparer.OrdinalIgnoreCase)
            .Select(g => Summarize(g.First().Group, g.ToList())), g => g.Group).ToList();
        // GAD-T480: a withheld group's figure must not come back as the overall row minus the shown groups (with two groups, or one
        // student pasted as a group of their own, that is exact). When any group with compared students is withheld, so is the
        // overall figure it is part of; the counts stay.
        if (groups.Count > 1)
        {
            if (groups.Any(g => g.ScoreDiff is null && g.Compared > 0)) overall = overall with { ScoreDiff = null, SecondsDiff = null };
            if (groups.Any(g => g.AfterScoreDiff is null && g.ComparedAfter > 0)) overall = overall with { AfterScoreDiff = null };
        }
        return new FadeCostReport(overall, groups, answers.Count, Warnings(overall, groups));
    }

    /// <summary>One set of answers' within-student differences; under five compared students only the counts are kept.</summary>
    public static FadeCostGroup Summarize(string group, IReadOnlyList<FadeAnswer> answers)
    {
        var scoreDiffs = new List<double>();
        var secondDiffs = new List<double>();
        var afterDiffs = new List<double>();
        foreach (var student in answers.GroupBy(a => a.Student))
        {
            var judged = student.Where(a => a.Score is not null).ToList();
            var faded = judged.Where(a => a.Faded).ToList();
            var other = judged.Where(a => !a.Faded).ToList();
            if (faded.Count > 0 && other.Count > 0)
            {
                scoreDiffs.Add(faded.Average(a => a.Score!.Value) - other.Average(a => a.Score!.Value));
                secondDiffs.Add(faded.Average(a => a.Seconds) - other.Average(a => a.Seconds));
            }
            var after = judged.Where(a => a.AfterFade).ToList();
            var clean = judged.Where(a => !a.Faded && !a.AfterFade).ToList();
            if (after.Count > 0 && clean.Count > 0)
                afterDiffs.Add(after.Average(a => a.Score!.Value) - clean.Average(a => a.Score!.Value));
        }
        var students = answers.Select(a => a.Student).Distinct().Count();
        var fadedAnswers = answers.Count(a => a.Faded);
        if (scoreDiffs.Count < GroupFairness.MinStudents)
            return new FadeCostGroup(group, students, scoreDiffs.Count, fadedAnswers, null, null, afterDiffs.Count, null);
        return new FadeCostGroup(group, students, scoreDiffs.Count, fadedAnswers, GroupFairness.Interval(scoreDiffs),
            GroupFairness.Interval(secondDiffs), afterDiffs.Count, afterDiffs.Count >= GroupFairness.MinStudents ? GroupFairness.Interval(afterDiffs) : null);
    }

    public static IReadOnlyList<string> Warnings(FadeCostGroup overall, IReadOnlyList<FadeCostGroup> groups)
    {
        var w = new List<string>();
        if (overall.TooFew)
        {
            w.Add($"Fewer than {GroupFairness.MinStudents} students have both a faded and an unfaded judged answer, so the fade's cost can't be estimated yet.");
            return w;
        }
        if (overall.ScoreDiff is null)
            w.Add($"The all-students figures are withheld because a group has fewer than {GroupFairness.MinStudents} students compared: " +
                  "with the other groups shown, they would give that group's figures away.");
        if (overall.ScoreDiff is { } d && d.ExcludesZero && d.Mean <= -ScoreThreshold)
            w.Add($"Answers during which the screen faded were judged {-d.Mean:0.00} lower (of 1) than the same students' other answers " +
                  $"(roughly {-d.High:0.00} to {-d.Low:0.00}). This may be what the fade costs, or harder questions drawing more looking away; " +
                  "look at faded answers before relying on them.");
        if (overall.AfterScoreDiff is { } a && a.ExcludesZero && a.Mean <= -ScoreThreshold)
            w.Add($"The answer right after a faded one was judged {-a.Mean:0.00} lower than the same students' clean answers. The fade may carry over into the next answer.");
        var usable = groups.Where(g => !g.TooFew && g.ScoreDiff is not null).ToList();
        if (usable.Count >= 2)
        {
            var hi = usable.MaxBy(g => g.ScoreDiff!.Mean)!;
            var lo = usable.MinBy(g => g.ScoreDiff!.Mean)!;
            if (hi.ScoreDiff!.Mean - lo.ScoreDiff!.Mean >= ScoreThreshold)
                w.Add($"The fade's apparent cost differs between groups: {lo.Group} {ScoreGaps.Signed(lo.ScoreDiff.Mean)} against {hi.Group} " +
                      $"{ScoreGaps.Signed(hi.ScoreDiff.Mean)} on the judged score. If it holds, the fade weighs more on one group.");
        }
        return w;
    }
}
