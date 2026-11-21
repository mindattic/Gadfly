using System.Security.Cryptography;
using System.Text;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Student;

/// <summary>Where one assignment stands for the student, in the words the dashboard shows.</summary>
/// <remarks>
/// <see cref="Archived"/>: the instructor archived the exam, so no new attempt can start; an attempt
/// already in progress stays <see cref="InProgress"/> and can be resumed (GAD-T70).
/// </remarks>
public enum AssignmentStanding { NotOpenYet, Open, InProgress, Completed, Closed, Unavailable, Archived }

/// <summary>Results the instructor released for one assignment (never shown before release).</summary>
public sealed record ReleasedResult(string? Grade, string? Comment, string? Recommendation, DateTimeOffset ReleasedAt);

/// <summary>One row of the student's dashboard (GAD-T51).</summary>
public sealed record StudentAssignmentView
{
    public required Guid AssignmentId { get; init; }
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
        WithDbAsync(scopes, organizationId, db => ListAsync(db, organizationId, studentUserId, now, ct));

    private static async Task<IReadOnlyList<StudentAssignmentView>> ListAsync(
        GadflyDbContext db, Guid organizationId, Guid studentUserId, DateTimeOffset now, CancellationToken ct)
    {
        var assignments = await db.ExamAssignments.AsNoTracking().Include(a => a.Exam)
            .Where(a => a.OrganizationId == organizationId && a.StudentUserId == studentUserId)
            .ToListAsync(ct);
        if (assignments.Count == 0) return [];

        var ids = assignments.Select(a => a.Id).ToList();
        var sessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.StudentUserId == studentUserId && ids.Contains(s.AssignmentId))
            .Select(s => new { s.Id, s.AssignmentId, s.Attempt, s.State, s.EndedAt })
            .ToListAsync(ct);

        // Results only for assignments the instructor released.
        var releasedIds = assignments.Where(a => a.ResultsReleasedAt is not null).Select(a => a.Id).ToHashSet();
        var releasedSessionIds = sessions.Where(s => releasedIds.Contains(s.AssignmentId)).Select(s => s.Id).ToList();
        var grades = releasedSessionIds.Count == 0 ? [] : await db.GradeDecisions.AsNoTracking()
            .Where(g => g.OrganizationId == organizationId && releasedSessionIds.Contains(g.SessionId))
            .ToListAsync(ct);
        var reports = releasedSessionIds.Count == 0 ? [] : await db.SessionReports.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && releasedSessionIds.Contains(r.SessionId))
            .Select(r => new { r.SessionId, r.Recommendation, r.CreatedAt })
            .ToListAsync(ct);

        var rows = new List<StudentAssignmentView>();
        foreach (var a in assignments)
        {
            var mine = sessions.Where(s => s.AssignmentId == a.Id).OrderBy(s => s.Attempt).ToList();
            var active = mine.FirstOrDefault(s => s.State is not (SessionState.Ended or SessionState.Failed));
            var used = mine.Count;

            ReleasedResult? result = null;
            if (a.ResultsReleasedAt is { } released)
            {
                // The decided attempt: the latest one the instructor graded, else the latest finished one.
                var mineIds = mine.Select(s => s.Id).ToHashSet();
                var grade = grades.Where(g => mineIds.Contains(g.SessionId)).OrderByDescending(g => g.DecidedAt).FirstOrDefault();
                var sessionId = grade?.SessionId ?? mine.Where(s => s.State == SessionState.Ended).OrderByDescending(s => s.Attempt).Select(s => (Guid?)s.Id).FirstOrDefault();
                var report = reports.Where(r => r.SessionId == sessionId).OrderByDescending(r => r.CreatedAt).FirstOrDefault();
                if (grade is not null || report is not null)
                    result = new ReleasedResult(grade?.Grade, grade?.Comment, report?.Recommendation, released);
            }

            rows.Add(new StudentAssignmentView
            {
                AssignmentId = a.Id,
                ExamTitle = a.Exam?.Title ?? "Exam",
                Instructions = a.Exam?.Instructions,
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
                Standing = StandingOf(a, active is not null, used, now),
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

    /// <summary>One of the student's own assignments, or null when it isn't theirs.</summary>
    public static async Task<StudentAssignmentView?> FindAsync(
        IServiceScopeFactory scopes, Guid organizationId, Guid studentUserId, Guid assignmentId, DateTimeOffset now, CancellationToken ct = default) =>
        (await ListAsync(scopes, organizationId, studentUserId, now, ct)).FirstOrDefault(r => r.AssignmentId == assignmentId);

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

    /// <summary>A student-readable label for the AI's recommendation (the instructor decides the grade).</summary>
    public static string DescribeRecommendation(string? recommendation) => recommendation switch
    {
        null or "" => "",
        "mastered" => "Understanding shown",
        "partial" => "Partly shown",
        "not-demonstrated" => "Not yet shown",
        var other => other,
    };

    /// <summary>The session's owner-checked state, or null when it isn't the student's.</summary>
    public static Task<(SessionState State, Guid AssignmentId, string ExamTitle)?> SessionAsync(
        IServiceScopeFactory scopes, Guid organizationId, Guid studentUserId, Guid sessionId, CancellationToken ct = default) =>
        WithDbAsync(scopes, organizationId, async db =>
    {
        var row = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId)
            .Select(s => new { s.State, s.AssignmentId, s.ExamId })
            .FirstOrDefaultAsync(ct);
        if (row is null) return ((SessionState, Guid, string)?)null;
        var title = await db.Exams.AsNoTracking().Where(e => e.Id == row.ExamId && e.OrganizationId == organizationId)
            .Select(e => e.Title).FirstOrDefaultAsync(ct);
        return ((SessionState, Guid, string)?)(row.State, row.AssignmentId, title ?? "Exam");
    });

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
    public const string Version = "2026-11-20.4";

    public static ConsentNotice For(string organizationName, string examTitle, int retentionDays, ExamMode mode,
        IntegrityLevel integrity = IntegrityLevel.Standard)
    {
        var paragraphs = new List<string>
        {
            $"This is an oral-style exam for {organizationName}: \"{examTitle}\". An AI examiner asks you questions and follows up on your own answers to see how well you understand the material.",
            "What is recorded: every question and your answers (typed text, or spoken transcripts if you use voice mode), when each question was asked and answered, and integrity signals such as an answer that reads like copied text. These are notes for a person to review, never an automatic fail.",
            "What is not recorded: no video is stored. In voice mode, any camera checks run on your own device and only short event notes (for example \"face not visible for 10 seconds\") are sent.",
            $"How long it is kept: {organizationName} keeps your answers and transcripts for {retentionDays} days, then they are deleted.",
            "Who decides: the AI only recommends. Your instructor reads your answers and decides your result, and releases it to you when ready.",
            "Your choice: you can leave the exam at any time with \"Finish\". Leaving early submits what you have answered so far and uses this attempt.",
        };
        paragraphs.InsertRange(2, BrowserSignals(integrity, mode));
        if (integrity == IntegrityLevel.Strict)
        {
            // The camera is on for this exam: say plainly what is and isn't collected (GAD-T57), instead of the voice-mode line.
            paragraphs.RemoveAll(p => p.StartsWith("What is not recorded:", StringComparison.Ordinal));
            paragraphs.InsertRange(paragraphs.FindIndex(p => p.StartsWith("How long it is kept:", StringComparison.Ordinal)), CameraNotice());
        }
        if (mode == ExamMode.Voice)
            paragraphs.Add("This exam is set up for voice. If you turn voice mode on, the examiner speaks the questions and you answer out loud. "
                + "Your microphone audio is streamed to a speech-to-text service to make a transcript, and the audio itself is not recorded or kept. "
                + "What is kept is the transcript (with how confident the recognizer was), how it was spoken (pace, pauses, hesitations, and whether more than one voice was heard), and what you confirm. "
                + "Notes such as \"a second voice was heard\" are for your instructor to read as weak evidence, never an automatic fail. "
                + "You can type your answers instead at any time, and the microphone is only used while voice mode is on.");
        return new ConsentNotice("Before you begin", paragraphs);
    }

    /// <summary>
    /// The browser integrity signals this exam notes (GAD-T56), in the same words the exam room shows;
    /// none when the exam's integrity level is Off.
    /// </summary>
    private static IEnumerable<string> BrowserSignals(IntegrityLevel integrity, ExamMode mode)
    {
        if (integrity == IntegrityLevel.Off) yield break;
        var what = "switching to another tab or window"
            + (mode == ExamMode.Text ? ", pasting into the answer box" : "")
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
        yield return "What is sent and kept from the camera: only short event notes, each with a type, a start and end time, a confidence and one plain sentence, for example \"no face in view for 12 seconds\", \"a second face appeared\", \"looked to the left for 8 seconds\", \"eye movements like reading something off-screen\", or \"the camera was not used\". The device also checks that the same face is in front of the camera throughout; that check is never sent. Short glances away while you think are noted as normal. Nothing is made of your emotions, mood or honesty: the camera is not used to judge any of that.";
        yield return "Camera notes are for your instructor to read alongside your answers, as low-confidence evidence, never an automatic fail. You can say no when your browser asks to use the camera: the exam goes ahead normally and a neutral note records that the camera was not used.";
    }

    /// <summary>The exact text the student accepted, as hashed.</summary>
    public string Text => $"Gadfly exam notice {Version}\n{Title}\n" + string.Join("\n", Paragraphs);

    public string Sha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text))).ToLowerInvariant();
}
