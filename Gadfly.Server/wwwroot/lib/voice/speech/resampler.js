/**
 * Streaming bin-average resampler (box filter): converts mono Float32 audio from any input rate to the target rate
 * without clicks between blocks. Averaging over each output bin is also a cheap anti-alias low-pass when
 * down-sampling (48 kHz or 44.1 kHz microphones to 16 kHz).
 *
 * This class is serialised into the AudioWorklet source (see worklet.ts), so it must stay self-contained: no imports,
 * no references to anything outside its own body.
 */
export class Resampler {
    inputRate;
    outputRate;
    ratio;
    acc = 0;
    binRemaining;
    constructor(inputRate, outputRate = 16000) {
        this.inputRate = inputRate;
        this.outputRate = outputRate;
        this.ratio = inputRate / outputRate;
        this.binRemaining = this.ratio;
    }
    /** Resamples one block; carries partial-bin state to the next call. */
    process(input) {
        if (this.inputRate === this.outputRate)
            return input;
        const out = [];
        for (let i = 0; i < input.length; i++) {
            const x = input[i];
            let remaining = 1;
            while (remaining > 1e-9) {
                const take = remaining < this.binRemaining ? remaining : this.binRemaining;
                this.acc += x * take;
                this.binRemaining -= take;
                remaining -= take;
                if (this.binRemaining <= 1e-9) {
                    out.push(this.acc / this.ratio);
                    this.acc = 0;
                    this.binRemaining = this.ratio;
                }
            }
        }
        return Float32Array.from(out);
    }
}
//# sourceMappingURL=resampler.js.map