// Eye contact with the examiner in the voice interview room (GAD-T240, GAD-T305). While the talking examiner is on screen, the
// camera is analysed on this device by MindAttic.Webcam (the self-hosted build in wwwroot/webcam, no CDN): presence (no face, a
// second face), where the student looks, and the reading-pattern detector. A live indicator says whether the student is looking at
// the examiner.
//
// Where the student looks (GAD-T305). Two ways, the better one when it can be trusted:
//   - calibrated: a gaze vector from the iris position and the head pose (gaze-quality.js gazePoint), mapped onto the page by a
//     five-dot calibration (made on the get-ready page, or here with "Set up eye contact again"), and compared with the examiner's
//     stage as it is on screen right now, plus a margin in degrees that grows with the measured accuracy. Leaning, sitting back or
//     turning the head while still looking at her is not a look-away. The first look at her in the room checks the calibration
//     (off by more than GAZE.anchorMaxDeg and it isn't used); while she speaks, a steady small offset is corrected quietly (drift).
//     A look-away also needs the old head-and-eye direction to have strayed a little (GAZE.agreeShare), so one estimate's error
//     alone never makes one.
//   - head direction (the old behaviour, and the fallback when there is no calibration, it was poor, skipped, or the stage is
//     scrolled out of view): head-and-eye direction against a baseline learned while the student looks at the examiner's face.
// While the student is answering (the examiner's avatar is listening):
//   - looking away for GAZE.fadeStartMs starts fading the exam screen toward a heavy dim and blur over GAZE.fadeRampMs, with a
//     warning to look back at the examiner; it restores smoothly as soon as the gaze returns. A look away that begins in the
//     first GAZE.thinkingGraceMs of an answer gets GAZE.thinkingFadeStartMs instead: a precaution for a student gathering an
//     answer, not a finding that a look away is thinking (GAD-T261, GAD-T446, docs/EVIDENCE_MAP.md mechanic 4);
//   - a longer look away (GAZE.remarkAfterMs past the fade start), a reading pattern, no face or a second face makes the
//     examiner say so out loud ("Please look at me while you answer"), at most once every GAZE.remarkGapMs;
//   - with an eye-contact accommodation on the assignment (options.accommodation, GAD-T261), there are no calibration dots, the
//     screen never fades and the examiner never asks for eye contact or remarks on reading-like eyes; face presence is still
//     checked and spoken about;
//   - each episode (look-away, reading, face absent, second face, and each fade with its peak) is sent to the server with its
//     start and end and this session's tracking quality so far (a 0..100 score, the method, the measured accuracy), for review.
//     Only those notes leave the device: never a frame, an image, a landmark or a face signature.
// Nothing here infers emotion or deception, and nothing blocks the answer. If the camera or the face model can't start, the
// indicator says eye contact isn't being checked and the interview goes on.
//
// This module only reads the voice room's DOM (the <ma-avatar> in [data-voice-stage] and its data-state) and speaks through
// its own avatar hub connection, so it does not depend on voice-room.js internals.

import { calibrate as runSetup, clearFit, dotTargets, gazePoint, loadFit, median, pxToDeg, saveFit, toPage } from "./gaze-quality.js";

/** Every threshold of the eye-contact check, in one place (also read by gaze-quality.js). */
export const GAZE = Object.freeze({
    /** Calibration: the prompt shows this long before the baseline is learned, so the student has time to look at the examiner. */
    calibrationDelayMs: 1200,
    /** Most frames averaged into the head-direction baseline (about 1.5 s at 30 fps); fewer on a slow device (see baselineMs). */
    baselineFrames: 45,
    /** The head-direction baseline lasts this long whatever the frame rate (the frame count follows the measured rate, 10 at least). */
    baselineMs: 1500,
    /** The examiner's face region in degrees, from the avatar's on-screen size seen from this distance, plus slack for the estimate's error. */
    viewingDistanceCm: 60,
    slackDeg: 5,
    minHalfWidthDeg: 7, maxHalfWidthDeg: 16,
    minHalfHeightDeg: 6, maxHalfHeightDeg: 12,
    /** A look away must hold this long before the region changes (blinks and jitter don't count). */
    switchMs: 200,
    /** The screen starts to fade after this much continuous look-away while answering (also when a look-away episode starts)... */
    fadeStartMs: 2000,
    /** ...but a look away that begins within this long of the answer starting gets more time, while the student gathers an answer (GAD-T261, GAD-T446)... */
    thinkingGraceMs: 10000,
    /** ...and starts to fade only after this much. */
    thinkingFadeStartMs: 6000,
    /** ...and reaches its heaviest dim and blur this much later. */
    fadeRampMs: 3000,
    /** The heaviest fade: how dark (0..1) and how blurred (px). */
    fadeMaxDim: 0.85,
    fadeMaxBlurPx: 12,
    /** How long the screen takes to restore once the gaze is back. */
    fadeRestoreMs: 600,
    /** A look away this long makes the examiner say "Please look at me while you answer" (the thinking grace moves it later by as much). */
    remarkAfterMs: 5000,
    /** The examiner speaks up at most once in this long. */
    remarkGapMs: 15000,
    /** The longest a spoken remark may hold things up if synthesis stalls. */
    remarkTimeoutMs: 12000,
    /** No face / a second face counts after this long (presence module). */
    faceAbsentMs: 2000,
    multipleFacesMs: 1000,
    /** No gaze sample for this long means the gaze is unknown (the face was lost), not "away"; at least three frame times on a slow device. */
    sampleStaleMs: 800,
    /** No camera frame at all for this long: the camera is lost. Gaze is unknown, never "away", and the indicator says so. */
    cameraLostMs: 3000,
    /** How often episodes and the fade are updated. */
    tickMs: 100,
    /** How often the module looks for the examiner (voice mode turned on or off). */
    watchMs: 1000,
    /** With no head-direction baseline after this long (no face, camera trouble), calibration stops waiting and runs again once a face is back. */
    calibrationGiveUpMs: 15000,

    // ---- the gaze vector (GAD-T305, gaze-quality.js) ----
    /** The eyes count as closed (a blink hides the iris) when the two eyelid openings add up to less than this, in eye-widths (the library's figure). */
    blinkOpen: 0.14,
    /** Frames whose face is too small or turned too far for the iris to be placed (the library's 0..1 measure) are not used for gaze. */
    minConfidence: 0.25,

    // ---- the five-dot calibration (GAD-T305) ----
    /** Each dot: time for the eyes to land on it, then time spent sampling it... */
    calibSettleMs: 700,
    calibCollectMs: 900,
    /** ...waiting up to this long for enough usable frames (blinks, a slow device) before moving on. */
    calibTargetMaxMs: 3000,
    /** Usable frames a dot needs to count, and how many of the five dots the fit needs. */
    calibMinSamples: 5,
    calibMinTargets: 4,
    /** Across the dots the gaze point must move at least this much (cm, on each axis), or the fit can't tell where the eyes went. */
    calibMinSpreadCm: 1.5,
    /** A believable scale, in CSS px of page per cm of gaze movement; outside it the fit is nonsense (a frozen face, a wrong camera). */
    minPxPerCm: 8,
    maxPxPerCm: 200,
    /** The corner dots sit this share of the page in from each edge. */
    calibEdge: 0.08,
    /** The examiner's face is this share of her stage's height down from its top: the anchor dot and the drift reference. */
    faceAnchorY: 0.4,
    /**
     * A fit whose leave-one-out accuracy is worse than this (degrees) is poor: it is tried once more (calibAttempts in all, with
     * calibRetryPauseMs between), then not used. Webcam gaze is good to about 4 degrees at best (Papoutsaki et al. 2016).
     */
    calibPoorDeg: 6,
    calibAttempts: 2,
    calibRetryPauseMs: 1500,
    /** A calibration made on the get-ready page is reused in the exam room for this long, in the same tab and at the same zoom. */
    modelMaxAgeMs: 2 * 60 * 60 * 1000,
    /** The room's first look at the examiner checks the calibration: off by more than this (degrees), it isn't used this time. */
    anchorMaxDeg: 7,
    /** Usable frames that first look needs to check the calibration. */
    anchorMinSamples: 8,

    // ---- the calibrated check (GAD-T305) ----
    /** The examiner's region is her stage as it is on screen plus a margin: this many degrees... */
    marginDeg: 3,
    /** ...plus this share of the measured accuracy and this share of the frame-to-frame noise, kept between these bounds (degrees). */
    marginAccuracyShare: 0.75,
    marginJitterShare: 0.5,
    minMarginDeg: 3,
    maxMarginDeg: 9,
    /** Hysteresis: once away, the gaze counts as back only well inside the margin (this share of it). */
    returnShare: 0.75,
    /**
     * Two estimates must agree: a calibrated look-away counts only once the old head-and-eye direction has also strayed this share
     * of its own limits, so neither estimate's error alone makes a look-away (and leaning, which only the old one mistakes, never does).
     */
    agreeShare: 0.5,
    /** The stage must be at least this share in view for the calibrated check; scrolled away, the head-direction check decides. */
    minStageVisible: 0.3,
    /** Drift: while the examiner speaks, gaze within this many degrees of her face is collected... */
    driftWindowDeg: 6,
    /** ...for at least this much looking time... */
    driftMinMs: 3000,
    /** ...and a steady offset larger than this (degrees) is corrected by this share of it, quietly. */
    driftMinDeg: 1.5,
    driftGain: 0.5,

    // ---- slow devices (GAD-T305) ----
    /** Below this many frames a second (after fpsWarmupMs), this module's copy of the camera is asked for slowDeviceFps, so the face model runs less often. */
    slowFps: 12,
    slowDeviceFps: 10,
    fpsWarmupMs: 5000,

    // ---- the per-session quality score, 0..100, sent with every episode (GAD-T305) ----
    /** Accuracy (degrees, the worse of the leave-one-out and the room's first look): full marks at the first figure, none at the second. */
    qAccGoodDeg: 2.5,
    qAccPoorDeg: 8,
    /** Steadiness (frame-to-frame gaze noise during calibration, degrees; a typical webcam's landmark noise gives about 1 to 3.5): the same. */
    qJitterGoodDeg: 1,
    qJitterPoorDeg: 4,
    /** Speed (frames a second): the same. */
    qFpsGood: 20,
    qFpsPoor: 8,
    /** The parts of the score: accuracy, steadiness, coverage (answering time with a usable gaze sample), conditions (time without a lighting or framing warning), speed. */
    qWeights: Object.freeze({ accuracy: 0.4, steadiness: 0.15, coverage: 0.25, conditions: 0.1, speed: 0.1 }),
    /** Without a calibration (head direction only) the score is at most this, from coverage, conditions and speed. */
    qHeadMax: 50,
    /** Points off for each quiet drift correction, up to the second figure. */
    qDriftPenalty: 2,
    qDriftPenaltyMax: 15,
    /** The confidence sent with a look-away, reading or fade episode runs from the first figure (score 0) to the second (score 100). */
    qConfidenceMin: 0.15,
    qConfidenceMax: 0.4,

    // ---- the get-ready setup check (GAD-T305, gaze-quality.js); advice only, never a failure ----
    /** Frames with one face watched (ms of looking time), at most this long in all, and the longest the face model may take to load. */
    checkMs: 4000,
    checkTimeoutMs: 20000,
    loadTimeoutMs: 30000,
    /** Mean picture brightness (0..255) below this is "dark" (the library's own figure). */
    minLuma: 45,
    /** The rest of the picture brighter than the face by more than this (0..255) is "a bright light behind you". */
    backlightDelta: 35,
    /** Head turned from the camera by more than this while looking at the screen (degrees) means a steep camera angle. */
    maxCameraPitchDeg: 22,
    maxCameraYawDeg: 22,
    /** Eye distance as a share of the picture's height: under the first, too far; over the second, too close. */
    minFaceScale: 0.045,
    maxFaceScale: 0.2,
    /** Face centre further than this from the picture's middle (share of the picture) is off to one side. */
    maxOffCentre: 0.3,
    /** Frame-to-frame gaze noise above this (degrees) means the eyes are hard to follow (glare on glasses, dim light): the noisy end of a typical webcam. Unvalidated. */
    maxJitterDeg: 3,
});

const LIB = "/lib/voice";
const AVATAR_HUB = "/hubs/avatar";
const KINDS = ["look-away", "reading", "face-absent", "second-face"];

let G = null;

const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
const round1 = (v) => Math.round(v * 10) / 10;
const iso = (ms) => new Date(ms).toISOString();
/** A performance.now() time as Unix ms. */
const epochOf = (perf) => Date.now() - (performance.now() - perf);

const TEXT = {
    off: "",
    loading: "Starting the eye-contact check…",
    calibrating: "Look at the examiner's face for a moment, so the camera learns where it is.",
    "calibrating-simple": "Eye tracking couldn't be set up precisely, so a simpler check of where your head and eyes point is used. That's fine. Now look at the examiner's face for a moment.",
    setup: "Follow the dot with your eyes and keep your head still. It takes about 10 seconds.",
    on: "Looking at the examiner",
    away: "Looking away from the examiner. Please look at the examiner.",
    reading: "Your eyes are moving as if reading. Please look at the examiner.",
    "no-face": "Your face isn't in view of the camera.",
    "two-faces": "More than one face is in view of the camera.",
    lost: "The camera picture has stopped, so eye contact isn't being checked right now. The exam carries on.",
    unknown: "Eye contact can't be checked right now: the camera can't follow your eyes. The exam carries on.",
    accommodated: "Eye contact isn't required for you (accommodation). The camera only checks that your face is in view.",
    unavailable: "Eye contact isn't being checked: the camera check couldn't start on this device. The exam carries on.",
    setupCaption: "Follow the dot with your eyes and keep your head still. Esc or Skip stops.",
    setupRetry: "Not quite precise enough. Once more: sit still and look right at the centre of each dot.",
};

/** Starts watching for the examiner; the camera is used only while the examiner is on screen. */
export function start(dotnet, options) {
    stop();
    const g = {
        dotnet, options: options || {}, stopped: false, running: false, starting: false, failed: false,
        lib: null, core: null, presence: null, headpose: null, reading: null, offs: [],
        avatar: null, faces: 1, warnings: [], calibrating: false, calibTimer: null, giveUpTimer: null,
        awayPerf: null, lastSamplePerf: 0, lastFramePerf: 0, cond: { reading: null, "face-absent": null, "second-face": null },
        answering: false, answeringSince: 0, episodes: {}, fade: null, level: 0,
        lastRemark: 0, remarking: false, hub: null, speech: null, hubAvatar: null, shown: null,
        // GAD-T305: "calibrated" or "head"; the page fit and its measured accuracy; the calibrated verdict; drift samples.
        mode: "head", fit: null, calib: null, cal: null, anchor: null, setup: null, setupOutcome: null, needsCalibration: false,
        fx: null, fy: null, rect: null, drift: [], fps: 0, fpsCount: 0, fpsSince: 0, startedPerf: 0, slow: false,
        stats: { ticks: 0, warnTicks: 0, answerTicks: 0, freshTicks: 0, drifts: 0 },
    };
    G = g;
    g.onClick = (e) => {
        const el = e.target.closest && e.target.closest("[data-examiner-gaze-action]");
        if (el && G === g && el.dataset.examinerGazeAction === "calibrate") void setupDots(g);
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
        g.lib = lib;
        const limits = examinerLimits(lib);
        core = g.core = new lib.WebcamCore({
            numFaces: 2, width: 640, height: 480,
            // Twice a second, a 16x9 thumbnail averaged to one brightness number: lets presence warn about a dark picture (quality score).
            measureLight: true,
            wasmBaseUrl: new URL(".", base).href.replace(/\/$/, ""),
            modelUrl: new URL("face_landmarker.bin", base).href,
        });
        g.presence = new lib.PresenceMonitor({ absentMs: GAZE.faceAbsentMs, multipleMs: GAZE.multipleFacesMs });
        g.headpose = new lib.HeadPoseMonitor({
            ...limits, baselineFrames: GAZE.baselineFrames, switchMs: GAZE.switchMs, minConfidence: GAZE.minConfidence,
            // Its own off-screen events are not used here: episodes are decided below, only while the student answers.
            thinkingMs: 1e9, sustainedMs: 1e9,
        });
        g.reading = new lib.ReadingPatternDetector();
        g.fx = new lib.OneEuroFilter();
        g.fy = new lib.OneEuroFilter();
        // First, so each frame's calibrated verdict is ready when the head-pose monitor's sample for the same frame is judged.
        g.offs.push(core.subscribe((f) => onFrame(g, f)));
        g.headpose.on("sample", (s) => onSample(g, s));
        g.reading.on("begin", (b) => { g.cond.reading = b.start; });
        g.reading.on("event", () => { g.cond.reading = null; });
        g.presence.on("status", (st) => {
            g.faces = st.faces;
            g.warnings = st.warnings || [];
            if (st.faces !== 1) g.awayPerf = null;
            // A calibration that gave up for want of a face runs again as soon as one is back.
            if (st.faces === 1 && g.needsCalibration && g.running && !g.calibrating && !g.setup) calibrate(g);
        });
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
        g.startedPerf = g.lastFramePerf = g.fpsSince = performance.now();
        g.fpsCount = 0;
        begin(g);
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
    if (g.setup) { g.setup.cancel = true; g.setup = null; }
    setCalibrating(g, false);
    releaseCore(g);
    g.running = false;
    g.awayPerf = null;
    g.anchor = null;
    g.cal = null;
    g.mode = "head";
    g.needsCalibration = false;
    g.cond = { reading: null, "face-absent": null, "second-face": null };
}

function releaseCore(g) {
    try { g.offs.forEach((off) => off && off()); } catch { }
    g.offs = [];
    try { g.core && g.core.stop(); } catch { }
    g.core = g.presence = g.headpose = g.reading = null;
}

/**
 * The examiner's face region for the head-direction check, in degrees either side of the baseline: the avatar's on-screen size
 * (CSS px taken as 1/96 inch) seen from GAZE.viewingDistanceCm, plus slack, kept within sane bounds.
 */
function examinerLimits(lib) {
    const r = stageRect();
    if (!r) return { halfWidthDeg: 10, halfHeightDeg: 8 };
    const cm = (px) => (px / 96) * 2.54;
    const l = lib.regionLimitsFor(cm(r.width), cm(r.height), GAZE.viewingDistanceCm, GAZE.slackDeg);
    return {
        halfWidthDeg: clamp(l.halfWidthDeg, GAZE.minHalfWidthDeg, GAZE.maxHalfWidthDeg),
        halfHeightDeg: clamp(l.halfHeightDeg, GAZE.minHalfHeightDeg, GAZE.maxHalfHeightDeg),
    };
}

function stageRect() {
    const stage = document.querySelector("[data-voice-stage]");
    const r = stage && stage.getBoundingClientRect();
    return r && r.width > 0 && r.height > 0 ? r : null;
}

/** Where the examiner's face is on the page (CSS px), from her stage's rectangle. */
function faceOf(r) {
    return r ? { x: r.left + r.width / 2, y: r.top + r.height * GAZE.faceAnchorY } : null;
}

/** How much of the stage is inside the window (0..1). */
function visibleShare(r) {
    const w = Math.max(0, Math.min(r.right, window.innerWidth) - Math.max(r.left, 0));
    const h = Math.max(0, Math.min(r.bottom, window.innerHeight) - Math.max(r.top, 0));
    return (w * h) / (r.width * r.height);
}

/** Frames for the head-direction baseline at the measured frame rate: GAZE.baselineMs of them, between 10 and GAZE.baselineFrames. */
function baselineFrames(g) {
    return Math.round(clamp((GAZE.baselineMs * (g.fps || 30)) / 1000, 10, GAZE.baselineFrames));
}

/** A gaze sample older than this is unknown: GAZE.sampleStaleMs, or three frame times on a slow device. */
function staleMs(g) {
    return Math.max(GAZE.sampleStaleMs, g.fps > 0 ? 3000 / g.fps : 0);
}

// ---------------------------------------------------------------------------------------------------------- calibration

/** The camera is on: reuse the get-ready page's calibration if there is one, then the first look at the examiner. */
function begin(g) {
    if (g.options.accommodation) {
        // Eye contact isn't required: no dots and no prompt; a quiet baseline keeps the (Info) notes meaningful.
        g.mode = "head";
        if (g.headpose) { g.headpose.options.baselineFrames = baselineFrames(g); g.headpose.recalibrate(); }
        return;
    }
    const fit = loadFit();
    g.fit = fit ? { ...fit } : null;
    g.calib = fit ? { crossDeg: fit.crossDeg, jitterDeg: fit.jitterDeg, validationDeg: null } : null;
    calibrate(g);
}

/**
 * Asks the student to look at the examiner's face, then learns that direction as "looking at the examiner" (the head-direction
 * baseline) and, with a calibration, checks it against her face and takes her face as the reference.
 */
function calibrate(g) {
    if (!g.running || !g.headpose) return;
    g.needsCalibration = false;
    setCalibrating(g, true);
    g.anchor = null;
    try { const st = document.querySelector("[data-voice-stage]"); st && st.scrollIntoView({ block: "nearest" }); } catch { }
    g.calibTimer = setTimeout(() => {
        if (!g.running || !g.headpose) return;
        if (g.lib) Object.assign(g.headpose.options, examinerLimits(g.lib), { baselineFrames: baselineFrames(g) });
        g.headpose.recalibrate();
        g.awayPerf = null;
        g.anchor = { pts: [] };
        g.calibArmed = true;
    }, GAZE.calibrationDelayMs);
    g.giveUpTimer = setTimeout(() => giveUp(g), GAZE.calibrationGiveUpMs);
}

/** No baseline in time (no face, camera trouble): stop prompting; calibration runs again when a face is back. */
function giveUp(g) {
    if (!g.calibrating || g.setup) return;
    setCalibrating(g, false);
    g.anchor = null;
    g.mode = "head";
    g.setupOutcome = null;
    g.needsCalibration = true;
}

function setCalibrating(g, on) {
    g.calibrating = on;
    g.calibArmed = false;
    clearTimeout(g.calibTimer);
    clearTimeout(g.giveUpTimer);
    const stage = document.querySelector("[data-voice-stage]");
    if (stage) { if (on) stage.setAttribute("data-gaze-calibrating", ""); else stage.removeAttribute("data-gaze-calibrating"); }
}

/** The first look at the examiner is done (the head-direction baseline is ready): check the calibration against it. */
function finishAnchor(g) {
    const a = g.anchor;
    g.anchor = null;
    setCalibrating(g, false);
    g.setupOutcome = null;
    g.mode = "head";
    g.cal = null;
    if (!g.fit || !g.calib || !a || a.pts.length < GAZE.anchorMinSamples) return;
    const face = faceOf(stageRect());
    if (!face) return;
    const dist = median(a.pts.map((p) => p.dist));
    const p = toPage(g.fit, median(a.pts.map((q) => q.x)), median(a.pts.map((q) => q.y)));
    const err = pxToDeg(g.fit, p.x - face.x, p.y - face.y, dist);
    if (!Number.isFinite(err)) return;
    g.calib.validationDeg = round1(err);
    if (err > GAZE.anchorMaxDeg) return;   // the setup changed too much since (another window size, another seat): the old check
    // The student is looking at her face now: that is the reference from here on (removes what changed since the setup).
    g.fit.ax += face.x - p.x;
    g.fit.ay += face.y - p.y;
    g.mode = "calibrated";
    g.cal = { region: "on", since: performance.now(), candidate: null, candidateSince: 0, lastT: 0, usable: false };
    g.fx.reset();
    g.fy.reset();
    g.drift = [];
}

/** "Set up eye contact again": the five dots here in the room, then the first look at the examiner. */
async function setupDots(g) {
    if (!g.running || g.setup || !g.lib || !g.core || g.options.accommodation) return;
    const control = { cancel: false };
    g.setup = control;
    setCalibrating(g, true);
    g.anchor = null;
    g.awayPerf = null;
    show(g, "setup");
    try { const st = document.querySelector("[data-voice-stage]"); st && st.scrollIntoView({ block: "nearest" }); } catch { }
    let r = null;
    try {
        r = await runSetup(g.lib, g.core, {
            targets: () => dotTargets(faceOf(stageRect())),
            captions: [TEXT.setupCaption, TEXT.setupRetry],
            control,
        });
    } catch { r = null; }
    if (G !== g || g.setup !== control) return;   // paused or stopped meanwhile
    g.setup = null;
    if (r && r.ok) {
        saveFit(r.fit);
        g.fit = { ...r.fit };
        g.calib = { crossDeg: r.crossDeg, jitterDeg: r.jitterDeg, validationDeg: null };
        g.setupOutcome = "ok";
    } else if (r && (r.reason === "skipped" || r.reason === "camera")) {
        g.setupOutcome = null;   // whatever was in use before stays (a lost camera says so in the indicator)
    } else {
        // Poor, or the camera stopped: the old head-direction check, said plainly.
        clearFit();
        g.fit = g.calib = null;
        g.setupOutcome = "poor";
    }
    calibrate(g);
}

// --------------------------------------------------------------------------------------------------------- per frame

function onFrame(g, f) {
    const t = f.t;
    g.lastFramePerf = t;
    g.fpsCount++;
    if (t - g.fpsSince >= 1000) {
        g.fps = (g.fpsCount * 1000) / (t - g.fpsSince);
        g.fpsCount = 0;
        g.fpsSince = t;
        slowDown(g, t);
    }
    if (f.faces.length !== 1 || !g.lib) return;   // none, or a second face that could be the one measured: presence's business
    const p = gazePoint(g.lib, f.faces[0]);
    if (!p) return;
    if (g.anchor) g.anchor.pts.push(p);
    if (g.mode === "calibrated" && g.fit && g.cal && !g.calibrating) classify(g, p, t);
}

/** The calibrated verdict for one frame: on the examiner (her stage plus the margin) or away, held for GAZE.switchMs. */
function classify(g, p, t) {
    const c = g.cal;
    const fit = g.fit;
    // Smoothed as a direction (cm over distance), so the One Euro filter's units are the same at any distance.
    const sx = g.fx.filter(p.x / p.dist, t);
    const sy = g.fy.filter(p.y / p.dist, t);
    const pt = toPage(fit, sx * p.dist, sy * p.dist);
    const r = g.rect;
    c.lastT = t;
    c.usable = !!r && visibleShare(r) >= GAZE.minStageVisible;
    if (!c.usable) return;
    const dx = Math.max(r.left - pt.x, 0, pt.x - r.right);
    const dy = Math.max(r.top - pt.y, 0, pt.y - r.bottom);
    const deg = pxToDeg(fit, dx, dy, p.dist);
    const margin = marginDeg(g);
    const want = deg > (c.region === "on" ? margin : margin * GAZE.returnShare) ? "away" : "on";
    if (want !== c.region) {
        if (c.candidate !== want) { c.candidate = want; c.candidateSince = t; }
        if (t - c.candidateSince >= GAZE.switchMs) { c.region = want; c.since = c.candidateSince; c.candidate = null; }
    } else c.candidate = null;

    // Drift: while the examiner speaks, people look at her. A steady small offset from her face then is the fit's, not theirs.
    const a = g.avatar;
    if (!g.answering && a && a.dataset && a.dataset.state === "speaking") {
        const face = faceOf(r);
        const ox = pt.x - face.x, oy = pt.y - face.y;
        if (pxToDeg(fit, ox, oy, p.dist) <= GAZE.driftWindowDeg) {
            g.drift.push({ x: ox, y: oy, d: p.dist });
            if (g.drift.length > 600) g.drift.shift();
            if (g.drift.length >= Math.max(10, ((g.fps || 15) * GAZE.driftMinMs) / 1000)) correctDrift(g);
        }
    }
}

function correctDrift(g) {
    const fit = g.fit;
    const mx = median(g.drift.map((s) => s.x));
    const my = median(g.drift.map((s) => s.y));
    const d = median(g.drift.map((s) => s.d));
    g.drift = [];
    if (!(pxToDeg(fit, mx, my, d) >= GAZE.driftMinDeg)) return;
    fit.ax -= mx * GAZE.driftGain;
    fit.ay -= my * GAZE.driftGain;
    g.stats.drifts++;
}

/** The margin around the examiner's stage, in degrees: GAZE.marginDeg plus shares of the measured accuracy and noise, within bounds. */
function marginDeg(g) {
    const jitter = g.calib && Number.isFinite(g.calib.jitterDeg) ? g.calib.jitterDeg : 0;
    return clamp(GAZE.marginDeg + GAZE.marginAccuracyShare * accuracyOf(g) + GAZE.marginJitterShare * jitter, GAZE.minMarginDeg, GAZE.maxMarginDeg);
}

/** The calibration's accuracy in degrees: the worse of its leave-one-out figure and the room's first look at the examiner. */
function accuracyOf(g) {
    const c = g.calib;
    return c ? Math.max(c.crossDeg || 0, c.validationDeg || 0) : GAZE.qAccPoorDeg;
}

/** "on", "away", or null when the calibrated check can't say (not calibrated, stale, the stage scrolled away). */
function calibratedVerdict(g, t) {
    const c = g.cal;
    if (g.mode !== "calibrated" || !c || !c.usable || t - c.lastT > staleMs(g)) return null;
    return c.region;
}

/** How far the head-and-eye direction has strayed from the baseline, as a share of its limits. */
function headShare(g, s) {
    const o = g.headpose ? g.headpose.options : { halfWidthDeg: 10, halfHeightDeg: 8 };
    return Math.max(Math.abs(s.yawDeg) / o.halfWidthDeg, Math.abs(s.pitchDeg) / o.halfHeightDeg);
}

function onSample(g, s) {
    const verdict = calibratedVerdict(g, s.t);
    // The reading detector looks only at looks away: the calibrated check, when it can, says when the student is on the examiner.
    if (g.reading) g.reading.push(verdict === "on" ? { ...s, region: "on-screen" } : s);
    g.lastSamplePerf = s.t;
    if (g.calibrating) {
        // Done once the baseline learned after the prompt (not one from before it) is ready.
        if (s.ready && g.calibArmed && !g.setup && g.headpose && g.headpose.baselineReady) finishAnchor(g);
        return;
    }
    let away = false;
    let from = s.t;
    const headAway = s.ready && s.region !== "on-screen";
    if (verdict != null) {
        if (verdict === "away" && s.ready && headShare(g, s) >= GAZE.agreeShare) {
            away = true;
            from = Math.max(g.cal.since, headAway ? s.t - s.dwellMs : s.t);
        }
    } else if (headAway) {
        away = true;
        from = s.t - s.dwellMs;
    }
    if (away && g.faces === 1) { if (g.awayPerf == null) g.awayPerf = from; }
    else g.awayPerf = null;
}

/** A slow device: ask for fewer frames on this module's own copy of the camera (the self-view keeps its own rate). */
function slowDown(g, t) {
    if (g.slow || !g.running || t - g.startedPerf < GAZE.fpsWarmupMs || !(g.fps > 0) || g.fps >= GAZE.slowFps) return;
    g.slow = true;
    try {
        const v = g.core && g.core.video;
        const track = v && v.srcObject && v.srcObject.getVideoTracks && v.srcObject.getVideoTracks()[0];
        if (track && track.applyConstraints) track.applyConstraints({ frameRate: { ideal: GAZE.slowDeviceFps, max: GAZE.slowDeviceFps } }).catch(() => { });
    } catch { /* the rate stays as it is */ }
}

/** Answering means the examiner is listening (voice-room.js sets the avatar to "listening" while the microphone is open). */
function isAnswering(g) {
    const a = g.avatar;
    return !!a && a.isConnected && (g.remarking || (a.dataset && a.dataset.state === "listening"));
}

function tick(g) {
    if (g.stopped || !g.running) return;
    const now = performance.now();
    g.rect = stageRect();
    const lost = now - g.lastFramePerf > GAZE.cameraLostMs;
    if (lost) g.awayPerf = null;
    if (g.awayPerf != null && now - g.lastSamplePerf > staleMs(g)) g.awayPerf = null;
    const answering = !g.calibrating && isAnswering(g);
    if (answering && !g.answering) g.answeringSince = Date.now();
    g.answering = answering;
    const awayMs = g.awayPerf == null ? 0 : now - g.awayPerf;
    const fadeStart = fadeStartFor(g, now);
    const accommodated = !!g.options.accommodation;

    // What the quality score counts: setup warnings over time, and answering time with a usable gaze sample.
    g.stats.ticks++;
    if (g.warnings.length) g.stats.warnTicks++;
    if (answering && g.faces === 1 && !lost) {
        g.stats.answerTicks++;
        if (now - g.lastSamplePerf <= staleMs(g)) g.stats.freshTicks++;
    }

    // Episodes, only while answering.
    const active = {
        "look-away": awayMs >= fadeStart ? epochOf(g.awayPerf) : null,
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
            // The accommodation: no spoken request about where the student looks (face presence still counts).
            if (accommodated && (kind === "look-away" || kind === "reading")) continue;
            if (kind === "look-away" && awayMs < GAZE.remarkAfterMs + (fadeStart - GAZE.fadeStartMs)) continue;
            if (remark(g, kind)) ep.remarked = true;
            break;
        }
    }

    // The fade: from the fade start of continuous look-away, to the heaviest over GAZE.fadeRampMs; never under the accommodation.
    const level = answering && !accommodated && awayMs >= fadeStart ? clamp((awayMs - fadeStart) / GAZE.fadeRampMs, 0, 1) : 0;
    if (level > 0) {
        if (!g.fade) g.fade = { start: epochOf(g.awayPerf + fadeStart), peak: 0 };
        g.fade.peak = Math.max(g.fade.peak, level);
    } else closeFade(g);
    applyFade(g, level);

    show(g, indicatorState(g, awayMs, lost));
}

/**
 * How much continuous look-away starts the fade (and a look-away episode): longer when the look away began in the first
 * GAZE.thinkingGraceMs of the answer, as a precaution while the student gathers an answer (GAD-T261; not a claim that it is thinking, GAD-T446).
 */
function fadeStartFor(g, now) {
    if (g.awayPerf == null || !g.answering) return GAZE.fadeStartMs;
    const answerStartPerf = now - (Date.now() - g.answeringSince);
    return Math.max(g.awayPerf, answerStartPerf) - answerStartPerf < GAZE.thinkingGraceMs ? GAZE.thinkingFadeStartMs : GAZE.fadeStartMs;
}

function indicatorState(g, awayMs, lost) {
    if (g.setup) return "setup";
    if (g.calibrating) return g.setupOutcome === "poor" ? "calibrating-simple" : "calibrating";
    if (lost) return "lost";
    if (g.faces === 0) return "no-face";
    if (g.faces > 1) return "two-faces";
    if (g.options.accommodation) return "accommodated";
    if (!g.headpose || !g.headpose.baselineReady) return "unknown";
    if (g.cond.reading != null) return "reading";
    if (awayMs > 0) return "away";
    return "on";
}

// ----------------------------------------------------------------------------------------------------------- quality

/**
 * This session's tracking quality so far (GAD-T305), 0..100, from the parts weighted in GAZE.qWeights. Head direction only (no
 * calibration in use) scores at most GAZE.qHeadMax. It says how far a reviewer can trust the eye-contact notes, nothing more.
 */
function quality(g) {
    const s = g.stats;
    const w = GAZE.qWeights;
    const share = (good, poor, v) => clamp((v - poor) / (good - poor), 0, 1);
    const coverage = s.answerTicks > 0 ? s.freshTicks / s.answerTicks : 1;
    const conditions = s.ticks > 0 ? 1 - s.warnTicks / s.ticks : 1;
    const speed = share(GAZE.qFpsGood, GAZE.qFpsPoor, g.fps || GAZE.qFpsGood);
    if (g.mode === "calibrated" && g.calib) {
        const accuracy = accuracyOf(g);
        const steadiness = Number.isFinite(g.calib.jitterDeg) ? share(GAZE.qJitterGoodDeg, GAZE.qJitterPoorDeg, g.calib.jitterDeg) : 0.5;
        const raw = w.accuracy * share(GAZE.qAccGoodDeg, GAZE.qAccPoorDeg, accuracy) + w.steadiness * steadiness
            + w.coverage * coverage + w.conditions * conditions + w.speed * speed;
        const score = clamp(100 * raw - Math.min(GAZE.qDriftPenaltyMax, GAZE.qDriftPenalty * s.drifts), 0, 100);
        return { score: Math.round(score), calibrated: true, accuracy: round1(accuracy) };
    }
    const raw = (w.coverage * coverage + w.conditions * conditions + w.speed * speed) / (w.coverage + w.conditions + w.speed);
    return { score: Math.round(clamp(GAZE.qHeadMax * raw, 0, 100)), calibrated: false, accuracy: null };
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

/**
 * Sends one episode. The summary is codes only, never free text: "remarked" or "peak:NN", then the quality ("q:NN", "m:cal" or
 * "m:head", and "a:N.N" degrees when calibrated); the server writes the sentence a reviewer reads.
 */
function send(g, e) {
    if (!(e.end >= e.start)) return;
    const q = quality(g);
    const codes = [e.summary, "q:" + q.score, "m:" + (q.calibrated ? "cal" : "head")];
    if (q.calibrated) codes.push("a:" + q.accuracy.toFixed(1));
    const gazeKind = e.type === "examiner-look-away" || e.type === "examiner-reading" || e.type === "examiner-fade";
    const confidence = gazeKind ? Math.round((GAZE.qConfidenceMin + (GAZE.qConfidenceMax - GAZE.qConfidenceMin) * q.score / 100) * 100) / 100 : 0.4;
    const ev = { source: "webcam", type: e.type, startedAt: iso(e.start), endedAt: iso(e.end), confidence, summary: codes.filter(Boolean).join(";") };
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
    if (again) again.hidden = !(state === "on" || state === "away" || state === "reading" || state === "unknown");
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
