using System.Globalization;
using System.Security.Claims;
using System.Text;
using Gadfly.Assessment;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Exams;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Desk;

/// <summary>
/// Who may open the admin desk (GAD-T246, hardened GAD-T247): the desk is on (<see cref="EnabledSetting"/>), the caller
/// has signed in with MFA (the same rule as every staff policy, with no exception), and holds the desk's Admin role in
/// the active organization, either as that organization's Owner or Admin or as an account named in
/// <see cref="AdminsSetting"/>, which holds the desk Admin role alongside whatever membership role it has (the owner's
/// 'ryan' account is a Student there, so it can sit exams and reset them from the app).
/// </summary>
public static class DeskAccess
{
    public const string Policy = "gadfly:admin-desk";
    public const string EnabledSetting = "Gadfly:AdminDesk:Enabled";
    public const string AdminsSetting = "Gadfly:AdminDesk:Admins";
    /// <summary>The organization id where <see cref="AdminsSetting"/> accounts hold the desk Admin role.</summary>
    public const string OrganizationSetting = "Gadfly:AdminDesk:Organization";

    public static bool IsEnabled(IConfiguration config) =>
        string.Equals(config[EnabledSetting], "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>User names granted the desk Admin role (in one organization only: see <see cref="IsDeskAdmin"/>).</summary>
    public static IReadOnlyList<string> ListedAdmins(IConfiguration config) =>
        config.GetSection(AdminsSetting).GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList();

    /// <summary>True when <paramref name="user"/> may use the desk on <paramref name="tenant"/>'s active organization.</summary>
    public static bool IsDeskAdmin(ClaimsPrincipal? user, ITenantContext tenant, IConfiguration config) =>
        Allowed(user, tenant, config, requireMfa: true);

    /// <summary>
    /// True when <paramref name="user"/> would be a desk admin but hasn't signed in with two-step verification yet
    /// (GAD-T247), so the home page can say why the desk is missing and where to set it up instead of hiding it silently.
    /// </summary>
    public static bool NeedsMfaForDesk(ClaimsPrincipal? user, ITenantContext tenant, IConfiguration config) =>
        OrgRoleHandler.MfaRequired && user is not null && !user.HasClaim(MaClaims.Amr, "mfa")
        && Allowed(user, tenant, config, requireMfa: false);

    private static bool Allowed(ClaimsPrincipal? user, ITenantContext tenant, IConfiguration config, bool requireMfa)
    {
        if (!IsEnabled(config) || user?.Identity?.IsAuthenticated != true) return false;
        if (tenant.IsSystem || tenant.OrganizationId is null || tenant.UserId is null || tenant.Role is null or OrgRoles.Api) return false;
        // Every desk admin needs the same MFA as every other staff policy (GAD-T247): Owners and Admins already did; a
        // listed account used to skip it (the owner's earlier request, "admin mode now, hide it under security later"),
        // which GAD-T292's help-center agent flagged as a real gap now that the desk is reachable before real students
        // use it. There is no separate enrollment prompt for a listed account that isn't also an Owner/Admin/Instructor/
        // Examiner/SecondReviewer (those roles alone force MFA enrollment at sign-in): it can enroll any time at
        // /account/mfa/setup, and simply can't open the desk until it has.
        if (requireMfa && OrgRoleHandler.MfaRequired && !user.HasClaim(MaClaims.Amr, "mfa")) return false;
        if (tenant.Role is OrgRoles.Owner or OrgRoles.Admin) return true;
        var name = user.Identity?.Name;
        return name is { Length: > 0 } && ListedAdmins(config).Contains(name, StringComparer.OrdinalIgnoreCase)
            && IsListedAdminOrganization(tenant, config);
    }

    /// <summary>
    /// Where a listed account holds the desk Admin role: only the organization named in <see cref="OrganizationSetting"/>, or,
    /// when none is configured, only while the account belongs to exactly one organization (the active one), so the grant never
    /// follows the account into another organization it joins.
    /// </summary>
    private static bool IsListedAdminOrganization(ITenantContext tenant, IConfiguration config)
    {
        if (Guid.TryParse(config[OrganizationSetting], out var configured)) return tenant.OrganizationId == configured;
        var orgs = tenant.Memberships.Select(m => m.OrganizationId).Distinct().ToList();
        return orgs.Count == 1 && orgs[0] == tenant.OrganizationId;
    }

    /// <summary>The tenant the desk acts as: the caller, in the same organization, with the Admin role.</summary>
    public static TenantSnapshot Elevated(ITenantContext tenant) => TenantSnapshot.Of(tenant) with { Role = OrgRoles.Admin };

    /// <summary>Registers the desk's policy and its handler.</summary>
    public static IServiceCollection AddAdminDesk(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, DeskAdminHandler>();
        services.Configure<AuthorizationOptions>(o =>
            o.AddPolicy(Policy, p => p.RequireAuthenticatedUser().AddRequirements(new DeskAdminRequirement())));
        return services;
    }
}

public sealed class DeskAdminRequirement : IAuthorizationRequirement;

public sealed class DeskAdminHandler(ITenantContext tenant, IConfiguration config) : AuthorizationHandler<DeskAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DeskAdminRequirement requirement)
    {
        if (DeskAccess.IsDeskAdmin(context.User, tenant, config)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed record DeskResult(bool Ok, string Message, Guid? Id = null);

public sealed record DeskExam(Guid Id, string Title, string Subject, string Topic, ExamStatus Status, bool Voice, Guid CollectionId,
    string CollectionTitle, ProcessingStatus CollectionStatus, string? Processing, int Objectives, int Assigned, string? Instructions,
    int DurationMinutes, DateTimeOffset CreatedAt)
{
    /// <summary>A draft whose notes are ready but which has no objectives or isn't published yet: one click finishes it.</summary>
    public bool CanFinish => Status == ExamStatus.Draft && CollectionStatus == ProcessingStatus.Ready;
}

public sealed record DeskStudent(Guid UserId, string UserName, string? DisplayName, string? Email, bool Active, int Assigned, DateTime? LastLogin);

public sealed record DeskAssignment(Guid Id, Guid ExamId, string ExamTitle, bool Voice, Guid StudentUserId, string StudentName, int AttemptsAllowed,
    int AttemptsUsed, string Status, Guid? LatestSessionId, string? LatestScore, DateTimeOffset AssignedAt, bool TypedAnswers = false);

public sealed record DeskTurn(int Index, string ConceptKey, string Question, string? Answer, string? AnswerMode);

/// <summary>What the desk's "new exam" form collects.</summary>
public sealed class DeskNewExam
{
    public string Subject { get; set; } = "";
    public string Topic { get; set; } = "";
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public string? Instructions { get; set; }
    public bool VoiceOnly { get; set; } = true;
    public int DurationMinutes { get; set; } = 20;
}

/// <summary>What the desk's exam editor changes.</summary>
public sealed class DeskExamEdit
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Topic { get; set; } = "";
    public string? Instructions { get; set; }
    public bool VoiceOnly { get; set; }
    public int DurationMinutes { get; set; } = 20;
}

/// <summary>One uploaded notes file, read by the page and handed over as a stream.</summary>
public sealed record DeskUpload(string Name, long Size, Func<Stream> Open);

/// <summary>
/// The admin desk (GAD-T246): exams, students and the bridge between them (assignments), for the owner to set up, test and
/// reset from one page. Built over the existing services (assessments, source library, directory, exam assignments) so their
/// rules and audit trail still apply; it runs in a scope whose tenant is the caller with the Admin role
/// (<see cref="DeskAccess.Elevated"/>), which the page opens only after <see cref="DeskAccess.IsDeskAdmin"/> passed.
/// </summary>
public sealed class DeskService(IServiceProvider sp)
{
    private GadflyDbContext Db => sp.GetRequiredService<GadflyDbContext>();
    private ITenantContext Tenant => sp.GetRequiredService<ITenantContext>();
    private AssessmentService Assessments => sp.GetRequiredService<AssessmentService>();
    private SourceLibraryService Library => sp.GetRequiredService<SourceLibraryService>();
    private IOrganizationDirectory Directory => sp.GetRequiredService<IOrganizationDirectory>();
    private TimeProvider Clock => sp.GetRequiredService<TimeProvider>();

    private Guid Org => Tenant.OrganizationId ?? throw new InvalidOperationException("No active organization.");
    private Guid User => Tenant.UserId ?? throw new InvalidOperationException("No signed-in user.");

    public const int DefaultQuestionLimit = 12;

    // ================================================================== exams

    public async Task<IReadOnlyList<DeskExam>> ExamsAsync(CancellationToken ct = default)
    {
        var db = Db;
        var org = Org;
        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org)
            .OrderBy(e => e.Status).ThenByDescending(e => e.CreatedAt).ToListAsync(ct);
        var collections = await db.SourceCollections.AsNoTracking().Where(c => c.OrganizationId == org)
            .Select(c => new { c.Id, c.Title, c.Status }).ToDictionaryAsync(c => c.Id, ct);
        var objectives = await db.LearningObjectives.AsNoTracking().Where(o => o.OrganizationId == org)
            .GroupBy(o => o.ExamId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var assigned = await db.ExamAssignments.AsNoTracking().Where(a => a.OrganizationId == org)
            .GroupBy(a => a.ExamId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var result = new List<DeskExam>(exams.Count);
        foreach (var e in exams)
        {
            var c = collections.GetValueOrDefault(e.CollectionId);
            string? processing = null;
            if (c is not null && c.Status != ProcessingStatus.Ready && e.Status == ExamStatus.Draft)
            {
                var job = await Library.LatestJobAsync(org, c.Id, ct);
                processing = job is null ? c.Status.ToString()
                    : job.IsActive ? $"Reading the notes… {job.Progress * 100:0}%{(job.Message is { Length: > 0 } m ? " · " + m : "")}"
                    : job.Error is { Length: > 0 } err ? "Failed: " + err : job.State.ToString();
            }
            var policy = ExamService.ReadPolicy(e.PolicyJson);
            result.Add(new DeskExam(e.Id, e.Title, e.Subject, e.Topic, e.Status, e.Mode == ExamMode.Voice, e.CollectionId,
                c?.Title ?? "(missing notes)", c?.Status ?? ProcessingStatus.Failed, processing, objectives.GetValueOrDefault(e.Id),
                assigned.GetValueOrDefault(e.Id), e.Instructions, (int)Math.Round(policy.TimeBudget.TotalMinutes), e.CreatedAt));
        }
        return result;
    }

    /// <summary>
    /// Creates an exam from a subject, a topic and source notes (pasted, uploaded or both): a course for the subject (reused when
    /// one has that title), a notes collection, the notes queued for the panel to read, and a draft assessment on them. When the
    /// panel has read them, <see cref="FinishExamAsync"/> turns its concepts into objectives and publishes.
    /// </summary>
    public async Task<DeskResult> CreateExamAsync(DeskNewExam form, IReadOnlyList<DeskUpload> uploads, CancellationToken ct = default)
    {
        var subject = form.Subject?.Trim() ?? "";
        var topic = form.Topic?.Trim() ?? "";
        if (subject.Length == 0 || topic.Length == 0) return new(false, "Give the exam a subject and a topic.");
        if (subject.Length > 200 || topic.Length > 200) return new(false, "Keep the subject and topic under 200 characters each.");
        var notes = form.Notes?.Trim() ?? "";
        if (notes.Length == 0 && uploads.Count == 0) return new(false, "Paste the source notes or upload a notes file.");
        var title = string.IsNullOrWhiteSpace(form.Title) ? $"{subject}: {topic}" : form.Title.Trim();
        if (title.Length > 200) return new(false, "Keep the title under 200 characters.");
        if (form.DurationMinutes is < AssessmentService.MinDurationMinutes or > AssessmentService.MaxDurationMinutes)
            return new(false, $"The duration must be {AssessmentService.MinDurationMinutes} to {AssessmentService.MaxDurationMinutes} minutes.");
        // The uploads are checked before anything is saved, so a bad file leaves no course or notes collection behind.
        if (Encoding.UTF8.GetByteCount(notes) > SourceLibraryService.MaxFileBytes)
            return new(false, $"The pasted notes are larger than the {SourceLibraryService.MaxFileBytes / (1024 * 1024)} MB limit.");
        foreach (var u in uploads)
        {
            if (SourceLibraryService.SanitizeFileName(u.Name) is null)
                return new(false, $"\"{u.Name}\": only {string.Join(", ", SourceLibraryService.AcceptedExtensions)} files can be read.");
            if (u.Size > SourceLibraryService.MaxFileBytes)
                return new(false, $"\"{u.Name}\" is larger than the {SourceLibraryService.MaxFileBytes / (1024 * 1024)} MB limit.");
            if (u.Size <= 0) return new(false, $"\"{u.Name}\" is empty.");
        }

        var db = Db;
        var org = Org;
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.OrganizationId == org && c.Title == subject && !c.IsArchived, ct);
        Guid courseId;
        if (course is not null) courseId = course.Id;
        else
        {
            var made = await Assessments.SaveCourseAsync(new CourseDraft { Title = subject, Subject = subject, Description = "Created from the admin desk." }, ct);
            if (!made.Ok || made.Id is null) return new(false, made.Message);
            courseId = made.Id.Value;
        }

        var collectionTitle = await UniqueCollectionTitleAsync($"{title} notes", ct);
        var collectionId = await Library.CreateCollectionAsync(org, User, collectionTitle, ct);
        var saved = new List<string>();
        if (notes.Length > 0)
        {
            var bytes = Encoding.UTF8.GetBytes(notes);
            await using var ms = new MemoryStream(bytes);
            var r = await Library.SaveUploadAsync(org, collectionId, "notes.md", bytes.Length, ms, ct);
            if (!r.Ok) return new(false, r.Message);
            saved.Add("pasted notes");
        }
        foreach (var u in uploads)
        {
            await using var s = u.Open();
            var r = await Library.SaveUploadAsync(org, collectionId, u.Name, u.Size, s, ct);
            if (!r.Ok) return new(false, r.Message);
            saved.Add(r.FileName ?? u.Name);
        }
        var queued = await Library.EnqueueProcessingAsync(org, User, collectionId, rebuildConceptMap: false, ct);
        if (!queued.Ok) return new(false, queued.Message);

        var details = new AssessmentDetails
        {
            CourseId = courseId, CollectionId = collectionId, Title = title, Subject = subject, Topic = topic,
            Mode = AssessmentMode.Examination, DurationMinutes = form.DurationMinutes, QuestionLimit = DefaultQuestionLimit,
            Instructions = string.IsNullOrWhiteSpace(form.Instructions) ? null : form.Instructions.Trim(),
        };
        var created = await Assessments.SaveDetailsAsync(details, ct);
        if (!created.Ok || created.Id is null) return new(false, created.Message);
        if (form.VoiceOnly) await SetModeAsync(created.Id.Value, voice: true, ct);
        return new(true, $"Created \"{title}\" from {string.Join(", ", saved)}. The panel is reading the notes now; when they are ready, " +
                         "press \"Build objectives & publish\".", created.Id);
    }

    /// <summary>Turns every concept the panel found in the notes into an objective (each with a starting criterion) and publishes.</summary>
    public async Task<DeskResult> FinishExamAsync(Guid examId, CancellationToken ct = default)
    {
        var svc = Assessments;
        var suggestions = await svc.SuggestionsAsync(examId, ct);
        if (suggestions.Count > 0)
        {
            var added = await svc.AddSuggestedAsync(examId, suggestions.Select(s => s.Key).ToList(), ct);
            if (!added.Ok) return new(false, added.Message);
        }
        var published = await svc.PublishAsync(examId, ct);
        return new(published.Ok, published.Ok ? $"Published with the objectives the panel built from the notes{(suggestions.Count > 0 ? $" ({suggestions.Count} added)" : "")}. Assign it on the Student exams tab." : published.Message, examId);
    }

    public async Task<DeskExamEdit?> GetEditAsync(Guid examId, CancellationToken ct = default)
    {
        var org = Org;
        var e = await Db.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == examId && x.OrganizationId == org, ct);
        return e is null ? null : new DeskExamEdit
        {
            Id = e.Id, Title = e.Title, Subject = e.Subject, Topic = e.Topic, Instructions = e.Instructions, VoiceOnly = e.Mode == ExamMode.Voice,
            DurationMinutes = (int)Math.Round(ExamService.ReadPolicy(e.PolicyJson).TimeBudget.TotalMinutes),
        };
    }

    /// <summary>
    /// Edits an exam. A draft goes through the assessment rules (all details, duration included). A published or archived exam
    /// keeps its frozen policy: only its wording (title, subject, topic, instructions) and voice mode change, and that is audited.
    /// </summary>
    public async Task<DeskResult> EditExamAsync(DeskExamEdit edit, CancellationToken ct = default)
    {
        var db = Db;
        var org = Org;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == edit.Id && x.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        var title = edit.Title?.Trim() ?? "";
        if (title.Length is 0 or > 200) return new(false, "Give the exam a title under 200 characters.");
        if ((edit.Subject?.Trim().Length ?? 0) > 200 || (edit.Topic?.Trim().Length ?? 0) > 200) return new(false, "Keep the subject and topic under 200 characters each.");
        if (edit.Instructions?.Length > 4000) return new(false, "Keep the instructions under 4000 characters.");

        if (exam.Status == ExamStatus.Draft)
        {
            var view = await Assessments.GetAsync(exam.Id, ct);
            if (view is null) return new(false, "That exam could not be opened.");
            var d = view.Details;
            d.Title = title;
            d.Subject = edit.Subject?.Trim() ?? "";
            d.Topic = edit.Topic?.Trim() ?? "";
            d.Instructions = edit.Instructions;
            d.DurationMinutes = edit.DurationMinutes;
            var r = await Assessments.SaveDetailsAsync(d, ct);
            if (!r.Ok) return new(false, r.Message);
            await SetModeAsync(exam.Id, edit.VoiceOnly, ct);
            return new(true, "Saved.", exam.Id);
        }

        var tracked = await db.Exams.FirstAsync(x => x.Id == edit.Id && x.OrganizationId == org, ct);
        var before = new { tracked.Title, tracked.Subject, tracked.Topic, tracked.Instructions, Mode = tracked.Mode.ToString() };
        tracked.Title = title;
        tracked.Subject = edit.Subject?.Trim() ?? "";
        tracked.Topic = edit.Topic?.Trim() ?? "";
        tracked.Instructions = string.IsNullOrWhiteSpace(edit.Instructions) ? null : edit.Instructions.Trim();
        tracked.Mode = edit.VoiceOnly ? ExamMode.Voice : ExamMode.Text;
        Assessments.Guard.Audit("assessment.desk-edited", "Exam", tracked.Id.ToString(), before,
            new { tracked.Title, tracked.Subject, tracked.Topic, tracked.Instructions, Mode = tracked.Mode.ToString() },
            reason: "Edited from the admin desk; the published policy (objectives, rubric, duration) is unchanged.");
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var durationNote = edit.DurationMinutes != (int)Math.Round(ExamService.ReadPolicy(exam.PolicyJson).TimeBudget.TotalMinutes)
            ? " The duration of a published exam is frozen, so it was not changed." : "";
        return new(true, "Saved." + durationNote, exam.Id);
    }

    public Task<AssessmentResult> ArchiveExamAsync(Guid examId, CancellationToken ct = default) => Assessments.ArchiveAsync(examId, ct);

    private async Task SetModeAsync(Guid examId, bool voice, CancellationToken ct)
    {
        var db = Db;
        var org = Org;
        var exam = await db.Exams.FirstAsync(x => x.Id == examId && x.OrganizationId == org, ct);
        var mode = voice ? ExamMode.Voice : ExamMode.Text;
        if (exam.Mode == mode) return;
        var before = exam.Mode;
        exam.Mode = mode;
        Assessments.Guard.Audit("assessment.mode-changed", "Exam", exam.Id.ToString(), new { Mode = before.ToString() }, new { Mode = mode.ToString() },
            reason: "Set from the admin desk.");
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task<string> UniqueCollectionTitleAsync(string wanted, CancellationToken ct)
    {
        var org = Org;
        var titles = (await Db.SourceCollections.AsNoTracking().Where(c => c.OrganizationId == org && c.Title.StartsWith(wanted))
            .Select(c => c.Title).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!titles.Contains(wanted)) return wanted;
        for (var i = 2; ; i++)
            if (!titles.Contains($"{wanted} ({i})")) return $"{wanted} ({i})";
    }

    // ================================================================== students

    public async Task<IReadOnlyList<DeskStudent>> StudentsAsync(CancellationToken ct = default)
    {
        var db = Db;
        var org = Org;
        var members = (await Directory.MembersAsync(org, ct)).Where(m => m.Member.Role == OrgRoles.Student).ToList();
        var ids = members.Select(m => m.Member.UserId).ToList();
        var accounts = await db.AuthUsers.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.LastLoginUtc }).ToDictionaryAsync(u => u.Id, ct);
        var assigned = await db.ExamAssignments.AsNoTracking().Where(a => a.OrganizationId == org)
            .GroupBy(a => a.StudentUserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return members.Select(m => new DeskStudent(m.Member.UserId, m.UserName, accounts.GetValueOrDefault(m.Member.UserId)?.DisplayName, m.Email,
                m.Member.IsActive, assigned.GetValueOrDefault(m.Member.UserId), accounts.GetValueOrDefault(m.Member.UserId)?.LastLoginUtc))
            .OrderBy(s => !s.Active).ThenBy(s => s.UserName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Adds a student with a new account: a name, a user name and the password the admin chose (by default they keep it; ticking
    /// <paramref name="mustChange"/> makes them pick their own at first sign-in). An existing user name is refused, so the desk never
    /// takes over or changes the role of an account that already exists.
    /// </summary>
    public async Task<DeskResult> AddStudentAsync(string? name, string userName, string? email, string password, bool mustChange, CancellationToken ct = default)
    {
        userName = userName?.Trim() ?? "";
        if (userName.Length == 0) return new(false, "Give a user name.");
        if (string.IsNullOrEmpty(password)) return new(false, "Give a password.");
        var db = Db;
        var normalized = IUserStore.Normalize(userName);
        if (await db.AuthUsers.AnyAsync(u => u.NormalizedUserName == normalized, ct))
            return new(false, $"The user name '{userName}' is already taken. Choose another.");
        var org = Org;
        var r = await Directory.AddMemberAsync(org, userName, string.IsNullOrWhiteSpace(email) ? null : email.Trim(), OrgRoles.Student, password, ct,
            actingRole: OrgRoles.Admin, actorUserId: User);
        if (!r.Ok || r.Id is not { } userId) return new(false, r.Message);
        var users = sp.GetRequiredService<IUserAdminService>();
        if (!string.IsNullOrWhiteSpace(name))
            await users.UpdateProfileAsync(userId, string.IsNullOrWhiteSpace(email) ? null : email.Trim(), name.Trim(), ct);
        if (!mustChange)
            await db.AuthUsers.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.MustChangePassword, false), ct);
        Audit("desk.student-added", "User", userId.ToString(), after: new { userName, name, mustChange });
        await db.SaveChangesAsync(ct);
        return new(true, $"Added {userName}. They sign in with that user name and password{(mustChange ? ", then choose their own" : "")}.", userId);
    }

    /// <summary>
    /// Sets a new password for a student of this organization. Refused for an account that holds a staff role anywhere or is
    /// platform staff: the desk resets students, never another administrator.
    /// </summary>
    public async Task<DeskResult> ResetPasswordAsync(Guid userId, string password, bool mustChange, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password)) return new(false, "Give the new password.");
        var db = Db;
        var org = Org;
        if (!await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == org && m.UserId == userId && m.Role == OrgRoles.Student && m.IsActive, ct))
            return new(false, "That person is not an active student of this organization.");
        // Only an account that is nothing but an active student here: a membership anywhere else (any role, active or not), a
        // removed membership here, or platform staff means another organization or administrator owns the account too.
        if (await db.OrganizationMembers.IgnoreQueryFilters()
                .AnyAsync(m => m.UserId == userId && !(m.OrganizationId == org && m.Role == OrgRoles.Student && m.IsActive), ct)
            || await db.AuthUsers.AnyAsync(u => u.Id == userId && u.Role == MaRoles.Admin, ct))
            return new(false, "That account is also staff somewhere; reset it from its own organization's members page.");
        var r = await sp.GetRequiredService<IUserAdminService>().ResetPasswordAsync(userId, password, mustChange, ct);
        if (!r.Ok) return new(false, r.Error ?? "The password was not changed.");
        Audit("desk.password-reset", "User", userId.ToString(), after: new { mustChange });
        await db.SaveChangesAsync(ct);
        return new(true, "Password changed. Their other sign-ins end within about a minute.", userId);
    }

    // ================================================================== student exams (assignments)

    public async Task<IReadOnlyList<DeskAssignment>> AssignmentsAsync(CancellationToken ct = default)
    {
        var db = Db;
        var org = Org;
        var rows = await db.ExamAssignments.AsNoTracking().Where(a => a.OrganizationId == org).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org)
            .Select(e => new { e.Id, e.Title, e.Mode }).ToDictionaryAsync(e => e.Id, ct);
        var sessions = (await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org)
                .Select(s => new { s.Id, s.AssignmentId, s.Attempt, s.State, s.EndReason }).ToListAsync(ct))
            .GroupBy(s => s.AssignmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Attempt).ToList());
        var latestIds = sessions.Values.Select(l => l[0].Id).ToList();
        var finals = await db.FinalGrades.AsNoTracking().Where(g => g.OrganizationId == org && latestIds.Contains(g.SessionId))
            .Select(g => new { g.SessionId, g.FinalPercent, g.Status }).ToDictionaryAsync(g => g.SessionId, ct);
        var reports = (await db.SessionReports.AsNoTracking().Where(r => r.OrganizationId == org && latestIds.Contains(r.SessionId))
                .Select(r => new { r.SessionId, r.OverallScore, r.CreatedAt }).ToListAsync(ct))
            .GroupBy(r => r.SessionId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First().OverallScore);
        var names = await NamesAsync(ct);
        return rows.Select(a =>
            {
                var list = sessions.GetValueOrDefault(a.Id) ?? [];
                var used = list.Count(s => s.EndReason != nameof(Gadfly.Engine.EndReason.PromptSetChanged));
                var latest = list.FirstOrDefault();
                var status = latest is null ? "Not started"
                    : latest.State is not (SessionState.Ended or SessionState.Failed) ? $"In progress (attempt {latest.Attempt})"
                    : latest.State == SessionState.Failed ? "Failed"
                    : used >= a.AttemptsAllowed ? "Finished · no attempts left" : "Finished";
                string? score = null;
                if (latest is not null && finals.TryGetValue(latest.Id, out var f) && f.FinalPercent is { } pct)
                    score = $"{pct.ToString("0.#", CultureInfo.InvariantCulture)}% ({f.Status})";
                else if (latest is not null && reports.TryGetValue(latest.Id, out var overall))
                    score = $"AI {(overall * 100).ToString("0", CultureInfo.InvariantCulture)}%";
                var exam = exams.GetValueOrDefault(a.ExamId);
                return new DeskAssignment(a.Id, a.ExamId, exam?.Title ?? "(missing exam)", exam?.Mode == ExamMode.Voice, a.StudentUserId,
                    names.GetValueOrDefault(a.StudentUserId, "(unknown user)"), a.AttemptsAllowed, used, status, latest?.Id, score, a.AssignedAt,
                    a.TypedAnswerAccommodation);
            })
            .OrderBy(r => r.StudentName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.ExamTitle, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Assigns a published exam to a student, or changes the attempts allowed when it is already assigned.</summary>
    public async Task<DeskResult> AssignAsync(Guid examId, Guid studentUserId, int attemptsAllowed, CancellationToken ct = default)
    {
        if (attemptsAllowed is < 1 or > 20) return new(false, "Attempts allowed must be 1 to 20.");
        var org = Org;
        var existing = await Db.ExamAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.OrganizationId == org && a.ExamId == examId && a.StudentUserId == studentUserId, ct);
        if (existing is not null) return await SetAttemptsAsync(existing.Id, attemptsAllowed, ct);
        var r = await Assessments.Exams().AssignAsync(examId, [studentUserId], null, null, attemptsAllowed, 1m, ct);
        return new(r.Ok, r.Message, r.Id);
    }

    /// <summary>
    /// Grants or withdraws the typed-answer accommodation for one student on one exam (GAD-T260), through the exam service so the
    /// same permission (assigning exams) and the same audit entry apply as on the instructor's assignments page: a desk admin who is
    /// a student can't grant it to themselves.
    /// </summary>
    public async Task<DeskResult> SetTypedAnswersAsync(Guid assignmentId, bool on, CancellationToken ct = default)
    {
        var r = await Assessments.Exams().SetTypedAnswerAccommodationAsync(assignmentId, on, ct);
        return new(r.Ok, r.Message, r.Id);
    }

    public async Task<DeskResult> SetAttemptsAsync(Guid assignmentId, int attemptsAllowed, CancellationToken ct = default)
    {
        if (attemptsAllowed is < 1 or > 20) return new(false, "Attempts allowed must be 1 to 20.");
        var db = Db;
        var org = Org;
        var a = await db.ExamAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId && x.OrganizationId == org, ct);
        if (a is null) return new(false, "That assignment no longer exists.");
        if (a.AttemptsAllowed == attemptsAllowed) return new(true, "No change.", a.Id);
        var before = a.AttemptsAllowed;
        a.AttemptsAllowed = attemptsAllowed;
        Audit("desk.attempts-changed", "ExamAssignment", a.Id.ToString(), new { AttemptsAllowed = before }, new { AttemptsAllowed = attemptsAllowed });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new(true, $"Attempts allowed is now {attemptsAllowed}.", a.Id);
    }

    /// <summary>
    /// Reset: every attempt at this assignment is removed with its transcript, scores and grades, and results are un-released, so
    /// none counts and the student starts fresh. The assignment itself (exam, attempts allowed) stays. Audited.
    /// </summary>
    public async Task<DeskResult> ResetAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var db = Db;
        var org = Org;
        var a = await db.ExamAssignments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assignmentId && x.OrganizationId == org, ct);
        if (a is null) return new(false, "That assignment no longer exists.");
        var ids = await db.ExamSessions.Where(s => s.OrganizationId == org && s.AssignmentId == assignmentId).Select(s => s.Id).ToListAsync(ct);
        if (await HasLiveAttemptAsync(db, org, assignmentId, ct))
            return new(false, "An attempt is in progress (running or paused). Let it end, then reset.");
        var examTitle = await db.Exams.AsNoTracking().Where(e => e.Id == a.ExamId && e.OrganizationId == org).Select(e => e.Title).FirstOrDefaultAsync(ct);
        var orgName = await db.Organizations.AsNoTracking().Where(o => o.Id == org).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "your organization";
        var studentName = await db.AuthUsers.AsNoTracking().Where(u => u.Id == a.StudentUserId).Select(u => u.DisplayName ?? u.UserName).FirstOrDefaultAsync(ct) ?? "there";
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();   // a retried attempt starts clean, so it adds one audit row, not two
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await DeleteGradesAsync(db, org, ids, ct);
            // The session records (the response ledger, evidence, turns, judgments, integrity) go too: children first, nothing cascades.
            var responseIds = db.StudentResponses.Where(r => r.OrganizationId == org && ids.Contains(r.SessionId)).Select(r => r.Id);
            await db.AssessmentEvidence.Where(e => e.OrganizationId == org && ids.Contains(e.SessionId)).ExecuteDeleteAsync(ct);
            await db.ClarificationEvents.Where(c => c.OrganizationId == org && ids.Contains(c.SessionId)).ExecuteDeleteAsync(ct);
            await db.ResponseVerifications.Where(v => v.OrganizationId == org && responseIds.Contains(v.ResponseId)).ExecuteDeleteAsync(ct);
            await db.TranscriptVersions.Where(v => v.OrganizationId == org && responseIds.Contains(v.ResponseId)).ExecuteDeleteAsync(ct);
            await db.StudentResponses.Where(r => r.OrganizationId == org && ids.Contains(r.SessionId)).ExecuteDeleteAsync(ct);
            var turnIds = db.SessionTurns.Where(t => t.OrganizationId == org && ids.Contains(t.SessionId)).Select(t => t.Id);
            var judgmentIds = db.Judgments.Where(j => j.OrganizationId == org && turnIds.Contains(j.TurnId)).Select(j => j.Id);
            await db.JudgeVotes.Where(v => v.OrganizationId == org && judgmentIds.Contains(v.JudgmentId)).ExecuteDeleteAsync(ct);
            await db.Judgments.Where(j => j.OrganizationId == org && turnIds.Contains(j.TurnId)).ExecuteDeleteAsync(ct);
            await db.SessionClaims.Where(c => c.OrganizationId == org && ids.Contains(c.SessionId)).ExecuteDeleteAsync(ct);
            await db.IntegrityEvents.Where(e => e.OrganizationId == org && ids.Contains(e.SessionId)).ExecuteDeleteAsync(ct);
            await db.ConceptMastery.Where(m => m.OrganizationId == org && ids.Contains(m.SessionId)).ExecuteDeleteAsync(ct);
            await db.UsageEntries.Where(u => u.OrganizationId == org && u.SessionId != null && ids.Contains(u.SessionId.Value))
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.SessionId, (Guid?)null), ct);
            await db.SessionTurns.Where(t => t.OrganizationId == org && ids.Contains(t.SessionId)).ExecuteDeleteAsync(ct);
            await db.ExamSessions.Where(s => s.OrganizationId == org && ids.Contains(s.Id)).ExecuteDeleteAsync(ct);
            await db.ExamAssignments.Where(x => x.Id == assignmentId && x.OrganizationId == org)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ResultsReleasedAt, (DateTimeOffset?)null), ct);
            Audit("desk.assignment-reset", "ExamAssignment", assignmentId.ToString(), new { Sessions = ids },
                reason: "Reset from the admin desk: every attempt, its transcript, scores and grades removed; the student starts fresh.");
            if (ids.Count > 0)
            {
                // Attempt reset (GAD-T310): only when there was something to reset, not an empty assignment.
                var data = new Notifications.EmailData(orgName, studentName, ExamTitle: examTitle, ActionUrl: "/my-exams", ActionLabel: "Open my exams");
                await Notifications.EmailOutbox.StageAsync(db, org, Notifications.EmailKinds.AttemptReset, a.StudentUserId, data, Clock.GetUtcNow(), ct, relatedId: assignmentId);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        db.ChangeTracker.Clear();
        return new(true, ids.Count == 0 ? "Nothing to reset: no attempts yet." : $"Reset: {ids.Count} attempt{(ids.Count == 1 ? "" : "s")} removed with their scores and grades. The student starts fresh.", assignmentId);
    }

    /// <summary>
    /// Clear scores: every score, grade, report and grading record of this assignment's attempts is copied to the audit log
    /// (one <c>desk.scores-archived</c> row per attempt), then removed, and results are un-released. The attempts and
    /// transcripts stay (and still count as used, which the student's page says). Audited (GAD-T262).
    /// </summary>
    public async Task<DeskResult> ClearScoresAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var db = Db;
        var org = Org;
        var a = await db.ExamAssignments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assignmentId && x.OrganizationId == org, ct);
        if (a is null) return new(false, "That assignment no longer exists.");
        var ids = await db.ExamSessions.Where(s => s.OrganizationId == org && s.AssignmentId == assignmentId).Select(s => s.Id).ToListAsync(ct);
        if (await HasLiveAttemptAsync(db, org, assignmentId, ct))
            return new(false, "An attempt is in progress (running or paused). Let it end, then clear the scores.");
        // Ended attempts get a grade record held at Examination In Progress, a status nothing moves out of on its own: the
        // grading sweeper only starts sessions with no grade record and only re-queues later statuses, so the cleared scores
        // stay cleared instead of being re-graded by the AI within seconds.
        var ended = await db.ExamSessions.Where(s => s.OrganizationId == org && s.AssignmentId == assignmentId && s.State == SessionState.Ended)
            .Select(s => s.Id).ToListAsync(ct);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();   // a retried attempt starts clean, so it adds one audit row, not two
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Archive before removing (GAD-T262): this is a high-risk educational use with record-keeping duties (EU AI Act
            // Annex III 3(b), Article 14), so how a grade was reached and then cleared stays on the append-only audit log.
            await ArchiveGradesAsync(db, org, assignmentId, ids, ct);
            await DeleteGradesAsync(db, org, ids, ct);
            await db.ExamAssignments.Where(x => x.Id == assignmentId && x.OrganizationId == org)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ResultsReleasedAt, (DateTimeOffset?)null), ct);
            var now = Clock.GetUtcNow();
            foreach (var id in ended)
                db.FinalGrades.Add(new FinalGrade { SessionId = id, OrganizationId = org, Status = GradeStatus.ExaminationInProgress, UpdatedAt = now });
            Audit("desk.scores-cleared", "ExamAssignment", assignmentId.ToString(), new { Sessions = ids },
                reason: "Scores cleared from the admin desk: grades, reports and grading records archived to the audit log and removed; attempts and transcripts kept; the attempts still count as used.");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        db.ChangeTracker.Clear();
        return new(true, ids.Count == 0
            ? "No attempts, so no scores to clear."
            : "Scores and grades cleared, with an archived copy kept in the audit log. The attempts and transcripts are kept and still count as used; the student is told so.", assignmentId);
    }

    /// <summary>
    /// Copies every grading record of each attempt (grades, reports, disagreements, reconciliations, approvals, appeals) into
    /// one <c>desk.scores-archived</c> audit row per attempt, tied to the session so a privacy erasure of that student still
    /// reaches it. Adds the rows; the caller saves them with the deletion in the same transaction.
    /// </summary>
    private async Task ArchiveGradesAsync(GadflyDbContext db, Guid org, Guid assignmentId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var ai = await db.AiObjectiveGrades.AsNoTracking().Where(g => g.OrganizationId == org && ids.Contains(g.SessionId)).ToListAsync(ct);
        var human = await db.HumanObjectiveGrades.AsNoTracking().Where(g => g.OrganizationId == org && ids.Contains(g.SessionId)).ToListAsync(ct);
        var disagreements = await db.GradeDisagreements.AsNoTracking().Where(d => d.OrganizationId == org && ids.Contains(d.SessionId)).ToListAsync(ct);
        var disagreementIds = disagreements.Select(d => d.Id).ToList();
        var reconciliations = disagreementIds.Count == 0 ? [] : await db.GradeReconciliations.AsNoTracking()
            .Where(r => r.OrganizationId == org && disagreementIds.Contains(r.DisagreementId)).ToListAsync(ct);
        var approvals = await db.ApprovalRecords.AsNoTracking().Where(r => r.OrganizationId == org && ids.Contains(r.SessionId)).ToListAsync(ct);
        var appeals = await db.GradeAppeals.AsNoTracking().Where(r => r.OrganizationId == org && ids.Contains(r.SessionId)).ToListAsync(ct);
        var finals = await db.FinalGrades.AsNoTracking().Where(g => g.OrganizationId == org && ids.Contains(g.SessionId)).ToListAsync(ct);
        var decisions = await db.GradeDecisions.AsNoTracking().Where(g => g.OrganizationId == org && ids.Contains(g.SessionId)).ToListAsync(ct);
        var reports = await db.SessionReports.AsNoTracking().Where(r => r.OrganizationId == org && ids.Contains(r.SessionId)).ToListAsync(ct);
        foreach (var id in ids)
        {
            var sessionDisagreements = disagreements.Where(d => d.SessionId == id).ToList();
            var sessionDisagreementIds = sessionDisagreements.Select(d => d.Id).ToHashSet();
            var snapshot = new
            {
                AssignmentId = assignmentId,
                FinalGrade = finals.Where(g => g.SessionId == id)
                    .Select(g => new { g.Status, g.FinalPercent, g.LetterOrLabel, g.ResultJson, g.PolicyJson, g.UpdatedAt }).FirstOrDefault(),
                AiObjectiveGrades = ai.Where(g => g.SessionId == id).ToList(),
                HumanObjectiveGrades = human.Where(g => g.SessionId == id).ToList(),
                Disagreements = sessionDisagreements,
                Reconciliations = reconciliations.Where(r => sessionDisagreementIds.Contains(r.DisagreementId)).ToList(),
                Approvals = approvals.Where(r => r.SessionId == id).ToList(),
                Appeals = appeals.Where(r => r.SessionId == id).ToList(),
                GradeDecisions = decisions.Where(g => g.SessionId == id).ToList(),
                Reports = reports.Where(r => r.SessionId == id).ToList(),
            };
            if (snapshot.FinalGrade is null && snapshot.AiObjectiveGrades.Count == 0 && snapshot.HumanObjectiveGrades.Count == 0
                && snapshot.Disagreements.Count == 0 && snapshot.Approvals.Count == 0 && snapshot.Appeals.Count == 0
                && snapshot.GradeDecisions.Count == 0 && snapshot.Reports.Count == 0)
                continue;
            Assessments.Guard.Audit("desk.scores-archived", "ExamSession", id.ToString(), before: snapshot,
                reason: "Archived copy of this attempt's grading records, taken by Clear scores before they were removed (GAD-T262).",
                sessionId: id);
        }
    }

    /// <summary>The questions and answers of one attempt, in order.</summary>
    public async Task<IReadOnlyList<DeskTurn>> TranscriptAsync(Guid sessionId, CancellationToken ct = default)
    {
        var org = Org;
        return await Db.SessionTurns.AsNoTracking().Where(t => t.OrganizationId == org && t.SessionId == sessionId).OrderBy(t => t.Index)
            .Select(t => new DeskTurn(t.Index, t.ConceptKey, t.Question, t.AnswerText, t.AnswerMode)).ToListAsync(ct);
    }

    // ================================================================== helpers

    /// <summary>Grading records reference sessions and each other: children first, since nothing cascades.</summary>
    private static async Task DeleteGradesAsync(GadflyDbContext db, Guid org, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var disagreementIds = db.GradeDisagreements.Where(d => d.OrganizationId == org && ids.Contains(d.SessionId)).Select(d => d.Id);
        await db.GradeReconciliations.Where(r => r.OrganizationId == org && disagreementIds.Contains(r.DisagreementId)).ExecuteDeleteAsync(ct);
        await db.GradeDisagreements.Where(d => d.OrganizationId == org && ids.Contains(d.SessionId)).ExecuteDeleteAsync(ct);
        await db.AiObjectiveGrades.Where(g => g.OrganizationId == org && ids.Contains(g.SessionId)).ExecuteDeleteAsync(ct);
        await db.HumanObjectiveGrades.Where(g => g.OrganizationId == org && ids.Contains(g.SessionId)).ExecuteDeleteAsync(ct);
        await db.ApprovalRecords.Where(r => r.OrganizationId == org && ids.Contains(r.SessionId)).ExecuteDeleteAsync(ct);
        await db.GradeAppeals.Where(r => r.OrganizationId == org && ids.Contains(r.SessionId)).ExecuteDeleteAsync(ct);
        await db.FinalGrades.Where(g => g.OrganizationId == org && ids.Contains(g.SessionId)).ExecuteDeleteAsync(ct);
        await db.GradeDecisions.Where(g => g.OrganizationId == org && ids.Contains(g.SessionId)).ExecuteDeleteAsync(ct);
        await db.SessionReports.Where(r => r.OrganizationId == org && ids.Contains(r.SessionId)).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// True while an attempt at the assignment is running or paused: the session orchestrator holds it live and keeps saving
    /// to it, so its rows can't be removed under it. Reset and Clear scores refuse rather than end it for the student.
    /// </summary>
    private static Task<bool> HasLiveAttemptAsync(GadflyDbContext db, Guid org, Guid assignmentId, CancellationToken ct) =>
        db.ExamSessions.AnyAsync(s => s.OrganizationId == org && s.AssignmentId == assignmentId
                                      && (s.State == SessionState.Running || s.State == SessionState.Paused), ct);

    private async Task<Dictionary<Guid, string>> NamesAsync(CancellationToken ct) =>
        (await Directory.MembersAsync(Org, ct)).GroupBy(m => m.Member.UserId).ToDictionary(g => g.Key, g => g.First().UserName);

    private void Audit(string action, string entityType, string? entityId, object? before = null, object? after = null, string? reason = null) =>
        Assessments.Guard.Audit(action, entityType, entityId, before, after, reason);
}
