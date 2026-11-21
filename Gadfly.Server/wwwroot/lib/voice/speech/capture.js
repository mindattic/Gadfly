import { AudioPipeline } from "./pipeline.js";
import { createVad } from "./vad.js";
import { addCaptureWorklet, WORKLET_NAME } from "./worklet.js";
/**
 * Captures the microphone, resamples to 16 kHz mono in an AudioWorklet, runs VAD and delivers PCM16 chunks through
 * <c>onChunk</c>. It never touches the network: wire <c>onChunk</c> to your own transport (the demo uses SignalR).
 * Must be started from a user gesture (a click) and a secure context (https or localhost).
 */
export class MicCapture {
    options;
    pipeline;
    context;
    stream;
    node;
    _state = "idle";
    constructor(options = {}) {
        this.options = options;
    }
    /** Current lifecycle state. */
    get state() {
        return this._state;
    }
    /** The VAD in use ("silero" or "energy"); undefined before start. */
    get vadKind() {
        return this.pipeline?.vadKind;
    }
    /** Capture clock in ms: how much microphone audio has been processed. Use it to mark "the question ended now". */
    get currentTimeMs() {
        return this.pipeline?.currentTimeMs ?? 0;
    }
    /** The active mode. */
    get mode() {
        return this.pipeline?.mode ?? this.options.mode ?? "hands-free";
    }
    /** Opens the microphone and starts delivering audio. */
    async start() {
        if (this._state === "listening" || this._state === "starting")
            return;
        this._state = "starting";
        // stop() may run during any await below: it sets the state to "stopped" and releases what this already holds, so each
        // step releases only what it acquired after that and returns.
        const stopped = () => this._state !== "starting";
        try {
            const vad = await createVad({ ...this.options, onFallback: (r) => this.options.onVadFallback?.(r) });
            const pipeline = new AudioPipeline(vad, this.options);
            if (stopped()) {
                pipeline.dispose();
                return;
            }
            this.pipeline = pipeline;
            const stream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    deviceId: this.options.deviceId ? { exact: this.options.deviceId } : undefined,
                    channelCount: 1,
                    echoCancellation: this.options.echoCancellation ?? true,
                    noiseSuppression: this.options.noiseSuppression ?? true,
                    autoGainControl: this.options.autoGainControl ?? false,
                },
            });
            if (stopped()) {
                stream.getTracks().forEach((t) => t.stop());
                return;
            }
            this.stream = stream;
            this.context = new AudioContext({ latencyHint: "interactive" });
            await this.context.resume();
            if (stopped())
                return; // stop() saw this stream and context and released both
            await addCaptureWorklet(this.context);
            if (stopped())
                return;
            const source = this.context.createMediaStreamSource(this.stream);
            this.node = new AudioWorkletNode(this.context, WORKLET_NAME, { numberOfInputs: 1, numberOfOutputs: 1, channelCount: 1 });
            this.node.port.onmessage = (e) => this.pipeline?.push(e.data);
            const mute = this.context.createGain();
            mute.gain.value = 0;
            source.connect(this.node).connect(mute).connect(this.context.destination);
            this._state = "listening";
        }
        catch (e) {
            if (stopped())
                return; // stopped meanwhile (a closed context fails the worklet): not an error, and already released
            await this.stop();
            this.options.onError?.(e);
            throw e;
        }
    }
    /** Push-to-talk: button down. */
    pttDown() {
        this.pipeline?.pttDown();
    }
    /** Push-to-talk: button up. */
    pttUp() {
        this.pipeline?.pttUp();
    }
    /** Switches between push-to-talk and hands-free while running. */
    setMode(mode) {
        this.pipeline?.setMode(mode);
    }
    /** Stops the microphone, closes any open segment (so the last <c>onSpeechEnd</c> fires) and releases the devices. */
    async stop() {
        const wasListening = this._state === "listening";
        this._state = "stopped";
        if (this.node)
            this.node.port.onmessage = null;
        this.stream?.getTracks().forEach((t) => t.stop());
        if (wasListening)
            await this.pipeline?.finish();
        this.node?.disconnect();
        await this.context?.close().catch(() => undefined);
        this.pipeline?.dispose();
        this.node = undefined;
        this.context = undefined;
        this.stream = undefined;
    }
}
//# sourceMappingURL=capture.js.map