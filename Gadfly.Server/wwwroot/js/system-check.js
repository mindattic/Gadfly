// Pre-exam browser check (GAD-T51). Loaded as a module by the start page; reports what the browser
// supports. Voice and camera are mandatory (GAD-T239): startMedia opens the microphone and the camera,
// shows the camera in the page's preview, draws the microphone level, and tells the page (OnMedia) when
// each one works: the camera once it delivers a picture, the microphone once it hears the student speak.
// Nothing is recorded or sent; both are released when the page leaves or the exam begins.
export function check() {
    const w = window;
    return {
        userAgent: navigator.userAgent || "",
        webSocket: "WebSocket" in w,
        cookies: navigator.cookieEnabled === true,
        online: navigator.onLine !== false,
        secureContext: w.isSecureContext === true,
        speechRecognition: !!(w.SpeechRecognition || w.webkitSpeechRecognition),
        mediaDevices: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
        screenWidth: w.innerWidth || 0,
    };
}

export function ping() {
    return true;
}

// ------------------------------------------------------------------------------------- microphone and camera (GAD-T239)

/** Speech louder than this (dBFS) for a few frames in a row counts as "we heard you". */
const HEARD_DBFS = -45;
const HEARD_FRAMES = 3;

let M = null;

function report(m, device, state) {
    if (M !== m || m[device] === state) return;
    m[device] = state;
    m.dotnet.invokeMethodAsync("OnMedia", device, state).catch(() => { });
}

function outcome(err) {
    if (!err) return "error";
    if (err.name === "NotAllowedError" || err.name === "SecurityError") return "denied";
    if (err.name === "NotFoundError" || err.name === "OverconstrainedError" || err.name === "DevicesNotFoundError") return "missing";
    if (err.name === "NotReadableError" || err.name === "AbortError") return "busy";
    return "error";
}

/**
 * Opens the microphone and the camera and keeps checking them. States reported per device ("microphone", "camera"):
 * waiting, listening (microphone open, not heard yet), ok, denied, missing, busy, lost, unsupported, error.
 */
export async function startMedia(dotnet, video, level) {
    stopMedia();
    const m = { dotnet, video, level, streams: [], ctx: null, timer: null, loud: 0, microphone: null, camera: null, onGesture: null };
    M = m;
    if (!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia) || window.isSecureContext === false) {
        report(m, "microphone", "unsupported");
        report(m, "camera", "unsupported");
        return;
    }
    report(m, "microphone", "waiting");
    report(m, "camera", "waiting");
    await openMicrophone(m);
    if (M === m) await openCamera(m);
}

async function openMicrophone(m) {
    let stream;
    try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true }, video: false });
    } catch (err) {
        report(m, "microphone", outcome(err));
        return;
    }
    if (M !== m) { stream.getTracks().forEach((t) => t.stop()); return; }
    m.streams.push(stream);
    watch(m, stream, "microphone");
    const Ctx = window.AudioContext || window.webkitAudioContext;
    if (!Ctx) { report(m, "microphone", "unsupported"); return; }
    try {
        m.ctx = new Ctx();
        const source = m.ctx.createMediaStreamSource(stream);
        const analyser = m.ctx.createAnalyser();
        analyser.fftSize = 1024;
        source.connect(analyser);
        const buf = new Float32Array(analyser.fftSize);
        // Audio may only run after a click on a page opened fresh: any click or key resumes it.
        m.onGesture = () => { try { m.ctx && m.ctx.resume(); } catch { } };
        document.addEventListener("pointerdown", m.onGesture);
        document.addEventListener("keydown", m.onGesture);
        try { m.ctx.resume(); } catch { }
        report(m, "microphone", "listening");
        m.timer = setInterval(() => {
            if (M !== m) return;
            analyser.getFloatTimeDomainData(buf);
            let sum = 0;
            for (let i = 0; i < buf.length; i++) sum += buf[i] * buf[i];
            const dbfs = 20 * Math.log10(Math.sqrt(sum / buf.length) + 1e-9);
            if (m.level) m.level.style.setProperty("--level", String(Math.max(0, Math.min(1, (dbfs + 60) / 55))));
            m.loud = dbfs > HEARD_DBFS ? m.loud + 1 : 0;
            if (m.loud >= HEARD_FRAMES && m.microphone === "listening") report(m, "microphone", "ok");
        }, 100);
    } catch {
        report(m, "microphone", "error");
    }
}

async function openCamera(m) {
    let stream;
    try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "user", width: { ideal: 640 }, height: { ideal: 480 } }, audio: false });
    } catch (err) {
        report(m, "camera", outcome(err));
        return;
    }
    if (M !== m) { stream.getTracks().forEach((t) => t.stop()); return; }
    m.streams.push(stream);
    watch(m, stream, "camera");
    const video = m.video;
    if (!video) { report(m, "camera", "error"); return; }
    video.srcObject = stream;
    try { await video.play(); } catch { }
    // A camera that opens but never delivers a picture (covered by another app, a dead driver) is not working.
    const started = performance.now();
    const wait = () => {
        if (M !== m) return;
        if (video.videoWidth > 0 && video.readyState >= 2) report(m, "camera", "ok");
        else if (performance.now() - started > 8000) report(m, "camera", "error");
        else setTimeout(wait, 200);
    };
    wait();
}

/** A device unplugged or a permission withdrawn while the page is open fails its check again. */
function watch(m, stream, device) {
    stream.getTracks().forEach((t) => t.addEventListener("ended", () => { if (M === m) report(m, device, "lost"); }));
}

/** Releases the microphone and the camera. */
export function stopMedia() {
    const m = M;
    M = null;
    if (!m) return;
    if (m.timer) clearInterval(m.timer);
    if (m.onGesture) {
        document.removeEventListener("pointerdown", m.onGesture);
        document.removeEventListener("keydown", m.onGesture);
    }
    m.streams.forEach((s) => { try { s.getTracks().forEach((t) => t.stop()); } catch { } });
    try { if (m.video) m.video.srcObject = null; } catch { }
    try { m.ctx && m.ctx.close(); } catch { }
}
