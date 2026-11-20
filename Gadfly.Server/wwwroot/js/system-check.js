// Pre-exam browser check (GAD-T51). Loaded as a module by the start page; reports what the browser
// supports. Microphone and camera checks arrive with voice mode (SR-T6, ET-T14).
export function check() {
    const w = window;
    return {
        userAgent: navigator.userAgent || "",
        webSocket: "WebSocket" in w,
        cookies: navigator.cookieEnabled === true,
        online: navigator.onLine !== false,
        secureContext: w.isSecureContext === true,
        speechRecognition: !!(w.SpeechRecognition || w.webkitSpeechRecognition),
        mediaDevices: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
        screenWidth: w.innerWidth || 0,
    };
}

export function ping() {
    return true;
}
