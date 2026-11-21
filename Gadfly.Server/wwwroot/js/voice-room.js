// Voice mode for the exam room (GAD-T53). The examiner's questions are spoken by <ma-avatar> (MindAttic.Avatar: lip sync from the
// synthesized voice's word timings, 2D portrait when 3D is unavailable) and the student's answers are captured from the microphone
// (MindAttic.SpeechRecognition: hands-free voice activity detection or push-to-talk), relayed through the server to a recognizer, and
// handed to the Blazor exam room as final segments with word timings and confidence. This file owns only the browser side; the
// server decides what may be said (VoiceHubFilter) and what an answer is (VoiceAnswerBuilder). Nothing here talks to a provider
// directly and no key ever reaches the browser.
//
// Everything degrades to typing: if the microphone is refused, the browser lacks the pieces, or a hub fails, the page is told
// through OnVoiceProblem and the exam room goes back to the answer box. The buttons use one delegated listener (data-voice-action)
// because the microphone may only be opened from the click itself, not from a round trip to the server.
//
// The interview room (GAD-T231): beside the examiner the student sees their own camera, as a mirror. The self-view stream stays in
// this page: no frame is recorded, analysed or sent anywhere, and a refused or missing camera changes nothing else. While the
// microphone is open the response delay (examiner finished, student starts speaking) counts up live and freezes when the student
// speaks; it rides along with the answer so the on-screen transcript can show it. It is display only and never scored.

const LIB = "/lib/voice";
const SPEECH_HUB = "/hubs/speech";
const AVATAR_HUB = "/hubs/avatar";

let S = null;

const q = (name) => document.querySelector(`[data-voice-${name}]`);
const text = (name, value) => { const el = q(name); if (el) el.textContent = value; };

function hasWebGl() {
    try {
        const c = document.createElement("canvas");
        return !!(c.getContext("webgl2") || c.getContext("webgl"));
    } catch { return false; }
}

function loadScript(src) {
    return new Promise((resolve, reject) => {
        if (document.querySelector(`script[data-voice-src="${src}"]`)) return resolve();
        const s = document.createElement("script");
        s.src = src;
        s.dataset.voiceSrc = src;
        s.onload = () => resolve();
        s.onerror = () => reject(new Error("Could not load " + src));
        document.head.appendChild(s);
    });
}

function status(message) { text("status", message); }

function problem(kind, message, fatal) {
    if (!S) return;
    S.dotnet.invokeMethodAsync("OnVoiceProblem", kind, message, fatal).catch(() => { });
}

// ------------------------------------------------------------------------------------------------------------ public API

/** Prepares the page for voice and reports what the browser can do. Opens nothing: the student starts it with a click. */
export function init(dotnet, cfg) {
    dispose();
    S = {
        dotnet, cfg, running: false, allowed: false, gate: true, speaking: false, mode: "hands-free",
        segments: [], partial: "", listeningStartMs: null, startedAtUnixMs: null, cueTimer: null, captionTimers: [],
        mic: null, pcm16ToBase64: null, speechConn: null, avatarConn: null, avatar: null, speech: null, avatarOk: false,
        selfStream: null, waitStartMs: null, delayMs: null, delayTimer: null,
    };
    S.onClick = (e) => {
        const el = e.target.closest && e.target.closest("[data-voice-action]");
        if (!el || !S) return;
        const action = el.dataset.voiceAction;
        if (action === "start") void start();
        else if (action === "done") void submit(true);
        else if (action === "clear") discard();
        else if (action === "mode") toggleMode(el);
        else if (action === "self") void toggleSelfView();
    };
    S.onDown = (e) => { if (e.target.closest?.('[data-voice-action="ptt"]') && S?.mic && S.mode === "push-to-talk" && !S.gate) S.mic.pttDown(); };
    S.onUp = () => { if (S?.mic && S.mode === "push-to-talk") S.mic.pttUp(); };
    document.addEventListener("click", S.onClick);
    document.addEventListener("pointerdown", S.onDown);
    document.addEventListener("pointerup", S.onUp);
    return {
        microphone: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia) && window.isSecureContext !== false,
        webgl: hasWebGl(),
        audioContext: !!(window.AudioContext || window.webkitAudioContext),
    };
}

async function start() {
    const s = S;
    if (!s || s.running) return;
    s.running = true;
    s.dotnet.invokeMethodAsync("OnVoiceStarting").catch(() => { });   // shows the stage before the avatar is created in it
    status("Starting voice mode…");
    try {
        // 1. The microphone first, inside the click that asked for it.
        const speechPkg = await import(`${LIB}/speech/index.js`);
        s.pcm16ToBase64 = speechPkg.pcm16ToBase64;
        s.mic = new speechPkg.MicCapture({
            mode: "hands-free",
            onChunk: (c) => {
                if (s.gate || !s.speechConn) return;
                s.speechConn.send("SendAudio", s.pcm16ToBase64(c.pcm), c.captureStartMs).catch(() => { });
            },
            onSpeechStart: () => {
                if (s.gate) return;
                s.startedAtUnixMs ??= Date.now();
                freezeDelay(s);
                clearCue();
                status("Hearing you…");
            },
            onSpeechEnd: () => {
                if (s.gate || !s.speechConn) return;
                s.speechConn.send("Commit").catch(() => { });
            },
            onLevel: (l) => {
                const meter = q("level");
                if (meter) meter.style.setProperty("--level", String(Math.max(0, Math.min(1, (l.dbfs + 60) / 55))));
            },
            onError: (err) => problem("microphone", err && err.message ? err.message : String(err), true),
        });
        await s.mic.start();
        void startSelfView(s);   // the camera is only a mirror for the student: never awaited, never required
    } catch (err) {
        const denied = err && (err.name === "NotAllowedError" || err.name === "SecurityError");
        return fail(denied ? "mic-denied" : "microphone",
            denied ? "The microphone was not allowed." : "The microphone could not be opened.", s);
    }

    try {
        // 2. The two hubs. Both need the signed-in student's cookie and the server's grant for this exam.
        await loadScript(`${LIB}/signalr/signalr.min.js`);
        s.speechConn = new signalR.HubConnectionBuilder().withUrl(SPEECH_HUB).withAutomaticReconnect([0, 1000, 3000, 6000]).build();
        s.speechConn.on("partial", (t) => { s.partial = t || ""; drawTranscript(); });
        s.speechConn.on("final", onFinal);
        s.speechConn.on("problem", (kind, message, fatal) => { if (fatal) problem("recognizer", message, true); });
        s.speechConn.onreconnected(async () => {
            try { await s.speechConn.invoke("StartSession", { language: "en", diarize: true }); }
            catch { problem("hub", "The connection to speech recognition was lost.", true); }
        });
        s.speechConn.onclose(() => { if (s.running) problem("hub", "The connection to speech recognition was lost.", true); });
        await s.speechConn.start();
        await s.speechConn.invoke("StartSession", { language: "en", diarize: true });
    } catch (err) {
        return fail("hub", "Speech recognition could not be reached.", s);
    }

    // 3. The talking head. Voice answers work without it: the question is then only shown as text.
    try {
        const stage = q("stage");
        const pkg = await import(`${LIB}/avatar/index.js`);
        const el = document.createElement("ma-avatar");
        if (s.cfg.modelUrl) el.setAttribute("src", s.cfg.modelUrl);
        if (s.cfg.portraitUrl) el.setAttribute("portrait", s.cfg.portraitUrl);
        el.setAttribute("view", "upper");
        el.setAttribute("camera-x", "-0.2");
        el.setAttribute("aria-hidden", "true");
        el.addEventListener("fallback", (e) => s.dotnet.invokeMethodAsync("OnAvatarMode", "portrait", (e.detail && e.detail.reason) || "").catch(() => { }));
        el.addEventListener("speaking-start", () => { s.speaking = true; });
        stage.appendChild(el);
        s.avatar = el;
        s.avatarConn = new signalR.HubConnectionBuilder().withUrl(AVATAR_HUB).build();
        s.speech = pkg.attachSpeechHub(el, s.avatarConn);
        s.avatarConn.on("speechStart", () => { clearCaptions(); });
        s.avatarConn.on("speechChunk", (chunk) => scheduleCaptions(chunk && chunk.words));
        s.avatarConn.on("speechEnd", () => { showFullCaption(); });
        await s.avatarConn.start();
        await Promise.race([el.ready, new Promise((r) => setTimeout(r, 90000))]);
        s.avatarOk = true;
        s.dotnet.invokeMethodAsync("OnAvatarMode", el.mode || "3d", "").catch(() => { });
    } catch (err) {
        s.avatarOk = false;
        s.dotnet.invokeMethodAsync("OnAvatarMode", "none", "The examiner's picture could not start; questions are shown as text.").catch(() => { });
    }

    s.gate = true;
    status("Voice mode is on.");
    await s.dotnet.invokeMethodAsync("OnVoiceStarted", s.mic.vadKind || "energy");
}

function fail(kind, message, s) {
    problem(kind, message, true);
    stop();
}

/** The examiner says <paramref name="what"/> (the server only allows texts it offered for this student). */
export function speak(what, cacheable) {
    const s = S;
    if (!s || !s.running) return;
    clearCue();
    s.gate = true;
    s.segments = [];
    s.partial = "";
    s.startedAtUnixMs = null;
    resetDelay(s);
    drawTranscript();
    showCaption(what);
    status("The examiner is speaking…");
    const done = () => { s.speaking = false; if (S === s && s.allowed) openMic(); else if (S === s) status("Waiting."); };
    if (!s.avatarOk || !s.speech) { done(); return; }
    s.avatar.state = "idle";
    s.speaking = true;
    // A stalled synthesis or playback must never leave the student waiting for ever: after a generous allowance, listen anyway.
    const allowance = new Promise((resolve) => setTimeout(resolve, 20000 + 120 * (what || "").length));
    Promise.race([s.speech.speak(what, undefined, !!cacheable), allowance])
        .catch(() => { showCaption(what); })
        .finally(done);
}

/** Barge-in: the student starts talking over a question, or leaves. */
export function stopSpeaking() {
    if (S && S.speech) S.speech.stop().catch(() => { });
}

/** Whether an answer may be given right now (a question or a clarification is waiting, nothing is being judged). */
export function setAllowed(allowed) {
    const s = S;
    if (!s) return;
    const was = s.allowed;
    s.allowed = !!allowed;
    if (!s.running || s.speaking) return;
    if (s.allowed && was && !s.gate) return;
    if (s.allowed) openMic();
    else { s.gate = true; clearCue(); stopDelayTimer(s); if (s.avatar) s.avatar.state = "thinking"; status("Thinking about your answer…"); }
}

/** Keeps the newest transcript line in view (the list itself is rendered by VoiceRoom). */
export function scrollTranscript() {
    const log = q("transcript");
    if (log) log.scrollTop = log.scrollHeight;
}

export function dispose() {
    stop();
    if (S) {
        document.removeEventListener("click", S.onClick);
        document.removeEventListener("pointerdown", S.onDown);
        document.removeEventListener("pointerup", S.onUp);
    }
    S = null;
}

/** Releases the microphone, the hubs and the avatar, and leaves the page listening for another start. */
export function stop() {
    const s = S;
    if (!s) return;
    s.running = false;
    s.gate = true;
    clearCue();
    clearCaptions();
    resetDelay(s);
    stopSelfView(s, null);
    try { s.mic && s.mic.stop(); } catch { }
    // Hold the connection in a local: the fields are nulled below, before this promise settles, so reading s.speechConn in the
    // callback threw and the speech hub connection was never closed (GAD-T200).
    const speechConn = s.speechConn;
    try { speechConn && speechConn.invoke("StopSession").catch(() => { }).finally(() => speechConn.stop().catch(() => { })); } catch { }
    try { s.speech && s.speech.stop().catch(() => { }); } catch { }
    try { s.avatarConn && s.avatarConn.stop(); } catch { }
    try { s.speech && s.speech.dispose(); } catch { }
    try { s.avatar && s.avatar.remove(); } catch { }
    s.mic = s.speechConn = s.avatarConn = s.avatar = s.speech = null;
}

// ------------------------------------------------------------------------------------------------------- listening

function openMic() {
    const s = S;
    if (!s || !s.mic || !s.speechConn) return;
    s.gate = false;
    s.listeningStartMs = s.mic.currentTimeMs;
    startDelay(s);
    s.speechConn.send("MarkListening", s.listeningStartMs).catch(() => { });
    if (s.avatar) s.avatar.state = "listening";
    status(s.mode === "push-to-talk" ? "Hold the button and speak." : "Listening. Answer out loud.");
}

function onFinal(seg) {
    const s = S;
    if (!s || s.gate || !seg || !seg.text || !seg.text.trim()) return;
    s.partial = "";
    s.segments.push({
        text: seg.text,
        words: (seg.words || []).map((w) => ({
            text: w.text, startMs: w.startMs, endMs: w.endMs, confidence: w.confidence ?? null, speakerId: w.speakerId ?? null, isAudioEvent: !!w.isAudioEvent,
        })),
    });
    drawTranscript();
    // The end-of-answer cue: a stretch of silence after what was said. Speaking again cancels it; "Done answering" skips it.
    clearCue();
    const cue = q("cue");
    const ms = s.cfg.endCueMs || 4000;
    if (cue) { cue.style.setProperty("--cue-ms", ms + "ms"); cue.dataset.active = "true"; }
    status("Finishing your answer… keep talking to add more.");
    s.cueTimer = setTimeout(() => void submit(false), ms);
}

function clearCue() {
    if (!S) return;
    if (S.cueTimer) { clearTimeout(S.cueTimer); S.cueTimer = null; }
    const cue = q("cue");
    if (cue) cue.dataset.active = "false";
}

function drawTranscript() {
    const s = S;
    if (!s) return;
    const said = s.segments.map((x) => x.text).join(" ");
    text("draft", said);
    text("live", s.partial);
    const has = s.segments.length > 0;
    document.querySelectorAll("[data-voice-needs-draft]").forEach((b) => { b.disabled = !has; });
}

function discard() {
    if (!S) return;
    clearCue();
    S.segments = [];
    S.partial = "";
    S.startedAtUnixMs = null;
    drawTranscript();
    status("Cleared. Answer again whenever you're ready.");
}

async function submit(immediate) {
    const s = S;
    if (!s || s.gate || s.segments.length === 0) return;
    clearCue();
    const payload = {
        segments: s.segments, listeningStartMs: s.listeningStartMs, startedAtUnixMs: s.startedAtUnixMs,
        responseDelayMs: s.delayMs == null ? null : Math.round(s.delayMs),
    };
    s.gate = true;
    status("Sending your answer…");
    try {
        await s.dotnet.invokeMethodAsync("OnVoiceAnswer", payload);
        if (S === s) { s.segments = []; s.partial = ""; s.startedAtUnixMs = null; drawTranscript(); }
    } catch {
        // The answer is still in the box: let the student try again or switch to typing.
        if (S === s) { s.gate = false; status("That did not go through. Press Done answering to try again."); }
    }
}

function toggleMode(button) {
    const s = S;
    if (!s || !s.mic) return;
    s.mode = s.mode === "hands-free" ? "push-to-talk" : "hands-free";
    s.mic.setMode(s.mode);
    button.textContent = s.mode === "hands-free" ? "Switch to hold-to-talk" : "Switch to hands-free";
    document.querySelectorAll('[data-voice-action="ptt"]').forEach((b) => { b.hidden = s.mode !== "push-to-talk"; });
    if (!s.gate) status(s.mode === "push-to-talk" ? "Hold the button and speak." : "Listening. Answer out loud.");
}

// ------------------------------------------------------------------------------------------------------ response delay

/** The examiner has finished and the microphone is open: the delay counts up until the student starts speaking. */
function startDelay(s) {
    stopDelayTimer(s);
    s.waitStartMs = performance.now();
    s.delayMs = null;
    drawDelay(s);
    s.delayTimer = setInterval(() => drawDelay(s), 100);
}

/** The student's first speech after the microphone opened fixes the delay for this answer. */
function freezeDelay(s) {
    if (s.delayMs != null || s.waitStartMs == null) return;
    s.delayMs = performance.now() - s.waitStartMs;
    stopDelayTimer(s);
    drawDelay(s);
}

function stopDelayTimer(s) {
    if (s.delayTimer) { clearInterval(s.delayTimer); s.delayTimer = null; }
}

function resetDelay(s) {
    stopDelayTimer(s);
    s.waitStartMs = null;
    s.delayMs = null;
    drawDelay(s);
}

function drawDelay(s) {
    const el = q("delay");
    if (!el || S !== s) return;
    if (s.waitStartMs == null) { el.textContent = ""; el.dataset.state = "idle"; return; }
    const ms = s.delayMs ?? (performance.now() - s.waitStartMs);
    el.textContent = "Response delay: " + (ms / 1000).toFixed(1) + " s";
    el.dataset.state = s.delayMs == null ? "counting" : "final";
}

// ----------------------------------------------------------------------------------------------------------- self-view

/** Shows the student's own camera beside the examiner. Local only: the stream is attached to a muted <video> and nothing else. */
async function startSelfView(s) {
    if (!s || s.selfStream || !s.running) return;
    const video = q("self");
    if (!video || !(navigator.mediaDevices && navigator.mediaDevices.getUserMedia)) return selfState(s, "unavailable");
    selfState(s, "starting");
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "user", width: { ideal: 640 }, height: { ideal: 480 } }, audio: false });
        if (S !== s || !s.running) { stream.getTracks().forEach((t) => t.stop()); return; }
        s.selfStream = stream;
        video.srcObject = stream;
        try { await video.play(); } catch { }
        selfState(s, "on");
    } catch (err) {
        const denied = err && (err.name === "NotAllowedError" || err.name === "SecurityError");
        selfState(s, denied ? "denied" : "unavailable");
    }
}

/** Releases the camera; <paramref name="next"/> is the state to report, or null to report nothing (voice mode is stopping). */
function stopSelfView(s, next) {
    if (!s) return;
    const stream = s.selfStream;
    s.selfStream = null;
    try { stream && stream.getTracks().forEach((t) => t.stop()); } catch { }
    const video = q("self");
    if (video) { try { video.srcObject = null; } catch { } }
    if (next) selfState(s, next);
}

async function toggleSelfView() {
    const s = S;
    if (!s || !s.running) return;
    if (s.selfStream) stopSelfView(s, "hidden");
    else await startSelfView(s);
}

function selfState(s, state) {
    if (S !== s) return;
    s.dotnet.invokeMethodAsync("OnSelfView", state).catch(() => { });
}

// ------------------------------------------------------------------------------------------------------------ captions

function clearCaptions() {
    if (!S) return;
    S.captionTimers.forEach(clearTimeout);
    S.captionTimers = [];
    S.captionAt = performance.now();
    S.captionBuilt = "";
    text("caption", "");
}

function showCaption(what) { text("caption", what || ""); S && (S.caption = what || ""); }
function showFullCaption() { if (S && S.caption) text("caption", S.caption); }

/** Reveals the spoken words as their timings come, so the caption follows the voice. */
function scheduleCaptions(words) {
    const s = S;
    if (!s || !Array.isArray(words)) return;
    const el = q("caption");
    if (!el) return;
    for (const w of words) {
        const wait = Math.max(0, (w.startMs || 0) - (performance.now() - (s.captionAt ?? performance.now())));
        s.captionTimers.push(setTimeout(() => {
            s.captionBuilt = (s.captionBuilt ? s.captionBuilt + " " : "") + w.word;
            el.textContent = s.captionBuilt;
        }, wait));
    }
}
