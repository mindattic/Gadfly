// Eye contact with the examiner in the voice interview room (GAD-T240). While the talking examiner is on screen, the camera is
// analysed on this device by MindAttic.Webcam (the self-hosted build in wwwroot/webcam, no CDN): presence (no face, a second
// face), head-and-eye direction against a baseline learned while the student looks at the examiner's face (calibration on the
// avatar's on-screen position), and the reading-pattern detector. A live indicator says whether the student is looking at the
// examiner. While the student is answering (the examiner's avatar is listening):
//   - looking away for GAZE.fadeStartMs starts fading the exam screen toward a heavy dim and blur over GAZE.fadeRampMs, with a
//     warning to look back at the examiner; it restores smoothly as soon as the gaze returns;
//   - a longer look away (GAZE.remarkAfterMs), a reading pattern, no face or a second face makes the examiner say so out loud
//     ("Please look at me while you answer"), at most once every GAZE.remarkGapMs;
//   - each episode (look-away, reading, face absent, second face, and each fade with its peak) is sent to the server with its
//     start and end, for review. Only those notes leave the device: never a frame, an image, a landmark or a face signature.
// Nothing here infers emotion or deception, and nothing blocks the answer. If the camera or the face model can't start, the
// indicator says eye contact isn't being checked and the interview goes on.
//
// This module only reads the voice room's DOM (the <ma-avatar> in [data-voice-stage] and its data-state) and speaks through
// its own avatar hub connection, so it does not depend on voice-room.js internals.

/** Every threshold of the eye-contact check, in one place. */
export const GAZE = Object.freeze({
    /** Calibration: the prompt shows this long before the baseline is learned, so the student has time to look at the examiner. */
    calibrationDelayMs: 1200,
    /** Frames averaged into the baseline (about 1.5 s at 30 fps). */
    baselineFrames: 45,
    /** The examiner's face region in degrees, from the avatar's on-screen size seen from this distance, plus slack for the estimate's error. */
    viewingDistanceCm: 60,
    slackDeg: 5,
    minHalfWidthDeg: 7, maxHalfWidthDeg: 16,
    minHalfHeightDeg: 6, maxHalfHeightDeg: 12,
    /** A look away must hold this long before the region changes (blinks and jitter don't count). */
    switchMs: 200,
    /** The screen starts to fade after this much continuous look-away while answering (also when a look-away episode starts)... */
    fadeStartMs: 2000,
    /** ...and reaches its heaviest dim and blur this much later. */
    fadeRampMs: 3000,
    /** The heaviest fade: how dark (0..1) and how blurred (px). */
    fadeMaxDim: 0.85,
    fadeMaxBlurPx: 12,
    /** How long the screen takes to restore once the gaze is back. */
    fadeRestoreMs: 600,
    /** A look away this long makes the examiner say "Please look at me while you answer". */
    remarkAfterMs: 5000,
    /** The examiner speaks up at most once in this long. */
    remarkGapMs: 15000,
    /** The longest a spoken remark may hold things up if synthesis stalls. */
    remarkTimeoutMs: 12000,
    /** No face / a second face counts after this long (presence module). */
    faceAbsentMs: 2000,
    multipleFacesMs: 1000,
    /** No head sample for this long means the gaze is unknown (the face was lost), not "away". */
    sampleStaleMs: 800,
    /** How often episodes and the fade are updated. */
    tickMs: 100,
    /** How often the module looks for the examiner (voice mode turned on or off). */
    watchMs: 1000,
});

const LIB = "/lib/voice";
const AVATAR_HUB = "/hubs/avatar";
const KINDS = ["look-away", "reading", "face-absent", "second-face"];

let G = null;

const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
const iso = (ms) => new Date(ms).toISOString();
/** A performance.now() time as Unix ms. */
const epochOf = (perf) => Date.now() - (performance.now() - perf);

const TEXT = {
    off: "",
    loading: "Starting the eye-contact check…",
    calibrating: "Look at the examiner's face for a moment, so the camera learns where it is.",
    on: "Looking at the examiner",
    away: "Looking away from the examiner. Please look at the examiner.",
    reading: "Your eyes are moving as if reading. Please look at the examiner.",
    "no-face": "Your face isn't in view of the camera.",
    "two-faces": "More than one face is in view of the camera.",
    unavailable: "Eye contact isn't being checked: the camera check couldn't start on this device. The exam carries on.",
};

/** Starts watching for the examiner; the camera is used only while the examiner is on screen. */
export function start(dotnet, options) {
    stop();
    const g = {
        dotnet, options: options || {}, stopped: false, running: false, starting: false, failed: false,
        core: null, presence: null, headpose: null, reading: null, offs: [],
        avatar: null, faces: 1, calibrating: false, calibTimer: null,
        awayPerf: null, lastSamplePerf: 0, cond: { reading: null, "face-absent": null, "second-face": null },
        answering: false, answeringSince: 0, episodes: {}, fade: null, level: 0,
        lastRemark: 0, remarking: false, hub: null, speech: null, hubAvatar: null, shown: null,
    };
    G = g;
    g.onClick = (e) => {
        const el = e.target.closest && e.target.closest("[data-examiner-gaze-action]");
        if (el && G === g && el.dataset.examinerGazeAction === "calibrate") calibrate(g);
    };
    document.addEventListener("click", g.onClick);
    g.watch = setInterval(() => watch(g), GAZE.watchMs);
    g.tick = setInterval(() => tick(g), GAZE.tickMs);
    window.addEventListener("pagehide", g.onHide = () => stop(), { once: true });
    watch(g);
}

/** Closes open episodes (each gets an end), sends them, and releases the camera. */
export function stop() {
    const g = G;
    if (!g) return;
    G = null;
    g.stopped = true;
    clearInterval(g.watch);
    clearInterval(g.tick);
    document.removeEventListener("click", g.onClick);
    if (g.onHide) window.removeEventListener("pagehide", g.onHide);
    pause(g);
    try { g.speech && g.speech.dispose(); } catch { }
    try { g.hub && g.hub.stop(); } catch { }
    g.speech = g.hub = null;
}

// ------------------------------------------------------------------------------------------------------------ lifecycle

function stageAvatar() {
    const stage = document.querySelector("[data-voice-stage]");
    return stage ? stage.querySelector("ma-avatar") : null;
}

/** Runs the camera while the examiner is on screen, and releases it when voice mode goes away. */
function watch(g) {
    if (g.stopped) return;
    const avatar = stageAvatar();
    if (avatar && !g.running && !g.starting && !g.failed) void resume(g, avatar);
    else if (!avatar && (g.running || g.starting)) { pause(g); show(g, "off"); }
    else if (avatar && g.running) g.avatar = avatar;
}

async function resume(g, avatar) {
    // Only the latest resume may commit: a pause and a new resume during this one's awaits make it stale (GAD-T240).
    const gen = g.resumeGen = (g.resumeGen || 0) + 1;
    const live = () => !g.stopped && g.starting && g.resumeGen === gen;
    let core = null;
    g.starting = true;
    g.avatar = avatar;
    show(g, "loading");
    const base = new URL("../" + (g.options.basePath || "webcam/"), import.meta.url);
    try {
        const lib = await import(new URL("webcam.browser.js", base).href);
        if (!live()) return;
        const limits = examinerLimits(lib);
        core = g.core = new lib.WebcamCore({
            numFaces: 2, width: 640, height: 480,
            wasmBaseUrl: new URL(".", base).href.replace(/\/$/, ""),
            modelUrl: new URL("face_landmarker.bin", base).href,
        });
        g.presence = new lib.PresenceMonitor({ absentMs: GAZE.faceAbsentMs, multipleMs: GAZE.multipleFacesMs });
        g.headpose = new lib.HeadPoseMonitor({
            ...limits, baselineFrames: GAZE.baselineFrames, switchMs: GAZE.switchMs,
            // Its own off-screen events are not used here: episodes are decided below, only while the student answers.
            thinkingMs: 1e9, sustainedMs: 1e9,
        });
        g.reading = new lib.ReadingPatternDetector();
        g.headpose.on("sample", (s) => onSample(g, s));
        g.reading.on("begin", (b) => { g.cond.reading = b.start; });
        g.reading.on("event", () => { g.cond.reading = null; });
        g.presence.on("status", (st) => { g.faces = st.faces; if (st.faces !== 1) g.awayPerf = null; });
        g.presence.on("begin", (b) => {
            if (b.type === "face-absent") g.cond["face-absent"] = b.start;
            else if (b.type === "multiple-faces") g.cond["second-face"] = b.start;
        });
        g.presence.on("event", (e) => {
            if (e.type === "face-absent") g.cond["face-absent"] = null;
            else if (e.type === "multiple-faces") g.cond["second-face"] = null;
        });
        g.offs.push(g.presence.connect(g.core), g.headpose.connect(g.core));
        const unshare = await shareSelfCamera(g);
        try { await core.retain(); } finally { unshare(); }
        // Superseded meanwhile: pause() already released this core (g.core may now be a newer resume's), but retain may have
        // opened the camera after that, so stop this one's own core.
        if (!live()) { try { core.stop(); } catch { } return; }
        g.running = true;
        calibrate(g);
    } catch {
        if (!live()) { try { core && core.stop(); } catch { } return; }
        releaseCore(g);
        // Denied, missing or no face model: say so once and don't keep asking for the camera.
        g.failed = true;
        show(g, "unavailable");
    } finally {
        if (g.resumeGen === gen) g.starting = false;
    }
}

/**
 * Uses the camera the voice room already opened for the student's self-view instead of asking for a second stream (GAD-T245):
 * the core's one getUserMedia call (until the returned function restores it) is answered with a clone of that stream's video track. The clone is this module's own, so
 * stopping the core never stops the self-view. If the self-view isn't on after a short wait, the core opens the camera itself.
 */
async function shareSelfCamera(g) {
    const live = () => {
        const v = document.querySelector("video[data-voice-self]");
        const st = v && v.srcObject;
        return st && st.getVideoTracks && st.getVideoTracks().some((t) => t.readyState === "live") ? st : null;
    };
    let stream = live();
    for (let i = 0; !stream && i < 30 && !g.stopped && g.starting; i++) {
        await new Promise((r) => setTimeout(r, 100));
        stream = live();
    }
    const md = navigator.mediaDevices;
    if (!stream || !md || !md.getUserMedia) return () => { };
    const original = md.getUserMedia;
    const restore = () => { if (md.getUserMedia !== original) { try { delete md.getUserMedia; } catch { } if (md.getUserMedia !== original) md.getUserMedia = original; } };
    md.getUserMedia = function (constraints) {
        restore();
        if (constraints && constraints.video && !constraints.audio) {
            const src = live() || stream;
            return Promise.resolve(new MediaStream(src.getVideoTracks().filter((t) => t.readyState === "live").map((t) => t.clone())));
        }
        return original.call(md, constraints);
    };
    return restore;   // the caller restores it once the core has opened its camera
}

/** Stops the camera; closes and sends every open episode first. */
function pause(g) {
    g.starting = false;
    g.answering = false;
    for (const kind of Object.keys(g.episodes)) closeEpisode(g, kind);
    closeFade(g);
    applyFade(g, 0);
    setCalibrating(g, false);
    releaseCore(g);
    g.running = false;
    g.awayPerf = null;
    g.cond = { reading: null, "face-absent": null, "second-face": null };
}

function releaseCore(g) {
    try { g.offs.forEach((off) => off && off()); } catch { }
    g.offs = [];
    try { g.core && g.core.stop(); } catch { }
    g.core = g.presence = g.headpose = g.reading = null;
}

/**
 * The examiner's face region, in degrees either side of the baseline: the avatar's on-screen size (CSS px taken as 1/96 inch)
 * seen from GAZE.viewingDistanceCm, plus slack, kept within sane bounds.
 */
function examinerLimits(lib) {
    const stage = document.querySelector("[data-voice-stage]");
    const r = stage && stage.getBoundingClientRect();
    if (!r || !r.width || !r.height) return { halfWidthDeg: 10, halfHeightDeg: 8 };
    const cm = (px) => (px / 96) * 2.54;
    const l = lib.regionLimitsFor(cm(r.width), cm(r.height), GAZE.viewingDistanceCm, GAZE.slackDeg);
    return {
        halfWidthDeg: clamp(l.halfWidthDeg, GAZE.minHalfWidthDeg, GAZE.maxHalfWidthDeg),
        halfHeightDeg: clamp(l.halfHeightDeg, GAZE.minHalfHeightDeg, GAZE.maxHalfHeightDeg),
    };
}

// ---------------------------------------------------------------------------------------------------------- calibration

/** Asks the student to look at the examiner's face, then learns that direction as "looking at the examiner". */
function calibrate(g) {
    if (!g.running || !g.headpose) return;
    setCalibrating(g, true);
    clearTimeout(g.calibTimer);
    g.calibTimer = setTimeout(() => {
        if (!g.running || !g.headpose) return;
        import(new URL("../" + (g.options.basePath || "webcam/") + "webcam.browser.js", import.meta.url).href)
            .then((lib) => { if (g.headpose) Object.assign(g.headpose.options, examinerLimits(lib)); })
            .catch(() => { })
            .finally(() => { if (g.headpose) g.headpose.recalibrate(); g.awayPerf = null; g.calibArmed = true; });
    }, GAZE.calibrationDelayMs);
}

function setCalibrating(g, on) {
    g.calibrating = on;
    g.calibArmed = false;
    if (!on) clearTimeout(g.calibTimer);
    const stage = document.querySelector("[data-voice-stage]");
    if (stage) { if (on) stage.setAttribute("data-gaze-calibrating", ""); else stage.removeAttribute("data-gaze-calibrating"); }
}

// --------------------------------------------------------------------------------------------------------- per frame

function onSample(g, s) {
    if (g.reading) g.reading.push(s);
    g.lastSamplePerf = s.t;
    if (g.calibrating) {
        // Done once the baseline learned after the prompt (not one from before it) is ready.
        if (s.ready && g.calibArmed && g.headpose && g.headpose.baselineReady) setCalibrating(g, false);
        return;
    }
    if (s.ready && s.region !== "on-screen" && g.faces === 1) { if (g.awayPerf == null) g.awayPerf = s.t - s.dwellMs; }
    else g.awayPerf = null;
}

/** Answering means the examiner is listening (voice-room.js sets the avatar to "listening" while the microphone is open). */
function isAnswering(g) {
    const a = g.avatar;
    return !!a && a.isConnected && (g.remarking || (a.dataset && a.dataset.state === "listening"));
}

function tick(g) {
    if (g.stopped || !g.running) return;
    const now = performance.now();
    if (g.awayPerf != null && now - g.lastSamplePerf > GAZE.sampleStaleMs) g.awayPerf = null;
    const answering = !g.calibrating && isAnswering(g);
    if (answering && !g.answering) g.answeringSince = Date.now();
    g.answering = answering;
    const awayMs = g.awayPerf == null ? 0 : now - g.awayPerf;

    // Episodes, only while answering.
    const active = {
        "look-away": awayMs >= GAZE.fadeStartMs ? epochOf(g.awayPerf) : null,
        reading: g.cond.reading,
        "face-absent": g.cond["face-absent"],
        "second-face": g.cond["second-face"],
    };
    for (const kind of KINDS) {
        const since = answering ? active[kind] : null;
        const ep = g.episodes[kind];
        if (since != null && !ep) g.episodes[kind] = { start: Math.max(since, g.answeringSince), remarked: false };
        else if (since == null && ep) closeEpisode(g, kind);
    }
    if (answering) {
        for (const kind of KINDS) {
            const ep = g.episodes[kind];
            if (!ep || ep.remarked) continue;
            if (kind === "look-away" && awayMs < GAZE.remarkAfterMs) continue;
            if (remark(g, kind)) ep.remarked = true;
            break;
        }
    }

    // The fade: from GAZE.fadeStartMs of continuous look-away, to the heaviest over GAZE.fadeRampMs.
    const level = answering && awayMs >= GAZE.fadeStartMs ? clamp((awayMs - GAZE.fadeStartMs) / GAZE.fadeRampMs, 0, 1) : 0;
    if (level > 0) {
        if (!g.fade) g.fade = { start: epochOf(g.awayPerf + GAZE.fadeStartMs), peak: 0 };
        g.fade.peak = Math.max(g.fade.peak, level);
    } else closeFade(g);
    applyFade(g, level);

    show(g, indicatorState(g, awayMs));
}

function indicatorState(g, awayMs) {
    if (g.calibrating) return "calibrating";
    if (g.faces === 0) return "no-face";
    if (g.faces > 1) return "two-faces";
    if (g.cond.reading != null) return "reading";
    if (awayMs > 0) return "away";
    return "on";
}

// ----------------------------------------------------------------------------------------------------------- episodes

function closeEpisode(g, kind) {
    const ep = g.episodes[kind];
    delete g.episodes[kind];
    if (!ep) return;
    send(g, { type: "examiner-" + kind, start: ep.start, end: Date.now(), summary: ep.remarked ? "remarked" : "" });
}

function closeFade(g) {
    const f = g.fade;
    g.fade = null;
    if (!f) return;
    send(g, { type: "examiner-fade", start: f.start, end: Date.now(), summary: "peak:" + Math.round(f.peak * 100) });
}

function send(g, e) {
    if (!(e.end >= e.start)) return;
    const ev = { source: "webcam", type: e.type, startedAt: iso(e.start), endedAt: iso(e.end), confidence: 0.4, summary: e.summary };
    g.dotnet.invokeMethodAsync("Record", [ev])
        .then((keep) => { if (keep === false && G === g) stop(); })
        .catch(() => { });
}

// ------------------------------------------------------------------------------------------------------------- remarks

/** The examiner asks out loud for eye contact. The server decides the words and offers them to the avatar hub. */
function remark(g, kind) {
    if (g.remarking || Date.now() - g.lastRemark < GAZE.remarkGapMs) return false;
    const avatar = g.avatar;
    if (!avatar || !avatar.isConnected || avatar.dataset.state !== "listening") return false;
    g.lastRemark = Date.now();
    g.remarking = true;
    void (async () => {
        let held = false;
        try {
            const what = await g.dotnet.invokeMethodAsync("Remark", kind);
            if (!what || G !== g || !avatar.isConnected) return;
            const speech = await avatarSpeech(g, avatar);
            if (!speech) return;
            // The voice room stops capturing the microphone while the examiner speaks, so the remark isn't transcribed into the answer.
            held = true;
            holdMic(true);
            let timedOut = false;
            await Promise.race([speech.speak(what, undefined, true), new Promise((r) => setTimeout(() => { timedOut = true; r(); }, GAZE.remarkTimeoutMs))]);
            // A stalled remark is cut off before the microphone reopens, so it can't start late and be recorded into the answer.
            if (timedOut) { try { speech.stop().catch(() => { }); } catch { } }
        } catch { /* never the student's problem */ }
        finally {
            g.remarking = false;
            if (held) holdMic(false);
            // Back to listening: the answer goes on (the avatar idles after speaking).
            if (avatar.isConnected && avatar.dataset.state === "idle") { try { avatar.state = "listening"; } catch { } }
        }
    })();
    return true;
}

/** Tells voice-room.js a remark is (or is no longer) being spoken: it closes and reopens its microphone gate (GAD-T245). */
function holdMic(active) {
    try { document.dispatchEvent(new CustomEvent("gadfly-examiner-remark", { detail: { active } })); } catch { }
}

/** This module's own connection to the avatar hub, attached to the examiner on screen. */
async function avatarSpeech(g, avatar) {
    if (g.speech && g.hubAvatar === avatar) return g.speech;
    try { g.speech && g.speech.dispose(); } catch { }
    g.speech = null;
    if (!window.signalR) return null;
    if (!g.hub) {
        // Kept only once it has started: a failed first start would otherwise leave a dead connection for every later remark.
        const hub = new window.signalR.HubConnectionBuilder().withUrl(AVATAR_HUB).withAutomaticReconnect().build();
        await hub.start();
        if (G !== g) { try { hub.stop(); } catch { } return null; }
        g.hub = hub;
    }
    const pkg = await import(`${LIB}/avatar/index.js`);
    g.speech = pkg.attachSpeechHub(avatar, g.hub);
    g.hubAvatar = avatar;
    return g.speech;
}

// ------------------------------------------------------------------------------------------------------------- display

function show(g, state) {
    if (g.shown === state) return;
    g.shown = state;
    const el = document.querySelector("[data-examiner-gaze]");
    if (!el) return;
    el.dataset.state = state;
    const label = el.querySelector("[data-examiner-gaze-label]");
    if (label) label.textContent = TEXT[state] ?? "";
    const again = el.querySelector("[data-examiner-gaze-action]");
    if (again) again.hidden = !(state === "on" || state === "away" || state === "reading");
}

function applyFade(g, level) {
    const el = document.querySelector("[data-examiner-fade]");
    if (!el) return;
    const rising = level >= g.level;
    g.level = level;
    el.style.setProperty("--fade-ms", (rising ? GAZE.tickMs : GAZE.fadeRestoreMs) + "ms");
    el.style.setProperty("--fade-dim", String(GAZE.fadeMaxDim));
    el.style.setProperty("--fade-blur", GAZE.fadeMaxBlurPx + "px");
    el.style.opacity = String(level);
    el.dataset.active = level > 0 ? "true" : "false";
}
