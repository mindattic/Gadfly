# Voice exam room and audio signals (GAD-T53, GAD-T58)

A voice exam is taken out loud: the examiner (a talking head, `<ma-avatar>`) speaks each question with lip sync and
captions, the student answers by voice with a live transcript, and the answer goes through the same response ledger and
judging. Since GAD-T239 every exam is taken this way: voice and camera are mandatory and there is no typed answer mode
(except the typed-answer accommodation, GAD-T260). Since GAD-T405 a spoken answer is taken as heard: it is not read back,
confirmed, corrected, said again or changed (see step 5 under "How an answer flows").

## Who gets it

- Every exam, whatever mode the instructor picked (GAD-T239). The consent notice every student accepts describes the
  microphone, the speech-to-text service and the camera. Where the deployment has voice off (`Gadfly:Voice:Mode`), the
  start page's system check fails ("Spoken examiner") and the exam can't begin.
- **Preflight.** The start page opens the microphone and the camera (`system-check.js` `startMedia`): the camera must
  deliver a picture and the microphone must hear the student speak before **Begin exam** is enabled. The practice
  question is answered out loud too.
- **No extra click.** **Begin exam** goes straight into the talking examiner: the exam room starts it on load
  (`voice-room.js` `autoStart`, which relies on the Begin exam click as the page's user activation; a page opened fresh,
  such as a reload, asks for one **Continue to the examiner** click).
- **The clock.** A new session holds its clock (`SocraticSession.HoldClock`) and the exam room starts it
  (`SessionOrchestrator.StartClockAsync`) when the examiner begins the first question, so planning it and connecting the
  devices don't use the student's time. It starts once; until then `Remaining` still counts from session creation, so a
  clock that never starts can't give extra time. The question is not shown until the examiner asks it.
- **Pause.** A refused, missing or lost microphone or camera (a track that ends mid-exam), or a lost hub, pauses the
  exam: the question and answer controls are hidden and the student is told how to restore it, then presses **Try
  again**; the examiner repeats the question. The clock stops while paused (GAD-T263): the exam room calls
  `SessionOrchestrator.PauseClockAsync` when the devices go and `ResumeClockAsync` when they are back, and the paused
  time is added back to `Remaining`. A pause is reported by the student's browser, so the time added back is capped per
  exam (`SocraticSession.PauseAllowance`: 10 minutes, or half the budget if shorter), it only counts after the clock
  started, and any answer ends an open pause by the server's clock. No 3D graphics (or reduced motion)
  shows the 2D portrait, whose mouth moves as she speaks. If the avatar fails entirely, questions are read out by the
  browser's own speech and shown as text, and answers are still spoken (see "The examiner's delivery").
- Nothing about a spoken answer is typed or redone (GAD-T405, GAD-T407; owner decision 2026-11-22): no transcript
  correction, no "Repeat my answer", no "I want to change my answer". What the recognizer heard is what is judged, and the
  exam moves on; a second, considered pass would change the Socratic exchange. Only a typed answer under the GAD-T260
  accommodation can still get a small typo fix or a meaning clarification.

## The interview room (GAD-T231)

With voice on, the exam room reads like an interview. Only the student's exam room changes; instructor and teacher
screens are untouched.

- **Self-view.** The student's own camera is shown, mirrored, beside the talking examiner. The stream is attached to a
  muted `<video>` in the page and nothing else: no frame is recorded, stored or sent (camera integrity checks, GAD-T57,
  are separate and unchanged). The one thing read from it, on the device, is where the student's face is, so the examiner
  can look at them (below). A refused or missing camera shows a short note and voice carries on; **Hide my camera**
  releases it and **Show my camera** asks again.
- **Transcript.** Every question the examiner speaks and every answer the student speaks is added to an on-screen
  transcript, newest at the bottom. Turning voice
  off and on doesn't repeat a question already listed.
- **Response delay.** When the examiner finishes and the microphone opens, "Response delay" counts up live and freezes
  when the student starts speaking; the answer's transcript line keeps it. It is the browser's timing, sent with the
  answer as `ResponseDelayMs` for display only: it is never scored or stored (the stored `timeToFirstWordSeconds` is still
  recomputed from the words on the server).
- **Examiner presence (GAD-T241).** The 3D examiner holds eye contact while she speaks and while she listens, renewed
  every couple of seconds so her idle wandering never takes her eyes away for long. With the self-view on, she turns
  toward the student's face: the browser's `FaceDetector`, or else MindAttic.Webcam's self-hosted face landmarker on a
  small 10 fps stream, finds the face on the device and only a smoothed position reaches `<ma-avatar>`
  (`setGazeTarget`); nothing is stored or sent, and with no face she looks straight out of the screen. While the student
  talks (the microphone's voice activity, `setStudentTalking`) she listens visibly: an attentive expression, small nods
  while they talk and at their pauses, the odd extra blink. Only after the answer is in does she take one brief
  (about a second) thinking look up and aside, then looks back at the student.

## The examiner's delivery (GAD-T303)

How she looks and sounds while she speaks. The avatar library (`<ma-avatar>`, MindAttic.Avatar) does the 3D head, streaming and
word-timed lip sync; `wwwroot/js/examiner-voice.js` adds the rest, and `voice-room.js` tells it when each utterance is prepared,
starts and ends.

| What | How |
|---|---|
| Lip sync | The library asks ElevenLabs for speech **with timestamps** (character alignment), turns the characters into word timings and streams PCM with them; TalkingHead shapes the mouth from each word's sounds over that word's provider-measured time. Per-character (phoneme) timing would be finer: filed as MAA-T9. |
| Starting quickly | Speech is streamed: she starts on the first audio chunk. The examiner's voice model defaults to ElevenLabs' low-latency `eleven_flash_v2_5` unless `MindAttic:Avatar:ElevenLabs:ModelId` is set (`Gadfly:Voice:TtsModel` changes the default). Faster first audio options are MAA-T15. |
| Prepared ahead | Fixed lines (the eye-contact remarks, the wait fillers) are synthesized once per server into the library's phrase cache when the first exam room opens (`ExaminerSpeechWarmup`, off with `Gadfly:Voice:WarmPhrases=false`), so they start at once. Questions are cached after their first playing, so **Repeat the question** and a reload replay instantly. The next question itself is written only after the answer is judged; with no read-back (GAD-T405) judging starts the moment the answer is in, and the wait fillers cover that wait after every answer. A supported prefetch is MAA-T14. |
| Speaking style | A fixed server-side map from the kind of question to one of three styles (`Voice/ExaminerStyle.cs`): `open` (opening a topic or a new situation: brows lifted a little, a slight tilt), `probe` (following up: a small lean and the opposite tilt) and `neutral` (requests, closings, hints, fillers, remarks). All three keep the same mild, friendly baseline; none smiles more or frowns, and a counterexample or contradiction (which tend to follow a weak answer) looks exactly like any probe, so her face never hints at the grade. A public style API is MAA-T10. |
| Captions | The whole question is shown at once, dimmed, and each word lights as she says it, counted from the moment her audio actually starts, on the provider's word timings. |
| Portrait | With no 3D (reduced motion, no WebGL, model missing) the portrait is shown and a mouth drawn over its lips opens and closes once per syllable with the same word timings (`Gadfly:Voice:PortraitMouth`, "x,y,w" as fractions of the image; known for the default portrait, unset for a custom one, which then only brightens). In the library: MAA-T11. |
| Quality | The 3D examiner's frame rate is watched: below about 22 fps for two 3-second windows her render resolution and animation rate step down (high, medium, low); still under 12 fps at the lowest, she becomes the portrait, between sentences, never mid-sentence. The tier is kept for the browser tab, so a reload starts where it ended. In the library: MAA-T12. |
| Lost graphics | A lost WebGL context shows the portrait at once; her voice carries on and the 3D head is released when she next stops speaking. A model that fails to load (404, bad file) is the library's portrait fallback. |
| Pause and resume | Hiding the tab mid-sentence pauses her voice, mouth and captions together; showing it resumes them where they were. A lost microphone, camera or connection pauses the exam (above): she stops, and on **Try again** she asks the question again from the start. In **hold-to-talk** mode, pressing **Hold to talk** while she is asking interrupts her and opens the microphone; hands-free mode does not interrupt on the student's voice, because her own voice from the speakers could trigger it. Library pause/resume: MAA-T13. |
| Voice failure | If the voice provider fails (an error from the hub, a refused request, or no sound within 9 seconds), the browser's own speech reads the question, with the caption lit and the portrait mouth moving on its word boundaries; a browser with no speech leaves the question on screen as text and says so. If the talking head itself can't start, questions are read by the browser's speech the same way. |
| Preloading | The get-ready page loads the examiner's scripts and, on a device that will show 3D (no reduced motion, WebGL, no data saver), downloads the avatar model in the background (`wwwroot/js/voice-preload.js`), so the exam room shows her without a long first load. |
| Mock mode | `MockTextToSpeech` answers in the provider's own with-timestamps shape (chunked NDJSON, base64 PCM 22.05 kHz, character alignment, chunks ending mid-word), with one soft burst per syllable and pauses at commas and full stops, so mock mode runs the same lip-sync, caption and portrait-mouth path as live. |

## Eye contact with the examiner (GAD-T240)

`ExaminerGaze` (hosted by `WebcamIntegrity` in the exam room, `wwwroot/js/examiner-gaze.js`) runs while the talking examiner
is on screen, for any exam whose integrity level isn't Off. It uses its own camera stream, analysed on the device only by
MindAttic.Webcam (presence, head-and-eye direction, reading pattern); no frame, image or landmark leaves the browser.

- **Where the student looks (GAD-T305).** Two methods, the better one whenever it can be trusted:
  - *Calibrated:* a gaze vector from the iris position in each eye plus the head pose (MediaPipe's facial transformation
    matrix), followed to the camera's plane (`gaze-quality.js` `gazePoint`), then mapped onto the page by a five-dot
    calibration (the examiner's face, or the page centre, plus the four corners). A straight line per axis absorbs where the
    camera is, the screen's size and zoom, and the student's own eye gain. Accuracy is measured honestly: each dot is left
    out and predicted from the other four. Worse than `calibPoorDeg` (6°) gets one retry, then the calibration is not used.
    The examiner's region is her stage as it is on screen right now plus a margin of `marginDeg` (3°) and shares of the
    measured accuracy and noise, capped at `maxMarginDeg` (9°). Leaning, sitting back or turning the head while still looking
    at her is not a look-away (the head point and the eye turn move in opposite directions). A look-away also needs the old
    head-and-eye direction to have strayed `agreeShare` (half) of its limits, so neither estimate's error alone makes one.
    While the examiner speaks, a steady offset of more than `driftMinDeg` within `driftWindowDeg` of her face is corrected
    by half, quietly (drift).
  - *Head direction (the old behaviour):* head-and-eye direction against a baseline learned while the student looks at the
    examiner's face, with the region sized from the avatar's on-screen size. It is used when there is no calibration, it was
    poor or skipped, the room's first look at the examiner disagrees with it by more than `anchorMaxDeg` (7°), or the stage
    is scrolled mostly out of view.
- **Setup.** The get-ready page offers the five dots (about 10 seconds; see below) and keeps the fit, four numbers and its
  accuracy, in that browser tab's `sessionStorage` for up to `modelMaxAgeMs` (2 h) at the same zoom. In the room, the student
  is asked to look at the examiner's face (the stage is outlined): that learns the head-direction baseline and checks the
  stored fit against her face, taking her face as the reference from then on. **Set up eye contact again** runs the five
  dots in the room, then that look. Esc or Skip stops the dots and keeps what was in use. Under the eye-contact accommodation
  there are no dots and no prompt.
- **Get-ready check.** Once the camera shows a picture, `system-check.js` starts `gaze-quality.js` in the page's eye-tracking
  panel. It watches about 4 seconds of frames and gives plain advice, never a failure: face in view, lighting (dark, or a
  bright light behind the student, from a 32x24 brightness thumbnail read and dropped), camera angle, distance and centring,
  how steadily the eyes can be followed (glare on glasses is the usual cause), and device speed. It never blocks Begin exam.
- **Slow devices.** Timings follow the measured frame rate (the baseline is `baselineMs` long, a sample is stale after three
  frame times). Under `slowFps` (12 frames a second) the module asks its own copy of the camera for `slowDeviceFps` (10), so
  the face model runs less often; the self-view keeps its own rate. The Strict exam's separate camera check asks for
  640x480 at 15 frames a second. (Running the face model in a worker needs MindAttic.Webcam: ET-T18.)
- **Lost camera, no face.** No camera frame for `cameraLostMs` (3 s): the indicator says eye contact isn't being checked and
  nothing counts as a look-away. With no face for `calibrationGiveUpMs` (15 s) the prompt stops and comes back when a face
  does.
- **Live indicator.** "Looking at the examiner", "Looking away from the examiner", reading, no face, more than one face, the
  camera picture stopped, or "can't follow your eyes right now".
- **While answering** (the avatar is listening): after `fadeStartMs` (2 s) of continuous look-away the whole exam screen starts
  fading toward a heavy dim and blur over `fadeRampMs` (3 s) with "Please look at the examiner", and restores smoothly
  (`fadeRestoreMs`) when the gaze returns. After `remarkAfterMs` (5 s), or at once for a reading pattern, no face or a second
  face, the examiner says so out loud ("Please look at me while you answer"), at most once per `remarkGapMs` (15 s). The words
  are fixed server-side (`WebcamIntegrityRecorder.ExaminerRemarks`) and offered to the avatar hub like any question.
- **Recorded for review.** Each episode is a webcam integrity event with start and end: `examiner-look-away`,
  `examiner-reading`, `examiner-face-absent`, `examiner-second-face`, and `examiner-fade` (with its peak). Summaries are
  written by the server (the browser sends only codes: "remarked" or "peak:NN", then the quality below); confidence at most
  0.4, severity at most Low, never a penalty (`WebcamIntegrityRecorder.RecordExaminerGazeAsync`).
- **Quality score (GAD-T305).** Every episode carries the session's tracking quality so far, 0..100, worked out on the device
  (`quality()` in `examiner-gaze.js`, weights in `GAZE.qWeights`): calibration accuracy (the worse of the leave-one-out figure
  and the room's first look), steadiness, the share of answering time with a usable gaze sample, time without a lighting or
  framing warning, and frame rate, minus a little per drift correction. Head direction only scores at most `qHeadMax` (50).
  The browser sends `q:NN;m:cal|head;a:N.N`; the server (`GazeQuality`) bounds it, appends "Eye-tracking quality this session:
  good / fair / poor (NN/100; set up to the student's screen, about N° accuracy | head and eye direction only)" to the summary
  the reviewer reads, and keeps the numbers in the event's `DataJson`. A poor score (under 40) keeps a look-away or reading
  note at Info. The confidence sent for look-away, reading and fade runs from 0.15 to 0.4 with the score. It says how far to
  trust the note, nothing more.
- All thresholds are in one place: the `GAZE` object at the top of `examiner-gaze.js`.

## How an answer flows

1. `voice-room.js` opens the microphone from the click (hands-free voice activity detection, or hold-to-talk), streams
   16 kHz PCM through `/hubs/speech` (MindAttic.SpeechRecognition relay) and shows partial and final transcript text.
2. Audio is not sent while the examiner is speaking (no echo, no self-transcription).
3. The end-of-answer cue: after a final segment, a stretch of silence submits the answer, with a visible countdown;
   speaking again cancels it; **Done answering** submits at once; **Clear** discards. The silence starts at
   `Gadfly:Voice:EndOfAnswerSeconds` (default 4) and adapts to the student's own pauses (GAD-T264): once three pauses
   within answers have been seen, it becomes 1.5 times the student's longer pauses (80th percentile of the last 20), never
   below that floor and never above `Gadfly:Voice:EndOfAnswerMaxSeconds` (default 12). An extra-time accommodation
   multiplies both the floor and the ceiling (capped at 45 s), so a student with 1.5x time waits 6 s at least.
4. `VoiceAnswerBuilder` rebuilds the segments server-side, recomputes `SpeechMetricsCalculator` and
   `TranscriptVerification.Assess` from the words (the browser's numbers are not trusted), and hands `ExamRoom` an
   `Answer { Mode = "voice" }` with word timings, flattened metrics (`SpeechMetricKeys`) and confidence.
5. `SessionOrchestrator.AnswerAsync` captures a `StudentResponse` (`Modality = "voice"`, `SpeechRecognitionText`,
   `SpeechRecognitionConfidence`) and judges it at once, as heard (GAD-T405, owner decision 2026-11-22). Until then
   Gadfly read every spoken answer back ("I recorded your answer as ... Is this what you said?", master plan 4.3 type A)
   and let the student confirm, correct or repeat it; the owner removed that because a second, considered pass changes the
   Socratic exchange. No meaning clarification is asked of a spoken answer either, and it can't be typo-fixed or changed
   afterwards. The recognizer's doubts are kept for reviewers instead: an audit entry (`response.verification.checked`)
   records that the answer was taken as heard, whether any words had low confidence and whether the library advised
   checking, and the uncertain words stay marked on the capture (GAD-T265). A read-back request created before the change
   is answered as heard by the exam room on its own, so an exam already in progress carries on.
6. Confidence is stored only when the provider gives it for every word. The Simulated recognizer gives none, so its answers
   carry no confidence rather than a made-up one.

## Security

- Both hubs require the **Student** policy, the hub rate limit, and a server-side grant (`VoiceGrants`) that the exam room
  creates after it has checked the session is the student's and running. A hub filter (`VoiceHubFilter`) refuses
  connections without a grant, and refuses `Speak` for any text the server did not offer for that student: the avatar hub
  cannot be used as a free text-to-speech endpoint.
- Provider keys stay on the server: ElevenLabs key from Vault (`MindAttic:Vault:LLM:elevenlabs:apiKey`, already loaded),
  voice id from `MindAttic:Avatar:ElevenLabs:VoiceId`. The browser can't pick a provider or a voice.
- The transcript reaches Gadfly through the student's browser (server to browser to server), the same trust model as the
  existing `SessionHub.Answer`. The metrics and verification advice are recomputed on the server from the relayed words.
  A server-side tap on the recognizer (so the browser never carries the text) would need a hook in the speech library.

## Configuration

| Setting | Meaning |
|---|---|
| `Gadfly:Voice:Mode` | `off`, `mock` or `live`. Default `mock` in Development, `off` elsewhere. |
| `Gadfly:Voice:AvatarModelUrl` | The avatar GLB. Default `/assets/avatars/mpfb.glb` when the file exists, otherwise the 2D portrait. |
| `Gadfly:Voice:PortraitUrl` | 2D fallback image (default `/assets/portrait.png`). |
| `Gadfly:Voice:PortraitMouth` | Where the portrait's mouth is, "x,y,w" as fractions of the image (set for the default portrait; unset for a custom one means no drawn mouth). |
| `Gadfly:Voice:TtsModel` | The examiner's ElevenLabs voice model when `MindAttic:Avatar:ElevenLabs:ModelId` isn't set (default `eleven_flash_v2_5`, low latency). |
| `Gadfly:Voice:WarmPhrases` | Prepare the fixed examiner lines in the phrase cache when the first exam room opens (default true). |
| `Gadfly:Voice:EndOfAnswerSeconds` | Shortest silence that ends an answer (2 to 15, default 4). |
| `Gadfly:Voice:EndOfAnswerMaxSeconds` | Longest the silence may adapt to for a student who pauses long (up to 30, default 12). Both are multiplied by the extra-time accommodation. |
| `MindAttic:SpeechRecognition:Provider` | Live only: `ElevenLabs` (default in live), `OpenAI` or `Fallback`. |
| `MindAttic:Avatar:ElevenLabs:VoiceId` | Live only, required (without it voice stays off). |

**Mock mode** uses the library's Simulated recognizer (placeholder words, "speech", timed to the microphone) and an
in-process fake text-to-speech (`MockTextToSpeech`, the avatar client's primary HTTP handler, so no request can leave the
process). Nothing is billed. **Live mode** is the only mode that reaches a paid provider; it must be switched on
deliberately.

## Assets

`Gadfly.Server/wwwroot/lib/voice` holds the browser pieces (three.js, TalkingHead, the SignalR client, and the built
`@mindattic/avatar` and `@mindattic/speechrecognition`), about 3 MB, committed so CI needs no npm or sibling checkout.
Refresh with `tools/sync-voice-assets.ps1`. The 36.8 MB default avatar `mpfb.glb` (CC0) is **not** committed: the project
copies it from a sibling `MindAttic.Avatar` checkout at build time when present (gitignored); without it the portrait is
shown. For production, host the model on blob storage or a CDN and set `AvatarModelUrl`.
`App.razor` carries the import map the avatar needs. No Content-Security-Policy is sent on exam pages today; if one is
added it needs `worker-src blob:` (the microphone worklet), `connect-src 'self' wss:` and `wasm-unsafe-eval`.

## Audio integrity signals (GAD-T58)

`AudioIntegritySignals` turns the speech metrics of a spoken answer into `IntegrityEvent` rows (source `audio`), stored with
the answer, once per type per turn. They are annotations for a human reviewer: never a score, never a penalty, never a
statement about honesty, confidence at most 0.6, severity at most Low, each with its numbers in the summary and in
`DataJson`. They appear on the review page with the other integrity events and in the integrity summary.

| Event | When |
|---|---|
| `second-speaker` | Diarization attributes at least 3 words to a voice other than the main speaker. |
| `reading-cadence` | At least 25 words, at least 4 reading-aloud features measured, and all but one point to reading (regular pauses, steady pace, pauses at punctuation, long runs between pauses, no hesitations). |
| `fluent-after-silence` | At least 10 s before the first word, then at least 25 words with no hesitations, repeats or long pauses; stronger when the student's earlier spoken answers were hesitant. |

**Gap: whispered prompting.** The library's metrics carry no loudness or signal level and diarization does not mark quiet
speech, so a whisper cannot be told from silence. A whisper that the recognizer attributes to another speaker is reported as
`second-speaker`. The review page already has wording for a `whispering` event for when a source exists (for example a level
feature in the speech library). Accent, language background, nerves and accommodations all move these numbers (master plan
15.1), which is why the review wording calls them weak evidence.

## Voice strain indicators (GAD-T232)

During each spoken answer `wwwroot/js/voice-acoustics.js` measures the student's own voice from the same 16 kHz audio that is
streamed to the recognizer (40 ms frames, autocorrelation pitch tracking between 75 and 400 Hz): median pitch, pitch
variability (SD in semitones), jitter (mean frame-to-frame pitch-period change, a proxy for cycle-to-cycle jitter), loudness
(mean and SD of dBFS over voiced frames; the microphone runs without automatic gain) and voiced time. Only these numbers go
with the answer (`VoiceAnswerPayload.Acoustics`); no audio, frame or contour. The server range-checks them and stores them as
`voice.*` speech metrics (`VoiceAcousticKeys`).

`VoiceStrain.ApplyAsync` (called by the exam room before the answer is captured) compares each answer with the student's first
two spoken answers in the session and stores the differences as `strain.*` metrics:

| Key | Meaning | Notable at |
|---|---|---|
| `strain.isBaseline` | 1 for the first two answers (the baseline), else 0 | |
| `strain.pitchSemitones` | pitch change | +2 semitones |
| `strain.pitchVariabilityRatio` | pitch SD against baseline | 1.5x or 0.67x |
| `strain.jitterRatio` | jitter against baseline | 1.5x |
| `strain.loudnessDb` | loudness change | +/-4 dB |
| `strain.speechRatePercent` | words per minute change | 25% slower |
| `strain.pauseRatioDelta` | share of the answer spent pausing | +0.10 |
| `strain.vsBaseline` | mean of the rises, each scaled so its notable point is 1: 0 at baseline, 1 clearly above | recorded for reviewers; not a probing cue since GAD-T258 (it was one at 0.5 under GAD-T233) |

The review transcript shows one descriptive line per spoken answer ("possible vocal strain / hesitation compared with this
student's first answers: ...") with its caveat, hidden from blinded graders; the student's live interview transcript shows a
short form. These describe the voice, never emotion, honesty or deception; they are not integrity events, never change a grade,
and since GAD-T258 never change how hard the examiner probes (that comes from what the answer says, `AnswerDelivery`).
