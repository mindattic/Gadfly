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
//
// Delivery (GAD-T303): how she looks and sounds while speaking is examiner-voice.js (word-lit captions on the provider's own timings,
// a speaking style per kind of question, the portrait's moving mouth, frame-rate quality tiers, lost graphics, pausing with the tab,
// and the browser's own speech when the voice provider fails). This file tells it when an utterance is prepared, starts and ends.

// Typed answers (GAD-T260): for a student an instructor or admin granted the typed-answer accommodation, cfg.typed is set. The
// examiner, her spoken questions and the camera run exactly as above, but no microphone or speech recognizer is opened: the answer
// is typed in the exam room's own answer box. "Answering" still puts the avatar in its listening state, so the gaze check follows
// typed answers the same way.

import { VoiceAcoustics } from "./voice-acoustics.js";
import * as VoiceDraft from "./voice-draft.js";
import { useExaminerLook } from "./avatar-look.js";

const LIB = "/lib/voice";
const SPEECH_HUB = "/hubs/speech";
const AVATAR_HUB = "/hubs/avatar";

/** Longest a hub connect or its first call may take (GAD-T306) before this room gives up on it rather than sitting on
 * "Connecting you to the examiner…" forever: there is no error and no close event to react to while it's simply slow. */
const CONNECT_TIMEOUT_MS = 15000;

let S = null;

const q = (name) => document.querySelector(`[data-voice-${name}]`);
const text = (name, value) => { const el = q(name); if (el) el.textContent = value; };

/** Resolves or rejects with <paramref name="promise"/>, or rejects on its own after <paramref name="ms"/> if it
 * hasn't settled by then (GAD-T306): bounds a connect or an invoke call that might otherwise hang indefinitely. */
function withTimeout(promise, ms) {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error("Timed out after " + ms + "ms.")), ms);
        promise.then((v) => { clearTimeout(timer); resolve(v); }, (e) => { clearTimeout(timer); reject(e); });
    });
}

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
        presenter: null, utterId: null, ours: null,   // the delivery helpers (GAD-T303); the hub id and record of this room's own utterance
        selfStream: null, waitStartMs: null, delayMs: null, delayTimer: null, face: null,
        pauses: [], lastSpeechEndAt: null,   // the student's own mid-answer pauses, which the end-of-answer cue adapts to (GAD-T264)
        fill: null, fillPlaying: null,       // filling the wait while an answer is judged (GAD-T290): pending lines, the one being said
        fillAmbience: null,                  // the quiet typing loop under the wait (GAD-T409, typing since GAD-T502)
        ack: null, ackPlaying: null, ackEndedAt: null, ackDeferredAllowed: false,   // the acknowledgement as an answer goes in (GAD-T496)
        remarkOver: null, remarkEnded: null, // an eye-contact remark being said, which a question waits for (GAD-T472)
        acoustics: new VoiceAcoustics(),   // voice strain indicators (GAD-T232): summary numbers only, never audio
        hubUp: true, replayQueue: [],   // reconnect buffering (GAD-T360): audio and commits sent while the hub is down are replayed, in order
        commitSentAt: [], latenciesMs: [],   // recognizer latency (GAD-T361): commit-to-final time, kept for the next answer's diagnostics
        displayPartial: "", displayShrinkTimer: null,   // the stabilized partial shown to the student (GAD-T360): never flickers down and up
        sessionId: cfg.sessionId || null, turn: null, draftChecked: false, recovered: false,   // draft kept across a reload mid-answer (GAD-T306)
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
    S.onDown = (e) => {
        if (!e.target.closest?.('[data-voice-action="ptt"]') || !S?.mic || S.mode !== "push-to-talk") return;
        if (S.speaking && S.speakToken && S.allowed) interrupt(S);   // pressing Hold to talk while she asks: she stops and listens
        if (!S.gate) S.mic.pttDown();
    };
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
    // Typed answers (GAD-T260): no microphone at all.
    if (!s.cfg.typed) try {
        // 1. The microphone first, inside the click that asked for it.
        const speechPkg = await import(`${LIB}/speech/index.js`);
        s.pcm16ToBase64 = speechPkg.pcm16ToBase64;
        s.mic = new speechPkg.MicCapture({
            mode: "hands-free",
            // The silence that closes one stretch of speech and commits it (GAD-T498, Gadfly:Voice:SpeechHangoverMs): a commit is
            // not the end of the answer, it only lets the recognizer's final, and so the end-of-answer countdown, start sooner.
            ...(s.cfg.hangoverMs > 0 ? { hangoverMs: s.cfg.hangoverMs } : {}),
            onChunk: (c) => {
                if (s.gate || !s.speechConn) return;
                try { s.acoustics.push(c.pcm); } catch { }
                sendAudio(s, s.pcm16ToBase64(c.pcm), c.captureStartMs);
            },
            onSpeechStart: () => {
                s.inSpeech = true;
                noteCutOff(s);   // speaking again right after the silence sent the answer (GAD-T462)
                if (s.gate) return;
                if (s.lastSpeechEndAt !== null && s.startedAtUnixMs !== null) notePause(s, performance.now() - s.lastSpeechEndAt);
                s.lastSpeechEndAt = null;
                cancelBackchannel(s, true);   // she never talks over the student (GAD-T496)
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
                s.lastSpeechEndAt = performance.now();
                sendCommit(s);
                // A final that landed mid-speech did not arm the cue (onFinal); the speech has ended now, so arm it for what is held.
                if (s.segments.length > 0 && !s.cueTimer) armCue(s);
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
        // Typed answers (GAD-T260) need no speech recognizer: only the examiner's own hub below.
        if (!s.cfg.typed) {
        const speechConn = s.speechConn = new signalR.HubConnectionBuilder().withUrl(SPEECH_HUB).withAutomaticReconnect([0, 1000, 3000, 6000]).build();
        // A connection this room has already let go (stop(), then Try again) must not act on the new one: its late close used to
        // report "lost" and pause the exam again, and a late reconnect called into a field stop() had nulled (GAD-T316).
        const current = () => S === s && s.running && s.speechConn === speechConn;
        s.speechConn.on("partial", (t) => {
            // Stable partials (GAD-T360): the shown text only shrinks after a short hold, so a momentary correction that
            // immediately regrows never flickers the line the student is watching.
            stablePartial(s, t || "");
            // Words still arriving mean the answer is not over (GAD-T286): a final that armed the end-of-answer cue must not submit
            // while the recognizer is still hearing more, e.g. a provider-side commit in the middle of one unbroken stretch of speech.
            if (s.partial.trim() && !s.gate && s.cueTimer) { clearCue(); status("Hearing you…"); }
            drawTranscript();
        });
        s.speechConn.on("final", onFinal);
        s.speechConn.on("problem", (kind, message, fatal) => { if (fatal) problem("recognizer", message, true); });
        // A dropped connection retries a few times on its own (GAD-T306) before this room gives up and asks the
        // student to press Try again: a clear status either way, never a silent stall. Audio and commits captured while it
        // is down are queued, not lost (GAD-T360, below): the mic keeps running and nothing the student says is dropped.
        s.speechConn.onreconnecting(() => { if (!current()) return; s.hubUp = false; status("Reconnecting…"); });
        s.speechConn.onreconnected(async () => {
            if (!current()) return;
            try {
                await speechConn.invoke("StartSession", startSessionArgs(s));
                if (!current()) return;
                s.hubUp = true;
                await replayQueueAsync(s);
                // What the room is doing now, not always "Listening": a reconnect during a question, a wait filler or the
                // judging wait must not tell the student to answer (GAD-T316).
                status(roomStatus(s));
            }
            catch { if (current()) problem("hub", "The connection to speech recognition was lost.", true); }
        });
        s.speechConn.onclose(() => { if (current()) problem("hub", "The connection to speech recognition was lost.", true); });
        // Bounded (GAD-T306): a connect or an initial invoke that never resolves (no error, no close event to react
        // to) must not leave the room stuck on "Connecting you to the examiner…" forever.
        await withTimeout(s.speechConn.start(), CONNECT_TIMEOUT_MS);
        await withTimeout(s.speechConn.invoke("StartSession", startSessionArgs(s)), CONNECT_TIMEOUT_MS);
        }
    } catch (err) {
        if (S !== s || !s.running) return;
        return fail("hub", "Speech recognition could not be reached.", s);
    }
    if (S !== s || !s.running) return;

    // 3. The talking head. Voice answers work without it: the question is then only shown as text.
    // The 3D model can take a long while to load on a slow connection (it used to hold the first question for up to 90 s). When this
    // device will show 3D, the portrait stands in at once and asks the questions, with its moving mouth (GAD-T328), while the model
    // loads out of sight behind it; the 3D examiner takes over between sentences once she is ready (promote3d).
    try {
        const stage = q("stage");
        const pkg = await import(`${LIB}/avatar/index.js`);
        if (S !== s || !s.running) return;
        const standIn = !!(s.cfg.modelUrl && s.cfg.portraitUrl) && hasWebGl() && !prefersReducedMotion();
        const el = createAvatar(s, standIn ? null : s.cfg.modelUrl);
        stage.appendChild(el);
        s.avatar = el;
        s.presenter = await createPresenter(s, el);
        const avatarConn = s.avatarConn = new signalR.HubConnectionBuilder().withUrl(AVATAR_HUB).withAutomaticReconnect([0, 1000, 3000, 6000]).build();
        const currentAvatar = () => S === s && s.running && s.avatarConn === avatarConn;
        s.speech = pkg.attachSpeechHub(el, s.avatarConn);
        s.avatarPkg = pkg;
        s.avatarConn.on("speechStart", (m) => { s.utterId = m && m.id; });
        s.avatarConn.on("speechChunk", (m) => { if (m && m.id === s.utterId && s.ours) s.presenter && s.presenter.words(m.words); });
        // A dropped connection to the examiner's own hub (previously not even noticed) retries a few times on its own
        // before this room gives up and asks the student to press Try again, the same as a dropped microphone or
        // camera (GAD-T306): without this, a lost connection here just left the examiner silent and unresponsive.
        s.avatarConn.onreconnecting(() => { if (currentAvatar()) status("Reconnecting…"); });
        // Back again: the status line said "Reconnecting…" until something else happened to replace it (GAD-T316).
        s.avatarConn.onreconnected(() => { if (currentAvatar()) status(roomStatus(s)); });
        s.avatarConn.onclose(() => { if (currentAvatar()) problem("hub", "The connection to the examiner was lost.", true); });
        // The model starts downloading now, alongside the hub connect below, not after it.
        if (standIn) load3d(s, stage);
        // Bounded (GAD-T306), like the hub above: a connect that never resolves must not hold up the fallback below.
        await withTimeout(s.avatarConn.start(), CONNECT_TIMEOUT_MS);
        // With the stand-in this is at once (a portrait has nothing to load); without one it is the library's own quick
        // portrait fallback, or a 3D model the get-ready page already put in the browser's cache.
        await Promise.race([el.ready, new Promise((r) => setTimeout(r, 90000))]);
        if (S !== s || !s.running) return;
        s.avatarOk = true;
        void startFaceFollow(s);   // the self-view may already be on: the examiner looks toward the student's face
        // The stand-in is not a fallback: no "instead of the 3D head" note while the 3D examiner is still on her way.
        s.dotnet.invokeMethodAsync("OnAvatarMode", standIn ? "3d" : (el.mode || "3d"), "").catch(() => { });
    } catch (err) {
        if (S !== s || !s.running) return;
        s.avatarOk = false;
        dropLoading3d(s);
        // Without the talking head the questions are still read out by the browser's own speech, with the caption lit word by word.
        try { s.presenter?.dispose(); } catch { }
        try { s.presenter = (await import("./examiner-voice.js")).createPresenter(null, null, q("caption"), {}, () => { }); } catch { s.presenter = null; }
        s.dotnet.invokeMethodAsync("OnAvatarMode", "none", "The examiner's picture could not start; questions are read out by your browser and shown as text.").catch(() => { });
    }

    // Stopped during the avatar's awaits (a lost device, Try again, the page leaving): telling the page the examiner started would
    // undo its pause, and a second visibility listener would leak (GAD-T316).
    if (S !== s || !s.running) return;
    if (!s.cfg.typed) watchVisibility(s);   // a closed laptop lid (GAD-T306) is caught here, not only by a track's own "ended" event
    s.gate = true;
    status("The examiner is ready.");
    // start() runs detached (void start()): a circuit that went away must not surface as an unhandled rejection (GAD-T316).
    await s.dotnet.invokeMethodAsync("OnVoiceStarted", s.mic ? (s.mic.vadKind || "energy") : "typed").catch(() => { });
}

function fail(kind, message, s) {
    problem(kind, message, true);
    stop();
}

// ------------------------------------------------------------------------------------- the examiner on screen (GAD-T328)

function prefersReducedMotion() {
    try { return matchMedia("(prefers-reduced-motion: reduce)").matches; } catch { return false; }
}

/**
 * One <ma-avatar>. With no `src` it is the portrait at once (the stand-in while the 3D model loads); with one, the library loads the
 * model and falls back to the portrait itself when it can't show 3D. Its events feed this room's own utterances, whichever avatar
 * is speaking them.
 */
function createAvatar(s, src) {
    const el = document.createElement("ma-avatar");
    el.gadflyRole = src ? "examiner" : "stand-in";
    if (src) el.setAttribute("src", src);
    if (src) useExaminerLook(el);   // the examiner's luminous blue look, once her 3D model is up (GAD-T503)
    if (s.cfg.portraitUrl) el.setAttribute("portrait", s.cfg.portraitUrl);
    el.setAttribute("view", "upper");
    el.setAttribute("camera-x", "-0.2");
    el.setAttribute("aria-hidden", "true");
    el.addEventListener("fallback", (e) => {
        const reason = (e.detail && e.detail.reason) || "";
        el.gadflyFallback = reason;
        // The stand-in is the portrait on purpose, and a model still loading behind it is settled by load3d: neither is news.
        if (el.gadflyRole !== "examiner") return;
        s.dotnet.invokeMethodAsync("OnAvatarMode", "portrait", reason).catch(() => { });
    });
    // A gaze remark (examiner-gaze.js speaks through the same avatar) is not one of this room's own utterances: its flag is
    // never set here, so it can't stay set and keep the microphone from reopening (GAD-T245).
    // Only while one of this room's own speak() calls is still waiting: a late start after its allowance ran out, or a remark's
    // start, must not set the flag after the call has cleared it (nothing would clear it again).
    el.addEventListener("speaking-start", () => {
        if (s.avatar !== el) return;
        if (!s.remarkHold && s.speakToken) s.speaking = true;
        // This room's own utterance has started playing (not a gaze remark, which speaks through another connection): the
        // caption and mouth clock starts with her audio, so the words light as she says them.
        if (s.ours && !s.ours.started && !s.remarkHold) { s.ours.started = true; s.presenter && s.presenter.started(); }
        if (s.ackPlaying && !s.ackPlaying.started) s.ackPlaying.started = true;   // her acknowledgement is audible now (GAD-T496)
    });
    el.addEventListener("speech-error", (e) => { if (s.avatar === el && s.ours && e.detail && e.detail.id === s.utterId) s.ours.failed = true; });
    return el;
}

/** The delivery helpers (examiner-voice.js) for the avatar on screen: captions, speaking style, the portrait's mouth, quality tiers. */
async function createPresenter(s, el) {
    s.delivery ??= await import("./examiner-voice.js");
    return presenterFor(s, el);
}

/** As createPresenter, once examiner-voice.js is loaded: at once, so a swap never leaves a moment with no presenter. */
function presenterFor(s, el) {
    return s.delivery.createPresenter(el, q("stage"), q("caption"), { portraitMouth: s.cfg.portraitMouth },
        (reason) => s.dotnet.invokeMethodAsync("OnAvatarMode", "portrait", reason).catch(() => { }));
}

/**
 * Loads the 3D examiner out of sight behind the portrait that stands in for her (GAD-T328). Invisible but laid out at full size,
 * so the 3D scene is framed for the stage it will fill. A model that can't load leaves the portrait where it is, with the usual note.
 */
function load3d(s, stage) {
    const el = createAvatar(s, s.cfg.modelUrl);
    el.gadflyRole = "loading";
    el.style.cssText = "position:absolute;inset:0;opacity:0;pointer-events:none;transition:opacity .45s ease";
    stage.appendChild(el);
    s.loading3d = el;
    el.ready.then(() => {
        if (S !== s || !s.running || s.loading3d !== el) return;
        if (el.mode === "3d") { promote3d(s, el); return; }
        dropLoading3d(s);
        s.dotnet.invokeMethodAsync("OnAvatarMode", "portrait", el.gadflyFallback || "load-failed").catch(() => { });
    }, () => dropLoading3d(s));
}

function dropLoading3d(s) {
    if (s.promoteTimer) { clearTimeout(s.promoteTimer); s.promoteTimer = null; }
    const el = s.loading3d;
    s.loading3d = null;
    try { el && el.remove(); } catch { }
}

/**
 * The 3D examiner is ready: she takes over from the portrait between sentences, never while she (or a filler line or a gaze
 * remark) is speaking, and not while the student is mid-sentence. Her voice moves to her, she fades in over the portrait, and
 * the portrait is released.
 */
function promote3d(s, el) {
    const attempt = () => {
        s.promoteTimer = null;
        if (S !== s || !s.running || s.loading3d !== el) return;
        const old = s.avatar;
        const busy = !s.avatarOk || !s.speech || !s.avatarPkg || !s.delivery || s.speaking || s.speakToken || s.fillPlaying || s.remarkHold
            || (old && old.state === "speaking") || (s.inSpeech && !s.gate);
        if (busy) { s.promoteTimer = setTimeout(attempt, 250); return; }
        s.loading3d = null;
        try { s.speech.dispose(); } catch { }
        s.speech = s.avatarPkg.attachSpeechHub(el, s.avatarConn);
        const presenter = s.presenter;
        el.gadflyRole = "examiner";
        el.state = old ? old.state : "idle";
        s.avatar = el;
        try { presenter && presenter.dispose(); } catch { }
        try { s.presenter = presenterFor(s, el); } catch { s.presenter = null; }
        el.style.opacity = "1";
        setTimeout(() => {
            if (old && old !== s.avatar) { try { old.remove(); } catch { } }
            if (s.avatar === el) el.style.cssText = "";
        }, 500);
        void startFaceFollow(s);   // she can look at the student now (the portrait can't)
        s.dotnet.invokeMethodAsync("OnAvatarMode", "3d", "").catch(() => { });
    };
    attempt();
}

/** Who is reading her lines: the voice provider, or the browser's own speech when the provider failed. Told to the page on a change. */
function voiceSource(s, source) {
    const was = s.voiceSource || "provider";
    s.voiceSource = source;
    if (was !== source) s.dotnet.invokeMethodAsync("OnExaminerVoice", source).catch(() => { });
}

/** How long the tab must stay hidden before this counts as a possible sleep or a closed lid, not a quick tab switch. */
const LID_GAP_MS = 20000;

/**
 * A closed laptop lid, or the OS suspending the machine, often doesn't end a media track the clean way unplugging it
 * does (GAD-T306): the tab is simply hidden for a long stretch, then visible again with streams that may or may not
 * still be delivering anything. Caught here and treated exactly like a lost microphone or camera: the exam pauses
 * (the same path a lost track already takes, so the clock credit in GAD-T263 applies the same way) and "Try again"
 * re-requests both devices from scratch, rather than assuming whatever is left over from before the lid closed still
 * works.
 */
function watchVisibility(s) {
    s.hiddenSince = null;
    s.onVisibility = () => {
        if (S !== s || !s.running) return;
        if (document.hidden) { s.hiddenSince = performance.now(); return; }
        const since = s.hiddenSince;
        s.hiddenSince = null;
        if (since === null || performance.now() - since < LID_GAP_MS) return;
        const micDead = s.mic && s.mic.stream && !s.mic.stream.getAudioTracks().some((t) => t.readyState === "live");
        const cameraDead = s.selfStream && !s.selfStream.getVideoTracks().some((t) => t.readyState === "live");
        if (micDead) fail("mic-lost", "The microphone stopped working.", s);
        else if (cameraDead) fail("camera-lost", "The camera stopped working.", s);
    };
    document.addEventListener("visibilitychange", s.onVisibility);
}

function unwatchVisibility(s) {
    if (s && s.onVisibility) document.removeEventListener("visibilitychange", s.onVisibility);
}

/**
 * The examiner says <paramref name="what"/> (the server only allows texts it offered for this student), in the speaking style the
 * server picked for this kind of question (GAD-T303: neutral, open or probe; never a hint of the grade). <repeat> is true when the
 * student pressed Repeat the question (GAD-T474).
 */
export function speak(what, cacheable, style, turn, repeat) {
    const s = S;
    if (!s || !s.running) return;
    cancelFill(s);   // the next question is here: no more filler for this wait (GAD-T290)
    clearCue();
    s.cutWatch = null;   // the next question: speech from here on is not a cut-off of the last answer (GAD-T462)
    const wasListening = !s.gate;
    s.gate = true;
    // An answer the page could not take yet (GAD-T473) is kept through the same question asked again (Repeat the question, or Try
    // again after a lost device) and sent once she has finished. A different question means the exam moved on without it: it is
    // dropped, never sent as this question's answer, and the student is told so plainly.
    const nextTurn = turn ?? s.turn;
    const keep = !!s.kept && s.segments.length > 0 && s.kept.turn === nextTurn;
    const dropped = !!s.kept && !keep && s.segments.length > 0;
    // Repeat the question pressed mid-answer (GAD-T474): the words already said are not thrown away. They stay in "What I heard" and
    // in the reload draft, words still being spoken are finalized now (the closed gate would swallow their speech-end commit), and
    // once she has finished the microphone opens again on the same answer, so the student carries on or clears them. Nothing is sent
    // on its own: the end-of-answer cue is not armed until the student speaks again (or presses Done answering).
    const carry = !keep && !!repeat && !s.sending && nextTurn === s.turn && (s.segments.length > 0 || s.startedAtUnixMs !== null);
    s.carryFinals = carry;   // finals for speech before the repeat still join this answer while the gate is closed (onFinal)
    // A remark's late final belongs to the last answer, not to this question's; carried, the last answer is this one.
    if (!carry) s.remarkCommitPending = false;
    if (carry) {
        s.lastSpeechEndAt = null;   // listening to the question again is not a pause within the answer
        s.displayPartial = "";
        talking(s, false);
        stopDelayTimer(s);
        if (wasListening && s.inSpeech && s.speechConn) sendCommit(s);
    } else if (!keep) {
        s.kept = null;
        s.segments = [];
        s.startedAtUnixMs = null;
        s.acoustics.reset();
        resetDelay(s);
    }
    s.partial = "";
    // A fresh set of segments for this turn: any draft kept from a reload mid-answer (GAD-T306) no longer applies. Except the
    // first time after a reload: the page asks the same question again, and clearing here wiped the draft before the microphone
    // opened to restore it, so recovery never happened (GAD-T316). Until that restore is tried, a draft for another turn is
    // simply not loaded (VoiceDraft.load matches the turn) and the next save replaces it.
    s.turn = nextTurn;
    if (!carry) {
        s.recovered = false;
        text("recovered", dropped ? DROPPED_TEXT : "");
    }
    if (s.draftChecked && !keep && !carry) VoiceDraft.clear(s.sessionId);
    drawTranscript();
    status("The examiner is speaking…");
    const token = s.speakToken = {};
    // Only the newest utterance finishes the turn: one replaced by a later speak() (Repeat the question) must not open the microphone.
    const done = () => {
        if (S !== s || s.speakToken !== token) return;
        s.speakToken = null;
        s.speaking = false;
        s.ours = null;
        if (s.allowed) { openMic(); resendKept(s); } else status(s.held ? HELD_TEXT : "Waiting.");
    };
    s.speaking = true;
    // No eye-contact remark may start from here on (examiner-gaze.js only speaks one to a listening examiner): Repeat the question
    // pressed mid-answer used to leave her "listening" until the question's delivery began (GAD-T472).
    if (s.avatar && s.avatar.state === "listening") s.avatar.state = "idle";
    // A filler line still being said (GAD-T290), or an eye-contact remark (GAD-T245, spoken on the gaze module's own hub through the
    // same examiner), finishes first, so two lines never overlap and its end isn't taken for this one's (GAD-T472: Repeat the question
    // during "Please look at me while you answer" played both at once). Both are short and stop themselves if they stall; the extra
    // bounds here are only backstops.
    const bounded = (p, ms) => p ? Promise.race([p, new Promise((r) => setTimeout(r, ms))]) : Promise.resolve();
    // The question is ready before her acknowledgement (GAD-T496) has made a sound: it is dropped, and the question goes first.
    if (s.ackPlaying && !s.ackPlaying.started && s.speech) { s.ackPlaying.skipped = true; s.speech.stop().catch(() => { }); }
    const ready = Promise.all([bounded(s.fillPlaying, FILL.timeoutMs + 1000), bounded(s.remarkOver, REMARK_WAIT_MS)]);
    ready.then(() => {
        if (S !== s || s.speakToken !== token) return;   // replaced meanwhile: the newer call finishes the turn
        if (!s.running) { done(); return; }
        if (s.presenter) { s.presenter.idle(); s.presenter.prepare(what, style); } else showCaption(what);
        void deliver(s, token, what, !!cacheable).finally(done);
    });
}

/**
 * Plays one utterance and resolves when it is over. Through the avatar hub when the talking head is up; if the voice provider fails
 * (an error from the hub, a refused request, or no sound within a few seconds) the browser's own speech reads it instead, and with no
 * browser speech the text simply stays on screen. A stalled playback can never hold the exam: every path is bounded.
 */
async function deliver(s, token, what, cacheable) {
    const mine = () => S === s && s.speakToken === token;
    let viaBrowser = !s.avatarOk || !s.speech;
    if (!viaBrowser) {
        const ours = s.ours = { started: false, failed: false };
        if (s.avatar) s.avatar.state = "idle";
        try {
            const finished = s.speech.speak(what, undefined, cacheable).catch(() => { ours.failed = true; });   // refused or the hub call failed
            // No sound at all within this long means the provider is not answering: fall back rather than sit in silence.
            await visibleWait(9000, () => ours.started || ours.failed || !mine());
            if (mine() && !ours.started && !ours.failed) { ours.failed = true; try { await s.speech.stop(); } catch { } }
            if (!ours.failed) {
                let over = false;
                void finished.then(() => { over = true; });
                await visibleWait(20000 + 120 * (what || "").length, () => over || !mine());
            }
        } catch {
            ours.failed = true;   // refused or the hub call failed
        }
        if (s.ours === ours) s.ours = null;
        if (!mine()) return;
        if (!ours.failed) { voiceSource(s, "provider"); s.presenter && s.presenter.ended(); return; }
        viaBrowser = true;
    }
    if (!mine()) return;
    const spoke = s.presenter ? await s.presenter.speakWithBrowser(what) : false;
    // The provider failed but her picture is up: a short note says why she sounds different (GAD-T328). With no talking head at
    // all, the exam room's own note already says the browser reads the questions.
    if (spoke && s.avatarOk && s.speech) voiceSource(s, "browser");
    if (!mine()) return;
    if (!spoke) {
        if (s.presenter) s.presenter.plain(what); else showCaption(what);
        status("The examiner's voice isn't available right now. The question is on the screen.");
    } else s.presenter && s.presenter.ended();
}

/**
 * Resolves once <paramref name="ms"/> of time with the page visible has passed, or as soon as <paramref name="done"/> is true. Time
 * with the tab hidden doesn't count: she is paused then (examiner-voice.js), so a bound must not run out behind her back.
 */
function visibleWait(ms, done) {
    return new Promise((resolve) => {
        let left = ms, last = performance.now();
        const tick = () => {
            const now = performance.now();
            if (!document.hidden) left -= now - last;
            last = now;
            if (done() || left <= 0) resolve(); else setTimeout(tick, 150);
        };
        tick();
    });
}

/** Push-to-talk pressed while she is asking (GAD-T303): she stops at once and the microphone opens for the answer. */
function interrupt(s) {
    const token = s.speakToken;
    if (!token) return;
    s.speakToken = null;
    s.ours = null;
    try { s.speech && s.speech.stop().catch(() => { }); } catch { }
    try { s.presenter && (s.presenter.stopBrowser(), s.presenter.ended()); } catch { }
    s.speaking = false;
    if (s.allowed) openMic();
}
// ---------------------------------------------------------------------------------------------------- filling the wait

/** Filler timings (GAD-T290): a line stops itself after timeoutMs; a blocked attempt retries every retryMs; lines are gapMs apart. */
const FILL = { timeoutMs: 6000, retryMs: 500, gapMs: 8000, lateMs: 10000, afterAckMs: 1500 };

/**
 * The panel is judging the answer, taken as heard (GAD-T405), and writing the next question (GAD-T290). If the wait outlasts
 * firstAfterMs the examiner says <first> (a short neutral line the server chose and offered), and on a long wait <again> at
 * againAfterMs. Nothing is said while the microphone is open, a question or remark is being spoken, or another line is still
 * playing: the attempt waits for that and is dropped once its moment has passed. The microphone gate stays closed
 * the whole time, so a line is never captured or transcribed into an answer, and the next question (speak()) cancels what is left.
 * A very quiet distant-typing loop (GAD-T409, GAD-T502) runs alongside, under the filler line's own volume, so the wait never feels silent.
 */
export function fillWait(first, again, firstAfterMs, againAfterMs) {
    const s = S;
    if (!s || !s.running) return;
    cancelFill(s);
    const f = s.fill = { timers: [], startedAt: performance.now(), saidAt: null };
    startFillAmbience(s);
    const attempt = (what, until) => {
        if (S !== s || s.fill !== f) return;
        const now = performance.now();
        if (!canFill(s) || (f.saidAt !== null && now - f.saidAt < FILL.gapMs)
            || (s.ackEndedAt !== null && now - s.ackEndedAt < FILL.afterAckMs)) {
            if (now + FILL.retryMs < until) f.timers.push(setTimeout(() => attempt(what, until), FILL.retryMs));
            return;
        }
        f.saidAt = now;
        sayFiller(s, what);
    };
    const firstUntil = f.startedAt + Math.max(firstAfterMs, againAfterMs || 0);
    if (first) f.timers.push(setTimeout(() => attempt(first, firstUntil), Math.max(0, firstAfterMs || 0)));
    if (again && againAfterMs > 0) f.timers.push(setTimeout(() => attempt(again, f.startedAt + againAfterMs + FILL.lateMs), againAfterMs));
}

/** The wait is over (the next step arrived, or failed): drop the lines not yet said. One already playing just finishes. */
export function endWait() {
    if (S) cancelFill(S);
}

function cancelFill(s) {
    if (!s) return;
    stopFillAmbience(s);   // every exit from the wait (next question, wait ends, mic reopens, stop()) ends the loop here too
    if (!s.fill) return;
    s.fill.timers.forEach(clearTimeout);
    s.fill = null;
}

/** Soft distant-typing ambience under the wait (GAD-T409; typing instead of page turns since GAD-T502, a synthesized seamless 20 s loop) alternated so a whole exam never repeats
 * the same one twice running. A plain looping <audio>, well under the filler line's own volume, faded in and out so it is
 * never an abrupt start or cut and is always gone before the examiner's next question can be heard. */
const FILL_AMBIENCE = {
    sources: ["/sounds/typing-1.mp3", "/sounds/typing-2.mp3"],
    gain: 0.06, fadeInMs: 200, fadeOutMs: 300,
};
let fillAmbienceNext = 0;

function startFillAmbience(s) {
    try {
        const src = FILL_AMBIENCE.sources[fillAmbienceNext % FILL_AMBIENCE.sources.length];
        fillAmbienceNext++;
        const audio = new Audio(src);
        audio.loop = true;
        audio.volume = 0;
        s.fillAmbience = audio;
        audio.play().then(() => fadeVolume(audio, FILL_AMBIENCE.gain, FILL_AMBIENCE.fadeInMs)).catch(() => { });
    } catch { }
}

function stopFillAmbience(s) {
    const audio = s.fillAmbience;
    if (!audio) return;
    s.fillAmbience = null;
    fadeVolume(audio, 0, FILL_AMBIENCE.fadeOutMs, () => { try { audio.pause(); } catch { } });
}

/**
 * Ramps an <audio> element's volume toward target over ms, then calls done (shared by the wait ambience's fade in/out). Timer
 * steps, not animation frames (GAD-T316): a hidden tab runs no animation frames, so a fade-out begun there never reached its
 * pause() and the ambience loop played on behind the next question until the tab was shown again.
 */
function fadeVolume(audio, target, ms, done) {
    const from = audio.volume, startedAt = performance.now();
    const step = () => {
        const t = document.hidden && target < from ? 1 : Math.min(1, (performance.now() - startedAt) / ms);
        try { audio.volume = from + (target - from) * t; } catch { return; }
        if (t < 1) setTimeout(step, 16);
        else if (done) done();
    };
    step();
}

/** Only while quietly waiting on the panel: the gate shut, nothing being asked or answered, the examiner free to talk. */
function canFill(s) {
    return S === s && s.running && s.avatarOk && !!s.speech && !!s.avatar && s.gate && !s.allowed
        && !s.speaking && !s.speakToken && !s.remarkHold && !s.fillPlaying;
}

/** Says one filler line through the avatar hub (cached, so a repeat costs nothing); a stalled one is cut off. */
function sayFiller(s, what) {
    s.avatar.state = "idle";
    showCaption(what);
    let timer = null;
    const allowance = new Promise((resolve) => { timer = setTimeout(() => resolve(true), FILL.timeoutMs); });
    const playing = Promise.race([s.speech.speak(what, undefined, true).then(() => false), allowance])
        .then((late) => { if (late && S === s && s.speech) s.speech.stop().catch(() => { }); })
        .catch(() => { })
        .finally(() => {
            clearTimeout(timer);
            if (s.fillPlaying === playing) s.fillPlaying = null;
            // A question waiting on this line takes it from here (speak()); otherwise go back to what the room was doing.
            if (S !== s || !s.running || s.speakToken || s.speaking) return;
            if (s.allowed) { if (s.gate) openMic(); resendKept(s); }
            else { if (s.avatar) s.avatar.state = s.held ? "idle" : "thinking"; status(waitingText(s)); }
        });
    s.fillPlaying = playing;
}

// ------------------------------------------------------------------------------------------- acknowledging an answer

/** Longest an acknowledgement may take before it is cut off (GAD-T496): it is a word or two. */
const ACK_TIMEOUT_MS = 3000;

/**
 * The acknowledgement for the next answer (GAD-T496): a short fixed line the server picked (never the same twice running, never
 * evaluative) and already offered to the avatar hub, so it plays the instant the answer is in, with no round trip.
 */
export function nextAcknowledgement(what) {
    if (S) S.ack = what || null;
}

/**
 * Says the pending acknowledgement as the answer goes in ("Mm, okay.", "I see."), through the avatar hub like every line, so she
 * lip-syncs it; it comes from the phrase cache prepared at room start, so it costs nothing per turn. Only when she is free to talk
 * (no question, filler or remark under way). The microphone gate is already shut, so it is never heard as part of an answer, and
 * the wait filler (GAD-T290) holds off until shortly after it. A question that is ready before it has made a sound skips it (speak()).
 */
function sayAck(s) {
    const what = s.ack;
    s.ack = null;
    cancelBackchannel(s, false);
    if (!what || S !== s || !s.running || !s.avatarOk || !s.speech || !s.avatar
        || s.speaking || s.speakToken || s.remarkHold || s.fillPlaying) return;
    const ack = { started: false, skipped: false };
    s.ackPlaying = ack;
    s.ackDeferredAllowed = false;
    showCaption(what);
    let timer = null;
    const allowance = new Promise((resolve) => { timer = setTimeout(() => resolve(true), ACK_TIMEOUT_MS); });
    const playing = Promise.race([s.speech.speak(what, undefined, true).then(() => false), allowance])
        .then((late) => { if (late && S === s && s.speech) s.speech.stop().catch(() => { }); })
        .catch(() => { })
        .finally(() => {
            clearTimeout(timer);
            if (s.fillPlaying === playing) s.fillPlaying = null;
            if (s.ackPlaying === ack) s.ackPlaying = null;
            s.ackEndedAt = performance.now();
            const reopen = s.ackDeferredAllowed;
            s.ackDeferredAllowed = false;
            if (S !== s || !s.running || s.speakToken || s.speaking) return;   // a question takes it from here
            if (reopen && s.allowed) { if (s.gate) openMic(); resendKept(s); return; }
            if (s.kept) { resendKept(s); return; }
            if (!s.allowed) { if (s.avatar) s.avatar.state = s.held ? "idle" : "thinking"; status(waitingText(s)); }
            else if (s.gate && s.avatar) s.avatar.state = "thinking";   // the answer is on its way: she is not listening
        });
    s.fillPlaying = playing;
}

/** Barge-in: the student starts talking over a question, or leaves. */
export function stopSpeaking() {
    if (S && S.speech) S.speech.stop().catch(() => { });
    if (S && S.presenter) S.presenter.stopBrowser();
}

/**
 * Whether an answer may be given right now (a question or a clarification is waiting, nothing is being judged). <held> says the
 * answer is not wanted because staff paused the exam from the live monitor (GAD-T471): the microphone closes the same way, the
 * status line says why, and on release the microphone opens again with the words already said still there.
 */
export function setAllowed(allowed, held) {
    const s = S;
    if (!s) return;
    const was = s.allowed;
    s.allowed = !!allowed;
    s.held = !s.allowed && !!held;
    if (s.allowed) cancelFill(s);
    // An answer is wanted again while her acknowledgement plays (GAD-T496): its end opens the microphone (sayAck).
    if (s.ackPlaying) s.ackDeferredAllowed = s.allowed;
    // A filler line still playing (GAD-T290) keeps the microphone shut until it ends; its end opens it if an answer is wanted.
    if (!s.running || s.speaking || s.fillPlaying) return;
    if (s.allowed && was && !s.gate) return;
    if (s.allowed) { openMic(); resendKept(s); }
    else closeGate(s);
}

/** What the status line says while staff hold the exam (GAD-T471). */
const HELD_TEXT = "Your instructor paused the exam. Anything you've already said is kept; you can carry on once they resume it.";

/** The status line while no answer is wanted: a staff pause (GAD-T471), an answer kept to send again (GAD-T473), or the judging wait. */
function waitingText(s) {
    return s.held ? HELD_TEXT : s.kept ? KEPT_TEXT : "Thinking about your answer…";
}

/**
 * No answer is wanted any more (a step is running, or staff paused the exam): the microphone gate closes and the end-of-answer
 * cue is dropped, so nothing is sent. The words already said stay in "What I heard" and in the reload draft (GAD-T471): speech
 * still in progress is committed now, since the gate would otherwise swallow its final, and that one final is let through (onFinal).
 */
function closeGate(s) {
    const wasListening = !s.gate;
    s.gate = true;
    clearCue();
    stopDelayTimer(s);
    if (wasListening) {
        s.lastSpeechEndAt = null;   // the pause is not the student's own
        s.partial = "";
        s.displayPartial = "";
        talking(s, false);
        if (s.inSpeech && s.speechConn) { s.remarkCommitPending = true; sendCommit(s); }
        drawTranscript();
    }
    // Held by staff she simply waits ("idle"); otherwise she is thinking about the answer. Neither is "listening", so the eye-contact
    // check neither fades the screen nor speaks a remark meanwhile.
    if (s.avatar) s.avatar.state = s.held ? "idle" : "thinking";
    status(waitingText(s));
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
    unwatchVisibility(s);
    cancelFill(s);
    s.fillPlaying = null;
    s.ackPlaying = null;
    s.ack = null;
    endRemarkWait(s);   // nothing of this room waits on a remark any more (GAD-T472)
    s.speakToken = null;
    s.ours = null;
    s.speaking = false;
    clearCue();
    clearCaptions();
    try { s.presenter && s.presenter.dispose(); } catch { }
    s.presenter = null;
    resetDelay(s);
    stopSelfView(s, null);
    s.hubUp = true;
    s.replayQueue = [];
    s.commitSentAt = [];
    s.latenciesMs = [];
    s.displayPartial = "";
    if (s.displayShrinkTimer) { clearTimeout(s.displayShrinkTimer); s.displayShrinkTimer = null; }
    try { s.mic && Promise.resolve(s.mic.stop()).catch(() => { }); } catch { }   // async: a failed close is not an unhandled rejection
    // Hold the connection in a local: the fields are nulled below, before this promise settles, so reading s.speechConn in the
    // callback threw and the speech hub connection was never closed (GAD-T200).
    const speechConn = s.speechConn;
    try { speechConn && speechConn.invoke("StopSession").catch(() => { }).finally(() => speechConn.stop().catch(() => { })); } catch { }
    try { s.speech && s.speech.stop().catch(() => { }); } catch { }
    try { s.avatarConn && s.avatarConn.stop(); } catch { }
    try { s.speech && s.speech.dispose(); } catch { }
    try { s.avatar && s.avatar.remove(); } catch { }
    dropLoading3d(s);   // a 3D examiner still loading behind the portrait (GAD-T328)
    s.voiceSource = null;
    s.mic = s.speechConn = s.avatarConn = s.avatar = s.speech = null;
}

// ------------------------------------------------------------------------------------------------------- listening

function openMic() {
    const s = S;
    // Typed answers (GAD-T260): the answer box is the exam room's; the examiner listens (the gaze check follows that state).
    if (s && s.cfg.typed && s.running) {
        s.gate = false;
        if (s.avatar) s.avatar.state = "listening";
        status(listenText(s));
        return;
    }
    if (!s || !s.mic || !s.speechConn) return;
    // An answer already under way (the microphone closed under it for a staff pause, GAD-T471): it carries on where it was, so its
    // listening start and response delay stay those of the question, and nothing is sent until the student finishes as usual.
    const resuming = s.segments.length > 0 || s.startedAtUnixMs !== null;
    s.gate = false;
    s.held = false;
    s.carryFinals = false;   // the gate is open again: finals join the answer the usual way (GAD-T474)
    s.lastSpeechEndAt = null;   // the wait before an answer starts is thinking time, not a pause within it
    if (!resuming) {
        s.backchannelSaid = false;   // one listening backchannel per answer (GAD-T496)
        s.listeningStartMs = s.mic.currentTimeMs;
        startDelay(s);
        s.speechConn.send("MarkListening", s.listeningStartMs).catch(() => { });
    }
    if (s.avatar) s.avatar.state = "listening";
    // After a staff pause (GAD-T471) or Repeat the question (GAD-T474) the words already said are still in "What I heard".
    status(resuming && s.segments.length > 0 && !s.recovered
        ? "You can carry on answering. What you'd already said is kept: keep talking, press Done answering, or Clear and answer again to start over."
        : listenText(s));
    if (!s.draftChecked) { s.draftChecked = true; tryRestoreDraft(s); }
}

/**
 * The page reloaded, or the browser crashed and relaunched, mid-answer (GAD-T306): the words already spoken for
 * this exact question are still in sessionStorage, so they reappear here, clearly marked as recovered. Tried once
 * per page load, and only onto an empty answer: it can never overwrite words the student has already said since
 * the reload. It only fills the box — the cue that auto-submits on silence is not armed by it, so nothing is sent
 * until the student presses "Done answering" (or starts over with "Clear and answer again") themselves.
 */
function tryRestoreDraft(s) {
    if (s.segments.length > 0 || s.turn === null) return;
    const restored = VoiceDraft.load(s.sessionId, s.turn);
    if (!restored || restored.length === 0) return;
    s.segments = restored.map((seg) => ({ text: seg && seg.text || "", words: (seg && seg.words) || [] }));
    s.recovered = true;
    drawTranscript();
    text("recovered", "Recovered what you'd already said before the page reloaded. Keep talking to add more, or press “Clear and answer again” to start over.");
}

/**
 * The examiner is giving a gaze remark (examiner-gaze.js tells us): close the gate so the microphone is not captured, sent or
 * transcribed into the answer while she speaks, and reopen it, without resetting the answer so far, when she has finished (GAD-T245).
 */
function remarkHold(s, active) {
    if (!s) return;
    if (active) {
        // Settles when the remark ends: a question asked meanwhile (Repeat the question) waits for it in speak() (GAD-T472).
        if (!s.remarkOver) s.remarkOver = new Promise((resolve) => { s.remarkEnded = resolve; });
        // Always mark the remark, even when the gate is already closed (thinking, sending): the avatar's speaking-start for a remark must never set
        // the speaking flag, nothing clears it but this room's own utterances.
        s.remarkHold = true;
        s.remarkClosedGate = false;
        if (!s.running || s.gate) return;   // not listening, so nothing to close
        s.remarkClosedGate = true;
        s.gate = true;
        s.lastSpeechEndAt = null;   // the examiner's remark is not the student's pause
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
        endRemarkWait(s);
        // The answer was taken from her meanwhile ("I don't know", time up): the remark left her idle, and examiner-gaze.js puts an
        // idle examiner back to "listening", so the eye-contact check went on treating the judging wait as answering (GAD-T316).
        if (s.running && !s.allowed && !s.speaking && !s.speakToken && !s.fillPlaying && s.avatar && s.avatar.state === "idle") s.avatar.state = "thinking";
        if (!closed || !s.running || !s.gate || !s.allowed || s.speaking) return;   // something else (a question, a send) took over the gate
        s.gate = false;
        if (s.avatar) s.avatar.state = "listening";
        status(listenText(s));
        if (s.segments.length && !s.inSpeech) {   // still talking: the next pause arms the cue as usual
            const ms = endCueMs(s);
            const cue = q("cue");
            if (cue) { cue.style.setProperty("--cue-ms", ms + "ms"); cue.dataset.active = "true"; }
            s.cueTimer = setTimeout(() => void submit(false), ms);
        }
    }
}

/**
 * Longest a question waits for an eye-contact remark to finish (GAD-T472): the gaze module cuts a stalled remark off after its own
 * remarkTimeoutMs (12 s, examiner-gaze.js); this is only a backstop a little past that.
 */
const REMARK_WAIT_MS = 13000;

/** The remark is over (or the room stopped): a question waiting on it in speak() goes ahead (GAD-T472). */
function endRemarkWait(s) {
    const ended = s.remarkEnded;
    s.remarkEnded = null;
    s.remarkOver = null;
    if (ended) ended();
}

/** What the status line says while an answer is open. */
function listenText(s) {
    if (s.cfg.typed) return "Type your answer in the box below.";
    return s.mode === "push-to-talk" ? "Hold the button and speak." : "Listening. Answer out loud.";
}

/** The status line for whatever the room is doing right now (after a hub reconnect, which interrupted it). */
function roomStatus(s) {
    if (s.speaking || s.speakToken || s.remarkHold || s.fillPlaying) return "The examiner is speaking…";
    if (!s.gate) return listenText(s);
    if (!s.allowed) return waitingText(s);
    return "The examiner is ready.";
}

function onFinal(seg) {
    const s = S;
    if (!s) return;
    // Recognizer latency (GAD-T361): how long this final took from the Commit that asked for it. Oldest pending commit
    // first, since finals arrive in the order they were committed; a remark's own commit (not a student answer) is
    // popped too, which is fine, since both measure the same thing (the recognizer's turnaround on a stretch of speech).
    if (s.commitSentAt.length > 0) {
        const ms = performance.now() - s.commitSentAt.shift();
        if (Number.isFinite(ms) && ms >= 0) { s.latenciesMs.push(ms); if (s.latenciesMs.length > 20) s.latenciesMs.shift(); }
    }
    const remarkFinal = s.remarkCommitPending;   // the next final answers the remark's commit, whatever the gate says
    s.remarkCommitPending = false;
    // Repeat the question pressed mid-answer (GAD-T474): no audio is sent while the gate is closed, so a final arriving meanwhile is
    // for words said before she began again, and joins the answer they belong to.
    const carried = !!s.carryFinals;
    if ((s.gate && !remarkFinal && !carried) || !seg || !seg.text || !seg.text.trim()) return;
    s.partial = "";
    s.displayPartial = "";
    // The same final delivered twice (a recognizer fallback or a commit racing the provider's own) is one segment (GAD-T286).
    const norm = seg.text.trim().replace(/\s+/g, " ").toLowerCase();
    const now = performance.now();
    if (s.lastFinalNorm === norm && now - (s.lastFinalAt || 0) < 5000) { s.lastFinalAt = now; return; }
    s.lastFinalNorm = norm; s.lastFinalAt = now;
    s.segments.push({
        text: seg.text,
        words: (seg.words || []).map((w) => ({
            text: w.text, startMs: w.startMs, endMs: w.endMs, confidence: w.confidence ?? null, speakerId: w.speakerId ?? null, isAudioEvent: !!w.isAudioEvent,
        })),
    });
    // Kept as a draft in case the page reloads or the browser crashes before this answer is sent (GAD-T306).
    VoiceDraft.save(s.sessionId, s.turn, s.segments);
    drawTranscript();
    if (s.gate) return;   // the remark is still on: its end arms the cue for these words
    // The end-of-answer cue: a stretch of silence after what was said. Speaking again cancels it; "Done answering" skips it.
    // While the student is still speaking (a provider commit mid-speech) it is not armed: onSpeechEnd arms it.
    if (s.inSpeech) { clearCue(); return; }
    armCue(s);
}

function armCue(s) {
    clearCue();
    const cue = q("cue");
    const ms = endCueMs(s);
    if (cue) { cue.style.setProperty("--cue-ms", ms + "ms"); cue.dataset.active = "true"; }
    status("Finishing your answer… keep talking to add more.");
    s.cueTimer = setTimeout(() => void submit(false), ms);
    armBackchannel(s);
}

/**
 * The listening backchannel (GAD-T496, off unless Gadfly:Voice:BackchannelMs is set): once a finished sentence has been followed
 * by cfg.backchannelMs of silence, she gives a soft "Mm." or "Mm-hm." that does not end the turn. The countdown runs on; speaking
 * again stops her at once and simply carries on the answer. At most once per answer, and only when the short (finished-sentence)
 * countdown is running and she is otherwise silent. Off by default: the microphone is open while she makes it, so it relies on
 * the browser's echo cancellation to keep her sound out of the answer.
 */
function armBackchannel(s) {
    const lines = s.backchannels;
    const ms = s.cfg.backchannelMs;
    if (!(ms > 0) || !lines || !lines.length || s.backchannelSaid || s.gate || !finishedSentence(s) || awaitingFinal(s)) return;
    s.backchannelTimer = setTimeout(() => {
        s.backchannelTimer = null;
        if (S !== s || !s.running || s.gate || s.inSpeech || !s.cueTimer || s.backchannelSaid || !s.avatarOk || !s.speech
            || s.speaking || s.speakToken || s.remarkHold || s.fillPlaying) return;
        s.backchannelSaid = true;
        const what = lines[Math.floor(Math.random() * lines.length)];
        const bc = s.backchannelPlaying = s.speech.speak(what, undefined, true).catch(() => { })
            .finally(() => { if (s.backchannelPlaying === bc) s.backchannelPlaying = null; });
    }, ms);
}

/** Lines the server offered for the listening backchannel (GAD-T496); nothing is said with none. */
export function backchannels(lines) {
    if (S) S.backchannels = Array.isArray(lines) ? lines.filter((x) => typeof x === "string" && x) : null;
}

function cancelBackchannel(s, silence) {
    if (s.backchannelTimer) { clearTimeout(s.backchannelTimer); s.backchannelTimer = null; }
    if (silence && s.backchannelPlaying && s.speech) { s.backchannelPlaying = null; s.speech.stop().catch(() => { }); }
}

/**
 * The end-of-answer silence for this student (GAD-T264). It starts at the configured floor (lengthened on the server by an
 * extra-time accommodation) and adapts to the student's own pattern: once a few pauses within answers have been seen (silence
 * after which the student went on speaking), it becomes half again the student's longer pauses (the 80th percentile of the
 * last 20), never below the floor and never above the ceiling. It only ever lengthens the cue: a quick speaker keeps the floor.
 *
 * Endpointing (GAD-T498): when what was heard last is a finished sentence (finishedSentence) and no committed speech is still
 * waiting for its final, the floor is the shorter endCueCompleteMs instead, so a finished answer goes in after about two seconds
 * rather than four, the way a conversation turns. A student who stops mid-sentence ("...because", "and um") keeps the full floor.
 * Each answer that turned out to be cut off (the student went on within CUT_OFF_MS, GAD-T462) lengthens the short floor by
 * CUT_OFF_BACKOFF_MS for the rest of the exam, never past the full floor, so the shorter wait backs off for a student it cuts short.
 */
function endCueMs(s) {
    const full = s.cfg.endCueMs || 4000;
    const ceiling = Math.max(full, s.cfg.endCueMaxMs || full);
    const short = s.cfg.endCueCompleteMs > 0 ? Math.min(full, s.cfg.endCueCompleteMs + (s.cutOffBackoffMs || 0)) : full;
    const floor = short < full && finishedSentence(s) && !awaitingFinal(s) ? short : full;
    if (s.pauses.length < PAUSE.minSamples) return floor;
    const sorted = [...s.pauses].sort((a, b) => a - b);
    const p80 = sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * 0.8))];
    return Math.round(Math.min(ceiling, Math.max(floor, p80 * PAUSE.factor)));
}

/** Words that lead into more: a final ending on one is mid-thought even if the recognizer put a full stop after it (GAD-T498). */
const LEADS_ON = new Set(("and but or so because since although though while if when whereas unless until than then like as "
    + "the a an of to for with in on at by from about into that which who whom whose what where why how this these those "
    + "my your his her its our their is are was were be been being has have had do does did can could would should will "
    + "um uh er erm hmm mm well also plus not very more most just").split(" "));

/** The last thing heard ends a sentence: a final stop, question or exclamation mark, and no word that leads into more after it. */
function finishedSentence(s) {
    if (s.partial && s.partial.trim()) return false;   // words still arriving
    const last = s.segments.length ? String(s.segments[s.segments.length - 1].text || "").trim() : "";
    if (!/[.?!]["'”’)]*$/.test(last)) return false;
    const words = last.toLowerCase().replace(/[^a-z' -]+/g, " ").trim().split(/\s+/);
    return !LEADS_ON.has(words[words.length - 1] || "");
}

/** A commit sent in the last few seconds has no final yet: speech the recognizer hasn't returned, so the answer isn't whole. */
const FINAL_WAIT_MS = 3000;
function awaitingFinal(s) {
    const newest = s.commitSentAt.length ? s.commitSentAt[s.commitSentAt.length - 1] : null;
    return newest !== null && performance.now() - newest < FINAL_WAIT_MS;
}

const PAUSE = { minMs: 300, minSamples: 3, keep: 20, factor: 1.5 };

/** Records one pause within an answer (speech ended, then the student went on). Too short to be a pause is ignored. */
function notePause(s, ms) {
    if (!Number.isFinite(ms) || ms < PAUSE.minMs) return;
    const ceiling = Math.max(s.cfg.endCueMs || 4000, s.cfg.endCueMaxMs || 0);
    s.pauses.push(Math.min(ms, ceiling));
    if (s.pauses.length > PAUSE.keep) s.pauses.shift();
}

/**
 * Answer cut-offs (GAD-T462). When the end-of-answer silence (not "Done answering") sent the answer, the next few seconds are
 * watched: if the student starts speaking again within CUT_OFF_MS, the answer may have been cut short. Only the delay is sent,
 * once per answer, never audio; the server range-checks it and keeps it with the answer's speech metrics as a neutral note for
 * reviewers and a fairness rate. It never changes this answer, the next question or the grade. Speech while the examiner is
 * talking (a wait filler or the next question) is not counted, since the microphone may be hearing her.
 */
const CUT_OFF_MS = 3000;
/** How much each cut-off lengthens the short end-of-answer silence after a finished sentence (GAD-T498). */
const CUT_OFF_BACKOFF_MS = 750;

function noteCutOff(s) {
    if (!s || s.cutWatch == null) return;
    const ms = performance.now() - s.cutWatch;
    // An eye-contact remark is the examiner talking too (GAD-T316): the microphone may be hearing her, not the student. The watch
    // stays on through her acknowledgement (GAD-T496), so a student who carries on right after it is still noticed.
    if (Number.isFinite(ms) && ms >= 0 && ms <= CUT_OFF_MS && (s.speaking || s.fillPlaying || s.remarkHold)) return;
    s.cutWatch = null;
    if (!Number.isFinite(ms) || ms < 0 || ms > CUT_OFF_MS) return;
    s.cutOffBackoffMs = Math.min(CUT_OFF_BACKOFF_MS * 4, (s.cutOffBackoffMs || 0) + CUT_OFF_BACKOFF_MS);   // GAD-T498
    s.dotnet.invokeMethodAsync("OnAnswerCutOff", Math.round(ms)).catch(() => { });
}

function clearCue() {
    if (!S) return;
    cancelBackchannel(S, false);
    if (S.cueTimer) { clearTimeout(S.cueTimer); S.cueTimer = null; }
    const cue = q("cue");
    if (cue) cue.dataset.active = "false";
}

// ------------------------------------------------------------------------------------------------ reconnect buffering
// GAD-T360: a dropped speech hub connection (withAutomaticReconnect, above) would otherwise silently swallow every
// SendAudio/Commit call made while it reconnects (HubConnection.send rejects when not connected): the microphone
// keeps capturing, but the words said during the drop would never reach the recognizer at all. Instead, while the hub
// is down, every call is queued here and replayed, in order, once StartSession has reopened a session on the far side.
// The replayed session is a fresh one (the library has no way to resume the old one across a dropped connection: filed
// as a gap in MindAttic.SpeechRecognition), so what's buffered is replayed into it rather than appended to nothing.

/** The StartSession arguments for this room: the exam's own language and vocabulary hints (GAD-T357, GAD-T358), never the browser's choosing. */
function startSessionArgs(s) {
    return { language: (s.cfg && s.cfg.language) || "en", diarize: true, keyterms: (s.cfg && s.cfg.keyterms) || [] };
}

/** Longest the reconnect buffer may hold (about 30 s of 100 ms mic chunks): a very long outage drops the oldest audio rather than growing without bound. */
const REPLAY_QUEUE_MAX = 300;

function queueOrSend(s, invoke) {
    if (s.hubUp && s.speechConn) { invoke().catch(() => { }); return; }
    s.replayQueue.push(invoke);
    while (s.replayQueue.length > REPLAY_QUEUE_MAX) s.replayQueue.shift();
}

function sendAudio(s, pcmBase64, captureStartMs) {
    queueOrSend(s, () => s.speechConn.send("SendAudio", pcmBase64, captureStartMs));
}

function sendCommit(s) {
    s.commitSentAt.push(performance.now());
    queueOrSend(s, () => s.speechConn.send("Commit"));
}

/** Replays whatever queued while the hub was down, in order, into the session StartSession just reopened. Best effort: one failed call doesn't stop the rest. */
async function replayQueueAsync(s) {
    const queued = s.replayQueue;
    s.replayQueue = [];
    for (const invoke of queued) { try { await invoke(); } catch { } }
}

// ------------------------------------------------------------------------------------------------------ stable partials
// GAD-T360: the recognizer sometimes revises a partial down (dropping a word it is no longer sure of) and then straight
// back up a moment later. Shown immediately, that reads as a flicker. Growth is always shown at once (that's the
// reassuring, "it's keeping up" case); a shrink is held for a short beat in case it's about to regrow, so a momentary
// dip never reaches the screen.
const PARTIAL_SHRINK_HOLD_MS = 250;

function stablePartial(s, text) {
    s.partial = text;
    if (s.displayShrinkTimer) { clearTimeout(s.displayShrinkTimer); s.displayShrinkTimer = null; }
    if (text.length >= s.displayPartial.length || !text) { s.displayPartial = text; return; }
    const shrunk = text;
    s.displayShrinkTimer = setTimeout(() => {
        s.displayShrinkTimer = null;
        if (S === s && s.partial === shrunk) { s.displayPartial = shrunk; drawTranscript(); }
    }, PARTIAL_SHRINK_HOLD_MS);
}

// ------------------------------------------------------------------------------------------------- display normalization
// GAD-T360: numbers and punctuation spoken as words, shaped for the screen only. What is stored and graded is built from
// the recognizer's own words in VoiceAnswerBuilder (Gadfly.Server), never from this: a display tweak here can never
// change what the student is judged on.
const NUMBER_WORDS = {
    zero: 0, one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10,
    eleven: 11, twelve: 12, thirteen: 13, fourteen: 14, fifteen: 15, sixteen: 16, seventeen: 17, eighteen: 18, nineteen: 19,
    twenty: 20, thirty: 30, forty: 40, fifty: 50, sixty: 60, seventy: 70, eighty: 80, ninety: 90,
};
const MAGNITUDE_WORDS = { hundred: 100, thousand: 1000, million: 1000000 };
const PUNCTUATION_WORDS = { comma: ",", period: ".", "full stop": ".", "question mark": "?", "exclamation mark": "!", "exclamation point": "!", colon: ":", semicolon: ";" };

function normalizeForDisplay(text) {
    const trimmed = (text || "").trim();
    if (!trimmed || trimmed.length > 20000) return trimmed;
    const words = trimmed.split(/\s+/);
    const out = [];
    for (let i = 0; i < words.length; i++) {
        const two = i + 1 < words.length ? (words[i] + " " + words[i + 1]).toLowerCase() : null;
        if (two && PUNCTUATION_WORDS[two]) { attach(out, PUNCTUATION_WORDS[two]); i++; continue; }
        const one = words[i].toLowerCase();
        if (PUNCTUATION_WORDS[one]) { attach(out, PUNCTUATION_WORDS[one]); continue; }
        const read = readNumber(words, i);
        if (read.consumed > 0) { out.push(String(read.value)); i += read.consumed - 1; continue; }
        out.push(words[i]);
    }
    let joined = out.join(" ").replace(/ ([,.?!:;])/g, "$1");
    if (joined.length === 0) return joined;
    if (!/[.?!]\s*$/.test(joined)) joined += ".";
    return joined.charAt(0).toUpperCase() + joined.slice(1);
}

function attach(out, mark) {
    if (out.length === 0) out.push(mark); else out[out.length - 1] += mark;
}

function readNumber(words, start) {
    let i = start, total = 0, current = 0, any = false;
    while (i < words.length) {
        const w = words[i].toLowerCase();
        if (w === "and" && any && i + 1 < words.length && isNumberWord(words[i + 1])) { i++; continue; }
        if (Object.prototype.hasOwnProperty.call(NUMBER_WORDS, w)) { current += NUMBER_WORDS[w]; any = true; i++; }
        else if (Object.prototype.hasOwnProperty.call(MAGNITUDE_WORDS, w)) {
            const mag = MAGNITUDE_WORDS[w];
            current = (current === 0 ? 1 : current) * mag;
            if (mag >= 1000) { total += current; current = 0; }
            any = true; i++;
        } else break;
    }
    return any ? { value: total + current, consumed: i - start } : { value: 0, consumed: 0 };
}

function isNumberWord(w) {
    const lw = w.toLowerCase();
    return Object.prototype.hasOwnProperty.call(NUMBER_WORDS, lw) || Object.prototype.hasOwnProperty.call(MAGNITUDE_WORDS, lw);
}

function drawTranscript() {
    const s = S;
    if (!s) return;
    const said = s.segments.map((x) => x.text).join(" ");
    text("draft", normalizeForDisplay(said));
    text("live", normalizeForDisplay(s.displayPartial));
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
    S.latenciesMs = [];
    S.recovered = false;
    S.kept = null;   // an answer waiting to be sent again (GAD-T473) was cleared by the student
    S.carryFinals = false;   // nor do words said before a repeated question join it any more (GAD-T474)
    text("recovered", "");
    VoiceDraft.clear(S.sessionId);
    drawTranscript();
    status("Cleared. Answer again whenever you're ready.");
}

async function submit(immediate, resend) {
    const s = S;
    if (!s || s.gate || s.segments.length === 0) return;
    clearCue();
    let acoustics = null;
    try { acoustics = s.acoustics.summary(); } catch { }
    const payload = {
        segments: s.segments, listeningStartMs: s.listeningStartMs, startedAtUnixMs: s.startedAtUnixMs,
        responseDelayMs: s.delayMs == null ? null : Math.round(s.delayMs), acoustics,
        // Recognizer latency (GAD-T361): commit-to-final, timed here since the hub pushes the final segment rather than
        // returning it from a call the server could time itself. Diagnostics only: never shown to the student, never graded.
        recognizerLatenciesMs: s.latenciesMs.slice(),
        // The question it answers, as asked here (GAD-T321): an answer kept and sent again later (GAD-T473) can only ever be taken for it.
        turn: s.turn,
    };
    s.gate = true;
    talking(s, false);
    // She stops listening once the answer is in (GAD-T316): left "listening" until the page's reply, the eye-contact check kept
    // treating the student as answering, and could fade the screen or start a spoken remark after the answer was already sent.
    if (s.avatar) s.avatar.state = "thinking";
    status("Sending your answer…");
    // Sent by the silence, not by "Done answering": watch the next few seconds for the student going on (GAD-T462).
    s.cutWatch = immediate ? null : performance.now();
    // She acknowledges the answer at once (GAD-T496), not after the round trip; a kept answer sent again was acknowledged already.
    if (!resend) sayAck(s);
    s.sending = true;
    try {
        let taken;
        try { taken = await s.dotnet.invokeMethodAsync("OnVoiceAnswer", payload); }
        finally { s.sending = false; }
        if (S !== s) return;
        if (taken === false) { keepAnswer(s, payload); return; }
        s.kept = null;
        // Taken: the answer is the page's now, so the words, the draft and the "what I heard" box are cleared. A question asked
        // meanwhile (the answer's step already moved on) cleared them itself, so only this answer's own words are touched.
        if (s.turn === payload.turn && s.segments === payload.segments) {
            s.segments = []; s.partial = ""; s.startedAtUnixMs = null; s.acoustics.reset(); s.latenciesMs = [];
            s.recovered = false; text("recovered", "");
            VoiceDraft.clear(s.sessionId);   // sent: the draft kept in case of a reload (GAD-T306) is no longer needed
            drawTranscript();
        }
    } catch {
        // The answer is still held here: let the student try again.
        // Only while this answer is still wanted (GAD-T316): a question being asked or the room stopped meanwhile must not have the
        // microphone reopened under it.
        if (S === s) {
            s.cutWatch = null;
            // Her acknowledgement (GAD-T496) is still playing: its end opens the microphone again for this answer.
            if (s.ackPlaying && s.allowed) { s.ackDeferredAllowed = true; status("That did not go through. Press Done answering to try again."); }
            else if (s.running && s.allowed && !s.speaking && !s.speakToken && !s.fillPlaying && !s.remarkHold) {
                s.gate = false;
                if (s.avatar) s.avatar.state = "listening";
                status("That did not go through. Press Done answering to try again.");
            }
        }
    }
}

// ------------------------------------------------------------------------------------------------- an answer kept (GAD-T473)
// The page can turn a spoken answer away without it being wrong: it arrived while another step was running (the page reloading
// its state after a dropped connection, "I don't know" or Finish being handled), during a staff pause, or the server refused it
// for the moment. Until GAD-T473 the browser cleared it anyway and the words were gone without a word to the student. Now the
// words stay in "What I heard" and in the reload draft, and are sent again, for the same question only, as soon as an answer is
// wanted again. If the exam moves to another question first, they are dropped with a plain note, never sent as its answer.

/** How long after a turned-away answer the page is tried again when it already says an answer is wanted, and how many times. */
const KEPT_RETRY_MS = 1500;
const KEPT_MAX_TRIES = 3;

const KEPT_TEXT = "Your answer is kept. It will be sent as soon as the examiner is ready.";
const KEPT_GIVE_UP_TEXT = "Your answer couldn't be sent yet, but it's still here. Press Done answering to send it, or keep talking to add more.";
const DROPPED_TEXT = "Your last answer came in after the exam had already moved on to this question, so it wasn't recorded. Answer this question whenever you're ready.";

/** The page did not take the answer just sent: keep its words (and draft) for the same question and say so. */
function keepAnswer(s, payload) {
    s.cutWatch = null;   // nothing was recorded for a cut-off note to belong to
    if (s.turn !== payload.turn) {
        // Another question was asked while it was on its way: never its answer.
        if (payload.segments.length > 0) text("recovered", DROPPED_TEXT);
        return;
    }
    if (s.segments !== payload.segments && payload.segments.length > 0) {
        // The same question was asked again while it was on its way (Repeat the question), which cleared the box: put them back,
        // ahead of anything said since.
        s.segments = payload.segments.concat(s.segments);
        VoiceDraft.save(s.sessionId, s.turn, s.segments);
    }
    const tries = (s.kept && s.kept.turn === s.turn ? s.kept.tries : 0) + 1;
    s.kept = { turn: s.turn, tries };
    drawTranscript();
    status(s.held ? HELD_TEXT : KEPT_TEXT);
    // The page's "not now" and its next "go ahead" (setAllowed) can arrive in either order: if it already wants an answer, try again
    // shortly; otherwise its next "go ahead" sends it.
    if (s.allowed) setTimeout(() => { if (S === s && s.allowed) resendKept(s); }, KEPT_RETRY_MS);
}

/** Sends a kept answer again, once an answer for its own question is wanted and nothing else is going on. */
function resendKept(s) {
    const kept = s.kept;
    if (!kept || S !== s || !s.running || s.sending || !s.allowed || s.speaking || s.speakToken || s.fillPlaying || s.remarkHold) return;
    if (kept.turn !== s.turn || s.segments.length === 0) { s.kept = null; return; }
    if (s.gate) openMic();
    if (kept.tries >= KEPT_MAX_TRIES) {
        // Turned away a few times running: stop sending it on our own and let the student send it (or add to it) themselves.
        s.kept = null;
        if (!s.gate) status(KEPT_GIVE_UP_TEXT);
        return;
    }
    void submit(true, true);
}

function toggleMode(button) {
    const s = S;
    if (!s || !s.mic) return;
    s.mode = s.mode === "hands-free" ? "push-to-talk" : "hands-free";
    s.mic.setMode(s.mode);
    button.textContent = s.mode === "hands-free" ? "Switch to hold-to-talk" : "Switch to hands-free";
    document.querySelectorAll('[data-voice-action="ptt"]').forEach((b) => { b.hidden = s.mode !== "push-to-talk"; });
    if (!s.gate) status(listenText(s));
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

/** The whole caption at once (no delivery helpers loaded); word-by-word captions are examiner-voice.js. */
function showCaption(what) {
    const el = q("caption");
    if (!el) return;
    el.dataset.timed = "false";
    el.textContent = what || "";
}
