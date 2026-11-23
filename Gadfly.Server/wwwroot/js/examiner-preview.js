// The examiner's hello on the get-ready page (GAD-T506, GAD-T595): the browser's own speech reads one short line in the page's
// language and the caption lights word by word as it goes. Local and free: no voice provider is called before the exam, so the
// exam itself may sound a little different (the page says so). Resolves false when the browser has no speech for the language;
// the line then simply stays on screen as text.
//
// The student's voice choice (GAD-T648): the exam voices are ElevenLabs voices, which the preview never calls, so a chosen voice is
// replayed as a distinct browser voice instead: `options.variant` (the chosen voice's place in the examiner's list, 0 for the
// examiner's own) picks among the browser's voices for the language, preferring ones that sound like the examiner
// (`options.male`), and shifts the pitch a little, so each choice is heard to differ. The exam itself speaks with the chosen voice.

let current = null;

// Words browsers put in their voice names; enough to prefer a matching voice, never to rule one out.
const FEMALE = /female|woman|aria|jenny|zira|samantha|victoria|karen|moira|tessa|fiona|serena|susan|hazel|libby|sonia|elvira|dalia|helena|monica|paulina|sun-?hi|yuna|heami|seoyeon|google .*(us|uk) english female/i;
const MALE = /\bmale\b|\bman\b|guy|david|mark|daniel\b|alex|fred|george|ryan|thomas|tom\b|james|aaron|jorge|alvaro|diego|pablo|raul|injoon|hyunsu|google uk english male/i;

// A small pitch shift per voice choice, so neighbouring choices that land on the same browser voice still sound different.
const PITCH = [1, 1.08, 0.92, 1.15, 0.86, 1.04, 0.96];

function voiceFor(langTag, options) {
    const voices = (window.speechSynthesis && window.speechSynthesis.getVoices()) || [];
    const base = langTag.split("-")[0].toLowerCase();
    const same = voices.filter((v) => (v.lang || "").toLowerCase().replace("_", "-").startsWith(base));
    if (!same.length) return null;
    const variant = Math.max(0, (options && options.variant) | 0);
    if (!options || (!variant && options.male === undefined)) return same.find((v) => v.localService) || same[0];
    // Voices that sound like the examiner first (local ones first among those), then the rest; the variant steps through them.
    const wanted = options.male ? MALE : FEMALE;
    const other = options.male ? FEMALE : MALE;
    const rank = (v) => (wanted.test(v.name) && !other.test(v.name) ? 0 : other.test(v.name) ? 2 : 1) * 2 + (v.localService ? 0 : 1);
    const ordered = same.slice().sort((a, b) => rank(a) - rank(b));
    const matching = ordered.filter((v) => rank(v) < 4);
    const pool = matching.length ? matching : ordered;
    return pool[variant % pool.length];
}

function voicesReady() {
    const synth = window.speechSynthesis;
    if (synth.getVoices().length) return Promise.resolve();
    return new Promise((resolve) => {
        const done = () => { synth.removeEventListener("voiceschanged", done); resolve(); };
        synth.addEventListener("voiceschanged", done);
        setTimeout(done, 1500);
    });
}

/**
 * Says `text` in `langTag` ("en-US", "ko-KR"), lighting the words of `caption`. `options` (GAD-T648, optional): { variant, male },
 * the student's voice choice as a browser voice (see above).
 */
export async function say(caption, text, langTag, options) {
    stop();
    const synth = window.speechSynthesis;
    if (!synth || typeof window.SpeechSynthesisUtterance !== "function" || !text) return false;
    await voicesReady();
    const voice = voiceFor(langTag || "en-US", options);
    if (!voice) return false;
    // Words as spans, so each lights as she reaches it; the text itself is unchanged for screen readers.
    const words = text.split(/(\s+)/);
    const spans = [];
    if (caption) {
        caption.replaceChildren();
        for (const w of words) {
            if (/^\s+$/.test(w)) { caption.append(document.createTextNode(w)); continue; }
            const s = document.createElement("span");
            s.textContent = w;
            s.className = "preview-word";
            caption.append(s);
            spans.push(s);
        }
    }
    const offsets = [];
    let at = 0;
    for (const s of spans) { const i = text.indexOf(s.textContent, at); offsets.push(i < 0 ? at : i); at = (i < 0 ? at : i) + s.textContent.length; }
    return await new Promise((resolve) => {
        const u = new SpeechSynthesisUtterance(text);
        u.lang = langTag || "en-US";
        u.voice = voice;
        u.rate = 0.95;
        u.pitch = PITCH[Math.max(0, (options && options.variant) | 0) % PITCH.length];
        const light = (k) => spans.forEach((s, i) => s.classList.toggle("lit", i <= k));
        u.onboundary = (e) => {
            if (e.name && e.name !== "word") return;
            let k = 0;
            while (k + 1 < offsets.length && offsets[k + 1] <= e.charIndex) k++;
            light(k);
        };
        const finish = (ok) => { clearTimeout(guard); light(spans.length - 1); if (current === u) current = null; resolve(ok); };
        u.onend = () => finish(true);
        u.onerror = () => finish(false);
        const guard = setTimeout(() => { try { synth.cancel(); } catch { } finish(true); }, 20000);
        current = u;
        synth.speak(u);
    });
}

export function stop() {
    if (!current) return;
    current = null;
    try { window.speechSynthesis.cancel(); } catch { }
}
