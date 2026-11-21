import { TalkingHead } from '@met4citizen/talkinghead';
const STATES = ['idle', 'listening', 'thinking', 'speaking'];
const STYLE = `
:host { display: block; position: relative; min-height: 240px; background: var(--ma-avatar-bg, #1c2430); border-radius: var(--ma-avatar-radius, 12px); overflow: hidden; }
.stage { position: absolute; inset: 0; }
.portrait { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; display: none; }
:host([data-mode="portrait"]) .portrait { display: block; }
:host([data-mode="portrait"]) .stage { display: none; }
.badge { position: absolute; left: 10px; bottom: 10px; padding: 3px 9px; border-radius: 999px; font: 12px/1.4 system-ui, sans-serif;
  color: #fff; background: rgba(0,0,0,.45); text-transform: capitalize; pointer-events: none; }
:host([data-mode="portrait"][data-state="speaking"]) .portrait { animation: ma-pulse 1.1s ease-in-out infinite; }
@keyframes ma-pulse { 50% { filter: brightness(1.12); } }
@media (prefers-reduced-motion: reduce) { .portrait { animation: none !important; } }
`;
/**
 * `<ma-avatar src="avatar.glb" portrait="portrait.png">`: a 3D talking head (TalkingHead on three.js) that lip-syncs to
 * audio with word timings. Falls back to the portrait image when the user prefers reduced motion, WebGL is missing or
 * the model fails to load; speech still plays.
 *
 * Attributes: `src` (GLB), `portrait` (fallback image), `view` (head|upper|mid|full, default upper), `state`
 * (idle|listening|thinking), `camera-x` / `camera-y` (framing offsets), `lang` (lip-sync language, default en), `mood`, `motion="always"` (ignore reduced motion).
 * Events: `ready`, `fallback` (detail.reason), `state-change`, `speaking-start`, `speaking-end`, `speech-stopped`.
 */
export class MaAvatarElement extends HTMLElement {
    static observedAttributes = ['src', 'portrait', 'view', 'state', 'mood'];
    head = null;
    stage;
    img;
    badge;
    ctx = null; // own context, used only in portrait mode
    nextPlayTime = 0;
    fallbackSources = new Set();
    loadToken = 0;
    gazeTimer;
    current = 'idle';
    speakingDone = null;
    speechGen = 0; // markers of a stopped utterance stay queued in TalkingHead; this ignores them
    _ready = Promise.resolve();
    /** Resolves when the model is shown or the portrait fallback is in place. */
    get ready() { return this._ready; }
    /** `3d` once the model is up, `portrait` in fallback, `loading` before that. */
    get mode() {
        return this.dataset.mode ?? 'loading';
    }
    get state() { return this.current; }
    set state(v) { this.setState(v); }
    connectedCallback() {
        if (this.shadowRoot)
            return;
        const root = this.attachShadow({ mode: 'open' });
        root.innerHTML = `<style>${STYLE}</style><div class="stage" part="stage"></div><img class="portrait" part="portrait" alt="Examiner"><div class="badge" part="badge"></div>`;
        this.stage = root.querySelector('.stage');
        this.img = root.querySelector('.portrait');
        this.badge = root.querySelector('.badge');
        this.dataset.mode = 'loading';
        this.syncPortrait();
        this.syncState();
        this._ready = this.load();
    }
    disconnectedCallback() {
        this.loadToken++;
        clearInterval(this.gazeTimer);
        try {
            this.head?.dispose();
        }
        catch { /* already gone */ }
        this.head = null;
        this.shadowRoot?.replaceChildren();
        void this.ctx?.close();
        this.ctx = null;
    }
    attributeChangedCallback(name, oldValue, value) {
        if (!this.shadowRoot || oldValue === value)
            return;
        if (name === 'portrait')
            this.syncPortrait();
        else if (name === 'src')
            this._ready = this.load();
        else if (name === 'view' && this.head)
            this.head.setView(value || 'upper');
        else if (name === 'mood' && this.head)
            this.head.setMood(value || 'neutral');
        else if (name === 'state' && value && STATES.includes(value))
            this.setState(value);
    }
    // ---- loading --------------------------------------------------------------------------------------------------
    async load() {
        const token = ++this.loadToken;
        const src = this.getAttribute('src');
        this.dataset.mode = 'loading';
        try {
            this.head?.dispose();
        }
        catch { /* ignore */ }
        this.head = null;
        this.stage.replaceChildren();
        const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches && this.getAttribute('motion') !== 'always';
        if (reduced)
            return this.fallback('reduced-motion');
        if (!src)
            return this.fallback('no-src');
        if (!webglAvailable())
            return this.fallback('no-webgl');
        try {
            const lang = this.getAttribute('lang') || 'en';
            const head = new TalkingHead(this.stage, {
                ttsEndpoint: '',
                lipsyncModules: [lang],
                lipsyncLang: lang,
                cameraView: this.getAttribute('view') || 'upper',
                cameraRotateEnable: false,
                cameraX: Number(this.getAttribute('camera-x') ?? 0),
                cameraY: Number(this.getAttribute('camera-y') ?? 0),
                modelFPS: 30,
            });
            await head.showAvatar({
                url: src,
                body: 'F',
                avatarMood: this.getAttribute('mood') || 'neutral',
                lipsyncLang: lang,
            });
            if (token !== this.loadToken) {
                head.dispose();
                return;
            }
            this.head = head;
            this.dataset.mode = '3d';
            this.syncState();
            this.dispatchEvent(new CustomEvent('ready'));
        }
        catch (err) {
            if (token !== this.loadToken)
                return;
            console.warn('<ma-avatar>: model failed to load, using the portrait', err);
            this.fallback('load-failed');
        }
    }
    fallback(reason) {
        this.dataset.mode = 'portrait';
        this.head = null;
        this.syncPortrait();
        this.dispatchEvent(new CustomEvent('fallback', { detail: { reason } }));
        this.dispatchEvent(new CustomEvent('ready'));
    }
    syncPortrait() {
        const p = this.getAttribute('portrait');
        if (p)
            this.img.src = p;
        else
            this.img.removeAttribute('src');
    }
    // ---- state ----------------------------------------------------------------------------------------------------
    setState(next) {
        if (next === this.current)
            return;
        const previous = this.current;
        this.current = next;
        this.syncState();
        this.dispatchEvent(new CustomEvent('state-change', { detail: { state: next, previous } }));
    }
    syncState() {
        if (!this.shadowRoot)
            return;
        this.dataset.state = this.current;
        this.badge.textContent = this.current;
        clearInterval(this.gazeTimer);
        if (!this.head)
            return;
        const head = this.head;
        const gaze = () => {
            if (this.current === 'listening')
                head.makeEyeContact(5000); // look at the person
            else if (this.current === 'thinking') {
                const w = this.clientWidth || 400, h = this.clientHeight || 300;
                head.lookAt(w * 0.28, h * 0.12, 3500); // glance up and away while it thinks
            }
            else if (this.current === 'idle')
                head.lookAtCamera(3000);
        };
        gaze();
        if (this.current !== 'speaking')
            this.gazeTimer = window.setInterval(gaze, 3000);
    }
    // ---- speech: whole utterance ----------------------------------------------------------------------------------
    /**
     * Speaks an utterance: decodes the audio and lip-syncs it to the word timings. Resolves when it has finished (or was
     * stopped). Call from a user gesture the first time so the browser lets audio play.
     */
    async speak(input) {
        await this._ready;
        const bytes = await toArrayBuffer(input.audio);
        const ctx = this.audioContext();
        await ctx.resume();
        const buffer = await ctx.decodeAudioData(bytes);
        this.stop();
        const gen = ++this.speechGen;
        const done = new Promise(resolve => { this.speakingDone = resolve; });
        if (this.head) {
            const ms = buffer.duration * 1000;
            this.head.speakAudio({
                audio: buffer,
                words: input.words,
                wtimes: input.wtimes,
                wdurations: input.wdurations,
                markers: [() => { if (gen === this.speechGen)
                        this.speechStarted(); }, () => { if (gen === this.speechGen)
                        this.speechEnded(); }],
                mtimes: [0, ms + 50],
            }, { lipsyncLang: this.getAttribute('lang') || 'en' });
        }
        else {
            this.playBuffer(buffer, 0);
            this.speechStarted();
            window.setTimeout(() => this.speechEnded(), buffer.duration * 1000 + 50);
        }
        return done;
    }
    // ---- speech: streaming PCM ------------------------------------------------------------------------------------
    /** Starts a streaming utterance of PCM16 mono chunks at `sampleRate` Hz (ElevenLabs `pcm_22050` is the default). */
    async beginStream(sampleRate = 22050) {
        await this._ready;
        this.stop();
        const gen = ++this.speechGen;
        if (this.head) {
            await this.head.audioCtx.resume();
            await this.head.streamStart({ sampleRate, lipsyncType: 'words', lipsyncLang: this.getAttribute('lang') || 'en' }, () => { if (gen === this.speechGen)
                this.speechStarted(); }, () => { if (gen === this.speechGen)
                this.speechEnded(); });
        }
        else {
            const ctx = this.audioContext();
            await ctx.resume();
            this.streamRate = sampleRate;
            this.nextPlayTime = ctx.currentTime + 0.05;
            this.speechStarted();
        }
    }
    streamRate = 22050;
    /** Feeds one PCM chunk (and any word timings that finished in it, in ms from the start of the utterance). */
    pushChunk(chunk) {
        if (this.head) {
            this.head.streamAudio({
                audio: chunk.audio,
                words: chunk.words, wtimes: chunk.wtimes, wdurations: chunk.wdurations,
            });
        }
        else {
            const pcm = chunk.audio instanceof Int16Array ? chunk.audio : new Int16Array(chunk.audio);
            const ctx = this.audioContext();
            const buf = ctx.createBuffer(1, pcm.length, this.streamRate);
            const ch = buf.getChannelData(0);
            for (let i = 0; i < pcm.length; i++)
                ch[i] = pcm[i] / 32768;
            this.nextPlayTime = Math.max(this.nextPlayTime, ctx.currentTime);
            this.playBuffer(buf, this.nextPlayTime - ctx.currentTime);
            this.nextPlayTime += buf.duration;
        }
    }
    /** Tells the avatar no more chunks are coming; speaking ends when the queued audio has played out. */
    endStream() {
        if (this.head) {
            this.head.streamNotifyEnd();
            return;
        }
        const ctx = this.audioContext();
        window.setTimeout(() => this.speechEnded(), Math.max(0, (this.nextPlayTime - ctx.currentTime) * 1000) + 50);
    }
    // ---- stop (barge-in) ------------------------------------------------------------------------------------------
    /** Cuts speech off at once (the student started talking, or Stop was pressed). Safe to call when silent. */
    stop() {
        this.speechGen++;
        const wasSpeaking = this.current === 'speaking' || this.speakingDone !== null;
        if (this.head) {
            if (this.head.isStreaming)
                this.head.streamInterrupt();
            this.head.stopSpeaking();
        }
        for (const s of this.fallbackSources) {
            try {
                s.stop();
            }
            catch { /* ended */ }
        }
        this.fallbackSources.clear();
        if (wasSpeaking) {
            this.finishSpeaking();
            this.dispatchEvent(new CustomEvent('speech-stopped'));
        }
    }
    // ---- internals ------------------------------------------------------------------------------------------------
    speechStarted() {
        this.setState('speaking');
        this.dispatchEvent(new CustomEvent('speaking-start'));
    }
    speechEnded() {
        if (this.current !== 'speaking')
            return;
        this.finishSpeaking();
        this.dispatchEvent(new CustomEvent('speaking-end'));
    }
    finishSpeaking() {
        this.setState('idle');
        const done = this.speakingDone;
        this.speakingDone = null;
        done?.();
    }
    audioContext() {
        if (this.head)
            return this.head.audioCtx;
        return (this.ctx ??= new AudioContext());
    }
    playBuffer(buffer, delaySeconds) {
        const ctx = this.audioContext();
        const src = ctx.createBufferSource();
        src.buffer = buffer;
        src.connect(ctx.destination);
        src.onended = () => this.fallbackSources.delete(src);
        this.fallbackSources.add(src);
        src.start(ctx.currentTime + delaySeconds);
    }
}
function webglAvailable() {
    try {
        const c = document.createElement('canvas');
        return !!(c.getContext('webgl2') || c.getContext('webgl'));
    }
    catch {
        return false;
    }
}
async function toArrayBuffer(a) {
    if (a instanceof ArrayBuffer)
        return a.slice(0);
    if (a instanceof Blob)
        return a.arrayBuffer();
    const r = await fetch(a);
    if (!r.ok)
        throw new Error(`audio fetch failed: ${r.status}`);
    return r.arrayBuffer();
}
/** Registers `<ma-avatar>` (idempotent). */
export function defineAvatarElement(name = 'ma-avatar') {
    if (!customElements.get(name))
        customElements.define(name, MaAvatarElement);
}
//# sourceMappingURL=ma-avatar.js.map