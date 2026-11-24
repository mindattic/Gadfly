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
// While the student is answering (the examiner's avatar is listening), by phase (GAD-T557, docs/research/attention-and-gaze.md 4.1):
//   - the thinking gap, from her last word to the student's first (GAZE.thinkingGapMaxMs at most): nothing ever fires. Looking
//     away while working out a hard answer is normal and helps (Glenberg et al. 1998; Doherty-Sneddon et al. 2012);
//   - speaking: gaze is face (her stage plus the margin), on-screen (anywhere else in the window: the transcript, the clock,
//     the captions) or off-screen. Only off-screen gaze counts, and only once it has lasted GAZE.offScreenMs continuously AND
//     the off-screen share of the last GAZE.offScreenWindowMs is over GAZE.offScreenShare; poor tracking quality lengthens it;
//   - then the reminder ladder (GAD-T561, 4.2), the examiner first: (1) a small slow head tilt and raised brows; (2) if the gaze
//     stays off-screen, a light dim and blur of everything but her stage (at most 35% and 4 px, her face kept sharp), restored
//     quickly when it returns, with a cool-down so it never cycles; (3) one short, warm, varied line at a pause in the student's
//     speech, never over them and never "look at me", once per question and GAZE.spokenPerExam times per exam at most;
//   - no face or a second face (presence, not gaze) is still said out loud, but never in the thinking gap;
//   - with an eye-contact accommodation on the assignment (options.accommodation, GAD-T261), there are no calibration dots and no
//     ladder; face presence is still checked and spoken about;
//   - each episode (look-away, reading, face absent, second face, and each fade with its peak) is sent to the server with its
//     start and end and this session's tracking quality so far (a 0..100 score, the method, the measured accuracy), for review.
//     Only those notes leave the device: never a frame, an image, a landmark or a face signature.
// Nothing here infers emotion or deception, and nothing blocks the answer. If the camera or the face model can't start, the
// indicator says eye contact isn't being checked and the interview goes on.
//
// This module only reads the voice room's DOM (the <ma-avatar> in [data-voice-stage] and its data-state) and speaks through
// its own avatar hub connection, so it does not depend on voice-room.js internals.

import { calibrate as runSetup, clearFit, dotTargets, gazePoint, loadFit, median, pxToDeg, saveFit, toPage } from "./gaze-quality.js";
import { t } from "./ui-text.js";

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
    // ---- the phase-aware policy (GAD-T557, docs/research/attention-and-gaze.md 4.1); every figure is a design choice to tune ----
    /**
     * The thinking gap: from the examiner's last word (the microphone opens) to the student's first word, or this long at most.
     * Looking away then is how people manage a hard question (62-92% of the gap; Doherty-Sneddon et al. 2012): nothing ever fires.
     */
    thinkingGapMaxMs: 20000,
    /** While speaking, a reminder needs continuous off-screen gaze (not on her, not anywhere in the window) this long... */
    offScreenMs: 8000,
    /** ...and an off-screen share over this window (of this answer's speaking time so far) above this share. */
    offScreenWindowMs: 30000,
    offScreenShare: 0.5,
    /** Tracking quality under GazeQuality.FairFrom (40) raises both time thresholds by this factor (a 4-degree tracker can't afford tight ones). */
    poorQualityScore: 40,
    poorQualityFactor: 1.5,
    /** An off-screen look-away is recorded for review (never acted on) once it has lasted this long, in any answering phase. */
    episodeMs: 8000,

    // ---- the reminder ladder (GAD-T561, 4.2): the examiner first, the screen second, words last; never a white-out ----
    /** Step 2, the peripheral soften, starts this long after step 1 (her small gesture) if the gaze is still off-screen... */
    softenAfterMs: 4000,
    /** ...dims everything but her stage to at most this (0..1) and blurs it to at most this (px), over this long... */
    softenMaxDim: 0.35,
    softenMaxBlurPx: 4,
    softenRampMs: 2000,
    /** ...restores this quickly when the gaze returns, and once restored never starts again within this long (no cycling). */
    softenRestoreMs: 350,
    softenCooldownMs: 5000,
    /** Step 3, one short warm line, this long after step 2, only at a pause in the student's speech at least this long... */
    spokenAfterMs: 6000,
    spokenPauseMs: 700,
    /** ...at most once per question and at most this many times per exam. */
    spokenPerExam: 3,
    /** Her gesture (step 1): how long the small head tilt and raised brows take. */
    gestureMs: 2000,
    /** The examiner speaks up (a reminder, or about no face or a second face) at most once in this long. */
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
    /**
     * Where the examiner's eyes are when they can't be measured (no avatar and no presence shown): this share of her stage's
     * height down from its top, centred. Normally the point between her eyes is taken from what is drawn (GAD-T509, faceShare).
     */
    faceAnchorY: 0.4,
    /** The point between her eyes is re-measured this often (and at once when the stage changes size)... */
    faceRefreshMs: 400,
    /** ...and eased toward by this share each time, so the 3D head's small idle movements don't jitter the target. */
    faceSmoothing: 0.5,
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
    // No "away" state is shown (GAD-T561): a running red "looking away" line is a standing threat cue, and looking away to think is fine.
    on: "Eye contact check on. Looking away to think is fine.",
    "no-face": "Your face isn't in view of the camera.",
    "two-faces": "More than one face is in view of the camera.",
    lost: "The camera picture has stopped, so eye contact isn't being checked right now. The exam carries on.",
    unknown: "Eye contact can't be checked right now: the camera can't follow your eyes. The exam carries on.",
    accommodated: "Eye contact isn't required for you (accommodation). The camera only checks that your face is in view.",
    unavailable: "Eye contact isn't being checked: the camera check couldn't start on this device. The exam carries on.",
    setupCaption: "Follow the dot with your eyes and keep your head still. Esc or Skip stops.",
    setupRetry: "Not quite precise enough. Once more: sit still and look right at the centre of each dot.",
};

/** Each line in the page's language (GAD-T606: examRoom.js.gaze.<camelCase state>), TEXT's English as the fallback. */
const GAZE_KEY = "examRoom.js.gaze.";
const say = (state) => TEXT[state]
    ? t(GAZE_KEY + state.replace(/-(\w)/g, (m, c) => c.toUpperCase()), TEXT[state])
    : "";

/** Starts watching for the examiner; the camera is used only while the examiner is on screen. */
export function start(dotnet, options) {
    stop();
    const g = {
        dotnet, options: options || {}, stopped: false, running: false, starting: false, failed: false,
        lib: null, core: null, presence: null, headpose: null, reading: null, offs: [],
        avatar: null, faces: 1, warnings: [], calibrating: false, calibTimer: null, giveUpTimer: null,
        lastSamplePerf: 0, lastFramePerf: 0, cond: { reading: null, "face-absent": null, "second-face": null },
        answering: false, answeringSince: 0, episodes: {}, fade: null, level: 0,
        lastRemark: 0, remarking: false, hub: null, speech: null, hubAvatar: null, shown: null,
        // GAD-T305: "calibrated" or "head"; the page fit and its measured accuracy; the calibrated verdict; drift samples.
        mode: "head", fit: null, calib: null, cal: null, anchor: null, setup: null, setupOutcome: null, needsCalibration: false,
        fx: null, fy: null, rect: null, drift: [], fps: 0, fpsCount: 0, fpsSince: 0, startedPerf: 0, slow: false,
        stats: { ticks: 0, warnTicks: 0, answerTicks: 0, freshTicks: 0, drifts: 0 },
        // GAD-T557: where the gaze is ("face", "screen", "off", or null unknown), since when it has been off-screen, this answer's
        // timeline (start, first word, the student's voice) and the off-screen samples of its speaking phase.
        where: null, offPerf: null, answerStartPerf: 0, firstWordPerf: null, studentSpeaking: false, lastSpeechEndPerf: null,
        samples: [], winLimits: null,
        // GAD-T561: the reminder ladder for this answer (step 0..3), the spoken lines this exam, the soften's cool-down.
        ladder: { step: 0, at: 0, spoke: false }, spokenCount: 0, softenCooldownUntil: 0,
    };
    G = g;
    g.onClick = (e) => {
        const el = e.target.closest && e.target.closest("[data-examiner-gaze-action]");
        if (el && G === g && el.dataset.examinerGazeAction === "calibrate") void setupDots(g);
    };
    // The student's voice (voice-room.js), or typing under the typed-answer accommodation: the first word ends the thinking gap,
    // and a pause in it is when a spoken reminder may come (GAD-T557, GAD-T561).
    g.onSpeech = (e) => studentVoice(g, !!(e.detail && e.detail.speaking));
    g.onInput = (e) => {
        if (!e.target || !e.target.closest || !e.target.closest("#answer-box")) return;
        studentVoice(g, true);
        clearTimeout(g.typingTimer);
        g.typingTimer = setTimeout(() => studentVoice(g, false), GAZE.spokenPauseMs);
    };
    document.addEventListener("gadfly-student-speech", g.onSpeech);
    document.addEventListener("input", g.onInput, true);
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
    document.removeEventListener("gadfly-student-speech", g.onSpeech);
    document.removeEventListener("input", g.onInput, true);
    clearTimeout(g.typingTimer);
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
        g.winLimits = windowLimits(lib);
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
            if (st.faces !== 1) g.offPerf = null;
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
    g.where = null;
    g.samples = [];
    g.ladder = { step: 0, at: 0, spoke: false };
    if (g.setup) { g.setup.cancel = true; g.setup = null; }
    setCalibrating(g, false);
    releaseCore(g);
    g.running = false;
    g.offPerf = null;
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

/**
 * The whole window's region for the head-direction check (GAD-T557): the window's size seen from GAZE.viewingDistanceCm, plus
 * slack. Beyond it the head points off the screen; between her region and it, at something else on the screen.
 */
function windowLimits(lib) {
    const cm = (px) => (px / 96) * 2.54;
    try {
        const l = lib.regionLimitsFor(cm(window.innerWidth || 1280), cm(window.innerHeight || 720), GAZE.viewingDistanceCm, GAZE.slackDeg);
        return { halfWidthDeg: Math.max(l.halfWidthDeg, GAZE.maxHalfWidthDeg), halfHeightDeg: Math.max(l.halfHeightDeg, GAZE.maxHalfHeightDeg) };
    } catch {
        return { halfWidthDeg: 30, halfHeightDeg: 20 };
    }
}

function stageRect() {
    const stage = document.querySelector("[data-voice-stage]");
    const r = stage && stage.getBoundingClientRect();
    return r && r.width > 0 && r.height > 0 ? r : null;
}

/**
 * Where the examiner's eyes are on the page (CSS px): the point between them as drawn right now (faceShare), placed on her stage's
 * rectangle. This is the anchor dot, the first look's reference and the drift reference.
 */
function faceOf(g, r) {
    if (!r) return null;
    const f = faceShare(g, r);
    return { x: r.left + r.width * f.x, y: r.top + r.height * f.y };
}

/**
 * The point between the examiner's eyes as shares of her stage (0..1 each way), measured from what is actually rendered
 * (GAD-T509): the 3D examiner's eye bones projected through her three.js camera, or the centre of her presence's sphere while
 * the 3D head isn't shown (GAD-T694). Cached for GAZE.faceRefreshMs (a resize re-measures at once) and eased by GAZE.faceSmoothing; the old fixed
 * fraction when neither can be measured. Kept as shares so scrolling moves it with the stage for free.
 */
function faceShare(g, r) {
    const now = performance.now();
    const c = g && g.faceCache;
    const same = !!c && Math.abs(c.w - r.width) < 1 && Math.abs(c.h - r.height) < 1;
    if (same && now - c.at < GAZE.faceRefreshMs) return c;
    const m = measureFace(r) || { x: 0.5, y: GAZE.faceAnchorY, source: "stage" };
    // Eased only within one source at one size: a swap (presence to 3D) or a resize takes the new point at once.
    const ease = same && c.source === m.source;
    const k = GAZE.faceSmoothing;
    const next = {
        x: ease ? c.x + (m.x - c.x) * k : m.x,
        y: ease ? c.y + (m.y - c.y) * k : m.y,
        source: m.source, at: now, w: r.width, h: r.height,
    };
    if (g) g.faceCache = next;
    return next;
}

/** The examiner the student sees: the one in charge (voice-room.js marks it), not a 3D model still loading out of sight. */
function shownAvatar() {
    const stage = document.querySelector("[data-voice-stage]");
    if (!stage) return null;
    const all = Array.from(stage.querySelectorAll("ma-avatar"));
    return all.find((a) => a.gadflyRole === "examiner") || all.find((a) => a.gadflyRole !== "loading") || all[0] || null;
}

/** The point between her eyes as shares of the stage `r`, or null when it can't be measured (it is then the fixed fraction). */
function measureFace(r) {
    try {
        const a = shownAvatar();
        const p = a && a.mode === "3d" ? eyes3d(a) : eyesPresence();
        if (!p || !Number.isFinite(p.x) || !Number.isFinite(p.y)) return null;
        const x = (p.x - r.left) / r.width, y = (p.y - r.top) / r.height;
        // Off her stage (a camera mid-reframe, a model not yet posed) is no measurement, and neither is the student's own inset:
        // the eye-contact target is never the self-view in the stage's corner.
        if (x < 0 || x > 1 || y < 0 || y > 1 || inSelfView(p.x, p.y)) return null;
        return { x, y, source: p.source };
    } catch { return null; }
}

/** The 3D examiner: the midpoint of her eye bones (TalkingHead's own LeftEye/RightEye) through her camera, in page px. */
function eyes3d(a) {
    const h = a.head;
    const L = h && h.objectLeftEye, R = h && h.objectRightEye, cam = h && h.camera, node = h && h.nodeAvatar;
    if (!L || !R || !cam || !node || !L.matrixWorld || !R.matrixWorld) return null;
    const V = L.position.constructor;   // three.js Vector3, without importing three here
    const p = new V().setFromMatrixPosition(L.matrixWorld).add(new V().setFromMatrixPosition(R.matrixWorld)).multiplyScalar(0.5);
    p.project(cam);
    if (!(p.z > -1 && p.z < 1)) return null;   // behind the camera or beyond its far plane
    const nr = node.getBoundingClientRect();
    if (!nr.width || !nr.height) return null;
    return { x: nr.left + ((p.x + 1) / 2) * nr.width, y: nr.top + ((1 - p.y) / 2) * nr.height, source: "3d" };
}

/**
 * Her presence (GAD-T694), shown while the 3D examiner is loading or can't be drawn: the centre of the glowing sphere, which
 * live-examiner.js draws centred in the stage's .examiner-presence (the CSS orb without WebGL is centred the same way). It has no
 * eyes, so its centre is where the student looks at her; null when the presence isn't shown, and the fixed fraction applies.
 */
function eyesPresence() {
    const stage = document.querySelector("[data-voice-stage]");
    const el = stage && stage.getAttribute("data-presence") === "on" ? stage.querySelector(".examiner-presence") : null;
    const box = el && el.getBoundingClientRect();
    if (!box || !box.width || !box.height) return null;
    return { x: box.left + box.width / 2, y: box.top + box.height / 2, source: "presence" };
}

/** Whether a page point lies on the student's own camera inset (the self-view in the stage's corner). */
function inSelfView(x, y) {
    const v = document.querySelector("[data-voice-self]");
    const box = v && (v.closest(".voice-self") || v).getBoundingClientRect();
    return !!box && box.width > 0 && x >= box.left && x <= box.right && y >= box.top && y <= box.bottom;
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
        g.offPerf = null;
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
    const face = faceOf(g, stageRect());
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
    g.cal = { region: "on", since: performance.now(), candidate: null, candidateSince: 0, lastT: 0, usable: false, offScreen: false };
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
    g.offPerf = null;
    show(g, "setup");
    try { const st = document.querySelector("[data-voice-stage]"); st && st.scrollIntoView({ block: "nearest" }); } catch { }
    let r = null;
    try {
        r = await runSetup(g.lib, g.core, {
            targets: () => dotTargets(faceOf(g, stageRect())),
            captions: [say("setupCaption"), say("setupRetry")],
            skipLabel: t("examRoom.js.gaze.skip", "Skip"),
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
    // Off the screen altogether (GAD-T557): beyond the window's edges by more than the margin. Anything inside the window (the
    // transcript, the clock, the captions) is on-screen and never fires a reminder. The same hysteresis as for her stage.
    const wdx = Math.max(-pt.x, 0, pt.x - window.innerWidth);
    const wdy = Math.max(-pt.y, 0, pt.y - window.innerHeight);
    const wdeg = pxToDeg(fit, wdx, wdy, p.dist);
    c.offScreen = wdeg > (c.offScreen ? margin * GAZE.returnShare : margin);

    // Drift: while the examiner speaks, people look at her. A steady small offset from her face then is the fit's, not theirs.
    const a = g.avatar;
    if (!g.answering && a && a.dataset && a.dataset.state === "speaking") {
        const face = faceOf(g, r);
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
    // Where the gaze is (GAD-T557): on her face (her stage plus the margin), elsewhere on the screen, or off the screen. Only
    // off-screen gaze is integrity context: reading the transcript or glancing at the clock is on-screen and never "away".
    let where = null;
    if (verdict != null) {
        // A calibrated off-screen look also needs the old head-and-eye direction to have strayed (neither estimate alone decides).
        where = verdict === "on" ? "face" : g.cal.offScreen && s.ready && headShare(g, s) >= GAZE.agreeShare ? "off" : "screen";
    } else if (s.ready) {
        const w = g.winLimits || { halfWidthDeg: 30, halfHeightDeg: 20 };
        const beyond = Math.max(Math.abs(s.yawDeg) / w.halfWidthDeg, Math.abs(s.pitchDeg) / w.halfHeightDeg) > 1;
        where = s.region === "on-screen" ? "face" : beyond ? "off" : "screen";
    }
    if (g.faces !== 1) where = null;   // none, or a second face that could be the one measured: presence's business
    g.where = where;
    if (where === "off") {
        g.notOffSince = null;
        if (g.offPerf == null) g.offPerf = s.t;
    } else if (where == null) {
        g.offPerf = null;
        g.notOffSince = null;
    } else if (g.offPerf != null) {
        // Back on the screen for GAZE.switchMs ends the off-screen stretch (one stray frame doesn't).
        if (g.notOffSince == null) g.notOffSince = s.t;
        else if (s.t - g.notOffSince >= GAZE.switchMs) { g.offPerf = null; g.notOffSince = null; }
    }
}

/**
 * The student's voice (or typing) started or stopped (GAD-T557): the first word of an answer ends the thinking gap, and the end of
 * speech starts the pause a spoken reminder waits for (GAD-T561).
 */
function studentVoice(g, speaking) {
    if (G !== g || g.stopped) return;
    const now = performance.now();
    if (speaking) {
        if (!g.answering && !g.calibrating && isAnswering(g)) beginAnswer(g, now);
        if (g.answering && g.firstWordPerf == null) g.firstWordPerf = now;
        g.studentSpeaking = true;
    } else {
        if (g.studentSpeaking) g.lastSpeechEndPerf = now;
        g.studentSpeaking = false;
    }
}

/** A new answer: its own thinking gap, its own 30-second window, its own ladder (thresholds are per answer). */
function beginAnswer(g, now) {
    g.answering = true;
    g.answeringSince = Date.now();
    g.answerStartPerf = now;
    g.firstWordPerf = null;
    g.samples = [];
    g.ladder = { step: 0, at: 0, softenAt: 0, spoke: false };
}

/**
 * The answer's phase (GAD-T557, research 4.1): "thinking" from the examiner's last word to the student's first (or
 * GAZE.thinkingGapMaxMs), "speaking" after it; anything else (listening to her, her own wait) is "other". Nothing ever fires
 * outside "speaking".
 */
function phaseOf(g, now) {
    if (!g.answering) return "other";
    if (g.firstWordPerf == null && now - g.answerStartPerf < GAZE.thinkingGapMaxMs) return "thinking";
    return "speaking";
}

/** When this answer's speaking phase began (perf time). */
function speakingFrom(g) {
    return g.firstWordPerf != null ? g.firstWordPerf : g.answerStartPerf + GAZE.thinkingGapMaxMs;
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
    if (lost) g.offPerf = null;
    if (g.offPerf != null && now - g.lastSamplePerf > staleMs(g)) g.offPerf = null;
    const answering = !g.calibrating && isAnswering(g);
    if (answering && !g.answering) beginAnswer(g, now);
    else if (!answering && g.answering) { g.answering = false; restoreLadder(g, now); g.samples = []; }
    const accommodated = !!g.options.accommodation;
    const phase = phaseOf(g, now);
    // A 4-degree tracker can't afford tight thresholds: below "fair" quality every time threshold is half as long again.
    const slack = quality(g).score < GAZE.poorQualityScore ? GAZE.poorQualityFactor : 1;
    const off = g.offPerf != null;
    const offMs = off ? now - g.offPerf : 0;

    // What the quality score counts: setup warnings over time, and answering time with a usable gaze sample.
    g.stats.ticks++;
    if (g.warnings.length) g.stats.warnTicks++;
    if (answering && g.faces === 1 && !lost) {
        g.stats.answerTicks++;
        if (now - g.lastSamplePerf <= staleMs(g)) g.stats.freshTicks++;
    }

    // The speaking phase's off-screen share over the last GAZE.offScreenWindowMs (only this answer's speaking time counts).
    if (phase === "speaking" && !lost && g.where != null) {
        g.samples.push({ t: now, off });
        while (g.samples.length && now - g.samples[0].t > GAZE.offScreenWindowMs) g.samples.shift();
    }
    const share = g.samples.length ? g.samples.reduce((n, x) => n + (x.off ? 1 : 0), 0) / g.samples.length : 0;
    const offSpeakingMs = phase === "speaking" && off ? now - Math.max(g.offPerf, speakingFrom(g)) : 0;

    // Episodes, recorded only while answering and never acted on: a long off-screen look (in either phase, marked with it), a
    // reading pattern, no face, a second face.
    const active = {
        "look-away": offMs >= GAZE.episodeMs * slack ? epochOf(g.offPerf) : null,
        reading: g.cond.reading,
        "face-absent": g.cond["face-absent"],
        "second-face": g.cond["second-face"],
    };
    for (const kind of KINDS) {
        const since = answering ? active[kind] : null;
        const ep = g.episodes[kind];
        if (since != null && !ep) g.episodes[kind] = { start: Math.max(since, g.answeringSince), remarked: false, phase, step: 0 };
        else if (since == null && ep) closeEpisode(g, kind);
        else if (ep && phase === "speaking") ep.phase = "speaking";
    }
    // Presence (no face, a second face) is said out loud as before, but never in the thinking gap: leaning out of frame to think is
    // thinking too. Where the student looks is never remarked on directly; the ladder below handles off-screen gaze.
    if (phase === "speaking") {
        for (const kind of ["face-absent", "second-face"]) {
            const ep = g.episodes[kind];
            if (!ep || ep.remarked) continue;
            if (remark(g, kind)) ep.remarked = true;
            break;
        }
    }

    // The reminder ladder (GAD-T561): only while speaking, only on sustained off-screen gaze, never under the accommodation.
    const L = g.ladder;
    const ladderOn = answering && !accommodated && phase === "speaking";
    if (!ladderOn || !off) {
        if (L.step > 0) restoreLadder(g, now);
    } else if (L.step === 0) {
        if (offSpeakingMs >= GAZE.offScreenMs * slack && share > GAZE.offScreenShare) {
            L.step = 1;
            L.at = now;
            gesture(g);   // 1: her small, slow movement toward the student; nothing changes on the screen
        }
    } else if (L.step === 1) {
        if (now - L.at >= GAZE.softenAfterMs * slack && now >= g.softenCooldownUntil) {
            L.step = 2;   // 2: everything but her dims and softens a little; her face stays bright and sharp
            L.at = L.softenAt = now;
        }
    } else if (L.step === 2) {
        const pause = !g.studentSpeaking && (g.lastSpeechEndPerf == null || now - g.lastSpeechEndPerf >= GAZE.spokenPauseMs);
        if (now - L.at >= GAZE.spokenAfterMs * slack && !L.spoke && g.spokenCount < GAZE.spokenPerExam && pause && remark(g, "look-away")) {
            L.step = 3;   // 3: one short warm line at a pause, never over the student and never "look at me"
            L.spoke = true;
            g.spokenCount++;
            if (g.episodes["look-away"]) g.episodes["look-away"].remarked = true;
        }
    }
    if (g.episodes["look-away"]) g.episodes["look-away"].step = Math.max(g.episodes["look-away"].step, L.step);

    // The soften: up over GAZE.softenRampMs from step 2, never a brightening; restored quickly when the gaze returns.
    const level = L.step >= 2 ? clamp((now - L.softenAt) / GAZE.softenRampMs, 0, 1) : 0;
    if (level > 0) {
        if (!g.fade) g.fade = { start: epochOf(L.softenAt), peak: 0 };
        g.fade.peak = Math.max(g.fade.peak, level);
    } else closeFade(g);
    applyFade(g, level);

    show(g, indicatorState(g, lost));
}

/** The gaze is back on the screen (or the answer ended): the ladder starts again from nothing; a soften that showed cools down. */
function restoreLadder(g, now) {
    const L = g.ladder;
    if (L.step >= 2) g.softenCooldownUntil = now + GAZE.softenRestoreMs + GAZE.softenCooldownMs;
    L.step = 0;
    L.at = 0;
    L.softenAt = 0;
}

/**
 * Step 1 of the ladder (GAD-T561): the examiner herself re-orients the student, the best-supported cue (Bartram et al. 2003; Oh et
 * al. 2018): a slight, slow head tilt and raised brows, her eyes on the camera. No sound and no screen change. The 3D examiner moves
 * through the avatar's own animation queue; her presence gets a very small CSS swell (none when reduced motion is asked for).
 */
function gesture(g) {
    const a = shownAvatar();
    if (a && a.mode === "3d" && typeof a.animate === "function") {
        try { typeof a.holdGaze === "function" && a.holdGaze(); } catch { }
        try {
            a.animate("gadfly-reminder", [700, 900, 700], {
                headRotateZ: [0.05, 0.05, 0], headRotateX: [-0.02, -0.02, 0],
                browInnerUp: [0.35, 0.35, 0], browOuterUpLeft: [0.25, 0.25, 0], browOuterUpRight: [0.25, 0.25, 0],
            });
        } catch { /* the model isn't ready: the next steps still follow */ }
        return;
    }
    try { if (matchMedia("(prefers-reduced-motion: reduce)").matches) return; } catch { }
    const stage = document.querySelector("[data-voice-stage]");
    if (!stage) return;
    stage.setAttribute("data-gaze-nudge", "");
    clearTimeout(g.nudgeTimer);
    g.nudgeTimer = setTimeout(() => stage.removeAttribute("data-gaze-nudge"), GAZE.gestureMs);
}

/** The indicator never says "looking away" (GAD-T561): only setup, presence and whether the check is running. */
function indicatorState(g, lost) {
    if (g.setup) return "setup";
    if (g.calibrating) return g.setupOutcome === "poor" ? "calibrating-simple" : "calibrating";
    if (lost) return "lost";
    if (g.faces === 0) return "no-face";
    if (g.faces > 1) return "two-faces";
    if (g.options.accommodation) return "accommodated";
    if (!g.headpose || !g.headpose.baselineReady) return "unknown";
    return "on";
}

// ---------------------------------------------------------------------------------------------------------- quality

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
    // Codes only (GAD-T557, GAD-T561): "remarked" when she said something, "ph:t" for a look that stayed in the thinking gap (looking
    // away then is normal), "step:N" for the reminder step it reached.
    const codes = [ep.remarked ? "remarked" : "", kind === "look-away" && ep.phase === "thinking" ? "ph:t" : "",
        kind === "look-away" && ep.step > 0 ? "step:" + ep.step : ""];
    send(g, { type: "examiner-" + kind, start: ep.start, end: Date.now(), summary: codes.filter(Boolean).join(";") });
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

/**
 * The examiner says one short line: a warm, varied reminder at a pause (GAD-T561: "Take your time; I'm right here.", never "look at
 * me"), or a word about no face or a second face. The server decides the words (never the same reminder twice in a session) and
 * offers them to the avatar hub.
 */
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
            // Still answering after those round trips (GAD-T316): an answer handed in meanwhile (the silence or Done answering)
            // must not get a remark spoken over the judging wait or the next question, and a reminder never talks over the student.
            if (G !== g || !avatar.isConnected || avatar.dataset.state !== "listening") return;
            if (kind === "look-away" && g.studentSpeaking) return;
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
    if (label) label.textContent = say(state);
    const again = el.querySelector("[data-examiner-gaze-action]");
    if (again) again.hidden = !(state === "on" || state === "unknown");
}

/**
 * The peripheral soften (GAD-T561, step 2): a light dim and blur over everything except the examiner's stage, cut out of the overlay
 * with an even-odd clip path, so her face stays at full brightness and sharpness while the surroundings recede (Hata et al. 2016).
 * At most GAZE.softenMaxDim and GAZE.softenMaxBlurPx; never a brightening or a white-out.
 */
function applyFade(g, level) {
    const el = document.querySelector("[data-examiner-fade]");
    if (!el) return;
    const rising = level >= g.level;
    g.level = level;
    el.style.setProperty("--fade-ms", (rising ? GAZE.tickMs : GAZE.softenRestoreMs) + "ms");
    el.style.setProperty("--fade-dim", String(GAZE.softenMaxDim));
    el.style.setProperty("--fade-blur", GAZE.softenMaxBlurPx + "px");
    const r = level > 0 ? g.rect : null;
    if (r) {
        const box = `${r.left}px ${r.top}px, ${r.right}px ${r.top}px, ${r.right}px ${r.bottom}px, ${r.left}px ${r.bottom}px, ${r.left}px ${r.top}px`;
        el.style.setProperty("clip-path", `polygon(evenodd, 0 0, 100% 0, 100% 100%, 0 100%, 0 0, ${box})`);
    }
    el.style.opacity = String(level);
    el.dataset.active = level > 0 ? "true" : "false";
}
