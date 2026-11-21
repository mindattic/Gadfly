import { AudioPipeline } from "./pipeline.js";
import { Resampler } from "./resampler.js";
import { createVad } from "./vad.js";
/** Decodes a WAV (16/24/32-bit PCM or 32-bit float, any channels and rate) to 16 kHz mono Float32. */
export function decodeWav(buffer) {
    const view = new DataView(buffer);
    const tag = (o) => String.fromCharCode(view.getUint8(o), view.getUint8(o + 1), view.getUint8(o + 2), view.getUint8(o + 3));
    if (buffer.byteLength < 44 || tag(0) !== "RIFF" || tag(8) !== "WAVE")
        throw new Error("Not a RIFF/WAVE file.");
    let format = 0, channels = 0, rate = 0, bits = 0, dataStart = -1, dataLength = 0;
    let pos = 12;
    while (pos + 8 <= buffer.byteLength) {
        const id = tag(pos);
        const size = Math.min(view.getUint32(pos + 4, true), buffer.byteLength - pos - 8);
        if (id === "fmt ") {
            format = view.getUint16(pos + 8, true);
            channels = view.getUint16(pos + 10, true);
            rate = view.getUint32(pos + 12, true);
            bits = view.getUint16(pos + 22, true);
        }
        else if (id === "data") {
            dataStart = pos + 8;
            dataLength = size;
            break;
        }
        pos += 8 + size + (size & 1);
    }
    if (dataStart < 0 || !channels || !rate)
        throw new Error("WAV has no fmt or data chunk.");
    const bytes = bits / 8;
    const frames = Math.floor(dataLength / (bytes * channels));
    const mono = new Float32Array(frames);
    for (let i = 0; i < frames; i++) {
        let sum = 0;
        for (let c = 0; c < channels; c++) {
            const o = dataStart + (i * channels + c) * bytes;
            if (format === 3 && bits === 32)
                sum += view.getFloat32(o, true);
            else if (bits === 16)
                sum += view.getInt16(o, true) / 0x8000;
            else if (bits === 24)
                sum += ((view.getUint8(o) | (view.getUint8(o + 1) << 8) | (view.getInt8(o + 2) << 16))) / 0x800000;
            else if (bits === 32)
                sum += view.getInt32(o, true) / 0x80000000;
            else
                throw new Error(`Unsupported WAV sample size: ${bits} bit.`);
        }
        mono[i] = sum / channels;
    }
    return new Resampler(rate, 16000).process(mono);
}
/**
 * Runs a WAV file through exactly the same VAD, segmentation and chunking as the microphone, with no microphone and no
 * provider. Use it to try the pipeline and the demo page without speaking, and to replay a recorded answer.
 */
export class WavCapture {
    options;
    pipeline;
    cancelled = false;
    constructor(options = {}) {
        this.options = options;
    }
    /** The VAD in use; undefined before play. */
    get vadKind() {
        return this.pipeline?.vadKind;
    }
    /** Capture clock in ms. */
    get currentTimeMs() {
        return this.pipeline?.currentTimeMs ?? 0;
    }
    /** Plays the file through the pipeline; resolves when the last segment closed. */
    async play(wav, play = {}) {
        this.cancelled = false;
        const samples = decodeWav(wav);
        const vad = await createVad({ ...this.options, onFallback: (r) => this.options.onVadFallback?.(r) });
        const pipeline = (this.pipeline = new AudioPipeline(vad, this.options));
        const block = 512 * 4;
        const tail = new Float32Array(16000 * 2); // trailing silence so the last segment closes by VAD
        const all = new Float32Array(samples.length + tail.length);
        all.set(samples, 0);
        all.set(tail, samples.length);
        for (let i = 0; i < all.length && !this.cancelled; i += block) {
            pipeline.push(all.subarray(i, Math.min(i + block, all.length)));
            if (play.realtime)
                await new Promise((r) => setTimeout(r, (block / 16000) * 1000));
            else
                await pipeline.idle();
        }
        await pipeline.finish();
        pipeline.dispose();
    }
    /** Stops playback early. */
    cancel() {
        this.cancelled = true;
    }
}
//# sourceMappingURL=wav.js.map