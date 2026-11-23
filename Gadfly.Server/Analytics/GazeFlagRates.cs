using Gadfly.Server.Data;
using Gadfly.Server.Integrity;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Analytics;

// Eye-contact flag rates by group (GAD-T261, docs/EVIDENCE_MAP.md mechanic 4). Webcam gaze is coarse (about 4 degrees), people
// look away for many reasons the camera cannot tell apart, and face and eye detection can fail more for some groups than others (research note 06). Before anyone
// relies on the voice room's look-away episodes or its screen fade, these rates are compared across groups: by the eye-contact
// accommodation, and by any grouping the institution supplies for the comparison (it is used in memory, never stored). Plain
// arithmetic over stored rows; no student is named and groups too small to report are withheld.

/// <summary>One group's eye-contact episode rates over its students' answering time.</summary>
/// <param name="AbsentFaces">Episodes with no face in view (the voice room's while answering, and the Strict camera check's).</param>
/// <param name="SecondFaces">Episodes with more than one face in view.</param>
/// <param name="ChangedFaces">Episodes where the face no longer resembled the one at the start (Strict camera check only).</param>
/// <param name="QualitySessions">Sessions whose episodes carried a tracking quality score (GAD-T305).</param>
/// <param name="MedianQuality">The median of those sessions' quality scores (0 to 100).</param>
/// <param name="PoorQualityShare">Share of those sessions whose quality was poor (below <see cref="GazeQuality.FairFrom"/>).</param>
/// <param name="CalibratedShare">Share of those sessions set up to the student's screen by the five-dot calibration.</param>
public sealed record GazeRateGroup(string Group, int Students, int Sessions, double AnswerMinutes, int LookAways, int Readings,
    int Fades, double FadeSeconds, int SessionsWithFade, int Remarks, int AbsentFaces = 0, int SecondFaces = 0, int ChangedFaces = 0,
    int QualitySessions = 0, double? MedianQuality = null, double? PoorQualityShare = null, double? CalibratedShare = null)
{
    public double? AbsentFacesPer10Min => AnswerMinutes <= 0 ? null : AbsentFaces / AnswerMinutes * 10;
    public double? SecondFacesPer10Min => AnswerMinutes <= 0 ? null : SecondFaces / AnswerMinutes * 10;
    public double? ChangedFacesPer10Min => AnswerMinutes <= 0 ? null : ChangedFaces / AnswerMinutes * 10;
    /// <summary>True when the group has too few students to report without risking identifying someone or reading noise.</summary>
    public bool TooFew => Students < GazeFlagRates.MinStudents;
    public double? LookAwaysPer10Min => AnswerMinutes <= 0 ? null : LookAways / AnswerMinutes * 10;
    public double? FadesPer10Min => AnswerMinutes <= 0 ? null : Fades / AnswerMinutes * 10;
    public double? RemarksPer10Min => AnswerMinutes <= 0 ? null : Remarks / AnswerMinutes * 10;
    /// <summary>Share of answering time the screen spent faded.</summary>
    public double? FadedShare => AnswerMinutes <= 0 ? null : Math.Min(1, FadeSeconds / (AnswerMinutes * 60));
    public double? SessionsWithFadeShare => Sessions == 0 ? null : SessionsWithFade / (double)Sessions;
}

/// <summary>The comparison: groups, how many students the supplied grouping could not place, and any warning to show.</summary>
public sealed record GazeRatesReport(IReadOnlyList<GazeRateGroup> Groups, int Sessions, int Unplaced, IReadOnlyList<string> Warnings)
{
    /// <summary>The total of <see cref="Sessions"/> is not shown while any group is withheld (GAD-T486).</summary>
    public bool TotalWithheld => GroupFairness.WithholdsTotal(Groups, g => g.TooFew);
}

public sealed class GazeFlagRates(GadflyDbContext db, Guid organizationId)
{
    /// <summary>A group is reported only with at least this many students.</summary>
    public const int MinStudents = 5;

    /// <summary>
    /// Groups whose rates differ by more than this ratio (highest over lowest) are flagged: the four-fifths rule, read the other way.
    /// </summary>
    public const double DisparityRatio = 1.25;

    /// <summary>Groups whose median tracking quality differs by at least this many points (of 100) are flagged (GAD-T447).</summary>
    public const double QualityGap = 10;

    public const string Caveat =
        "Until these rates have been compared across groups, read eye-contact episodes as context only, never as evidence on their own. " +
        "A webcam estimates head and eye direction to about 4 degrees, cannot tell reading notes from thinking, and face detection can fail more often for some people than others.";

    /// <summary>The label for students whose assignment declares the eye-contact accommodation, and for the rest.</summary>
    public const string Accommodated = "Eye-contact accommodation", NotAccommodated = "No accommodation";

    /// <summary>
    /// Camera episodes the face-detection evidence concerns most (GAD-T447; ElenchusAtScale Section 5.6.1: Yoder-Himes 2022,
    /// Buolamwini and Gebru 2018): no face, a second face, and a face that no longer resembles the one at the start.
    /// </summary>
    public const string FaceCaveat =
        "Face detection has failed more often for some people than others (darker skin in one proctoring product), so an absent, second " +
        "or changed face can be the camera failing rather than the student. The changed-face check runs only in Strict exams. Tracking " +
        "quality is the eye-tracking score sent with each episode (0 to 100); a group whose quality is lower gets more false look-aways.";

    /// <summary>Episode types counted as an absent face, a second face and a changed face.</summary>
    public static readonly IReadOnlySet<string> AbsentFaceTypes = new HashSet<string>(StringComparer.Ordinal) { "examiner-face-absent", "face-absent" },
        SecondFaceTypes = new HashSet<string>(StringComparer.Ordinal) { "examiner-second-face", "multiple-faces" },
        ChangedFaceTypes = new HashSet<string>(StringComparer.Ordinal) { "identity-drift" };

    /// <summary>The tracking-quality group of a session (GAD-T305 bands), for grouping the rates by quality instead of by student.</summary>
    public static string QualityGroup(GazeQuality? q) => q is null ? "No quality score (older page)"
        : q.Score >= GazeQuality.GoodFrom ? $"Tracking good ({GazeQuality.GoodFrom}+)"
        : q.Score >= GazeQuality.FairFrom ? $"Tracking fair ({GazeQuality.FairFrom}-{GazeQuality.GoodFrom - 1})"
        : $"Tracking poor (under {GazeQuality.FairFrom})";

    /// <summary>A session's tracking quality: the lowest score its episodes carried (the session's weakest stretch), or null.</summary>
    public static GazeQuality? SessionQuality(IEnumerable<GazeQuality?> episodes) =>
        episodes.Where(q => q is not null).MinBy(q => q!.Score);

    /// <summary>The median of <paramref name="values"/>, or null when empty.</summary>
    public static double? Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        if (v.Count == 0) return null;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    /// <summary>The analytics scope's assessments (published or archived; the same set the analytics page reports on).</summary>
    public async Task<IReadOnlyList<Guid>> ExamIdsAsync(AnalyticsScope scope, CancellationToken ct = default)
    {
        var q = db.Exams.AsNoTracking().Where(e => e.OrganizationId == organizationId && e.Status != ExamStatus.Draft);
        if (scope.ExamId is { } examId) q = q.Where(e => e.Id == examId);
        else if (scope.CourseId is { } courseId) q = q.Where(e => e.CourseId == courseId);
        return await q.Select(e => e.Id).ToListAsync(ct);
    }

    /// <summary>
    /// Rates for the sessions of the given exams (voice room episodes only, exams whose integrity level isn't Off), grouped by
    /// <paramref name="groupOf"/> (student id to group label; null leaves the student out as unplaced). Without a grouping the
    /// groups are the eye-contact accommodation's.
    /// </summary>
    public async Task<GazeRatesReport> BuildAsync(IReadOnlyCollection<Guid> examIds, Func<Guid, string?>? groupOf = null, CancellationToken ct = default) =>
        await BuildAsync(examIds, groupOf, byQuality: false, ct);

    /// <summary>
    /// As above; with <paramref name="byQuality"/> each session is grouped by its tracking quality band instead (GAD-T447), so the
    /// rates can be read against how well the camera could follow the eyes.
    /// </summary>
    public async Task<GazeRatesReport> BuildAsync(IReadOnlyCollection<Guid> examIds, Func<Guid, string?>? groupOf, bool byQuality, CancellationToken ct = default)
    {
        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId && examIds.Contains(e.Id) && e.IntegrityLevel != IntegrityLevel.Off)
            .Select(e => e.Id).ToListAsync(ct);
        var sessions = await (from s in db.ExamSessions.AsNoTracking()
                              join a in db.ExamAssignments.AsNoTracking() on s.AssignmentId equals a.Id
                              where s.OrganizationId == organizationId && a.OrganizationId == organizationId && exams.Contains(s.ExamId)
                              select new { s.Id, s.StudentUserId, a.GazeAccommodation }).ToListAsync(ct);
        var ids = sessions.Select(s => s.Id).ToList();
        var turns = await db.SessionTurns.AsNoTracking()
            .Where(t => t.OrganizationId == organizationId && ids.Contains(t.SessionId) && t.AnswerCompletedAt != null)
            .Select(t => new { t.SessionId, t.AskedAt, t.AnswerStartedAt, t.AnswerCompletedAt }).ToListAsync(ct);
        var answerSeconds = turns.GroupBy(t => t.SessionId).ToDictionary(g => g.Key,
            g => g.Sum(t => Math.Clamp((t.AnswerCompletedAt!.Value - (t.AnswerStartedAt ?? t.AskedAt)).TotalSeconds, 0, 3600)));
        var types = WebcamIntegrityRecorder.ExaminerTypes.Concat(AbsentFaceTypes).Concat(SecondFaceTypes).Concat(ChangedFaceTypes).Distinct().ToArray();
        var events = await db.IntegrityEvents.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId && ids.Contains(e.SessionId) && e.Source == WebcamIntegrityRecorder.Source
                && types.Contains(e.Type))
            .Select(e => new { e.SessionId, e.Type, e.StartedAt, e.EndedAt, e.Summary, e.DataJson }).ToListAsync(ct);
        var bySession = events.GroupBy(e => e.SessionId).ToDictionary(g => g.Key, g => g.ToList());

        // Only sessions with answering time: a session never answered says nothing about rates.
        var rows = sessions.Where(s => answerSeconds.GetValueOrDefault(s.Id) > 0).Select(s =>
        {
            var ev = bySession.GetValueOrDefault(s.Id) ?? [];
            var fades = ev.Where(e => e.Type == "examiner-fade").ToList();
            var quality = SessionQuality(ev.Where(e => WebcamIntegrityRecorder.ExaminerTypes.Contains(e.Type)).Select(e => GazeQuality.FromJson(e.DataJson)));
            return new
            {
                s.StudentUserId,
                Group = byQuality ? QualityGroup(quality)
                    : groupOf is null ? (s.GazeAccommodation ? Accommodated : NotAccommodated) : Clean(groupOf(s.StudentUserId)),
                Minutes = answerSeconds[s.Id] / 60,
                LookAways = ev.Count(e => e.Type == "examiner-look-away"),
                Readings = ev.Count(e => e.Type == "examiner-reading"),
                Fades = fades.Count,
                FadeSeconds = fades.Sum(e => e.EndedAt is { } end ? Math.Max(0, (end - e.StartedAt).TotalSeconds) : 0),
                Remarks = ev.Count(e => WebcamIntegrityRecorder.SaysExaminerSpoke(e.Summary)),
                AbsentFaces = ev.Count(e => AbsentFaceTypes.Contains(e.Type)),
                SecondFaces = ev.Count(e => SecondFaceTypes.Contains(e.Type)),
                ChangedFaces = ev.Count(e => ChangedFaceTypes.Contains(e.Type)),
                Quality = quality,
            };
        }).ToList();

        var unplaced = rows.Where(r => r.Group is null).Select(r => r.StudentUserId).Distinct().Count();
        var groups = rows.Where(r => r.Group is not null).GroupBy(r => r.Group!, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var q = g.Where(r => r.Quality is not null).Select(r => r.Quality!).ToList();
                return new GazeRateGroup(g.First().Group!, g.Select(r => r.StudentUserId).Distinct().Count(), g.Count(),
                    Math.Round(g.Sum(r => r.Minutes), 1), g.Sum(r => r.LookAways), g.Sum(r => r.Readings), g.Sum(r => r.Fades),
                    Math.Round(g.Sum(r => r.FadeSeconds), 1), g.Count(r => r.Fades > 0), g.Sum(r => r.Remarks),
                    g.Sum(r => r.AbsentFaces), g.Sum(r => r.SecondFaces), g.Sum(r => r.ChangedFaces), q.Count,
                    Median(q.Select(x => (double)x.Score)), q.Count == 0 ? null : q.Count(x => x.Poor) / (double)q.Count,
                    q.Count == 0 ? null : q.Count(x => x.Calibrated) / (double)q.Count);
            })
            .OrderBy(g => g.Group, StringComparer.OrdinalIgnoreCase).ToList();
        // Withheld groups keep only their name and size.
        groups = groups.Select(g => g.TooFew ? new GazeRateGroup(g.Group, g.Students, 0, 0, 0, 0, 0, 0, 0, 0) : g).ToList();
        return new GazeRatesReport(groups, rows.Count, unplaced, Warnings(groups));
    }

    /// <summary>The warnings for a set of groups: too little to compare, or rates that differ by more than <see cref="DisparityRatio"/>.</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<GazeRateGroup> groups)
    {
        var usable = groups.Where(g => !g.TooFew && g.AnswerMinutes > 0).ToList();
        var w = new List<string>();
        if (usable.Count < 2)
        {
            w.Add($"Fewer than two groups have {MinStudents} or more students with answering time, so the rates can't be compared yet.");
            return w;
        }
        void Check(string what, Func<GazeRateGroup, double?> rate)
        {
            var rates = usable.Select(g => (g.Group, Rate: rate(g) ?? 0)).ToList();
            var hi = rates.MaxBy(r => r.Rate);
            var lo = rates.MinBy(r => r.Rate);
            if (hi.Rate <= 0) return;
            if (lo.Rate <= 0 || hi.Rate / lo.Rate > DisparityRatio)
                w.Add($"{what} differ between groups: {hi.Group} {hi.Rate:0.0} against {lo.Group} {lo.Rate:0.0} per 10 answering minutes. " +
                      "Don't rely on these episodes for either group until the difference is understood.");
        }
        Check("Look-away episodes", g => g.LookAwaysPer10Min);
        Check("Screen fades", g => g.FadesPer10Min);
        Check("Absent-face episodes", g => g.AbsentFacesPer10Min);
        Check("Second-face episodes", g => g.SecondFacesPer10Min);
        Check("Changed-face episodes", g => g.ChangedFacesPer10Min);
        // Tracking quality (GAD-T447): a group the camera follows worse gets more false look-aways.
        var rated = usable.Where(g => g.MedianQuality is not null && g.QualitySessions > 0).ToList();
        if (rated.Count >= 2)
        {
            var best = rated.MaxBy(g => g.MedianQuality!.Value)!;
            var worst = rated.MinBy(g => g.MedianQuality!.Value)!;
            if (best.MedianQuality!.Value - worst.MedianQuality!.Value >= QualityGap)
                w.Add($"Eye-tracking quality differs between groups: median {worst.MedianQuality:0} for {worst.Group} against {best.MedianQuality:0} for " +
                      $"{best.Group} (of 100). The camera follows {worst.Group} less well, so their look-away notes are weaker evidence still.");
        }
        return w;
    }

    /// <summary>
    /// Reads a grouping the institution pastes in, one "email or user name, group" per line (comma, tab or semicolon), against the
    /// organization's members. Returns student id to group; lines that match no member are counted, never echoed.
    /// </summary>
    public static (Dictionary<Guid, string> Map, int Unmatched) ParseGrouping(string? text,
        IEnumerable<(Guid UserId, string UserName, string? Email)> members)
    {
        var byKey = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in members)
        {
            if (!string.IsNullOrWhiteSpace(m.Email)) byKey.TryAdd(m.Email.Trim(), m.UserId);
            if (!string.IsNullOrWhiteSpace(m.UserName)) byKey.TryAdd(m.UserName.Trim(), m.UserId);
        }
        var map = new Dictionary<Guid, string>();
        var unmatched = 0;
        foreach (var raw in (text ?? "").Split('\n').Take(20000))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var cut = line.IndexOfAny([',', '\t', ';']);
            if (cut <= 0) { unmatched++; continue; }
            var who = line[..cut].Trim().Trim('"');
            var group = Clean(line[(cut + 1)..].Trim().Trim('"'));
            if (group is null || !byKey.TryGetValue(who, out var id)) { unmatched++; continue; }
            map[id] = group;
        }
        return (map, unmatched);
    }

    private static string? Clean(string? group)
    {
        if (string.IsNullOrWhiteSpace(group)) return null;
        var g = new string(group.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return g.Length == 0 ? null : g.Length > 60 ? g[..60] : g;
    }
}
