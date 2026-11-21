using System.Collections.Concurrent;

namespace Gadfly.Server.Voice;

/// <summary>
/// What a signed-in student may do on the two voice hubs (the avatar's speech hub and the speech-recognition relay), decided
/// by the server and never by the browser (GAD-T53). The exam room arms a student after it has checked that the session is
/// theirs and running, and offers each text it wants the examiner to say (the question, the transcript read-back). The hub
/// filter then refuses a connection that was not armed, and refuses to synthesize any text that was not offered: a client can't
/// turn the avatar hub into a free text-to-speech endpoint, or open a recognizer without an exam in progress.
/// Grants are kept per student and session, counted per exam room that armed them, so closing one tab leaves another tab's
/// grant in place and arming another session doesn't take the first one's away.
/// </summary>
public sealed class VoiceGrants(TimeProvider clock)
{
    public static readonly TimeSpan ArmedFor = TimeSpan.FromHours(4);
    public const int MaxOffers = 16;

    private sealed class Armed(Guid org, DateTimeOffset until)
    {
        public Guid Organization { get; } = org;
        public DateTimeOffset Until { get; set; } = until;
        /// <summary>Exam rooms currently holding this grant.</summary>
        public int Rooms { get; set; } = 1;
        public Dictionary<string, DateTimeOffset> Texts { get; } = new(StringComparer.Ordinal);
    }

    private readonly ConcurrentDictionary<(Guid User, Guid Session), Armed> _armed = new();

    /// <summary>Called by the exam room once the session is known to be this student's and running.</summary>
    public void Arm(Guid user, Guid org, Guid session)
    {
        var now = clock.GetUtcNow();
        var until = now + ArmedFor;
        // Grants a room never released (a circuit that never disposed) end with their time.
        foreach (var kv in _armed)
            if (kv.Value.Until <= now) _armed.TryRemove(kv);
        _armed.AddOrUpdate((user, session), _ => new Armed(org, until), (_, a) =>
        {
            lock (a) { a.Until = until; a.Rooms++; }
            return a;
        });
    }

    /// <summary>Ends one exam room's hold on the student's grant for this session (leaving the room, the session ending).</summary>
    public void Disarm(Guid user, Guid session)
    {
        if (!_armed.TryGetValue((user, session), out var a)) return;
        lock (a)
        {
            if (--a.Rooms > 0) return;
            _armed.TryRemove(new KeyValuePair<(Guid, Guid), Armed>((user, session), a));
        }
    }

    public bool IsArmed(Guid user)
    {
        var now = clock.GetUtcNow();
        return _armed.Any(kv => kv.Key.User == user && kv.Value.Until > now);
    }

    /// <summary>Lets the examiner say <paramref name="text"/> to this student for the rest of the grant (a repeat needs no new offer).</summary>
    public bool Offer(Guid user, Guid session, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !_armed.TryGetValue((user, session), out var a)) return false;
        var now = clock.GetUtcNow();
        lock (a)
        {
            if (a.Until <= now) return false;
            a.Texts[text.Trim()] = now;
            while (a.Texts.Count > MaxOffers) a.Texts.Remove(a.Texts.MinBy(kv => kv.Value).Key);
        }
        return true;
    }

    public bool MaySpeak(Guid user, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var now = clock.GetUtcNow();
        var key = text.Trim();
        foreach (var (k, a) in _armed)
        {
            if (k.User != user) continue;
            lock (a)
                if (a.Until > now && a.Texts.ContainsKey(key)) return true;
        }
        return false;
    }
}
