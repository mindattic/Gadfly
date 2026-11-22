// Voice calibration on the get-ready page (GAD-T594): the student reads a short known sentence aloud, three times, through the real
// speech recognizer in that language (the mock one in mock mode), and only derived numbers come back: how long the reading took,
// the pauses in it, how sure the recognizer was and how close its words came to the sentence. No audio is kept anywhere, and the
// recognized words never leave this module: the page gets numbers only.
//
// The server arms a short-lived recognizer grant for the calibration (the same hub filter checks as the exam: the language and the
// vocabulary hints are the server's, never the browser's).

import * as prefs from "./device-prefs.js";

const LIB = "/lib/voice";
const SPEECH_HUB = "/hubs/speech";
const CONNECT_TIMEOUT_MS = 15000;
/** A reading is over this long after the student stops speaking. */
const END_SILENCE_MS = 1600;
/** Longest a reading may take before it ends by itself. */
const MAX_READING_MS = 20000;
/** How long to wait for the recognizer's last words after the reading ends. */
const FINAL_WAIT_MS = 3500;

let C = null;

function loadScript(src) {
    return new Promise((resolve, reject) => {
        if (window.signalR) return resolve();
        const existing = document.querySelector(`script[data-voice-src="${src}"]`);
        if (existing) { existing.addEventListener("load", () => resolve()); existing.addEventListener("error", reject); if (window.signalR) resolve(); return; }
        const s = document.createElement("script");
        s.src = src;
        s.dataset.voiceSrc = src;
        s.onload = () => resolve();
        s.onerror = reject;
        document.head.appendChild(s);
    });
}

function withTimeout(p, ms) {
    return Promise.race([p, new Promise((_, reject) => setTimeout(() => reject(new Error("timeout")), ms))]);
}

/**
 * Opens the chosen microphone and the recognizer in `language` ("en", "ko"), with `keyterms` (the sentence's words, which the server
 * has allowed). `level` is an optional element whose --level the microphone drives. Resolves true when ready, false otherwise.
 */
export async function open(language, keyterms, level) {
    close();
    const c = { language, conn: null, mic: null, active: false, inSpeech: false, started: null, lastEnd: null, pauses: [], finals: [], commits: 0, level };
    C = c;
    try {
        const speech = await import(`${LIB}/speech/index.js`);
        await loadScript(`${LIB}/signalr/signalr.min.js`);
        if (C !== c) return false;
        c.conn = new window.signalR.HubConnectionBuilder().withUrl(SPEECH_HUB).build();
        c.conn.on("final", (seg) => { if (C === c && seg) c.finals.push(seg); });
        await withTimeout(c.conn.start(), CONNECT_TIMEOUT_MS);
        await withTimeout(c.conn.invoke("StartSession", { language, diarize: false, keyterms: keyterms || [] }), CONNECT_TIMEOUT_MS);
        if (C !== c) return false;
        const deviceId = prefs.get("microphone");
        c.mic = new speech.MicCapture({
            mode: "hands-free",
            ...(deviceId ? { deviceId } : {}),
            onChunk: (chunk) => {
                if (!c.active || !c.conn) return;
                c.conn.send("SendAudio", speech.pcm16ToBase64(chunk.pcm), chunk.captureStartMs).catch(() => { });
            },
            onSpeechStart: () => {
                if (!c.active) return;
                const now = performance.now();
                if (c.lastEnd !== null) c.pauses.push(now - c.lastEnd);
                if (c.started === null) c.started = now;
                c.inSpeech = true;
            },
            onSpeechEnd: () => {
                if (!c.active) return;
                c.inSpeech = false;
                c.lastEnd = performance.now();
                c.commits++;
                c.conn && c.conn.send("Commit").catch(() => { });
            },
            onLevel: (l) => { if (c.level) c.level.style.setProperty("--level", String(Math.max(0, Math.min(1, (l.dbfs + 60) / 55)))); },
            onError: () => { },
        });
        await c.mic.start();
        return C === c;
    } catch {
        if (C === c) close();
        return false;
    }
}

/**
 * One reading of `expected`: listens until the student has spoken and then been quiet for a moment (or the time runs out), waits
 * for the recognizer's words, and resolves with numbers only: { heard, spanMs, pausesMs: [..], confidence (0..1 or null), match (0..1) }.
 */
export function reading(expected) {
    const c = C;
    if (!c || !c.conn || !c.mic) return Promise.resolve({ heard: false, spanMs: 0, pausesMs: [], confidence: null, match: 0 });
    c.started = null; c.lastEnd = null; c.pauses = []; c.finals = []; c.commits = 0; c.inSpeech = false;
    c.active = true;
    const began = performance.now();
    return new Promise((resolve) => {
        const tick = setInterval(async () => {
            if (C !== c) { clearInterval(tick); resolve({ heard: false, spanMs: 0, pausesMs: [], confidence: null, match: 0 }); return; }
            const now = performance.now();
            const finished = c.started !== null && !c.inSpeech && c.lastEnd !== null && now - c.lastEnd > END_SILENCE_MS;
            if (!finished && now - began < MAX_READING_MS) return;
            clearInterval(tick);
            c.active = false;
            if (c.inSpeech) { c.inSpeech = false; c.lastEnd = now; c.commits++; c.conn.send("Commit").catch(() => { }); }
            const waitUntil = performance.now() + FINAL_WAIT_MS;
            while (C === c && c.finals.length < Math.min(1, c.commits) && performance.now() < waitUntil) await new Promise((r) => setTimeout(r, 100));
            // A little longer for any further finals of a reading said in more than one breath.
            if (c.commits > 1) await new Promise((r) => setTimeout(r, 600));
            resolve(measure(c, expected));
        }, 100);
    });
}

function measure(c, expected) {
    const spanMs = c.started !== null && c.lastEnd !== null ? Math.max(0, c.lastEnd - c.started) : 0;
    const words = c.finals.flatMap((f) => (f && f.words) || []).filter((w) => w && !w.isAudioEvent);
    const text = c.finals.map((f) => (f && f.text) || "").join(" ");
    // Pauses from the recognizer's own word timings when it gives them (finer than the voice detector's), else the detector's.
    let pauses = c.pauses.slice();
    const timed = words.filter((w) => Number.isFinite(w.startMs) && Number.isFinite(w.endMs));
    if (timed.length > 1) {
        pauses = [];
        for (let i = 1; i < timed.length; i++) {
            const gap = timed[i].startMs - timed[i - 1].endMs;
            if (gap >= 200) pauses.push(gap);
        }
    }
    const confs = words.map((w) => w.confidence).filter((x) => typeof x === "number" && Number.isFinite(x));
    const confidence = confs.length ? confs.reduce((a, b) => a + b, 0) / confs.length : null;
    return { heard: spanMs > 300 || text.trim().length > 0, spanMs, pausesMs: pauses, confidence, match: similarity(text, expected) };
}

/** 1 - edit distance / length, over letters only (spaces and punctuation ignored, case folded). */
export function similarity(heard, expected) {
    const norm = (s) => (s || "").toLowerCase().normalize("NFC").replace(/[^\p{L}\p{N}]/gu, "");
    const a = norm(heard), b = norm(expected);
    if (!b.length) return 0;
    if (!a.length) return 0;
    const prev = new Array(b.length + 1), cur = new Array(b.length + 1);
    for (let j = 0; j <= b.length; j++) prev[j] = j;
    for (let i = 1; i <= a.length; i++) {
        cur[0] = i;
        for (let j = 1; j <= b.length; j++) cur[j] = Math.min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
        for (let j = 0; j <= b.length; j++) prev[j] = cur[j];
    }
    return Math.max(0, 1 - prev[b.length] / Math.max(a.length, b.length));
}

/** Releases the microphone and the recognizer. */
export function close() {
    const c = C;
    C = null;
    if (!c) return;
    c.active = false;
    try { c.mic && c.mic.stop && c.mic.stop(); } catch { }
    try { c.mic && c.mic.dispose && c.mic.dispose(); } catch { }
    try { c.conn && c.conn.invoke("StopSession").catch(() => { }).finally(() => c.conn.stop().catch(() => { })); } catch { }
    try { if (c.level) c.level.style.setProperty("--level", "0"); } catch { }
}
