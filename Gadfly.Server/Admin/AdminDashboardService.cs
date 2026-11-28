using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Admin;

/// <summary>What kind of thing needs attention on the dashboard (GAD-T300).</summary>
public enum AttentionKind { InProgress, AwaitingGrading, Reconciliation, Approval, Appeal, NotStartedNearDue, FailedJob }

/// <summary>One thing to act on, with the address of the page where it is acted on.</summary>
/// <param name="Href">Relative to the app root, no leading slash.</param>
public sealed record AttentionItem(AttentionKind Kind, string Title, string Detail, string Href, DateTimeOffset? At);

/// <summary>One "needs attention" group: how many there are, the first few, and where the whole list lives.</summary>
public sealed record AttentionGroup(AttentionKind Kind, string Heading, int Count, IReadOnlyList<AttentionItem> Items, string ListHref, string ListLabel);

/// <summary>Key numbers over one window (the last 7 or 30 days).</summary>
/// <param name="ExamsTaken">Interviews that ended in the window.</param>
/// <param name="Started">Interviews started in the window and no longer in progress (the completion rate's denominator).</param>
/// <param name="Completed">Of those, how many reached a proper end (not abandoned, not failed).</param>
/// <param name="MedianDuration">Start to end, over the interviews that ended in the window; null when none did.</param>
/// <param name="MedianTurnaround">Interview end to the grade's final approval, over grades approved in the window; null when none were.</param>
public sealed record DashboardWindow(int Days, int ExamsTaken, int Started, int Completed, TimeSpan? MedianDuration, int GradesApproved,
    TimeSpan? MedianTurnaround)
{
    /// <summary>Completed ÷ started, 0..1; null when nothing started.</summary>
    public double? CompletionRate => Started == 0 ? null : (double)Completed / Started;
}

/// <summary>A getting-started step for a new organization.</summary>
public sealed record SetupStep(string Key, string Title, string Hint, string Href, bool Done);

/// <summary>Everything the admin dashboard shows.</summary>
public sealed record AdminDashboard(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AttentionGroup> Attention,
    DashboardWindow Last7,
    DashboardWindow Last30,
    IReadOnlyList<SetupStep> Setup,
    int Exams,
    int PublishedExams,
    int Students,
    int Assignments)
{
    /// <summary>Total things waiting across every attention group.</summary>
    public int AttentionTotal => Attention.Sum(g => g.Count);

    /// <summary>True until every getting-started step is done: the dashboard then leads with the checklist.</summary>
    public bool IsNew => Setup.Any(s => !s.Done);
}

/// <summary>
/// The admin dashboard's data (GAD-T300). Every query names the caller's organization explicitly on top of the context's tenant
/// filter (the job table has no filter at all), aggregates in SQL, and fetches the names for the few rows it shows in one query,
/// so the cost does not grow with the number of rows (no per-row queries). Read-only: nothing here writes or audits.
/// </summary>
public sealed class AdminDashboardService(GadflyDbContext db, ITenantContext tenant, TimeProvider clock)
{
    /// <summary>How many rows each attention group shows before "see all".</summary>
    public const int ItemsPerGroup = 5;

    /// <summary>An assignment closing within this many days, not started yet, is flagged.</summary>
    public const int NearDueDays = 3;

    /// <summary>Failed background jobs from this many days back are flagged.</summary>
    public const int FailedJobDays = 7;

    /// <summary>Medians are taken over at most this many of the most recent rows, so a large organization stays fast.</summary>
    private const int MedianSample = 5000;

    private static readonly SessionState[] Live = [SessionState.Created, SessionState.ConsentGiven, SessionState.Running, SessionState.Paused];

    private static readonly GradeStatus[] Ungraded =
        [GradeStatus.ExaminationCompleted, GradeStatus.TranscriptVerificationPending, GradeStatus.AiGradingPending, GradeStatus.HumanReviewPending];

    private static readonly string[] NotCompleted = [nameof(Gadfly.Engine.EndReason.Abandoned), nameof(Gadfly.Engine.EndReason.Failed)];
    private static readonly string NotAnAttempt = nameof(Gadfly.Engine.EndReason.PromptSetChanged);

    private Guid Org => tenant.OrganizationId ?? throw new InvalidOperationException("The dashboard needs an active organization.");

    public async Task<AdminDashboard> LoadAsync(CancellationToken ct = default)
    {
        if (!AdminArea.IsOrgAdmin(tenant.Role) && !tenant.IsSystem)
            throw new UnauthorizedAccessException("The dashboard is for the organization's owners and admins.");
        var org = Org;
        var now = clock.GetUtcNow();

        // A DbContext runs one query at a time, so these run in sequence; each is a single aggregate or a short, capped list.
        var attention = new List<AttentionGroup>
        {
            await InProgressAsync(org, now, ct),
            await AwaitingGradingAsync(org, ct),
            await ReconciliationsAsync(org, ct),
            await ApprovalsAsync(org, ct),
            await AppealsAsync(org, ct),
            await NotStartedNearDueAsync(org, now, ct),
            await FailedJobsAsync(org, now, ct),
        };
        await NameStudentsAsync(attention, ct);

        var last7 = await WindowAsync(org, now, 7, ct);
        var last30 = await WindowAsync(org, now, 30, ct);

        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org)
            .GroupBy(_ => 1).Select(g => new { All = g.Count(), Published = g.Count(e => e.Status == ExamStatus.Published) }).FirstOrDefaultAsync(ct);
        var students = await db.OrganizationMembers.AsNoTracking()
            .CountAsync(m => m.OrganizationId == org && m.Role == OrgRoles.Student && m.IsActive, ct);
        var assignments = await db.ExamAssignments.AsNoTracking().CountAsync(a => a.OrganizationId == org, ct);
        var anyEnded = await db.ExamSessions.AsNoTracking().AnyAsync(s => s.OrganizationId == org && s.State == SessionState.Ended, ct);

        var setup = Setup(exams?.Published ?? 0, students, anyEnded);
        return new AdminDashboard(now, attention, last7, last30, setup, exams?.All ?? 0, exams?.Published ?? 0, students, assignments);
    }

    /// <summary>
    /// The getting-started checklist, in the order a new organization does it. Every published exam is open to every student
    /// (GAD-T845), so there is no assigning step.
    /// </summary>
    public static IReadOnlyList<SetupStep> Setup(int publishedExams, int students, bool anyInterviewEnded) =>
    [
        new("exam", "Publish an exam", "Paste or upload the notes the examiner should draw on, then publish it. Every student can then take it.", "admin/exams", publishedExams > 0),
        new("students", "Add students", "Give each student a user name and password, or invite them by email.", "admin/students", students > 0),
        new("watch", "Watch a practice interview",
            "Add yourself a test student, sign in as them and take the practice interview from My exams, then the exam you published. You can follow it live here.",
            "admin/live", anyInterviewEnded),
    ];

    // ------------------------------------------------------------------ attention

    private async Task<AttentionGroup> InProgressAsync(Guid org, DateTimeOffset now, CancellationToken ct)
    {
        var q = db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org && Live.Contains(s.State) && s.StartedAt != null);
        var count = await q.CountAsync(ct);
        var rows = count == 0 ? [] : await (from s in q
                                             join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
                                             orderby s.StartedAt
                                             select new { s.Id, s.StudentUserId, e.Title, s.StartedAt, s.State }).Take(ItemsPerGroup).ToListAsync(ct);
        var items = rows.Select(r => new AttentionItem(AttentionKind.InProgress, Student(r.StudentUserId),
            $"{r.Title} · {(r.State == SessionState.Paused ? "paused" : "started")} {Ago(now - r.StartedAt!.Value)} ago", "admin/live", r.StartedAt)).ToList();
        return new(AttentionKind.InProgress, "Interviews in progress", count, items, "admin/live", "Watch live sessions");
    }

    private async Task<AttentionGroup> AwaitingGradingAsync(Guid org, CancellationToken ct)
    {
        // Ended attempts with no grade record yet, plus grade records still before the human's review is done.
        var ended = db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org && s.State == SessionState.Ended
            && (s.EndReason == null || s.EndReason != NotAnAttempt));
        var waiting = from s in ended
                      join g in db.FinalGrades.AsNoTracking().Where(g => g.OrganizationId == org) on s.Id equals g.SessionId into gj
                      from g in gj.DefaultIfEmpty()
                      where g == null || Ungraded.Contains(g.Status)
                      select new { s.Id, s.StudentUserId, s.ExamId, s.EndedAt };
        var count = await waiting.CountAsync(ct);
        var rows = count == 0 ? [] : await (from w in waiting
                                             join e in db.Exams.AsNoTracking() on w.ExamId equals e.Id
                                             orderby w.EndedAt
                                             select new { w.Id, w.StudentUserId, e.Title, w.EndedAt }).Take(ItemsPerGroup).ToListAsync(ct);
        var items = rows.Select(r => new AttentionItem(AttentionKind.AwaitingGrading, Student(r.StudentUserId),
            $"{r.Title} · ended {When(r.EndedAt)}", $"grading/{r.Id}", r.EndedAt)).ToList();
        return new(AttentionKind.AwaitingGrading, "Attempts waiting to be graded", count, items, "grading", "Open the grading queue");
    }

    private async Task<AttentionGroup> ReconciliationsAsync(Guid org, CancellationToken ct)
    {
        var open = db.GradeDisagreements.AsNoTracking()
            .Where(d => d.OrganizationId == org && d.ResolvedAt == null && d.Band == DisagreementBand.MandatoryReconciliation);
        var perSession = open.GroupBy(d => d.SessionId).Select(g => new { SessionId = g.Key, Objectives = g.Count(), Since = g.Min(d => d.DetectedAt) });
        var count = await perSession.CountAsync(ct);
        var rows = count == 0 ? [] : await (from p in perSession
                                             join s in db.ExamSessions.AsNoTracking() on p.SessionId equals s.Id
                                             join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
                                             orderby p.Since
                                             select new { p.SessionId, s.StudentUserId, e.Title, p.Objectives, p.Since }).Take(ItemsPerGroup).ToListAsync(ct);
        var items = rows.Select(r => new AttentionItem(AttentionKind.Reconciliation, Student(r.StudentUserId),
            $"{r.Title} · {r.Objectives} objective{(r.Objectives == 1 ? "" : "s")} where the AI and the grader disagree", $"grading/{r.SessionId}", r.Since)).ToList();
        return new(AttentionKind.Reconciliation, "Grades to reconcile", count, items, "grading", "Open the grading queue");
    }

    private async Task<AttentionGroup> ApprovalsAsync(Guid org, CancellationToken ct)
    {
        var q = db.FinalGrades.AsNoTracking().Where(g => g.OrganizationId == org && g.Status == GradeStatus.AwaitingFinalApproval);
        var count = await q.CountAsync(ct);
        var rows = count == 0 ? [] : await (from g in q
                                             join s in db.ExamSessions.AsNoTracking() on g.SessionId equals s.Id
                                             join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
                                             orderby g.UpdatedAt
                                             select new { g.SessionId, s.StudentUserId, e.Title, g.FinalPercent, g.UpdatedAt }).Take(ItemsPerGroup).ToListAsync(ct);
        var items = rows.Select(r => new AttentionItem(AttentionKind.Approval, Student(r.StudentUserId),
            $"{r.Title}{(r.FinalPercent is { } p ? $" · {p:0.#}%" : "")} · ready since {When(r.UpdatedAt)}", $"grading/{r.SessionId}", r.UpdatedAt)).ToList();
        return new(AttentionKind.Approval, "Grades waiting for final approval", count, items, "grading", "Open the grading queue");
    }

    private async Task<AttentionGroup> AppealsAsync(Guid org, CancellationToken ct)
    {
        var q = db.GradeAppeals.AsNoTracking().Where(a => a.OrganizationId == org && a.State == AppealState.Open);
        var count = await q.CountAsync(ct);
        var rows = count == 0 ? [] : await (from a in q
                                             join s in db.ExamSessions.AsNoTracking() on a.SessionId equals s.Id
                                             join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
                                             orderby a.OpenedAt
                                             select new { a.SessionId, a.StudentUserId, e.Title, a.OpenedAt }).Take(ItemsPerGroup).ToListAsync(ct);
        var items = rows.Select(r => new AttentionItem(AttentionKind.Appeal, Student(r.StudentUserId),
            $"{r.Title} · appealed {When(r.OpenedAt)}", $"grading/{r.SessionId}", r.OpenedAt)).ToList();
        return new(AttentionKind.Appeal, "Open appeals", count, items, "grading", "Open the grading queue");
    }

    private async Task<AttentionGroup> NotStartedNearDueAsync(Guid org, DateTimeOffset now, CancellationToken ct)
    {
        var due = now.AddDays(NearDueDays);
        var q = from a in db.ExamAssignments.AsNoTracking()
                join e in db.Exams.AsNoTracking() on a.ExamId equals e.Id
                where a.OrganizationId == org && e.Status == ExamStatus.Published
                      && a.ClosesAt != null && a.ClosesAt > now && a.ClosesAt <= due
                      && !db.ExamSessions.Any(s => s.AssignmentId == a.Id && s.StartedAt != null)
                select new { a.Id, a.StudentUserId, e.Title, a.ClosesAt };
        var count = await q.CountAsync(ct);
        var rows = count == 0 ? [] : await q.OrderBy(r => r.ClosesAt).Take(ItemsPerGroup).ToListAsync(ct);
        var items = rows.Select(r => new AttentionItem(AttentionKind.NotStartedNearDue, Student(r.StudentUserId),
            $"{r.Title} · closes in {Ago(r.ClosesAt!.Value - now)} ({When(r.ClosesAt)})", $"admin/students/{r.StudentUserId}", r.ClosesAt)).ToList();
        return new(AttentionKind.NotStartedNearDue, $"Not started, closing within {NearDueDays} days", count, items, "admin/assignments", "Open student terms");
    }

    private async Task<AttentionGroup> FailedJobsAsync(Guid org, DateTimeOffset now, CancellationToken ct)
    {
        // The job table is not tenant-filtered: the organization filter here is what keeps another organization's jobs out.
        var since = now.AddDays(-FailedJobDays);
        var q = db.Jobs.AsNoTracking().Where(j => j.OrganizationId == org && j.State == JobState.Failed && (j.CompletedAt ?? j.CreatedAt) >= since);
        var count = await q.CountAsync(ct);
        var rows = count == 0 ? [] : await q.OrderByDescending(j => j.CompletedAt ?? j.CreatedAt).Take(ItemsPerGroup)
            .Select(j => new { j.Kind, j.Error, j.Attempts, At = j.CompletedAt ?? j.CreatedAt }).ToListAsync(ct);
        var items = rows.Select(r => new AttentionItem(AttentionKind.FailedJob, JobLabel(r.Kind),
            $"failed after {r.Attempts} tr{(r.Attempts == 1 ? "y" : "ies")} {When(r.At)}{(string.IsNullOrWhiteSpace(r.Error) ? "" : " · " + Shorten(r.Error!, 140))}",
            "admin/status", r.At)).ToList();
        return new(AttentionKind.FailedJob, $"Background work that failed (last {FailedJobDays} days)", count, items, "admin/status", "Open system status");
    }

    /// <summary>Plain names for the job kinds an admin may see.</summary>
    public static string JobLabel(string kind) => kind switch
    {
        "source.process" => "Reading source notes",
        "grading.ai" => "AI preliminary grading",
        "webhook.deliver" => "Webhook delivery",
        "privacy.retention" => "Retention clean-up",
        _ => kind,
    };

    // Placeholder names are replaced by NameStudentsAsync with one query for every student shown.
    private const string NamePrefix = "\u0001student:";
    private static string Student(Guid id) => NamePrefix + id.ToString("N");

    private async Task NameStudentsAsync(List<AttentionGroup> groups, CancellationToken ct)
    {
        var ids = groups.SelectMany(g => g.Items).Select(i => i.Title).Where(t => t.StartsWith(NamePrefix, StringComparison.Ordinal))
            .Select(t => Guid.ParseExact(t[NamePrefix.Length..], "N")).Distinct().ToList();
        var names = ids.Count == 0 ? new Dictionary<Guid, string>()
            : await db.AuthUsers.AsNoTracking().Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.DisplayName != null && u.DisplayName != "" ? u.DisplayName : u.UserName })
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            groups[i] = g with
            {
                Items = g.Items.Select(it => it.Title.StartsWith(NamePrefix, StringComparison.Ordinal)
                    ? it with { Title = names.GetValueOrDefault(Guid.ParseExact(it.Title[NamePrefix.Length..], "N")) ?? "(unknown student)" }
                    : it).ToList(),
            };
        }
    }

    // ------------------------------------------------------------------ numbers

    private async Task<DashboardWindow> WindowAsync(Guid org, DateTimeOffset now, int days, CancellationToken ct)
    {
        var since = now.AddDays(-days);
        var sessions = db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org && (s.EndReason == null || s.EndReason != NotAnAttempt));

        var durations = await sessions.Where(s => s.State == SessionState.Ended && s.EndedAt >= since && s.StartedAt != null)
            .OrderByDescending(s => s.EndedAt).Take(MedianSample)
            .Select(s => new { Start = s.StartedAt!.Value, End = s.EndedAt!.Value }).ToListAsync(ct);
        var examsTaken = durations.Count < MedianSample ? durations.Count
            : await sessions.CountAsync(s => s.State == SessionState.Ended && s.EndedAt >= since, ct);

        var started = await sessions.Where(s => s.StartedAt >= since && !Live.Contains(s.State))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                All = g.Count(),
                Completed = g.Count(s => s.State == SessionState.Ended && (s.EndReason == null || !NotCompleted.Contains(s.EndReason))),
            })
            .FirstOrDefaultAsync(ct);

        // Turnaround: the interview's end to the grade's first final approval, for grades first approved in the window.
        var approvals = await (from a in db.ApprovalRecords.AsNoTracking()
                               where a.OrganizationId == org && a.Action == ApprovalAction.Finalize
                               group a by a.SessionId into g
                               select new { SessionId = g.Key, At = g.Min(x => x.At) } into first
                               where first.At >= since
                               join s in db.ExamSessions.AsNoTracking() on first.SessionId equals s.Id
                               where s.EndedAt != null
                               orderby first.At descending
                               select new { End = s.EndedAt!.Value, first.At }).Take(MedianSample).ToListAsync(ct);

        return new DashboardWindow(days, examsTaken, started?.All ?? 0, started?.Completed ?? 0,
            Median(durations.Select(d => d.End - d.Start)), approvals.Count, Median(approvals.Select(a => a.At - a.End)));
    }

    /// <summary>The median of the non-negative spans (a negative one is a clock oddity and is left out); null when there are none.</summary>
    public static TimeSpan? Median(IEnumerable<TimeSpan> spans)
    {
        var sorted = spans.Where(s => s >= TimeSpan.Zero).Select(s => s.Ticks).Order().ToArray();
        if (sorted.Length == 0) return null;
        var mid = sorted.Length / 2;
        return TimeSpan.FromTicks(sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2);
    }

    // ------------------------------------------------------------------ wording

    /// <summary>A span in plain words: "45 minutes", "3 hours", "2 days".</summary>
    public static string Ago(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return "less than a minute";
        if (span.TotalMinutes < 60) return Plural((int)span.TotalMinutes, "minute");
        if (span.TotalHours < 48) return Plural((int)span.TotalHours, "hour");
        return Plural((int)span.TotalDays, "day");
    }

    /// <summary>A duration as the dashboard shows it: "18 min", "1 h 5 min", "2.5 days"; a dash when there is none.</summary>
    public static string Duration(TimeSpan? span)
    {
        if (span is not { } s) return "—";
        if (s.TotalMinutes < 1) return $"{Math.Max(0, (int)s.TotalSeconds)} s";
        if (s.TotalMinutes < 60) return $"{(int)Math.Round(s.TotalMinutes)} min";
        if (s.TotalHours < 48) return s.Minutes == 0 || s.TotalHours >= 10 ? $"{(int)Math.Round(s.TotalHours)} h" : $"{(int)s.TotalHours} h {s.Minutes} min";
        return $"{s.TotalDays:0.#} days";
    }

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")}";

    private static string When(DateTimeOffset? at) => at is { } t ? t.UtcDateTime.ToString("d MMM HH:mm") + " UTC" : "at an unknown time";

    private static string Shorten(string s, int max)
    {
        s = s.ReplaceLineEndings(" ").Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
