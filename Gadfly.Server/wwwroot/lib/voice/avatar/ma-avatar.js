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
 *
 * Presence (GAD-T241): she holds eye contact in every state, while she speaks and while she listens, refreshed every couple
 * of seconds so the model's own idle wandering never takes her eyes away for long. `setGazeTarget({x, y})` (where the
 * student's face is in their webcam, mirrored, each in [-1, 1]) turns her gaze toward them; null looks straight out of the
 * screen. `setStudentTalking(bool)` (the student's voice activity) makes her listen visibly: an attentive expression, small
 * nods while they talk and at their pauses, the odd extra blink. The `thinking` state is one brief look up and aside, then
 * straight back to the student.
 */
const GAZE_EVERY_MS = 2200;   // how often the gaze is renewed
const GAZE_HOLD_MS = 3200;    // how long each renewal holds (longer than the period, so there is never a gap)
const THINK_MS = 1100;        // the brief thinking look; after it she looks back at the student
const FOLLOW_X = 0.3;         // how far the face position turns her (share of the half-viewport the lookAt range spans)
const FOLLOW_Y = 0.25;
const NOD_GAP_MS = 1800;      // never two nods closer than this
const rand = (a, b) => a + Math.random() * (b - a);
const clamp1 = (v) => Math.max(-1, Math.min(1, Number(v) || 0));
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
    thinkTimer;
    listenTimer;
    thinkUntil = 0;
    gazeTarget = null;      // the student's face, {x, y} in [-1, 1] mirrored, or null for straight out of the screen
    appliedTarget = null;   // the target the current gaze renewal used
    studentTalking = false;
    lastNodAt = 0;
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
        clearTimeout(this.thinkTimer);
        clearTimeout(this.listenTimer);
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
                // Mostly eye contact, little wandering head movement (GAD-T241); the gaze loop below does the rest.
                avatarIdleEyeContact: 0.9,
                avatarIdleHeadMove: 0.3,
                avatarSpeakingEyeContact: 0.9,
                avatarSpeakingHeadMove: 0.35,
            });
            await head.showAvatar({
                url: src,
                body: 'F',
                avatarMood: this.getAttribute('mood') || 'neutral',
                lipsyncLang: lang,
                avatarIdleEyeContact: 0.9,
                avatarIdleHeadMove: 0.3,
                avatarSpeakingEyeContact: 0.9,
                avatarSpeakingHeadMove: 0.35,
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
        clearTimeout(this.thinkTimer);
        this.thinkUntil = 0;
        if (this.current !== 'listening') {
            this.studentTalking = false;
            clearTimeout(this.listenTimer);
        }
        if (!this.head)
            return;
        if (this.current === 'thinking') {
            // The student has finished: one brief look up and aside, then straight back to them.
            const away = this.awayPoint();
            try {
                this.head.lookAt(away.x, away.y, THINK_MS);
            }
            catch { /* model not ready */ }
            this.express('thinking');
            this.thinkUntil = performance.now() + THINK_MS + 700;
            this.thinkTimer = window.setTimeout(() => this.holdGaze(), THINK_MS + 750);
        }
        else {
            this.holdGaze();
            if (this.current === 'listening')
                this.express('attentive');
        }
        // Eye contact in every state, speaking included: renewed before the last renewal runs out.
        this.gazeTimer = window.setInterval(() => this.holdGaze(), GAZE_EVERY_MS);
    }
    // ---- presence (GAD-T241) --------------------------------------------------------------------------------------
    /**
     * Where the student's face is in their webcam image, mirrored (as the student sees themselves): `x` and `y` in [-1, 1],
     * positive x toward the student's right, positive y lower. null (no camera, no face) looks straight out of the screen.
     */
    setGazeTarget(target) {
        const next = target && Number.isFinite(Number(target.x)) && Number.isFinite(Number(target.y))
            ? { x: clamp1(target.x), y: clamp1(target.y) } : null;
        this.gazeTarget = next;
        // Follow a clear move at once rather than at the next renewal.
        const was = this.appliedTarget;
        const moved = (!was) !== (!next) || (was && next && Math.hypot(was.x - next.x, was.y - next.y) > 0.2);
        if (moved)
            this.holdGaze();
    }
    /** The student's voice activity while she listens: true while they are talking, false at a pause or the end. */
    setStudentTalking(talking) {
        const now = !!talking && this.current === 'listening';
        if (now === this.studentTalking)
            return;
        this.studentTalking = now;
        clearTimeout(this.listenTimer);
        if (!this.head || this.current !== 'listening')
            return;
        if (now) {
            this.express('attentive', true);
            this.scheduleBackchannel();
        }
        else {
            // A pause: the small nod of someone following along.
            this.listenTimer = window.setTimeout(() => {
                if (this.current === 'listening' && !this.studentTalking)
                    this.nod();
            }, rand(250, 550));
        }
    }
    /** While the student talks: a small nod or a blink every few seconds, never in lockstep. */
    scheduleBackchannel() {
        clearTimeout(this.listenTimer);
        this.listenTimer = window.setTimeout(() => {
            if (!this.studentTalking || this.current !== 'listening' || !this.head)
                return;
            const r = Math.random();
            if (r < 0.5)
                this.nod();
            else if (r < 0.75)
                this.blink();
            else {
                this.nod();
                window.setTimeout(() => this.blink(), 450);
            }
            this.express('attentive', true);
            this.scheduleBackchannel();
        }, rand(2200, 4800));
    }
    /** Looks at the student (toward their face when known, else straight out), unless the brief thinking look is on. */
    holdGaze() {
        const head = this.head;
        if (!head || performance.now() < this.thinkUntil)
            return;
        this.appliedTarget = this.gazeTarget;
        const p = this.facePoint();
        try {
            head.lookAt(p ? p.x : null, p ? p.y : null, GAZE_HOLD_MS);
        }
        catch { /* model not ready */ }
        if (this.current === 'listening')
            this.express('attentive', this.studentTalking);
    }
    /** The viewport point that lies toward the student's face, or null for straight at the camera. */
    facePoint() {
        const t = this.gazeTarget;
        if (!t)
            return null;
        const eyes = this.eyesOnScreen();
        if (!eyes)
            return null;
        const maxx = Math.max(window.innerWidth - eyes.x, eyes.x);
        const maxy = Math.max(window.innerHeight - eyes.y, eyes.y);
        return { x: eyes.x + t.x * FOLLOW_X * maxx, y: eyes.y + t.y * FOLLOW_Y * maxy };
    }
    /** Up and to one side of her eyes, for the thinking look. */
    awayPoint() {
        const eyes = this.eyesOnScreen();
        if (eyes) {
            const side = Math.random() < 0.5 ? -1 : 1;
            return { x: eyes.x + side * 0.35 * Math.max(window.innerWidth - eyes.x, eyes.x), y: eyes.y - 0.3 * Math.max(window.innerHeight - eyes.y, eyes.y) };
        }
        const r = this.getBoundingClientRect();
        return { x: r.left + r.width * 0.8, y: r.top + r.height * 0.05 };
    }
    /** Her eyes' position in the viewport (the coordinate space TalkingHead.lookAt uses). */
    eyesOnScreen() {
        const head = this.head;
        try {
            const l = head?.objectLeftEye, r = head?.objectRightEye, cam = head?.camera, node = head?.nodeAvatar;
            if (!l || !r || !cam || !node)
                return null;
            l.updateMatrixWorld(true);
            r.updateMatrixWorld(true);
            const a = l.position.clone().setFromMatrixPosition(l.matrixWorld);
            const b = r.position.clone().setFromMatrixPosition(r.matrixWorld);
            a.add(b).divideScalar(2).project(cam);
            const rect = node.getBoundingClientRect();
            const x = (a.x + 1) / 2 * rect.width + rect.left, y = -(a.y - 1) / 2 * rect.height + rect.top;
            return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
        }
        catch {
            return null;
        }
    }
    /** Pushes one short facial animation, replacing a running one of the same name. */
    animate(name, dt, vs, replace = true) {
        const head = this.head;
        if (!head?.animQueue || !head.animFactory)
            return false;
        try {
            const old = head.animQueue.findIndex(a => a.template?.name === name);
            if (old !== -1) {
                if (!replace)
                    return false;
                head.animQueue.splice(old, 1);
            }
            head.animQueue.push(head.animFactory({ name, dt, vs }));
            return true;
        }
        catch {
            return false;
        }
    }
    /** An expression held over one gaze renewal: `attentive` while listening (stronger while the student talks), `thinking`. */
    express(kind, engaged = false) {
        if (kind === 'thinking') {
            this.animate('ma-expression', [250, THINK_MS, 500], {
                browInnerUp: [0.3, 0.3, 0], browDownLeft: [0.15, 0.15, 0], mouthPressLeft: [0.25, 0.25, 0], mouthPressRight: [0.25, 0.25, 0],
                eyeSquintLeft: [0.15, 0.15, 0], eyeSquintRight: [0.15, 0.15, 0],
            });
            return;
        }
        const k = engaged ? 1 : 0.6;
        this.animate('ma-expression', [400, GAZE_HOLD_MS, 600], {
            browInnerUp: [0.22 * k, 0.22 * k, 0], mouthSmile: [0.12 * k, 0.12 * k, 0],
            eyeWideLeft: [0.12 * k, 0.12 * k, 0], eyeWideRight: [0.12 * k, 0.12 * k, 0],
        });
    }
    /** A small nod: down a little and back. */
    nod() {
        const now = performance.now();
        if (now - this.lastNodAt < NOD_GAP_MS)
            return;
        if (this.animate('ma-nod', [[160, 240], [200, 300], [220, 320]], { headMove: [0], headRotateX: [[0.04, 0.08], [0, 0.015], 0] }, false))
            this.lastNodAt = now;
    }
    blink() {
        this.animate('ma-blink', [50, [80, 140], 100], { eyeBlinkLeft: [1, 1, 0], eyeBlinkRight: [1, 1, 0] }, false);
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