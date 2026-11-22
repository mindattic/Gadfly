// Guided tours (GAD-T293). A small, dependency-free spotlight engine driven by Components/Shared/Tour.razor, which registers
// one tour per page (steps from Tours/TourCatalog.cs). Each step's callout points at an element with data-tour="name" (or a
// CSS selector); a step whose element is not on the page is skipped. Next, Back, Skip and Done buttons, Esc closes, the
// arrow keys step, focus stays in the callout and goes back where it was afterwards, and every step is announced through
// an aria-live region. Motion is off when the reader asks for reduced motion.
//
// "Shown once": a tour starts by itself the first time an account sees the page, remembered in localStorage under the
// account's id, the tour id and its version. Storage can be missing or refuse (private windows, blocked site data), so
// every access is guarded; without it a tour may simply show again. A page address with ?tour=<id> starts the tour at once
// (the replay links in Help). The engine never runs in the exam room or the practice interview: it touches no camera,
// microphone or system check, only the page's layout.

const tours = new Map();
let active = null;
let queryConsumed = false;

const STORAGE_PREFIX = "gadfly.tour.";
const MARGIN = 12;   // px kept between the callout and the edge of the window
const GAP = 12;      // px between the highlighted element and the callout

function reducedMotion() {
    try { return window.matchMedia("(prefers-reduced-motion: reduce)").matches; } catch { return false; }
}

/** The exam room (/exam/{id}) and the practice interview never show a tour: nothing may distract or cover the examiner. */
function forbiddenHere() {
    const path = (location.pathname || "").toLowerCase();
    return /^\/exam(\/|$)/.test(path) || /^\/my-exams\/practice(\/|$)/.test(path);
}

function storageKey(def) { return `${STORAGE_PREFIX}${def.userKey || "anonymous"}.${def.id}.v${def.version}`; }

function wasShown(def) {
    try { return window.localStorage.getItem(storageKey(def)) === "1"; } catch { return false; }
}

function markShown(def) {
    try { window.localStorage.setItem(storageKey(def), "1"); } catch { /* storage unavailable: the tour may show again */ }
}

function ensureStylesheet() {
    try {
        if (document.querySelector('link[data-tour-css]')) return;
        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.href = "tour.css";
        link.setAttribute("data-tour-css", "");
        document.head.appendChild(link);
    } catch { /* the tour still works, unstyled */ }
}

/** Registers a page's tour (called by Tour.razor once the page has rendered) and starts it when it should. */
export function register(def) {
    if (!def || !def.id || !Array.isArray(def.steps)) return;
    ensureStylesheet();
    tours.set(def.id, def);
    if (forbiddenHere()) return;

    let requested = null;
    if (!queryConsumed) {
        try { requested = new URLSearchParams(location.search).get("tour"); } catch { requested = null; }
    }
    const replay = requested && requested.toLowerCase() === def.id.toLowerCase();
    if (replay) queryConsumed = true;
    if (!replay && (!def.auto || wasShown(def))) return;

    const delay = Math.max(0, Number(def.delay) || 0);
    def._timer = window.setTimeout(() => {
        def._timer = 0;
        if (tours.get(def.id) === def && !active) start(def.id);
    }, replay ? Math.min(delay, 900) : delay);
}

/** Forgets a page's tour when the page goes away, closing it if it is showing. */
export function unregister(id) {
    const def = tours.get(id);
    if (def && def._timer) window.clearTimeout(def._timer);
    tours.delete(id);
    if (active && active.def.id === id) close(false);
}

/** Starts (or restarts) a tour from its first step. Returns false when it cannot run here. */
export function start(id) {
    const def = tours.get(id);
    if (!def || forbiddenHere()) return false;
    if (active) close(false);
    markShown(def);

    const returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const ui = build(def);
    active = { def, ui, index: -1, returnFocus, raf: 0 };

    window.addEventListener("resize", onViewportChange);
    window.addEventListener("scroll", onViewportChange, true);
    document.addEventListener("keydown", onKeyDown, true);
    document.addEventListener("focusin", onFocusIn, true);

    if (!go(0, 1)) { close(false); return false; }
    return true;
}

/** Ends the tour that is showing, if any. */
export function stop() { if (active) close(false); }

function build(def) {
    const root = document.createElement("div");
    root.className = "tour-root";
    if (reducedMotion()) root.classList.add("tour-still");

    const blocker = document.createElement("div");   // keeps clicks off the page while the tour is open
    blocker.className = "tour-blocker";
    blocker.addEventListener("click", () => active && active.ui.next.focus());

    const spot = document.createElement("div");
    spot.className = "tour-spot";
    spot.setAttribute("aria-hidden", "true");

    const box = document.createElement("div");
    box.className = "tour-callout";
    box.setAttribute("role", "dialog");
    box.setAttribute("aria-modal", "true");
    const uid = "tour-" + Math.random().toString(36).slice(2, 8);
    box.setAttribute("aria-labelledby", uid + "-title");
    box.setAttribute("aria-describedby", uid + "-body");

    const head = document.createElement("div");
    head.className = "tour-head";
    const count = document.createElement("span");
    count.className = "tour-count";
    const skip = document.createElement("button");
    skip.type = "button";
    skip.className = "tour-skip";
    skip.textContent = "Skip tour";
    skip.addEventListener("click", () => close(true));
    head.append(count, skip);

    const title = document.createElement("h2");
    title.className = "tour-title";
    title.id = uid + "-title";
    title.tabIndex = -1;
    const body = document.createElement("p");
    body.className = "tour-body";
    body.id = uid + "-body";

    const nav = document.createElement("div");
    nav.className = "tour-nav";
    const back = document.createElement("button");
    back.type = "button";
    back.className = "tour-back";
    back.textContent = "Back";
    back.addEventListener("click", () => step(-1));
    const next = document.createElement("button");
    next.type = "button";
    next.className = "tour-next";
    next.addEventListener("click", () => step(1));
    nav.append(back, next);

    box.append(head, title, body, nav);

    const live = document.createElement("div");
    live.className = "visually-hidden tour-live";
    live.setAttribute("aria-live", "polite");
    live.setAttribute("aria-atomic", "true");

    root.append(blocker, spot, box, live);
    document.body.appendChild(root);
    return { root, spot, box, count, title, body, back, next, skip, live };
}

/** The element a step points at: the first visible match, or null (a centred step, or a missing element). */
function resolve(anchor) {
    if (!anchor) return null;
    let list;
    try {
        list = /^[A-Za-z0-9_-]+$/.test(anchor)
            ? document.querySelectorAll(`[data-tour="${anchor}"]`)
            : document.querySelectorAll(anchor);
    } catch { return null; }
    for (const el of list) {
        if (active && active.ui.root.contains(el)) continue;
        const r = el.getBoundingClientRect();
        if (r.width > 0 && r.height > 0 && getComputedStyle(el).visibility !== "hidden") return el;
    }
    return null;
}

/** The steps that can be shown right now: centred ones, and those whose element is on the page. */
function available(def) {
    return def.steps.map((s, i) => (!s.anchor || resolve(s.anchor)) ? i : -1).filter(i => i >= 0);
}

/** Moves to step `from`, or the nearest showable one in `direction`. False when there is none. */
function go(from, direction) {
    const def = active.def;
    for (let i = from; i >= 0 && i < def.steps.length; i += direction) {
        const s = def.steps[i];
        if (!s.anchor || resolve(s.anchor)) { show(i); return true; }
    }
    return false;
}

function step(direction) {
    if (!active) return;
    const moved = go(active.index + direction, direction);
    if (!moved && direction > 0) close(true);   // past the last showable step: done
}

function show(index) {
    const { def, ui } = active;
    active.index = index;
    const s = def.steps[index];
    const showable = available(def);
    const pos = Math.max(0, showable.indexOf(index)) + 1;
    const total = Math.max(showable.length, pos);
    const last = !showable.some(i => i > index);
    const first = !showable.some(i => i < index);

    ui.count.textContent = `${def.title} · ${pos} of ${total}`;
    ui.title.textContent = s.title;
    ui.body.textContent = s.body;
    ui.back.disabled = first;
    ui.next.textContent = last ? "Done" : "Next";
    ui.live.textContent = `Step ${pos} of ${total}: ${s.title}. ${s.body}`;

    const el = resolve(s.anchor);
    if (el) {
        try { el.scrollIntoView({ block: "center", inline: "nearest", behavior: reducedMotion() ? "auto" : "smooth" }); }
        catch { try { el.scrollIntoView(); } catch { } }
    }
    place();
    // Smooth scrolling moves the element after this; place again once it has settled.
    window.setTimeout(() => active && active.index === index && place(), reducedMotion() ? 0 : 350);
    ui.next.focus({ preventScroll: true });
}

function place() {
    if (!active) return;
    const { def, ui, index } = active;
    const s = def.steps[index];
    const el = resolve(s && s.anchor);
    const vw = document.documentElement.clientWidth || window.innerWidth;
    const vh = window.innerHeight;
    const box = ui.box;
    box.style.maxWidth = Math.min(380, vw - 2 * MARGIN) + "px";
    const bw = box.offsetWidth, bh = box.offsetHeight;

    if (!el) {
        ui.root.classList.add("tour-centred");
        ui.spot.style.display = "none";
        box.style.left = Math.max(MARGIN, (vw - bw) / 2) + "px";
        box.style.top = Math.max(MARGIN, (vh - bh) / 2) + "px";
        return;
    }
    ui.root.classList.remove("tour-centred");
    const r = el.getBoundingClientRect();
    const pad = 6;
    const top = Math.max(-pad, r.top - pad), left = Math.max(-pad, r.left - pad);
    const bottom = Math.min(vh + pad, r.bottom + pad), right = Math.min(vw + pad, r.right + pad);
    ui.spot.style.display = "block";
    ui.spot.style.top = top + "px";
    ui.spot.style.left = left + "px";
    ui.spot.style.width = Math.max(0, right - left) + "px";
    ui.spot.style.height = Math.max(0, bottom - top) + "px";

    let y;
    if (bottom + GAP + bh <= vh - MARGIN) y = bottom + GAP;            // below
    else if (top - GAP - bh >= MARGIN) y = top - GAP - bh;             // above
    else y = vh - bh - MARGIN;                                         // a tall element: keep the callout at the bottom
    let x = r.left + r.width / 2 - bw / 2;
    x = Math.min(Math.max(MARGIN, x), Math.max(MARGIN, vw - bw - MARGIN));
    box.style.left = x + "px";
    box.style.top = Math.max(MARGIN, y) + "px";
}

function onViewportChange() {
    if (!active || active.raf) return;
    active.raf = window.requestAnimationFrame(() => { if (active) { active.raf = 0; place(); } });
}

function focusables() {
    return Array.from(active.ui.box.querySelectorAll("button")).filter(b => !b.disabled);
}

function onKeyDown(e) {
    if (!active) return;
    if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); close(true); return; }
    if (e.key === "Tab") {
        const f = focusables();
        if (f.length === 0) { e.preventDefault(); return; }
        const i = f.indexOf(document.activeElement);
        e.preventDefault();
        const n = e.shiftKey ? (i <= 0 ? f.length - 1 : i - 1) : (i < 0 || i >= f.length - 1 ? 0 : i + 1);
        f[n].focus();
        return;
    }
    const inBox = active.ui.box.contains(document.activeElement);
    if (inBox && e.key === "ArrowRight") { e.preventDefault(); step(1); }
    else if (inBox && e.key === "ArrowLeft" && !active.ui.back.disabled) { e.preventDefault(); step(-1); }
}

/** Focus that lands outside the callout (a click on the page, a script) is brought back to it. */
function onFocusIn(e) {
    if (!active || active.ui.box.contains(e.target)) return;
    active.ui.next.focus({ preventScroll: true });
}

function close(finished) {
    if (!active) return;
    const { ui, returnFocus, raf, def } = active;
    if (raf) window.cancelAnimationFrame(raf);
    window.removeEventListener("resize", onViewportChange);
    window.removeEventListener("scroll", onViewportChange, true);
    document.removeEventListener("keydown", onKeyDown, true);
    document.removeEventListener("focusin", onFocusIn, true);
    active = null;
    if (finished) markShown(def);
    try { ui.root.remove(); } catch { }
    let target = returnFocus && returnFocus.isConnected ? returnFocus : document.querySelector(".tour-launch");
    if (target && typeof target.focus === "function") {
        try { target.focus({ preventScroll: true }); } catch { }
    }
}
