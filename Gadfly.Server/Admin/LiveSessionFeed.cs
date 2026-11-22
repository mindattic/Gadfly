using System.Collections.Concurrent;
using Gadfly.Server.Data;
using Gadfly.Server.Sessions;

namespace Gadfly.Server.Admin;

/// <summary>What happened on a live exam (GAD-T299).</summary>
public enum LiveEventKind
{
    /// <summary>The exam took a step (a question, an answer, a clock change): <see cref="LiveSessionEvent.Snapshot"/> is the new state.</summary>
    Stepped,
    /// <summary>New integrity notes were stored for the session.</summary>
    Integrity,
    /// <summary>The student's exam room opened or closed (<see cref="LiveSessionFeed.RoomsOpen"/> changed).</summary>
    Room,
    /// <summary>Staff sent the student a short notice (<see cref="LiveSessionEvent.Text"/>).</summary>
    Notice,
    /// <summary>Staff paused, resumed, extended or ended the exam (<see cref="LiveSessionEvent.Text"/> says what, for the student).</summary>
    StaffAction,
}

/// <summary>
/// Where a live exam stands after its last step, as the orchestrator saw it (GAD-T299). Kept in memory only, never stored.
/// </summary>
/// <param name="Remaining">Time left at <paramref name="At"/>.</param>
/// <param name="ClockRunning">False while the clock stands still (held for the first question, a device pause, a staff pause).</param>
public sealed record LiveSnapshot(
    Guid SessionId,
    Guid OrganizationId,
    Guid StudentUserId,
    SessionState State,
    int? TurnIndex,
    string? Move,
    string? ConceptTitle,
    TimeSpan Remaining,
    bool ClockStarted,
    bool DevicePaused,
    bool HeldByStaff,
    int ObjectivesClosed,
    int ObjectivesTotal,
    DateTimeOffset At)
{
    public bool ClockRunning => ClockStarted && !DevicePaused && !HeldByStaff && State != SessionState.Ended;

    /// <summary>Time left at <paramref name="now"/>, counting down from the snapshot while the clock runs.</summary>
    public TimeSpan RemainingAt(DateTimeOffset now)
    {
        if (!ClockRunning) return Remaining;
        var left = Remaining - (now - At);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>The snapshot as an exam step reports it.</summary>
    public static LiveSnapshot From(Guid organizationId, Guid studentUserId, SessionStep step, DateTimeOffset at) =>
        new(step.SessionId, organizationId, studentUserId, step.State, step.TurnIndex, step.Move, step.ConceptTitle, step.Remaining,
            step.ClockStarted, step.ClockPaused, step.HeldByStaff, step.ConceptsClosed, step.ConceptsTotal, at);
}

/// <summary>One thing that happened on a live exam.</summary>
public sealed record LiveSessionEvent(LiveEventKind Kind, Guid OrganizationId, Guid SessionId, DateTimeOffset At,
    LiveSnapshot? Snapshot = null, string? Text = null);

/// <summary>A notice staff sent to a student, kept for a short while so a reloaded exam room still shows it.</summary>
public sealed record StaffNotice(string Text, DateTimeOffset At);

/// <summary>
/// The in-process broadcaster behind the live sessions monitor (GAD-T299). The orchestrator publishes each step's state, the integrity
/// recorders say when notes arrive, the student's exam room says when it opens and closes, and staff actions go out to the exam room.
/// Subscribers (the admin page, the student's notice banner) re-render through their own <c>InvokeAsync</c>; a slow or failing
/// subscriber never holds up the exam. Nothing here is stored: after a restart the monitor reads the database until the next step.
/// One instance per server (<see cref="Shared"/>, also registered in DI); a second server instance has its own, which is why the page
/// also refreshes on a timer.
/// </summary>
public sealed class LiveSessionFeed
{
    /// <summary>The process's feed, for code that is not built by DI (the integrity recorders are created by hand).</summary>
    public static LiveSessionFeed Shared { get; } = new();

    /// <summary>How long a staff notice is shown again to an exam room that reloads.</summary>
    public static readonly TimeSpan NoticeKeptFor = TimeSpan.FromMinutes(5);

    /// <summary>Snapshots older than this (no step for a long time, the session gone) are dropped when the table grows.</summary>
    private static readonly TimeSpan SnapshotKeptFor = TimeSpan.FromHours(12);

    private readonly ConcurrentDictionary<Guid, LiveSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<Guid, int> _rooms = new();
    private readonly ConcurrentDictionary<Guid, StaffNotice> _notices = new();
    private readonly object _subscribersLock = new();
    private Action<LiveSessionEvent>[] _subscribers = [];

    /// <summary>Calls <paramref name="handler"/> for every event until the returned handle is disposed.</summary>
    public IDisposable Subscribe(Action<LiveSessionEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_subscribersLock) _subscribers = [.. _subscribers, handler];
        return new Subscription(this, handler);
    }

    private void Unsubscribe(Action<LiveSessionEvent> handler)
    {
        lock (_subscribersLock) _subscribers = _subscribers.Where(h => h != handler).ToArray();
    }

    private sealed class Subscription(LiveSessionFeed feed, Action<LiveSessionEvent> handler) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) feed.Unsubscribe(handler);
        }
    }

    public int SubscriberCount => _subscribers.Length;

    private void Publish(LiveSessionEvent e)
    {
        foreach (var handler in _subscribers)
        {
            // A subscriber only schedules its own re-render; one that throws is its own problem, never the exam's.
            try { handler(e); }
            catch (Exception) { /* ignored on purpose */ }
        }
    }

    // ------------------------------------------------------------------ publishers

    /// <summary>The orchestrator finished a step on a session (GAD-T299). Never throws.</summary>
    public void Stepped(Guid organizationId, Guid studentUserId, SessionStep step, DateTimeOffset at)
    {
        try
        {
            var snapshot = LiveSnapshot.From(organizationId, studentUserId, step, at);
            if (snapshot.State == SessionState.Ended) _snapshots.TryRemove(step.SessionId, out _);
            else _snapshots[step.SessionId] = snapshot;
            Trim(at);
            Publish(new LiveSessionEvent(LiveEventKind.Stepped, organizationId, step.SessionId, at, snapshot));
        }
        catch (Exception) { /* the monitor must never break an exam step */ }
    }

    /// <summary>New integrity notes were saved for the session. Never throws.</summary>
    public void IntegrityRecorded(Guid organizationId, Guid sessionId, DateTimeOffset at)
    {
        try { Publish(new LiveSessionEvent(LiveEventKind.Integrity, organizationId, sessionId, at)); }
        catch (Exception) { /* ignored on purpose */ }
    }

    /// <summary>The student's exam room for the session opened; dispose the result when it closes.</summary>
    public IDisposable RoomOpened(Guid organizationId, Guid sessionId, DateTimeOffset at)
    {
        _rooms.AddOrUpdate(sessionId, 1, (_, n) => n + 1);
        Publish(new LiveSessionEvent(LiveEventKind.Room, organizationId, sessionId, at));
        return new Room(this, organizationId, sessionId);
    }

    private sealed class Room(LiveSessionFeed feed, Guid org, Guid session) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var left = feed._rooms.AddOrUpdate(session, 0, (_, n) => Math.Max(0, n - 1));
            if (left == 0) feed._rooms.TryRemove(new KeyValuePair<Guid, int>(session, 0));
            feed.Publish(new LiveSessionEvent(LiveEventKind.Room, org, session, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>Sends the student's exam room a notice from staff.</summary>
    public void Notice(Guid organizationId, Guid sessionId, string text, DateTimeOffset at)
    {
        _notices[sessionId] = new StaffNotice(text, at);
        Publish(new LiveSessionEvent(LiveEventKind.Notice, organizationId, sessionId, at, Text: text));
    }

    /// <summary>Staff changed the exam; <paramref name="studentMessage"/> is what the student's exam room says about it.</summary>
    public void StaffAction(Guid organizationId, Guid sessionId, string studentMessage, DateTimeOffset at, LiveSnapshot? snapshot = null)
    {
        if (snapshot is not null)
        {
            if (snapshot.State == SessionState.Ended) _snapshots.TryRemove(sessionId, out _);
            else _snapshots[sessionId] = snapshot;
        }
        Publish(new LiveSessionEvent(LiveEventKind.StaffAction, organizationId, sessionId, at, snapshot, studentMessage));
    }

    // ------------------------------------------------------------------ readers

    /// <summary>The last step's state for the session in this organization, or null when this server has not seen one.</summary>
    public LiveSnapshot? Snapshot(Guid organizationId, Guid sessionId) =>
        _snapshots.TryGetValue(sessionId, out var s) && s.OrganizationId == organizationId ? s : null;

    /// <summary>Remembers a snapshot read from the database (after a restart) until the next step replaces it.</summary>
    public void Remember(LiveSnapshot snapshot)
    {
        if (snapshot.State != SessionState.Ended) _snapshots.TryAdd(snapshot.SessionId, snapshot);
    }

    /// <summary>
    /// How many exam rooms are open on the session on this server, in that organization (GAD-T387: every caller already
    /// knows the session is theirs, but the signature checks it too, the same discipline as <see cref="Snapshot"/>,
    /// so a future caller can't skip it by accident).
    /// </summary>
    public int RoomsOpen(Guid organizationId, Guid sessionId) =>
        _snapshots.TryGetValue(sessionId, out var s) && s.OrganizationId != organizationId ? 0
            : _rooms.TryGetValue(sessionId, out var n) ? n : 0;

    /// <summary>The latest staff notice for the session, while it is recent enough to show again (GAD-T387: organization-checked, see <see cref="RoomsOpen"/>).</summary>
    public StaffNotice? RecentNotice(Guid organizationId, Guid sessionId, DateTimeOffset now)
    {
        if (_snapshots.TryGetValue(sessionId, out var s) && s.OrganizationId != organizationId) return null;
        return _notices.TryGetValue(sessionId, out var n) && now - n.At <= NoticeKeptFor ? n : null;
    }

    private void Trim(DateTimeOffset now)
    {
        if (_snapshots.Count > 5000)
            foreach (var (id, s) in _snapshots)
                if (now - s.At > SnapshotKeptFor) _snapshots.TryRemove(id, out _);
        if (_notices.Count > 1000)
            foreach (var (id, n) in _notices)
                if (now - n.At > NoticeKeptFor) _notices.TryRemove(id, out _);
    }
}
