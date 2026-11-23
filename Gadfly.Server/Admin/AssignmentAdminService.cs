using System.Text;
using Gadfly.Server.Data;
using Gadfly.Server.Desk;
using Gadfly.Server.Exams;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Admin;

public enum AssignmentProgressFilter { Any, NotStarted, InProgress, Interrupted, AwaitingGrading, Graded, Released }

public enum AssignmentAccommodationFilter { Any, ExtraTime, EyeContact, TypedAnswers, AnyAccommodation }

public enum AssignmentSort { Student, Exam, Due, Assigned }

/// <summary>What the assignments list asks for.</summary>
public sealed record AssignmentQuery
{
    public Guid? ExamId { get; init; }
    /// <summary>Matches the student's name, user name or email.</summary>
    public string? Student { get; init; }
    public AssignmentProgressFilter Progress { get; init; } = AssignmentProgressFilter.Any;
    public AssignmentAccommodationFilter Accommodation { get; init; } = AssignmentAccommodationFilter.Any;
    /// <summary>Only assignments due (closing) on or after this instant.</summary>
    public DateTimeOffset? DueFrom { get; init; }
    /// <summary>Only assignments due before this instant.</summary>
    public DateTimeOffset? DueTo { get; init; }
    public AssignmentSort Sort { get; init; } = AssignmentSort.Student;
    public bool Descending { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record AssignmentListRow(Guid Id, Guid ExamId, string ExamTitle, ExamStatus ExamStatus, Guid StudentUserId, string StudentName,
    string UserName, string? Email, bool StudentActive, DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt, int AttemptsAllowed, int AttemptsUsed,
    AssignmentProgress Progress, string? Score, DateTimeOffset? ReleasedAt, decimal TimeMultiplier, bool EyeContact, bool TypedAnswers,
    DateTimeOffset AssignedAt);

public sealed record AssignmentPage(IReadOnlyList<AssignmentListRow> Rows, int Total, int Page, int PageSize)
{
    public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

/// <summary>One cell of the students-by-exams grid: the assignment there, or none.</summary>
public sealed record GridCell(Guid AssignmentId, AssignmentProgress Progress, int AttemptsUsed, int AttemptsAllowed, DateTimeOffset? ClosesAt);

public sealed record GridStudent(Guid UserId, string Name, string UserName);

public sealed record AssignmentGrid(IReadOnlyList<GridStudent> Students, IReadOnlyList<(Guid Id, string Title, ExamStatus Status)> Exams,
    IReadOnlyDictionary<(Guid Student, Guid Exam), GridCell> Cells, int TotalStudents, int Page, int PageSize)
{
    public int Pages => Math.Max(1, (TotalStudents + PageSize - 1) / PageSize);
}

/// <summary>The outcome of a bulk change: how many changed, and why the others did not.</summary>
public sealed record BulkResult(bool Ok, string Message, int Changed, IReadOnlyList<string> Problems)
{
    public static BulkResult Fail(string message) => new(false, message, 0, []);
}

/// <summary>
/// The assignments console (GAD-T298): every student's exams as a paged list or a students-by-exams grid, with status worked out
/// in SQL; bulk assign and unassign, set or move due dates, grant another attempt, reset an attempt, accommodations, CSV export.
/// Built over the services that own those rules: <see cref="ExamService"/> assigns and sets the eye-contact accommodation, and
/// the admin desk's <see cref="DeskService.ClearScoresAsync"/> does the clearing part of a reset (owner decision (d), GAD-T262:
/// the grading records are archived to the audit log first and the attempt stays held out of grading). Every change is audited
/// per assignment, so it shows on the student's profile; the changes that take something away need a reason.
/// </summary>
public sealed class AssignmentAdminService(IServiceProvider sp)
{
    private GadflyDbContext Db => sp.GetRequiredService<GadflyDbContext>();
    private AdminGate Gate => field ??= new AdminGate(Db, sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<TimeProvider>());
    private ExamService Exams => field ??= ActivatorUtilities.CreateInstance<ExamService>(sp);
    private DeskService Desk => field ??= new DeskService(sp);

    public const int MaxBulk = 2000;
    public const int MaxAttempts = 20;
    public const int MaxGridExams = 40;
    public const int MaxExportRows = 100_000;

    private static readonly SessionState[] Closed = [SessionState.Ended, SessionState.Failed];

    // ================================================================== reading

    /// <summary>A member-init shape so EF can keep filtering and sorting after the projection.</summary>
    private sealed class Flat
    {
        public Guid Id { get; init; }
        public Guid ExamId { get; init; }
        public string ExamTitle { get; init; } = "";
        public ExamStatus ExamStatus { get; init; }
        public Guid StudentUserId { get; init; }
        public string UserName { get; init; } = "";
        public string? DisplayName { get; init; }
        public string? Email { get; init; }
        public bool StudentActive { get; init; }
        public DateTimeOffset? OpensAt { get; init; }
        public DateTimeOffset? ClosesAt { get; init; }
        public int AttemptsAllowed { get; init; }
        public DateTimeOffset? ReleasedAt { get; init; }
        public decimal TimeMultiplier { get; init; }
        public bool EyeContact { get; init; }
        public bool TypedAnswers { get; init; }
        public DateTimeOffset AssignedAt { get; init; }
        public int Used { get; init; }
        public bool Live { get; init; }
        public bool Ended { get; init; }
        public bool Graded { get; init; }
    }

    /// <summary>Every assignment of the organization with the facts its status is worked out from, all in one SQL query.</summary>
    private IQueryable<Flat> Base()
    {
        var db = Db;
        var org = Gate.Org;
        var notAnAttempt = AdminPeople.NotAnAttempt;
        var finals = AdminPeople.FinalStatuses;
        return
            from a in db.ExamAssignments.AsNoTracking()
            join e in db.Exams.AsNoTracking() on a.ExamId equals e.Id
            join u in db.AuthUsers.AsNoTracking() on a.StudentUserId equals u.Id
            where a.OrganizationId == org && e.OrganizationId == org
            select new Flat
            {
                Id = a.Id, ExamId = a.ExamId, ExamTitle = e.Title, ExamStatus = e.Status, StudentUserId = a.StudentUserId,
                UserName = u.UserName, DisplayName = u.DisplayName, Email = u.Email,
                StudentActive = db.OrganizationMembers.Any(m => m.OrganizationId == org && m.UserId == a.StudentUserId && m.IsActive),
                OpensAt = a.OpensAt, ClosesAt = a.ClosesAt, AttemptsAllowed = a.AttemptsAllowed, ReleasedAt = a.ResultsReleasedAt,
                TimeMultiplier = a.TimeMultiplier, EyeContact = a.GazeAccommodation, TypedAnswers = a.TypedAnswerAccommodation, AssignedAt = a.AssignedAt,
                Used = db.ExamSessions.Count(s => s.OrganizationId == org && s.AssignmentId == a.Id && (s.EndReason == null || s.EndReason != notAnAttempt)),
                Live = db.ExamSessions.Any(s => s.OrganizationId == org && s.AssignmentId == a.Id && !Closed.Contains(s.State)),
                Ended = db.ExamSessions.Any(s => s.OrganizationId == org && s.AssignmentId == a.Id && s.State == SessionState.Ended),
                Graded = db.FinalGrades.Any(f => f.OrganizationId == org && finals.Contains(f.Status)
                                                 && db.ExamSessions.Any(s => s.Id == f.SessionId && s.OrganizationId == org && s.AssignmentId == a.Id)),
            };
    }

    private IQueryable<Flat> Filtered(AssignmentQuery q)
    {
        var rows = Base();
        if (q.ExamId is { } exam) rows = rows.Where(r => r.ExamId == exam);
        if (q.Student?.Trim() is { Length: > 0 } s)
            rows = rows.Where(r => r.UserName.Contains(s) || (r.DisplayName != null && r.DisplayName.Contains(s)) || (r.Email != null && r.Email.Contains(s)));
        if (q.DueFrom is { } from) rows = rows.Where(r => r.ClosesAt != null && r.ClosesAt >= from);
        if (q.DueTo is { } to) rows = rows.Where(r => r.ClosesAt != null && r.ClosesAt < to);
        rows = q.Accommodation switch
        {
            AssignmentAccommodationFilter.ExtraTime => rows.Where(r => r.TimeMultiplier > 1m),
            AssignmentAccommodationFilter.EyeContact => rows.Where(r => r.EyeContact),
            AssignmentAccommodationFilter.TypedAnswers => rows.Where(r => r.TypedAnswers),
            AssignmentAccommodationFilter.AnyAccommodation => rows.Where(r => r.TimeMultiplier > 1m || r.EyeContact || r.TypedAnswers),
            _ => rows,
        };
        // The same order of rules as AdminPeople.Of, written as SQL conditions.
        rows = q.Progress switch
        {
            AssignmentProgressFilter.InProgress => rows.Where(r => r.Live),
            AssignmentProgressFilter.NotStarted => rows.Where(r => !r.Live && r.Used == 0),
            AssignmentProgressFilter.Released => rows.Where(r => !r.Live && r.Used > 0 && r.ReleasedAt != null),
            AssignmentProgressFilter.Interrupted => rows.Where(r => !r.Live && r.Used > 0 && r.ReleasedAt == null && !r.Ended),
            AssignmentProgressFilter.Graded => rows.Where(r => !r.Live && r.Used > 0 && r.ReleasedAt == null && r.Ended && r.Graded),
            AssignmentProgressFilter.AwaitingGrading => rows.Where(r => !r.Live && r.Used > 0 && r.ReleasedAt == null && r.Ended && !r.Graded),
            _ => rows,
        };
        return (q.Sort, q.Descending) switch
        {
            (AssignmentSort.Exam, false) => rows.OrderBy(r => r.ExamTitle).ThenBy(r => r.DisplayName ?? r.UserName),
            (AssignmentSort.Exam, true) => rows.OrderByDescending(r => r.ExamTitle).ThenBy(r => r.DisplayName ?? r.UserName),
            // No due date sorts last either way round.
            (AssignmentSort.Due, false) => rows.OrderBy(r => r.ClosesAt == null).ThenBy(r => r.ClosesAt).ThenBy(r => r.DisplayName ?? r.UserName),
            (AssignmentSort.Due, true) => rows.OrderBy(r => r.ClosesAt == null).ThenByDescending(r => r.ClosesAt).ThenBy(r => r.DisplayName ?? r.UserName),
            (AssignmentSort.Assigned, false) => rows.OrderBy(r => r.AssignedAt).ThenBy(r => r.Id),
            (AssignmentSort.Assigned, true) => rows.OrderByDescending(r => r.AssignedAt).ThenBy(r => r.Id),
            (_, true) => rows.OrderByDescending(r => r.DisplayName ?? r.UserName).ThenBy(r => r.ExamTitle),
            _ => rows.OrderBy(r => r.DisplayName ?? r.UserName).ThenBy(r => r.ExamTitle),
        };
    }

    private static AssignmentProgress ProgressOf(Flat f) => AdminPeople.Of(f.Used, f.Live, f.Ended, f.Graded, f.ReleasedAt);

    private async Task<IReadOnlyList<AssignmentListRow>> RowsAsync(List<Flat> flat, CancellationToken ct)
    {
        if (flat.Count == 0) return [];
        var db = Db;
        var org = Gate.Org;
        var ids = flat.Select(f => f.Id).ToList();
        // The latest attempt of each assignment on the page, and its score.
        var latest = (await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org && ids.Contains(s.AssignmentId))
                .Select(s => new { s.Id, s.AssignmentId, s.Attempt }).ToListAsync(ct))
            .GroupBy(s => s.AssignmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Attempt).First().Id);
        var sessionIds = latest.Values.ToList();
        var finals = await db.FinalGrades.AsNoTracking().Where(g => g.OrganizationId == org && sessionIds.Contains(g.SessionId))
            .Select(g => new { g.SessionId, g.FinalPercent, g.Status }).ToDictionaryAsync(g => g.SessionId, ct);
        var reports = (await db.SessionReports.AsNoTracking().Where(r => r.OrganizationId == org && sessionIds.Contains(r.SessionId))
                .Select(r => new { r.SessionId, r.ReportJson, r.CreatedAt }).ToListAsync(ct))
            .GroupBy(r => r.SessionId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First().ReportJson);
        return flat.Select(f =>
        {
            string? score = null;
            if (latest.TryGetValue(f.Id, out var sid))
            {
                finals.TryGetValue(sid, out var g);
                score = AdminPeople.Score(g?.FinalPercent, g?.Status, reports.GetValueOrDefault(sid));
            }
            return new AssignmentListRow(f.Id, f.ExamId, f.ExamTitle, f.ExamStatus, f.StudentUserId, AdminPeople.NameOf(f.UserName, f.DisplayName),
                f.UserName, f.Email, f.StudentActive, f.OpensAt, f.ClosesAt, f.AttemptsAllowed, f.Used, ProgressOf(f), score, f.ReleasedAt,
                f.TimeMultiplier, f.EyeContact, f.TypedAnswers, f.AssignedAt);
        }).ToList();
    }

    /// <summary>One page of the list.</summary>
    public async Task<AssignmentPage> ListAsync(AssignmentQuery q, CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var size = AdminPeople.ClampPageSize(q.PageSize);
        var query = Filtered(q);
        var total = await query.CountAsync(ct);
        var page = Math.Clamp(q.Page, 1, Math.Max(1, (total + size - 1) / size));
        var flat = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new AssignmentPage(await RowsAsync(flat, ct), total, page, size);
    }

    /// <summary>
    /// The students-by-exams grid: one page of active students (matching the search) down the side, the published exams (or the one
    /// chosen) across the top, and each cell's assignment status.
    /// </summary>
    public async Task<AssignmentGrid> GridAsync(string? studentSearch, Guid? examId, int page, int pageSize, CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var db = Db;
        var org = Gate.Org;
        var size = AdminPeople.ClampPageSize(pageSize);
        var students =
            from m in db.OrganizationMembers.AsNoTracking()
            join u in db.AuthUsers.AsNoTracking() on m.UserId equals u.Id
            where m.OrganizationId == org && m.Role == OrgRoles.Student && m.IsActive
            select new { u.Id, u.UserName, u.DisplayName, u.Email };
        if (studentSearch?.Trim() is { Length: > 0 } s)
            students = students.Where(x => x.UserName.Contains(s) || (x.DisplayName != null && x.DisplayName.Contains(s)) || (x.Email != null && x.Email.Contains(s)));
        var total = await students.CountAsync(ct);
        var pageNo = Math.Clamp(page, 1, Math.Max(1, (total + size - 1) / size));
        var shown = await students.OrderBy(x => x.DisplayName ?? x.UserName).ThenBy(x => x.UserName)
            .Skip((pageNo - 1) * size).Take(size).ToListAsync(ct);

        var examQuery = db.Exams.AsNoTracking().Where(e => e.OrganizationId == org);
        examQuery = examId is { } one ? examQuery.Where(e => e.Id == one) : examQuery.Where(e => e.Status == ExamStatus.Published);
        var exams = (await examQuery.OrderBy(e => e.Title).Take(MaxGridExams).Select(e => new { e.Id, e.Title, e.Status }).ToListAsync(ct))
            .Select(e => (e.Id, e.Title, e.Status)).ToList();

        var studentIds = shown.Select(x => x.Id).ToList();
        var examIds = exams.Select(e => e.Id).ToList();
        var flat = await Base().Where(f => studentIds.Contains(f.StudentUserId) && examIds.Contains(f.ExamId)).ToListAsync(ct);
        var cells = flat.ToDictionary(f => (f.StudentUserId, f.ExamId), f => new GridCell(f.Id, ProgressOf(f), f.Used, f.AttemptsAllowed, f.ClosesAt));
        return new AssignmentGrid(shown.Select(x => new GridStudent(x.Id, AdminPeople.NameOf(x.UserName, x.DisplayName), x.UserName)).ToList(),
            exams, cells, total, pageNo, size);
    }

    /// <summary>Exams for the filters: every published or archived exam, published first.</summary>
    public async Task<IReadOnlyList<(Guid Id, string Title, ExamStatus Status)>> ExamOptionsAsync(CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var org = Gate.Org;
        return (await Db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && e.Status != ExamStatus.Draft)
                .OrderBy(e => e.Status).ThenBy(e => e.Title).Select(e => new { e.Id, e.Title, e.Status }).ToListAsync(ct))
            .Select(e => (e.Id, e.Title, e.Status)).ToList();
    }

    /// <summary>The list as CSV: every assignment the filters match (no paging). Recorded in the audit log.</summary>
    public async Task<string> ExportCsvAsync(AssignmentQuery q, CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var flat = await Filtered(q).Take(MaxExportRows).ToListAsync(ct);
        var sb = new StringBuilder();
        sb.Append(StudentCsv.Line("student", "username", "email", "exam", "status", "opens_utc", "due_utc", "attempts_used", "attempts_allowed",
            "score", "results_released_utc", "extra_time", "eye_contact_not_expected", "typed_answers", "assigned_utc"));
        // Scores in chunks, so a large export never builds one huge IN list.
        foreach (var chunk in flat.Chunk(500))
            foreach (var r in await RowsAsync([.. chunk], ct))
                sb.Append(StudentCsv.Line(r.StudentName, r.UserName, r.Email, r.ExamTitle, AdminPeople.Describe(r.Progress), Utc(r.OpensAt), Utc(r.ClosesAt),
                    r.AttemptsUsed, r.AttemptsAllowed, r.Score, Utc(r.ReleasedAt), r.TimeMultiplier > 1m ? AdminPeople.Time(r.TimeMultiplier) : "",
                    r.EyeContact ? "yes" : "", r.TypedAnswers ? "yes" : "", Utc(r.AssignedAt)));
        Gate.Audit("admin.assignments-exported", "ExamAssignment", null, after: new { Rows = flat.Count, Filter = q with { Page = 1 } });
        await Db.SaveChangesAsync(ct);
        return sb.ToString();
    }

    private static string Utc(DateTimeOffset? d) =>
        d is { } x ? x.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) : "";

    // ================================================================== changes

    /// <summary>The assignments named, in this organization only; caps the selection.</summary>
    private async Task<(List<ExamAssignment> Rows, string? Problem)> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return ([], "Choose at least one assignment.");
        if (ids.Count > MaxBulk) return ([], $"Choose at most {MaxBulk:N0} assignments at a time.");
        var org = Gate.Org;
        var list = ids.Distinct().ToList();
        var rows = await Db.ExamAssignments.Where(a => a.OrganizationId == org && list.Contains(a.Id)).ToListAsync(ct);
        return rows.Count == 0 ? ([], "Those assignments no longer exist.") : (rows, null);
    }

    private static string Count(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";

    /// <summary>
    /// Assigns a published exam to the chosen students, with a window, attempts and extra time (the exam service's rules and audit).
    /// Students already assigned are skipped and say so.
    /// </summary>
    /// <param name="studentUserIds">The students; null means every active student of the organization.</param>
    public async Task<BulkResult> AssignAsync(Guid examId, IReadOnlyCollection<Guid>? studentUserIds, DateTimeOffset? opensAt, DateTimeOffset? closesAt,
        int attemptsAllowed, decimal timeMultiplier, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Assigning an exam", "Exam", examId.ToString(), ct)) return BulkResult.Fail(AdminGate.DeniedMessage);
        if (studentUserIds is { Count: 0 }) return BulkResult.Fail("Choose at least one student.");
        if (studentUserIds is { Count: > MaxBulk }) return BulkResult.Fail($"Choose at most {MaxBulk:N0} students at a time.");
        var r = await Exams.AssignAsync(examId, studentUserIds?.Distinct().ToList(), opensAt, closesAt, attemptsAllowed, timeMultiplier, ct);
        return new BulkResult(r.Ok, r.Message, 0, []);
    }

    /// <summary>Whether the exam is open to every student of the organization (GAD-T669); null when it isn't this organization's.</summary>
    public async Task<bool?> OpenToAllStudentsAsync(Guid examId, CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var org = Gate.Org;
        return await Db.Exams.AsNoTracking().Where(e => e.Id == examId && e.OrganizationId == org)
            .Select(e => (bool?)e.OpenToAllStudents).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Opens the exam to every student of the organization, or limits it to the students assigned (GAD-T669). Only who can start
    /// it changes: assignments already made (open or assigned) stay as they are. Allowed on published exams, since it is access,
    /// not a frozen exam setting. Audited as <c>exam.open-to-all</c>.
    /// </summary>
    public async Task<BulkResult> SetOpenToAllStudentsAsync(Guid examId, bool open, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Changing who can take an exam", "Exam", examId.ToString(), ct)) return BulkResult.Fail(AdminGate.DeniedMessage);
        var db = Db;
        var org = Gate.Org;
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return BulkResult.Fail("That exam does not exist in this organization.");
        if (exam.OpenToAllStudents == open) return new BulkResult(true, "No change.", 0, []);
        Gate.Audit("exam.open-to-all", "Exam", exam.Id.ToString(), new { exam.OpenToAllStudents }, new { OpenToAllStudents = open });
        exam.OpenToAllStudents = open;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new BulkResult(true, open
            ? "Every student in the organization can now take this exam."
            : "Only the students assigned can take this exam now. Assignments already made are kept.", 1, []);
    }

    /// <summary>
    /// Active students named in pasted text (user names or emails, one per line or separated by commas or semicolons), and the
    /// entries that matched nobody.
    /// </summary>
    public async Task<(IReadOnlyList<Guid> Ids, IReadOnlyList<string> Unknown)> ResolveStudentsAsync(string? text, CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var wanted = (text ?? "").Split(['\n', '\r', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxBulk).ToList();
        if (wanted.Count == 0) return ([], []);
        var db = Db;
        var org = Gate.Org;
        var normalized = wanted.Select(w => MindAttic.Authentication.Services.IUserStore.Normalize(w)).ToList();
        var found = await (from m in db.OrganizationMembers.AsNoTracking()
                           join u in db.AuthUsers.AsNoTracking() on m.UserId equals u.Id
                           where m.OrganizationId == org && m.Role == OrgRoles.Student && m.IsActive
                                 && (normalized.Contains(u.NormalizedUserName) || (u.Email != null && wanted.Contains(u.Email)))
                           select new { u.Id, u.NormalizedUserName, u.Email }).ToListAsync(ct);
        var unknown = wanted.Where(w => !found.Any(f => f.NormalizedUserName == MindAttic.Authentication.Services.IUserStore.Normalize(w)
                                                        || string.Equals(f.Email, w, StringComparison.OrdinalIgnoreCase))).ToList();
        return (found.Select(f => f.Id).Distinct().ToList(), unknown);
    }

    /// <summary>
    /// Removes assignments that have no attempt yet, with a reason (audited with what was removed). An assignment with any attempt
    /// is kept, since its transcript and grades hang off it: reset the attempt instead, or let it stand.
    /// </summary>
    public async Task<BulkResult> UnassignAsync(IReadOnlyCollection<Guid> assignmentIds, string? reason, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Removing assignments", "ExamAssignment", null, ct)) return BulkResult.Fail(AdminGate.DeniedMessage);
        var why = AdminPeople.Reason(reason);
        if (why is null) return BulkResult.Fail("Say why these assignments are being removed; the reason is kept in the audit log.");
        var (rows, problem) = await LoadAsync(assignmentIds, ct);
        if (problem is not null) return BulkResult.Fail(problem);
        var db = Db;
        var org = Gate.Org;
        var ids = rows.Select(a => a.Id).ToList();
        var withAttempts = (await db.ExamSessions.Where(s => s.OrganizationId == org && ids.Contains(s.AssignmentId))
            .Select(s => s.AssignmentId).Distinct().ToListAsync(ct)).ToHashSet();
        var removable = rows.Where(a => !withAttempts.Contains(a.Id)).ToList();
        foreach (var a in removable)
        {
            Gate.Audit("admin.assignment-removed", "ExamAssignment", a.Id.ToString(),
                before: new { a.ExamId, a.StudentUserId, a.OpensAt, a.ClosesAt, a.AttemptsAllowed, a.TimeMultiplier, a.GazeAccommodation, a.TypedAnswerAccommodation },
                reason: why);
            db.ExamAssignments.Remove(a);
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var kept = rows.Count - removable.Count;
        var problems = kept == 0 ? (IReadOnlyList<string>)[] :
            [$"{Count(kept, "assignment")} kept because the student has already made an attempt. Reset the attempt instead, or leave it."];
        return new BulkResult(removable.Count > 0, removable.Count == 0 ? problems[0] : $"Removed {Count(removable.Count, "assignment")}.", removable.Count, problems);
    }

    /// <summary>
    /// Sets the window on the chosen assignments. Each date is changed only when its "change" flag is set (null then clears it).
    /// Rows where the window would close before it opens are left alone and listed. Audited per assignment.
    /// </summary>
    public async Task<BulkResult> SetDatesAsync(IReadOnlyCollection<Guid> assignmentIds, bool changeOpens, DateTimeOffset? opensAt, bool changeCloses,
        DateTimeOffset? closesAt, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Changing assignment dates", "ExamAssignment", null, ct)) return BulkResult.Fail(AdminGate.DeniedMessage);
        if (!changeOpens && !changeCloses) return BulkResult.Fail("Choose which date to change.");
        var (rows, problem) = await LoadAsync(assignmentIds, ct);
        if (problem is not null) return BulkResult.Fail(problem);
        return await ChangeDatesAsync(rows, a => (changeOpens ? opensAt : a.OpensAt, changeCloses ? closesAt : a.ClosesAt), "admin.assignment-dates-set", null, ct);
    }

    /// <summary>Moves the due date of the chosen assignments by a number of days (back with a negative number). Rows with no due date are skipped.</summary>
    public async Task<BulkResult> MoveDueDatesAsync(IReadOnlyCollection<Guid> assignmentIds, int days, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Moving due dates", "ExamAssignment", null, ct)) return BulkResult.Fail(AdminGate.DeniedMessage);
        if (days == 0 || Math.Abs(days) > 365) return BulkResult.Fail("Move the due date by 1 to 365 days, forward or back.");
        var (rows, problem) = await LoadAsync(assignmentIds, ct);
        if (problem is not null) return BulkResult.Fail(problem);
        var undated = rows.Count(a => a.ClosesAt is null);
        var r = await ChangeDatesAsync(rows.Where(a => a.ClosesAt is not null).ToList(), a => (a.OpensAt, a.ClosesAt!.Value.AddDays(days)),
            "admin.assignment-due-moved", $"Due date moved by {days} day{(Math.Abs(days) == 1 ? "" : "s")}.", ct);
        return undated == 0 ? r : r with { Problems = [.. r.Problems, $"{Count(undated, "assignment")} skipped: no due date to move."], Ok = r.Ok || r.Changed > 0 };
    }

    private async Task<BulkResult> ChangeDatesAsync(List<ExamAssignment> rows, Func<ExamAssignment, (DateTimeOffset? Opens, DateTimeOffset? Closes)> next,
        string action, string? reason, CancellationToken ct)
    {
        var db = Db;
        var changed = 0;
        var bad = 0;
        foreach (var a in rows)
        {
            var (opens, closes) = next(a);
            if (opens is { } o && closes is { } c && c <= o) { bad++; continue; }
            if (opens == a.OpensAt && closes == a.ClosesAt) continue;
            Gate.Audit(action, "ExamAssignment", a.Id.ToString(), new { a.OpensAt, a.ClosesAt }, new { OpensAt = opens, ClosesAt = closes, a.StudentUserId, a.ExamId }, reason);
            a.OpensAt = opens;
            a.ClosesAt = closes;
            changed++;
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        IReadOnlyList<string> problems = bad == 0 ? [] : [$"{Count(bad, "assignment")} left alone: the window would close before it opens."];
        return new BulkResult(changed > 0 || bad == 0, changed == 0 && bad == 0 ? "No change: those dates were already set." : $"Dates changed on {Count(changed, "assignment")}.", changed, problems);
    }

    /// <summary>Gives each chosen assignment one more attempt (up to <see cref="MaxAttempts"/>), with a reason. Audited per assignment.</summary>
    public async Task<BulkResult> GrantAttemptAsync(IReadOnlyCollection<Guid> assignmentIds, string? reason, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Granting another attempt", "ExamAssignment", null, ct)) return BulkResult.Fail(AdminGate.DeniedMessage);
        var why = AdminPeople.Reason(reason);
        if (why is null) return BulkResult.Fail("Say why another attempt is being granted; the reason is kept in the audit log.");
        var (rows, problem) = await LoadAsync(assignmentIds, ct);
        if (problem is not null) return BulkResult.Fail(problem);
        var db = Db;
        var changed = 0;
        foreach (var a in rows.Where(a => a.AttemptsAllowed < MaxAttempts))
        {
            Gate.Audit("admin.attempt-granted", "ExamAssignment", a.Id.ToString(), new { a.AttemptsAllowed },
                new { AttemptsAllowed = a.AttemptsAllowed + 1, a.StudentUserId, a.ExamId }, why);
            a.AttemptsAllowed++;
            changed++;
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var capped = rows.Count - changed;
        IReadOnlyList<string> problems = capped == 0 ? [] : [$"{Count(capped, "assignment")} already at the most attempts allowed ({MaxAttempts})."];
        return new BulkResult(changed > 0, changed == 0 ? problems[0] : $"Another attempt granted on {Count(changed, "assignment")}.", changed, problems);
    }

    /// <summary>
    /// Resets an attempt: the admin desk's Clear scores runs first (each attempt's grades, reports, reconciliations, approvals and
    /// appeals copied to the audit log, then removed; results un-released; the attempt and its transcript kept, held out of grading
    /// and still counted, owner decision (d)), then one more attempt is granted so the student can sit it again. Needs a reason;
    /// refused while an attempt is open and when there is no attempt to reset. Audited as <c>admin.attempt-reset</c> on top of the
    /// desk's own rows.
    /// </summary>
    public async Task<BulkResult> ResetAttemptAsync(IReadOnlyCollection<Guid> assignmentIds, string? reason, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Resetting an attempt", "ExamAssignment", null, ct)) return BulkResult.Fail(AdminGate.DeniedMessage);
        var why = AdminPeople.Reason(reason);
        if (why is null) return BulkResult.Fail("Say why the attempt is being reset; the reason is kept in the audit log.");
        var (rows, problem) = await LoadAsync(assignmentIds, ct);
        if (problem is not null) return BulkResult.Fail(problem);
        var db = Db;
        var org = Gate.Org;
        var ids = rows.Select(a => a.Id).ToList();
        var attempted = (await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org && ids.Contains(s.AssignmentId))
            .Select(s => s.AssignmentId).Distinct().ToListAsync(ct)).ToHashSet();
        db.ChangeTracker.Clear();
        var problems = new List<string>();
        var done = 0;
        foreach (var a in rows)
        {
            if (!attempted.Contains(a.Id)) { problems.Add("No attempt to reset yet on one assignment; it was left alone."); continue; }
            var cleared = await Desk.ClearScoresAsync(a.Id, ct);
            if (!cleared.Ok) { problems.Add(cleared.Message); continue; }
            var tracked = await db.ExamAssignments.FirstAsync(x => x.Id == a.Id && x.OrganizationId == org, ct);
            var before = tracked.AttemptsAllowed;
            tracked.AttemptsAllowed = Math.Min(MaxAttempts, before + 1);
            Gate.Audit("admin.attempt-reset", "ExamAssignment", a.Id.ToString(), new { AttemptsAllowed = before },
                new { tracked.AttemptsAllowed, tracked.StudentUserId, tracked.ExamId }, why);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            done++;
        }
        var distinct = problems.GroupBy(p => p).Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ({g.Count()} times)").ToList();
        return new BulkResult(done > 0, done == 0 ? (distinct.FirstOrDefault() ?? "Nothing was reset.")
            : $"Reset {Count(done, "attempt")}: the scores were cleared with an archived copy kept in the audit log, the attempt still counts, and one more attempt was granted.",
            done, distinct);
    }

    /// <summary>Sets the extra-time accommodation on the chosen assignments (from the next attempt). Audited per assignment.</summary>
    public async Task<BulkResult> SetExtraTimeAsync(IReadOnlyCollection<Guid> assignmentIds, decimal multiplier, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Changing an extra-time accommodation", "ExamAssignment", null, ct)) return BulkResult.Fail(AdminGate.DeniedMessage);
        if (multiplier is < 1m or > 4m) return BulkResult.Fail("Extra time must be between none (1x) and 4x.");
        var (rows, problem) = await LoadAsync(assignmentIds, ct);
        if (problem is not null) return BulkResult.Fail(problem);
        var db = Db;
        var changed = 0;
        foreach (var a in rows.Where(a => a.TimeMultiplier != multiplier))
        {
            Gate.Audit("assessment.accommodation.time", "ExamAssignment", a.Id.ToString(), new { a.TimeMultiplier },
                new { TimeMultiplier = multiplier, a.StudentUserId, a.ExamId });
            a.TimeMultiplier = multiplier;
            changed++;
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new BulkResult(true, changed == 0 ? "No change." :
            $"Extra time set to {AdminPeople.Time(multiplier)} on {Count(changed, "assignment")}. It applies from the next attempt.", changed, []);
    }

    /// <summary>Turns the eye-contact accommodation on or off (the exam service's rule and audit, one assignment at a time).</summary>
    public Task<BulkResult> SetEyeContactAsync(IReadOnlyCollection<Guid> assignmentIds, bool on, CancellationToken ct = default) =>
        PerAssignmentAsync("Changing an eye-contact accommodation", assignmentIds, id => Exams.SetGazeAccommodationAsync(id, on, ct),
            on ? "Eye-contact accommodation on" : "Eye-contact accommodation off", ct);

    /// <summary>
    /// Grants or withdraws the typed-answer accommodation (GAD-T260) through the exam service, one assignment at a time: one named
    /// student types their answers on one exam, camera and spoken questions unchanged. Staff only; never a student-facing switch.
    /// </summary>
    public Task<BulkResult> SetTypedAnswersAsync(IReadOnlyCollection<Guid> assignmentIds, bool on, CancellationToken ct = default) =>
        PerAssignmentAsync("Changing a typed-answer accommodation", assignmentIds, id => Exams.SetTypedAnswerAccommodationAsync(id, on, ct),
            on ? "Typed-answer accommodation on" : "Typed-answer accommodation off", ct);

    private async Task<BulkResult> PerAssignmentAsync(string attempted, IReadOnlyCollection<Guid> assignmentIds, Func<Guid, Task<ExamResult>> change,
        string done, CancellationToken ct)
    {
        if (!await Gate.RequireAsync(attempted, "ExamAssignment", null, ct)) return BulkResult.Fail(AdminGate.DeniedMessage);
        var (rows, problem) = await LoadAsync(assignmentIds, ct);
        if (problem is not null) return BulkResult.Fail(problem);
        Db.ChangeTracker.Clear();
        var problems = new List<string>();
        var changed = 0;
        foreach (var a in rows)
        {
            var r = await change(a.Id);
            if (r.Ok) changed++;
            else problems.Add(r.Message);
        }
        var distinct = problems.Distinct().ToList();
        return new BulkResult(changed > 0, changed == 0 ? distinct.FirstOrDefault() ?? "No change." : $"{done} for {Count(changed, "assignment")}.", changed, distinct);
    }

    public static string Describe(AssignmentProgressFilter f) => f switch
    {
        AssignmentProgressFilter.Any => "Any status",
        AssignmentProgressFilter.NotStarted => AdminPeople.Describe(AssignmentProgress.NotStarted),
        AssignmentProgressFilter.InProgress => AdminPeople.Describe(AssignmentProgress.InProgress),
        AssignmentProgressFilter.Interrupted => AdminPeople.Describe(AssignmentProgress.Interrupted),
        AssignmentProgressFilter.AwaitingGrading => AdminPeople.Describe(AssignmentProgress.AwaitingGrading),
        AssignmentProgressFilter.Graded => AdminPeople.Describe(AssignmentProgress.Graded),
        AssignmentProgressFilter.Released => AdminPeople.Describe(AssignmentProgress.Released),
        _ => f.ToString(),
    };
}
