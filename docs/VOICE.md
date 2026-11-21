# Voice exam room and audio signals (GAD-T53, GAD-T58)

A voice exam is taken out loud: the examiner (a talking head, `<ma-avatar>`) speaks each question with lip sync and
captions, the student answers by voice with a live transcript, and the answer goes through the same response ledger,
transcript read-back and judging. Since GAD-T239 every exam is taken this way: voice and camera are mandatory and there is
no typed answer mode.

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
  again**; the examiner repeats the question. The clock keeps running while paused. No 3D graphics (or reduced motion)
  shows the 2D portrait and the examiner still speaks. If the avatar fails entirely, questions are shown as text and
  answers are still spoken.
- Only small transcript corrections are typed (fixing words the recognizer got wrong); "I want to change my answer"
  (a typed substantive revision) is not offered in the exam room.

## The interview room (GAD-T231)

With voice on, the exam room reads like an interview. Only the student's exam room changes; instructor and teacher
screens are untouched.

- **Self-view.** The student's own camera is shown, mirrored, beside the talking examiner. The stream is attached to a
  muted `<video>` in the page and nothing else: no frame is recorded, stored or sent (camera integrity checks, GAD-T57,
  are separate and unchanged). The one thing read from it, on the device, is where the student's face is, so the examiner
  can look at them (below). A refused or missing camera shows a short note and voice carries on; **Hide my camera**
  releases it and **Show my camera** asks again.
- **Transcript.** Every question or request the examiner speaks (in full, even when a long read-back is spoken
  shortened) and every answer the student speaks is added to an on-screen transcript, newest at the bottom. Turning voice
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

## Eye contact with the examiner (GAD-T240)

`ExaminerGaze` (hosted by `WebcamIntegrity` in the exam room, `wwwroot/js/examiner-gaze.js`) runs while the talking examiner
is on screen, for any exam whose integrity level isn't Off. It uses its own camera stream, analysed on the device only by
MindAttic.Webcam (presence, head-and-eye direction, reading pattern); no frame, image or landmark leaves the browser.

- **Calibration.** When it starts (and on **Set up eye contact again**), the examiner's stage is outlined and the student is
  asked to look at the examiner's face; that direction becomes "looking at the examiner". The allowed region is sized from the
  avatar's on-screen size.
- **Live indicator.** "Looking at the examiner", "Looking away from the examiner", reading, no face, or more than one face.
- **While answering** (the avatar is listening): after `fadeStartMs` (2 s) of continuous look-away the whole exam screen starts
  fading toward a heavy dim and blur over `fadeRampMs` (3 s) with "Please look at the examiner", and restores smoothly
  (`fadeRestoreMs`) when the gaze returns. After `remarkAfterMs` (5 s), or at once for a reading pattern, no face or a second
  face, the examiner says so out loud ("Please look at me while you answer"), at most once per `remarkGapMs` (15 s). The words
  are fixed server-side (`WebcamIntegrityRecorder.ExaminerRemarks`) and offered to the avatar hub like any question.
- **Recorded for review.** Each episode is a webcam integrity event with start and end: `examiner-look-away`,
  `examiner-reading`, `examiner-face-absent`, `examiner-second-face`, and `examiner-fade` (with its peak). Summaries are
  written by the server (the browser sends only "remarked" or "peak:NN"); confidence at most 0.4, severity at most Low, never
  a penalty (`WebcamIntegrityRecorder.RecordExaminerGazeAsync`).
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
   `SpeechRecognitionConfidence`), and asks the existing neutral question **"I recorded your answer as ... Is this what you
   said?"**. Gadfly always asks this of a spoken answer (master plan 4.3, type A); the library's advice is recorded in the
   request's reason. Confirming says only that the wording matches, never that the answer is right.
   "Repeat my answer" can be spoken; a meaning clarification can be answered by voice.
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
| `strain.vsBaseline` | mean of the rises, each scaled so its notable point is 1: 0 at baseline, 1 clearly above | read by `AnswerDelivery` (GAD-T233), cue at 0.5 |

The review transcript shows one descriptive line per spoken answer ("possible vocal strain / hesitation compared with this
student's first answers: ...") with its caveat, hidden from blinded graders; the student's live interview transcript shows a
short form. These describe the voice, never emotion, honesty or deception; they are not integrity events and never change a grade.
