using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Integrity;

/// <summary>What one recorded absence from the exam window adds up to for the attempt (GAD-T511).</summary>
/// <param name="Count">Absences of a second or more recorded in this attempt so far.</param>
/// <param name="Longest">The longest of them.</param>
/// <param name="NeedsReview">The absences so far reach the window-absence threshold, so the attempt is marked for review because of them (by
/// this absence or an earlier one). Only window absences count (GAD-T615): a review flag set for another reason, such as the calibration
/// review every Korean attempt gets (GAD-T536), is never reported here, so the student is never told about it.</param>
/// <param name="NewlyFlagged">This absence is the one that marked it.</param>
/// <param name="Exempt">An accommodation turns the counting off for this student: absences are still noted, never flagged.</param>
public sealed record WindowAbsence(int Count, TimeSpan Longest, bool NeedsReview, bool NewlyFlagged, bool Exempt);

/// <summary>
/// When absences from the exam window mark an attempt for review (GAD-T604): the standard thresholds, raised ones from the
/// window-absence accommodation, or never (<see cref="Off"/>).
/// </summary>
public sealed record WindowAbsenceRule(bool Off, int ReviewAfterCount, TimeSpan ReviewAfterLong)
{
    public static readonly WindowAbsenceRule Standard = new(false, WindowFocusRecorder.ReviewAfterCount, WindowFocusRecorder.ReviewAfterLong);

    /// <summary>The assignment's rule: the accommodation's values, never below the standard ones.</summary>
    public static WindowAbsenceRule For(bool off, int? count, int? seconds) => new(off,
        Math.Max(WindowFocusRecorder.ReviewAfterCount, count ?? 0),
        TimeSpan.FromSeconds(Math.Max(WindowFocusRecorder.ReviewAfterLong.TotalSeconds, seconds ?? 0)));

    public bool IsStandard => !Off && ReviewAfterCount == WindowFocusRecorder.ReviewAfterCount && ReviewAfterLong == WindowFocusRecorder.ReviewAfterLong;

    /// <summary>Whether <paramref name="count"/> absences with the longest <paramref name="longest"/> mark the attempt.</summary>
    public bool Flags(int count, TimeSpan longest) => !Off && (count >= ReviewAfterCount || longest >= ReviewAfterLong);

    /// <summary>The rule in plain words, for staff.</summary>
    public string Describe() => Off
        ? "never marks the attempt for review (accommodation)"
        : $"marks the attempt for review after {ReviewAfterCount} absences or one of {WindowFocusRecorder.Duration(ReviewAfterLong)}"
          + (IsStandard ? "" : " (accommodation)");
}

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

        // GAD-T604: the window-absence accommodation decides (it replaced GAD-T511's stopgap, which exempted the typed-answer and
        // eye-contact accommodations: those are about the answer channel and the gaze, not about leaving the window).
        var rule = await RuleAsync(db, organizationId, session.AssignmentId, ct);
        var exempt = rule.Off;

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
            Severity = span >= rule.ReviewAfterLong && !rule.Off ? IntegritySeverity.Low : IntegritySeverity.Info,
            Summary = Describe(span, turn?.Index, exempt),
        });

        var prior = await db.IntegrityEvents.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.OrganizationId == organizationId && e.Source == Source && e.Type == AbsenceType)
            .Select(e => new { e.StartedAt, e.EndedAt }).ToListAsync(ct);
        var count = prior.Count + 1;
        var longest = prior.Select(p => (p.EndedAt ?? p.StartedAt) - p.StartedAt).Append(span).Max();

        var newly = false;
        if (!session.NeedsInstructorReview && rule.Flags(count, longest))
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
        // The window's own threshold, never session.NeedsInstructorReview: that flag may have been set for another reason (GAD-T615).
        return new WindowAbsence(count, longest, rule.Flags(count, longest), newly, exempt);
    }

    /// <summary>The window-absence rule for one assignment (GAD-T604); the standard one when the assignment is gone.</summary>
    public static async Task<WindowAbsenceRule> RuleAsync(GadflyDbContext db, Guid organizationId, Guid assignmentId, CancellationToken ct)
    {
        var a = await db.ExamAssignments.AsNoTracking()
            .Where(x => x.Id == assignmentId && x.OrganizationId == organizationId)
            .Select(x => new { x.WindowAbsenceFlagOff, x.WindowAbsenceReviewAfterCount, x.WindowAbsenceReviewAfterSeconds }).FirstOrDefaultAsync(ct);
        return a is null ? WindowAbsenceRule.Standard : WindowAbsenceRule.For(a.WindowAbsenceFlagOff, a.WindowAbsenceReviewAfterCount, a.WindowAbsenceReviewAfterSeconds);
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

    public static string Duration(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{Math.Max(1, (int)Math.Round(t.TotalSeconds))} s";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
