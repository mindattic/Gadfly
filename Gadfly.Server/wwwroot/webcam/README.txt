Self-hosted browser build of MindAttic.Webcam (@mindattic/webcam 0.2.0) for the exam room's camera integrity checks (GAD-T57).
Nothing here is fetched from a CDN at exam time, so the student's browser contacts no third party.

webcam.browser.js          dist/webcam.browser.js of MindAttic.Webcam (npm run build there)
vision_wasm_internal.*     @mediapipe/tasks-vision 1.2.3 wasm runtime (Apache-2.0, Google)
face_landmarker.bin        MediaPipe face_landmarker.task model (Apache-2.0, Google); renamed because ASP.NET
                           static files serve no unknown extension and .task has no MIME mapping

To refresh: rebuild MindAttic.Webcam and copy the same three groups of files here. Video never leaves the device.
