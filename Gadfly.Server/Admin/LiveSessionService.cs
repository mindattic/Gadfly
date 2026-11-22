using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Review;
using Gadfly.Server.Sessions;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Admin;

/// <summary>Where a live exam's connection stands, as the monitor can tell (GAD-T299).</summary>
public enum LiveConnection
{
    /// <summary>The student's exam room is open on this server.</summary>
    Connected,
    /// <summary>The exam room is not open here, but the exam took a step recently.</summary>
    Recent,
    /// <summary>Nothing heard from the exam for a while: the student may have closed the page or lost their connection.</summary>
    Quiet,
}

/// <summary>One integrity note as the monitor lists it. A low-confidence note for a reviewer, never a verdict.</summary>
public sealed record LiveIntegrityNote(Guid SessionId, string Label, string Summary, IntegritySeverity Severity, DateTimeOffset At);

/// <summary>One exam in progress on the live monitor.</summary>
/// <param name="QuestionNumber">The question on screen, counting from 1; null before the first one.</param>
/// <param name="Remaining">Time left now; null while the clock waits for the first question.</param>
public sealed record LiveSessionRow(
    Guid SessionId,
    Guid StudentUserId,
    string StudentName,
    Guid ExamId,
    string ExamTitle,
    int Attempt,
    SessionState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    int? QuestionNumber,
    string? Move,
    string? ConceptTitle,
    TimeSpan? Remaining,
    bool ClockStarted,
    bool DevicePaused,
    bool HeldByStaff,
    int ObjectivesClosed,
    int ObjectivesTotal,
    LiveConnection Connection,
    DateTimeOffset? LastActivity,
    int IntegrityCount,
    IReadOnlyList<LiveIntegrityNote> RecentNotes)
{
    /// <summary>The exam has begun (the clock started): staff can pause, resume, extend or end it.</summary>
    public bool CanAct => State is SessionState.Running or SessionState.Paused && StartedAt is not null;

    /// <summary>Where the exam stands, in a few words.</summary>
    public string Status => HeldByStaff ? "Paused by staff"
        : State == SessionState.Created ? "Reading the exam notice"
        : State == SessionState.ConsentGiven || !ClockStarted ? "Getting ready"
        : DevicePaused ? "Paused: microphone, camera or examiner lost"
        : "In progress";
}

/// <summary>Everything the live monitor shows.</summary>
public sealed record LiveSessionsView(DateTimeOffset GeneratedAt, IReadOnlyList<LiveSessionRow> Sessions)
{
    public int Running => Sessions.Count(s => s.State is SessionState.Running or SessionState.Paused);
    public int Paused => Sessions.Count(s => s.HeldByStaff || s.DevicePaused);
}

/// <summary>The result of a staff action on a live exam.</summary>
public sealed record LiveActionResult(bool Ok, string Message);

/// <summary>
/// The admin live sessions monitor (GAD-T299): the exams in progress in the caller's organization, with where each stands, and the staff
/// actions on one: extend time, pause, resume, end, and send the student a short notice. The monitor reads each exam's state from the
/// in-process <see cref="LiveSessionFeed"/> (pushed by the orchestrator on every step) and falls back to the saved transcript after a
/// restart, so it does not need to poll the database for every change. Every action needs a reason, goes through the orchestrator (the
/// same gate, save and end path the student's own steps use, so an ended exam is graded like any other), is written to the audit log
/// with the session's id, and appears on the session's review timeline (<see cref="ReviewService"/>). No audio or video is read or shown.
/// </summary>
public sealed class LiveSessionService(GadflyDbContext db, ITenantContext tenant, TimeProvider clock, SessionOrchestrator orchestrator, LiveSessionFeed? liveFeed = null)
{
    /// <summary>Audit actions written by the monitor; the review timeline shows every action starting with <see cref="AuditPrefix"/>.</summary>
    public const string AuditPrefix = "session.staff.";
    public const string ExtendedAction = AuditPrefix + "extended";
    public const string PausedAction = AuditPrefix + "paused";
    public const string ResumedAction = AuditPrefix + "resumed";
    public const string EndedAction = AuditPrefix + "ended";
    public const string NoticeAction = AuditPrefix + "notice";
    /// <summary>Written by <see cref="Sessions.StaleSessionSweep"/> (GAD-T306), not by a staff member: an abandoned session closed on
    /// its own once it sat well past its deadline. Shares the staff-action prefix so it appears on the same review timeline.</summary>
    public const string AutoClosedAction = AuditPrefix + "auto-closed";

    /// <summary>The most extra time one action can add.</summary>
    public const int MaxExtendMinutesPerAction = 60;

    /// <summary>The longest notice a student is sent.</summary>
    public const int MaxNoticeLength = 300;

    /// <summary>Most recent integrity notes listed under each exam.</summary>
    public const int NotesPerSession = 3;

    /// <summary>The exam counts as recently active for this long after its last step.</summary>
    public static readonly TimeSpan RecentFor = TimeSpan.FromMinutes(3);

    private static readonly SessionState[] Live = [SessionState.Created, SessionState.ConsentGiven, SessionState.Running, SessionState.Paused];

    private readonly LiveSessionFeed feed = liveFeed ?? LiveSessionFeed.Shared;

    private AdminGate Gate => field ??= new AdminGate(db, tenant, clock);

    /// <summary>Writes the audit rows with the session's id, so the review timeline can find them.</summary>
    private Assessments.AssessmentGuard Auditor => field ??= new Assessments.AssessmentGuard(db, tenant, clock);

    /// <summary>True when the caller may use the monitor (the organization's Owner or Admin).</summary>
    public bool Allowed => Gate.Allowed;

    // ------------------------------------------------------------------ reading

    /// <summary>The exams in progress in the caller's organization, oldest first.</summary>
    public async Task<LiveSessionsView> LoadAsync(CancellationToken ct = default)
    {
        Gate.EnsureReader();
        var org = Gate.Org;
        var now = clock.GetUtcNow();

        var sessions = await (from s in db.ExamSessions.AsNoTracking()
                              join e in db.Exams.AsNoTracking() on s.ExamId equals e.Id
                              where s.OrganizationId == org && Live.Contains(s.State)
                              orderby s.StartedAt ?? s.CreatedAt
                              select new { s.Id, s.StudentUserId, s.ExamId, e.Title, s.Attempt, s.State, s.CreatedAt, s.StartedAt })
            .Take(500).ToListAsync(ct);
        if (sessions.Count == 0) return new LiveSessionsView(now, []);

        var ids = sessions.Select(s => s.Id).ToList();
        var studentIds = sessions.Select(s => s.StudentUserId).Distinct().ToList();
        var names = await db.AuthUsers.AsNoTracking().Where(u => studentIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => AdminPeople.NameOf(u.UserName, u.DisplayName), ct);

        // Exams this server has not seen step since it started: read their saved transcripts once, then keep them in the feed.
        var missing = ids.Where(id => feed.Snapshot(org, id) is null).ToList();
        if (missing.Count > 0)
        {
            var saved = await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == org && missing.Contains(s.Id) && s.TranscriptJson != null)
                .Select(s => new { s.Id, s.StudentUserId, s.State, s.TranscriptJson }).ToListAsync(ct);
            foreach (var row in saved)
                if (FromTranscript(org, row.Id, row.StudentUserId, row.State, row.TranscriptJson, now) is { } snap) feed.Remember(snap);
        }

        var counts = await db.IntegrityEvents.AsNoTracking().Where(e => e.OrganizationId == org && ids.Contains(e.SessionId))
            .GroupBy(e => e.SessionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var recent = await db.IntegrityEvents.AsNoTracking().Where(e => e.OrganizationId == org && ids.Contains(e.SessionId))
            .OrderByDescending(e => e.StartedAt).Take(Math.Min(500, ids.Count * 10))
            .Select(e => new { e.SessionId, e.Type, e.Source, e.Summary, e.Severity, e.StartedAt }).ToListAsync(ct);
        var notes = recent.GroupBy(e => e.SessionId).ToDictionary(g => g.Key, g => (IReadOnlyList<LiveIntegrityNote>)g.Take(NotesPerSession)
            .Select(e => new LiveIntegrityNote(e.SessionId, IntegrityExplanations.For(e.Type, e.Source).Label, e.Summary, e.Severity, e.StartedAt)).ToList());

        var rows = sessions.Select(s =>
        {
            var snap = feed.Snapshot(org, s.Id);
            var rooms = feed.RoomsOpen(org, s.Id);
            var last = snap?.At ?? s.StartedAt ?? s.CreatedAt;
            var connection = rooms > 0 ? LiveConnection.Connected : now - last <= RecentFor ? LiveConnection.Recent : LiveConnection.Quiet;
            var clockStarted = snap?.ClockStarted ?? s.StartedAt is not null && s.State is SessionState.Running or SessionState.Paused;
            return new LiveSessionRow(s.Id, s.StudentUserId, names.GetValueOrDefault(s.StudentUserId) ?? "(unknown student)", s.ExamId, s.Title,
                s.Attempt, s.State, s.CreatedAt, s.StartedAt,
                snap?.TurnIndex is { } turn ? turn + 1 : null, snap?.Move, snap?.ConceptTitle,
                snap is not null && clockStarted ? snap.RemainingAt(now) : null,
                clockStarted, snap?.DevicePaused ?? false, snap?.HeldByStaff ?? s.State == SessionState.Paused,
                snap?.ObjectivesClosed ?? 0, snap?.ObjectivesTotal ?? 0,
                connection, snap?.At, counts.GetValueOrDefault(s.Id), notes.GetValueOrDefault(s.Id) ?? []);
        }).ToList();
        return new LiveSessionsView(now, rows);
    }

    /// <summary>A snapshot rebuilt from a saved transcript (after a restart); null when it can't be read.</summary>
    internal static LiveSnapshot? FromTranscript(Guid org, Guid sessionId, Guid student, SessionState state, string? transcriptJson, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(transcriptJson)) return null;
        try
        {
            var s = SessionTranscriptSerializer.Deserialize(transcriptJson);
            var open = s.IsEnded ? null : s.OpenTurn;
            return new LiveSnapshot(sessionId, org, student, s.IsEnded ? SessionState.Ended : state, open?.Index ?? (s.Turns.Count > 0 ? s.Turns.Count - 1 : null),
                open?.Move.ToString(), null, s.Remaining(now), !s.ClockHeld, s.IsPaused && !s.IsEnded, s.IsHeld && !s.IsEnded, 0, 0, now);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ staff actions

    /// <summary>Adds <paramref name="minutes"/> to the exam's time (1 to 60 per action, 2 hours in all). Audited.</summary>
    public async Task<LiveActionResult> ExtendAsync(Guid sessionId, int minutes, string? reason, CancellationToken ct = default)
    {
        if (minutes < 1 || minutes > MaxExtendMinutesPerAction)
            return new(false, $"Add between 1 and {MaxExtendMinutesPerAction} minutes at a time.");
        var added = TimeSpan.Zero;
        return await ActAsync(sessionId, reason, "Extending an exam's time", ExtendedAction,
            (s, _) => added = s.ExtendTime(TimeSpan.FromMinutes(minutes)),
            step => added <= TimeSpan.Zero
                ? null
                : new { minutesRequested = minutes, minutesAdded = (int)Math.Round(added.TotalMinutes), remaining = step.Remaining.ToString(@"hh\:mm\:ss") },
            step => $"Your instructor gave you {Minutes(added)} more. You now have {Minutes(step.Remaining)} left.",
            step => added <= TimeSpan.Zero
                ? $"This exam already has the most extra time allowed ({Minutes(SocraticSession.MaxExtraTime)} in all); nothing was added."
                : added < TimeSpan.FromMinutes(minutes)
                    ? $"Added {Minutes(added)}: that reaches the most extra time allowed ({Minutes(SocraticSession.MaxExtraTime)} in all)."
                    : $"Added {Minutes(added)}. {Minutes(step.Remaining)} left.", ct);
    }

    /// <summary>Pauses the exam: its clock stands still and answers wait until it is resumed. Audited.</summary>
    public Task<LiveActionResult> PauseAsync(Guid sessionId, string? reason, CancellationToken ct = default)
    {
        var changed = false;
        return ActAsync(sessionId, reason, "Pausing an exam", PausedAction,
            (s, now) => changed = s.Hold(now),
            step => changed ? new { remaining = step.Remaining.ToString(@"hh\:mm\:ss") } : null,
            _ => "Your instructor paused your exam. Your time is stopped. Please wait here; you can carry on once they resume it.",
            _ => changed ? "Paused. The student's clock is stopped until you resume it." : "That exam is already paused (or its clock has not started yet).", ct);
    }

    /// <summary>Resumes an exam staff paused, with the paused time added back. Audited.</summary>
    public Task<LiveActionResult> ResumeAsync(Guid sessionId, string? reason, CancellationToken ct = default)
    {
        var changed = false;
        return ActAsync(sessionId, reason, "Resuming an exam", ResumedAction,
            (s, now) => changed = s.Release(now),
            step => changed ? new { remaining = step.Remaining.ToString(@"hh\:mm\:ss") } : null,
            step => $"Your instructor resumed your exam. You have {Minutes(step.Remaining)} left.",
            step => changed ? $"Resumed. {Minutes(step.Remaining)} left." : "That exam was not paused by staff.", ct);
    }

    /// <summary>
    /// Ends the exam through the orchestrator's normal end path (an answer waiting to be judged is judged as recorded, the report is
    /// written and grading starts as for any other ended attempt). Audited.
    /// </summary>
    public async Task<LiveActionResult> EndAsync(Guid sessionId, string? reason, CancellationToken ct = default)
    {
        var (session, refusal) = await FindAsync(sessionId, reason, "Ending an exam", ct);
        if (session is null) return refusal!;
        var why = AdminPeople.Reason(reason)!;
        try
        {
            var step = await orchestrator.EndAsync(Gate.Org, sessionId, session.StudentUserId, EndReason.Abandoned, ct);
            Auditor.Audit(EndedAction, "ExamSession", sessionId.ToString(), new { state = session.State.ToString() },
                new { state = step.State.ToString(), endReason = step.EndReason }, why, sessionId);
            await db.SaveChangesAsync(ct);
            feed.StaffAction(Gate.Org, sessionId, "Your instructor ended your exam. Your answers so far were submitted.", clock.GetUtcNow(),
                LiveSnapshot.From(Gate.Org, session.StudentUserId, step, clock.GetUtcNow()));
            return new(true, "Ended. The attempt is graded like any other: it is in the grading queue now.");
        }
        catch (SessionRefusedException ex)
        {
            return new(false, ex.Message);
        }
    }

    /// <summary>Shows the student a short notice in their exam room (text only: it is never spoken or recorded). Audited.</summary>
    public async Task<LiveActionResult> SendNoticeAsync(Guid sessionId, string? text, CancellationToken ct = default)
    {
        var notice = (text ?? "").ReplaceLineEndings(" ").Trim();
        if (notice.Length == 0) return new(false, "Write the notice the student should see.");
        if (notice.Length > MaxNoticeLength) return new(false, $"Keep the notice under {MaxNoticeLength} characters.");
        if (!await Gate.RequireAsync("Sending a student a notice", "ExamSession", sessionId.ToString(), ct)) return new(false, AdminGate.DeniedMessage);
        var org = Gate.Org;
        var session = await db.ExamSessions.AsNoTracking().Where(s => s.Id == sessionId && s.OrganizationId == org)
            .Select(s => new { s.State }).FirstOrDefaultAsync(ct);
        if (session is null) return new(false, "That exam was not found in this organization.");
        if (!Live.Contains(session.State)) return new(false, "That exam has already ended.");
        // The notice is the reason: what was said is the record.
        Auditor.Audit(NoticeAction, "ExamSession", sessionId.ToString(), after: new { text = notice }, reason: notice, sessionId: sessionId);
        await db.SaveChangesAsync(ct);
        feed.Notice(org, sessionId, notice, clock.GetUtcNow());
        return new(true, feed.RoomsOpen(org, sessionId) > 0
            ? "Sent. It is on the student's screen now."
            : "Sent. The student's exam room is not open on this server right now; they will see it if they come back within a few minutes.");
    }

    private async Task<LiveActionResult> ActAsync(Guid sessionId, string? reason, string attempted, string action,
        Action<SocraticSession, DateTimeOffset> act, Func<SessionStep, object?> audit, Func<SessionStep, string> toStudent, Func<SessionStep, string> toStaff,
        CancellationToken ct)
    {
        var (session, refusal) = await FindAsync(sessionId, reason, attempted, ct);
        if (session is null) return refusal!;
        if (session.StartedAt is null || session.State is not (SessionState.Running or SessionState.Paused))
            return new(false, "That exam has not started yet: its clock starts when the examiner asks the first question.");
        try
        {
            var step = await orchestrator.StaffStepAsync(Gate.Org, sessionId, session.StudentUserId, act, ct);
            var after = audit(step);
            if (after is not null)
            {
                Auditor.Audit(action, "ExamSession", sessionId.ToString(), null, after, AdminPeople.Reason(reason), sessionId);
                await db.SaveChangesAsync(ct);
                feed.StaffAction(Gate.Org, sessionId, toStudent(step), clock.GetUtcNow(), LiveSnapshot.From(Gate.Org, session.StudentUserId, step, clock.GetUtcNow()));
            }
            return new(after is not null, toStaff(step));
        }
        catch (SessionRefusedException ex)
        {
            return new(false, ex.Message);
        }
    }

    private sealed record Found(Guid StudentUserId, SessionState State, DateTimeOffset? StartedAt);

    /// <summary>The session in the caller's organization, after the permission and reason checks.</summary>
    private async Task<(Found? Session, LiveActionResult? Refusal)> FindAsync(Guid sessionId, string? reason, string attempted, CancellationToken ct)
    {
        if (!await Gate.RequireAsync(attempted, "ExamSession", sessionId.ToString(), ct)) return (null, new(false, AdminGate.DeniedMessage));
        if (AdminPeople.Reason(reason) is null) return (null, new(false, "Say why (a sentence). It goes in the audit log and on the session's review."));
        var org = Gate.Org;
        var found = await db.ExamSessions.AsNoTracking().Where(s => s.Id == sessionId && s.OrganizationId == org)
            .Select(s => new Found(s.StudentUserId, s.State, s.StartedAt)).FirstOrDefaultAsync(ct);
        if (found is null) return (null, new(false, "That exam was not found in this organization."));
        if (!Live.Contains(found.State)) return (null, new(false, "That exam has already ended."));
        return (found, null);
    }

    /// <summary>A staff action's audit row as the review timeline shows it.</summary>
    public static StaffActionView ToTimeline(AuditEvent a, string? byName)
    {
        System.Text.Json.Nodes.JsonNode? after = null;
        try { after = string.IsNullOrWhiteSpace(a.AfterJson) ? null : System.Text.Json.Nodes.JsonNode.Parse(a.AfterJson); }
        catch (System.Text.Json.JsonException) { }
        string? Field(string name) => after?[name]?.ToString();
        var (label, detail, reason) = a.Action switch
        {
            ExtendedAction => ("Staff added time", Field("minutesAdded") is { } m ? $"{m} minute{(m == "1" ? "" : "s")} added" : null, a.Reason),
            PausedAction => ("Staff paused the exam", "The clock stood still and answers waited until it was resumed.", a.Reason),
            ResumedAction => ("Staff resumed the exam", "The paused time was added back.", a.Reason),
            EndedAction => ("Staff ended the exam", "Ended early from the live monitor; the answers so far were submitted and graded as usual.", a.Reason),
            NoticeAction => ("Staff sent the student a notice", Field("text") is { } t ? $"“{t}”" : null, (string?)null),
            AutoClosedAction => ("Closed automatically", "Nobody came back to it and it sat well past its own time limit; graded as usual.", a.Reason),
            _ => ("Staff action", a.Action, a.Reason),
        };
        return new StaffActionView(a.At, label, detail, reason, byName ?? (a.Action == AutoClosedAction ? "System (automatic)" : "(unknown)"));
    }

    /// <summary>A span as whole minutes in words ("1 minute", "25 minutes"); under a minute reads "less than a minute".</summary>
    public static string Minutes(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return "less than a minute";
        var m = (int)Math.Round(span.TotalMinutes);
        return m == 1 ? "1 minute" : $"{m} minutes";
    }

    /// <summary>A countdown as the monitor shows it: "12:05" or "1:02:30".</summary>
    public static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
    }
}
