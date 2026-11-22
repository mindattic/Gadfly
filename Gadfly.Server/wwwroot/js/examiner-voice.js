// The examiner's delivery (GAD-T303): everything about how she looks and sounds while she speaks that the avatar library
// (<ma-avatar>, MindAttic.Avatar) does not do itself. voice-room.js owns the exam flow and calls into this module; this module owns:
//
//   - Word-synced captions: the whole question is shown at once, dimmed, and each word lights up as she says it. Timed from the
//     provider's own word timings (ElevenLabs character alignment, assembled into words by the library), counted from the moment
//     her audio actually starts, on a clock that stops while she is paused.
//   - Speaking style: a fixed server-side style per kind of question (Voice/ExaminerStyle.cs) played as a small head tilt and brow
//     lift on the 3D head. No style carries valence: all share the same mild baseline, so her face never hints at the grade.
//   - The 2D portrait's mouth: in portrait mode a mouth drawn over the picture opens and closes with the same word timings (one
//     opening per syllable), so the fallback still visibly speaks.
//   - Quality tiers: the 3D examiner's frame rate is watched and, on a slow device, her render resolution and animation rate are
//     lowered step by step; if it is still too slow she becomes the portrait (between utterances, never mid-sentence). The tier is
//     remembered for this tab, so a reload starts where it ended.
//   - Graphics failures: a lost WebGL context shows the portrait at once (her voice carries on) and finishes the switch when she
//     next stops speaking.
//   - Pause and resume: hiding the tab mid-sentence pauses her voice, mouth and captions together, and showing it resumes them.
//   - Speech fallback: when the voice provider fails, the browser's own speech reads the question (captions and the mouth follow
//     its word boundaries); with no browser speech the question stays on screen as text.
//
// Gaps in the library that this works around are filed as MindAttic.Avatar tickets (see docs/VOICE.md).

const STYLE_FADE_MS = 450;
const TIER_KEY = "gadfly.examiner.tier";
// Each tier: render resolution (share of the device pixel ratio, capped) and animation frames per second.
const TIERS = [
    { name: "high", pixelRatio: 1, maxRatio: 2, fps: 30 },
    { name: "medium", pixelRatio: 1, maxRatio: 1, fps: 30 },
    { name: "low", pixelRatio: 0.75, maxRatio: 0.75, fps: 24 },
];
const FPS = { windowMs: 3000, slowBelow: 22, portraitBelow: 12, slowWindows: 2, settleMs: 4000 };

const STYLES = {
    // The shared baseline every style keeps: a mild, friendly face. Only tilt and brow lift differ between styles.
    neutral: { mouthSmile: 0.07 },
    open: { mouthSmile: 0.07, browInnerUp: 0.22, browOuterUpLeft: 0.12, browOuterUpRight: 0.12, headRotateZ: 0.045 },
    probe: { mouthSmile: 0.07, browInnerUp: 0.1, eyeSquintLeft: 0.08, eyeSquintRight: 0.08, headRotateX: 0.035, headRotateZ: -0.04 },
};

const now = () => performance.now();
const safe = (fn) => { try { return fn(); } catch { return undefined; } };

/**
 * Wires the delivery helpers to one examiner. `avatar` is the <ma-avatar> element (or null when the avatar failed: captions still
 * work), `stage` its container, `caption` the caption element. `cfg.portraitMouth` is "x,y,w" (fractions of the portrait) or null.
 * `onPortrait(reason)` is called when this module switches the examiner to the portrait (slow device, lost graphics).
 */
export function createPresenter(avatar, stage, caption, cfg, onPortrait) {
    const p = {
        avatar, stage, caption, cfg: cfg || {}, onPortrait: onPortrait || (() => { }),
        tokens: [], spans: [], words: [], lit: -1,
        clock: { running: false, startedAt: 0, pausedAt: 0, pausedTotal: 0, paused: false },
        raf: 0, mouth: null, mouthGeo: null, fallbackActive: false, tier: readTier(), fps: null, portraitPending: null, disposed: false,
        listeners: [],
    };
    const on = (target, type, fn, opts) => { if (!target) return; target.addEventListener(type, fn, opts); p.listeners.push(() => target.removeEventListener(type, fn, opts)); };

    on(document, "visibilitychange", () => { if (document.hidden) pause(p, "hidden"); else resume(p, "hidden"); });
    // Made while the tab is hidden (the 3D examiner taking over from the portrait in a background tab, or a page opened there):
    // starts paused, or her next question played unheard behind the tab and showing it again resumed nothing (GAD-T316).
    if (document.hidden) pause(p, "hidden");
    on(window, "resize", () => { p.mouthGeo = null; });
    // The stage also changes size without the window doing so: it fills the height the exam room leaves it (GAD-T416).
    if (stage && typeof ResizeObserver === "function") {
        const ro = new ResizeObserver(() => { p.mouthGeo = null; });
        ro.observe(stage);
        p.listeners.push(() => ro.disconnect());
    }
    if (avatar) {
        on(avatar, "fallback", () => { p.mouthGeo = null; stopFps(p); });
        // The library's 'ready' fires for the 3D model and for the portrait alike.
        on(avatar, "ready", () => afterReady(p));
        if (avatar.mode && avatar.mode !== "loading") afterReady(p);
    }

    return {
        /** A new utterance is about to be asked: show its words dimmed and set her speaking style. */
        prepare: (text, style) => prepare(p, text, style),
        /** Word timings from the hub (`[{ word, startMs, durationMs }]`, ms from the start of the utterance). */
        words: (words) => addWords(p, words),
        /** Her audio has actually started: the caption and mouth clock starts now. */
        started: () => startClock(p),
        /** She has finished (or was stopped): every word lit, mouth closed, style eased off. */
        ended: () => endUtterance(p),
        /** The whole text at once, without word lighting (no timings, or the avatar is off). */
        plain: (text) => plainCaption(p, text),
        pause: (why) => pause(p, why || "app"),
        resume: (why) => resume(p, why || "app"),
        /** The browser's own speech reads `text` (provider failure). Resolves when done; false if the browser has no speech. */
        speakWithBrowser: (text) => speakWithBrowser(p, text),
        /** Stops a browser-speech fallback that is running. */
        stopBrowser: () => stopBrowser(p),
        /** Called between utterances: completes a pending switch to the portrait. */
        idle: () => completePortrait(p),
        get tier() { return p.fallbackActive ? "portrait" : TIERS[p.tier]?.name; },
        dispose: () => dispose(p),
    };
}

// --------------------------------------------------------------------------------------------------------------- captions

function prepare(p, text, style) {
    p.words = [];
    p.lit = -1;
    resetClock(p);
    renderCaption(p, text || "");
    applyStyle(p, style || "neutral", estimateMs(text));
}

function renderCaption(p, text) {
    const el = p.caption;
    p.tokens = (text || "").split(/\s+/).filter(Boolean);
    p.spans = [];
    if (!el) return;
    el.replaceChildren();
    el.dataset.timed = "true";
    p.tokens.forEach((t, i) => {
        if (i > 0) el.appendChild(document.createTextNode(" "));
        const s = document.createElement("span");
        s.className = "voice-word";
        s.textContent = t;
        el.appendChild(s);
        p.spans.push(s);
    });
}

function plainCaption(p, text) {
    stopLoop(p);
    p.tokens = []; p.spans = []; p.words = [];
    if (p.caption) { p.caption.dataset.timed = "false"; p.caption.textContent = text || ""; }
    setMouth(p, 0);
}

function addWords(p, words) {
    if (!Array.isArray(words)) return;
    for (const w of words) {
        const start = Number(w && w.startMs), dur = Number(w && w.durationMs);
        if (!Number.isFinite(start)) continue;
        p.words.push({ word: String(w.word || ""), startMs: start, durationMs: Number.isFinite(dur) && dur > 0 ? dur : 250 });
    }
    if (p.clock.running) loop(p);
}

function light(p, upTo) {
    if (upTo <= p.lit) return;
    for (let i = Math.max(0, p.lit + 1); i <= upTo && i < p.spans.length; i++) p.spans[i].classList.add("voice-word-said");
    p.lit = upTo;
}

function endUtterance(p) {
    light(p, p.spans.length - 1);
    stopLoop(p);
    setMouth(p, 0);
    p.clock.running = false;
    applyStyle(p, "neutral", 0);
    completePortrait(p);
}

// ------------------------------------------------------------------------------------------------------------ the clock

function resetClock(p) {
    stopLoop(p);
    p.clock = { running: false, startedAt: 0, pausedAt: 0, pausedTotal: 0, paused: false };
}

function startClock(p) {
    if (p.clock.running) return;
    p.clock.running = true;
    p.clock.startedAt = now();
    p.clock.pausedTotal = 0;
    if (p.clock.paused) p.clock.pausedAt = now();
    loop(p);
}

function elapsed(p) {
    const c = p.clock;
    if (!c.running) return 0;
    const end = c.paused ? c.pausedAt : now();
    return Math.max(0, end - c.startedAt - c.pausedTotal);
}

function loop(p) {
    if (p.raf || p.disposed) return;
    const tick = () => {
        p.raf = 0;
        if (p.disposed || !p.clock.running) return;
        const t = elapsed(p);
        let upTo = -1, open = 0;
        for (let i = 0; i < p.words.length; i++) {
            const w = p.words[i];
            if (w.startMs > t) break;
            upTo = i;
            if (t < w.startMs + w.durationMs) open = mouthOpen(w, t);
        }
        light(p, upTo);
        setMouth(p, open);
        if (!p.clock.paused) p.raf = requestAnimationFrame(tick);
    };
    p.raf = requestAnimationFrame(tick);
}

function stopLoop(p) {
    if (p.raf) cancelAnimationFrame(p.raf);
    p.raf = 0;
}

/** How open the portrait's mouth is at time t inside word w: one opening per syllable, eased at the edges of the word. */
function mouthOpen(w, t) {
    const syl = syllables(w.word);
    const x = Math.min(1, Math.max(0, (t - w.startMs) / w.durationMs));
    const edge = Math.min(1, x * 6, (1 - x) * 6);
    return Math.min(1, 0.25 + 0.75 * Math.abs(Math.sin(Math.PI * syl * x))) * edge;
}

function syllables(word) {
    const m = String(word || "").toLowerCase().match(/[aeiouy]+/g);
    return Math.max(1, m ? m.length : 1);
}

function estimateMs(text) {
    return Math.max(1500, String(text || "").length * 65);
}

// ------------------------------------------------------------------------------------------------------- pause / resume

/** Pauses her voice, lip sync, captions and mouth together. Reasons stack: she resumes when every reason has gone. */
function pause(p, why) {
    p.pauseReasons ??= new Set();
    p.pauseReasons.add(why);
    if (p.clock.paused) return;
    p.clock.paused = true;
    p.clock.pausedAt = now();
    stopLoop(p);
    const head = p.avatar && p.avatar.head;
    if (head) safe(() => head.stop());   // TalkingHead: stops the animation clock and suspends its audio, so both hold in step
    else if (p.avatar && p.avatar.ctx) safe(() => p.avatar.ctx.suspend());
    if (p.fallbackActive && window.speechSynthesis) safe(() => window.speechSynthesis.pause());
}

function resume(p, why) {
    if (!p.pauseReasons) return;
    p.pauseReasons.delete(why);
    if (p.pauseReasons.size > 0 || !p.clock.paused) return;
    p.clock.pausedTotal += now() - p.clock.pausedAt;
    p.clock.paused = false;
    const head = p.avatar && p.avatar.head;
    if (head) safe(() => head.start());
    else if (p.avatar && p.avatar.ctx) safe(() => p.avatar.ctx.resume());
    if (p.fallbackActive && window.speechSynthesis) safe(() => window.speechSynthesis.resume());
    if (p.clock.running) loop(p);
}

// --------------------------------------------------------------------------------------------------------- speaking style

/** Holds a style on the 3D head for about the utterance, easing in and out. Portrait and no-avatar: nothing to do. */
function applyStyle(p, style, ms) {
    const head = p.avatar && p.avatar.head;
    if (!head || !head.animQueue || !head.animFactory) return;
    const target = STYLES[style] || STYLES.neutral;
    const vs = {};
    for (const [k, v] of Object.entries(target)) vs[k] = [v, v, 0];
    safe(() => {
        const old = head.animQueue.findIndex((a) => a.template && a.template.name === "gadfly-style");
        if (old !== -1) head.animQueue.splice(old, 1);
        if (ms > 0) head.animQueue.push(head.animFactory({ name: "gadfly-style", dt: [STYLE_FADE_MS, ms, STYLE_FADE_MS], vs }));
    });
}

// ---------------------------------------------------------------------------------------------------------- portrait mouth

function ensureMouth(p) {
    if (p.mouth || !p.stage || !p.cfg.portraitMouth) return p.mouth;
    const m = document.createElement("div");
    m.className = "voice-mouth";
    m.setAttribute("aria-hidden", "true");
    p.stage.appendChild(m);
    p.mouth = m;
    return m;
}

/** Where the mouth is on screen: the portrait is scaled and centred in the stage, with object-fit cover (the library's default) or
 * contain (the 16:9 exam room shows the whole portrait, GAD-T501). */
function mouthGeometry(p) {
    if (p.mouthGeo) return p.mouthGeo;
    const parts = String(p.cfg.portraitMouth || "").split(",").map(Number);
    const img = p.avatar && p.avatar.shadowRoot && p.avatar.shadowRoot.querySelector(".portrait");
    if (parts.length !== 3 || parts.some((x) => !Number.isFinite(x)) || !img || !img.naturalWidth || !p.stage) return null;
    const W = p.avatar.clientWidth, H = p.avatar.clientHeight, w = img.naturalWidth, h = img.naturalHeight;
    if (!W || !H) return null;
    const contain = safe(() => getComputedStyle(img).objectFit) === "contain";
    const sc = contain ? Math.min(W / w, H / h) : Math.max(W / w, H / h);
    const ox = (W - w * sc) / 2 + p.avatar.offsetLeft, oy = (H - h * sc) / 2 + p.avatar.offsetTop;
    const width = parts[2] * w * sc;
    p.mouthGeo = { x: ox + parts[0] * w * sc, y: oy + parts[1] * h * sc, w: width, h: width * 0.45 };
    return p.mouthGeo;
}

function setMouth(p, open) {
    const portrait = p.avatar && p.avatar.mode === "portrait";
    if (!portrait) { if (p.mouth) p.mouth.dataset.open = "false"; return; }
    const m = ensureMouth(p);
    if (!m) return;
    const g = mouthGeometry(p);
    if (!g) return;
    m.style.left = `${g.x - g.w / 2}px`;
    m.style.top = `${g.y - g.h / 2}px`;
    m.style.width = `${g.w}px`;
    m.style.height = `${g.h}px`;
    m.style.setProperty("--open", String(Math.max(0, Math.min(1, open))));
    m.dataset.open = open > 0.02 ? "true" : "false";
}

// ---------------------------------------------------------------------------------------------------- quality and failures

function readTier() {
    const t = Number(safe(() => sessionStorage.getItem(TIER_KEY)));
    return Number.isInteger(t) && t >= 0 && t < TIERS.length ? t : 0;
}

function saveTier(t) { safe(() => sessionStorage.setItem(TIER_KEY, String(t))); }

function afterReady(p) {
    const el = p.avatar;
    if (!el || p.disposed) return;
    p.mouthGeo = null;
    if (el.mode !== "3d" || !el.head) return;
    applyTier(p, p.tier);
    // A lost WebGL context (driver reset, GPU memory pressure, too many tabs): the portrait shows at once and her voice goes on.
    const canvas = el.shadowRoot && el.shadowRoot.querySelector("canvas");
    if (canvas && !canvas.dataset.gadflyWatched) {
        canvas.dataset.gadflyWatched = "true";
        canvas.addEventListener("webglcontextlost", (e) => { safe(() => e.preventDefault()); toPortrait(p, "graphics-lost"); });
    }
    startFps(p);
}

function applyTier(p, t) {
    const head = p.avatar && p.avatar.head;
    const tier = TIERS[t];
    if (!head || !tier) return;
    const ratio = Math.min(tier.maxRatio, tier.pixelRatio * (window.devicePixelRatio || 1));
    safe(() => {
        if (head.opt) { head.opt.modelPixelRatio = ratio / (window.devicePixelRatio || 1); head.opt.modelFPS = tier.fps; }
        head.animFrameDur = 1000 / tier.fps;
        if (head.renderer) head.renderer.setPixelRatio(ratio);
    });
}

/** Watches the page's frame rate while the 3D examiner is up; steps the tier down on a sustained slow rate. */
function startFps(p) {
    stopFps(p);
    const f = { frames: 0, since: now(), slow: 0, settleUntil: now() + FPS.settleMs, raf: 0 };
    p.fps = f;
    const frame = () => {
        if (p.fps !== f || p.disposed) return;
        f.raf = requestAnimationFrame(frame);
        if (document.hidden || p.clock.paused) { f.frames = 0; f.since = now(); return; }
        f.frames++;
        const span = now() - f.since;
        if (span < FPS.windowMs) return;
        const rate = f.frames * 1000 / span;
        f.frames = 0; f.since = now();
        if (now() < f.settleUntil) return;
        if (rate >= FPS.slowBelow) { f.slow = 0; return; }
        if (++f.slow < FPS.slowWindows) return;
        f.slow = 0;
        f.settleUntil = now() + FPS.settleMs;
        if (p.tier < TIERS.length - 1) { p.tier++; saveTier(p.tier); applyTier(p, p.tier); }
        else if (rate < FPS.portraitBelow) { saveTier(p.tier); toPortrait(p, "slow-device"); }
    };
    f.raf = requestAnimationFrame(frame);
}

function stopFps(p) {
    if (p.fps && p.fps.raf) cancelAnimationFrame(p.fps.raf);
    p.fps = null;
}

/**
 * Makes the examiner the portrait. Visually at once (the portrait is shown over the 3D stage); the 3D head itself is released only
 * when she is not speaking, so a sentence in progress is never cut off.
 */
function toPortrait(p, reason) {
    const el = p.avatar;
    if (!el || el.mode !== "3d" || p.portraitPending) return;
    p.portraitPending = reason;
    stopFps(p);
    el.dataset.mode = "portrait";   // the library's styles show the portrait image and hide the 3D stage
    p.mouthGeo = null;
    p.onPortrait(reason);
    if (el.state !== "speaking") completePortrait(p);
}

function completePortrait(p) {
    const el = p.avatar;
    const reason = p.portraitPending;
    if (!el || !reason || el.state === "speaking") return;
    p.portraitPending = null;
    const head = el.head;
    const ctx = head && head.audioCtx;
    safe(() => el.fallback(reason));   // the library's own portrait mode: later speech plays through its plain audio path
    safe(() => head && head.dispose());
    safe(() => { if (ctx && ctx.state !== "closed") void ctx.close().catch(() => { }); });
}

// -------------------------------------------------------------------------------------------------------- browser speech

function englishVoice() {
    const voices = safe(() => window.speechSynthesis.getVoices()) || [];
    return voices.find((v) => /^en(-|_)/i.test(v.lang) && v.localService) || voices.find((v) => /^en/i.test(v.lang)) || null;
}

/**
 * Reads `text` with the browser's own speech when the voice provider failed. The caption lights each word at its boundary event
 * and the portrait mouth moves with it. Resolves true when it finished, false when the browser has no speech (the text stays on
 * screen). Bounded: a speech engine that never reports the end can't hold the exam.
 */
function speakWithBrowser(p, text) {
    const synth = window.speechSynthesis;
    if (!synth || typeof window.SpeechSynthesisUtterance !== "function" || !text) return Promise.resolve(false);
    stopBrowser(p);
    renderCaption(p, text);
    p.fallbackActive = true;
    return new Promise((resolve) => {
        const u = new SpeechSynthesisUtterance(text);
        u.lang = "en-US";
        const v = englishVoice();
        if (v) u.voice = v;
        u.rate = 1;
        // Character offsets of each caption word, to map boundary events onto words.
        const offsets = [];
        let at = 0;
        for (const t of p.tokens) { const i = text.indexOf(t, at); offsets.push(i < 0 ? at : i); at = (i < 0 ? at : i) + t.length; }
        let mouthTimer = 0;
        const finish = (ok) => {
            clearTimeout(guard);
            clearTimeout(mouthTimer);
            if (!p.fallbackActive) return resolve(ok);
            p.fallbackActive = false;
            light(p, p.spans.length - 1);
            setMouth(p, 0);
            resolve(ok);
        };
        u.onboundary = (e) => {
            if (e.name && e.name !== "word") return;
            let k = 0;
            while (k + 1 < offsets.length && offsets[k + 1] <= e.charIndex) k++;
            light(p, k);
            const word = p.tokens[k] || "";
            const ms = 140 + 55 * word.length, start = now();
            clearTimeout(mouthTimer);
            const step = () => {
                const t = now() - start;
                if (t >= ms || !p.fallbackActive) { setMouth(p, 0); return; }
                setMouth(p, mouthOpen({ word, startMs: 0, durationMs: ms }, t));
                mouthTimer = setTimeout(step, 33);
            };
            step();
        };
        u.onend = () => finish(true);
        u.onerror = () => finish(false);
        const guard = setTimeout(() => { safe(() => synth.cancel()); finish(true); }, 15000 + 90 * text.length);
        p.browserUtterance = u;
        safe(() => synth.speak(u));
    });
}

function stopBrowser(p) {
    if (!p.fallbackActive) return;
    p.fallbackActive = false;
    safe(() => window.speechSynthesis.cancel());
    setMouth(p, 0);
}

function dispose(p) {
    p.disposed = true;
    stopLoop(p);
    stopFps(p);
    stopBrowser(p);
    p.listeners.forEach((off) => safe(off));
    p.listeners = [];
    safe(() => p.mouth && p.mouth.remove());
    p.mouth = null;
}
