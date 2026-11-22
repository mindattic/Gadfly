// Eye-tracking setup and quality (GAD-T305). Shared by the get-ready page (system-check.js starts the setup check here once the
// camera shows a picture) and the exam room (examiner-gaze.js uses the gaze vector, the calibration and the stored fit).
//
// The gaze vector. MindAttic.Webcam gives, per frame, the head's position and direction in camera space (MediaPipe's facial
// transformation matrix) and where each iris sits in its eye opening. From those this file follows the line of sight to the plane
// the camera sits in (roughly the screen's plane): where the head points, plus how far the eyes are turned in the head times the
// distance (the library's own geometry, features.ts). Leaning, sitting back or turning the head moves the head point and the eye
// turn in opposite directions, so the gaze point stays put while the student keeps looking at the same spot.
//
// The calibration. Five dots (the examiner's face, or the middle of the page, and the four corners) give five pairs of "gaze point
// on the camera's plane" and "point on the page". A straight line per axis maps one to the other: it absorbs where the camera is,
// the screen's size and zoom, and the person's own eye gain. Each dot is left out in turn and predicted from the other four: that
// error, in degrees of visual angle, is the honest accuracy. Webcam gaze is coarse (Papoutsaki et al. 2016: about 4 degrees), so a
// fit worse than GAZE.calibPoorDeg is retried once and then not used: the exam room falls back to the head-direction check.
//
// Privacy: only numbers are kept. The fit (four numbers and its accuracy) stays in this tab's sessionStorage, so the exam room can
// reuse it; the setup check reads a 32x24 brightness thumbnail twice a second to tell a bright light behind the student from a dark
// face, averages it to two numbers and drops it. Nothing here is sent anywhere.
//
// This module and examiner-gaze.js import each other (GAZE lives there). Neither uses the other's exports at load time, only
// inside functions, so either may be loaded first.

import { GAZE } from "./examiner-gaze.js";

const RAD = Math.PI / 180;
const STORE = "gadfly.gaze.fit.v1";
/** The library's iris offsets (in eye-widths) for a 90 degree eye turn, sideways and up/down (headpose.ts EYE_K_X, EYE_K_Y). */
const EYE_K_X = 0.4;
const EYE_K_Y = 0.32;

const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
const round1 = (v) => Math.round(v * 10) / 10;
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

export function median(values) {
    if (!values.length) return NaN;
    const s = [...values].sort((a, b) => a - b);
    const m = s.length >> 1;
    return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
}

// ---------------------------------------------------------------------------------------------------------- gaze vector

/**
 * One face's gaze point on the camera's plane, in cm, with what it was measured from. Null when the frame can't say where the
 * eyes look: no iris points, the eyes closed (a blink), or a face too small or turned too far for the iris to be placed.
 */
export function gazePoint(lib, face) {
    const f = lib.extractFeatures(face);
    if (!f) return null;
    const open = f.right.open + f.left.open;
    if (open < GAZE.blinkOpen) return null;
    const confidence = clamp(f.scale / 0.07, 0, 1) * clamp(1.4 - Math.abs(f.yaw) * 2.5, 0, 1);
    if (confidence < GAZE.minConfidence) return null;
    // Iris offset of both eyes, measured along the eye line (which rolls with the head), turned back to the camera's axes.
    const ix = (f.right.x + f.left.x) / 2;
    const iy = (f.right.y + f.left.y) / 2;
    const cos = Math.cos(f.roll);
    const sin = Math.sin(f.roll);
    const ex = Math.asin(clamp((ix * cos - iy * sin) / EYE_K_X, -0.9, 0.9));
    const ey = Math.asin(clamp((ix * sin + iy * cos) / EYE_K_Y, -0.9, 0.9));
    const dist = clamp(f.dist, 20, 200);
    // The head point is where the face's own forward line meets the camera's plane; the eyes turn the line of sight from there.
    const x = f.hx + dist * (Math.tan(clamp(f.planeYaw + ex, -1.2, 1.2)) - Math.tan(f.planeYaw));
    const y = f.hy + dist * (Math.tan(clamp(f.planePitch + ey, -1.2, 1.2)) - Math.tan(f.planePitch));
    if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
    return {
        x, y, dist, confidence, open, scale: f.scale, px: f.px, py: f.py,
        headYawDeg: f.planeYaw / RAD, headPitchDeg: f.planePitch / RAD, eyeX: ex, eyeY: ey,
    };
}

// ---------------------------------------------------------------------------------------------------------- the fit

/** A page point (CSS px) in screen terms: moving the window between calibration and use does not move the fit. */
export function screenPoint(x, y) {
    return { x: x + (window.screenX || 0), y: y + (window.screenY || 0) };
}

/** A gaze point (cm on the camera's plane) as a point on the page (CSS px). */
export function toPage(fit, gx, gy) {
    return { x: fit.ax + fit.bx * gx - (window.screenX || 0), y: fit.ay + fit.by * gy - (window.screenY || 0) };
}

/** Degrees of visual angle for an offset of (dx, dy) CSS px on the page, seen from dist cm. */
export function pxToDeg(fit, dx, dy, dist) {
    return Math.atan(Math.hypot(dx / Math.abs(fit.bx), dy / Math.abs(fit.by)) / Math.max(20, dist)) / RAD;
}

/** Least squares X = a + b g; null when g barely moved (the fit could not tell where the eyes went). */
function line(g, X) {
    const n = g.length;
    if (n < 2) return null;
    const mg = g.reduce((s, v) => s + v, 0) / n;
    const mX = X.reduce((s, v) => s + v, 0) / n;
    let sgg = 0, sgX = 0;
    for (let i = 0; i < n; i++) { sgg += (g[i] - mg) ** 2; sgX += (g[i] - mg) * (X[i] - mX); }
    if (!(Math.sqrt(sgg / n) >= GAZE.calibMinSpreadCm)) return null;
    const b = sgX / sgg;
    const a = mX - b * mg;
    return Number.isFinite(a) && Number.isFinite(b) && b !== 0 ? { a, b } : null;
}

/** Frame-to-frame noise of the gaze direction, in degrees: the median step, so slow drift and the odd saccade don't count. */
function jitterDeg(pts) {
    if (pts.length < 3) return NaN;
    const dx = [], dy = [];
    for (let i = 1; i < pts.length; i++) {
        dx.push(Math.abs(pts[i].x / pts[i].dist - pts[i - 1].x / pts[i - 1].dist));
        dy.push(Math.abs(pts[i].y / pts[i].dist - pts[i - 1].y / pts[i - 1].dist));
    }
    // Median absolute step of a noisy constant is about 0.95 of the noise's standard deviation.
    return Math.hypot(median(dx), median(dy)) * 1.05 / RAD;
}

/**
 * Fits the page mapping from calibration targets ({X, Y} in screen terms, pts = gaze points while looking there). Returns
 * { ok, reason, fit, crossDeg, jitterDeg }: ok is false for too few usable dots ("face"), eyes that didn't move ("still"), a
 * nonsense scale ("scale") or an accuracy worse than GAZE.calibPoorDeg ("poor", with the fit for the record).
 */
export function fitTargets(targets) {
    const rows = [];
    for (const t of targets) {
        if (t.pts.length < GAZE.calibMinSamples) continue;
        rows.push({
            X: t.X, Y: t.Y,
            gx: median(t.pts.map((p) => p.x)), gy: median(t.pts.map((p) => p.y)), dist: median(t.pts.map((p) => p.dist)),
            jitter: jitterDeg(t.pts),
        });
    }
    const fail = (reason) => ({ ok: false, reason, fit: null, crossDeg: NaN, jitterDeg: NaN });
    if (rows.length < GAZE.calibMinTargets) return fail("face");
    const fx = line(rows.map((r) => r.gx), rows.map((r) => r.X));
    const fy = line(rows.map((r) => r.gy), rows.map((r) => r.Y));
    if (!fx || !fy) return fail("still");
    if ([fx.b, fy.b].some((b) => Math.abs(b) < GAZE.minPxPerCm || Math.abs(b) > GAZE.maxPxPerCm)) return fail("scale");

    // Leave each dot out, fit on the rest, predict it: the accuracy on a point the fit never saw.
    let sq = 0, n = 0;
    for (let i = 0; i < rows.length; i++) {
        const rest = rows.filter((_, k) => k !== i);
        const lx = line(rest.map((r) => r.gx), rest.map((r) => r.X));
        const ly = line(rest.map((r) => r.gy), rest.map((r) => r.Y));
        if (!lx || !ly) continue;
        const r = rows[i];
        const e = pxToDeg({ bx: lx.b, by: ly.b }, lx.a + lx.b * r.gx - r.X, ly.a + ly.b * r.gy - r.Y, r.dist);
        if (!Number.isFinite(e)) continue;
        sq += e * e;
        n++;
    }
    if (n < 3) return fail("still");
    const crossDeg = Math.sqrt(sq / n);
    const jitters = rows.map((r) => r.jitter).filter(Number.isFinite);
    const jitter = jitters.length ? median(jitters) : NaN;
    const fit = {
        v: 1, ax: fx.a, bx: fx.b, ay: fy.a, by: fy.b, crossDeg: round1(crossDeg),
        jitterDeg: Number.isFinite(jitter) ? round1(jitter) : null, at: Date.now(), dpr: window.devicePixelRatio || 1,
    };
    const poor = crossDeg > GAZE.calibPoorDeg;
    return { ok: !poor, reason: poor ? "poor" : null, fit, crossDeg: fit.crossDeg, jitterDeg: fit.jitterDeg };
}

/** Keeps a good fit for this tab (numbers only), so the exam room can reuse it. */
export function saveFit(fit) {
    try { sessionStorage.setItem(STORE, JSON.stringify(fit)); } catch { /* storage blocked: the room calibrates on its own */ }
}

export function clearFit() {
    try { sessionStorage.removeItem(STORE); } catch { }
}

/** The fit kept for this tab, if it is recent and made at the same zoom; null otherwise. */
export function loadFit() {
    try {
        const f = JSON.parse(sessionStorage.getItem(STORE) || "null");
        if (!f || f.v !== 1 || ![f.ax, f.bx, f.ay, f.by, f.crossDeg, f.at, f.dpr].every(Number.isFinite)) return null;
        if (Date.now() - f.at > GAZE.modelMaxAgeMs || Math.abs((window.devicePixelRatio || 1) - f.dpr) > 0.01) return null;
        if (f.crossDeg > GAZE.calibPoorDeg) return null;
        return f;
    } catch {
        return null;
    }
}

// ---------------------------------------------------------------------------------------------------------- the dots

/**
 * Shows the calibration dots over the page and collects gaze points for each. targets() returns page points (CSS px), read when
 * the run starts. control.cancel (set by the caller) or Escape or Skip ends it with reason "skipped"; a camera that stops
 * delivering frames ends it with reason "camera". Resolves with fitTargets' result.
 */
export async function runDots(lib, core, opts) {
    const control = opts.control || {};
    const layer = document.createElement("div");
    layer.className = "gaze-calib";
    layer.setAttribute("role", "dialog");
    layer.setAttribute("aria-label", "Eye-tracking setup");
    const dot = document.createElement("div");
    dot.className = "gaze-calib-dot";
    dot.setAttribute("aria-hidden", "true");
    const bar = document.createElement("div");
    bar.className = "gaze-calib-bar";
    const caption = document.createElement("p");
    caption.setAttribute("aria-live", "polite");
    caption.textContent = opts.caption || "";
    const skip = document.createElement("button");
    skip.type = "button";
    skip.className = "secondary";
    skip.textContent = opts.skipLabel || "Skip";
    bar.append(caption, skip);
    layer.append(dot, bar);
    document.body.appendChild(layer);

    let skipped = false;
    const onKey = (e) => { if (e.key === "Escape") skipped = true; };
    skip.addEventListener("click", () => { skipped = true; });
    window.addEventListener("keydown", onKey);
    let current = null;
    let lastFrame = performance.now();
    const off = core.subscribe((f) => {
        lastFrame = performance.now();
        if (!current || f.faces.length !== 1) return;   // no face, or a second face that could be the one measured
        const p = gazePoint(lib, f.faces[0]);
        if (p) current.pts.push(p);
    });
    const stopped = () => skipped || control.cancel;
    try {
        const targets = [];
        const points = opts.targets();
        for (const pt of points) {
            if (stopped()) break;
            dot.style.left = pt.x + "px";
            dot.style.top = pt.y + "px";
            dot.dataset.phase = "settle";
            // The caption moves out of the way of a dot that would sit under it.
            bar.dataset.alt = "";
            const b = bar.getBoundingClientRect();
            if (pt.x > b.left - 30 && pt.x < b.right + 30 && pt.y > b.top - 30 && pt.y < b.bottom + 30) bar.dataset.alt = "1";
            await wait(GAZE.calibSettleMs);
            if (stopped()) break;
            const s = screenPoint(pt.x, pt.y);
            const t = { X: s.x, Y: s.y, pts: [] };
            current = t;
            dot.dataset.phase = "collect";
            const began = performance.now();
            for (;;) {
                await wait(50);
                if (stopped()) break;
                const now = performance.now();
                if (now - lastFrame > GAZE.cameraLostMs) return { ok: false, reason: "camera", fit: null, crossDeg: NaN, jitterDeg: NaN };
                const el = now - began;
                if (el >= GAZE.calibCollectMs && (t.pts.length >= GAZE.calibMinSamples || el >= GAZE.calibTargetMaxMs)) break;
            }
            current = null;
            targets.push(t);
        }
        if (stopped()) return { ok: false, reason: "skipped", fit: null, crossDeg: NaN, jitterDeg: NaN };
        return fitTargets(targets);
    } finally {
        current = null;
        off();
        window.removeEventListener("keydown", onKey);
        layer.remove();
    }
}

/**
 * The dots, and once more if the first fit was poor (GAZE.calibAttempts in all, opts.captions[i] for each). Resolves with the
 * first good result, else the best poor one (ok false), or the skip or camera failure as it happened.
 */
export async function calibrate(lib, core, opts) {
    let best = null;
    const control = opts.control || {};
    for (let attempt = 0; attempt < GAZE.calibAttempts; attempt++) {
        if (attempt > 0) {
            await wait(GAZE.calibRetryPauseMs);
            if (control.cancel) return { ok: false, reason: "skipped", fit: null, crossDeg: NaN, jitterDeg: NaN };
        }
        const captions = opts.captions || [];
        const r = await runDots(lib, core, { ...opts, caption: captions[Math.min(attempt, captions.length - 1)] || "" });
        if (r.reason === "skipped" || r.reason === "camera") return r;
        if (r.ok) return r;
        if (!best || (r.fit && (!best.fit || r.crossDeg < best.crossDeg))) best = r;
    }
    return best;
}

/** The five dots: an anchor (the examiner's face, or the middle of the page) and the four corners, GAZE.calibEdge in. */
export function dotTargets(anchor) {
    const W = window.innerWidth, H = window.innerHeight, e = GAZE.calibEdge;
    const a = anchor && Number.isFinite(anchor.x) && Number.isFinite(anchor.y)
        ? { x: clamp(anchor.x, W * e, W * (1 - e)), y: clamp(anchor.y, H * e, H * (1 - e)) }
        : { x: W / 2, y: H / 2 };
    return [a, { x: W * e, y: H * e }, { x: W * (1 - e), y: H * e }, { x: W * (1 - e), y: H * (1 - e) }, { x: W * e, y: H * (1 - e) }];
}

/**
 * Hands the core's one getUserMedia call a clone of the live video track already shown in `video` (the page's own preview), so
 * no second camera stream is opened and stopping the core never stops the preview. Returns the function that undoes the patch.
 */
export function shareVideo(video) {
    const live = () => {
        const st = video && video.srcObject;
        return st && st.getVideoTracks && st.getVideoTracks().some((t) => t.readyState === "live") ? st : null;
    };
    const md = navigator.mediaDevices;
    if (!live() || !md || !md.getUserMedia) return () => { };
    const original = md.getUserMedia;
    const restore = () => { if (md.getUserMedia !== original) { try { delete md.getUserMedia; } catch { } if (md.getUserMedia !== original) md.getUserMedia = original; } };
    md.getUserMedia = function (constraints) {
        restore();
        const src = live();
        if (src && constraints && constraints.video && !constraints.audio) {
            return Promise.resolve(new MediaStream(src.getVideoTracks().filter((t) => t.readyState === "live").map((t) => t.clone())));
        }
        return original.call(md, constraints);
    };
    return restore;
}

// ---------------------------------------------------------------------------------------------------------- get-ready check

const TEXT = {
    title: "Eye tracking",
    intro: "During the exam your camera checks, on this device only, that you are looking at the examiner while you answer. This looks at your setup so that check works well for you. It never stops you starting.",
    loading: "Starting the camera check… The first time can take a few seconds.",
    measuring: "Look at your camera picture above for a few seconds.",
    unavailable: "The eye-tracking check couldn't run on this device. That's fine: the exam carries on and uses a simpler check.",
    setup: "Set up eye tracking (about 10 seconds)",
    setupAgain: "Set it up again",
    measureAgain: "Check my setup again",
    setupHint: "Recommended: follow a dot with your eyes so the camera learns your screen. It makes the check fairer, with fewer false \"looking away\" moments.",
    caption: "Follow the dot with your eyes and keep your head still. Esc or Skip stops.",
    retry: "Not quite precise enough. Once more: sit still and look right at the centre of each dot.",
    skipped: "Setup skipped. The exam will use a simpler check of where your head and eyes point; you can set it up here or in the exam.",
    camera: "The camera stopped during the setup. Check that it is still connected, then try again.",
    good: (deg) => `Set up: about ${deg}° accuracy. The exam room checks it once more when you start.`,
    poor: (deg) => `Not precise enough this time (about ${deg}°), so the exam will use a simpler check of where your head and eyes point. That's fine. More light on your face, or sitting still, may help if you try again.`,
    failed: "The camera couldn't follow your eyes well enough to set this up. The exam will use a simpler check of where your head and eyes point. That's fine. More light on your face may help if you try again.",
};

let Q = null;

/** Starts the get-ready setup check in `panel`, reading the camera shown in `video`. Never throws; never blocks anything. */
export async function start(video, panel) {
    stop();
    if (!video || !panel) return;
    const q = { video, panel, core: null, lib: null, off: null, result: null, setup: null, control: null, phase: "loading" };
    Q = q;
    ensureStyles();
    render(q);
    try {
        const base = new URL("../webcam/", import.meta.url);
        const lib = await bounded(import(new URL("webcam.browser.js", base).href), GAZE.loadTimeoutMs);
        if (Q !== q) return;
        q.lib = lib;
        const core = q.core = new lib.WebcamCore({
            numFaces: 2, width: 640, height: 480, measureLight: true,
            wasmBaseUrl: new URL(".", base).href.replace(/\/$/, ""),
            modelUrl: new URL("face_landmarker.bin", base).href,
        });
        const unshare = shareVideo(video);
        const opening = core.retain();
        // A start that outlives the timeout or the page section still opens the camera later: stop it then.
        // Also when this same check gave up on it (the timeout released q.core but left Q as it was): that camera was left on (GAD-T316).
        opening.then(() => { if (Q !== q || q.core !== core) { try { core.stop(); } catch { } } }, () => { });
        try { await bounded(opening, GAZE.loadTimeoutMs); } finally { unshare(); }
        if (Q !== q) return;
        await measure(q);
    } catch {
        if (Q !== q) return;
        release(q);
        q.phase = "unavailable";
        render(q);
    }
}

/** Ends the check and releases its copy of the camera. */
export function stop() {
    const q = Q;
    Q = null;
    if (!q) return;
    if (q.control) q.control.cancel = true;
    release(q);
}

function release(q) {
    try { q.off && q.off(); } catch { }
    q.off = null;
    try { q.core && q.core.stop(); } catch { }
    q.core = null;
}

function bounded(promise, ms) {
    return Promise.race([promise, new Promise((_, reject) => setTimeout(() => reject(new Error("timeout")), ms))]);
}

function ensureStyles() {
    const href = new URL("../examiner-gaze.css", import.meta.url).href;
    if ([...document.querySelectorAll('link[rel="stylesheet"]')].some((l) => l.href === href)) return;
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = href;
    document.head.appendChild(link);
}

/** Watches GAZE.checkMs of frames with a face (or gives up after GAZE.checkTimeoutMs) and judges the setup. */
async function measure(q) {
    q.phase = "measuring";
    q.result = null;
    render(q);
    const a = { frames: 0, one: 0, many: 0, oneMs: 0, lastT: 0, firstT: 0, luma: [], light: [], scale: [], yaw: [], pitch: [], px: [], py: [], pts: [], lightAt: 0 };
    const canvas = document.createElement("canvas");
    canvas.width = 32;
    canvas.height = 24;
    q.off = q.core.subscribe((f) => {
        if (Q !== q) return;
        a.frames++;
        if (!a.firstT) a.firstT = f.t;
        if (a.lastT && f.faces.length === 1) a.oneMs += Math.min(200, f.t - a.lastT);
        a.lastT = f.t;
        if (f.luma !== undefined) a.luma.push(f.luma);
        if (f.faces.length > 1) a.many++;
        if (f.faces.length !== 1) return;
        a.one++;
        const face = f.faces[0];
        const p = gazePoint(q.lib, face);
        if (p) {
            a.pts.push(p);
            a.scale.push(p.scale);
            a.yaw.push(p.headYawDeg);
            a.pitch.push(p.headPitchDeg);
            a.px.push(p.px);
            a.py.push(p.py);
        }
        if (f.t - a.lightAt >= 500) {
            a.lightAt = f.t;
            const l = faceLight(q.core.video, face, canvas);
            if (l) a.light.push(l);
        }
    });
    const began = performance.now();
    while (Q === q && a.oneMs < GAZE.checkMs && performance.now() - began < GAZE.checkTimeoutMs) await wait(200);
    try { q.off && q.off(); } catch { }
    q.off = null;
    if (Q !== q) return;
    q.result = judge(a);
    q.phase = "done";
    render(q);
}

/**
 * Brightness of the face and of the rest of the picture (0..255), from a 32x24 thumbnail read here and dropped: a bright window
 * behind a dark face is the commonest reason a webcam loses the eyes.
 */
function faceLight(video, face, canvas) {
    try {
        if (!video || !video.videoWidth) return null;
        const ctx = canvas.getContext("2d", { willReadFrequently: true });
        if (!ctx) return null;
        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
        const d = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
        const lm = face.landmarks;
        const xs = [10, 152, 234, 454].map((i) => lm[i].x), ys = [10, 152, 234, 454].map((i) => lm[i].y);
        const x0 = Math.min(...xs) * canvas.width, x1 = Math.max(...xs) * canvas.width;
        const y0 = Math.min(...ys) * canvas.height, y1 = Math.max(...ys) * canvas.height;
        let fin = 0, nin = 0, fout = 0, nout = 0;
        for (let y = 0; y < canvas.height; y++) {
            for (let x = 0; x < canvas.width; x++) {
                const i = (y * canvas.width + x) * 4;
                const v = 0.2126 * d[i] + 0.7152 * d[i + 1] + 0.0722 * d[i + 2];
                if (x >= x0 && x <= x1 && y >= y0 && y <= y1) { fin += v; nin++; } else { fout += v; nout++; }
            }
        }
        return nin && nout ? { face: fin / nin, rest: fout / nout } : null;
    } catch {
        return null;
    }
}

/** The findings, each { name, ok, result, fix }: a warning at worst, never a failure. */
function judge(a) {
    const items = [];
    const seconds = a.lastT > a.firstT ? (a.lastT - a.firstT) / 1000 : 0;
    const fps = seconds > 0 ? a.frames / seconds : 0;
    if (a.one < Math.max(10, a.frames * 0.5) || a.pts.length < 10) {
        items.push({ name: "Your face", ok: false, result: "not clearly in view most of the time", fix: "Sit facing the camera with your whole face in the picture." });
    } else {
        items.push(a.many > a.frames * 0.2
            ? { name: "Your face", ok: false, result: "more than one face in view", fix: "During the exam only you should be in the camera picture." }
            : { name: "Your face", ok: true, result: "in view" });
    }
    const luma = median(a.luma);
    const face = median(a.light.map((l) => l.face));
    const rest = median(a.light.map((l) => l.rest));
    if (Number.isFinite(luma) && luma < GAZE.minLuma) {
        items.push({ name: "Lighting", ok: false, result: "dark", fix: "Turn on a lamp in front of you (behind your screen) so it lights your face." });
    } else if (Number.isFinite(face) && Number.isFinite(rest) && rest - face > GAZE.backlightDelta) {
        items.push({ name: "Lighting", ok: false, result: "a bright light behind you", fix: "Turn so the light falls on your face, or close the blind behind you: the camera can't see your eyes against a bright background." });
    } else {
        items.push({ name: "Lighting", ok: true, result: Number.isFinite(luma) || Number.isFinite(face) ? "good" : "not measured" });
    }
    if (a.pts.length >= 10) {
        const yaw = median(a.yaw), pitch = median(a.pitch);
        items.push(Math.abs(pitch) > GAZE.maxCameraPitchDeg || Math.abs(yaw) > GAZE.maxCameraYawDeg
            ? { name: "Camera angle", ok: false, result: "steep", fix: "Put the camera at about eye level, centred at the top of your screen, and face the screen." }
            : { name: "Camera angle", ok: true, result: "good" });
        const scale = median(a.scale);
        const off = Math.max(Math.abs(median(a.px)), Math.abs(median(a.py)));
        items.push(scale < GAZE.minFaceScale
            ? { name: "Distance", ok: false, result: "far from the camera", fix: "Move a little closer, to about an arm's length." }
            : scale > GAZE.maxFaceScale
                ? { name: "Distance", ok: false, result: "very close", fix: "Sit back a little, so your whole face fits in the picture." }
                : off > GAZE.maxOffCentre
                    ? { name: "Distance", ok: false, result: "off to one side", fix: "Centre your face in the camera picture." }
                    : { name: "Distance", ok: true, result: "good" });
        const jitter = jitterDeg(a.pts);
        items.push(Number.isFinite(jitter) && jitter > GAZE.maxJitterDeg
            ? { name: "Following your eyes", ok: false, result: "unsteady", fix: "If you wear glasses, reflections on the lenses are the usual cause: tilt the screen or the lamp until you see no bright spots on them. More light on your face also helps." }
            : { name: "Following your eyes", ok: true, result: "steady" });
    }
    items.push(fps > 0 && fps < GAZE.slowFps
        ? { name: "Device speed", ok: false, result: `slow (${Math.round(fps)} frames a second)`, fix: "Closing other apps and browser tabs helps. The exam carries on either way; the check simply looks less often." }
        : { name: "Device speed", ok: true, result: fps > 0 ? "fine" : "not measured" });
    return { items, fps };
}

async function setup(q) {
    if (Q !== q || !q.core || !q.lib || q.control || q.phase === "loading") return;
    const control = q.control = { cancel: false };
    q.setup = { running: true };
    render(q);
    let r = null;
    try {
        r = await calibrate(q.lib, q.core, { targets: () => dotTargets(null), captions: [TEXT.caption, TEXT.retry], control });
    } catch { r = null; }
    if (q.control === control) q.control = null;
    if (Q !== q) return;
    if (r && r.ok) {
        saveFit(r.fit);
        q.setup = { text: TEXT.good(r.crossDeg), ok: true };
    } else if (r && r.reason === "skipped") {
        q.setup = { text: TEXT.skipped };
    } else if (r && r.reason === "camera") {
        q.setup = { text: TEXT.camera };
    } else {
        clearFit();
        q.setup = { text: r && Number.isFinite(r.crossDeg) ? TEXT.poor(r.crossDeg) : TEXT.failed };
    }
    render(q);
}

function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
}

function render(q) {
    const p = q.panel;
    if (!p || !p.isConnected) return;
    p.replaceChildren();
    p.append(el("h3", "gaze-quality-title", TEXT.title), el("p", "hint", TEXT.intro));
    const list = el("ul", "syschecks");
    const row = (status, name, result, fix) => {
        const li = el("li", "syscheck syscheck-" + status);
        li.append(el("span", "syscheck-mark", status === "pass" ? "✓" : status === "warn" ? "!" : "…"));
        li.lastChild.setAttribute("aria-hidden", "true");
        const body = el("div");
        body.append(el("strong", null, name), document.createTextNode(" — " + result));
        if (fix) body.append(el("div", "hint", fix));
        li.append(body);
        list.append(li);
    };
    if (q.phase === "loading") row("later", "Setup", TEXT.loading);
    else if (q.phase === "measuring") row("later", "Setup", TEXT.measuring);
    else if (q.phase === "unavailable") row("warn", "Setup", TEXT.unavailable);
    else if (q.result) for (const i of q.result.items) row(i.ok ? "pass" : "warn", i.name, i.result, i.ok ? null : i.fix);
    p.append(list);
    if (q.phase === "done" && q.core) {
        const actions = el("div", "gaze-quality-actions");
        const go = el("button", null, q.setup && q.setup.ok ? TEXT.setupAgain : TEXT.setup);
        go.type = "button";
        go.disabled = !!q.control;
        go.addEventListener("click", () => { void setup(q); });
        const again = el("button", "link", TEXT.measureAgain);
        again.type = "button";
        again.disabled = !!q.control;
        again.addEventListener("click", () => { if (Q === q && !q.control && q.core) void measure(q); });
        actions.append(go, again);
        p.append(actions);
        const note = el("p", q.setup && q.setup.text ? "gaze-quality-result" : "hint", q.setup && q.setup.text ? q.setup.text : TEXT.setupHint);
        note.setAttribute("role", "status");
        p.append(note);
    }
}
