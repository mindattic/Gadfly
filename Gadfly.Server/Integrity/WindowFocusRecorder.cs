using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Integrity;

/// <summary>What one recorded absence from the exam window adds up to for the attempt (GAD-T511).</summary>
/// <param name="Count">Absences of a second or more recorded in this attempt so far.</param>
/// <param name="Longest">The longest of them.</param>
/// <param name="NeedsReview">The attempt is marked for an instructor's review (by this absence or an earlier one).</param>
/// <param name="NewlyFlagged">This absence is the one that marked it.</param>
/// <param name="Exempt">An accommodation turns the counting off for this student: absences are still noted, never flagged.</param>
public sealed record WindowAbsence(int Count, TimeSpan Longest, bool NeedsReview, bool NewlyFlagged, bool Exempt);

/// <summary>
/// Leaving the exam window (GAD-T511, docs/research/attention-and-gaze.md 4.4). The exam room pauses at once (the examiner stops and
/// the answer waits) and says so neutrally when the student is back; every absence of a second or more is noted here with how long it
/// lasted and the question it fell in. Three or more absences, or one of a minute or more, mark the attempt as needing an
/// instructor's review (<see cref="ExamSession.NeedsInstructorReview"/>), and the student is told and may explain. Nothing is ever
/// cancelled or deleted automatically: a human decides (GDPR Art. 22(3); CNIL 2023, paras. 38-40; AI Act Art. 86), and focus events
/// have known false triggers (notifications, OS dialogs, assistive technology). Runs for every exam, whatever its integrity level.
/// </summary>
public sealed class WindowFocusRecorder(GadflyDbContext db, TimeProvider clock, ILogger<WindowFocusRecorder> log)
{
    /// <summary>The integrity-event source for these notes (separate from integrity.js's own "browser" notes).</summary>
    public const string Source = "focus";
    public const string AbsenceType = "window-away";
    /// <summary>The student's own explanation, stored beside the notes for the instructor.</summary>
    public const string NoteSource = "student";
    public const string NoteType = "review-note";

    /// <summary>Shorter absences are blips (a click into the browser's own chrome) and are not noted.</summary>
    public static readonly TimeSpan MinAbsence = TimeSpan.FromSeconds(1);
    /// <summary>This many absences in one attempt mark it for review...</summary>
    public const int ReviewAfterCount = 3;
    /// <summary>...as does one absence this long.</summary>
    public static readonly TimeSpan ReviewAfterLong = TimeSpan.FromSeconds(60);
    /// <summary>Longest absence one note may claim.</summary>
    private static readonly TimeSpan MaxSpan = TimeSpan.FromHours(4);
    /// <summary>Longest explanation kept.</summary>
    public const int MaxNoteLength = 1000;
    /// <summary>A notice arriving this long after the session ended is still accepted.</summary>
    private static readonly TimeSpan LateGrace = TimeSpan.FromMinutes(5);

    public static WindowFocusRecorder Create(IServiceProvider services) =>
        services.GetService<WindowFocusRecorder>() ?? ActivatorUtilities.CreateInstance<WindowFocusRecorder>(services);

    /// <summary>
    /// Notes one absence that ended just now and lasted <paramref name="duration"/> (the browser measures it), and marks the attempt for
    /// review when the absences reach the threshold. Null when the session isn't the student's, isn't running, or the absence was a blip.
    /// </summary>
    public async Task<WindowAbsence?> RecordAbsenceAsync(Guid organizationId, Guid studentUserId, Guid sessionId, TimeSpan duration, CancellationToken ct = default)
    {
        if (duration < MinAbsence) return null;
        if (duration > MaxSpan) duration = MaxSpan;
        var now = clock.GetUtcNow();
        var session = await db.ExamSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId, ct);
        if (session is null) return null;
        var accepting = session.State is SessionState.Running or SessionState.Paused
            || (session.State == SessionState.Ended && session.EndedAt is { } ended && now - ended <= LateGrace);
        if (!accepting) return null;

        var exempt = await db.ExamAssignments.AsNoTracking()
            .Where(a => a.Id == session.AssignmentId && a.OrganizationId == organizationId)
            .Select(a => a.TypedAnswerAccommodation || a.GazeAccommodation).FirstOrDefaultAsync(ct);

        var end = now;
        var start = end - duration;
        var windowStart = session.ConsentRecordedAt ?? session.CreatedAt;
        if (start < windowStart) start = windowStart;
        var turn = await db.SessionTurns.AsNoTracking()
            .Where(t => t.SessionId == sessionId && t.OrganizationId == organizationId && t.AskedAt <= start)
            .OrderByDescending(t => t.AskedAt).Select(t => new { t.Id, t.Index }).FirstOrDefaultAsync(ct);

        var span = end - start;
        db.IntegrityEvents.Add(new IntegrityEvent
        {
            OrganizationId = organizationId, SessionId = sessionId, Source = Source, Type = AbsenceType,
            StartedAt = start, EndedAt = end, TurnId = turn?.Id, Confidence = 0.9,
            Severity = span >= ReviewAfterLong ? IntegritySeverity.Low : IntegritySeverity.Info,
            Summary = Describe(span, turn?.Index, exempt),
        });

        var prior = await db.IntegrityEvents.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.OrganizationId == organizationId && e.Source == Source && e.Type == AbsenceType)
            .Select(e => new { e.StartedAt, e.EndedAt }).ToListAsync(ct);
        var count = prior.Count + 1;
        var longest = prior.Select(p => (p.EndedAt ?? p.StartedAt) - p.StartedAt).Append(span).Max();

        var newly = false;
        if (!exempt && !session.NeedsInstructorReview && (count >= ReviewAfterCount || longest >= ReviewAfterLong))
        {
            session.NeedsInstructorReview = true;
            session.ReviewFlaggedAt = now;
            session.ReviewReason = Truncate(ReviewReason(count, longest), ReviewReasonMax);
            newly = true;
        }
        try
        {
            await db.SaveChangesAsync(ct);
            Admin.LiveSessionFeed.Shared.IntegrityRecorded(organizationId, sessionId, now);   // live monitor (GAD-T299)
        }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "Could not note an absence from the exam window for session {SessionId}.", sessionId);
            return null;
        }
        if (newly) log.LogInformation("Session {SessionId} marked for instructor review after {Count} absences from the exam window.", sessionId, count);
        return new WindowAbsence(count, longest, session.NeedsInstructorReview, newly, exempt);
    }

    /// <summary>
    /// The student's explanation of their absences, kept beside the notes for the instructor (never graded, never sent to the AI).
    /// False when the session isn't theirs or the text is empty.
    /// </summary>
    public async Task<bool> SaveNoteAsync(Guid organizationId, Guid studentUserId, Guid sessionId, string? text, CancellationToken ct = default)
    {
        var said = (text ?? "").Trim();
        if (said.Length == 0) return false;
        if (said.Length > MaxNoteLength) said = said[..MaxNoteLength];
        var mine = await db.ExamSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId, ct);
        if (!mine) return false;
        var now = clock.GetUtcNow();
        db.IntegrityEvents.Add(new IntegrityEvent
        {
            OrganizationId = organizationId, SessionId = sessionId, Source = NoteSource, Type = NoteType,
            StartedAt = now, Confidence = 1, Severity = IntegritySeverity.Info,
            Summary = $"The student's explanation of leaving the exam window: \"{said}\"",
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    private const int ReviewReasonMax = 500;

    /// <summary>The reason an instructor reads on the attempt.</summary>
    public static string ReviewReason(int count, TimeSpan longest) =>
        $"The exam window was left {count} time{(count == 1 ? "" : "s")} during the attempt (longest {Duration(longest)}). "
        + "The exam paused each time and nothing was cancelled; leaving the window has innocent causes (a notification, a system dialog, "
        + "assistive technology), so please look at the notes and the answers and decide.";

    /// <summary>The note a reviewer reads for one absence.</summary>
    public static string Describe(TimeSpan span, int? turnIndex, bool exempt) =>
        $"The student left the exam window for {Duration(span)}{(turnIndex is { } i ? $" during question {i + 1}" : "")}. "
        + "The examiner stopped and the answer waited; on return the exam said it had paused and she repeated the end of the question. "
        + "Browser focus events have innocent causes too (a notification, a system dialog, assistive technology)."
        + (exempt ? " The student has an accommodation, so absences are noted but never mark the attempt for review." : "");

    private static string Duration(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{Math.Max(1, (int)Math.Round(t.TotalSeconds))} s";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
