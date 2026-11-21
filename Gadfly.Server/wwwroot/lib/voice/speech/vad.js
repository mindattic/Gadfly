/** Root-mean-square level of a frame, 0..1. */
export function rms(frame) {
    let sum = 0;
    for (let i = 0; i < frame.length; i++)
        sum += frame[i] * frame[i];
    return Math.sqrt(sum / Math.max(1, frame.length));
}
/** Converts a linear amplitude to dBFS, floored at -100. */
export function toDbfs(amplitude) {
    return amplitude <= 1e-5 ? -100 : 20 * Math.log10(amplitude);
}
/**
 * Energy-based fallback detector with an adaptive noise floor (fast to drop, slow to rise), so a fan or a quiet room
 * does not read as speech. No model download and no dependencies; less robust than Silero against music or typing.
 */
export class EnergyVad {
    kind = "energy";
    floorDb = -50;
    marginDb;
    minDbfs;
    constructor(options = {}) {
        this.marginDb = options.marginDb ?? 12;
        this.minDbfs = options.minDbfs ?? -55;
    }
    process(frame) {
        const db = toDbfs(rms(frame));
        if (db < this.floorDb)
            this.floorDb = this.floorDb * 0.9 + db * 0.1;
        else
            this.floorDb += 0.003 * (db - this.floorDb);
        if (db < this.minDbfs)
            return 0;
        const excess = db - this.floorDb - this.marginDb;
        return 1 / (1 + Math.exp(-excess / 3));
    }
    reset() {
        this.floorDb = -50;
    }
}
/**
 * Silero VAD v5 through onnxruntime-web, loaded lazily so the package itself has no runtime dependency.
 * Frames are 512 samples at 16 kHz with the model's 64-sample context prepended and the recurrent state carried
 * between calls. {@link createVad} falls back to {@link EnergyVad} when loading fails.
 */
export class SileroVad {
    kind = "silero";
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    ort;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    session;
    state = new Float32Array(2 * 1 * 128);
    context = new Float32Array(64);
    static async create(options) {
        const vad = new SileroVad();
        vad.ort = await import(/* @vite-ignore */ options.onnxRuntimeUrl);
        if (options.wasmPaths)
            vad.ort.env.wasm.wasmPaths = options.wasmPaths;
        const response = await fetch(options.modelUrl);
        if (!response.ok)
            throw new Error(`Silero model download failed: HTTP ${response.status}`);
        vad.session = await vad.ort.InferenceSession.create(new Uint8Array(await response.arrayBuffer()), {
            executionProviders: ["wasm"],
        });
        return vad;
    }
    async process(frame) {
        const input = new Float32Array(64 + frame.length);
        input.set(this.context, 0);
        input.set(frame, 64);
        this.context = frame.slice(frame.length - 64);
        const result = await this.session.run({
            input: new this.ort.Tensor("float32", input, [1, input.length]),
            state: new this.ort.Tensor("float32", this.state, [2, 1, 128]),
            sr: new this.ort.Tensor("int64", BigInt64Array.from([16000n]), []),
        });
        this.state = new Float32Array(result.stateN.data);
        return result.output.data[0];
    }
    reset() {
        this.state = new Float32Array(2 * 1 * 128);
        this.context = new Float32Array(64);
    }
    dispose() {
        void this.session?.release?.();
    }
}
/** Creates the detector described by <paramref name="selection"/>. */
export async function createVad(selection = {}) {
    const want = selection.vad ?? "auto";
    if (want !== "energy" && selection.silero) {
        try {
            return await SileroVad.create(selection.silero);
        }
        catch (e) {
            selection.onFallback?.(e);
            if (want === "silero")
                throw e;
        }
    }
    else if (want === "silero") {
        throw new Error("vad 'silero' needs the silero.modelUrl and silero.onnxRuntimeUrl options");
    }
    return new EnergyVad(selection.energy);
}
//# sourceMappingURL=vad.js.map