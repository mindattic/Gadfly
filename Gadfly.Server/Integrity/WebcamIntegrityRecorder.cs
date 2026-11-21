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

    /// <summary>
    /// Partial updates of an ongoing episode (D21, GAD-T171) have a bucket of their own so they can never crowd
    /// out the real events: one update a minute per open episode needs only a few slots.
    /// </summary>
    public const int MaxPartialPerWindow = 20;

    /// <summary>The types that can be ongoing: the ones with a start and an end (not the "no camera" note or a glance).</summary>
    public static readonly IReadOnlySet<string> PartialTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "face-absent", "multiple-faces", "identity-drift", "gaze-off-screen", "reading-pattern",
    };

    /// <summary>The longest episode key a client may send.</summary>
    public const int MaxEpisodeKey = 80;

    /// <summary>A span signal arriving this long after the session ended is still accepted (it started before the end).</summary>
    public static readonly TimeSpan LateGrace = TimeSpan.FromMinutes(15);

    /// <summary>Longest span an event may claim (a face missing for the whole exam is still one honest span).</summary>
    private static readonly TimeSpan MaxSpan = TimeSpan.FromHours(4);

    /// <summary>
    /// The voice interview room's eye-contact episodes (GAD-T240): while the student is answering, the camera, analysed
    /// on the device, checks that they are looking at the examiner's face. Each is a span with a start and an end, recorded from any
    /// exam whose integrity level isn't Off (the voice-mode notice says camera checks run on the device and send short notes).
    /// </summary>
    public static readonly IReadOnlySet<string> ExaminerTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "examiner-look-away", "examiner-reading", "examiner-face-absent", "examiner-second-face", "examiner-fade",
    };

    /// <summary>What the examiner says out loud when an eye-contact episode starts while the student answers (GAD-T240).</summary>
    public static readonly IReadOnlyDictionary<string, string> ExaminerRemarks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["look-away"] = "Please look at me while you answer.",
        ["reading"] = "Please look at me while you answer, rather than reading.",
        ["face-absent"] = "I can't see your face. Please come back into view while you answer.",
        ["second-face"] = "I can see someone else on camera. Please make sure you are answering on your own.",
    };

    /// <summary>The types the library emits for the record (setup-quality hints stay on the device) plus the neutral "no camera" note.</summary>
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(
        new[] { "face-absent", "multiple-faces", "identity-drift", "gaze-off-screen", "thinking-glance", "reading-pattern", UnavailableType }
            .Concat(ExaminerTypes), StringComparer.Ordinal);

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
    private static readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> Partials = new();

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
    public Task<WebcamRecordResult> RecordAsync(Guid organizationId, Guid studentUserId, Guid sessionId, IReadOnlyList<HubIntegrityEvent>? events, CancellationToken ct = default) =>
        RecordCoreAsync(organizationId, studentUserId, sessionId, events, examinerRoom: false, ct);

    /// <summary>
    /// Whether the voice interview room should check eye contact with the examiner (GAD-T240): the session is the student's and
    /// the exam's integrity level isn't Off. The checks run on the device; only <see cref="ExaminerTypes"/> episodes are sent.
    /// </summary>
    public async Task<bool> ExaminerGazeEnabledAsync(Guid organizationId, Guid studentUserId, Guid sessionId, CancellationToken ct = default)
    {
        var examId = await db.ExamSessions.AsNoTracking()
            .Where(x => x.Id == sessionId && x.OrganizationId == organizationId && x.StudentUserId == studentUserId)
            .Select(x => (Guid?)x.ExamId).FirstOrDefaultAsync(ct);
        if (examId is null) return false;
        var level = await db.Exams.AsNoTracking().Where(e => e.Id == examId && e.OrganizationId == organizationId)
            .Select(e => (IntegrityLevel?)e.IntegrityLevel).FirstOrDefaultAsync(ct);
        return level is { } l && l != IntegrityLevel.Off;
    }

    /// <summary>
    /// Stores the voice interview room's eye-contact episodes (GAD-T240): only <see cref="ExaminerTypes"/>, with the same
    /// bounds, rate limits, cap, confidence ceiling and server-decided severity as every webcam event; any exam whose
    /// integrity level isn't Off. KeepGoing is false when the client should stop sending.
    /// </summary>
    public Task<WebcamRecordResult> RecordExaminerGazeAsync(Guid organizationId, Guid studentUserId, Guid sessionId, IReadOnlyList<HubIntegrityEvent>? events, CancellationToken ct = default) =>
        RecordCoreAsync(organizationId, studentUserId, sessionId, events, examinerRoom: true, ct);

    private async Task<WebcamRecordResult> RecordCoreAsync(Guid organizationId, Guid studentUserId, Guid sessionId, IReadOnlyList<HubIntegrityEvent>? events, bool examinerRoom, CancellationToken ct)
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
        if (examinerRoom ? level is null or IntegrityLevel.Off : level != IntegrityLevel.Strict) return new(false, 0);
        // The camera integrity checks (Strict) don't store the interview room's types, and the interview room stores nothing else.
        events = events?.Where(e => e is not null && ExaminerTypes.Contains((e.Type ?? "").Trim()) == examinerRoom).ToList();
        if (events is null || events.Count == 0) return new(session.State != SessionState.Ended, 0);

        // From consent (or creation), not StartedAt: that moves to when a held exam clock starts (GAD-T239), after the room is already open.
        var windowStart = session.ConsentRecordedAt ?? session.CreatedAt;
        var windowEnd = session.EndedAt is { } e2 && e2 < now ? e2 : now;

        var accepted = new List<Clean>();
        foreach (var e in events.Take(MaxBatch))
            if (Clean.From(e, windowStart, windowEnd) is { } c) accepted.Add(c);
        if (accepted.Count == 0) return new(session.State != SessionState.Ended, 0);

        // Rate limits per session: a misbehaving page can't flood the record. Partial updates of an ongoing episode
        // spend their own bucket, so they never use up the room real events need.
        var partials = accepted.Where(x => x.Ongoing).ToList();
        var finals = accepted.Where(x => !x.Ongoing).ToList();
        var allowedPartials = TakeRate(Partials, sessionId, now, partials.Count, MaxPartialPerWindow);
        var allowedFinals = TakeRate(Recent, sessionId, now, finals.Count, MaxPerWindow);
        if (allowedPartials < partials.Count || allowedFinals < finals.Count)
            log.LogWarning("Webcam events over the rate limit for session {SessionId}: {Dropped} dropped.", sessionId,
                partials.Count - allowedPartials + finals.Count - allowedFinals);
        accepted = [.. finals.Take(allowedFinals), .. partials.Take(allowedPartials)];
        if (accepted.Count == 0) return new(true, 0);

        var stored = await db.IntegrityEvents
            .Where(x => x.SessionId == sessionId && x.OrganizationId == organizationId && x.Source == Source).ToListAsync(ct);
        if (stored.Any(x => x.Type == CappedType)) return new(true, 0);
        var rows = stored.Count;
        var added = 0;
        // One row per episode: found by its episode key, or (rows from before partial updates) by type and start.
        var byKey = new Dictionary<string, IntegrityEvent>(StringComparer.Ordinal);
        var byStart = new Dictionary<(string, DateTimeOffset), IntegrityEvent>();
        foreach (var x in stored)
        {
            if (EpisodeData.Parse(x.DataJson) is { } d) byKey.TryAdd(d.Episode, x);
            byStart.TryAdd((x.Type, x.StartedAt), x);
        }
        var unavailable = stored.Count(x => x.Type == UnavailableType);

        var turns = await db.SessionTurns.AsNoTracking()
            .Where(t => t.SessionId == sessionId && t.OrganizationId == organizationId)
            .Select(t => new { t.Id, t.AskedAt, t.AnswerCompletedAt })
            .ToListAsync(ct);

        // Within a batch an episode's partial updates come before its closing event.
        foreach (var c in accepted.OrderBy(x => x.Start).ThenBy(x => x.End ?? DateTimeOffset.MaxValue))
        {
            var existing = c.EpisodeKey is { } k && byKey.TryGetValue(k, out var byK) ? byK
                : byStart.GetValueOrDefault((c.Type, c.Start));
            if (existing is not null)
            {
                // A retried batch, the next partial update or the closing event: the same row grows, never a second row.
                if (Apply(existing, c)) added++;
                continue;
            }
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
            var row = new IntegrityEvent
            {
                OrganizationId = organizationId, SessionId = sessionId, Source = Source, Type = c.Type,
                StartedAt = c.Start, EndedAt = c.End, TurnId = turn,
                Confidence = c.Confidence, Severity = c.Severity, Summary = c.Summary,
                DataJson = c.EpisodeKey is { } key ? EpisodeData.Json(key, c.Ongoing) : null,
            };
            db.IntegrityEvents.Add(row);
            if (c.EpisodeKey is { } rk) byKey[rk] = row;
            byStart[(row.Type, row.StartedAt)] = row;
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

    /// <summary>
    /// Folds an update into the episode's one row. A partial update only extends an episode that is still open
    /// (a late or retried partial never reopens or shrinks a closed one); the closing event sets the final end
    /// and summary. Returns true when the row changed.
    /// </summary>
    public static bool Apply(IntegrityEvent row, Clean c)
    {
        var data = EpisodeData.Parse(row.DataJson);
        var open = data?.Ongoing ?? false;
        if (c.Ongoing)
        {
            if (!open || c.End is not { } end || (row.EndedAt is { } have && end <= have)) return false;
            row.EndedAt = end;
            row.Summary = c.Summary;
            row.Confidence = Math.Max(row.Confidence, c.Confidence);
            row.Severity = c.Severity;
            return true;
        }
        // The closing event. An identical retry changes nothing.
        var key = data?.Episode ?? c.EpisodeKey;
        var endAt = c.End ?? row.EndedAt;
        if (!open && row.EndedAt == endAt && row.Summary == c.Summary) return false;
        row.EndedAt = endAt;
        row.Summary = c.Summary;
        row.Confidence = c.Confidence;
        row.Severity = c.Severity;
        if (key is not null) row.DataJson = EpisodeData.Json(key, false);
        return true;
    }

    /// <summary>Counts how many of <paramref name="want"/> still fit in the session's rate window, and spends them.</summary>
    public static int TakeRate(Guid sessionId, DateTimeOffset now, int want) => TakeRate(Recent, sessionId, now, want, MaxPerWindow);

    private static int TakeRate(ConcurrentDictionary<Guid, Queue<DateTimeOffset>> buckets, Guid sessionId, DateTimeOffset now, int want, int max)
    {
        var q = buckets.GetOrAdd(sessionId, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > RateWindow) q.Dequeue();
            var room = Math.Max(0, max - q.Count);
            var take = Math.Min(room, want);
            for (var i = 0; i < take; i++) q.Enqueue(now);
            if (buckets.Count > 2000) Prune(buckets, now);
            return take;
        }
    }

    private static void Prune(ConcurrentDictionary<Guid, Queue<DateTimeOffset>> buckets, DateTimeOffset now)
    {
        foreach (var (id, q) in buckets)
            lock (q)
                if (q.Count == 0 || now - q.Peek() > RateWindow) buckets.TryRemove(id, out _);
    }

    /// <summary>What is kept in an episode event's DataJson: its episode key and whether it is still open.</summary>
    public sealed record EpisodeData(string Episode, bool Ongoing)
    {
        public static string Json(string episode, bool ongoing) => System.Text.Json.JsonSerializer.Serialize(new EpisodeData(episode, ongoing));

        public static EpisodeData? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return System.Text.Json.JsonSerializer.Deserialize<EpisodeData>(json) is { Episode.Length: > 0 } d ? d : null; }
            catch (System.Text.Json.JsonException) { return null; }
        }
    }

    /// <summary>The episode key: the browser's id if it is a short plain token, else type and start (which the browser builds too).</summary>
    public static string KeyOf(string type, DateTimeOffset start, string? sent)
    {
        var id = (sent ?? "").Trim();
        if (id.Length is > 0 and <= MaxEpisodeKey && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '@' or ':' or '.' or '_' or '-')) return id;
        return $"{type}@{start.ToUnixTimeMilliseconds()}";
    }

    /// <summary>Plain words for an episode still going, written by the server from what it knows (type and how long so far).</summary>
    public static string PartialSummary(string type, TimeSpan span)
    {
        var so = span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} min {span.Seconds} s" : $"{Math.Max(0, (int)span.TotalSeconds)} s";
        return type switch
        {
            "face-absent" => $"No face has been in view for {so} so far; still going.",
            "multiple-faces" => $"More than one face has been in view for {so} so far; still going. It may be a person, a photo or a reflection.",
            "identity-drift" => $"The face in view has looked different from the one at the start for {so} so far; still going. Face geometry is a weak signal.",
            "gaze-off-screen" => $"The student has looked away from the screen for {so} so far; still going. Long looks away can be thinking, reading something, or a distraction.",
            "reading-pattern" => $"Reading-like eye movement has been seen for {so} so far; still going.",
            _ => $"Camera event \"{type}\" for {so} so far; still going.",
        };
    }

    /// <summary>
    /// Plain words for an eye-contact episode in the voice interview room (GAD-T240), written by the server from the type, how
    /// long it lasted and whether the examiner spoke up (the browser sends only the code "remarked"). Never a verdict.
    /// </summary>
    public static string ExaminerSummary(string type, TimeSpan span, bool remarked, int? fadePeakPercent = null)
    {
        var so = span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} min {span.Seconds} s" : $"{Math.Max(0, (int)Math.Round(span.TotalSeconds))} s";
        var said = remarked ? " The examiner asked out loud for eye contact." : "";
        return type switch
        {
            "examiner-look-away" => $"Looked away from the examiner for {so} while answering.{said} Looking away can be thinking; read it with the answer given.",
            "examiner-reading" => $"Eye movement like reading, away from the examiner, for {so} while answering.{said} It can have other explanations (re-reading the transcript on screen); check it against the answer.",
            "examiner-face-absent" => $"No face in view for {so} while answering.{said} Often harmless (leaning out of frame, the camera moved); check whether the answer continued.",
            "examiner-second-face" => $"More than one face in view for {so} while answering.{said} It may be a person, a photo or a reflection.",
            "examiner-fade" => $"The exam screen dimmed for {so} (at most {fadePeakPercent ?? 0}% faded) because the student looked away from the examiner while answering, with a warning to look back; it cleared when they did.",
            _ => $"Camera event \"{type}\" for {so}.",
        };
    }

    /// <summary>The fade episode's peak (GAD-T240): the browser sends only the code "peak:NN", NN a whole percent 0..100.</summary>
    public static int? FadePeakOf(string? summary)
    {
        var s = ReasonOf(summary);
        return s.StartsWith("peak:", StringComparison.Ordinal) && int.TryParse(s.AsSpan(5), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 0, 100) : null;
    }

    /// <summary>An event after validation: allow-listed type, bounded text and times, server-decided confidence and severity.</summary>
    public sealed record Clean(string Type, DateTimeOffset Start, DateTimeOffset? End, double Confidence, IntegritySeverity Severity, string Summary,
        string? EpisodeKey = null, bool Ongoing = false)
    {
        public static Clean? From(HubIntegrityEvent? e, DateTimeOffset windowStart, DateTimeOffset windowEnd)
        {
            if (e is null || !string.Equals(e.Source, Source, StringComparison.Ordinal)) return null;
            var type = (e.Type ?? "").Trim();
            if (!Types.Contains(type)) return null;

            var start = e.StartedAt;
            var sentStart = start;
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

            // A partial update of an episode still going: only for types that have a span, and it needs an end ("so far").
            var ongoing = e.Ongoing;
            if (ongoing && (!PartialTypes.Contains(type) || end is null)) return null;
            var span = end is { } x ? x - start : TimeSpan.Zero;
            var confidence = double.IsFinite(e.Confidence) ? Math.Clamp(e.Confidence, 0, MaxConfidence) : 0;
            confidence = Math.Round(type is "thinking-glance" or UnavailableType ? Math.Min(confidence, 0.3) : confidence, 2);
            var summary = type == UnavailableType
                ? UnavailableReasons.GetValueOrDefault(ReasonOf(e.Summary), UnavailableReasons["error"])
                : Text(e.Summary, MaxSummary);
            if (ongoing) summary = PartialSummary(type, span);
            if (ExaminerTypes.Contains(type)) summary = ExaminerSummary(type, span, ReasonOf(e.Summary) == "remarked", FadePeakOf(e.Summary));
            if (summary.Length == 0) summary = IntegrityExplanationsFallback(type);
            var key = PartialTypes.Contains(type) ? KeyOf(type, sentStart, e.EpisodeId) : null;
            return new Clean(type, start, type == UnavailableType ? null : end, confidence, SeverityOf(type, span), summary, key, ongoing);
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
        "examiner-reading" or "examiner-second-face" => IntegritySeverity.Low,
        "examiner-fade" => IntegritySeverity.Info,
        "examiner-look-away" or "examiner-face-absent" => span >= TimeSpan.FromSeconds(10) ? IntegritySeverity.Low : IntegritySeverity.Info,
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
