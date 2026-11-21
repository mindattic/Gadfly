// Browser integrity signals for the exam room (GAD-T56; docs/SOCRATIC_PLATFORM.md "Anti-cheating
// layers", Browser row). Watches the tab being hidden, the window losing focus, leaving full screen,
// extra screens, pastes into the answer box, copies of the question, and a weak devtools guess. It
// only observes: nothing here blocks the student or changes what they can do. Signals are batched
// and sent to .NET every few seconds and when the page is hidden or closed; anything unsent is kept
// in sessionStorage and sent after a reload. The exam room doesn't load this at all when the exam's
// integrity level is Off, and the server refuses (and the module stops) when the session isn't running.

const FLUSH_MS = 5000;
const MAX_QUEUE = 300;
const MAX_SEND = 50;
const DEVTOOLS_GAP = 160;

let state = null;

function storageKey(sessionId) {
    return "gadfly-integrity-" + sessionId;
}

function now() {
    return Date.now();
}

function push(signal) {
    if (!state || state.stopped) return;
    if (state.queue.length >= MAX_QUEUE) state.queue.shift();
    state.queue.push(signal);
}

function inAnswerBox(target) {
    return !!(target && target.closest && target.closest("#answer-box"));
}

function selectionInQuestion() {
    const sel = document.getSelection ? document.getSelection() : null;
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return 0;
    const node = sel.anchorNode && (sel.anchorNode.nodeType === 1 ? sel.anchorNode : sel.anchorNode.parentElement);
    if (!node || !node.closest || !node.closest(".exam-question")) return 0;
    return String(sel).length;
}

async function flush() {
    const s = state;
    if (!s || s.stopped || s.sending || s.queue.length === 0) return;
    s.sending = true;
    const batch = s.queue.splice(0, MAX_SEND);
    try {
        const keepGoing = await s.dotnet.invokeMethodAsync("RecordIntegrity", { sentAt: now(), events: batch });
        if (keepGoing === false && state === s) stop(false);
    } catch {
        // Connection trouble: put them back and try again later. Never surfaces to the student.
        if (!s.stopped) s.queue.unshift(...batch);
    } finally {
        s.sending = false;
    }
}

function save() {
    if (!state || state.stopped) return;
    try {
        if (state.queue.length > 0) sessionStorage.setItem(storageKey(state.sessionId), JSON.stringify(state.queue));
        else sessionStorage.removeItem(storageKey(state.sessionId));
    } catch { /* storage blocked: those signals are lost, the exam goes on */ }
}

function restore(sessionId) {
    try {
        const raw = sessionStorage.getItem(storageKey(sessionId));
        sessionStorage.removeItem(storageKey(sessionId));
        const list = raw ? JSON.parse(raw) : [];
        return Array.isArray(list) ? list.slice(-MAX_QUEUE) : [];
    } catch {
        return [];
    }
}

/** Closes open spans (hidden, blurred, devtools) at this moment, e.g. when the page goes away. */
function closeOpenSpans(at) {
    if (state.hiddenAt) { push({ type: "tab-hidden", at: state.hiddenAt, endAt: at }); state.hiddenAt = at; }
    if (state.blurAt && !state.hiddenAt) { push({ type: "focus-lost", at: state.blurAt, endAt: at }); state.blurAt = at; }
    if (state.devtoolsAt) { push({ type: "devtools-open", at: state.devtoolsAt, endAt: at }); state.devtoolsAt = at; }
}

function onVisibility() {
    const t = now();
    if (document.visibilityState === "hidden") {
        if (!state.hiddenAt) state.hiddenAt = t;
        // Switching tabs also blurs the window; the hidden span says it all.
        state.blurAt = null;
        flush();
        save();
    } else if (state.hiddenAt) {
        push({ type: "tab-hidden", at: state.hiddenAt, endAt: t });
        state.hiddenAt = null;
        state.blurAt = document.hasFocus() ? null : t;
        flush();
    }
}

function onBlur() {
    if (state.hiddenAt || state.blurAt) return;
    state.blurAt = now();
}

function onFocus() {
    if (state.blurAt && !state.hiddenAt) {
        const t = now();
        // A focus blip under half a second (a click into browser chrome) isn't worth a row.
        if (t - state.blurAt >= 500) push({ type: "focus-lost", at: state.blurAt, endAt: t });
    }
    state.blurAt = null;
}

function onFullscreen() {
    const full = !!document.fullscreenElement;
    if (state.wasFullscreen && !full) push({ type: "fullscreen-exit", at: now() });
    state.wasFullscreen = full;
}

function onPaste(e) {
    if (!state.recordPaste || !inAnswerBox(e.target)) return;
    let chars = 0;
    try { chars = (e.clipboardData && e.clipboardData.getData("text") || "").length; } catch { /* no access */ }
    push({ type: "paste", at: now(), chars });
}

function onCopy() {
    const chars = selectionInQuestion();
    if (chars > 0) push({ type: "copy-question", at: now(), chars });
}

function onPageHide() {
    closeOpenSpans(now());
    flush();
    save();
}

function checkDevtools() {
    // Weak heuristic: a docked devtools panel makes the window much bigger than the page. Zoom and
    // browser side panels do the same, so the server stores it as Info only.
    const gapW = (window.outerWidth || 0) - (window.innerWidth || 0);
    const gapH = (window.outerHeight || 0) - (window.innerHeight || 0);
    const open = window.outerWidth > 0 && (gapW > DEVTOOLS_GAP || gapH > DEVTOOLS_GAP + 100);
    const t = now();
    if (open && !state.devtoolsAt) state.devtoolsAt = t;
    else if (!open && state.devtoolsAt) {
        push({ type: "devtools-open", at: state.devtoolsAt, endAt: t });
        state.devtoolsAt = null;
    }
}

function reportScreens(count, method) {
    if (count > 1 && state.lastScreens !== count) push({ type: "extra-screen", at: now(), screens: count, method });
    state.lastScreens = count;
}

async function watchScreens(strict) {
    // Strict exams ask for the Window Management permission (the browser shows its own prompt);
    // otherwise only the permission-free screen.isExtended is read.
    if (strict && typeof window.getScreenDetails === "function") {
        try {
            const details = await window.getScreenDetails();
            if (!state || state.stopped) return;
            const onScreens = () => reportScreens(details.screens.length, "screen-details");
            details.addEventListener("screenschange", onScreens);
            state.cleanup.push(() => details.removeEventListener("screenschange", onScreens));
            onScreens();
            return;
        } catch { /* refused or unsupported: fall back */ }
    }
    if (typeof screen.isExtended === "boolean") {
        const onChange = () => reportScreens(screen.isExtended ? 2 : 1, "is-extended");
        if (typeof screen.addEventListener === "function") {
            screen.addEventListener("change", onChange);
            state.cleanup.push(() => screen.removeEventListener("change", onChange));
        }
        onChange();
    }
}

function listen(target, name, handler, options) {
    target.addEventListener(name, handler, options);
    state.cleanup.push(() => target.removeEventListener(name, handler, options));
}

/**
 * Starts watching. options: { sessionId, level: "Standard" | "Strict", recordPaste: bool }.
 * dotnet: a DotNetObjectReference with [JSInvokable] RecordIntegrity(batch) returning bool (false = stop).
 */
export function start(dotnet, options) {
    stop(true);
    if (!options || options.level === "Off") return false;
    state = {
        dotnet,
        sessionId: options.sessionId,
        recordPaste: options.recordPaste !== false,
        queue: restore(options.sessionId),
        cleanup: [],
        hiddenAt: document.visibilityState === "hidden" ? now() : null,
        blurAt: null,
        devtoolsAt: null,
        wasFullscreen: !!document.fullscreenElement,
        lastScreens: 1,
        sending: false,
        stopped: false,
    };
    listen(document, "visibilitychange", onVisibility);
    listen(window, "blur", onBlur);
    listen(window, "focus", onFocus);
    listen(document, "fullscreenchange", onFullscreen);
    listen(document, "paste", onPaste, true);
    listen(document, "copy", onCopy, true);
    listen(window, "pagehide", onPageHide);
    const timer = setInterval(() => { checkDevtools(); flush(); }, FLUSH_MS);
    state.cleanup.push(() => clearInterval(timer));
    checkDevtools();
    watchScreens(options.level === "Strict");
    if (state.queue.length > 0) flush();
    return true;
}

/** Asks for full screen (Strict exams offer a button; never required). Returns whether it worked. */
export async function enterFullscreen() {
    try {
        if (!document.fullscreenElement && document.documentElement.requestFullscreen) {
            await document.documentElement.requestFullscreen({ navigationUI: "hide" });
        }
        if (state) state.wasFullscreen = !!document.fullscreenElement;
        return !!document.fullscreenElement;
    } catch {
        return false;
    }
}

export function isFullscreen() {
    return !!document.fullscreenElement;
}

/**
 * Stops watching. keepUnsent (default): keep what is queued in sessionStorage, sent when the exam
 * room opens again; false when the server said stop.
 */
export function stop(keepUnsent) {
    if (!state) return;
    const s = state;
    if (keepUnsent !== false && !s.stopped) {
        closeOpenSpans(now());
        save();
    }
    s.stopped = true;
    for (const undo of s.cleanup) { try { undo(); } catch { /* already gone */ } }
    s.cleanup = [];
    if (keepUnsent === false) {
        try { sessionStorage.removeItem(storageKey(s.sessionId)); } catch { /* ignore */ }
    }
    if (state === s) state = null;
}
