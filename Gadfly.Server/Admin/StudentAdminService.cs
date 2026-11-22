using Gadfly.Server.Data;
using Gadfly.Server.Desk;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Admin;

public enum StudentStatusFilter { All, Active, Deactivated, NeverSignedIn, Invited }

public enum StudentAccommodationFilter { Any, ExtraTime, EyeContact, AnyAccommodation }

public enum StudentSort { Name, LastSignIn, Assigned, Added }

/// <summary>What the students directory asks for: a search, filters, a sort and a page.</summary>
public sealed record StudentQuery
{
    public string? Search { get; init; }
    public Guid? ExamId { get; init; }
    public StudentStatusFilter Status { get; init; } = StudentStatusFilter.All;
    public StudentAccommodationFilter Accommodation { get; init; } = StudentAccommodationFilter.Any;
    public StudentSort Sort { get; init; } = StudentSort.Name;
    public bool Descending { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

/// <summary>One row of the directory: a student of the organization, or (under the Invited filter) a pending invitation.</summary>
public sealed record StudentListRow(Guid? UserId, Guid? InvitationId, string UserName, string? DisplayName, string? Email, bool Active,
    bool Invited, int Assigned, DateTime? LastLoginUtc, DateTime CreatedUtc, bool ExtraTime, bool EyeContact)
{
    public string Name => AdminPeople.NameOf(UserName, DisplayName);

    public string State => Invited ? "Invited" : !Active ? "Deactivated" : LastLoginUtc is null ? "Never signed in" : "Active";
}

public sealed record StudentPage(IReadOnlyList<StudentListRow> Rows, int Total, int Page, int PageSize, int PendingInvitations)
{
    public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

public sealed record ProfileAttempt(Guid SessionId, int Attempt, SessionState State, string? EndReason, DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt, bool Counts, string? Score, int IntegrityNotes, int IntegrityRaised);

public sealed record ProfileAssignment(Guid AssignmentId, Guid ExamId, string ExamTitle, ExamStatus ExamStatus, DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt, int AttemptsAllowed, int AttemptsUsed, AssignmentProgress Progress, DateTimeOffset? ReleasedAt,
    decimal TimeMultiplier, bool EyeContact, IReadOnlyList<ProfileAttempt> Attempts);

public sealed record ProfileAuditRow(DateTimeOffset At, string Action, string? Actor, string? Detail);

/// <summary>Everything the student profile page shows.</summary>
/// <param name="ManagedHere">The account belongs only to this organization as a student, so its name, email and password can be changed here.</param>
/// <param name="IntegrityNotes">Reviewer notes over every attempt (low-confidence signals, never a verdict).</param>
/// <param name="IntegrityRaised">Of those, how many a reviewer was asked to look at (Medium or High).</param>
public sealed record StudentProfile(Guid UserId, string UserName, string? DisplayName, string? Email, bool Active, bool AccountActive,
    bool MfaEnabled, bool MustChangePassword, DateTime? LastLoginUtc, DateTime CreatedUtc, bool ManagedHere,
    IReadOnlyList<ProfileAssignment> Assignments, int IntegrityNotes, int IntegrityRaised, IReadOnlyList<ProfileAuditRow> Audit)
{
    public string Name => AdminPeople.NameOf(UserName, DisplayName);
}

/// <summary>What the "add a student" form collects.</summary>
public sealed class StudentForm
{
    public string? Name { get; set; }
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public string Password { get; set; } = "";
    public bool MustChange { get; set; } = true;
}

public enum ImportAction { Create, Invite, UpdateName, Unchanged, Skip, Error }

/// <summary>What importing one row does (dry run) or did (applied).</summary>
public sealed record ImportRowResult(int Line, string Email, string? Name, ImportAction Action, string Message, bool Done = false);

public sealed record ImportReport(bool Applied, IReadOnlyList<string> FileErrors, IReadOnlyList<string> IgnoredColumns, IReadOnlyList<ImportRowResult> Rows)
{
    public int Count(ImportAction a) => Rows.Count(r => r.Action == a);
    public int Errors => Rows.Count(r => r.Action == ImportAction.Error);
    /// <summary>Rows that change something.</summary>
    public int Changes => Rows.Count(r => r.Action is ImportAction.Create or ImportAction.Invite or ImportAction.UpdateName);
}

/// <summary>
/// The students directory and profile (GAD-T296): search, filters, sort and paging done in SQL; create, edit, deactivate and
/// reactivate, invitations, password resets and CSV import and export. Built over the services that already own those rules
/// (the admin desk's add-student and password reset, the organization directory's memberships and invitations, the auth library's
/// profile update), so their checks and audit rows still apply, and every change here adds an <c>admin.*</c> audit row with the
/// reason where the change takes something away. Owners and admins only; every query names the organization.
/// </summary>
public sealed class StudentAdminService(IServiceProvider sp)
{
    private GadflyDbContext Db => sp.GetRequiredService<GadflyDbContext>();
    private IOrganizationDirectory Directory => sp.GetRequiredService<IOrganizationDirectory>();
    private AdminGate Gate => field ??= new AdminGate(Db, sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<TimeProvider>());
    private DeskService Desk => field ??= new DeskService(sp);

    public const int MaxExportRows = 50_000;

    // ================================================================== reading

    /// <summary>A member-init shape (not a constructor), so EF can still filter and sort on it after the projection.</summary>
    private sealed class Flat
    {
        public Guid UserId { get; init; }
        public string UserName { get; init; } = "";
        public string? DisplayName { get; init; }
        public string? Email { get; init; }
        public bool Active { get; init; }
        public int Assigned { get; init; }
        public DateTime? LastLoginUtc { get; init; }
        public DateTime CreatedUtc { get; init; }
        public bool ExtraTime { get; init; }
        public bool EyeContact { get; init; }
    }

    /// <summary>The filtered, sorted students of the organization as a query (the Invited filter is handled separately).</summary>
    private IQueryable<Flat> Filtered(StudentQuery q)
    {
        var db = Db;
        var org = Gate.Org;
        var rows =
            from m in db.OrganizationMembers.AsNoTracking()
            join u in db.AuthUsers.AsNoTracking() on m.UserId equals u.Id
            where m.OrganizationId == org && m.Role == OrgRoles.Student
            select new Flat
            {
                UserId = u.Id, UserName = u.UserName, DisplayName = u.DisplayName, Email = u.Email, Active = m.IsActive,
                Assigned = db.ExamAssignments.Count(a => a.OrganizationId == org && a.StudentUserId == u.Id),
                LastLoginUtc = u.LastLoginUtc, CreatedUtc = m.CreatedUtc,
                ExtraTime = db.ExamAssignments.Any(a => a.OrganizationId == org && a.StudentUserId == u.Id && a.TimeMultiplier > 1m),
                EyeContact = db.ExamAssignments.Any(a => a.OrganizationId == org && a.StudentUserId == u.Id && a.GazeAccommodation),
            };

        if (q.Search?.Trim() is { Length: > 0 } s)
            rows = rows.Where(r => r.UserName.Contains(s) || (r.DisplayName != null && r.DisplayName.Contains(s)) || (r.Email != null && r.Email.Contains(s)));
        if (q.ExamId is { } exam)
            rows = rows.Where(r => db.ExamAssignments.Any(a => a.OrganizationId == org && a.ExamId == exam && a.StudentUserId == r.UserId));
        rows = q.Status switch
        {
            StudentStatusFilter.Active => rows.Where(r => r.Active),
            StudentStatusFilter.Deactivated => rows.Where(r => !r.Active),
            StudentStatusFilter.NeverSignedIn => rows.Where(r => r.Active && r.LastLoginUtc == null),
            _ => rows,
        };
        rows = q.Accommodation switch
        {
            StudentAccommodationFilter.ExtraTime => rows.Where(r => r.ExtraTime),
            StudentAccommodationFilter.EyeContact => rows.Where(r => r.EyeContact),
            StudentAccommodationFilter.AnyAccommodation => rows.Where(r => r.ExtraTime || r.EyeContact),
            _ => rows,
        };
        return (q.Sort, q.Descending) switch
        {
            (StudentSort.LastSignIn, false) => rows.OrderBy(r => r.LastLoginUtc).ThenBy(r => r.UserName),
            (StudentSort.LastSignIn, true) => rows.OrderByDescending(r => r.LastLoginUtc).ThenBy(r => r.UserName),
            (StudentSort.Assigned, false) => rows.OrderBy(r => r.Assigned).ThenBy(r => r.UserName),
            (StudentSort.Assigned, true) => rows.OrderByDescending(r => r.Assigned).ThenBy(r => r.UserName),
            (StudentSort.Added, false) => rows.OrderBy(r => r.CreatedUtc).ThenBy(r => r.UserName),
            (StudentSort.Added, true) => rows.OrderByDescending(r => r.CreatedUtc).ThenBy(r => r.UserName),
            (_, true) => rows.OrderByDescending(r => r.DisplayName ?? r.UserName).ThenByDescending(r => r.UserName),
            _ => rows.OrderBy(r => r.DisplayName ?? r.UserName).ThenBy(r => r.UserName),
        };
    }

    private static StudentListRow Row(Flat f) =>
        new(f.UserId, null, f.UserName, f.DisplayName, f.Email, f.Active, false, f.Assigned, f.LastLoginUtc, f.CreatedUtc, f.ExtraTime, f.EyeContact);

    /// <summary>One page of the directory.</summary>
    public async Task<StudentPage> ListAsync(StudentQuery q, CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var size = AdminPeople.ClampPageSize(q.PageSize);
        var invitations = await PendingInvitationsAsync(ct);
        if (q.Status == StudentStatusFilter.Invited)
        {
            var s = q.Search?.Trim();
            var list = invitations.Where(i => string.IsNullOrEmpty(s) || i.Email.Contains(s, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Email, StringComparer.OrdinalIgnoreCase).ToList();
            var invPage = Math.Clamp(q.Page, 1, Math.Max(1, (list.Count + size - 1) / size));
            var rows = list.Skip((invPage - 1) * size).Take(size)
                .Select(i => new StudentListRow(null, i.Id, i.Email, null, i.Email, false, true, 0, null, i.CreatedUtc, false, false)).ToList();
            return new StudentPage(rows, list.Count, invPage, size, invitations.Count);
        }

        var query = Filtered(q);
        var total = await query.CountAsync(ct);
        var page = Math.Clamp(q.Page, 1, Math.Max(1, (total + size - 1) / size));
        var flat = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new StudentPage(flat.Select(Row).ToList(), total, page, size, invitations.Count);
    }

    private async Task<IReadOnlyList<InvitationSummary>> PendingInvitationsAsync(CancellationToken ct) =>
        (await Directory.InvitationsAsync(Gate.Org, ct)).Where(i => i.Status == InvitationStatus.Pending && i.Role == OrgRoles.Student).ToList();

    /// <summary>Published and archived exams, for the "assigned to" filter.</summary>
    public async Task<IReadOnlyList<(Guid Id, string Title)>> ExamOptionsAsync(CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var org = Gate.Org;
        return (await Db.Exams.AsNoTracking().Where(e => e.OrganizationId == org && e.Status != ExamStatus.Draft)
                .OrderBy(e => e.Title).Select(e => new { e.Id, e.Title }).ToListAsync(ct))
            .Select(e => (e.Id, e.Title)).ToList();
    }

    /// <summary>The directory as CSV: every student the filters match (no paging), up to <see cref="MaxExportRows"/>.</summary>
    public async Task<string> ExportCsvAsync(StudentQuery q, CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var sb = new System.Text.StringBuilder();
        sb.Append(StudentCsv.Line("email", "name", "username", "status", "exams_assigned", "extra_time", "eye_contact", "last_sign_in_utc", "added_utc"));
        if (q.Status == StudentStatusFilter.Invited)
        {
            foreach (var i in await PendingInvitationsAsync(ct))
                sb.Append(StudentCsv.Line(i.Email, "", "", "Invited", 0, "", "", "", Utc(i.CreatedUtc)));
            return sb.ToString();
        }
        var rows = await Filtered(q).Take(MaxExportRows).ToListAsync(ct);
        foreach (var r in rows.Select(Row))
            sb.Append(StudentCsv.Line(r.Email, r.DisplayName, r.UserName, r.State, r.Assigned, r.ExtraTime ? "yes" : "", r.EyeContact ? "yes" : "",
                r.LastLoginUtc is { } l ? Utc(l) : "", Utc(r.CreatedUtc)));
        // Personal data leaving the app: who took it, and how much, is kept.
        Gate.Audit("admin.students-exported", "User", null, after: new { Rows = rows.Count, Filter = q with { Page = 1 } });
        await Db.SaveChangesAsync(ct);
        return sb.ToString();
    }

    private static string Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>One student's profile, or null when they are not a student of this organization.</summary>
    public async Task<StudentProfile?> ProfileAsync(Guid userId, CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var db = Db;
        var org = Gate.Org;
        var member = await db.OrganizationMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrganizationId == org && m.UserId == userId && m.Role == OrgRoles.Student, ct);
        if (member is null) return null;
        var u = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u is null) return null;

        var assignments = await db.ExamAssignments.AsNoTracking().Where(a => a.OrganizationId == org && a.StudentUserId == userId)
            .Join(db.Exams.AsNoTracking(), a => a.ExamId, e => e.Id, (a, e) => new { A = a, e.Title, e.Status })
            .ToListAsync(ct);
        var assignmentIds = assignments.Select(a => a.A.Id).ToList();
        var sessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == org && s.StudentUserId == userId && assignmentIds.Contains(s.AssignmentId))
            .Select(s => new { s.Id, s.AssignmentId, s.Attempt, s.State, s.EndReason, s.StartedAt, s.EndedAt })
            .ToListAsync(ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var finals = await db.FinalGrades.AsNoTracking().Where(g => g.OrganizationId == org && sessionIds.Contains(g.SessionId))
            .Select(g => new { g.SessionId, g.FinalPercent, g.Status }).ToDictionaryAsync(g => g.SessionId, ct);
        var reports = (await db.SessionReports.AsNoTracking().Where(r => r.OrganizationId == org && sessionIds.Contains(r.SessionId))
                .Select(r => new { r.SessionId, r.ReportJson, r.CreatedAt }).ToListAsync(ct))
            .GroupBy(r => r.SessionId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First().ReportJson);
        var integrity = (await db.IntegrityEvents.AsNoTracking().Where(e => e.OrganizationId == org && sessionIds.Contains(e.SessionId))
                .GroupBy(e => e.SessionId)
                .Select(g => new { g.Key, All = g.Count(), Raised = g.Count(e => e.Severity == IntegritySeverity.Medium || e.Severity == IntegritySeverity.High) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key);

        var views = assignments.Select(x =>
            {
                var mine = sessions.Where(s => s.AssignmentId == x.A.Id).OrderByDescending(s => s.Attempt).ToList();
                var attempts = mine.Select(s =>
                {
                    finals.TryGetValue(s.Id, out var f);
                    var score = AdminPeople.Score(f?.FinalPercent, f?.Status, reports.GetValueOrDefault(s.Id));
                    integrity.TryGetValue(s.Id, out var i);
                    return new ProfileAttempt(s.Id, s.Attempt, s.State, s.EndReason, s.StartedAt, s.EndedAt, s.EndReason != AdminPeople.NotAnAttempt,
                        score, i?.All ?? 0, i?.Raised ?? 0);
                }).ToList();
                var used = attempts.Count(a => a.Counts);
                var progress = AdminPeople.Of(used, mine.Any(s => s.State is not (SessionState.Ended or SessionState.Failed)),
                    mine.Any(s => s.State == SessionState.Ended),
                    mine.Any(s => finals.TryGetValue(s.Id, out var f) && AdminPeople.FinalStatuses.Contains(f.Status)), x.A.ResultsReleasedAt);
                return new ProfileAssignment(x.A.Id, x.A.ExamId, x.Title, x.Status, x.A.OpensAt, x.A.ClosesAt, x.A.AttemptsAllowed, used, progress,
                    x.A.ResultsReleasedAt, x.A.TimeMultiplier, x.A.GazeAccommodation, attempts);
            })
            .OrderBy(a => a.Progress == AssignmentProgress.InProgress ? 0 : 1).ThenByDescending(a => a.ClosesAt ?? DateTimeOffset.MaxValue)
            .ThenBy(a => a.ExamTitle, StringComparer.CurrentCultureIgnoreCase).ToList();

        var audit = await AuditAsync(userId, assignmentIds, sessionIds, ct);
        return new StudentProfile(u.Id, u.UserName, u.DisplayName, u.Email, member.IsActive, u.IsActive, u.MfaEnabled, u.MustChangePassword,
            u.LastLoginUtc, member.CreatedUtc, await Gate.OwnsAccountAsync(userId, ct), views,
            integrity.Values.Sum(i => i.All), integrity.Values.Sum(i => i.Raised), audit);
    }

    /// <summary>The newest audit rows about this student: their account, their assignments and attempts, and their membership.</summary>
    private async Task<IReadOnlyList<ProfileAuditRow>> AuditAsync(Guid userId, List<Guid> assignmentIds, List<Guid> sessionIds, CancellationToken ct)
    {
        const int limit = 100;
        var db = Db;
        var org = Gate.Org;
        var userKey = userId.ToString();
        var assignmentKeys = assignmentIds.Select(i => i.ToString()).ToList();
        var events = await db.AuditEvents.AsNoTracking()
            .Where(e => e.OrganizationId == org && (
                (e.EntityType == "User" && e.EntityId == userKey)
                || (e.EntityType == "ExamAssignment" && e.EntityId != null && assignmentKeys.Contains(e.EntityId))
                || (e.SessionId != null && sessionIds.Contains(e.SessionId.Value))))
            .OrderByDescending(e => e.At).Take(limit)
            .Select(e => new { e.At, e.Action, e.ActorUserId, e.Reason })
            .ToListAsync(ct);
        var membership = await db.AuthOrganizationAudit.AsNoTracking()
            .Where(a => a.OrganizationId == org && a.SubjectUserId == userId)
            .OrderByDescending(a => a.TimestampUtc).Take(limit)
            .Select(a => new { a.TimestampUtc, a.Action, a.ActorUserId, a.Detail })
            .ToListAsync(ct);
        var actorIds = events.Select(e => e.ActorUserId).Concat(membership.Select(m => m.ActorUserId)).OfType<Guid>().Distinct().ToList();
        var names = await db.AuthUsers.AsNoTracking().Where(x => actorIds.Contains(x.Id))
            .Select(x => new { x.Id, x.UserName, x.DisplayName }).ToDictionaryAsync(x => x.Id, x => AdminPeople.NameOf(x.UserName, x.DisplayName), ct);
        string? Actor(Guid? id) => id is { } g ? names.GetValueOrDefault(g, "(removed account)") : "system";
        return events.Select(e => new ProfileAuditRow(e.At, e.Action, Actor(e.ActorUserId), e.Reason))
            .Concat(membership.Select(m => new ProfileAuditRow(new DateTimeOffset(DateTime.SpecifyKind(m.TimestampUtc, DateTimeKind.Utc)), m.Action, Actor(m.ActorUserId), m.Detail)))
            .OrderByDescending(r => r.At).Take(limit).ToList();
    }

    // ================================================================== changes

    /// <summary>Adds a student with a new account (the admin desk's rules: a new user name only, the password policy applies).</summary>
    public async Task<DeskResult> CreateAsync(StudentForm form, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Adding a student", "User", null, ct)) return new(false, AdminGate.DeniedMessage);
        var email = string.IsNullOrWhiteSpace(form.Email) ? null : form.Email.Trim();
        if (email is not null && !OrganizationDirectory.IsValidEmail(email)) return new(false, "That email is not an email address.");
        if (form.Name?.Trim().Length > StudentCsv.MaxNameLength) return new(false, $"Keep the name under {StudentCsv.MaxNameLength} characters.");
        return await Desk.AddStudentAsync(form.Name, form.UserName, email, form.Password, form.MustChange, ct);
    }

    /// <summary>Invites an email address as a student: they accept, choose their own password and sign in. Audited.</summary>
    public async Task<DeskResult> InviteAsync(string email, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Inviting a student", "Invitation", null, ct)) return new(false, AdminGate.DeniedMessage);
        var r = await Directory.InviteAsync(Gate.Org, email?.Trim() ?? "", OrgRoles.Student, Gate.Role, Gate.User, ct);
        if (!r.Ok) return new(false, r.Message);
        Gate.Audit("admin.student-invited", "Invitation", r.Id?.ToString());
        await Db.SaveChangesAsync(ct);
        return new(true, r.Message, r.Id);
    }

    public async Task<DeskResult> ResendInvitationAsync(Guid invitationId, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Resending an invitation", "Invitation", invitationId.ToString(), ct)) return new(false, AdminGate.DeniedMessage);
        var r = await Directory.ResendInvitationAsync(Gate.Org, invitationId, Gate.User, ct);
        if (!r.Ok) return new(false, r.Message);
        Gate.Audit("admin.invitation-resent", "Invitation", invitationId.ToString());
        await Db.SaveChangesAsync(ct);
        return new(true, r.Message, invitationId);
    }

    public async Task<DeskResult> RevokeInvitationAsync(Guid invitationId, string? reason, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Withdrawing an invitation", "Invitation", invitationId.ToString(), ct)) return new(false, AdminGate.DeniedMessage);
        var r = await Directory.RevokeInvitationAsync(Gate.Org, invitationId, Gate.User, ct);
        if (!r.Ok) return new(false, r.Message);
        Gate.Audit("admin.invitation-revoked", "Invitation", invitationId.ToString(), reason: AdminPeople.Reason(reason));
        await Db.SaveChangesAsync(ct);
        return new(true, r.Message, invitationId);
    }

    /// <summary>Changes a student's name and email. Only for an account that belongs to this organization alone. Audited.</summary>
    public async Task<DeskResult> UpdateAsync(Guid userId, string? name, string? email, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Editing a student", "User", userId.ToString(), ct)) return new(false, AdminGate.DeniedMessage);
        var db = Db;
        var org = Gate.Org;
        if (!await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == org && m.UserId == userId && m.Role == OrgRoles.Student, ct))
            return new(false, "That person is not a student of this organization.");
        if (!await Gate.OwnsAccountAsync(userId, ct))
            return new(false, "This account also belongs to another organization or is staff somewhere, so only its owner can change its name or email.");
        var newName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var newEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (newName is { Length: > StudentCsv.MaxNameLength }) return new(false, $"Keep the name under {StudentCsv.MaxNameLength} characters.");
        if (newEmail is not null && !OrganizationDirectory.IsValidEmail(newEmail)) return new(false, "That email is not an email address.");
        var before = await db.AuthUsers.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.DisplayName, u.Email }).FirstAsync(ct);
        if (before.DisplayName == newName && string.Equals(before.Email, newEmail, StringComparison.OrdinalIgnoreCase)) return new(true, "No change.", userId);
        var r = await sp.GetRequiredService<IUserAdminService>().UpdateProfileAsync(userId, newEmail, newName, ct);
        if (!r.Ok) return new(false, r.Error ?? "The change was not saved.");
        Gate.Audit("admin.student-edited", "User", userId.ToString(), before, new { DisplayName = newName, Email = newEmail });
        await db.SaveChangesAsync(ct);
        return new(true, "Saved.", userId);
    }

    /// <summary>
    /// Deactivates (with a reason) or reactivates a student's membership. A deactivated student can't sign in to this organization;
    /// their assignments, attempts and results are kept, and reactivating gives everything back. Audited, with the reason.
    /// </summary>
    public async Task<DeskResult> SetActiveAsync(Guid userId, bool active, string? reason, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync(active ? "Reactivating a student" : "Deactivating a student", "User", userId.ToString(), ct))
            return new(false, AdminGate.DeniedMessage);
        var why = AdminPeople.Reason(reason);
        if (!active && why is null) return new(false, "Say why the student is being deactivated; the reason is kept in the audit log.");
        var db = Db;
        var org = Gate.Org;
        var m = await db.OrganizationMembers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == org && x.UserId == userId && x.Role == OrgRoles.Student, ct);
        if (m is null) return new(false, "That person is not a student of this organization.");
        if (m.IsActive == active) return new(true, active ? "Already active." : "Already deactivated.", userId);
        var live = !active && await db.ExamSessions.AnyAsync(s => s.OrganizationId == org && s.StudentUserId == userId
                                                              && (s.State == SessionState.Running || s.State == SessionState.Paused), ct);
        var r = await Directory.SetMemberActiveAsync(org, m.Id, active, ct, Gate.Role, Gate.User);
        if (!r.Ok) return new(false, r.Message);
        Gate.Audit(active ? "admin.student-reactivated" : "admin.student-deactivated", "User", userId.ToString(),
            new { Active = !active }, new { Active = active }, why);
        await db.SaveChangesAsync(ct);
        return new(true, active
            ? "Reactivated. They can sign in again and see their exams."
            : "Deactivated. They are signed out within about a minute; their attempts and results are kept." +
              (live ? " An exam they had open stops with their sign-in." : ""), userId);
    }

    /// <summary>Sets a new password (the admin desk's rule: a student of this organization only, never staff). Audited.</summary>
    public async Task<DeskResult> ResetPasswordAsync(Guid userId, string password, bool mustChange, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Resetting a student's password", "User", userId.ToString(), ct)) return new(false, AdminGate.DeniedMessage);
        return await Desk.ResetPasswordAsync(userId, password, mustChange, ct);
    }

    // ================================================================== CSV import

    /// <summary>The dry run: what importing <paramref name="csv"/> would do, row by row. Nothing is saved.</summary>
    public async Task<ImportReport> PreviewImportAsync(string? csv, CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var file = StudentCsv.Parse(csv);
        if (!file.Usable) return new ImportReport(false, file.FileErrors, file.IgnoredColumns, []);
        return new ImportReport(false, [], file.IgnoredColumns, await PlanAsync(file, ct));
    }

    /// <summary>
    /// Applies the import: plans again against the data as it is now (so a second run of the same file changes nothing), then does
    /// each row on its own. A row that fails is reported and the others still go through. One summary audit row, plus each change's own.
    /// </summary>
    public async Task<ImportReport> ApplyImportAsync(string? csv, CancellationToken ct = default)
    {
        if (!await Gate.RequireAsync("Importing students", "User", null, ct)) return new ImportReport(false, [AdminGate.DeniedMessage], [], []);
        var file = StudentCsv.Parse(csv);
        if (!file.Usable) return new ImportReport(false, file.FileErrors, file.IgnoredColumns, []);
        var plan = await PlanAsync(file, ct);
        var byLine = file.Rows.ToDictionary(r => r.Line);
        var done = new List<ImportRowResult>(plan.Count);
        var users = sp.GetRequiredService<IUserAdminService>();
        foreach (var p in plan)
        {
            var row = byLine[p.Line];
            ImportRowResult result;
            try
            {
                result = p.Action switch
                {
                    ImportAction.Create => await ApplyCreateAsync(p, row, ct),
                    ImportAction.Invite => await ApplyInviteAsync(p, ct),
                    ImportAction.UpdateName => await ApplyNameAsync(p, row, users, ct),
                    _ => p,
                };
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                Db.ChangeTracker.Clear();
                result = p with { Action = ImportAction.Error, Message = "Not imported: the change could not be saved. Try this row again." };
            }
            done.Add(result);
        }
        var report = new ImportReport(true, [], file.IgnoredColumns, done);
        Gate.Audit("admin.students-imported", "User", null, after: new
        {
            Rows = done.Count,
            Created = done.Count(r => r.Action == ImportAction.Create && r.Done),
            Invited = done.Count(r => r.Action == ImportAction.Invite && r.Done),
            Renamed = done.Count(r => r.Action == ImportAction.UpdateName && r.Done),
            Unchanged = report.Count(ImportAction.Unchanged),
            Skipped = report.Count(ImportAction.Skip),
            Errors = report.Errors,
        });
        await Db.SaveChangesAsync(ct);
        return report;
    }

    private async Task<ImportRowResult> ApplyCreateAsync(ImportRowResult p, StudentCsvRow row, CancellationToken ct)
    {
        var r = await Desk.AddStudentAsync(row.Name, UserNameFor(row), row.Email, row.Password!, mustChange: true, ct);
        return r.Ok ? p with { Message = $"Created as '{UserNameFor(row)}'; they choose their own password at first sign-in.", Done = true }
                    : p with { Action = ImportAction.Error, Message = r.Message };
    }

    private async Task<ImportRowResult> ApplyInviteAsync(ImportRowResult p, CancellationToken ct)
    {
        var r = await Directory.InviteAsync(Gate.Org, p.Email, OrgRoles.Student, Gate.Role, Gate.User, ct);
        if (!r.Ok) return p with { Action = ImportAction.Error, Message = r.Message };
        Gate.Audit("admin.student-invited", "Invitation", r.Id?.ToString(), reason: "CSV import");
        await Db.SaveChangesAsync(ct);
        return p with { Message = "Invitation sent.", Done = true };
    }

    private async Task<ImportRowResult> ApplyNameAsync(ImportRowResult p, StudentCsvRow row, IUserAdminService users, CancellationToken ct)
    {
        var db = Db;
        var org = Gate.Org;
        var target = await (from m in db.OrganizationMembers.AsNoTracking()
                            join u in db.AuthUsers.AsNoTracking() on m.UserId equals u.Id
                            where m.OrganizationId == org && m.Role == OrgRoles.Student && m.IsActive && u.Email != null && u.Email == row.Email
                            select new { u.Id, u.Email, u.DisplayName }).FirstOrDefaultAsync(ct);
        if (target is not null && !await Gate.OwnsAccountAsync(target.Id, ct))
            return p with { Action = ImportAction.Skip, Message = "This account also belongs elsewhere, so its name is not changed here." };
        if (target is null) return p with { Action = ImportAction.Error, Message = "The student was not found any more." };
        var r = await users.UpdateProfileAsync(target.Id, target.Email, row.Name, ct);
        if (!r.Ok) return p with { Action = ImportAction.Error, Message = r.Error ?? "The name was not changed." };
        Gate.Audit("admin.student-edited", "User", target.Id.ToString(), new { target.DisplayName }, new { DisplayName = row.Name }, "CSV import");
        await db.SaveChangesAsync(ct);
        return p with { Message = "Name updated.", Done = true };
    }

    /// <summary>A row's user name: the one given, else the email address itself.</summary>
    private static string UserNameFor(StudentCsvRow row) => row.UserName ?? row.Email;

    /// <summary>What each row would do against the organization as it is now.</summary>
    private async Task<IReadOnlyList<ImportRowResult>> PlanAsync(StudentCsvFile file, CancellationToken ct)
    {
        var db = Db;
        var org = Gate.Org;
        var emails = file.Rows.Where(r => r.Error is null).Select(r => r.Email).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Accounts with these emails (any organization), their membership here, and the user names asked for.
        var accounts = (await db.AuthUsers.AsNoTracking().Where(u => u.Email != null && emails.Contains(u.Email))
                .Select(u => new { u.Id, u.Email, u.DisplayName }).ToListAsync(ct))
            .GroupBy(u => u.Email!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var accountIds = accounts.Values.SelectMany(l => l).Select(a => a.Id).ToList();
        var members = await db.OrganizationMembers.AsNoTracking().Where(m => m.OrganizationId == org && accountIds.Contains(m.UserId))
            .Select(m => new { m.UserId, m.Role, m.IsActive }).ToListAsync(ct);
        var owned = new Dictionary<Guid, bool>();
        foreach (var id in members.Where(m => m.Role == OrgRoles.Student).Select(m => m.UserId).Distinct())
            owned[id] = await Gate.OwnsAccountAsync(id, ct);
        var wantedNames = file.Rows.Where(r => r.Error is null && r.Password is not null).Select(r => IUserStore.Normalize(UserNameFor(r))).Distinct().ToList();
        var takenNames = (await db.AuthUsers.AsNoTracking().Where(u => wantedNames.Contains(u.NormalizedUserName)).Select(u => u.NormalizedUserName).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var invited = (await PendingInvitationsAsync(ct)).Select(i => i.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var policy = sp.GetService<IPasswordPolicy>();
        var namesInFile = new Dictionary<string, int>(StringComparer.Ordinal);

        var plan = new List<ImportRowResult>(file.Rows.Count);
        foreach (var row in file.Rows)
        {
            if (row.Error is not null) { plan.Add(new(row.Line, row.Email, row.Name, ImportAction.Error, row.Error)); continue; }
            var mine = accounts.GetValueOrDefault(row.Email) ?? [];
            var here = mine.Select(a => (Account: a, Member: members.FirstOrDefault(m => m.UserId == a.Id))).FirstOrDefault(x => x.Member is not null);
            if (here.Member is { } m)
            {
                var a = here.Account;
                if (m.Role != OrgRoles.Student)
                    plan.Add(new(row.Line, row.Email, row.Name, ImportAction.Error, $"Already a member of this organization as {m.Role}; students are imported here, staff on the members page."));
                else if (!m.IsActive)
                    plan.Add(new(row.Line, row.Email, row.Name, ImportAction.Skip, "A deactivated student. Reactivate them from their profile if they should come back."));
                else if (row.Name is not null && row.Name != a.DisplayName && owned.GetValueOrDefault(a.Id))
                    plan.Add(new(row.Line, row.Email, row.Name, ImportAction.UpdateName, $"Already a student; the name changes from \"{a.DisplayName ?? "(none)"}\" to \"{row.Name}\"."));
                else
                    plan.Add(new(row.Line, row.Email, row.Name, ImportAction.Unchanged, "Already a student."));
                continue;
            }
            if (invited.Contains(row.Email))
            {
                plan.Add(new(row.Line, row.Email, row.Name, ImportAction.Unchanged, "Already invited. Resend the invitation from the Invited list if it was lost."));
                continue;
            }
            if (row.Password is null || mine.Count > 0)
            {
                // No password, or the address already has a Gadfly account elsewhere: an organization never pulls an outside account in;
                // the person accepts an invitation themselves (and the plan never says whether the account exists).
                plan.Add(new(row.Line, row.Email, row.Name, ImportAction.Invite,
                    row.Password is null ? "Will be invited by email; they choose their own password." : "Will be invited by email (this address can't be given a new account here)."));
                continue;
            }
            var userName = UserNameFor(row);
            var normalized = IUserStore.Normalize(userName);
            if (takenNames.Contains(normalized))
            {
                plan.Add(new(row.Line, row.Email, row.Name, ImportAction.Error, $"The user name '{userName}' is already taken. Give another in the username column."));
                continue;
            }
            if (namesInFile.TryGetValue(normalized, out var firstLine))
            {
                plan.Add(new(row.Line, row.Email, row.Name, ImportAction.Error, $"The user name '{userName}' is also on line {firstLine}."));
                continue;
            }
            namesInFile[normalized] = row.Line;
            if (policy is not null && await policy.ValidateAsync(row.Password, null, ct) is { Ok: false } bad)
            {
                plan.Add(new(row.Line, row.Email, row.Name, ImportAction.Error, "The password is not accepted: " + (bad.Reason ?? "too weak.")));
                continue;
            }
            plan.Add(new(row.Line, row.Email, row.Name, ImportAction.Create, $"Will get an account '{userName}' with the given password, to change at first sign-in."));
        }
        return plan;
    }

    public static string Describe(ImportAction a) => a switch
    {
        ImportAction.Create => "Create account",
        ImportAction.Invite => "Invite by email",
        ImportAction.UpdateName => "Update name",
        ImportAction.Unchanged => "No change",
        ImportAction.Skip => "Skipped",
        _ => "Error",
    };
}
