// Get-ready microphone test (GAD-T359): a level meter (system-check.js already draws one from its own analyser), plus a
// noise-floor and clipping check and a plain-English echo hint, the same "advice only, never blocks Begin exam" spirit as
// gaze-quality.js. Reuses the library's own level math (MindAttic.SpeechRecognition's rms/toDbfs) rather than re-deriving it.
import { rms, toDbfs } from "/lib/voice/speech/index.js";

const SAMPLE_MS = 4000;             // how long to listen before giving noise-floor/clipping advice
const FRAME_MS = 100;
const CLIP_THRESHOLD = 0.97;        // a sample this close to full scale counts toward clipping
const CLIP_FRAMES_FOR_WARNING = 3;  // this many clipped frames in the sample window is worth a warning
const QUIET_DBFS = -50;             // at or below this, the room is quiet (good)
const NOISY_DBFS = -35;             // at or above this, background noise is likely to be picked up with the voice

let M = null;

/**
 * Starts the check on an already-open microphone stream. <paramref name="panel"/> is the element the advice is written
 * into (plain text, built here, not through Blazor: this runs independently of the component's render cycle, the same
 * as gaze-quality.js). Call once per stream; call stop() when the stream is released.
 */
export function start(stream, panel) {
    stop();
    if (!stream || !panel || !(window.AudioContext || window.webkitAudioContext)) return;
    const m = { stream, panel, ctx: null, timer: null, frames: 0, clipFrames: 0, dbfsSamples: [], done: false };
    M = m;
    try {
        const Ctx = window.AudioContext || window.webkitAudioContext;
        m.ctx = new Ctx();
        const source = m.ctx.createMediaStreamSource(stream);
        const analyser = m.ctx.createAnalyser();
        analyser.fftSize = 2048;
        source.connect(analyser);
        const buf = new Float32Array(analyser.fftSize);
        const track = stream.getAudioTracks()[0];
        const settings = track && track.getSettings ? track.getSettings() : {};
        m.echoCancellation = settings.echoCancellation;
        panel.textContent = "Listening for a few seconds to check your microphone…";
        m.timer = setInterval(() => {
            if (M !== m || m.done) return;
            analyser.getFloatTimeDomainData(buf);
            let peak = 0;
            for (let i = 0; i < buf.length; i++) { const a = Math.abs(buf[i]); if (a > peak) peak = a; }
            if (peak >= CLIP_THRESHOLD) m.clipFrames++;
            m.dbfsSamples.push(toDbfs(rms(buf)));
            m.frames++;
            if (m.frames * FRAME_MS >= SAMPLE_MS) { m.done = true; render(m); }
        }, FRAME_MS);
    } catch {
        panel.textContent = "";
    }
}

function render(m) {
    if (!m.panel) return;
    const sorted = [...m.dbfsSamples].sort((a, b) => a - b);
    // The noise floor is the quieter end of the sample, not the average: a few words said while the student reads the
    // page shouldn't make the room look noisy.
    const floor = sorted.length ? sorted[Math.floor(sorted.length * 0.2)] : -100;
    const lines = [];
    if (floor <= QUIET_DBFS) lines.push("Background noise: quiet. Good.");
    else if (floor < NOISY_DBFS) lines.push("Background noise: a little noticeable. A quieter room will help the recognizer hear you clearly.");
    else lines.push("Background noise: noisy where you are. Close windows, turn off fans or move somewhere quieter if you can.");
    if (m.clipFrames >= CLIP_FRAMES_FOR_WARNING)
        lines.push("Your microphone is picking up very loud peaks (clipping). Move it a little further away or turn its input volume down.");
    if (m.echoCancellation === false)
        lines.push("Echo cancellation isn't on for this microphone. If you hear the examiner echo back through your own audio, use headphones.");
    else
        lines.push("If the examiner's voice echoes back through your microphone, headphones usually fix it.");
    m.panel.textContent = lines.join(" ");
}

/** Stops listening and releases the audio graph; the microphone stream itself is left to its owner (system-check.js). */
export function stop() {
    const m = M;
    M = null;
    if (!m) return;
    if (m.timer) clearInterval(m.timer);
    try { m.ctx && m.ctx.close(); } catch { }
}
