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
        presenter: null, utterId: null, ours: null,   // the delivery helpers (GAD-T303); the hub id and record of this room's own utterance
        selfStream: null, waitStartMs: null, delayMs: null, delayTimer: null, face: null,
        pauses: [], lastSpeechEndAt: null,   // the student's own mid-answer pauses, which the end-of-answer cue adapts to (GAD-T264)
        fill: null, fillPlaying: null,       // filling the wait while an answer is judged (GAD-T290): pending lines, the one being said
        fillAmbience: null,                  // the quiet page-turn loop under the wait (GAD-T409)
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
            onChunk: (c) => {
                if (s.gate || !s.speechConn) return;
                try { s.acoustics.push(c.pcm); } catch { }
                s.speechConn.send("SendAudio", s.pcm16ToBase64(c.pcm), c.captureStartMs).catch(() => { });
            },
            onSpeechStart: () => {
                s.inSpeech = true;
                if (s.gate) return;
                if (s.lastSpeechEndAt !== null && s.startedAtUnixMs !== null) notePause(s, performance.now() - s.lastSpeechEndAt);
                s.lastSpeechEndAt = null;
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
                s.speechConn.send("Commit").catch(() => { });
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
        s.speechConn =new signalR.HubConnectionBuilder().withUrl(SPEECH_HUB).withAutomaticReconnect([0, 1000, 3000, 6000]).build();
        s.speechConn.on("partial", (t) => {
            s.partial = t || "";
            // Words still arriving mean the answer is not over (GAD-T286): a final that armed the end-of-answer cue must not submit
            // while the recognizer is still hearing more, e.g. a provider-side commit in the middle of one unbroken stretch of speech.
            if (s.partial.trim() && !s.gate && s.cueTimer) { clearCue(); status("Hearing you…"); }
            drawTranscript();
        });
        s.speechConn.on("final", onFinal);
        s.speechConn.on("problem", (kind, message, fatal) => { if (fatal) problem("recognizer", message, true); });
        s.speechConn.onreconnected(async () => {
            try { await s.speechConn.invoke("StartSession", { language: "en", diarize: true }); }
            catch { problem("hub", "The connection to speech recognition was lost.", true); }
        });
        s.speechConn.onclose(() => { if (s.running) problem("hub", "The connection to speech recognition was lost.", true); });
        await s.speechConn.start();
        await s.speechConn.invoke("StartSession", { language: "en", diarize: true });
        }
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
        el.addEventListener("speaking-start", () => {
            if (!s.remarkHold && s.speakToken) s.speaking = true;
            // This room's own utterance has started playing (not a gaze remark, which speaks through another connection): the
            // caption and mouth clock starts with her audio, so the words light as she says them.
            if (s.ours && !s.ours.started && !s.remarkHold) { s.ours.started = true; s.presenter && s.presenter.started(); }
        });
        el.addEventListener("speech-error", (e) => { if (s.ours && e.detail && e.detail.id === s.utterId) s.ours.failed = true; });
        stage.appendChild(el);
        s.avatar = el;
        s.presenter = (await import("./examiner-voice.js")).createPresenter(el, stage, q("caption"), { portraitMouth: s.cfg.portraitMouth },
            (reason) => s.dotnet.invokeMethodAsync("OnAvatarMode", "portrait", reason).catch(() => { }));
        s.avatarConn = new signalR.HubConnectionBuilder().withUrl(AVATAR_HUB).build();
        s.speech = pkg.attachSpeechHub(el, s.avatarConn);
        s.avatarConn.on("speechStart", (m) => { s.utterId = m && m.id; });
        s.avatarConn.on("speechChunk", (m) => { if (m && m.id === s.utterId && s.ours) s.presenter && s.presenter.words(m.words); });
        await s.avatarConn.start();
        await Promise.race([el.ready, new Promise((r) => setTimeout(r, 90000))]);
        if (S !== s || !s.running) return;
        s.avatarOk = true;
        void startFaceFollow(s);   // the self-view may already be on: the examiner looks toward the student's face
        s.dotnet.invokeMethodAsync("OnAvatarMode", el.mode || "3d", "").catch(() => { });
    } catch (err) {
        if (S !== s || !s.running) return;
        s.avatarOk = false;
        // Without the talking head the questions are still read out by the browser's own speech, with the caption lit word by word.
        try { s.presenter?.dispose(); } catch { }
        try { s.presenter = (await import("./examiner-voice.js")).createPresenter(null, null, q("caption"), {}, () => { }); } catch { s.presenter = null; }
        s.dotnet.invokeMethodAsync("OnAvatarMode", "none", "The examiner's picture could not start; questions are read out by your browser and shown as text.").catch(() => { });
    }

    s.gate = true;
    status("The examiner is ready.");
    await s.dotnet.invokeMethodAsync("OnVoiceStarted", s.mic ? (s.mic.vadKind || "energy") : "typed");
}

function fail(kind, message, s) {
    problem(kind, message, true);
    stop();
}

/**
 * The examiner says <paramref name="what"/> (the server only allows texts it offered for this student), in the speaking style the
 * server picked for this kind of question (GAD-T303: neutral, open or probe; never a hint of the grade).
 */
export function speak(what, cacheable, style) {
    const s = S;
    if (!s || !s.running) return;
    cancelFill(s);   // the next question is here: no more filler for this wait (GAD-T290)
    clearCue();
    s.gate = true;
    s.remarkCommitPending = false;   // a remark's late final belongs to the last answer, not to this question's
    s.segments = [];
    s.partial = "";
    s.startedAtUnixMs = null;
    s.acoustics.reset();
    resetDelay(s);
    drawTranscript();
    status("The examiner is speaking…");
    const token = s.speakToken = {};
    // Only the newest utterance finishes the turn: one replaced by a later speak() (Repeat the question) must not open the microphone.
    const done = () => {
        if (S !== s || s.speakToken !== token) return;
        s.speakToken = null;
        s.speaking = false;
        s.ours = null;
        if (s.allowed) openMic(); else status("Waiting.");
    };
    s.speaking = true;
    // A filler line still being said (GAD-T290) finishes first, so the two never overlap and its end isn't taken for this one's.
    // The line is short and stops itself if it stalls; the extra bound here is only a backstop.
    const filler = s.fillPlaying;
    const ready = filler ? Promise.race([filler, new Promise((r) => setTimeout(r, FILL.timeoutMs + 1000))]) : Promise.resolve();
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
        if (!ours.failed) { s.presenter && s.presenter.ended(); return; }
        viaBrowser = true;
    }
    if (!mine()) return;
    const spoke = s.presenter ? await s.presenter.speakWithBrowser(what) : false;
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
const FILL = { timeoutMs: 6000, retryMs: 500, gapMs: 8000, lateMs: 10000 };

/**
 * The panel is judging the answer, taken as heard (GAD-T405), and writing the next question (GAD-T290). If the wait outlasts
 * firstAfterMs the examiner says <first> (a short neutral line the server chose and offered), and on a long wait <again> at
 * againAfterMs. Nothing is said while the microphone is open, a question or remark is being spoken, or another line is still
 * playing: the attempt waits for that and is dropped once its moment has passed. The microphone gate stays closed
 * the whole time, so a line is never captured or transcribed into an answer, and the next question (speak()) cancels what is left.
 * A quiet page-turn loop (GAD-T409) runs alongside, under the filler line's own volume, so the wait never feels silent.
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
        if (!canFill(s) || (f.saidAt !== null && now - f.saidAt < FILL.gapMs)) {
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

/** Page-turn/book-rustle ambience under the wait (GAD-T409), a CC0 clip (GAD-T408) alternated so a whole exam never repeats
 * the same one twice running. A plain looping <audio>, well under the filler line's own volume, faded in and out so it is
 * never an abrupt start or cut and is always gone before the examiner's next question can be heard. */
const FILL_AMBIENCE = {
    sources: ["/sounds/page-turn-1.mp3", "/sounds/page-turn-2.mp3"],
    gain: 0.12, fadeInMs: 200, fadeOutMs: 300,
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

/** Ramps an <audio> element's volume toward target over ms, then calls done (shared by the wait ambience's fade in/out). */
function fadeVolume(audio, target, ms, done) {
    const from = audio.volume, startedAt = performance.now();
    const step = () => {
        const t = Math.min(1, (performance.now() - startedAt) / ms);
        try { audio.volume = from + (target - from) * t; } catch { return; }
        if (t < 1) requestAnimationFrame(step);
        else if (done) done();
    };
    requestAnimationFrame(step);
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
            if (s.allowed) { if (s.gate) openMic(); }
            else { if (s.avatar) s.avatar.state = "thinking"; status("Thinking about your answer…"); }
        });
    s.fillPlaying = playing;
}

/** Barge-in: the student starts talking over a question, or leaves. */
export function stopSpeaking() {
    if (S && S.speech) S.speech.stop().catch(() => { });
    if (S && S.presenter) S.presenter.stopBrowser();
}

/** Whether an answer may be given right now (a question or a clarification is waiting, nothing is being judged). */
export function setAllowed(allowed) {
    const s = S;
    if (!s) return;
    const was = s.allowed;
    s.allowed = !!allowed;
    if (s.allowed) cancelFill(s);
    // A filler line still playing (GAD-T290) keeps the microphone shut until it ends; its end opens it if an answer is wanted.
    if (!s.running || s.speaking || s.fillPlaying) return;
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
    cancelFill(s);
    s.fillPlaying = null;
    s.speakToken = null;
    s.ours = null;
    s.speaking = false;
    clearCue();
    clearCaptions();
    try { s.presenter && s.presenter.dispose(); } catch { }
    s.presenter = null;
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
    // Typed answers (GAD-T260): the answer box is the exam room's; the examiner listens (the gaze check follows that state).
    if (s && s.cfg.typed && s.running) {
        s.gate = false;
        if (s.avatar) s.avatar.state = "listening";
        status(listenText(s));
        return;
    }
    if (!s || !s.mic || !s.speechConn) return;
    s.gate = false;
    s.lastSpeechEndAt = null;   // the wait before an answer starts is thinking time, not a pause within it
    s.listeningStartMs = s.mic.currentTimeMs;
    startDelay(s);
    s.speechConn.send("MarkListening", s.listeningStartMs).catch(() => { });
    if (s.avatar) s.avatar.state = "listening";
    status(listenText(s));
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

/** What the status line says while an answer is open. */
function listenText(s) {
    if (s.cfg.typed) return "Type your answer in the box below.";
    return s.mode === "push-to-talk" ? "Hold the button and speak." : "Listening. Answer out loud.";
}

function onFinal(seg) {
    const s = S;
    if (!s) return;
    const remarkFinal = s.remarkCommitPending;   // the next final answers the remark's commit, whatever the gate says
    s.remarkCommitPending = false;
    if ((s.gate && !remarkFinal) || !seg || !seg.text || !seg.text.trim()) return;
    s.partial = "";
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
}

/**
 * The end-of-answer silence for this student (GAD-T264). It starts at the configured floor (lengthened on the server by an
 * extra-time accommodation) and adapts to the student's own pattern: once a few pauses within answers have been seen (silence
 * after which the student went on speaking), it becomes half again the student's longer pauses (the 80th percentile of the
 * last 20), never below the floor and never above the ceiling. It only ever lengthens the cue: a quick speaker keeps the floor.
 */
function endCueMs(s) {
    const floor = s.cfg.endCueMs || 4000;
    const ceiling = Math.max(floor, s.cfg.endCueMaxMs || floor);
    if (s.pauses.length < PAUSE.minSamples) return floor;
    const sorted = [...s.pauses].sort((a, b) => a - b);
    const p80 = sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * 0.8))];
    return Math.round(Math.min(ceiling, Math.max(floor, p80 * PAUSE.factor)));
}

const PAUSE = { minMs: 300, minSamples: 3, keep: 20, factor: 1.5 };

/** Records one pause within an answer (speech ended, then the student went on). Too short to be a pause is ignored. */
function notePause(s, ms) {
    if (!Number.isFinite(ms) || ms < PAUSE.minMs) return;
    const ceiling = Math.max(s.cfg.endCueMs || 4000, s.cfg.endCueMaxMs || 0);
    s.pauses.push(Math.min(ms, ceiling));
    if (s.pauses.length > PAUSE.keep) s.pauses.shift();
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
