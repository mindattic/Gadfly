import { floatToPcm16 } from "./encode.js";
import { FRAME_SAMPLES } from "./worklet.js";
import { EnergyVad, rms, toDbfs } from "./vad.js";
const FRAME_MS = (FRAME_SAMPLES / 16000) * 1000;
/**
 * The transport-agnostic core: takes 16 kHz mono Float32 audio of any block size, measures level, runs the VAD,
 * gates audio by mode and emits PCM16 chunks and segment boundaries. The browser-free parts are what the microphone
 * and WAV sources share, so the same behaviour can be exercised from a file.
 */
export class AudioPipeline {
    o;
    vad;
    carry = new Float32Array(0);
    samplesSeen = 0;
    chain = Promise.resolve();
    ring = [];
    inSegment = false;
    voicedRun = 0;
    silenceRun = 0;
    firstVoicedMs = 0;
    segmentStartMs = 0;
    lastVoicedEndMs = 0;
    pending = [];
    pendingSamples = 0;
    constructor(vad, options = {}) {
        this.vad = vad;
        this.o = {
            mode: options.mode ?? "hands-free",
            chunkMs: options.chunkMs ?? 100,
            prerollMs: options.prerollMs ?? 300,
            hangoverMs: options.hangoverMs ?? 800,
            minSpeechMs: options.minSpeechMs ?? 160,
            startThreshold: options.startThreshold ?? 0.5,
            endThreshold: options.endThreshold ?? 0.35,
            onChunk: options.onChunk,
            onLevel: options.onLevel,
            onSpeechStart: options.onSpeechStart,
            onSpeechEnd: options.onSpeechEnd,
            onVadFallback: options.onVadFallback,
            onError: options.onError,
        };
    }
    /** Current mode. */
    get mode() {
        return this.o.mode;
    }
    /** Which detector is running (it can change from silero to energy on failure). */
    get vadKind() {
        return this.vad.kind;
    }
    /** Capture-clock position: milliseconds of audio pushed so far. */
    get currentTimeMs() {
        return this.samplesSeen / 16;
    }
    /** True while a segment is open. */
    get speaking() {
        return this.inSegment;
    }
    /** Switches mode; an open segment is closed first. */
    setMode(mode) {
        this.enqueue(() => {
            if (this.inSegment)
                this.endSegment();
            this.o.mode = mode;
            this.voicedRun = 0;
            this.silenceRun = 0;
        });
    }
    /** Feeds 16 kHz mono Float32 samples in [-1, 1], any block size. */
    push(samples) {
        let data = samples;
        if (this.carry.length) {
            data = new Float32Array(this.carry.length + samples.length);
            data.set(this.carry, 0);
            data.set(samples, this.carry.length);
        }
        const frames = [];
        let offset = 0;
        while (data.length - offset >= FRAME_SAMPLES) {
            frames.push({ samples: data.slice(offset, offset + FRAME_SAMPLES), timeMs: this.samplesSeen / 16 });
            this.samplesSeen += FRAME_SAMPLES;
            offset += FRAME_SAMPLES;
        }
        this.carry = data.slice(offset);
        if (frames.length) {
            this.enqueue(async () => {
                for (const f of frames)
                    await this.consume(f);
            });
        }
    }
    /** Push-to-talk: the button went down. Opens a segment (with pre-roll). */
    pttDown() {
        this.enqueue(() => {
            if (this.o.mode !== "push-to-talk" || this.inSegment)
                return;
            const start = this.ring.length ? this.ring[this.ring.length - 1].timeMs + FRAME_MS : this.currentTimeMs;
            this.beginSegment(start);
        });
    }
    /** Push-to-talk: the button went up. Closes the segment. */
    pttUp() {
        this.enqueue(() => {
            if (this.o.mode === "push-to-talk" && this.inSegment)
                this.endSegment();
        });
    }
    /** Closes any open segment and resolves once every queued frame was processed. Call before stopping. */
    async finish() {
        this.enqueue(() => {
            if (this.inSegment)
                this.endSegment();
            this.flushPending();
        });
        await this.chain;
    }
    /** Resolves once every queued frame was processed. */
    idle() {
        return this.chain;
    }
    /** Releases the detector. */
    dispose() {
        this.vad.dispose?.();
    }
    enqueue(work) {
        this.chain = this.chain.then(work).catch((e) => this.o.onError?.(e));
    }
    async score(frame) {
        try {
            return await this.vad.process(frame);
        }
        catch (e) {
            if (this.vad.kind === "silero") {
                this.vad.dispose?.();
                this.vad = new EnergyVad();
                this.o.onVadFallback?.(e);
                return this.vad.process(frame);
            }
            throw e;
        }
    }
    async consume(frame) {
        const probability = await this.score(frame.samples);
        let peak = 0;
        for (let i = 0; i < frame.samples.length; i++) {
            const a = Math.abs(frame.samples[i]);
            if (a > peak)
                peak = a;
        }
        const level = rms(frame.samples);
        if (this.o.mode === "hands-free")
            this.handsFree(frame, probability);
        else
            this.pushToTalk(frame);
        this.o.onLevel?.({
            rms: level,
            peak,
            dbfs: toDbfs(level),
            probability,
            speaking: this.inSegment,
            timeMs: frame.timeMs,
        });
    }
    ringCapacity() {
        return Math.ceil((this.o.prerollMs + this.o.minSpeechMs) / FRAME_MS) + 1;
    }
    handsFree(frame, probability) {
        if (!this.inSegment) {
            this.ring.push(frame);
            while (this.ring.length > this.ringCapacity())
                this.ring.shift();
            if (probability >= this.o.startThreshold) {
                if (this.voicedRun === 0)
                    this.firstVoicedMs = frame.timeMs;
                this.voicedRun++;
            }
            else {
                this.voicedRun = 0;
            }
            if (this.voicedRun * FRAME_MS >= this.o.minSpeechMs)
                this.beginSegment(this.firstVoicedMs);
            return;
        }
        this.add(frame);
        if (probability >= this.o.endThreshold) {
            this.silenceRun = 0;
            this.lastVoicedEndMs = frame.timeMs + FRAME_MS;
        }
        else {
            this.silenceRun++;
            if (this.silenceRun * FRAME_MS >= this.o.hangoverMs)
                this.endSegment();
        }
    }
    pushToTalk(frame) {
        if (this.inSegment) {
            this.add(frame);
            this.lastVoicedEndMs = frame.timeMs + FRAME_MS;
            return;
        }
        this.ring.push(frame);
        while (this.ring.length > Math.ceil(this.o.prerollMs / FRAME_MS) + 1)
            this.ring.shift();
    }
    beginSegment(startMs) {
        this.inSegment = true;
        this.segmentStartMs = startMs;
        this.lastVoicedEndMs = startMs;
        this.silenceRun = 0;
        this.voicedRun = 0;
        const keep = this.o.mode === "push-to-talk" ? Math.ceil(this.o.prerollMs / FRAME_MS) : this.ring.length;
        for (const f of this.ring.slice(-keep))
            this.add(f);
        this.ring = [];
        this.o.onSpeechStart?.(startMs);
    }
    endSegment() {
        this.flushPending();
        this.inSegment = false;
        this.ring = [];
        this.voicedRun = 0;
        this.silenceRun = 0;
        const endMs = Math.max(this.lastVoicedEndMs, this.segmentStartMs);
        this.o.onSpeechEnd?.({ startMs: this.segmentStartMs, endMs, durationMs: endMs - this.segmentStartMs });
    }
    add(frame) {
        this.pending.push(frame);
        this.pendingSamples += frame.samples.length;
        if (this.pendingSamples >= (this.o.chunkMs * 16))
            this.flushPending();
    }
    flushPending() {
        if (!this.pending.length)
            return;
        const all = new Float32Array(this.pendingSamples);
        let at = 0;
        for (const f of this.pending) {
            all.set(f.samples, at);
            at += f.samples.length;
        }
        const startMs = this.pending[0].timeMs;
        this.pending = [];
        this.pendingSamples = 0;
        const pcm = floatToPcm16(all);
        this.o.onChunk?.({ pcm: pcm.buffer, captureStartMs: startMs, durationMs: all.length / 16 });
    }
}
//# sourceMappingURL=pipeline.js.map