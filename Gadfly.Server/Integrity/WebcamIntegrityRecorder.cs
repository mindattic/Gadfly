using System.Collections.Concurrent;
using Gadfly.Server.Data;
using Gadfly.Server.Sessions;
using Gadfly.Server.Student;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Integrity;

/// <summary>
/// What the exam room needs to know before it starts the camera: whether this exam watches by webcam
/// and whether the student's recorded consent was to wording that said so.
/// </summary>
public sealed record WebcamPlan(bool Required, bool Consented, string? Reason);

/// <summary>How a batch went: whether the client should keep sending, and how many rows were stored.</summary>
public readonly record struct WebcamRecordResult(bool KeepGoing, int Stored);

/// <summary>
/// Validates and stores webcam integrity events (GAD-T57; docs/SOCRATIC_PLATFORM.md "Anti-cheating
/// layers", Webcam row). The events are the MindAttic.Webcam library's output (type, start, end,
/// confidence, summary): the device computes them and nothing else leaves it, never a frame. Whatever a
/// browser sends is untrusted, so only an allow-list of types is stored, text and times are bounded,
/// volume is capped, and the severity is decided here, never taken from the client: webcam signals are
/// annotations for a human to weigh, at most Low, and never an automatic penalty (D2). Nothing here
/// infers emotion or deception. Register scoped; use <see cref="Create"/> inside a Blazor component's own scope.
/// </summary>
public sealed class WebcamIntegrityRecorder(GadflyDbContext db, TimeProvider clock, ILogger<WebcamIntegrityRecorder> log)
{
    public const string Source = "webcam";
    public const int MaxEventsPerSession = 300;
    public const int MaxBatch = 50;
    public const int MaxSummary = 300;
    public const string CappedType = "webcam-capped";
    public const string UnavailableType = "camera-unavailable";

    /// <summary>At most this many events a session may send per rate window (real events are episodes: a handful a minute at most).</summary>
    public const int MaxPerWindow = 40;
    public static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);

    /// <summary>A span signal arriving this long after the session ended is still accepted (it started before the end).</summary>
    public static readonly TimeSpan LateGrace = TimeSpan.FromMinutes(15);

    /// <summary>Longest span an event may claim (a face missing for the whole exam is still one honest span).</summary>
    private static readonly TimeSpan MaxSpan = TimeSpan.FromHours(4);

    /// <summary>The types the library emits for the record (setup-quality hints stay on the device) plus the neutral "no camera" note.</summary>
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "face-absent", "multiple-faces", "identity-drift", "gaze-off-screen", "thinking-glance", "reading-pattern", UnavailableType,
    };

    /// <summary>Why the camera was not used, as the browser reports it; each maps to a fixed, neutral sentence.</summary>
    public static readonly IReadOnlyDictionary<string, string> UnavailableReasons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["denied"] = "The student did not allow the camera, so no camera checks ran. The exam continued normally; this is a neutral note, not a concern.",
        ["no-device"] = "No camera was found, so no camera checks ran. The exam continued normally; this is a neutral note, not a concern.",
        ["in-use"] = "The camera could not be opened (another program may have been using it), so no camera checks ran. The exam continued normally.",
        ["unsupported"] = "The browser could not run the camera checks, so none ran. The exam continued normally.",
        ["error"] = "The camera checks could not start, so none ran. The exam continued normally.",
        ["no-consent"] = "The camera checks were not started because the notice the student accepted did not cover them. The exam continued normally.",
    };

    private static readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> Recent = new();

    public static WebcamIntegrityRecorder Create(IServiceProvider services) =>
        services.GetService<WebcamIntegrityRecorder>() ?? ActivatorUtilities.CreateInstance<WebcamIntegrityRecorder>(services);

    /// <summary>
    /// Decides whether this session should run the camera: the exam's integrity level is Strict (webcam and
    /// audio signals, see the exam builder) and the consent the student gave is to the current notice,
    /// which describes the camera plainly. Null when the session isn't the student's.
    /// </summary>
    public async Task<WebcamPlan?> PlanAsync(Guid organizationId, Guid studentUserId, Guid sessionId, CancellationToken ct = default)
    {
        var s = await db.ExamSessions.AsNoTracking()
            .Where(x => x.Id == sessionId && x.OrganizationId == organizationId && x.StudentUserId == studentUserId)
            .Select(x => new { x.State, x.ExamId, x.ConsentRecordedAt, x.ConsentTextSha256 })
            .FirstOrDefaultAsync(ct);
        if (s is null) return null;
        var exam = await db.Exams.AsNoTracking().Where(e => e.Id == s.ExamId && e.OrganizationId == organizationId)
            .Select(e => new { e.Title, e.Mode, e.IntegrityLevel }).FirstOrDefaultAsync(ct);
        if (exam is null || exam.IntegrityLevel != IntegrityLevel.Strict) return new WebcamPlan(false, false, "not-required");
        if (s.State is SessionState.Created or SessionState.Ended or SessionState.Failed || s.ConsentRecordedAt is null)
            return new WebcamPlan(false, false, "not-running");

        var name = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.Name).FirstOrDefaultAsync(ct);
        var days = await db.OrganizationSettings.AsNoTracking().Where(o => o.OrganizationId == organizationId)
            .Select(o => (int?)o.RetentionDays).FirstOrDefaultAsync(ct);
        var notice = ConsentNotice.For(name ?? "your organization", exam.Title, days ?? 90, exam.Mode, exam.IntegrityLevel);
        var consented = string.Equals(notice.Sha256, s.ConsentTextSha256, StringComparison.OrdinalIgnoreCase);
        return new WebcamPlan(true, consented, consented ? null : "no-consent");
    }

    /// <summary>
    /// Validates and stores a batch. KeepGoing is false when the client should stop sending (not the student's
    /// session, exam not watching by camera, session over); true otherwise, even when some events were dropped.
    /// </summary>
    public async Task<WebcamRecordResult> RecordAsync(Guid organizationId, Guid studentUserId, Guid sessionId, IReadOnlyList<HubIntegrityEvent>? events, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var session = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId)
            .Select(s => new { s.State, s.ExamId, s.StartedAt, s.ConsentRecordedAt, s.CreatedAt, s.EndedAt })
            .FirstOrDefaultAsync(ct);
        if (session is null) return new(false, 0);
        var accepting = session.State is SessionState.Running or SessionState.ConsentGiven or SessionState.Paused
            || (session.State == SessionState.Ended && session.EndedAt is { } ended && now - ended <= LateGrace);
        if (!accepting) return new(false, 0);
        var level = await db.Exams.AsNoTracking().Where(e => e.Id == session.ExamId && e.OrganizationId == organizationId)
            .Select(e => (IntegrityLevel?)e.IntegrityLevel).FirstOrDefaultAsync(ct);
        if (level != IntegrityLevel.Strict) return new(false, 0);
        if (events is null || events.Count == 0) return new(session.State != SessionState.Ended, 0);

        var windowStart = session.StartedAt ?? session.ConsentRecordedAt ?? session.CreatedAt;
        var windowEnd = session.EndedAt is { } e2 && e2 < now ? e2 : now;

        var accepted = new List<Clean>();
        foreach (var e in events.Take(MaxBatch))
            if (Clean.From(e, windowStart, windowEnd) is { } c) accepted.Add(c);
        if (accepted.Count == 0) return new(session.State != SessionState.Ended, 0);

        // Rate limit per session: a misbehaving page can't flood the record.
        var allowed = TakeRate(sessionId, now, accepted.Count);
        if (allowed < accepted.Count)
        {
            log.LogWarning("Webcam events over the rate limit for session {SessionId}: {Dropped} dropped.", sessionId, accepted.Count - allowed);
            accepted = accepted.Take(allowed).ToList();
        }
        if (accepted.Count == 0) return new(true, 0);

        var mine = db.IntegrityEvents.Where(x => x.SessionId == sessionId && x.OrganizationId == organizationId && x.Source == Source);
        var stored = await mine.Select(x => new { x.Type, x.StartedAt }).ToListAsync(ct);
        if (stored.Any(x => x.Type == CappedType)) return new(true, 0);
        var rows = stored.Count;
        var added = 0;
        var seen = stored.Select(x => (x.Type, x.StartedAt)).ToHashSet();
        var unavailable = stored.Count(x => x.Type == UnavailableType);

        var turns = await db.SessionTurns.AsNoTracking()
            .Where(t => t.SessionId == sessionId && t.OrganizationId == organizationId)
            .Select(t => new { t.Id, t.AskedAt, t.AnswerCompletedAt })
            .ToListAsync(ct);

        foreach (var c in accepted.OrderBy(x => x.Start))
        {
            // A retried batch (the client re-sends what a failed call may have stored) is stored once.
            if (!seen.Add((c.Type, c.Start))) continue;
            if (c.Type == UnavailableType && unavailable++ >= 3) continue;
            if (rows >= MaxEventsPerSession - 1)
            {
                db.IntegrityEvents.Add(new IntegrityEvent
                {
                    OrganizationId = organizationId, SessionId = sessionId, Source = Source, Type = CappedType,
                    StartedAt = c.Start, Confidence = 1, Severity = IntegritySeverity.Info,
                    Summary = $"The camera checks kept reporting events; after {MaxEventsPerSession - 1} stored events the rest of this session's camera events were not stored.",
                });
                added++;
                break;
            }
            var turn = turns.Where(t => t.AskedAt <= c.Start && (t.AnswerCompletedAt is null || t.AnswerCompletedAt >= c.Start))
                .OrderByDescending(t => t.AskedAt).Select(t => (Guid?)t.Id).FirstOrDefault();
            db.IntegrityEvents.Add(new IntegrityEvent
            {
                OrganizationId = organizationId, SessionId = sessionId, Source = Source, Type = c.Type,
                StartedAt = c.Start, EndedAt = c.End, TurnId = turn,
                Confidence = c.Confidence, Severity = c.Severity, Summary = c.Summary,
            });
            rows++;
            added++;
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "Could not store webcam events for session {SessionId}.", sessionId);
            added = 0;
        }
        // Late events of a finished session are kept, but the browser is told to stop and release the camera.
        return new(session.State != SessionState.Ended, added);
    }

    /// <summary>Counts how many of <paramref name="want"/> still fit in the session's rate window, and spends them.</summary>
    public static int TakeRate(Guid sessionId, DateTimeOffset now, int want)
    {
        var q = Recent.GetOrAdd(sessionId, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > RateWindow) q.Dequeue();
            var room = Math.Max(0, MaxPerWindow - q.Count);
            var take = Math.Min(room, want);
            for (var i = 0; i < take; i++) q.Enqueue(now);
            if (Recent.Count > 2000) Prune(now);
            return take;
        }
    }

    private static void Prune(DateTimeOffset now)
    {
        foreach (var (id, q) in Recent)
            lock (q)
                if (q.Count == 0 || now - q.Peek() > RateWindow) Recent.TryRemove(id, out _);
    }

    /// <summary>An event after validation: allow-listed type, bounded text and times, server-decided confidence and severity.</summary>
    public sealed record Clean(string Type, DateTimeOffset Start, DateTimeOffset? End, double Confidence, IntegritySeverity Severity, string Summary)
    {
        public static Clean? From(HubIntegrityEvent? e, DateTimeOffset windowStart, DateTimeOffset windowEnd)
        {
            if (e is null || !string.Equals(e.Source, Source, StringComparison.Ordinal)) return null;
            var type = (e.Type ?? "").Trim();
            if (!Types.Contains(type)) return null;

            var start = e.StartedAt;
            // Wholly after the session (a few seconds of slack for clock differences): not part of it.
            if (start > windowEnd.AddSeconds(5)) return null;
            if (start > windowEnd) start = windowEnd;
            if (start < windowStart)
            {
                // Entirely before the session: not ours. Straddling the start: clamp.
                if (e.EndedAt is not { } endsAt || endsAt < windowStart) return null;
                start = windowStart;
            }
            DateTimeOffset? end = e.EndedAt;
            if (end is { } en)
            {
                if (en > windowEnd) en = windowEnd;
                end = en < start ? null : en - start > MaxSpan ? start + MaxSpan : en;
            }

            var span = end is { } x ? x - start : TimeSpan.Zero;
            var confidence = double.IsFinite(e.Confidence) ? Math.Clamp(e.Confidence, 0, MaxConfidence) : 0;
            confidence = Math.Round(type is "thinking-glance" or UnavailableType ? Math.Min(confidence, 0.3) : confidence, 2);
            var summary = type == UnavailableType
                ? UnavailableReasons.GetValueOrDefault(ReasonOf(e.Summary), UnavailableReasons["error"])
                : Text(e.Summary, MaxSummary);
            if (summary.Length == 0) summary = IntegrityExplanationsFallback(type);
            return new Clean(type, start, type == UnavailableType ? null : end, confidence, SeverityOf(type, span), summary);
        }
    }

    /// <summary>The most a webcam event's confidence is stored as: what the detector saw is uncertain, so it reads as low-confidence.</summary>
    public const double MaxConfidence = 0.4;

    /// <summary>
    /// Webcam events are annotations: never above Low, whatever the client claims. A short blip, a thinking
    /// glance and a missing camera are Info; the rest are Low and listed for a person to read in context.
    /// </summary>
    public static IntegritySeverity SeverityOf(string type, TimeSpan span) => type switch
    {
        "thinking-glance" or UnavailableType => IntegritySeverity.Info,
        "reading-pattern" or "identity-drift" => IntegritySeverity.Low,
        "face-absent" or "multiple-faces" or "gaze-off-screen" => span >= TimeSpan.FromSeconds(15) ? IntegritySeverity.Low : IntegritySeverity.Info,
        _ => IntegritySeverity.Info,
    };

    /// <summary>The browser sends the reason in the summary field of a camera-unavailable event (a code, never free text).</summary>
    private static string ReasonOf(string? summary) => (summary ?? "").Trim().ToLowerInvariant();

    private static string IntegrityExplanationsFallback(string type) => $"Camera event \"{type}\".";

    /// <summary>Control characters and anything shaped like an embedded image or URL scheme removed, then clipped.</summary>
    internal static string Text(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new System.Text.StringBuilder(Math.Min(s.Length, max));
        foreach (var ch in s)
        {
            if (char.IsControl(ch)) { sb.Append(' '); continue; }
            sb.Append(ch);
            if (sb.Length >= max * 2) break;
        }
        var cleaned = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"data:\S*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)).Trim();
        return cleaned.Length <= max ? cleaned : cleaned[..max];
    }
}
