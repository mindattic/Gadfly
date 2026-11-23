// Gets the talking examiner ready while the student is still on the get-ready page (GAD-T303), so the exam room shows her and she
// speaks without a long first load. Blazor moves from the get-ready page to the exam room inside the same document, so modules
// imported here are already loaded there; the avatar model is read into model-cache.js, where the exam room picks it up.
// Everything is best effort and quiet: a failure here only means the exam room loads it itself, as before.

const LIB = "/lib/voice";
let started = null;

function reducedMotion() {
    try { return matchMedia("(prefers-reduced-motion: reduce)").matches; } catch { return false; }
}

function webgl() {
    try {
        const c = document.createElement("canvas");
        return !!(c.getContext("webgl2") || c.getContext("webgl"));
    } catch { return false; }
}

function script(src) {
    return new Promise((resolve) => {
        if (document.querySelector(`script[data-voice-src="${src}"]`)) return resolve();
        const s = document.createElement("script");
        s.src = src;
        s.dataset.voiceSrc = src;   // voice-room.js's loadScript skips a script already added this way
        s.onload = s.onerror = () => resolve();
        document.head.appendChild(s);
    });
}

/**
 * Loads the examiner's scripts and, when this device will show the 3D examiner (3D graphics, no reduced-motion preference, not on a
 * data saver), downloads the model in the background. Returns at once; the work goes on while the student reads the consent notice.
 */
export function preload(cfg) {
    if (started) return true;
    started = (async () => {
        await Promise.allSettled([
            import(`${LIB}/speech/index.js`),
            import(`${LIB}/avatar/index.js`),   // brings three.js and TalkingHead through the import map
            import(`${LIB}/talkinghead/modules/lipsync-en.mjs`),
            script(`${LIB}/signalr/signalr.min.js`),
            import("/js/voice-room.js"),   // the exam room's own module (GAD-T317): its dependencies above were already
                                            // preloaded, but the module itself was not, so the exam room still paid for it
        ]);
        const saveData = !!(navigator.connection && navigator.connection.saveData);
        if (!cfg || !cfg.modelUrl || saveData || reducedMotion() || !webgl()) return;
        try {
            (await import("/js/model-cache.js")).prefetch(cfg.modelUrl);   // into Cache Storage and this page's memory (GAD-T698)
        } catch { /* the exam room loads it, or shows her presence */ }
    })();
    return true;
}
