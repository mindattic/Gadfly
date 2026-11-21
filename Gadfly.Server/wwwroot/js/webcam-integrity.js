// Camera integrity checks for the exam room (GAD-T57; docs/SOCRATIC_PLATFORM.md "Anti-cheating layers",
// Webcam row). Runs MindAttic.Webcam's presence, gaze-region and reading-pattern modules on the device
// (the self-hosted build in wwwroot/webcam, no CDN) and passes only the library's sanitized events (type,
// start, end, confidence, one sentence) to the WebcamIntegrity component, which stores them as webcam
// integrity events. No frame, image, landmark or face signature is ever sent. Nothing here infers emotion
// or deception, and nothing blocks the student: if the camera is denied or missing, a neutral
// "camera-unavailable" note is reported and the exam carries on.

const BATCH = 10;      // small batches keep each interop message well under the circuit's size limit
const EVERY_MS = 5000;
const HEARTBEAT_MS = 15000;   // asks whether the exam is still running; the camera stops when it isn't

const PARTIAL_MS = 60000;       // an episode still going is reported about once a minute (D21, GAD-T171)
const PARTIAL_TICK_MS = 5000;   // how often to look for an episode that is due an update

let state = null;

/** A stable id for one episode: its type and start, so the server keeps ONE record that grows. */
function episodeId(type, startMs) { return type + "@" + Math.round(startMs); }

function classify(err) {
    const name = err && err.name;
    if (name === "NotAllowedError" || name === "SecurityError" || name === "PermissionDeniedError") return "denied";
    if (name === "NotFoundError" || name === "OverconstrainedError" || name === "DevicesNotFoundError") return "no-device";
    if (name === "NotReadableError" || name === "AbortError" || name === "TrackStartError") return "in-use";
    if (name === "NotSupportedError" || name === "TypeError") return "unsupported";
    return "error";
}

/** The framing hint for the student: the most useful single thing, or null when all is well. */
function hintOf(status) {
    if (!status) return null;
    if (status.faces === 0) return "no-face";
    const w = status.warnings || [];
    if (w.includes("low-light")) return "low-light";
    if (w.includes("face-too-small")) return "too-small";
    if (w.includes("face-cut-off")) return "cut-off";
    if (w.includes("face-off-center")) return "off-center";
    return null;
}

async function report(s, reason) {
    // The neutral note goes out as an ordinary event; the server maps the reason code to a fixed sentence.
    try {
        await s.dotnet.invokeMethodAsync("RecordWebcam", [{
            source: "webcam", type: "camera-unavailable", startedAt: new Date().toISOString(), endedAt: null,
            confidence: 0.3, summary: reason,
        }]);
    } catch { /* never the student's problem */ }
    try { await s.dotnet.invokeMethodAsync("OnStatus", "unavailable", null); } catch { }
}

export async function start(dotnet, video, options) {
    if (state && !state.stopped) await stop();
    const s = { dotnet, stopped: false, monitor: null, sink: null, core: null, hint: null, reported: false };
    state = s;
    const base = new URL("../" + (options && options.basePath ? options.basePath : "webcam/"), import.meta.url);
    try {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            s.reported = true;
            await report(s, "unsupported");
            return false;
        }
        await dotnet.invokeMethodAsync("OnStatus", "starting", null);
        const lib = await import(new URL("webcam.browser.js", base).href);
        s.core = new lib.WebcamCore({
            numFaces: 2, measureLight: true, video,
            wasmBaseUrl: new URL(".", base).href.replace(/\/$/, ""),
            modelUrl: new URL("face_landmarker.bin", base).href,
        });
        // Quality hints (lighting, framing) are for the student only: includeQuality stays off, so they are never sent.
        s.monitor = new lib.IntegrityMonitor({ includeQuality: false, includeAnnotations: true });
        // Episodes that have started and not ended (the library's "begin" hook). Each gets a partial note every PARTIAL_MS.
        s.open = new Map();
        s.monitor.on("begin", (b) => {
            if (s.stopped) return;
            const id = episodeId(b.type, b.start);
            if (!s.open.has(id)) s.open.set(id, { type: b.type, start: Math.round(b.start), sentAt: Date.now() });
        });
        s.monitor.on("event", (e) => { s.open.delete(episodeId(e.type, e.start)); });
        s.sink = s.monitor.sink(async (events) => {
            // The closing event of an episode carries the same id as its partial notes, so it closes the same record.
            const tagged = events.map((h) => ({ ...h, episodeId: episodeId(h.type, Date.parse(h.startedAt)) }));
            const keepGoing = await dotnet.invokeMethodAsync("RecordWebcam", tagged);
            // The session ended: stop after this call returns, so the events still queued are sent on the way out.
            if (keepGoing === false) setTimeout(() => { void stop(true); }, 0);
        }, { everyMs: EVERY_MS, maxBatch: BATCH });
        s.heartbeat = setInterval(() => {
            if (s.stopped) return;
            dotnet.invokeMethodAsync("RecordWebcam", []).then((keep) => { if (keep === false) void stop(true); }).catch(() => { });
        }, HEARTBEAT_MS);
        s.partials = setInterval(() => {
            if (s.stopped) return;
            const now = Date.now();
            const due = [];
            for (const [id, ep] of s.open) {
                if (now - ep.sentAt < (options && options.partialMs || PARTIAL_MS)) continue;
                ep.sentAt = now;
                due.push({
                    source: "webcam", type: ep.type, startedAt: new Date(ep.start).toISOString(), endedAt: new Date(now).toISOString(),
                    confidence: 0.4, summary: "", episodeId: id, ongoing: true,
                });
            }
            if (due.length === 0) return;
            dotnet.invokeMethodAsync("RecordWebcam", due).then((keep) => { if (keep === false) void stop(true); }).catch(() => { });
        }, PARTIAL_TICK_MS);
        s.monitor.presence?.on("status", (status) => {
            if (s.stopped) return;
            const hint = hintOf(status);
            if (hint === s.hint) return;
            s.hint = hint;
            dotnet.invokeMethodAsync("OnStatus", "on", hint).catch(() => { });
        });
        await s.monitor.start(s.core);
        if (s.stopped) {
            // stop() ran while the camera was starting: its monitor.stop() came before the camera was held, so release it now,
            // and the timers started after stop() cleared them.
            if (s.heartbeat) clearInterval(s.heartbeat);
            if (s.partials) clearInterval(s.partials);
            try { s.monitor.stop(); } catch { }
            try { s.core.stop(); } catch { }
            return false;
        }
        const stream = video && video.srcObject;
        if (stream && stream.getVideoTracks) {
            for (const t of stream.getVideoTracks()) {
                t.addEventListener("ended", () => {
                    if (s.stopped || s.reported) return;
                    s.reported = true;
                    report(s, "error");
                });
            }
        }
        window.addEventListener("pagehide", s.onHide = () => { void stop(); }, { once: true });
        await dotnet.invokeMethodAsync("OnStatus", "on", s.hint);
        return true;
    } catch (err) {
        if (s.stopped) return false;
        s.reported = true;
        // The heartbeat and partial-episode timers are started before the camera is; stop() returns early once stopped is set,
        // so a failed start has to clear them itself or they tick for the rest of the page's life (GAD-T201).
        if (s.heartbeat) clearInterval(s.heartbeat);
        if (s.partials) clearInterval(s.partials);
        if (s.onHide) window.removeEventListener("pagehide", s.onHide);
        try { s.monitor && s.monitor.stop(); } catch { }
        try { s.sink && await s.sink.close(); } catch { }
        s.stopped = true;
        if (state === s) state = null;
        await report(s, classify(err));
        return false;
    }
}

/** Closes open events (so each has an end), sends what is queued, and releases the camera. */
export async function stop(flush = true, notify = true) {
    const s = state;
    if (!s || s.stopped) return;
    s.stopped = true;
    state = null;
    if (s.onHide) window.removeEventListener("pagehide", s.onHide);
    if (s.heartbeat) clearInterval(s.heartbeat);
    if (s.partials) clearInterval(s.partials);
    try { s.monitor && s.monitor.stop(); } catch { }
    if (flush) { try { s.sink && await s.sink.close(); } catch { } }
    else { try { s.sink && s.sink.close(); } catch { } }
    if (notify) { try { await s.dotnet.invokeMethodAsync("OnStatus", "stopped", null); } catch { } }
}
