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
        /// <summary>
        /// The exam's own vocabulary and spoken language (GAD-T358, GAD-T357), computed server-side by
        /// <see cref="VoiceVocabulary"/> when the exam room arms the student: what the recognizer may be asked to use as
        /// keyterm hints. Never set from anything the browser sends.
        /// </summary>
        public HashSet<string> Vocabulary { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Language { get; set; }
        /// <summary>The examiner's voice for this session (GAD-T475, <see cref="ExaminerVoice"/>); null is the platform voice.</summary>
        public string? VoiceId { get; set; }
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

    /// <summary>The organization and session of the student's most recently armed grant, for tagging voice call timings (GAD-T413).</summary>
    public (Guid Organization, Guid Session)? Current(Guid user)
    {
        var now = clock.GetUtcNow();
        (Guid, Guid)? best = null;
        var bestUntil = DateTimeOffset.MinValue;
        foreach (var (k, a) in _armed)
        {
            if (k.User != user) continue;
            DateTimeOffset until;
            lock (a) until = a.Until;
            if (until > now && until > bestUntil) (best, bestUntil) = ((a.Organization, k.Session), until);
        }
        return best;
    }

    /// <summary>
    /// Records the exam's own vocabulary and spoken language for this student's session grant (GAD-T358, GAD-T357),
    /// computed server-side (<see cref="VoiceVocabulary"/>) by the exam room once it has armed the student. Does nothing
    /// if the session isn't (or is no longer) armed.
    /// </summary>
    public void SetVocabulary(Guid user, Guid session, string? language, IReadOnlyList<string> vocabulary)
    {
        if (!_armed.TryGetValue((user, session), out var a)) return;
        lock (a)
        {
            a.Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
            a.Vocabulary.Clear();
            foreach (var term in vocabulary) if (!string.IsNullOrWhiteSpace(term)) a.Vocabulary.Add(term.Trim());
        }
    }

    /// <summary>
    /// The spoken language the exam expects for this student's most recently armed grant, or null when any (the
    /// recognizer then detects it). Used to refuse a <c>StartSession</c> call that asks for a different one
    /// (<see cref="VoiceHubFilter"/>): the browser can request only what the exam itself specifies.
    /// </summary>
    public string? AllowedLanguage(Guid user)
    {
        var now = clock.GetUtcNow();
        string? language = null;
        var bestUntil = DateTimeOffset.MinValue;
        foreach (var (k, a) in _armed)
        {
            if (k.User != user) continue;
            DateTimeOffset until;
            string? lang;
            lock (a) { until = a.Until; lang = a.Language; }
            if (until > now && until > bestUntil) (language, bestUntil) = (lang, until);
        }
        return language;
    }

    /// <summary>
    /// True when every one of <paramref name="keyterms"/> is part of the vocabulary this student's armed grant computed
    /// from their own exam (GAD-T358): a <c>StartSession</c> call may ask the recognizer to use any subset of it, but
    /// never a term the server did not itself put there. A student with no armed grant, or an exam with no computed
    /// vocabulary at all, allows nothing (an empty request still passes: no hints is always safe).
    /// </summary>
    public bool IsVocabularyAllowed(Guid user, IEnumerable<string>? keyterms)
    {
        if (keyterms is null) return true;
        var now = clock.GetUtcNow();
        foreach (var term in keyterms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            var allowed = _armed.Any(kv =>
            {
                if (kv.Key.User != user) return false;
                lock (kv.Value) return kv.Value.Until > now && kv.Value.Vocabulary.Contains(term.Trim());
            });
            if (!allowed) return false;
        }
        return true;
    }

    /// <summary>Sets the examiner's voice for this student's session grant (GAD-T475), chosen server-side by
    /// <see cref="ExaminerVoice"/>. Does nothing if the session isn't (or is no longer) armed.</summary>
    public void SetVoice(Guid user, Guid session, string? voiceId)
    {
        if (!_armed.TryGetValue((user, session), out var a)) return;
        lock (a) a.VoiceId = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim();
    }

    /// <summary>The examiner's voice for this student's session grant (GAD-T475); null for the platform voice or no grant (GAD-T495).</summary>
    public string? VoiceOf(Guid user, Guid session)
    {
        if (!_armed.TryGetValue((user, session), out var a)) return null;
        lock (a) return a.Until > clock.GetUtcNow() ? a.VoiceId : null;
    }

    /// <summary>
    /// The voice to say <paramref name="text"/> in: that of the student's live grant that offered it (the most recently armed one
    /// when several did), or null for the platform voice. <see cref="VoiceHubFilter"/> puts this into every <c>Speak</c> call.
    /// </summary>
    public string? VoiceFor(Guid user, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var now = clock.GetUtcNow();
        var key = text.Trim();
        string? voice = null;
        var bestUntil = DateTimeOffset.MinValue;
        foreach (var (k, a) in _armed)
        {
            if (k.User != user) continue;
            lock (a)
                if (a.Until > now && a.Until > bestUntil && a.Texts.ContainsKey(key)) (voice, bestUntil) = (a.VoiceId, a.Until);
        }
        return voice;
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
