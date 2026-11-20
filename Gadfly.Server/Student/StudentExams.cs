using System.Security.Cryptography;
using System.Text;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Student;

/// <summary>Where one assignment stands for the student, in the words the dashboard shows.</summary>
public enum AssignmentStanding { NotOpenYet, Open, InProgress, Completed, Closed, Unavailable }

/// <summary>Results the instructor released for one assignment (never shown before release).</summary>
public sealed record ReleasedResult(string? Grade, string? Comment, string? Recommendation, DateTimeOffset ReleasedAt);

/// <summary>One row of the student's dashboard (GAD-T51).</summary>
public sealed record StudentAssignmentView
{
    public required Guid AssignmentId { get; init; }
    public required string ExamTitle { get; init; }
    public string? Instructions { get; init; }
    public ExamMode Mode { get; init; }
    public DateTimeOffset? OpensAt { get; init; }
    public DateTimeOffset? ClosesAt { get; init; }
    public int AttemptsUsed { get; init; }
    public int AttemptsAllowed { get; init; }
    public AssignmentStanding Standing { get; init; }
    /// <summary>The attempt in progress (not ended), if any.</summary>
    public Guid? ActiveSessionId { get; init; }
    public SessionState? ActiveSessionState { get; init; }
    public ReleasedResult? Result { get; init; }

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
    private static async Task<T> WithDbAsync<T>(IServiceScopeFactory scopes, Func<GadflyDbContext, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<GadflyDbContext>());
    }

    public static Task<IReadOnlyList<StudentAssignmentView>> ListAsync(
        IServiceScopeFactory scopes, Guid organizationId, Guid studentUserId, DateTimeOffset now, CancellationToken ct = default) =>
        WithDbAsync(scopes, db => ListAsync(db, organizationId, studentUserId, now, ct));

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
                OpensAt = a.OpensAt,
                ClosesAt = a.ClosesAt,
                AttemptsUsed = used,
                AttemptsAllowed = a.AttemptsAllowed,
                ActiveSessionId = active?.Id,
                ActiveSessionState = active?.State,
                Result = result,
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
        if (a.Exam?.Status != ExamStatus.Published) return AssignmentStanding.Unavailable;
        if (a.ClosesAt is { } closes && now > closes) return used > 0 ? AssignmentStanding.Completed : AssignmentStanding.Closed;
        if (hasActive) return AssignmentStanding.InProgress;
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
        WithDbAsync(scopes, async db =>
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
        WithDbAsync(scopes, async db =>
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
    public const string Version = "2026-11-20.1";

    public static ConsentNotice For(string organizationName, string examTitle, int retentionDays, ExamMode mode)
    {
        var paragraphs = new List<string>
        {
            $"This is an oral-style exam for {organizationName}: \"{examTitle}\". An AI examiner asks you questions and follows up on your own answers to see how well you understand the material.",
            "What is recorded: every question and your answers (typed text now; spoken transcripts when voice mode arrives), when each question was asked and answered, and integrity signals such as an answer that reads like copied text. These are notes for a person to review, never an automatic fail.",
            "What is not recorded: no video is stored. In voice mode, any camera checks run on your own device and only short event notes (for example \"face not visible for 10 seconds\") are sent.",
            $"How long it is kept: {organizationName} keeps your answers and transcripts for {retentionDays} days, then they are deleted.",
            "Who decides: the AI only recommends. Your instructor reads your answers and decides your result, and releases it to you when ready.",
            "Your choice: you can leave the exam at any time with \"Finish\". Leaving early submits what you have answered so far and uses this attempt.",
        };
        if (mode == ExamMode.Voice)
            paragraphs.Add("This exam is set up for voice. Voice and camera are not available yet, so you will answer by typing.");
        return new ConsentNotice("Before you begin", paragraphs);
    }

    /// <summary>The exact text the student accepted, as hashed.</summary>
    public string Text => $"Gadfly exam notice {Version}\n{Title}\n" + string.Join("\n", Paragraphs);

    public string Sha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text))).ToLowerInvariant();
}
