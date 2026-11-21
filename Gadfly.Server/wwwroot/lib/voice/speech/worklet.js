import { Resampler } from "./resampler.js";
/** Registered name of the capture processor. */
export const WORKLET_NAME = "mindattic-capture";
/** Samples per frame posted from the worklet and consumed by the VAD (32 ms at 16 kHz, Silero's frame size). */
export const FRAME_SAMPLES = 512;
/**
 * Source of the AudioWorklet processor. It resamples the microphone to 16 kHz mono and posts fixed 512-sample
 * Float32 frames to the main thread. Built as a string so the package needs no separate worklet file to host.
 */
export function workletSource() {
    return `
${Resampler.toString()}
class MindAtticCaptureProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.resampler = new Resampler(sampleRate, 16000);
    this.frame = new Float32Array(${FRAME_SAMPLES});
    this.filled = 0;
  }
  process(inputs) {
    const channel = inputs[0] && inputs[0][0];
    if (!channel) return true;
    const out = this.resampler.process(channel);
    for (let i = 0; i < out.length; i++) {
      this.frame[this.filled++] = out[i];
      if (this.filled === ${FRAME_SAMPLES}) {
        const copy = this.frame.slice(0);
        this.port.postMessage(copy, [copy.buffer]);
        this.filled = 0;
      }
    }
    return true;
  }
}
registerProcessor(${JSON.stringify(WORKLET_NAME)}, MindAtticCaptureProcessor);
`;
}
/** Loads the worklet module into an AudioContext from an in-memory Blob URL. */
export async function addCaptureWorklet(context) {
    const url = URL.createObjectURL(new Blob([workletSource()], { type: "application/javascript" }));
    try {
        await context.audioWorklet.addModule(url);
    }
    finally {
        URL.revokeObjectURL(url);
    }
}
//# sourceMappingURL=worklet.js.map