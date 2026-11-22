// The student's chosen camera and microphone (GAD-T593), shared by the get-ready page and the exam room. Kept in localStorage on this
// device only (deviceIds are per site and per browser profile), under:
//
//   gadfly.device.camera      the camera's deviceId
//   gadfly.device.microphone  the microphone's deviceId
//
// Any storage failure (private window, blocked storage) just means the browser's default device. A remembered device that is no
// longer connected falls back to the default too: use constraints() rather than building { exact } yourself.

export const KEYS = { camera: "gadfly.device.camera", microphone: "gadfly.device.microphone" };

/** The remembered deviceId for "camera" or "microphone", or null. */
export function get(kind) {
    try { return localStorage.getItem(KEYS[kind]) || null; } catch { return null; }
}

/** Remembers (or, with an empty id, forgets) the device for "camera" or "microphone". */
export function set(kind, deviceId) {
    try {
        if (deviceId) localStorage.setItem(KEYS[kind], deviceId);
        else localStorage.removeItem(KEYS[kind]);
    } catch { /* storage blocked: the default device is used */ }
}

/**
 * getUserMedia constraints for `kind` with the remembered device: `base` (the usual audio or video constraints) plus
 * deviceId { exact } when one is remembered. Pass the result to open(); it retries without the device if that one is gone.
 */
export function constraints(kind, base) {
    const id = get(kind);
    const own = Object.assign({}, base || {});
    if (id) own.deviceId = { exact: id };
    return own;
}

/**
 * Opens `kind` ("camera" or "microphone") with the remembered device, falling back to the browser's default when the remembered
 * one is missing (unplugged, or the browser forgot its permission). `base` is the usual constraints for that kind.
 */
export async function open(kind, base) {
    const media = (c) => navigator.mediaDevices.getUserMedia(kind === "camera" ? { video: c, audio: false } : { audio: c, video: false });
    const remembered = get(kind);
    if (!remembered) return await media(base || true);
    try {
        return await media(constraints(kind, base));
    } catch (err) {
        if (err && (err.name === "OverconstrainedError" || err.name === "NotFoundError" || err.name === "NotReadableError")) {
            set(kind, "");
            return await media(base || true);
        }
        throw err;
    }
}

// The examiner the student chose on the get-ready page (GAD-T595): { id, modelUrl, portraitUrl } from the server's examiner
// catalog (Gadfly.Server/Student/ExaminerCatalog.cs), under localStorage "gadfly.examiner". The exam room shows that examiner when
// it is set and still offered; otherwise its own default.
export const EXAMINER_KEY = "gadfly.examiner";

/** The chosen examiner ({ id, modelUrl, portraitUrl }) or null. */
export function getExaminer() {
    try { const v = localStorage.getItem(EXAMINER_KEY); return v ? JSON.parse(v) : null; } catch { return null; }
}

/** Remembers the chosen examiner (null forgets it). */
export function setExaminer(examiner) {
    try {
        if (examiner && examiner.id) localStorage.setItem(EXAMINER_KEY, JSON.stringify({ id: examiner.id, modelUrl: examiner.modelUrl || null, portraitUrl: examiner.portraitUrl || null }));
        else localStorage.removeItem(EXAMINER_KEY);
    } catch { /* storage blocked: the default examiner */ }
}

/** The cameras and microphones the browser lists: { kind: "camera"|"microphone", deviceId, label }. Labels appear once allowed. */
export async function list() {
    try {
        const all = await navigator.mediaDevices.enumerateDevices();
        return all
            .filter((d) => (d.kind === "videoinput" || d.kind === "audioinput") && d.deviceId)
            .map((d) => ({ kind: d.kind === "videoinput" ? "camera" : "microphone", deviceId: d.deviceId, label: d.label || "" }));
    } catch {
        return [];
    }
}
