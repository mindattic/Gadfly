using System.Collections.Concurrent;

namespace Gadfly.Server.Voice;

/// <summary>
/// What a signed-in student may do on the two voice hubs (the avatar's speech hub and the speech-recognition relay), decided
/// by the server and never by the browser (GAD-T53). The exam room arms a student after it has checked that the session is
/// theirs and running, and offers each text it wants the examiner to say (the question, the transcript read-back). The hub
/// filter then refuses a connection that was not armed, and refuses to synthesize any text that was not offered: a client can't
/// turn the avatar hub into a free text-to-speech endpoint, or open a recognizer without an exam in progress.
/// </summary>
public sealed class VoiceGrants(TimeProvider clock)
{
    public static readonly TimeSpan ArmedFor = TimeSpan.FromHours(4);
    public const int MaxOffers = 16;

    private sealed class Armed(Guid org, Guid session, DateTimeOffset until)
    {
        public Guid Organization { get; } = org;
        public Guid Session { get; set; } = session;
        public DateTimeOffset Until { get; set; } = until;
        public Dictionary<string, DateTimeOffset> Texts { get; } = new(StringComparer.Ordinal);
    }

    private readonly ConcurrentDictionary<Guid, Armed> _armed = new();

    /// <summary>Called by the exam room once the session is known to be this student's and running.</summary>
    public void Arm(Guid user, Guid org, Guid session)
    {
        var until = clock.GetUtcNow() + ArmedFor;
        _armed.AddOrUpdate(user, _ => new Armed(org, session, until), (_, a) =>
        {
            lock (a) { a.Session = session; a.Until = until; a.Texts.Clear(); }
            return a;
        });
    }

    /// <summary>Ends the student's grant (leaving the exam room, the session ending).</summary>
    public void Disarm(Guid user, Guid session)
    {
        if (_armed.TryGetValue(user, out var a) && a.Session == session) _armed.TryRemove(user, out _);
    }

    public bool IsArmed(Guid user) => _armed.TryGetValue(user, out var a) && a.Until > clock.GetUtcNow();

    /// <summary>Lets the examiner say <paramref name="text"/> to this student for the rest of the grant (a repeat needs no new offer).</summary>
    public bool Offer(Guid user, Guid session, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !_armed.TryGetValue(user, out var a)) return false;
        var now = clock.GetUtcNow();
        lock (a)
        {
            if (a.Session != session || a.Until <= now) return false;
            a.Texts[text.Trim()] = now;
            while (a.Texts.Count > MaxOffers) a.Texts.Remove(a.Texts.MinBy(kv => kv.Value).Key);
        }
        return true;
    }

    public bool MaySpeak(Guid user, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !_armed.TryGetValue(user, out var a) || a.Until <= clock.GetUtcNow()) return false;
        lock (a) return a.Texts.ContainsKey(text.Trim());
    }
}
