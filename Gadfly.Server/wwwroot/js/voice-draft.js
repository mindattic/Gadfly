// Words already spoken, kept as a short-lived draft across a reload or a crash (GAD-T306). Only the committed
// (final) segments the recognizer had already heard are kept — never the in-flight partial, which has no stable
// meaning once the recognizer connection that produced it is gone. Scoped to this browser tab (sessionStorage), keyed
// by session and turn so a draft never reappears on a different question or a different student's session in the
// same browser. Restoring only ever fills the answer box: nothing here submits anything on its own, so a recovered
// draft can't be sent without the student pressing "Done answering" (or cleared with "Clear and answer again")
// themselves.

const PREFIX = "gadfly:voice-draft:";
/** Long enough to cover a reload or a crash and relaunch; not meant to resurrect a tab left open for days. */
const MAX_AGE_MS = 30 * 60 * 1000;

function key(sessionId) { return PREFIX + sessionId; }

/** Saves the answer captured so far for this turn, replacing whatever was saved for an earlier one. Call with an
 * empty list to clear it (the same as {@link clear}). */
export function save(sessionId, turn, segments) {
    if (!sessionId) return;
    if (!segments || segments.length === 0) { clear(sessionId); return; }
    try { sessionStorage.setItem(key(sessionId), JSON.stringify({ turn, segments, savedAt: Date.now() })); }
    catch { /* storage full, blocked (private browsing) or unavailable: the draft just isn't recoverable this time */ }
}

/** The saved draft's segments for this exact session and turn, if any and not stale; null otherwise. */
export function load(sessionId, turn) {
    if (!sessionId || turn === null || turn === undefined) return null;
    try {
        const raw = sessionStorage.getItem(key(sessionId));
        if (!raw) return null;
        const saved = JSON.parse(raw);
        if (!saved || saved.turn !== turn || !Array.isArray(saved.segments) || saved.segments.length === 0) return null;
        if (Date.now() - (saved.savedAt || 0) > MAX_AGE_MS) { clear(sessionId); return null; }
        return saved.segments;
    } catch { return null; }
}

/** Drops the saved draft (the answer was submitted, the question changed, or the student cleared it). */
export function clear(sessionId) {
    if (!sessionId) return;
    try { sessionStorage.removeItem(key(sessionId)); } catch { }
}
