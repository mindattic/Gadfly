using System.Security.Cryptography;
using System.Text;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Translation;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Student;

/// <summary>Where one assignment stands for the student, in the words the dashboard shows.</summary>
/// <remarks>
/// <see cref="Archived"/>: the instructor archived the exam, so no new attempt can start; an attempt
/// already in progress stays <see cref="InProgress"/> and can be resumed (GAD-T70).
/// </remarks>
public enum AssignmentStanding { NotOpenYet, Open, InProgress, Completed, Closed, Unavailable, Archived }

/// <summary>
/// Results the instructor released for one assignment (never shown before release). <paramref name="Levels"/> is the AI report's
/// corroborated level per learning objective (Level 0 = insufficient evidence), the reported result since GAD-T425; the old
/// running-estimate recommendation is no longer carried (GAD-T460). Empty when the report predates objectives.
/// </summary>
public sealed record ReleasedResult(string? Grade, string? Comment, IReadOnlyList<Gadfly.Server.Review.ObjectiveLevel> Levels, DateTimeOffset ReleasedAt);

/// <summary>One row of the student's dashboard (GAD-T51).</summary>
public sealed record StudentAssignmentView
{
    public required Guid AssignmentId { get; init; }
    public Guid ExamId { get; init; }
    public required string ExamTitle { get; init; }
    public string? Instructions { get; init; }
    public ExamMode Mode { get; init; }
    public IntegrityLevel Integrity { get; init; } = IntegrityLevel.Standard;
    public DateTimeOffset? OpensAt { get; init; }
    public DateTimeOffset? ClosesAt { get; init; }
    public int AttemptsUsed { get; init; }
    public int AttemptsAllowed { get; init; }
    public AssignmentStanding Standing { get; init; }
    /// <summary>The attempt in progress (not ended), if any.</summary>
    public Guid? ActiveSessionId { get; init; }
    public SessionState? ActiveSessionState { get; init; }
    public ReleasedResult? Result { get; init; }
    /// <summary>The instructor archived the exam: no new attempts, but one in progress can still be finished.</summary>
    public bool ExamArchived { get; init; }
    /// <summary>
    /// When an administrator last cleared this assignment's scores (GAD-T262), if they did: the attempts are kept and still
    /// count as used, and any result shown before is withdrawn until it is graded and published again.
    /// </summary>
    public DateTimeOffset? ScoresClearedAt { get; init; }
    /// <summary>
    /// The typed-answer accommodation an instructor or admin granted on this assignment (GAD-T260): the student types their answers;
    /// the camera, the spoken questions and the gaze check stay. The student can't change it.
    /// </summary>
    public bool TypedAnswers { get; init; }

    /// <summary>True when the attempt in progress already has consent, so "Resume" can go straight to the exam room.</summary>
    public bool CanResumeDirectly => ActiveSessionId is not null && ActiveSessionState is SessionState.ConsentGiven or SessionState.Running or SessionState.Paused;

    public bool CanStart => Standing is AssignmentStanding.Open or AssignmentStanding.InProgress;
}

/// <summary>
/// Read side of the student pages (GAD-T51/T52): the student's own assignments, attempts and
/// released results, always filtered to the active organization and the signed-in student on top
/// of the context's tenant filter. Each call opens its own scope (and DbContext): a Blazor circuit's
/// scoped context is shared with the layout and other components, which query it concurrently.
/// </summary>
public static class StudentExams
{
    // The scope acts for the organization it reads (GAD-T73): never the possibly-absent HttpContext of a circuit.
    private static async Task<T> WithDbAsync<T>(IServiceScopeFactory scopes, Guid organizationId, Func<GadflyDbContext, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId));
        return await work(scope.ServiceProvider.GetRequiredService<GadflyDbContext>());
    }

    public static Task<IReadOnlyList<StudentAssignmentView>> ListAsync(
        IServiceScopeFactory scopes, Guid organizationId, Guid studentUserId, DateTimeOffset now, CancellationToken ct = default) =>
        ListAsync(scopes, organizationId, studentUserId, now, null, ct);

    /// <summary>
    /// The student's assignments with each exam's title, instructions and objective titles in <paramref name="language"/> (the page's
    /// language) when its approved texts exist; the English otherwise, never an unapproved machine draft (GAD-T630).
    /// </summary>
    public static Task<IReadOnlyList<StudentAssignmentView>> ListAsync(
        IServiceScopeFactory scopes, Guid organizationId, Guid studentUserId, DateTimeOffset now, string? language, CancellationToken ct = default) =>
        WithDbAsync(scopes, organizationId, db => ListAsync(db, organizationId, studentUserId, now, language, ct));

    private static async Task<IReadOnlyList<StudentAssignmentView>> ListAsync(
        GadflyDbContext db, Guid organizationId, Guid studentUserId, DateTimeOffset now, string? language, CancellationToken ct)
    {
        var assignments = await db.ExamAssignments.AsNoTracking().Include(a => a.Exam)
            .Where(a => a.OrganizationId == organizationId && a.StudentUserId == studentUserId)
            .ToListAsync(ct);
        if (assignments.Count == 0) return [];

        var ids = assignments.Select(a => a.Id).ToList();
        var sessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.StudentUserId == studentUserId && ids.Contains(s.AssignmentId))
            .Select(s => new { s.Id, s.AssignmentId, s.Attempt, s.State, s.EndedAt, s.EndReason, s.NeedsInstructorReview })
            .ToListAsync(ct);

        // Results only for assignments the instructor released, and never for an attempt held for instructor review (GAD-T603).
        var releasedIds = assignments.Where(a => a.ResultsReleasedAt is not null).Select(a => a.Id).ToHashSet();
        var releasedSessionIds = sessions.Where(s => releasedIds.Contains(s.AssignmentId) && !s.NeedsInstructorReview).Select(s => s.Id).ToList();
        var grades = releasedSessionIds.Count == 0 ? [] : await db.GradeDecisions.AsNoTracking()
            .Where(g => g.OrganizationId == organizationId && releasedSessionIds.Contains(g.SessionId))
            .ToListAsync(ct);
        var reports = releasedSessionIds.Count == 0 ? [] : await db.SessionReports.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && releasedSessionIds.Contains(r.SessionId))
            .Select(r => new { r.SessionId, r.ReportJson, r.CreatedAt })
            .ToListAsync(ct);

        // Clear scores leaves an audit row on the assignment (GAD-T262); the student is told the attempts still count. A
        // later reset (the student starts fresh) or a later attempt makes that notice stale, so only the latest clear counts.
        var assignmentKeys = ids.Select(i => i.ToString()).ToList();
        var deskEvents = await db.AuditEvents.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId && e.EntityType == "ExamAssignment" && e.EntityId != null
                        && assignmentKeys.Contains(e.EntityId)
                        && (e.Action == "desk.scores-cleared" || e.Action == "desk.assignment-reset"))
            .Select(e => new { e.EntityId, e.Action, e.At })
            .ToListAsync(ct);
        var cleared = deskEvents.GroupBy(e => e.EntityId!)
            .Select(g => g.OrderByDescending(e => e.At).First())
            .Where(e => e.Action == "desk.scores-cleared")
            .ToDictionary(e => e.EntityId!, e => e.At);

        var rows = new List<StudentAssignmentView>();
        foreach (var a in assignments)
        {
            var mine = sessions.Where(s => s.AssignmentId == a.Id).OrderBy(s => s.Attempt).ToList();
            var active = mine.FirstOrDefault(s => s.State is not (SessionState.Ended or SessionState.Failed));
            var used = mine.Count(x => x.EndReason != nameof(Gadfly.Engine.EndReason.PromptSetChanged));

            ReleasedResult? result = null;
            if (a.ResultsReleasedAt is { } released)
            {
                // The decided attempt: the latest one the instructor graded, else the latest finished one.
                var mineIds = mine.Where(s => !s.NeedsInstructorReview).Select(s => s.Id).ToHashSet();
                var grade = grades.Where(g => mineIds.Contains(g.SessionId)).OrderByDescending(g => g.DecidedAt).FirstOrDefault();
                var sessionId = grade?.SessionId ?? mine.Where(s => s.State == SessionState.Ended && !s.NeedsInstructorReview).OrderByDescending(s => s.Attempt).Select(s => (Guid?)s.Id).FirstOrDefault();
                var report = reports.Where(r => r.SessionId == sessionId).OrderByDescending(r => r.CreatedAt).FirstOrDefault();
                if (grade is not null || report is not null)
                    result = new ReleasedResult(grade?.Grade, grade?.Comment, Gadfly.Server.Review.ReportedLevels.FromReportJson(report?.ReportJson), released);
            }

            var texts = a.Exam is null || Gadfly.Engine.ExamLanguage.IsEnglish(language) ? null
                : await StudentExamTexts.ForAsync(db, organizationId, a.ExamId, language, ct);
            if (texts is not null && result is not null)
                result = result with { Levels = result.Levels.Select(l => l with { Title = texts.ObjectiveTitle(l.Title) }).ToList() };
            rows.Add(new StudentAssignmentView
            {
                AssignmentId = a.Id,
                ExamId = a.ExamId,
                ExamTitle = texts?.Title ?? a.Exam?.Title ?? "Exam",
                Instructions = texts is not null ? texts.Instructions : a.Exam?.Instructions,
                Mode = a.Exam?.Mode ?? ExamMode.Text,
                Integrity = a.Exam?.IntegrityLevel ?? IntegrityLevel.Standard,
                OpensAt = a.OpensAt,
                ClosesAt = a.ClosesAt,
                AttemptsUsed = used,
                AttemptsAllowed = a.AttemptsAllowed,
                ActiveSessionId = active?.Id,
                ActiveSessionState = active?.State,
                Result = result,
                ExamArchived = a.Exam?.Status == ExamStatus.Archived,
                ScoresClearedAt = cleared.TryGetValue(a.Id.ToString(), out var clearedAt) && active is null
                                  && !mine.Any(s => s.EndedAt > clearedAt) ? clearedAt : null,
                Standing = StandingOf(a, active is not null, used, now),
                TypedAnswers = a.TypedAnswerAccommodation,
            });
        }
        return rows.OrderBy(r => r.Standing switch
            {
                AssignmentStanding.InProgress => 0,
                AssignmentStanding.Open => 1,
                AssignmentStanding.NotOpenYet => 2,
                _ => 3,
            })
            .ThenBy(r => r.ClosesAt ?? DateTimeOffset.MaxValue)
            .ThenBy(r => r.ExamTitle, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>Attempts allowed on an assignment created for an open exam.</summary>
    public const int OpenExamAttempts = 50;

    /// <summary>
    /// Start by exam (GAD-T669): the student's attempt record (assignment) on <paramref name="examId"/>, the one they already have,
    /// or one made now when the exam is published and <paramref name="createIfMissing"/>: every published exam is open to every
    /// student in the organization (GAD-T845). 50 attempts, assigned by the exam's owner, accommodations copied from the student's
    /// most recent assignment in the organization. This is the only way an exam gets an assignment (GAD-T674: none are made in
    /// bulk). A duplicate made at the same moment loses to the unique (exam, student) index and the winner's id is returned. Null
    /// when there is none and none can be made.
    /// </summary>
    public static Task<Guid?> EnsureOpenAssignmentAsync(
        IServiceScopeFactory scopes, Guid organizationId, Guid studentUserId, Guid examId, DateTimeOffset now,
        bool createIfMissing = true, CancellationToken ct = default) =>
        WithDbAsync(scopes, organizationId, async db =>
    {
        Task<Guid?> ExistingAsync() => db.ExamAssignments.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.StudentUserId == studentUserId && a.ExamId == examId)
            .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);

        if (await ExistingAsync() is { } existing) return existing;
        if (!createIfMissing) return null;
        var exam = await db.Exams.AsNoTracking()
            .Where(e => e.Id == examId && e.OrganizationId == organizationId && e.Status == ExamStatus.Published)
            .Select(e => new
            {
                e.Id,
                AssignedBy = e.CreatedByUserId != Guid.Empty ? e.CreatedByUserId
                    : db.SourceCollections.Where(c => c.Id == e.CollectionId).Select(c => (Guid?)c.CreatedByUserId).FirstOrDefault() ?? Guid.Empty,
            })
            .FirstOrDefaultAsync(ct);
        if (exam is null || exam.AssignedBy == Guid.Empty) return null;

        var latest = await LatestAccommodationsAsync(db, organizationId, studentUserId, ct);
        var assignment = NewOpenAssignment(organizationId, exam.Id, studentUserId, exam.AssignedBy, now, latest);
        db.ExamAssignments.Add(assignment);
        try
        {
            await db.SaveChangesAsync(ct);
            return assignment.Id;
        }
        catch (DbUpdateException)
        {
            // Assigned at the same moment elsewhere: the unique (exam, student) index refused this one; use the one that won.
            db.ChangeTracker.Clear();
            return await ExistingAsync();
        }
    });

    /// <summary>The accommodations a new open assignment copies: the student's most recent assignment in the organization's.</summary>
    private sealed record OpenAccommodations(decimal TimeMultiplier, bool GazeAccommodation, bool TypedAnswerAccommodation,
        bool StudentInEuropeanUnion, bool WindowAbsenceFlagOff, int? WindowAbsenceReviewAfterCount, int? WindowAbsenceReviewAfterSeconds);

    // GAD-T668: a new open assignment carries the student's accommodations from their most recent assignment in the
    // organization, so extra time, gaze, typed answers, EU status and window-absence allowances follow them to every exam.
    private static Task<OpenAccommodations?> LatestAccommodationsAsync(GadflyDbContext db, Guid organizationId, Guid studentUserId, CancellationToken ct) =>
        db.ExamAssignments.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.StudentUserId == studentUserId)
            .OrderByDescending(a => a.AssignedAt)
            .Select(a => new OpenAccommodations(a.TimeMultiplier, a.GazeAccommodation, a.TypedAnswerAccommodation, a.StudentInEuropeanUnion,
                a.WindowAbsenceFlagOff, a.WindowAbsenceReviewAfterCount, a.WindowAbsenceReviewAfterSeconds))
            .FirstOrDefaultAsync(ct);

    private static ExamAssignment NewOpenAssignment(Guid organizationId, Guid examId, Guid studentUserId, Guid assignedBy, DateTimeOffset now,
        OpenAccommodations? latest) => new()
    {
        OrganizationId = organizationId,
        ExamId = examId,
        StudentUserId = studentUserId,
        AttemptsAllowed = OpenExamAttempts,
        AssignedByUserId = assignedBy,
        AssignedAt = now,
        TimeMultiplier = latest?.TimeMultiplier ?? 1m,
        GazeAccommodation = latest?.GazeAccommodation ?? false,
        TypedAnswerAccommodation = latest?.TypedAnswerAccommodation ?? false,
        StudentInEuropeanUnion = latest?.StudentInEuropeanUnion ?? false,
        WindowAbsenceFlagOff = latest?.WindowAbsenceFlagOff ?? false,
        WindowAbsenceReviewAfterCount = latest?.WindowAbsenceReviewAfterCount,
        WindowAbsenceReviewAfterSeconds = latest?.WindowAbsenceReviewAfterSeconds,
    };

    /// <summary>One of the student's own assignments, or null when it isn't theirs.</summary>
    public static async Task<StudentAssignmentView?> FindAsync(
        IServiceScopeFactory scopes, Guid organizationId, Guid studentUserId, Guid assignmentId, DateTimeOffset now, string? language = null, CancellationToken ct = default) =>
        (await ListAsync(scopes, organizationId, studentUserId, now, language, ct)).FirstOrDefault(r => r.AssignmentId == assignmentId);

    private static AssignmentStanding StandingOf(ExamAssignment a, bool hasActive, int used, DateTimeOffset now)
    {
        var archived = a.Exam?.Status == ExamStatus.Archived;
        if (a.Exam?.Status != ExamStatus.Published && !archived) return AssignmentStanding.Unavailable;
        if (a.ClosesAt is { } closes && now > closes) return used > 0 ? AssignmentStanding.Completed : AssignmentStanding.Closed;
        // Archiving blocks new attempts only: an attempt already in progress can still be resumed.
        if (hasActive) return AssignmentStanding.InProgress;
        if (archived) return used > 0 ? AssignmentStanding.Completed : AssignmentStanding.Archived;
        if (a.OpensAt is { } opens && now < opens) return AssignmentStanding.NotOpenYet;
        if (used >= a.AttemptsAllowed) return AssignmentStanding.Completed;
        return AssignmentStanding.Open;
    }

    public static string Describe(AssignmentStanding s) => s switch
    {
        AssignmentStanding.NotOpenYet => "Not open yet",
        AssignmentStanding.Open => "Ready to start",
        AssignmentStanding.InProgress => "In progress",
        AssignmentStanding.Completed => "Submitted",
        AssignmentStanding.Closed => "Closed",
        AssignmentStanding.Archived => "No longer available",
        _ => "Not available yet",
    };

    /// <summary>The standing in the page's language (GAD-T544); <see cref="Describe(AssignmentStanding)"/> is the English.</summary>
    public static string Describe(AssignmentStanding s, Gadfly.Server.Localization.UiText t) => t[s switch
    {
        AssignmentStanding.NotOpenYet => "standing.notOpenYet",
        AssignmentStanding.Open => "standing.open",
        AssignmentStanding.InProgress => "standing.inProgress",
        AssignmentStanding.Completed => "standing.completed",
        AssignmentStanding.Closed => "standing.closed",
        AssignmentStanding.Archived => "standing.archived",
        _ => "standing.unavailable",
    }].Value;

    /// <summary>
    /// A student-readable line for one objective's level (GAD-T460; the instructor decides the grade). Level 0 is worded as
    /// not enough evidence, never as no understanding.
    /// </summary>
    public static string DescribeLevel(Gadfly.Server.Review.ObjectiveLevel level) => level.Insufficient
        ? $"{level.Title}: not enough evidence to say"
        : $"{level.Title}: Level {level.Level} of 5 ({level.Label})";

    /// <summary>The session's owner-checked state, or null when it isn't the student's.</summary>
    public static Task<(SessionState State, Guid AssignmentId, string ExamTitle)?> SessionAsync(
        IServiceScopeFactory scopes, Guid organizationId, Guid studentUserId, Guid sessionId, CancellationToken ct = default) =>
        WithDbAsync(scopes, organizationId, async db =>
    {
        var row = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId)
            .Select(s => new { s.State, s.AssignmentId, s.ExamId, s.Language })
            .FirstOrDefaultAsync(ct);
        if (row is null) return ((SessionState, Guid, string)?)null;
        // The attempt's language (GAD-T630): the approved localized title, else the English one.
        var title = (await LocalizedExamTexts.ForStudentAsync(db, organizationId, row.ExamId, row.Language, ct))?.Title;
        return ((SessionState, Guid, string)?)(row.State, row.AssignmentId, title ?? "Exam");
    });

    /// <summary>
    /// Whether this student may type their answers in this session (GAD-T260): only when an instructor or admin granted the
    /// typed-answer accommodation on the session's assignment. False when the session isn't the student's.
    /// </summary>
    public static Task<bool> TypedAnswersAsync(IServiceScopeFactory scopes, Guid organizationId, Guid studentUserId, Guid sessionId, CancellationToken ct = default) =>
        WithDbAsync(scopes, organizationId, db => TypedAnswersAsync(db, organizationId, studentUserId, sessionId, ct));

    /// <inheritdoc cref="TypedAnswersAsync(IServiceScopeFactory, Guid, Guid, Guid, CancellationToken)"/>
    public static async Task<bool> TypedAnswersAsync(GadflyDbContext db, Guid organizationId, Guid studentUserId, Guid sessionId, CancellationToken ct = default) =>
        await (from s in db.ExamSessions.AsNoTracking()
               join a in db.ExamAssignments.AsNoTracking() on s.AssignmentId equals a.Id
               where s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId && a.OrganizationId == organizationId
               select a.TypedAnswerAccommodation).FirstOrDefaultAsync(ct);

    /// <summary>How many days the organization keeps answers and transcripts (decision D7 default 90).</summary>
    public static Task<(string OrganizationName, int RetentionDays)> OrganizationAsync(IServiceScopeFactory scopes, Guid organizationId, CancellationToken ct = default) =>
        WithDbAsync(scopes, organizationId, async db =>
    {
        var name = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.Name).FirstOrDefaultAsync(ct);
        var days = await db.OrganizationSettings.AsNoTracking().Where(s => s.OrganizationId == organizationId)
            .Select(s => (int?)s.RetentionDays).FirstOrDefaultAsync(ct);
        return (name ?? "your organization", days ?? 90);
    });
}

/// <summary>
/// The plain-language exam notice a student accepts before the first question (GAD-T51). The exact
/// text shown is hashed (SHA-256) and stored on the session, so a reviewer can tell which wording
/// the student agreed to (BIPA/GDPR consent record; retention and withdrawal are GAD-T61).
/// </summary>
public sealed record ConsentNotice(string Title, IReadOnlyList<string> Paragraphs)
{
    /// <summary>Bumped whenever the wording changes; part of the hashed text.</summary>
    public const string Version = "2026-11-22.1";   // GAD-T453: the same-face check's note is sent; the face data is not

    /// <remarks>Every exam is spoken with the camera on (GAD-T239), so <paramref name="mode"/> no longer changes the wording.</remarks>
    /// <param name="typedAnswers">
    /// The student has the typed-answer accommodation (GAD-T260): the notice says they type their answers, the microphone isn't used
    /// and pasting into the answer box is noted. Without it the wording is unchanged.
    /// </param>
    /// <param name="language">
    /// The page's language (GAD-T544): "ko" gives the Korean notice, the same terms in natural formal Korean, and "es" the Spanish
    /// one (GAD-T612, neutral Latin American Spanish, usted); anything else the English, unchanged. The hash covers the exact words shown, so the record says which language the student read.
    /// </param>
    public static ConsentNotice For(string organizationName, string examTitle, int retentionDays, ExamMode mode,
        IntegrityLevel integrity = IntegrityLevel.Standard, bool typedAnswers = false, string? language = null)
    {
        if (Gadfly.Server.Localization.UiCatalog.Normalize(language) == Gadfly.Server.Localization.UiCatalog.Korean)
            return KoreanFor(organizationName, examTitle, retentionDays, integrity, typedAnswers);
        if (Gadfly.Server.Localization.UiCatalog.Normalize(language) == Gadfly.Server.Localization.UiCatalog.Spanish)
            return SpanishFor(organizationName, examTitle, retentionDays, integrity, typedAnswers);
        var paragraphs = new List<string>
        {
            $"This is an oral-style exam for {organizationName}: \"{examTitle}\". An AI examiner asks you questions and follows up on your own answers to see how well you understand the material.",
            "What is recorded: every question and a transcript of your spoken answers (with any corrections you make to it), when each question was asked and answered, and integrity signals such as an answer that reads like copied text. These are notes for a person to review, never an automatic fail.",
            "What is not recorded: no video and no audio is stored. Your camera shows you on your own screen beside the examiner; the picture is not recorded or sent.",
            $"How long it is kept: {organizationName} keeps your answers and transcripts for {retentionDays} days, then they are deleted.",
            "Who decides: the AI only gives a preliminary assessment. People grade your answers, any large disagreement with the AI is reconciled by a reviewer, and an authorized person approves and publishes your result. You can appeal a published result.",
            "Your choice: you can leave the exam at any time with \"Finish\". Leaving early submits what you have answered so far and uses this attempt.",
        };
        if (typedAnswers)
            paragraphs[1] = "What is recorded: every question and the answers you type (with any corrections you make), when each question was asked and answered, and integrity signals such as an answer that reads like copied text. These are notes for a person to review, never an automatic fail.";
        paragraphs.InsertRange(2, BrowserSignals(integrity, typedAnswers));
        if (integrity == IntegrityLevel.Strict)
        {
            // The camera is on for this exam: say plainly what is and isn't collected (GAD-T57), instead of the voice-mode line.
            paragraphs.RemoveAll(p => p.StartsWith("What is not recorded:", StringComparison.Ordinal));
            paragraphs.InsertRange(paragraphs.FindIndex(p => p.StartsWith("How long it is kept:", StringComparison.Ordinal)), CameraNotice());
        }
        if (typedAnswers)
        {
            paragraphs.Add("Your instructor has given you an accommodation: you type your answers instead of speaking them. The examiner still speaks each question out loud and shows it on screen, "
                + "and your camera must be on for the whole exam, with the same eye-contact check as everyone else unless you also have the eye-contact accommodation. Your microphone is not used. "
                + "Each answer is kept as typed, and pasting text into the answer box is noted for your instructor to review (never blocked, never an automatic fail). "
                + "If your camera stops working during the exam, the exam pauses until you bring it back, and the clock stops while it is paused (for up to 10 minutes in all, or half the exam time if that is shorter; after that it keeps running). Your time starts when the examiner asks the first question.");
            return new ConsentNotice("Before you begin", paragraphs);
        }
        paragraphs.Add("This exam is spoken, and your microphone and camera must be on for the whole exam. The examiner speaks the questions and you answer out loud; there is no typed answer. "
            + "Your microphone audio is streamed to a speech-to-text service to make a transcript, and the audio itself is not recorded or kept. "
            + "What is kept is the transcript (with how confident the recognizer was), how it was spoken (pace, pauses, hesitations, and whether more than one voice was heard), and what you confirm. "
            + "Notes such as \"a second voice was heard\" are for your instructor to read as weak evidence, never an automatic fail. "
            + "If your microphone or camera stops working during the exam, the exam pauses until you bring it back, and the clock stops while it is paused (for up to 10 minutes in all, or half the exam time if that is shorter; after that it keeps running). Your time starts when the examiner asks the first question.");
        return new ConsentNotice("Before you begin", paragraphs);
    }

    /// <summary>
    /// The browser integrity signals this exam notes (GAD-T56), in the same words the exam room shows;
    /// none when the exam's integrity level is Off.
    /// </summary>
    private static IEnumerable<string> BrowserSignals(IntegrityLevel integrity, bool typedAnswers = false)
    {
        if (integrity == IntegrityLevel.Off) yield break;
        var what = "switching to another tab or window"
            + (typedAnswers ? ", pasting into the answer box" : "")
            + (integrity == IntegrityLevel.Strict
                ? ", copying the question, leaving full screen, and having more than one screen connected (your browser may ask to allow screen detection; you can decline)"
                : ", and copying the question");
        yield return $"During the exam your browser also notes {what}: when it happened and for how long. Nothing is blocked; these are notes for your instructor to review, never an automatic fail.";
    }

    /// <summary>
    /// What the camera checks collect and don't (GAD-T57), in plain words. Face geometry can count as biometric
    /// data, so this is the notice the student's consent has to cover before the camera is turned on.
    /// </summary>
    private static IEnumerable<string> CameraNotice()
    {
        yield return "Camera: this exam uses your camera. Your browser will ask you to allow it. The camera is analysed on your own device only. No video, photo, screenshot, audio or image of your face is recorded, stored or sent anywhere, and the face data used for the checks is held in your browser's memory and discarded when the exam ends.";
        yield return "What is sent and kept from the camera: only short event notes, each with a type, a start and end time, a confidence and one plain sentence, for example \"no face in view for 12 seconds\", \"a second face appeared\", \"looked to the left for 8 seconds\", \"eye movements like reading something off-screen\", or \"the camera was not used\". The device also checks that the same face stays in front of the camera; if it may have changed, a note saying so is sent like the others, but the face data used for the check never leaves your device.Short glances away while you think are noted as normal. Nothing is made of your emotions, mood or honesty: the camera is not used to judge any of that.";
        yield return "Camera notes are for your instructor to read alongside your answers, as low-confidence evidence, never an automatic fail. The camera must be on to take this exam: without it the exam can't start, and it pauses if the camera stops.";
    }

    /// <summary>
    /// The Korean notice (GAD-T544): the same terms, paragraph for paragraph, in the formal 합쇼체 a consent notice calls for.
    /// Wording follows docs/LOCALIZATION.md's glossary (시험관, 응시, 대화 기록, 담당 교수자).
    /// </summary>
    private static ConsentNotice KoreanFor(string organizationName, string examTitle, int retentionDays, IntegrityLevel integrity, bool typedAnswers)
    {
        const string pause = "시험 중 {0}가 멈추면 다시 켤 때까지 시험이 일시 정지되고, 일시 정지된 동안에는 시간이 멈춥니다(모두 합해 최대 10분 또는 시험 시간의 절반 중 더 짧은 시간까지이며, 그 뒤에는 시간이 계속 흐릅니다). 시험 시간은 시험관이 첫 질문을 할 때부터 시작됩니다.";
        var paragraphs = new List<string>
        {
            $"{organizationName}의 구술형 시험 \"{examTitle}\"입니다. AI 시험관이 질문을 하고 학생의 답변을 바탕으로 후속 질문을 하며, 학습 내용을 얼마나 잘 이해하고 있는지 확인합니다.",
            typedAnswers
                ? "기록되는 내용: 모든 질문과 입력한 답변(직접 수정한 내용 포함), 각 질문을 한 시각과 답변한 시각, 그리고 복사한 글처럼 보이는 답변 같은 부정행위 방지 신호. 이는 사람이 검토하기 위한 참고 기록이며, 자동으로 불합격 처리되지 않습니다."
                : "기록되는 내용: 모든 질문과 말로 한 답변의 대화 기록(직접 수정한 내용 포함), 각 질문을 한 시각과 답변한 시각, 그리고 복사한 글처럼 보이는 답변 같은 부정행위 방지 신호. 이는 사람이 검토하기 위한 참고 기록이며, 자동으로 불합격 처리되지 않습니다.",
            "기록되지 않는 내용: 영상과 음성은 저장되지 않습니다. 카메라 화면은 학생 본인의 화면에만 보이며, 녹화되거나 전송되지 않습니다.",
            $"보관 기간: {organizationName}은(는) 답변과 대화 기록을 {retentionDays}일 동안 보관한 뒤 삭제합니다.",
            "결정하는 주체: AI는 예비 평가만 합니다. 답변은 사람이 채점하고, AI와 크게 다른 부분은 검토자가 조정하며, 권한 있는 담당자가 결과를 승인하고 공개합니다. 공개된 결과에는 이의 신청을 할 수 있습니다.",
            "선택권: 언제든지 \"시험 마치기\"를 눌러 시험을 끝낼 수 있습니다. 일찍 끝내면 그때까지 답한 내용이 제출되고, 이번 응시 기회가 사용됩니다.",
        };
        if (integrity != IntegrityLevel.Off)
        {
            var what = "다른 탭이나 창으로 전환한 것"
                + (typedAnswers ? ", 답변 칸에 붙여 넣은 것" : "")
                + (integrity == IntegrityLevel.Strict
                    ? ", 질문을 복사한 것, 전체 화면에서 나간 것, 화면이 두 개 이상 연결된 것(브라우저가 화면 감지 허용 여부를 물을 수 있으며, 거부해도 됩니다)"
                    : ", 질문을 복사한 것");
            paragraphs.Insert(2, $"시험 중에는 브라우저가 {what}을(를) 언제, 얼마 동안이었는지 함께 기록합니다. 아무것도 차단되지 않으며, 담당 교수자가 검토하기 위한 참고 기록일 뿐 자동으로 불합격 처리되지 않습니다.");
        }
        if (integrity == IntegrityLevel.Strict)
        {
            paragraphs.RemoveAll(p => p.StartsWith("기록되지 않는 내용:", StringComparison.Ordinal));
            paragraphs.InsertRange(paragraphs.FindIndex(p => p.StartsWith("보관 기간:", StringComparison.Ordinal)),
            [
                "카메라: 이 시험은 카메라를 사용합니다. 브라우저가 카메라 사용 허용 여부를 물을 것입니다. 카메라 영상은 학생의 기기 안에서만 분석됩니다. 영상, 사진, 화면 캡처, 음성, 얼굴 이미지는 어디에도 기록·저장·전송되지 않으며, 확인에 쓰이는 얼굴 데이터는 브라우저 메모리에만 있다가 시험이 끝나면 삭제됩니다.",
                "카메라에서 전송되고 보관되는 것: 짧은 이벤트 기록만 전송됩니다. 각 기록에는 종류, 시작 및 종료 시각, 신뢰도, 그리고 \"12초 동안 얼굴이 보이지 않음\", \"두 번째 얼굴이 나타남\", \"8초 동안 왼쪽을 봄\", \"화면 밖의 무언가를 읽는 듯한 눈의 움직임\", \"카메라가 사용되지 않음\" 같은 한 문장 설명이 들어 있습니다. 기기는 같은 얼굴이 계속 카메라 앞에 있는지도 확인합니다. 얼굴이 바뀌었을 가능성이 있으면 그 사실을 알리는 기록이 다른 기록처럼 전송되지만, 확인에 쓰인 얼굴 데이터는 기기 밖으로 나가지 않습니다. 생각하는 동안 잠깐 시선을 돌리는 것은 정상적인 일로 기록됩니다. 감정, 기분, 정직성은 판단하지 않으며, 카메라는 그런 판단에 전혀 쓰이지 않습니다.",
                "카메라 기록은 담당 교수자가 답변과 함께 참고하는 신뢰도 낮은 자료일 뿐, 자동으로 불합격 처리되지 않습니다. 이 시험을 보려면 카메라가 켜져 있어야 합니다. 카메라가 없으면 시험을 시작할 수 없고, 카메라가 멈추면 시험이 일시 정지됩니다.",
            ]);
        }
        paragraphs.Add(typedAnswers
            ? "담당 교수자가 편의 지원을 제공했습니다. 답변을 말하는 대신 입력합니다. 시험관은 여전히 각 질문을 소리 내어 말하고 화면에도 보여 줍니다. "
              + "카메라는 시험 내내 켜져 있어야 하며, 시선 맞추기 편의 지원을 따로 받지 않았다면 다른 학생과 같은 시선 확인이 적용됩니다. 마이크는 사용하지 않습니다. "
              + "각 답변은 입력한 그대로 보관되며, 답변 칸에 글을 붙여 넣으면 담당 교수자가 검토할 수 있도록 기록됩니다(차단되거나 자동으로 불합격 처리되지 않습니다). "
              + string.Format(pause, "카메라")
            : "이 시험은 말로 진행되며, 시험 내내 마이크와 카메라가 켜져 있어야 합니다. 시험관이 질문을 말하면 소리 내어 답합니다. 입력 답변은 없습니다. "
              + "대화 기록을 만들기 위해 마이크 음성이 음성 인식 서비스로 전송되지만, 음성 자체는 녹음되거나 보관되지 않습니다. "
              + "보관되는 것은 대화 기록(인식 신뢰도 포함), 말하는 방식(속도, 쉼, 머뭇거림, 두 사람 이상의 목소리가 들렸는지 여부), 그리고 학생이 확인한 내용입니다. "
              + "\"두 번째 목소리가 들림\" 같은 기록은 담당 교수자가 약한 증거로 참고할 뿐, 자동으로 불합격 처리되지 않습니다. "
              + string.Format(pause, "마이크나 카메라"));
        return new ConsentNotice("시작하기 전에", paragraphs);
    }

    /// <summary>
    /// The Spanish notice (GAD-T612): the same terms, paragraph for paragraph, in neutral Latin American Spanish addressing the
    /// student as usted. Wording follows docs/LOCALIZATION.md's Spanish glossary (la IA examinadora, intento, transcripción, su docente).
    /// </summary>
    private static ConsentNotice SpanishFor(string organizationName, string examTitle, int retentionDays, IntegrityLevel integrity, bool typedAnswers)
    {
        const string pause = "Si {0} deja de funcionar durante el examen, el examen se pausa hasta que vuelva a activarlo, y el tiempo se detiene mientras está en pausa (hasta 10 minutos en total, o la mitad del tiempo del examen si eso es menos; después, el tiempo sigue corriendo). Su tiempo empieza cuando la IA examinadora hace la primera pregunta.";
        var paragraphs = new List<string>
        {
            $"Este es un examen de tipo oral de {organizationName}: \"{examTitle}\". Una IA examinadora le hace preguntas y retoma sus propias respuestas para ver qué tan bien comprende el material.",
            typedAnswers
                ? "Qué se registra: cada pregunta y las respuestas que usted escribe (con las correcciones que haga), cuándo se hizo y se respondió cada pregunta, y señales de integridad, como una respuesta que parece texto copiado. Son notas para que una persona las revise, nunca un reprobado automático."
                : "Qué se registra: cada pregunta y una transcripción de sus respuestas habladas (con las correcciones que haga), cuándo se hizo y se respondió cada pregunta, y señales de integridad, como una respuesta que parece texto copiado. Son notas para que una persona las revise, nunca un reprobado automático.",
            "Qué no se registra: no se guarda video ni audio. Su cámara le muestra en su propia pantalla junto a la IA examinadora; la imagen no se graba ni se envía.",
            $"Cuánto tiempo se conserva: {organizationName} conserva sus respuestas y transcripciones durante {retentionDays} días y luego las elimina.",
            "Quién decide: la IA solo da una evaluación preliminar. Las personas califican sus respuestas, quien revisa concilia cualquier desacuerdo importante con la IA, y una persona autorizada aprueba y publica su resultado. Puede apelar un resultado publicado.",
            "Su decisión: puede salir del examen en cualquier momento con \"Terminar\". Salir antes envía lo que haya respondido hasta ese momento y usa este intento.",
        };
        if (integrity != IntegrityLevel.Off)
        {
            var what = "cuando cambia a otra pestaña o ventana"
                + (typedAnswers ? ", cuando pega texto en el cuadro de respuesta" : "")
                + (integrity == IntegrityLevel.Strict
                    ? ", cuando copia la pregunta, cuando sale de la pantalla completa y cuando hay más de una pantalla conectada (su navegador puede pedirle permiso para detectar pantallas; puede rechazarlo)"
                    : " y cuando copia la pregunta");
            paragraphs.Insert(2, $"Durante el examen, su navegador también anota {what}: cuándo ocurrió y por cuánto tiempo. No se bloquea nada; son notas para que su docente las revise, nunca un reprobado automático.");
        }
        if (integrity == IntegrityLevel.Strict)
        {
            paragraphs.RemoveAll(p => p.StartsWith("Qué no se registra:", StringComparison.Ordinal));
            paragraphs.InsertRange(paragraphs.FindIndex(p => p.StartsWith("Cuánto tiempo se conserva:", StringComparison.Ordinal)),
            [
                "Cámara: este examen usa su cámara. Su navegador le pedirá permiso. La imagen de la cámara se analiza solo en su propio dispositivo. No se graba, guarda ni envía ningún video, foto, captura de pantalla, audio ni imagen de su rostro, y los datos faciales que se usan para las comprobaciones se mantienen en la memoria de su navegador y se descartan cuando termina el examen.",
                "Qué se envía y se conserva de la cámara: solo notas breves de eventos, cada una con un tipo, una hora de inicio y de fin, un nivel de confianza y una frase sencilla, por ejemplo \"no hubo un rostro a la vista durante 12 segundos\", \"apareció un segundo rostro\", \"miró hacia la izquierda durante 8 segundos\", \"movimientos de los ojos como si leyera algo fuera de la pantalla\" o \"no se usó la cámara\". El dispositivo también comprueba que el mismo rostro siga frente a la cámara; si pudo haber cambiado, se envía una nota que lo indica, como las demás, pero los datos faciales usados para la comprobación nunca salen de su dispositivo. Las miradas breves hacia otro lado mientras piensa se anotan como algo normal. No se saca ninguna conclusión sobre sus emociones, su estado de ánimo ni su honestidad: la cámara no se usa para juzgar nada de eso.",
                "Las notas de la cámara son para que su docente las lea junto con sus respuestas, como evidencia de baja confianza, nunca un reprobado automático. La cámara debe estar encendida para presentar este examen: sin ella, el examen no puede comenzar, y se pausa si la cámara deja de funcionar.",
            ]);
        }
        paragraphs.Add(typedAnswers
            ? "Su docente le ha dado una adaptación: usted escribe sus respuestas en lugar de decirlas. La IA examinadora sigue diciendo cada pregunta en voz alta y la muestra en pantalla. "
              + "Su cámara debe estar encendida durante todo el examen, con la misma comprobación de contacto visual que el resto de estudiantes, salvo que también tenga la adaptación de contacto visual. No se usa su micrófono. "
              + "Cada respuesta se conserva tal como la escribió, y pegar texto en el cuadro de respuesta se anota para que su docente lo revise (nunca se bloquea ni es un reprobado automático). "
              + string.Format(pause, "su cámara")
            : "Este examen es hablado, y su micrófono y su cámara deben estar encendidos durante todo el examen. La IA examinadora dice las preguntas y usted responde en voz alta; no hay respuestas escritas. "
              + "El audio de su micrófono se transmite a un servicio de voz a texto para hacer una transcripción, y el audio en sí no se graba ni se conserva. "
              + "Lo que se conserva es la transcripción (con el nivel de confianza del reconocimiento), cómo se habló (ritmo, pausas, titubeos y si se oyó más de una voz) y lo que usted confirma. "
              + "Notas como \"se oyó una segunda voz\" son para que su docente las lea como evidencia débil, nunca un reprobado automático. "
              + string.Format(pause, "su micrófono o su cámara"));
        return new ConsentNotice("Antes de comenzar", paragraphs);
    }

    /// <summary>The exact text the student accepted, as hashed.</summary>
    public string Text => $"Gadfly exam notice {Version}\n{Title}\n" + string.Join("\n", Paragraphs);

    public string Sha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text))).ToLowerInvariant();
}
