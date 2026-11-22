// The examiner's hello on the get-ready page (GAD-T506, GAD-T595): the browser's own speech reads one short line in the page's
// language and the caption lights word by word as it goes. Local and free: no voice provider is called before the exam, so the
// exam itself may sound a little different (the page says so). Resolves false when the browser has no speech for the language;
// the line then simply stays on screen as text.

let current = null;

function voiceFor(langTag) {
    const voices = (window.speechSynthesis && window.speechSynthesis.getVoices()) || [];
    const base = langTag.split("-")[0].toLowerCase();
    const same = voices.filter((v) => (v.lang || "").toLowerCase().replace("_", "-").startsWith(base));
    return same.find((v) => v.localService) || same[0] || null;
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

/** Says `text` in `langTag` ("en-US", "ko-KR"), lighting the words of `caption`. */
export async function say(caption, text, langTag) {
    stop();
    const synth = window.speechSynthesis;
    if (!synth || typeof window.SpeechSynthesisUtterance !== "function" || !text) return false;
    await voicesReady();
    const voice = voiceFor(langTag || "en-US");
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
