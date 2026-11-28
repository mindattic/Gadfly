// The examiner shown live outside the exam room (GAD-T691): never a picture of her. <LiveExaminer> (Components/Shared) mounts the
// 3D examiner muted and idle (TalkingHead's own breathing, blinking and small eye movements), through the same <ma-avatar> and
// blue look the exam room uses (voice-room.js createAvatar, avatar-look.js). She loads only once scrolled into view (her model is
// tens of megabytes), stops drawing while off screen or in a hidden tab, and is released when the component goes away. No audio is
// ever fed to her.
//
// Under reduced motion she is still live, only calmer (GAD-T694: TalkingHead's head movement cut to a third, blinking kept).
// Until she is up, and wherever she can't be (no WebGL, a data saver, a model that fails to load), the examiner's
// presence stands in: a slowly turning blue sphere with glowing circuitry (three.js, already loaded for the avatar), or, without
// WebGL, the CSS orb under it (live-examiner.css). It is not figurative. <ExaminerPresence> mounts it alone; the exam room's
// fallback can drive it with her voice's level: `element.examinerPresence.setLevel(0..1)`.

const LIB = "/lib/voice";

function reducedMotion() {
    try { return matchMedia("(prefers-reduced-motion: reduce)").matches; } catch { return false; }
}

function webgl() {
    try {
        const c = document.createElement("canvas");
        return !!(c.getContext("webgl2") || c.getContext("webgl"));
    } catch { return false; }
}

/**
 * The model's URL to load: the browser's one cached copy when model-cache.js (GAD-T698) is there, else the plain URL, which the
 * HTTP cache still serves.
 */
export async function resolveModelUrl(url) {
    if (!url) return url;
    try {
        const m = await import("/js/model-cache.js");
        if (typeof m.getModelUrl === "function") return (await m.getModelUrl(url)) || url;
    } catch { /* not deployed yet, or no Cache Storage: the plain URL */ }
    return url;
}

/** Calls `onChange(visible)` whenever `el` comes into or leaves the viewport, or the tab is hidden or shown. */
function watchVisible(el, onChange) {
    let inView = false;
    const emit = () => onChange(inView && !document.hidden);
    const io = typeof IntersectionObserver === "function"
        ? new IntersectionObserver((entries) => { inView = entries.some((e) => e.isIntersecting); emit(); }, { rootMargin: "200px 0px" })
        : null;
    if (io) io.observe(el); else { inView = true; queueMicrotask(emit); }
    document.addEventListener("visibilitychange", emit);
    return () => { io && io.disconnect(); document.removeEventListener("visibilitychange", emit); };
}

// ---- the presence: a blue sphere with circuitry ----------------------------------------------------------------------------------

/** A seeded random source, so the circuitry is the same pattern on every page. */
function random(seed) {
    let s = seed >>> 0;
    return () => { s = (s + 0x6d2b79f5) >>> 0; let t = s; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}

/** The circuit traces on an equirectangular canvas, wrapping left to right: right-angle and 45-degree runs ending in pads. */
function circuitCanvas() {
    const W = 1024, H = 512, step = 16;
    const c = document.createElement("canvas");
    c.width = W; c.height = H;
    const g = c.getContext("2d");
    g.fillStyle = "#000";
    g.fillRect(0, 0, W, H);
    const rnd = random(691);
    const dirs = [[1, 0], [1, 1], [0, 1], [-1, 1], [-1, 0], [-1, -1], [0, -1], [1, -1]];
    g.lineCap = "round";
    g.lineJoin = "round";
    for (let i = 0; i < 140; i++) {
        const pts = [];
        let x = Math.floor(rnd() * W / step) * step;
        let y = Math.floor((0.14 + rnd() * 0.72) * H / step) * step;   // clear of the poles, where the texture pinches
        let d = Math.floor(rnd() * 4) * 2;
        pts.push([x, y]);
        const runs = 2 + Math.floor(rnd() * 4);
        for (let r = 0; r < runs; r++) {
            const len = (2 + Math.floor(rnd() * 7)) * step;
            x += dirs[d][0] * len; y += dirs[d][1] * len;
            y = Math.max(0.1 * H, Math.min(0.9 * H, y));
            pts.push([x, y]);
            d = (d + (rnd() < 0.5 ? 1 : 7)) % 8;   // turn 45 degrees either way
        }
        const width = rnd() < 0.25 ? 3 : 1.6;
        for (const off of [-W, 0, W]) {
            g.strokeStyle = "#fff";
            g.lineWidth = width;
            g.beginPath();
            pts.forEach(([px, py], k) => (k ? g.lineTo(px + off, py) : g.moveTo(px + off, py)));
            g.stroke();
            for (const [px, py] of [pts[0], pts[pts.length - 1]]) {
                g.beginPath();
                g.arc(px + off, py, width + 2.5, 0, Math.PI * 2);
                g.lineWidth = 1.6;
                g.stroke();
            }
        }
    }
    return c;
}

const VERTEX = `
varying vec2 vUv; varying vec3 vNormal; varying vec3 vView;
void main() {
    vUv = uv;
    vNormal = normalize( normalMatrix * normal );
    vec4 mv = modelViewMatrix * vec4( position, 1.0 );
    vView = -mv.xyz;
    gl_Position = projectionMatrix * mv;
}`;

const FRAGMENT = `
uniform sampler2D map; uniform vec3 base; uniform vec3 trace; uniform vec3 rim;
uniform float time; uniform float level;
varying vec2 vUv; varying vec3 vNormal; varying vec3 vView;
void main() {
    float c = texture2D( map, vUv ).r;
    float facing = clamp( dot( normalize( vNormal ), normalize( vView ) ), 0.0, 1.0 );
    float fres = pow( 1.0 - facing, 2.4 );
    // A soft shimmer travelling along the traces; still when time stands still (reduced motion).
    float shimmer = 0.6 + 0.4 * sin( time * 0.9 - vUv.x * 18.0 + vUv.y * 7.0 );
    vec3 col = base * ( 0.32 + 0.68 * facing )
        + trace * c * ( 0.55 * shimmer + 0.9 * level ) * ( 0.35 + 0.65 * facing )
        + rim * fres * ( 0.75 + 0.6 * level );
    gl_FragColor = vec4( col, 1.0 );
}`;

/**
 * How the sphere answers her voice (GAD-T840): at full level it is `scale` larger (the CSS orb's --presence-level scale matches),
 * following the level at `rise`/`fall` per second; silent, it breathes once every `breathS` seconds. Reduced motion: glow only.
 */
const PULSE = { scale: 0.12, rise: 40, fall: 16, breathS: 6, breathScale: 0.015, breathGlow: 0.08 };

/**
 * Starts the presence in `el` (an `.examiner-presence`): the WebGL sphere over the CSS orb, or the CSS orb alone without WebGL.
 * Returns `{ setLevel(0..1), dispose() }`, also kept as `el.examinerPresence`. Draws only while visible.
 */
export function mountPresence(el) {
    if (!el) return null;
    if (el.examinerPresence) return el.examinerPresence;
    const calm = reducedMotion();
    let disposed = false, visible = false, raf = 0, level = 0, shown = 0, last = 0;
    let gl = null;   // { renderer, scene, camera, sphere, uniforms, resize }

    const draw = (now) => {
        raf = 0;
        if (disposed || !gl) return;
        const dt = last ? Math.min(0.1, (now - last) / 1000) : 0;
        last = now;
        // examiner-voice.js already shapes each word's swell; this only takes the frame-to-frame edge off, quicker rising than falling.
        shown += (level - shown) * (calm ? 1 : 1 - Math.exp(-dt * (level > shown ? PULSE.rise : PULSE.fall)));
        if (calm) gl.uniforms.level.value = shown;
        else {
            gl.uniforms.time.value += dt;
            gl.sphere.rotation.y += dt * 0.12;
            // Silent, she breathes: a slow small swell and glow that gives way to her voice as it rises.
            const breath = (0.5 + 0.5 * Math.sin((gl.uniforms.time.value * 2 * Math.PI) / PULSE.breathS)) * (1 - shown);
            gl.uniforms.level.value = shown + PULSE.breathGlow * breath;
            gl.sphere.scale.setScalar(1 + PULSE.scale * shown + PULSE.breathScale * breath);
        }
        gl.renderer.render(gl.scene, gl.camera);
        if (!calm && visible) raf = requestAnimationFrame(draw);
    };
    const kick = () => { if (!raf && gl && !disposed && (visible || calm)) raf = requestAnimationFrame(draw); };

    const unwatch = watchVisible(el, (v) => {
        visible = v;
        if (v) { last = 0; kick(); } else if (raf) { cancelAnimationFrame(raf); raf = 0; }
    });

    (async () => {
        if (!webgl()) return;
        let THREE;
        try { THREE = await import("three"); } catch { return; }
        if (disposed) return;
        try {
            const canvas = document.createElement("canvas");
            canvas.className = "examiner-presence-gl";
            canvas.setAttribute("aria-hidden", "true");
            const renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: "low-power" });
            renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
            renderer.setClearColor(0x000000, 0);
            const scene = new THREE.Scene();
            const camera = new THREE.PerspectiveCamera(30, 1, 0.1, 50);
            const map = new THREE.CanvasTexture(circuitCanvas());
            map.wrapS = THREE.RepeatWrapping;
            map.anisotropy = 4;
            const uniforms = {
                map: { value: map },
                base: { value: new THREE.Color(0x1f5fd6) },
                trace: { value: new THREE.Color(0x9fe6ff) },
                rim: { value: new THREE.Color(0xb8ecff) },
                time: { value: 0 },
                level: { value: 0 },
            };
            const sphere = new THREE.Mesh(new THREE.SphereGeometry(1, 64, 48),
                new THREE.ShaderMaterial({ uniforms, vertexShader: VERTEX, fragmentShader: FRAGMENT }));
            sphere.rotation.x = 0.18;
            scene.add(sphere);
            const resize = () => {
                const w = Math.max(1, el.clientWidth), h = Math.max(1, el.clientHeight);
                renderer.setSize(w, h, false);
                camera.aspect = w / h;
                // The sphere fills about 60% of the shorter side.
                const fit = 2 / (0.6 * 2 * Math.tan((camera.fov * Math.PI) / 360) * Math.min(1, camera.aspect));
                camera.position.set(0, 0, fit);
                camera.updateProjectionMatrix();
                kick();
            };
            const ro = typeof ResizeObserver === "function" ? new ResizeObserver(resize) : null;
            ro && ro.observe(el);
            el.appendChild(canvas);
            gl = { renderer, scene, camera, sphere, uniforms, map, ro, canvas };
            resize();
            el.setAttribute("data-presence", "gl");
        } catch { gl = null; }
    })();

    const handle = {
        /** Her voice's loudness, 0 (silent) to 1: the circuitry and rim brighten with it. */
        setLevel(v) {
            level = Math.max(0, Math.min(1, Number(v) || 0));
            el.style.setProperty("--presence-level", level.toFixed(3));
            if (calm) kick();
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            unwatch();
            if (raf) cancelAnimationFrame(raf);
            if (gl) {
                try { gl.ro && gl.ro.disconnect(); } catch { }
                try { gl.sphere.geometry.dispose(); gl.sphere.material.dispose(); gl.map.dispose(); } catch { }
                try { gl.renderer.dispose(); gl.renderer.forceContextLoss(); } catch { }
                try { gl.canvas.remove(); } catch { }
                gl = null;
            }
            el.removeAttribute("data-presence");
            if (el.examinerPresence === handle) delete el.examinerPresence;
        },
    };
    el.examinerPresence = handle;
    return handle;
}

/** For Blazor: the level of the presence in `el`, when it has one. */
export function setPresenceLevel(el, v) {
    el && el.examinerPresence && el.examinerPresence.setLevel(v);
}

/** For Blazor: releases the presence in `el`. */
export function disposePresence(el) {
    el && el.examinerPresence && el.examinerPresence.dispose();
}

/** Reduced motion: TalkingHead's idle and speaking head movement cut to a third; blinking, eyes and lip sync untouched. */
export function calm(head) {
    try {
        for (const mode of ["idle", "speaking"]) {
            for (const alt of (head?.animTemplateEyes?.[mode]?.alt || [])) {
                const hm = alt.vs && alt.vs.headMove;
                if (Array.isArray(hm)) alt.vs.headMove = hm.map((v) => (typeof v === "number" ? v / 3 : v));
            }
        }
    } catch { }
}

// ---- the live examiner -----------------------------------------------------------------------------------------------------------

/**
 * Mounts the live examiner in `host` (the component's `.live-examiner`). `cfg`: { modelUrl, look: { skinTone, hair, attire, look },
 * framing ("head-and-shoulders" | "close" | "seated", over look.look), loading (the screen-reader status while she loads) }.
 * Returns `{ update(cfg), dispose() }`: `update` takes a new `{ modelUrl, look, framing }`; another model replaces her, while a new
 * look or framing restyles the one already loaded in place (no reload).
 */
export function mount(host, cfg) {
    cfg = cfg || {};
    const stage = host.querySelector(".live-examiner-stage");
    const presenceEl = host.querySelector(".examiner-presence");
    const status = host.querySelector(".live-examiner-status");
    let disposed = false, started = false, visible = false, avatar = null, presence = null, run = 0;
    const say = (text) => { if (status && status.textContent !== text) status.textContent = text; };

    const pause = () => {
        const head = avatar && avatar.head;
        if (!head) return;
        try { visible ? head.start() : head.stop(); } catch { }
    };

    const showPresence = () => {
        presence ??= mountPresence(presenceEl);
        host.setAttribute("data-mode", "presence");
    };

    const lookOf = () => {
        const look = { ...(cfg.look || {}) };
        if (cfg.framing) look.look = cfg.framing;
        return look;
    };
    let shownLook = "";

    /** Restyles the loaded model in the current look: every material's look is cleared and applied again; the camera follows. */
    const restyle = async (el) => {
        if (!el || el.mode !== "3d") return;
        const look = lookOf(), key = JSON.stringify(look);
        if (key === shownLook) return;
        try {
            const { applyExaminerLook, customLookOptions, framingOf } = await import("./avatar-look.js");
            if (disposed || avatar !== el) return;
            shownLook = key;
            el.setAttribute("view", framingOf(look).view);
            const root = el.head && el.head.armature;
            root && root.traverse((mesh) => {
                if (!mesh.isMesh) return;
                for (const m of Array.isArray(mesh.material) ? mesh.material : [mesh.material]) if (m && m.userData) m.userData.gadflyLook = false;
            });
            applyExaminerLook(el, customLookOptions(look));
        } catch { /* the look she has stays */ }
    };

    const start = async () => {
        started = true;
        const mine = ++run;
        showPresence();
        const saveData = !!(navigator.connection && navigator.connection.saveData);
        if (!cfg.modelUrl || !stage || saveData || !webgl()) return;
        say(cfg.loading || "");
        host.setAttribute("data-mode", "loading");
        try {
            const [{ useExaminerLook, customLookOptions, framingOf }, , src] = await Promise.all([
                import("./avatar-look.js"),
                import(`${LIB}/avatar/index.js`),
                resolveModelUrl(cfg.modelUrl),
            ]);
            if (disposed || mine !== run) return;
            const look = lookOf();
            shownLook = JSON.stringify(look);
            const el = document.createElement("ma-avatar");
            el.setAttribute("src", src);
            el.setAttribute("view", framingOf(look).view);
            el.setAttribute("camera-x", "0");
            el.setAttribute("aria-hidden", "true");
            el.setAttribute("motion", "always");
            useExaminerLook(el, customLookOptions(look));
            avatar = el;
            stage.appendChild(el);
            await el.ready;
            if (disposed || avatar !== el) return;
            if (el.mode !== "3d") throw new Error(el.mode);
            if (reducedMotion()) calm(el.head);
            void restyle(el);   // the look changed while she loaded
            say("");
            host.setAttribute("data-mode", "live");
            pause();
            // The sphere has done its job: release its GPU context once she has faded in over it.
            setTimeout(() => { if (!disposed && avatar === el && presence) { presence.dispose(); presence = null; } }, 700);
        } catch {
            if (disposed || mine !== run) return;
            try { avatar && avatar.remove(); } catch { }
            avatar = null;
            say("");
            host.setAttribute("data-mode", "presence");
        }
    };

    const unwatch = watchVisible(host, (v) => {
        visible = v;
        if (v && !started) void start();
        pause();
    });

    return {
        update(next) {
            if (disposed || !next) return;
            const model = (next.modelUrl || null) !== (cfg.modelUrl || null);
            cfg = { ...cfg, ...next };
            if (!model) { void restyle(avatar); return; }
            run++;
            try { avatar && avatar.remove(); } catch { }
            avatar = null;
            shownLook = "";
            say("");
            if (visible) void start(); else { started = false; showPresence(); }
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            unwatch();
            try { avatar && avatar.remove(); } catch { }   // <ma-avatar> disposes TalkingHead and its audio context when removed
            avatar = null;
            if (presence) { presence.dispose(); presence = null; }
        },
    };
}
