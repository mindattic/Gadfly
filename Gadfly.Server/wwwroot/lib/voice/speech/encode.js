/** Converts Float32 samples in [-1, 1] to little-endian signed 16-bit PCM. */
export function floatToPcm16(samples) {
    const out = new Int16Array(samples.length);
    for (let i = 0; i < samples.length; i++) {
        const s = Math.max(-1, Math.min(1, samples[i]));
        out[i] = s < 0 ? Math.round(s * 0x8000) : Math.round(s * 0x7fff);
    }
    return out;
}
/** Base64 of raw bytes, for transports that carry text (the SignalR JSON protocol). */
export function pcm16ToBase64(pcm) {
    const bytes = pcm instanceof Int16Array ? new Uint8Array(pcm.buffer, pcm.byteOffset, pcm.byteLength) : new Uint8Array(pcm);
    let binary = "";
    const step = 0x8000;
    for (let i = 0; i < bytes.length; i += step) {
        binary += String.fromCharCode(...bytes.subarray(i, i + step));
    }
    return btoa(binary);
}
//# sourceMappingURL=encode.js.map