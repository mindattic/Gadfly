// Voice strain indicators (GAD-T232). Measures the student's own voice during one spoken answer, in the browser, from the same
// 16 kHz mono audio that voice-room.js streams to the recognizer: pitch level and variability, frame-to-frame jitter, loudness
// and how much of the captured audio was voiced. Only these summary numbers leave the page, with the answer; no audio, frame,
// pitch contour or voice print is kept or sent. The server compares them with the student's own first answers (VoiceStrain).
//
// Descriptive only: a different voice can come from effort, nerves, a hard question, tiredness, a cold or the room. Nothing here
// or on the server reads emotion, honesty or deception into these numbers.

const RATE = 16000;
const FRAME = 640;            // 40 ms
const MIN_LAG = 40;           // 400 Hz
const MAX_LAG = 213;          // ~75 Hz
const SILENCE_DBFS = -50;     // below this a frame is treated as silence
const VOICING = 0.5;          // normalized autocorrelation needed to call a frame voiced
const MAX_FRAMES = 15000;     // 10 minutes of audio per answer, then stop measuring
const MIN_VOICED_FRAMES = 25; // 1 s of voiced audio before pitch numbers are reported

const round = (v, d) => { const p = 10 ** d; return Math.round(v * p) / p; };
const mean = (a) => a.reduce((s, v) => s + v, 0) / a.length;
const sd = (a) => { const m = mean(a); return Math.sqrt(a.reduce((s, v) => s + (v - m) * (v - m), 0) / a.length); };
const median = (a) => { const s = [...a].sort((x, y) => x - y); const h = s.length >> 1; return s.length % 2 ? s[h] : (s[h - 1] + s[h]) / 2; };

export class VoiceAcoustics {
    constructor() { this.reset(); }

    /** Forgets everything measured (a new question, a cleared answer). */
    reset() {
        this.carry = new Float32Array(0);
        this.frames = 0;
        this.f0 = [];
        this.db = [];
        this.periodDiffs = [];
        this.lastPeriod = null;
    }

    /** The end of one speech segment: the next audio is not continuous with this one. */
    endSegment() {
        this.carry = new Float32Array(0);
        this.lastPeriod = null;
    }

    /** Adds a PCM16 mono 16 kHz chunk (an ArrayBuffer) of the student's answer. */
    push(pcm) {
        if (this.frames >= MAX_FRAMES || !pcm) return;
        const ints = new Int16Array(pcm);
        const data = new Float32Array(this.carry.length + ints.length);
        data.set(this.carry, 0);
        for (let i = 0; i < ints.length; i++) data[this.carry.length + i] = ints[i] / 32768;
        let off = 0;
        while (data.length - off >= FRAME && this.frames < MAX_FRAMES) {
            this.frame(data.subarray(off, off + FRAME));
            off += FRAME;
        }
        this.carry = data.slice(off);
    }

    frame(raw) {
        this.frames++;
        const n = raw.length;
        let m = 0;
        for (let i = 0; i < n; i++) m += raw[i];
        m /= n;
        const x = new Float32Array(n);
        let energy = 0;
        for (let i = 0; i < n; i++) { x[i] = raw[i] - m; energy += x[i] * x[i]; }
        const db = 20 * Math.log10(Math.max(Math.sqrt(energy / n), 1e-9));
        if (db < SILENCE_DBFS) { this.lastPeriod = null; return; }

        // Normalized autocorrelation over the speaking-voice range; the shortest lag close to the best one avoids octave errors.
        const r = new Float32Array(MAX_LAG + 2);
        let best = 0;
        for (let lag = MIN_LAG - 1; lag <= MAX_LAG + 1; lag++) {
            let c = 0, e1 = 0, e2 = 0;
            for (let i = 0; i + lag < n; i++) { c += x[i] * x[i + lag]; e1 += x[i] * x[i]; e2 += x[i + lag] * x[i + lag]; }
            r[lag] = c / Math.sqrt(e1 * e2 + 1e-12);
            if (lag >= MIN_LAG && lag <= MAX_LAG && r[lag] > best) best = r[lag];
        }
        if (best < VOICING) { this.lastPeriod = null; return; }
        let lag = MIN_LAG;
        for (let l = MIN_LAG; l <= MAX_LAG; l++) {
            if (r[l] >= 0.9 * best && r[l] >= r[l - 1] && r[l] >= r[l + 1]) { lag = l; break; }
        }
        const a = r[lag - 1], b = r[lag], c = r[lag + 1];
        const denom = a - 2 * b + c;
        const period = lag + (denom !== 0 ? Math.max(-0.5, Math.min(0.5, 0.5 * (a - c) / denom)) : 0);

        this.f0.push(RATE / period);
        this.db.push(db);
        if (this.lastPeriod !== null) {
            const d = Math.abs(period - this.lastPeriod) / ((period + this.lastPeriod) / 2);
            if (d < 0.2) this.periodDiffs.push(d); // larger jumps are intonation or a tracking error, not jitter
        }
        this.lastPeriod = period;
    }

    /** The answer's summary numbers, or null when too little voiced audio was heard to say anything. */
    summary() {
        if (this.frames === 0) return null;
        const voiced = this.f0.length;
        const out = {
            voicedSeconds: round((voiced * FRAME) / RATE, 2),
            voicedFraction: round(voiced / this.frames, 3),
        };
        if (voiced < MIN_VOICED_FRAMES) return out;
        const med = median(this.f0);
        out.pitchMedianHz = round(med, 1);
        out.pitchSdSemitones = round(sd(this.f0.map((f) => 12 * Math.log2(f / med))), 2);
        if (this.periodDiffs.length >= 10) out.jitterPercent = round(mean(this.periodDiffs) * 100, 2);
        out.loudnessMeanDbfs = round(mean(this.db), 1);
        out.loudnessSdDb = round(sd(this.db), 2);
        return out;
    }
}
