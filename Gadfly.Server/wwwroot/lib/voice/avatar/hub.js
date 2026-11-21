/**
 * Connects an `<ma-avatar>` to the hub mapped by `MapMindAtticAvatar` on the server. The server streams
 * `speechStart`, `speechChunk` and `speechEnd`; PCM formats (`pcm_22050`, the server default) go through the
 * low-latency streaming path, encoded formats (MP3) are collected and decoded at the end.
 */
export function attachSpeechHub(avatar, hub) {
    let activeId = null;
    let pcm = true;
    let buffered = [];
    let bufferedWords = [];
    let contentType = 'audio/mpeg';
    // SignalR does not wait for an async handler before running the next message: chunks and the end that arrive while
    // beginStream is still starting (the first one loads a worklet) are held here and replayed in order once it has.
    let pending = null;
    const handlers = {
        speechStart: async (m) => {
            activeId = m.id;
            buffered = [];
            bufferedWords = [];
            pending = null;
            pcm = m.format.startsWith('pcm_');
            contentType = m.format.startsWith('opus') ? 'audio/ogg' : m.format.startsWith('wav') ? 'audio/wav' : 'audio/mpeg';
            if (pcm) {
                const held = pending = [];
                try {
                    await avatar.beginStream(m.sampleRate ?? 22050);
                }
                finally {
                    if (pending === held) {
                        pending = null;
                        for (const run of held) {
                            try {
                                run();
                            }
                            catch { /* one bad chunk must not drop the rest or the end */ }
                        }
                    }
                }
            }
        },
        speechChunk: (m) => {
            if (m.id !== activeId)
                return;
            if (pending) {
                pending.push(() => handlers.speechChunk(m));
                return;
            }
            const bytes = base64ToBytes(m.audio);
            if (pcm) {
                avatar.pushChunk({
                    audio: bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength),
                    words: m.words.map(w => w.word),
                    wtimes: m.words.map(w => w.startMs),
                    wdurations: m.words.map(w => w.durationMs),
                });
            }
            else {
                buffered.push(bytes);
                bufferedWords.push(...m.words);
            }
        },
        speechEnd: async (m) => {
            if (m.id !== activeId)
                return;
            if (pending) {
                pending.push(() => { void handlers.speechEnd(m).catch(() => undefined); });
                return;
            }
            if (pcm)
                avatar.endStream();
            else {
                const blob = new Blob(buffered, { type: contentType });
                await avatar.speak({
                    audio: blob,
                    words: bufferedWords.map(w => w.word),
                    wtimes: bufferedWords.map(w => w.startMs),
                    wdurations: bufferedWords.map(w => w.durationMs),
                });
            }
        },
        speechStopped: (m) => { if (m.id === activeId)
            avatar.stop(); },
        speechError: (m) => {
            if (m.id === activeId) {
                avatar.stop();
                avatar.dispatchEvent(new CustomEvent('speech-error', { detail: m }));
            }
        },
    };
    for (const [k, fn] of Object.entries(handlers))
        hub.on(k, fn);
    return {
        async speak(text, voiceId, cache) {
            const finished = new Promise(resolve => {
                const done = () => { avatar.removeEventListener('speaking-end', done); avatar.removeEventListener('speech-stopped', done); resolve(); };
                avatar.addEventListener('speaking-end', done);
                avatar.addEventListener('speech-stopped', done);
            });
            await hub.invoke("Speak", text, voiceId ?? null, cache ?? false);
            return finished;
        },
        async stop() {
            activeId = null; // later chunks of the stopped utterance are ignored
            pending = null;
            avatar.stop();
            await hub.invoke('Stop');
        },
        dispose() {
            for (const [k, fn] of Object.entries(handlers))
                hub.off(k, fn);
        },
    };
}
function base64ToBytes(b64) {
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++)
        out[i] = bin.charCodeAt(i);
    return out;
}
//# sourceMappingURL=hub.js.map