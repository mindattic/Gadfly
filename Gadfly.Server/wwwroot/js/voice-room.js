// Voice mode for the exam room (GAD-T53). The examiner's questions are spoken by <ma-avatar> (MindAttic.Avatar: lip sync from the
// synthesized voice's word timings, 2D portrait when 3D is unavailable) and the student's answers are captured from the microphone
// (MindAttic.SpeechRecognition: hands-free voice activity detection or push-to-talk), relayed through the server to a recognizer, and
// handed to the Blazor exam room as final segments with word timings and confidence. This file owns only the browser side; the
// server decides what may be said (VoiceHubFilter) and what an answer is (VoiceAnswerBuilder). Nothing here talks to a provider
// directly and no key ever reaches the browser.
//
// Voice and camera are mandatory (GAD-T239): there is no typed answer mode. The examiner starts on its own when the exam room opens
// (autoStart; the Begin exam click on the start page is the user gesture the browser's audio needs). If the microphone or the camera
// is refused, missing or lost mid-exam, or a hub fails, the page is told through OnVoiceProblem and the exam room pauses until the
// student restores it and presses Try again. The buttons use one delegated listener (data-voice-action) because the microphone may
// only be opened from the click itself, not from a round trip to the server.
//
// The interview room (GAD-T231): beside the examiner the student sees their own camera. The self-view stream stays in this page:
// no frame is recorded or sent from here (camera checks, when the exam uses them, are webcam-integrity.js). While the
// microphone is open the response delay (examiner finished, student starts speaking) counts up live and freezes when the student
// speaks; it rides along with the answer so the on-screen transcript can show it. It is display only and never scored.

import { VoiceAcoustics } from "./voice-acoustics.js";

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
        selfStream: null, waitStartMs: null, delayMs: null, delayTimer: null, face: null,
        acoustics: new VoiceAcoustics(),   // voice strain indicators (GAD-T232): summary numbers only, never audio
    };
    S.onRemark = (e) => remarkHold(S, !!(e.detail && e.detail.active));
    document.addEventListener("gadfly-examiner-remark", S.onRemark);
    S.onClick = (e) => {
        const el = e.target.closest && e.target.closest("[data-voice-action]");
        if (!el || !S) return;
        const action = el.dataset.voiceAction;
        if (action === "start") void start();
        else if (action === "done") void submit(true);
        else if (action === "clear") discard();
        else if (action === "mode") toggleMode(el);
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

/**
 * Starts the examiner without a click of its own (GAD-T239). The click on Begin exam counts as the page's user activation, which
 * the browser needs before audio may play; a page opened fresh (a reload) has none, and is asked for one click instead.
 */
export function autoStart() {
    const s = S;
    if (!s || s.running) return;
    if (navigator.userActivation && navigator.userActivation.hasBeenActive === false) {
        problem("gesture", "The browser needs a click before audio can play.", true);
        return;
    }
    void start();
}

async function start() {
    const s = S;
    if (!s || s.running) return;
    s.running = true;
    s.dotnet.invokeMethodAsync("OnVoiceStarting").catch(() => { });   // shows the stage before the avatar is created in it
    status("Connecting you to the examiner…");
    try {
        // 1. The microphone first, inside the click that asked for it.
        const speechPkg = await import(`${LIB}/speech/index.js`);
        s.pcm16ToBase64 = speechPkg.pcm16ToBase64;
        s.mic = new speechPkg.MicCapture({
            mode: "hands-free",
            onChunk: (c) => {
                if (s.gate || !s.speechConn) return;
                try { s.acoustics.push(c.pcm); } catch { }
                s.speechConn.send("SendAudio", s.pcm16ToBase64(c.pcm), c.captureStartMs).catch(() => { });
            },
            onSpeechStart: () => {
                s.inSpeech = true;
                if (s.gate) return;
                s.startedAtUnixMs ??= Date.now();
                freezeDelay(s);
                clearCue();
                talking(s, true);
                status("Hearing you…");
            },
            onSpeechEnd: () => {
                s.inSpeech = false;
                try { s.acoustics.endSegment(); } catch { }
                talking(s, false);
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
        if (S !== s || !s.running) return;
        watchTracks(s, s.mic.stream, "mic-lost", "The microphone stopped working.");
    } catch (err) {
        if (S !== s || !s.running) return;
        const denied = err && (err.name === "NotAllowedError" || err.name === "SecurityError");
        return fail(denied ? "mic-denied" : "microphone",
            denied ? "The microphone was not allowed." : "The microphone could not be opened.", s);
    }

    // 1b. The camera is required too (GAD-T239): no camera, no exam.
    const camera = await startSelfView(s);
    if (S !== s || !s.running) return;
    if (camera !== "on") {
        return fail(camera === "denied" ? "camera-denied" : "camera",
            camera === "denied" ? "The camera was not allowed." : "The camera could not be opened.", s);
    }

    try {
        // 2. The two hubs. Both need the signed-in student's cookie and the server's grant for this exam.
        await loadScript(`${LIB}/signalr/signalr.min.js`);
        if (S !== s || !s.running) return;
        s.speechConn =new signalR.HubConnectionBuilder().withUrl(SPEECH_HUB).withAutomaticReconnect([0, 1000, 3000, 6000]).build();
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
        if (S !== s || !s.running) return;
        return fail("hub", "Speech recognition could not be reached.", s);
    }
    if (S !== s || !s.running) return;

    // 3. The talking head. Voice answers work without it: the question is then only shown as text.
    try {
        const stage = q("stage");
        const pkg = await import(`${LIB}/avatar/index.js`);
        if (S !== s || !s.running) return;
        const el = document.createElement("ma-avatar");
        if (s.cfg.modelUrl) el.setAttribute("src", s.cfg.modelUrl);
        if (s.cfg.portraitUrl) el.setAttribute("portrait", s.cfg.portraitUrl);
        el.setAttribute("view", "upper");
        el.setAttribute("camera-x", "-0.2");
        el.setAttribute("aria-hidden", "true");
        el.addEventListener("fallback", (e) => s.dotnet.invokeMethodAsync("OnAvatarMode", "portrait", (e.detail && e.detail.reason) || "").catch(() => { }));
        // A gaze remark (examiner-gaze.js speaks through the same avatar) is not one of this room's own utterances: its flag is
        // never set here, so it can't stay set and keep the microphone from reopening (GAD-T245).
        // Only while one of this room's own speak() calls is still waiting: a late start after its allowance ran out, or a remark's
        // start, must not set the flag after the call has cleared it (nothing would clear it again).
        el.addEventListener("speaking-start", () => { if (!s.remarkHold && s.speakToken) s.speaking = true; });
        stage.appendChild(el);
        s.avatar = el;
        s.avatarConn = new signalR.HubConnectionBuilder().withUrl(AVATAR_HUB).build();
        s.speech = pkg.attachSpeechHub(el, s.avatarConn);
        s.avatarConn.on("speechStart", () => { clearCaptions(); });
        s.avatarConn.on("speechChunk", (chunk) => scheduleCaptions(chunk && chunk.words));
        s.avatarConn.on("speechEnd", () => { showFullCaption(); });
        await s.avatarConn.start();
        await Promise.race([el.ready, new Promise((r) => setTimeout(r, 90000))]);
        if (S !== s || !s.running) return;
        s.avatarOk = true;
        void startFaceFollow(s);   // the self-view may already be on: the examiner looks toward the student's face
        s.dotnet.invokeMethodAsync("OnAvatarMode", el.mode || "3d", "").catch(() => { });
    } catch (err) {
        if (S !== s || !s.running) return;
        s.avatarOk = false;
        s.dotnet.invokeMethodAsync("OnAvatarMode", "none", "The examiner's picture could not start; questions are shown as text.").catch(() => { });
    }

    s.gate = true;
    status("The examiner is ready.");
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
    s.remarkCommitPending = false;   // a remark's late final belongs to the last answer, not to this question's
    s.segments = [];
    s.partial = "";
    s.startedAtUnixMs = null;
    s.acoustics.reset();
    resetDelay(s);
    drawTranscript();
    showCaption(what);
    status("The examiner is speaking…");
    const token = s.speakToken = {};
    const done = () => { if (s.speakToken === token) s.speakToken = null; s.speaking = false; if (S === s && s.allowed) openMic(); else if (S === s) status("Waiting."); };
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
        document.removeEventListener("gadfly-examiner-remark", S.onRemark);
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

/**
 * The examiner is giving a gaze remark (examiner-gaze.js tells us): close the gate so the microphone is not captured, sent or
 * transcribed into the answer while she speaks, and reopen it, without resetting the answer so far, when she has finished (GAD-T245).
 */
function remarkHold(s, active) {
    if (!s) return;
    if (active) {
        // Always mark the remark, even when the gate is already closed (thinking, sending): the avatar's speaking-start for a remark must never set
        // the speaking flag, nothing clears it but this room's own utterances.
        s.remarkHold = true;
        s.remarkClosedGate = false;
        if (!s.running || s.gate) return;   // not listening, so nothing to close
        s.remarkClosedGate = true;
        s.gate = true;
        s.partial = "";
        clearCue();
        talking(s, false);
        // Words already spoken are finalized now (the gate would swallow the speech-end commit), so none is lost to the remark.
        // The final answering this commit arrives while the gate is closed: let exactly that one through (onFinal).
        if (s.inSpeech && s.speechConn) { s.remarkCommitPending = true; s.speechConn.send("Commit").catch(() => { }); }
        drawTranscript();
        status("The examiner is speaking...");
    } else if (s.remarkHold) {
        const closed = s.remarkClosedGate;
        s.remarkHold = false;
        s.remarkClosedGate = false;
        if (!closed || !s.running || !s.gate || !s.allowed || s.speaking) return;   // something else (a question, a send) took over the gate
        s.gate = false;
        if (s.avatar) s.avatar.state = "listening";
        status(s.mode === "push-to-talk" ? "Hold the button and speak." : "Listening. Answer out loud.");
        if (s.segments.length && !s.inSpeech) {   // still talking: the next pause arms the cue as usual
            const ms = s.cfg.endCueMs || 4000;
            const cue = q("cue");
            if (cue) { cue.style.setProperty("--cue-ms", ms + "ms"); cue.dataset.active = "true"; }
            s.cueTimer = setTimeout(() => void submit(false), ms);
        }
    }
}

function onFinal(seg) {
    const s = S;
    if (!s) return;
    const remarkFinal = s.remarkCommitPending;   // the next final answers the remark's commit, whatever the gate says
    s.remarkCommitPending = false;
    if ((s.gate && !remarkFinal) || !seg || !seg.text || !seg.text.trim()) return;
    s.partial = "";
    s.segments.push({
        text: seg.text,
        words: (seg.words || []).map((w) => ({
            text: w.text, startMs: w.startMs, endMs: w.endMs, confidence: w.confidence ?? null, speakerId: w.speakerId ?? null, isAudioEvent: !!w.isAudioEvent,
        })),
    });
    drawTranscript();
    if (s.gate) return;   // the remark is still on: its end arms the cue for these words
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
    S.acoustics.reset();
    drawTranscript();
    status("Cleared. Answer again whenever you're ready.");
}

async function submit(immediate) {
    const s = S;
    if (!s || s.gate || s.segments.length === 0) return;
    clearCue();
    let acoustics = null;
    try { acoustics = s.acoustics.summary(); } catch { }
    const payload = {
        segments: s.segments, listeningStartMs: s.listeningStartMs, startedAtUnixMs: s.startedAtUnixMs,
        responseDelayMs: s.delayMs == null ? null : Math.round(s.delayMs), acoustics,
    };
    s.gate = true;
    talking(s, false);
    status("Sending your answer…");
    try {
        await s.dotnet.invokeMethodAsync("OnVoiceAnswer", payload);
        if (S === s) { s.segments = []; s.partial = ""; s.startedAtUnixMs = null; s.acoustics.reset(); drawTranscript(); }
    } catch {
        // The answer is still held here: let the student try again.
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

/**
 * Shows the student's own camera beside the examiner. The stream is attached to a muted <video> and nothing else. Returns the
 * outcome ("on", "denied" or "unavailable"); the camera is required, so anything but "on" pauses the exam (GAD-T239).
 */
async function startSelfView(s) {
    if (!s || !s.running) return "unavailable";
    if (s.selfStream) return "on";
    const video = q("self");
    if (!video || !(navigator.mediaDevices && navigator.mediaDevices.getUserMedia)) { selfState(s, "unavailable"); return "unavailable"; }
    selfState(s, "starting");
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "user", width: { ideal: 640 }, height: { ideal: 480 } }, audio: false });
        if (S !== s || !s.running) { stream.getTracks().forEach((t) => t.stop()); return "unavailable"; }
        if (!stream.getVideoTracks().some((t) => t.readyState === "live")) { stream.getTracks().forEach((t) => t.stop()); selfState(s, "unavailable"); return "unavailable"; }
        s.selfStream = stream;
        video.srcObject = stream;
        try { await video.play(); } catch { }
        watchTracks(s, stream, "camera-lost", "The camera stopped working.");
        selfState(s, "on");
        return "on";
    } catch (err) {
        const denied = err && (err.name === "NotAllowedError" || err.name === "SecurityError");
        const outcome = denied ? "denied" : "unavailable";
        selfState(s, outcome);
        return outcome;
    }
}

/**
 * A microphone or camera that ends mid-exam (unplugged, permission revoked, taken by another app) pauses the exam and asks the
 * student to restore it (GAD-T239). Tracks stopped by stop() itself are ignored: s.running is already false then.
 */
function watchTracks(s, stream, kind, message) {
    if (!stream) return;
    stream.getTracks().forEach((t) => t.addEventListener("ended", () => {
        if (S !== s || !s.running) return;
        fail(kind, message, s);
    }));
}

/** Releases the camera; <paramref name="next"/> is the state to report, or null to report nothing (voice mode is stopping). */
function stopSelfView(s, next) {
    if (!s) return;
    stopFaceFollow(s);
    const stream = s.selfStream;
    s.selfStream = null;
    try { stream && stream.getTracks().forEach((t) => t.stop()); } catch { }
    const video = q("self");
    if (video) { try { video.srcObject = null; } catch { } }
    if (next) selfState(s, next);
}

function selfState(s, state) {
    if (S !== s) return;
    s.dotnet.invokeMethodAsync("OnSelfView", state).catch(() => { });
}

// ------------------------------------------------------------------------------------------------------- examiner presence
// GAD-T241: the examiner looks at the student and listens visibly (ma-avatar.js: setGazeTarget, setStudentTalking). The only thing
// read from the self-view, on the device, is where the student's face is; that position goes to <ma-avatar> and nowhere else.

/** The student's voice activity, for the examiner's listening (nods, attentive look). Only while an answer is open. */
function talking(s, on) {
    if (!s || S !== s || !s.avatar || typeof s.avatar.setStudentTalking !== "function") return;
    try { s.avatar.setStudentTalking(on && !s.gate); } catch { }
}

const FACE_EVERY_MS = 200;     // native FaceDetector sampling
const FACE_LOST_MS = 1500;     // no face for this long: the examiner looks straight out of the screen again
const FACE_SMOOTH = 0.35;      // share of each new reading taken (steadies the gaze against detector jitter)

/**
 * Follows the student's face in the camera so the 3D examiner looks at them. Runs only while the self-view is on and the examiner
 * is the 3D model. Uses the browser's FaceDetector where it has one, else MindAttic.Webcam's self-hosted face landmarker (the same
 * build as the camera integrity checks) on a small, slow stream. Only a smoothed face position in [-1, 1] leaves this function, and
 * only to <ma-avatar>: no frame or position is stored or sent. Any failure just leaves her looking straight out of the screen.
 */
async function startFaceFollow(s) {
    if (!s || S !== s || !s.running || s.face || !s.selfStream || !s.avatar || s.avatar.mode !== "3d") return;
    const video = q("self");
    if (!video) return;
    const f = { stopped: false, timer: null, watch: null, core: null, unsub: null, x: 0, y: 0, seenAt: 0, has: false };
    s.face = f;
    const seen = (cx, cy) => {
        if (f.stopped || !Number.isFinite(cx) || !Number.isFinite(cy)) return;
        const nx = 1 - 2 * cx, ny = 2 * cy - 1;   // the camera image is unmirrored; the student sees a mirror
        if (!f.has) { f.x = nx; f.y = ny; f.has = true; }
        else { f.x += (nx - f.x) * FACE_SMOOTH; f.y += (ny - f.y) * FACE_SMOOTH; }
        f.seenAt = performance.now();
        try { s.avatar && s.avatar.setGazeTarget({ x: f.x, y: f.y }); } catch { }
    };
    f.watch = setInterval(() => {
        if (f.stopped || !f.has || performance.now() - f.seenAt < FACE_LOST_MS) return;
        f.has = false;
        try { s.avatar && s.avatar.setGazeTarget(null); } catch { }
    }, 500);

    if ("FaceDetector" in window) {
        try {
            const detector = new window.FaceDetector({ fastMode: true, maxDetectedFaces: 1 });
            let busy = false;
            f.timer = setInterval(async () => {
                if (f.stopped || busy || video.readyState < 2 || !video.videoWidth) return;
                busy = true;
                try {
                    const faces = await detector.detect(video);
                    const b = faces && faces[0] && faces[0].boundingBox;
                    if (b) seen((b.x + b.width / 2) / video.videoWidth, (b.y + b.height * 0.4) / video.videoHeight);
                } catch { } finally { busy = false; }
            }, FACE_EVERY_MS);
            return;
        } catch { /* listed but not usable: fall back to the landmarker */ }
    }

    try {
        const lib = await import("/webcam/webcam.browser.js");
        if (f.stopped) return;
        const core = new lib.WebcamCore({
            width: 320, height: 240, frameRate: 10, numFaces: 1,
            wasmBaseUrl: new URL("/webcam", location.href).href, modelUrl: new URL("/webcam/face_landmarker.bin", location.href).href,
        });
        f.core = core;
        f.unsub = core.subscribe((frame) => {
            const face = frame && frame.faces && frame.faces[0];
            const l = face && face.landmarks;
            if (!l || !l[33] || !l[263]) return;
            seen((l[33].x + l[263].x) / 2, (l[33].y + l[263].y) / 2);   // between the outer eye corners
        });
        await core.start();
        if (f.stopped) { try { core.stop(); } catch { } }
    } catch {
        try { f.core && f.core.stop(); } catch { }
        f.core = null;
    }
}

function stopFaceFollow(s) {
    const f = s && s.face;
    if (!f) return;
    s.face = null;
    f.stopped = true;
    if (f.timer) clearInterval(f.timer);
    if (f.watch) clearInterval(f.watch);
    try { f.unsub && f.unsub(); } catch { }
    try { f.core && f.core.stop(); } catch { }
    try { s.avatar && s.avatar.setGazeTarget(null); } catch { }
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
