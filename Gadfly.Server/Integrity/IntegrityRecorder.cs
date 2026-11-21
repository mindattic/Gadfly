using System.Text.Json;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Integrity;

/// <summary>One browser signal as wwwroot/js/integrity.js reports it. Times are the client's clock, in Unix milliseconds.</summary>
public sealed record BrowserSignal
{
    /// <summary>One of <see cref="IntegrityRecorder.BrowserTypes"/>.</summary>
    public string Type { get; init; } = "";
    public long At { get; init; }
    /// <summary>End of a span (tab hidden, focus lost, devtools open); null for an instant (paste, copy).</summary>
    public long? EndAt { get; init; }
    /// <summary>Characters pasted or copied.</summary>
    public int? Chars { get; init; }
    /// <summary>Screens seen (extra-screen).</summary>
    public int? Screens { get; init; }
    /// <summary>How it was detected, e.g. "screen-details" or "is-extended" (extra-screen).</summary>
    public string? Method { get; init; }
}

/// <summary>A batch of signals and the client's clock when it was sent (to correct clock skew).</summary>
public sealed record BrowserSignalBatch
{
    public long SentAt { get; init; }
    public List<BrowserSignal> Events { get; init; } = [];
}

/// <summary>What the exam room needs to know to watch (or not): the exam's integrity level and mode.</summary>
public sealed record IntegritySettings(IntegrityLevel Level, ExamMode Mode);

/// <summary>
/// Stores browser integrity signals (GAD-T56; docs/SOCRATIC_PLATFORM.md "Anti-cheating layers", Browser
/// row) as <see cref="IntegrityEvent"/> rows for the session. Every row is evidence for the instructor
/// to weigh, never a verdict: severity is set conservatively and browser signals alone are never High
/// (D2: no automatic failing). Only the session's own student can record, only while the session runs
/// (or shortly after it ended, for a span that started before the end), volume is capped per session,
/// and consecutive identical signals are merged into one row. Register scoped; it uses the scope's
/// <see cref="GadflyDbContext"/>, so a Blazor component creates one per call in its own scope.
/// </summary>
public sealed class IntegrityRecorder(GadflyDbContext db, TimeProvider clock, ILogger<IntegrityRecorder> log)
{
    public const string Source = "browser";
    public const int MaxEventsPerSession = 500;
    public const int MaxBatch = 100;
    public const string CappedType = "signals-capped";

    /// <summary>Signals the browser may send.</summary>
    public static readonly IReadOnlySet<string> BrowserTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "tab-hidden", "focus-lost", "fullscreen-exit", "extra-screen", "paste", "copy-question", "devtools-open",
    };

    /// <summary>A span signal arriving this long after the session ended is still accepted (it started before the end).</summary>
    public static readonly TimeSpan LateGrace = TimeSpan.FromMinutes(15);

    /// <summary>Gaps up to this long join two spans of the same kind into one row.</summary>
    private static readonly TimeSpan SpanJoin = TimeSpan.FromSeconds(2);

    /// <summary>Instants (paste, copy) of the same kind this close together become one row with a count.</summary>
    private static readonly TimeSpan InstantJoin = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The recorder from the scope's container, or built from it when it isn't registered.</summary>
    public static IntegrityRecorder Create(IServiceProvider services) =>
        services.GetService<IntegrityRecorder>() ?? ActivatorUtilities.CreateInstance<IntegrityRecorder>(services);

    /// <summary>The exam's integrity level and mode for the student's own session, or null when it isn't theirs.</summary>
    public async Task<IntegritySettings?> SettingsAsync(Guid organizationId, Guid studentUserId, Guid sessionId, CancellationToken ct = default)
    {
        var examId = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId)
            .Select(s => (Guid?)s.ExamId).FirstOrDefaultAsync(ct);
        if (examId is null) return null;
        return await db.Exams.AsNoTracking().Where(e => e.Id == examId && e.OrganizationId == organizationId)
            .Select(e => new IntegritySettings(e.IntegrityLevel, e.Mode)).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Validates and stores a batch. Returns false when the client should stop sending (not the
    /// student's session, integrity off, session over); true otherwise, even when some signals were
    /// dropped as invalid or over the cap.
    /// </summary>
    /// <param name="clockSkew">
    /// Server clock minus client clock, when the caller has a better estimate than this batch alone
    /// (e.g. the smallest offset seen on the connection, which excludes queueing delays).
    /// </param>
    public async Task<bool> RecordAsync(Guid organizationId, Guid studentUserId, Guid sessionId, BrowserSignalBatch batch,
        CancellationToken ct = default, TimeSpan? clockSkew = null)
    {
        var now = clock.GetUtcNow();
        var session = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId)
            .Select(s => new { s.State, s.ExamId, s.StartedAt, s.ConsentRecordedAt, s.CreatedAt, s.EndedAt })
            .FirstOrDefaultAsync(ct);
        if (session is null) return false;

        var accepting = session.State == SessionState.Running
            || (session.State == SessionState.Ended && session.EndedAt is { } ended && now - ended <= LateGrace);
        if (!accepting) return session.State is SessionState.Created or SessionState.ConsentGiven or SessionState.Paused;

        var level = await db.Exams.AsNoTracking().Where(e => e.Id == session.ExamId && e.OrganizationId == organizationId)
            .Select(e => (IntegrityLevel?)e.IntegrityLevel).FirstOrDefaultAsync(ct);
        if (level is null or IntegrityLevel.Off) return false;

        // Client clock to server clock: the batch was sent just now.
        var skew = clockSkew ?? ObservedSkew(batch, now) ?? TimeSpan.Zero;
        var windowStart = session.StartedAt ?? session.ConsentRecordedAt ?? session.CreatedAt;
        var windowEnd = session.EndedAt is { } e && e < now ? e : now;
        DateTimeOffset Clamp(DateTimeOffset t) => t < windowStart ? windowStart : t > windowEnd ? windowEnd : t;

        var incoming = new List<Incoming>();
        foreach (var s in (batch.Events ?? []).Take(MaxBatch))
        {
            if (s is null || !BrowserTypes.Contains(s.Type) || s.At <= 0) continue;
            DateTimeOffset start, end;
            try
            {
                start = DateTimeOffset.FromUnixTimeMilliseconds(s.At) + skew;
                end = s.EndAt is { } endMs && endMs >= s.At ? DateTimeOffset.FromUnixTimeMilliseconds(endMs) + skew : start;
            }
            catch (ArgumentOutOfRangeException) { continue; }
            // Outside the session entirely (before it started, or a span that began after it ended).
            if (end < windowStart || start > windowEnd) continue;
            start = Clamp(start);
            end = Clamp(end);
            incoming.Add(new Incoming(s.Type, start, IsSpan(s.Type) ? end : null,
                Math.Clamp(s.Chars ?? 0, 0, 1_000_000), Math.Clamp(s.Screens ?? 0, 0, 64),
                s.Method is { Length: > 0 and <= 40 } m ? m : null));
        }
        if (incoming.Count == 0) return true;
        incoming.Sort((a, b) => a.Start.CompareTo(b.Start));

        var mine = db.IntegrityEvents.Where(x => x.SessionId == sessionId && x.OrganizationId == organizationId && x.Source == Source);
        var priorByType = await mine.GroupBy(x => x.Type).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        if (priorByType.ContainsKey(CappedType)) return false;
        var rows = priorByType.Values.Sum();
        // Only the latest stored row can absorb the next signal (merging is for consecutive identical ones).
        var last = await mine.OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(ct);

        var turns = await db.SessionTurns.AsNoTracking()
            .Where(t => t.SessionId == sessionId && t.OrganizationId == organizationId)
            .Select(t => new TurnSpan(t.Id, t.Index, t.AskedAt, t.AnswerCompletedAt))
            .ToListAsync(ct);
        var touched = new HashSet<IntegrityEvent>();
        var added = 0;

        foreach (var s in incoming)
        {
            var turn = TurnAt(turns, s.Start);
            if (last is not null && last.Type == s.Type && last.TurnId == turn?.Id && Joins(last, s))
            {
                var agg = Read(last.DataJson);
                agg.Count++;
                agg.Chars += s.Chars;
                agg.Screens = Math.Max(agg.Screens, s.Screens);
                agg.Method ??= s.Method;
                if (s.End is { } end && (last.EndedAt is null || end > last.EndedAt)) last.EndedAt = end;
                last.DataJson = Write(agg);
                priorByType[s.Type] = priorByType.GetValueOrDefault(s.Type) + 1;
                touched.Add(last);
                continue;
            }

            if (rows >= MaxEventsPerSession - 1)
            {
                db.IntegrityEvents.Add(new IntegrityEvent
                {
                    OrganizationId = organizationId, SessionId = sessionId, Source = Source, Type = CappedType,
                    StartedAt = s.Start, Confidence = 1, Severity = IntegritySeverity.Info,
                    Summary = $"The browser kept reporting signals; after {MaxEventsPerSession - 1} recorded events the rest of this session's browser signals were not stored.",
                });
                log.LogInformation("Browser integrity signals capped for session {SessionId}.", sessionId);
                added++;
                break;
            }

            var row = new IntegrityEvent
            {
                OrganizationId = organizationId, SessionId = sessionId, Source = Source, Type = s.Type,
                StartedAt = s.Start, EndedAt = s.End, TurnId = turn?.Id,
                Confidence = Confidence(s.Type, s.Method),
                DataJson = Write(new Aggregate { Count = 1, Chars = s.Chars, Screens = s.Screens, Method = s.Method }),
            };
            priorByType[s.Type] = priorByType.GetValueOrDefault(s.Type) + 1;
            db.IntegrityEvents.Add(row);
            touched.Add(row);
            last = row;
            rows++;
            added++;
        }

        foreach (var row in touched)
        {
            var agg = Read(row.DataJson);
            var turn = turns.FirstOrDefault(t => t.Id == row.TurnId);
            row.Severity = SeverityOf(row.Type, row.EndedAt is { } end ? end - row.StartedAt : TimeSpan.Zero, agg, priorByType.GetValueOrDefault(row.Type), level.Value);
            row.Summary = Describe(row.Type, row.EndedAt is { } end2 ? end2 - row.StartedAt : TimeSpan.Zero, agg, turn);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "Could not store browser integrity signals for session {SessionId}.", sessionId);
        }
        return session.State == SessionState.Running;
    }

    /// <summary>Server clock minus the client's clock as this batch shows it (includes transit time), or null without a send time.</summary>
    public static TimeSpan? ObservedSkew(BrowserSignalBatch batch, DateTimeOffset serverNow)
    {
        if (batch.SentAt <= 0) return null;
        try { return serverNow - DateTimeOffset.FromUnixTimeMilliseconds(batch.SentAt); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private sealed record Incoming(string Type, DateTimeOffset Start, DateTimeOffset? End, int Chars, int Screens, string? Method);

    private sealed record TurnSpan(Guid Id, int Index, DateTimeOffset AskedAt, DateTimeOffset? AnsweredAt);

    /// <summary>What a row's DataJson keeps: how many signals it merges and their details.</summary>
    private sealed class Aggregate
    {
        public int Count { get; set; } = 1;
        public int Chars { get; set; }
        public int Screens { get; set; }
        public string? Method { get; set; }
    }

    private static Aggregate Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Aggregate();
        try { return JsonSerializer.Deserialize<Aggregate>(json, Json) ?? new Aggregate(); }
        catch (JsonException) { return new Aggregate(); }
    }

    private static string Write(Aggregate a) => JsonSerializer.Serialize(a, Json);

    private static bool IsSpan(string type) => type is "tab-hidden" or "focus-lost" or "devtools-open";

    private static bool Joins(IntegrityEvent last, Incoming s) => s.Start >= last.StartedAt && (IsSpan(s.Type)
        ? s.Start - (last.EndedAt ?? last.StartedAt) <= SpanJoin
        // Instants merge with a nearby one; a screen count only re-reports what is already recorded.
        : s.Type == "extra-screen" || s.Start - (last.EndedAt ?? last.StartedAt) <= InstantJoin);

    /// <summary>The turn whose question was waiting at this moment, if any.</summary>
    private static TurnSpan? TurnAt(IReadOnlyList<TurnSpan> turns, DateTimeOffset at) =>
        turns.Where(t => t.AskedAt <= at && (t.AnsweredAt is null || t.AnsweredAt >= at)).MaxBy(t => t.AskedAt);

    private static double Confidence(string type, string? method) => type switch
    {
        "tab-hidden" or "fullscreen-exit" or "paste" => 0.95,
        "copy-question" => 0.9,
        "focus-lost" => 0.8,
        "extra-screen" => method == "screen-details" ? 0.9 : 0.7,
        "devtools-open" => 0.3,
        _ => 0.5,
    };

    /// <summary>
    /// Conservative severity: a short blip is Info, something long or repeated is at most Medium.
    /// Browser signals alone are never High. <paramref name="occurrences"/> counts this kind across the session so far.
    /// </summary>
    internal static IntegritySeverity SeverityOf(string type, TimeSpan duration, int count, int chars, int occurrences, IntegrityLevel level) => type switch
    {
        "tab-hidden" when duration >= TimeSpan.FromSeconds(60) || count >= 3 || occurrences >= 4 => IntegritySeverity.Medium,
        "tab-hidden" when duration >= TimeSpan.FromSeconds(10) || occurrences >= 2 => IntegritySeverity.Low,
        "tab-hidden" => IntegritySeverity.Info,
        "focus-lost" when duration >= TimeSpan.FromMinutes(2) || occurrences >= 6 => IntegritySeverity.Medium,
        "focus-lost" when duration >= TimeSpan.FromSeconds(15) || count >= 3 || occurrences >= 3 => IntegritySeverity.Low,
        "focus-lost" => IntegritySeverity.Info,
        "paste" when chars >= 80 => IntegritySeverity.Medium,
        "paste" when chars >= 20 || count >= 3 => IntegritySeverity.Low,
        "paste" => IntegritySeverity.Info,
        "copy-question" => IntegritySeverity.Low,
        "fullscreen-exit" => level == IntegrityLevel.Strict ? IntegritySeverity.Low : IntegritySeverity.Info,
        "extra-screen" => level == IntegrityLevel.Strict ? IntegritySeverity.Low : IntegritySeverity.Info,
        _ => IntegritySeverity.Info,
    };

    private static IntegritySeverity SeverityOf(string type, TimeSpan duration, Aggregate a, int occurrences, IntegrityLevel level) =>
        SeverityOf(type, duration, a.Count, a.Chars, occurrences, level);

    /// <summary>A plain-language sentence for the instructor.</summary>
    internal static string Describe(string type, TimeSpan duration, int count, int chars, int screens, string? method, int? questionNumber)
    {
        var when = questionNumber is { } q ? $" while answering question {q}" : " between questions";
        var times = count > 1 ? $" ({count} times in a row)" : "";
        return type switch
        {
            "tab-hidden" => $"The exam tab was hidden for {Duration(duration)}{when}{(count > 1 ? $", {count} times close together" : "")}.",
            "focus-lost" => $"The exam window lost focus for {Duration(duration)}{when}{(count > 1 ? $", {count} times close together" : "")}, but the tab stayed visible (another window was clicked).",
            "fullscreen-exit" => $"The student left full-screen mode{when}{times}.",
            "extra-screen" => method == "screen-details"
                ? $"The browser reported {(screens > 1 ? $"{screens} screens" : "more than one screen")} connected{when}."
                : $"The browser reported that the display extends across more than one screen{when}.",
            "paste" => $"Text was pasted into the answer box{when}: {Characters(chars)}{times}.",
            "copy-question" => $"The question text was copied{when}{times}.",
            "devtools-open" => $"The browser window's size suggests developer tools may have been open{when}. A weak guess: window zoom and side panels look the same.",
            _ => $"Browser signal \"{type}\"{when}.",
        };
    }

    private static string Describe(string type, TimeSpan duration, Aggregate a, TurnSpan? turn) =>
        Describe(type, duration, a.Count, a.Chars, a.Screens, a.Method, turn is null ? null : turn.Index + 1);

    private static string Characters(int n) => n == 1 ? "1 character" : $"{n:N0} characters";

    internal static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.FromSeconds(1)) return "under a second";
        if (d < TimeSpan.FromMinutes(1)) return $"{(int)d.TotalSeconds} s";
        if (d < TimeSpan.FromHours(1)) return d.Seconds == 0 ? $"{(int)d.TotalMinutes} min" : $"{(int)d.TotalMinutes} min {d.Seconds} s";
        return $"{(int)d.TotalHours} h {d.Minutes} min";
    }
}
