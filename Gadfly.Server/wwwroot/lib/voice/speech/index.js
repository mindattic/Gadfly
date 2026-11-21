/** Package version, kept in step with package.json. */
export const VERSION = "1.0.0";
export { MicCapture } from "./capture.js";
export { WavCapture, decodeWav } from "./wav.js";
export { AudioPipeline, } from "./pipeline.js";
export { EnergyVad, SileroVad, createVad, rms, toDbfs, } from "./vad.js";
export { Resampler } from "./resampler.js";
export { floatToPcm16, pcm16ToBase64 } from "./encode.js";
export { FRAME_SAMPLES } from "./worklet.js";
//# sourceMappingURL=index.js.map