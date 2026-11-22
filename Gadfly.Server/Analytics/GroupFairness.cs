using Gadfly.Assessment;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Analytics;

// Shared footing for the fairness-by-group views (GAD-T433, T434, T438, T448; the blueprint audit GAD-T421, ElenchusAtScale
// Sections 5.3 to 5.7). Every view groups the examination sessions of one analytics scope either by the accommodations the
// assignment declared or by a grouping the institution pastes in for the comparison (used in memory, never stored), withholds
// groups with fewer than MinStudents students, and warns when a gap passes the view's stated threshold. Only Owners and Admins
// see them: they are the roles that never grade blind, so no grader who is still blind sees a panel level or an integrity note
// through an aggregate.

/// <summary>One examination session as the fairness views see it: who sat it, for which assessment, under which accommodations.</summary>
public sealed record FairSession(Guid Id, Guid StudentUserId, Guid ExamId, bool TypedAnswers, bool GazeAccommodation, decimal TimeMultiplier)
{
    /// <summary>The session's accommodation group (<see cref="GroupFairness.AccommodationLabel"/>).</summary>
    public string Accommodation => GroupFairness.AccommodationLabel(TypedAnswers, GazeAccommodation, TimeMultiplier);
}

public static class GroupFairness
{
    /// <summary>A group is reported only with at least this many distinct students (small numbers mislead and can identify).</summary>
    public const int MinStudents = 5;

    public const string NoAccommodation = "No accommodation", Typed = "Typed answers", EyeContact = "Eye-contact accommodation",
        ExtraTime = "Extra time";

    /// <summary>
    /// The fairness views are for Owners and Admins only. Instructors, examiners and second reviewers can enter human scores, so
    /// they may be grading blind (<see cref="Grading.BlindGrading.AppliesToRole"/>), and an aggregate of panel levels or integrity
    /// notes over a small assessment would show them what blind grading hides.
    /// </summary>
    public static bool ViewerAllowed(string? role) => role is AssessmentRoles.Owner or AssessmentRoles.InstitutionAdmin;

    /// <summary>The accommodations an assignment declared, as one group label ("Typed answers + Extra time"), or "No accommodation".</summary>
    public static string AccommodationLabel(bool typed, bool gaze, decimal timeMultiplier)
    {
        var parts = new List<string>(3);
        if (typed) parts.Add(Typed);
        if (gaze) parts.Add(EyeContact);
        if (timeMultiplier > 1m) parts.Add(ExtraTime);
        return parts.Count == 0 ? NoAccommodation : string.Join(" + ", parts);
    }

    /// <summary>The analytics scope's assessments (published or archived, as the analytics page reports on).</summary>
    public static async Task<IReadOnlyList<Guid>> ExamIdsAsync(GadflyDbContext db, Guid organizationId, AnalyticsScope scope, CancellationToken ct = default)
    {
        var q = db.Exams.AsNoTracking().Where(e => e.OrganizationId == organizationId && e.Status != ExamStatus.Draft);
        if (scope.ExamId is { } examId) q = q.Where(e => e.Id == examId);
        else if (scope.CourseId is { } courseId) q = q.Where(e => e.CourseId == courseId);
        return await q.Select(e => e.Id).ToListAsync(ct);
    }

    /// <summary>The sessions of <paramref name="examIds"/> with their assignment's accommodations, tenant-filtered.</summary>
    public static async Task<IReadOnlyList<FairSession>> SessionsAsync(GadflyDbContext db, Guid organizationId, IReadOnlyCollection<Guid> examIds,
        CancellationToken ct = default) =>
        await (from s in db.ExamSessions.AsNoTracking()
               join a in db.ExamAssignments.AsNoTracking() on s.AssignmentId equals a.Id
               where s.OrganizationId == organizationId && a.OrganizationId == organizationId && examIds.Contains(s.ExamId)
               select new FairSession(s.Id, s.StudentUserId, s.ExamId, a.TypedAnswerAccommodation, a.GazeAccommodation, a.TimeMultiplier))
            .ToListAsync(ct);

    /// <summary>
    /// The group of a session: by accommodation without a pasted grouping, else the pasted group of its student (null: unplaced).
    /// </summary>
    public static Func<FairSession, string?> Grouper(IReadOnlyDictionary<Guid, string>? pasted) =>
        pasted is null ? s => s.Accommodation : s => Clean(pasted.GetValueOrDefault(s.StudentUserId));

    /// <summary>
    /// Reads a grouping pasted in for the comparison, one "email or user name, group" per line (comma, tab or semicolon), against
    /// the organization's members. Returns student id to group; lines that match no member are counted, never echoed.
    /// </summary>
    public static (Dictionary<Guid, string> Map, int Unmatched) ParseGrouping(string? text, IEnumerable<(Guid UserId, string UserName, string? Email)> members) =>
        GazeFlagRates.ParseGrouping(text, members);

    /// <summary>A group label without control characters, at most 60 characters; null when empty.</summary>
    public static string? Clean(string? group)
    {
        if (string.IsNullOrWhiteSpace(group)) return null;
        var g = new string(group.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return g.Length == 0 ? null : g.Length > 60 ? g[..60] : g;
    }

    /// <summary>The mean, or null for no values.</summary>
    public static double? Mean(IEnumerable<double> values)
    {
        var v = values as IReadOnlyCollection<double> ?? values.ToList();
        return v.Count == 0 ? null : v.Average();
    }

    /// <summary>The sample variance (n - 1), or null for fewer than two values.</summary>
    public static double? Variance(IEnumerable<double> values)
    {
        var v = values as IReadOnlyCollection<double> ?? values.ToList();
        if (v.Count < 2) return null;
        var m = v.Average();
        return v.Sum(x => (x - m) * (x - m)) / (v.Count - 1);
    }

    /// <summary>
    /// A mean with an approximate 95% interval (mean ± 1.96 standard errors); null for fewer than two values. An approximation for
    /// reading the size of a difference, not a test.
    /// </summary>
    public static MeanInterval? Interval(IEnumerable<double> values)
    {
        var v = values as IReadOnlyCollection<double> ?? values.ToList();
        if (v.Count < 2) return null;
        var m = v.Average();
        var half = 1.96 * Math.Sqrt(Variance(v)!.Value / v.Count);
        return new MeanInterval(m, m - half, m + half, v.Count);
    }

    /// <summary>
    /// A view's total of pairs, answers or sessions, as shown beside its groups (GAD-T486): withheld whenever any group is withheld,
    /// since the total minus the groups shown would give that group's count away (exactly, with two groups or one student pasted
    /// as a group of their own). The same rule as the fade cost's overall row (GAD-T480).
    /// </summary>
    public static string Total(int total, bool withheld, string noun) =>
        withheld ? $"{noun}: total withheld (a group is too small to show)" : $"{total} {noun}";

    /// <summary>True when a total shown beside these groups must be withheld (GAD-T486): any group is withheld.</summary>
    public static bool WithholdsTotal<T>(IEnumerable<T> groups, Func<T, bool> withheld) => groups.Any(withheld);

    /// <summary>The order groups are listed in: "No accommodation" first, then by name.</summary>
    public static IOrderedEnumerable<T> Ordered<T>(IEnumerable<T> groups, Func<T, string> name) =>
        groups.OrderBy(g => name(g) == NoAccommodation ? 0 : 1).ThenBy(name, StringComparer.OrdinalIgnoreCase);
}

/// <summary>A mean with an approximate 95% interval over <see cref="N"/> values.</summary>
public sealed record MeanInterval(double Mean, double Low, double High, int N)
{
    /// <summary>True when the interval lies wholly above or below zero.</summary>
    public bool ExcludesZero => Low > 0 || High < 0;
}
