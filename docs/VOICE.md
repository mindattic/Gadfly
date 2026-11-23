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
- **Pause.** A refused, missing or lost microphone or camera (a track that ends mid-exam), a lost hub (the speech
  recognizer's or, since GAD-T306, the examiner's own avatar hub too), or a closed laptop lid (GAD-T306: the tab
  hidden for a stretch long enough to be a sleep, not a quick tab switch, with its devices then found dead) pauses the
  exam: the question and answer controls are hidden and the student is told how to restore it, then presses **Try
  again**; the examiner repeats the question, and pressing it re-requests the microphone and camera from scratch
  rather than assuming whatever was open before still works. The clock stops while paused (GAD-T263): the exam room calls
  `SessionOrchestrator.PauseClockAsync` when the devices go and `ResumeClockAsync` when they are back, and the paused
  time is added back to `Remaining`. A pause is reported by the student's browser, so the time added back is capped per
  exam (`SocraticSession.PauseAllowance`: 10 minutes, or half the budget if shorter), it only counts after the clock
  started, and any answer ends an open pause by the server's clock. No 3D graphics (or reduced motion)
  shows the 2D portrait, whose mouth moves as she speaks. If the avatar fails entirely, questions are read out by the
  browser's own speech and shown as text, and answers are still spoken (see "The examiner's delivery"). See
  "Resilience" below for what a dropped connection to the page itself (not just a device or a hub) does, and for the
  words-already-spoken draft a reload or a crash no longer loses.
- Nothing about a spoken answer is typed or redone (GAD-T405, GAD-T407; owner decision 2026-11-22): no transcript
  correction, no "Repeat my answer", no "I want to change my answer". What the recognizer heard is what is judged, and the
  exam moves on; a second, considered pass would change the Socratic exchange. Only a typed answer under the GAD-T260
  accommodation can still get a small typo fix or a meaning clarification.

## Resilience (GAD-T306)

A dropped connection, a reload mid-answer, a closed laptop lid or an abandoned session should never cost a student
their attempt or leave them staring at a frozen page.

- **A dropped connection to the page itself.** The Blazor circuit that runs the exam room is a different connection
  from the two voice hubs above: losing it means nothing on the page can do anything server-side until it comes back
  (there is no way to push a render while it's down). Blazor's own reconnection UI (`#components-reconnect-modal`,
  styled in `app.css`) shows a clear "Reconnecting…" status instead of a blank or frozen page; "That's taking too
  long" once its own retries are exhausted, with a "Try again" that retries once more or reloads. Once the circuit is
  back, `CircuitReconnectSignal`/`ReconnectCircuitHandler` (`Gadfly.Server/Sessions/CircuitReconnectSignal.cs`) tell
  the exam room to re-run its load path (`ExamRoom.OnReconnectedAsync`) and fetch the server's current step again,
  rather than carrying on with whatever it last rendered, which may be stale (a staff pause or extension, or the exam
  having ended, while the connection was down).
- **Words already spoken, kept as a draft.** `voice-draft.js` keeps the final (committed) segments of the answer so
  far in `sessionStorage`, keyed by session and turn, updated as each one arrives and cleared on submit or when the
  question changes. If the page reloads, or the browser crashes and is relaunched, mid-answer, `voice-room.js`
  restores them into "What I heard" the next time the microphone opens for that same question, clearly marked
  ("Recovered what you'd already said…"). Restoring only ever fills the answer box: the end-of-answer cue is never
  armed by it, so nothing is sent until the student presses **Done answering** (or starts over with **Clear and
  answer again**) themselves. A different question, a different session, or a draft older than 30 minutes is never
  restored.
- **A staff pause closes the microphone (GAD-T471).** When staff pause the exam from the live monitor (GAD-T299), the server
  pushes it to the room through the live feed (`StaffNoticeBanner`, which shows "Your exam is paused"), and the exam room
  follows it: `VoiceRoom` gets `CanAnswer = false` with `HeldByStaff`, and `voice-room.js` closes the microphone gate, drops the
  end-of-answer countdown (nothing is sent while answers would be refused) and says "Your instructor paused the exam…". Speech
  in progress is committed so its words land in "What I heard"; everything already said stays there and in the reload draft.
  On release the microphone opens again for the same question with those words still in place, the answer's listening start
  and response delay unchanged, and nothing is sent until the student carries on and pauses, or presses **Done answering**.
- **An answer that arrives at a bad moment is kept, never dropped (GAD-T473).** `VoiceRoom.OnVoiceAnswer` returns whether the
  exam room took the answer (`ExamRoom.OnVoiceAnswerAsync`). It is not taken while another step is running (the reconnect
  re-load above, "I don't know", Finish), during a staff pause, when the server refuses it for the moment (the reason is shown),
  or when it names another question. Then `voice-room.js` keeps the words in "What I heard" and in the reload draft, says
  "Your answer is kept. It will be sent as soon as the examiner is ready.", and sends it again as soon as an answer is wanted for
  the same question (and once more after 1.5 s if the page already wants one); after three refusals in a row it stops sending
  on its own and leaves **Done answering** to the student. The answer carries the question the browser heard asked (`turn`,
  GAD-T321), so a kept answer can never become the next question's answer: if the exam moves to another question first, the
  words are dropped with a plain note ("Your last answer came in after the exam had already moved on…"). Its transcript line
  is only kept once the answer is taken, so it never appears twice.
- **Repeat the question keeps what was already said (GAD-T474).** Pressed mid-answer (or during a staff pause), the words
  already spoken for that question stay in "What I heard" and in the reload draft instead of being wiped; speech still in progress
  is committed and its late final joins them while she speaks. When she finishes, the microphone opens on the same answer
  (listening start and response delay unchanged) and nothing is sent until the student carries on and pauses, presses **Done
  answering**, or clears them with **Clear and answer again**. **I don't know** is disabled while staff hold the exam (with a
  tooltip saying why) and comes back when they resume.
- **Bounded waits, with a plain retry.** Every hub connect and its first call (`voice-room.js`, `CONNECT_TIMEOUT_MS`,
  15 s) and the exam room's own JS startup (`VoiceRoom.OnAfterRenderAsync`, 20 s) are bounded, the same house style as
  the get-ready page's checks (`StartExam.razor`, GAD-T288): past the bound, the student sees "That's taking too
  long" rather than a silently stuck "Connecting you to the examiner…". A provider call from the panel is bounded by
  its own `ProviderTimeout` (`LegionVotingClient`), which already surfaces as the exam room's existing "Something went
  wrong on our side… press Try again" on a step.
- **Stale sessions.** A student who closes the tab and never comes back (no pause, no staff action, no reload) leaves
  a session sitting `Running` or `Paused` with nothing to ever call `SessionOrchestrator.EndAsync` for it.
  `StaleSessionSweep` (queued periodically by `StaleSessionScheduler` onto the job queue, `session.stale-sweep`)
  finds sessions sitting this way well past their own deadline (time budget, any staff extra time and pause credit
  already included) plus a 30-minute grace, and closes them through the ordinary end path — graded exactly as a real
  time-up would be, counted fairly as a used attempt — with an audit note ("closed: abandoned past time limit") on the
  session's review timeline. A session staff are actively holding (GAD-T299) is left alone: that is a deliberate,
  visible action with its own generous allowance, not an abandonment, and the live monitor (`/admin/live`) already
  shows and resolves it.

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
| Starting quickly | Speech is streamed: she starts on the first audio chunk. The next question is spoken while it is still being written (GAD-T495, below): she starts on its first guarded sentence. The examiner's voice model defaults to ElevenLabs' low-latency `eleven_flash_v2_5` unless `MindAttic:Avatar:ElevenLabs:ModelId` is set (`Gadfly:Voice:TtsModel` changes the default). Faster first audio options are MAA-T15. Quality-vs-latency tiers are below (GAD-T415). |
| Prepared ahead | Fixed lines (the wait fillers first, then the eye-contact remarks) are synthesized once per server into the library's phrase cache when the first exam room opens (`ExaminerSpeechWarmup`, off with `Gadfly:Voice:WarmPhrases=false`), so they start at once. The cache is bounded and questions go into it too, so an exam room opened ten minutes or more after the last check checks the lines again: a line still cached costs nothing (and is kept), and only one pushed out is synthesized again (GAD-T329). Questions are cached after their first playing, so **Repeat the question** and a reload replay instantly. The next question itself is written only after the answer is judged; with no read-back (GAD-T405) judging starts the moment the answer is in, and the wait fillers cover that wait after every answer. A supported prefetch is MAA-T14. |
| Acknowledging the answer | The moment an answer is in (the end-of-answer silence or **Done answering**), she says a short acknowledgement, the backchannel a listener gives when someone stops talking (GAD-T496): "Mm, okay.", "Okay.", "Mm-hm.", "I see.", "Okay, thank you.", "Mm. Okay." or "Thank you." (`ExaminerFiller.Acknowledgements`). Receipt, never assessment: no "good", "right" or "correct", and the same lines follow strong and weak answers alike. The server picks the next one (never the same twice running) and offers it when each answer window opens, so the browser plays it with no round trip; it goes through the avatar hub like every line, so she lip-syncs it, and it is prepared ahead per examiner voice with the other fixed lines (in mock mode by the mock voice), so it costs nothing per turn. The microphone is already shut, so it is never heard as part of the answer. A question ready before it has made a sound skips it; the wait filler below holds off until 1.5 s after it. A kept answer sent again is not acknowledged twice. Optionally (`Gadfly:Voice:BackchannelMs`, off by default), while the student still holds the floor, a finished sentence followed by that many milliseconds of silence (500 is a natural choice) gets a soft "Mm." or "Mm-hm." that does not end the turn: the countdown runs on and speaking again stops her at once. Once per answer; off by default because the microphone is open while she makes it and only the browser's echo cancellation keeps her sound out of the answer. |
| Filling the wait | While the panel judges an answer and writes the next question (GAD-T290), she says one short neutral line if the question isn't ready within 2 seconds ("Let me think about that.", "Okay, give me a moment.", ...) and one more if the wait passes 20 seconds ("Just a moment more."). Since GAD-T559 the 3D examiner first makes a thinking gesture (a glance up and aside, then back with a small nod) and there is no background sound: the old very quiet typing loop (GAD-T409/T502) is only the "typing" arm of an A/B test, for the share of sessions in `Gadfly:Voice:WaitTypingShare` (0, the default, is off; the arm is fixed per session from its id). Each wait's length and lines said are counted per arm (meter `Gadfly.ExamRoom`: `gadfly.examroom.waits`, `wait_ms`, `wait_lines`) and logged with the session; `ThinkingFiller` in the exam room. The lines are fixed in `Voice/ExaminerFiller.cs`, picked at random (never the same first line twice running), never about the answer (no "good" or "interesting"), spoken with the microphone shut, and never put in the transcript or the record. A question that arrives mid-line waits for the line to finish; the rest are dropped. They are prepared ahead (above), so the first one plays at once. |
| Speaking style | A fixed server-side map from the kind of question to one of three styles (`Voice/ExaminerStyle.cs`): `open` (opening a topic or a new situation: brows lifted a little, a slight tilt), `probe` (following up: a small lean and the opposite tilt) and `neutral` (requests, closings, hints, fillers, remarks). All three keep the same mild, friendly baseline; none smiles more or frowns, and a counterexample or contradiction (which tend to follow a weak answer) looks exactly like any probe, so her face never hints at the grade. A public style API is MAA-T10. |
| Captions | The whole question is shown at once, dimmed, and each word lights as she says it, counted from the moment her audio actually starts, on the provider's word timings. |
| Portrait | With no 3D (reduced motion, no WebGL, model missing) the portrait is shown and a mouth drawn over its lips opens and closes once per syllable with the same word timings (`Gadfly:Voice:PortraitMouth`, "x,y,w" as fractions of the image; known for the default portrait, unset for a custom one, which then only brightens). In the library: MAA-T11. |
| Quality | The 3D examiner's frame rate is watched: below about 22 fps for two 3-second windows her render resolution and animation rate step down (high, medium, low); still under 12 fps at the lowest, she becomes the portrait, between sentences, never mid-sentence. The tier is kept for the browser tab, so a reload starts where it ended. In the library: MAA-T12. |
| Lost graphics | A lost WebGL context shows the portrait at once; her voice carries on and the 3D head is released when she next stops speaking. A model that fails to load (404, bad file) is the library's portrait fallback. |
| Pause and resume | Hiding the tab mid-sentence pauses her voice, mouth and captions together; showing it resumes them where they were. A lost microphone, camera or connection pauses the exam (above): she stops, and on **Try again** she asks the question again from the start. In **hold-to-talk** mode, pressing **Hold to talk** while she is asking interrupts her and opens the microphone; hands-free mode does not interrupt on the student's voice, because her own voice from the speakers could trigger it. Library pause/resume: MAA-T13. |
| Voice failure | If the voice provider fails (an error from the hub, a refused request, or no sound within 9 seconds), the browser's own speech reads the question, with the caption lit and the portrait mouth moving on its word boundaries; a browser with no speech leaves the question on screen as text and says so. While the browser's speech is reading for her, a short note under the controls says her usual voice isn't responding and the browser is reading the questions; it goes away when her own voice is back (GAD-T328). If the talking head itself can't start, questions are read by the browser's speech the same way. |
| Preloading | The get-ready page loads the examiner's scripts and the portrait and, on a device that will show 3D (no reduced motion, WebGL, no data saver), downloads the avatar model in the background (`wwwroot/js/voice-preload.js`), so the exam room shows her without a long first load. |
| While she loads | The exam never waits for the 3D model (GAD-T328; it used to wait up to 90 seconds before the first question). On a device that will show 3D, the portrait appears at once and asks the first questions with its moving mouth, while the model loads out of sight behind it, starting at the same moment as the examiner's connection. Once the 3D examiner is ready she fades in over the portrait between sentences: never while she, a wait filler or an eye-contact remark is speaking, and not while the student is mid-sentence. Her voice and captions move to her and the portrait is released. No "instead of the 3D head" note is shown for this stand-in; a model that can't load leaves the portrait in place with the usual note. |
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
- **Live indicator.** Whether the check is on, no face, more than one face, the camera picture stopped, or "can't follow your
  eyes right now". It never says "looking away" (GAD-T561): a running red warning is a standing threat cue.
- **While answering, by phase** (GAD-T557, `docs/research/attention-and-gaze.md` 4.1; the avatar is listening). The *thinking
  gap*, from her last word to the student's first word (`thinkingGapMaxMs`, 20 s, at most), never triggers anything: looking
  away while working out an answer is normal and helps. While *speaking*, gaze is classed as her face, elsewhere on the screen
  (the transcript, the clock: never "away") or off the screen. Only off-screen gaze counts: continuous for `offScreenMs` (8 s)
  **and** over `offScreenShare` (50%) of the last `offScreenWindowMs` (30 s) of the answer's speaking time; tracking quality
  under fair (40) makes every threshold half as long again. Then the **reminder ladder** (GAD-T561, 4.2): (1) her small, slow
  head tilt and raised brows (the portrait: a tiny CSS tilt; none under reduced motion); (2) `softenAfterMs` (4 s) later, if
  still off-screen, everything but her stage dims to at most 35% and blurs to at most 4 px over 2 s (`examiner-fade`, her stage
  cut out with a clip path), restored in 350 ms when the gaze returns and not again for 5 s; (3) `spokenAfterMs` (6 s) later,
  only at a pause of 700 ms or more in the student's speech, one warm line from `WebcamIntegrityRecorder.GentleReminders`
  ("Take your time; I'm right here."), never the same twice in a session, once per question, three times per exam at most.
  Nothing on the screen ever tells the student to look at her; there is no white-out. No face or a second face is still said
  out loud (fixed lines in `ExaminerRemarks`), at most once per `remarkGapMs` (15 s), never in the thinking gap. Under the
  eye-contact accommodation the ladder is off.
  One voice at a time (GAD-T472): **Repeat the question** pressed while she is saying a remark waits for the remark to finish
  (bounded at 13 s, a little past the remark's own 12 s cut-off) and then asks the question; once a question is on its way no
  remark can start, since she is no longer listening.
- **Recorded for review.** Each episode is a webcam integrity event with start and end: `examiner-look-away`,
  `examiner-reading`, `examiner-face-absent`, `examiner-second-face`, and `examiner-fade` (with its peak). Summaries are
  written by the server (the browser sends only codes: "remarked", "peak:NN", "ph:t" for a look-away that stayed in the thinking
  gap (kept at Info), "step:N" for the ladder step reached, then the quality below); confidence at most
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
   The shown partial only shrinks after a short hold (`PARTIAL_SHRINK_HOLD_MS`), so a momentary provider revision that
   regrows a moment later never flickers on screen (GAD-T360); what the student sees is also shaped for reading
   (spelled-out numbers become digits, spoken punctuation words become marks, capitalized, a closing full stop added if
   missing — `TranscriptDisplay.ForDisplay` server-side, a matching `normalizeForDisplay` in `voice-room.js` for the live
   line): purely a display tweak, never changing what is stored or graded (`VoiceAnswerBuilder` still builds the answer
   from the recognizer's own words, verbatim). If the speech hub's SignalR connection drops mid-answer, the microphone
   keeps capturing: audio and `Commit` calls are queued in the browser and replayed, in order, into the fresh session
   `StartSession` opens once reconnected (GAD-T360) — the library itself (`SpeechSessionManager`) has no way to resume
   the dropped session, only to start a new one, so nothing is lost but the recognizer's own continuity resets (filed as
   `SR-T13`).
2. Audio is not sent while the examiner is speaking (no echo, no self-transcription).
3. The end-of-answer cue: after a final segment, a stretch of silence submits the answer, with a visible countdown;
   speaking again cancels it; **Done answering** submits at once; **Clear** discards. The silence starts at
   `Gadfly:Voice:EndOfAnswerSeconds` (default 4) and adapts to the student's own pauses (GAD-T264): once three pauses
   within answers have been seen, it becomes 1.5 times the student's longer pauses (80th percentile of the last 20), never
   below that floor and never above `Gadfly:Voice:EndOfAnswerMaxSeconds` (default 12). An extra-time accommodation
   multiplies both the floor and the ceiling (capped at 45 s), so a student with 1.5x time waits 6 s at least.
   **Endpointing (GAD-T498).** A finished answer goes in sooner. When the last final ends a sentence (a full stop, question
   or exclamation mark, and its last word is not one that leads into more, such as "and", "because", "the" or "um") and no
   committed speech is still waiting for its final, the floor is `Gadfly:Voice:EndOfAnswerCompleteSeconds` (default 2,
   1 to the floor, also multiplied by the accommodation) instead of the full floor; a student who stops mid-sentence keeps
   the full floor. The pause adaptation above applies to both. Each cut-off (below) lengthens the short floor by 0.75 s for
   the rest of the exam (`CUT_OFF_BACKOFF_MS`, up to 3 s, never past the full floor), so it backs off for a student it cuts
   short. The browser's voice activity detection closes a stretch of speech and commits it to the recognizer after
   `Gadfly:Voice:SpeechHangoverMs` of silence (default 700, 300 to 2000; the library's own default is 800): a commit is not
   the end of the answer, it only lets the final, and so the countdown, start sooner. Speaking again at any point cancels the
   countdown, as before.
   **Cut-offs (GAD-T462).** When the silence (not **Done answering**) hands the answer in, the next 3 seconds are watched
   (`CUT_OFF_MS` in `voice-room.js`): if the student starts speaking again, the browser calls `OnAnswerCutOff` with the
   delay in milliseconds, once per answer, and nothing else (no audio, no words). Speech while the examiner is talking (a
   wait filler or the next question) is not counted. `VoiceRoom` ties it to the answer just sent and
   `AnswerCutOffRecorder` range-checks it (0 to 3 s), drops it when voice measurements are off for the session
   (`VoiceStrain.EnabledForSessionAsync`, GAD-T259/T283), and records it through `SessionOrchestrator.NoteAnswerCutOffAsync`,
   which waits for the answer's own judging and changes only the answer's speech metrics: `VoiceAnswerBuilder` stores
   `cutoff.count = 0` on every spoken answer (watched), the marker sets it to 1 with `cutoff.resumeSeconds`
   (`Gadfly.Engine.AnswerCutOff`). With voice measurements off, `VoiceStrain.TurnOff` removes the watch, so such an answer
   is never marked and does not count in the rate. Reviewers see "The student may have been cut off" on the turn (a blind
   grader too: it is a note about the transcript, not about the person); Analytics shows the cut-off rate by group in
   "What made the examiner press harder". It never steers probing (`AnswerDelivery` reads no `cutoff.*` key, GAD-T258) and
   never enters a grade. The last answer of an exam is not marked (the session has ended).
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

## Recognizer: provider, vocabulary and language (GAD-T357, GAD-T358)

- **Fallback chain.** Live mode defaults `MindAttic:SpeechRecognition:Provider` to `Fallback` (the library's own
  `FallbackSpeechRecognizer`: ElevenLabs Scribe, then OpenAI Whisper) rather than pinning to ElevenLabs alone, so a
  provider outage or a quota error mid-session fails over automatically; the library replays the audio of any utterance
  whose final transcript had not yet arrived, so an answer in progress isn't lost on the switch (timings stay on one
  continuous timeline). Set the configuration key explicitly (`ElevenLabs` or `OpenAI`) to pin to one provider instead.
- **Vocabulary hints.** `VoiceVocabulary.ForSessionAsync` builds the exam's own vocabulary — every learning objective's
  title plus the title of each objective's linked concept (the source material's own term for it) — capped at
  `VoiceVocabulary.MaxTerms` (60, comfortably under the library's own 100), computed from the database on the server, never
  from anything the browser sends. The exam room (`VoiceRoom.razor`) computes it once when it arms the student
  (`VoiceGrants.SetVocabulary`) and passes it to `voice-room.js` through the same already-trusted channel the end-of-answer
  cue and avatar model URL already use (the component's `init()` call). The browser's `StartSession` call then asks the
  recognizer to use it, but `VoiceHubFilter` refuses the call outright if the browser asks for any keyterm or language the
  grant didn't itself record (`VoiceGrants.IsVocabularyAllowed`, `AllowedLanguage`): a tampered client call gains nothing,
  since the only vocabulary that can ever reach the recognizer is what the server computed for that exam.
- **Per-exam language.** `SessionPolicy.Language` (an exam's existing policy JSON, so no schema change) is set from the
  exam settings page (`admin/exams`, "Spoken language") or the exam definition file's `Settings.Language`; null lets the
  recognizer detect it, same as before this ticket. `VoiceHubFilter` enforces it the same way as the vocabulary: the
  browser may only ask for the language the exam itself specifies.

## Exam language and second-language speakers (GAD-T528, GAD-T554, GAD-T555)

The whole localization architecture (offered languages, localized texts, the engine, grading and fairness by language) is in
[LOCALIZATION.md](LOCALIZATION.md); this section covers the voice side.

Each attempt has an exam language (`ExamSession.Language`) and a copy of the student's primary language
(`ExamSession.PrimaryLanguage`, GAD-T534). `SessionSpeech` reads them when the exam room arms the student and keeps them on
the `VoiceGrants` grant; nothing comes from the browser, and a language is never inferred from the voice. **An English exam
by a student whose primary language is English (or unknown) behaves exactly as before** (`SessionSpeech.IsDefault`).

- **Recognition (GAD-T529, GAD-T554, GAD-T607).** A Korean exam sends `ko` to the recognizer, a Spanish exam `es` (Scribe `language_code`, Whisper
  `language`; the hub filter still refuses any other). An English exam by a second-language speaker is pinned to `en`, so an
  accent never makes the recognizer drift into another language. Keyterms are capped at Scribe realtime's limits, 50 terms
  and 1,000 characters (GAD-T597), and a Korean or Spanish attempt gets the exam's objective titles in that language first,
  then the English.
- **Endpointing in ESL mode (GAD-T554).** For a second-language speaker the end-of-answer silences start from the `Esl`
  settings (6 s floor, 15 s ceiling, 3.5 s finished-sentence cue used only after 3 answers in a row that were not cut off,
  twice the cut-off backoff; docs/research/esl-and-korean.md R3), with the extra-time accommodation on top and the 45 s cap.
  The student's own calibration reading in the exam language (GAD-T594), when present, can only lengthen the floor (to 1.5
  times their longest reading pause). A Korean or Spanish exam never uses the short finished-sentence cue: that test is English.
- **Voice (GAD-T530, GAD-T555, GAD-T607).** `eleven_flash_v2_5`, the default model, speaks Korean and Spanish, so a Korean
  or Spanish exam uses the same
  voice and model unless `Gadfly:Voice:TtsModels:ko` names another (`eleven_multilingual_v2`, `eleven_turbo_v2_5` and
  `eleven_v3` speak Korean; `eleven_flash_v2`/`eleven_turbo_v2` do not) or `Gadfly:Voice:VoiceIds:ko` a platform voice for
  Korean (`TtsModels:es` and `VoiceIds:es` for Spanish; the exam's or organization's own voice always stands). The premade
  voices speak Spanish on the multilingual models; whether their accent suits neutral Latin American Spanish is for the
  Spanish tester (GAD-T614) to judge, and `VoiceIds:es` is where a native voice would go. A second-language speaker hears the examiner at
  `PlainSpeechSpeed` (0.9). Because the avatar library binds one model and one set of voice settings per process (MAA-T20),
  a session that needs another model or speed is spoken by Gadfly's own `ElevenLabsTtsClient` (`ExaminerTts`), built from
  the library's options and HTTP client (the mock voice in mock mode) and sharing its phrase cache; `VoiceHubFilter` plays
  such a session's `Speak` calls through `QuestionVoiceStreams.SpeakText`, on the library's own events. The mock voice reads
  Hangul and times each syllable block as a syllable, and counts Spanish accented vowels as vowels. Captions of her questions are always on.
- **Lip-sync (GAD-T531).** TalkingHead has no Korean module; `wwwroot/js/lipsync-ko.js` is Gadfly's own (Hangul syllables
  decomposed to jamo by Unicode arithmetic and mapped to visemes), registered on the loaded head by `examiner-voice.js`.
  The library passes only word timings to the browser, so visemes are spread over each word by relative duration (Korean is
  syllable-timed); per-character timings would need the library to forward ElevenLabs' character alignment.
  Spanish (GAD-T608) works the same way with `wwwroot/js/lipsync-es.js`: Spanish spelling is regular, so a letter table and
  a few context rules (ch, ll as y, rr and a word-initial r trilled, silent u in qu/gue/gui but heard in gü, silent h, ñ with
  an i-glide, seseo for c before e/i and z, y as a consonant before a vowel, accented vowels as their base vowel, numbers
  read in Spanish, ¿ and ¡ dropped) give the visemes. The browser-speech fallback reads with a Latin American Spanish voice
  first (es-419, es-MX, es-US...), then any Spanish one.
- **Fillers (GAD-T532).** `ExaminerFiller.For(language)`: Korean acknowledgements, wait fillers, backchannels and
  eye-contact remarks, reviewed as non-evaluative; prepared per voice and language in the phrase cache.
  Spanish (GAD-T609), with usted: acknowledgements Mm., Ajá., Entiendo., De acuerdo., Gracias. (never correcto, bien, muy
  bien, exacto, perfecto, claro), wait lines such as "Un momento, por favor.", and the gentle reminders and camera remarks
  (`WebcamIntegrityRecorder.GentleRemindersSpanish`), never a request to look at her and with no gendered adjective.

## Get-ready microphone test (GAD-T359)

Once the get-ready page's microphone opens, `wwwroot/js/mic-check.js` (hooked in by `system-check.js`, `[data-mic-check]`
in `StartExam.razor`) listens for about four seconds and writes plain advice into its own panel — a noise floor
("quiet" / "a little noticeable" / "noisy"), a clipping warning if very loud peaks are seen, and an echo hint (reading
the microphone's own `echoCancellation` setting where the browser reports one). It reuses the speech library's own level
math (`rms`, `toDbfs` from `@mindattic/speechrecognition`) rather than re-deriving it. Advice only, the same spirit as the
eye-tracking setup check next to it: it never blocks **Begin exam**, and a failure to load just leaves the panel empty.

## The live turn: how fast the examiner answers (GAD-T494)

After the student stops speaking, the step on the server (`SessionOrchestrator.AnswerAsync`) records the answer, judges
it, plans the next move and words the next question; the examiner starts speaking when that question comes back. On
production the question's wording was the bottleneck: the panel vote that drafted it (`panel.questionGeneration.Generate`,
about 7.5 s) and then a second panel vote for its leak guard (`panel.questionGeneration.CheckLeak`, about 4.1 s), one
after the other, while ElevenLabs' first audio byte takes about 0.17 s. Now:

1. The answer is recorded and verified (a spoken answer is taken as heard: no call), then **judged provisionally by the
   same fast model** (GAD-T508, below): the judge prompt and rubric, one reply (`live.judge`, metered as `live-judge`).
2. The planner picks the move (local, no call) from that provisional judgment and the panel's judgments of earlier answers.
3. **One fast model** words the question (`ILiveTurnModel`, `Gadfly/Socrates/LiveTurnModel.cs`; `QuestionGenerator.LiveModel`):
   `Gadfly:LiveTurn:Provider` (default `claude`) and `Gadfly:LiveTurn:Model` (default Claude Sonnet 5, `claude-sonnet-5`; an
   Opus id is refused and replaced by Sonnet 5). Timed as `live.questionGeneration.LiveDraft`.
4. **The same model runs the leak guard** on that question, a one-word LEAKS/SAFE reply (`live.questionGeneration.CheckLeakLive`).
   The question is returned only after the guard says SAFE. LEAKS redrafts once and then uses the plain fallback question,
   and a reply that is neither word counts as undecided and also falls back (fail closed), exactly as with the panel guard (GAD-T92).
5. The question goes to the examiner's voice. After a spoken answer it goes there **while it is being written**, a guarded
   sentence at a time (GAD-T495, below), so she starts on the first sentence instead of waiting for the whole question.

If the fast model fails (provider down, timeout), that attempt goes back to the panel vote and the panel guard, so a
provider outage costs speed, never safety. Every call is still metered (`PanelCall`, kind `live-turn`) and timed
(`ApiCallTimings`, with a `.firstChunk` row for the streamed reply, GAD-T495 below). Mock mode (`Gadfly:AiMode=mock`) uses the mock
panel's own template replies, so nothing is called or billed.

**The panel judges off the turn (GAD-T508).** The multi-model panel's judgment of an answer (aporia check, judges,
analysis, claim extraction and contradiction checks) no longer blocks the next question. What is provisional and what
is final:

- *Provisional* (`Turn.Provisional`, a `ProvisionalJudgment`): the fast model's score of the answer against the rubric,
  plus the local recitation check. It only steers: the planner reads it (`SocraticSession.ForPlanning`) for answers whose
  full judgment has not arrived, and the concept's running mastery estimate uses it until then. It is never written as a
  `Judgment` row, never graded, reported, sent as evidence or shown to a reviewer as a grade.
- *Final* (`Turn.Judgment`): the panel's judgment, exactly as before. Right after the step, the server starts it in the
  background (`SourceExamEngine.StartFullJudgment`, one answer at a time, in turn order, from a snapshot, so the session's
  step lock is not held during the panel calls). When it finishes, it is applied under the step lock
  (`ApplyFinishedJudgmentAsync`): it becomes the turn's judgment, the concept's mastery is replayed from it, a leak the
  analysis found is flagged, its claims enter the ledger, and the step is saved (`Judgment`/`JudgeVote` rows, mastery,
  claims, evidence). From then on the planner plans from it. Its panel calls are metered as `exam-judging` and timed under
  their usual `panel.*` names.
- *Disagreement*: when the two differ materially (one heard an honest "I don't know" and the other an attempt, they fall
  on different sides of the evidence thresholds, or they are 0.25 or more apart), the turn's `ProvisionalJudgment.Disagreement`
  says how, and an audit event `judgment.provisional.disagreed` is written against the answer, for reviewers and for
  calibrating the fast model. The panel's judgment is the one graded either way.

The exam never ends on a provisional judgment: when the planner would end it, or the student, staff, the clock or the
stale-session sweep ends it, the pending full judgments are finished first (`SourceExamEngine.EndAsync`), so the report
reads only the panel's. A panel that still fails at the end does not stop the exam ending; that answer goes to a human, as
an unjudged answer always has. If the fast model fails on a turn, the panel judges that answer on the turn, after any
earlier pending ones, as before. Recovery: the provisional and pending state is in the saved transcript, so after a
restart the pending judgments resume as soon as the session loads, every step starts the next one, and a judgment that
failed is retried on the student's next step. `Gadfly:LiveTurn:DeferJudging=false` puts the panel judgment back on the turn.

**The one-turn steering lag (GAD-T515).** Everything the panel's analysis finds (misconceptions, uncertainties, claims and
the contradictions between them) reaches the planner one turn late: the next move after answer N is planned from N's
provisional score alone, and N's analysis is in by the move after N+1. Measured on the planner's rules (local, no calls):
- *Clarification fired far more often.* The "no analysis, score under 0.35: ask a neutral clarification" rule read a pending
  analysis as a missing one, so under deferred judging every weak answer (not an "I don't know", not right after a
  clarification or assist) was asked to clarify, up to twice per objective, where the panel's analysis would usually have
  let the examiner probe instead. Fixed: that rule now skips answers whose full judgment is pending.
- *Misconceptions and contradictions are probed one turn later, not lost,* while the examiner stays on the objective: the
  counterexample / assumption-testing rule and the elenchus read every turn on the objective, not just the last.
- *Leaving an objective early lost them.* When the provisional scores made an objective Sufficient, the examiner moved on,
  and a misconception or contradiction the panel then found turned it Inconclusive with nothing asked about it. Fixed: before
  leaving an objective that is Sufficient only on the planning view with answers still pending, the engine waits for those
  full judgments (one wait per objective, like the end of the exam) and plans again; a panel failure keeps the move.
- *Still lost:* an uncertainty about meaning (asked only about the latest answer) and Learning Mode's hint after a
  misconception the score did not show, when the analysis arrives after the next answer. Accepted: a clarification of an
  answer two turns back reads oddly, and the misconception is still tested by the rule above.

Decision: no fast-model misconception pass for now. It would add a second fast-model call on every turn, and its
misconceptions would not carry the panel's quote checks; revisit if the `judgment.provisional.disagreed` rate (GAD-T514)
or reviewer reports show the examiner steering past misconceptions.

**When the fast model is good enough to steer (GAD-T647).** The fast-model agreement report
(`/grading/provisional-agreement`, `ProvisionalAgreement`, GAD-T645/T662) counts, per compared turn, whether the panel
recorded a material disagreement (the definition above) and the score gap. A disagreement means the planner may have
chosen a different move than the panel's view would have; the next move corrects it, so a modest rate costs one off-key
question, not a grade. Decision rule, read off the overall row and the provider/model row of the fast model in use:
- *Enough to read:* at least 100 compared turns from at least 10 ended sessions with a real (not mock) fast model, and a
  group (subject or move kind) is read only once it has the report's minimum (`MinTurns`, 10). Below that, collect more.
- *Good enough to steer:* the 95% Wilson upper bound of the disagreement rate is at or under 15%, the mean signed gap is
  within ±0.10 (no steady lean), and no subject or move group's lower bound is over 25%. Keep the model and prompt.
- *Needs prompt changes:* the lower bound is over 15% but the rate is at most 30%, or the mean signed gap is beyond
  ±0.10 (the fast model is consistently harsher or kinder than the panel: recalibrate the judge prompt), or one subject or
  move group's lower bound is over 25% (fix the prompt for that case). One prompt revision, then re-read on fresh turns.
- *Needs a different fast model:* the lower bound is over 30%, or it is still over 15% after one prompt revision; switch to
  the provider/model whose own row has a lower interval that does not overlap, or trial one.
- *Inconclusive:* the interval straddles 15% (lower bound under, upper bound over). Keep collecting; no change.
The `judgment.provisional.disagreed` audit count must match the transcripts' count (the report's cross-check) before the
figures are trusted.

Result (2026-11-23, live database, read-only, no model calls): **not yet enough data.** Live has 8 ended sessions
(5 test runs, 1 evidence-complete, 1 time-up, 1 abandoned; 38 turns), all from 2026-11-21 to 2026-11-22 19:03 UTC, before
the provisional judgment landed (GAD-T508, 2026-11-22 20:49 UTC): no transcript carries a provisional judgment and there is
no `judgment.provisional.disagreed` audit event, so there are 0 compared turns. No tuning is called for yet; the re-read is
GAD-T670.

**The wait at the end (GAD-T516).** Ending waits for the panel, so the student's last step costs the full panel time of
the last answer (its full judgment only starts once that answer is recorded, inside the step that ends the exam) plus
whatever is left of the one before it; the judgments run one at a time, in turn order, because each reads the claim
ledger of every earlier answer. Every such wait is measured (`SourceExamEngine.EndJudgingWait`: answers pending, ms
waited, whether the exam then ended or the panel's verdicts kept it going, whether one failed, whether the planner or
someone else ended it), logged, and written to the audit trail against the session as `judgment.end.waited`. Decision
until live numbers say otherwise: hide it rather than reshape the judging. Judging the last answers in parallel would
break the claim-ledger order the contradiction checks rely on, and starting the last judgment earlier would mean running
the panel on a partial answer, which the prefetch deliberately never does (GAD-T497). If `judgment.end.waited` shows
waits of more than a few seconds, the exam room should cover them with the examiner's closing remarks and a
"reviewing your last answer" progress line; only if that is still too long is it worth judging the last answer's
panel calls in parallel with the earlier one while keeping the ledger merge in turn order.

**Prefetching the next step (GAD-T497).** While the student is still speaking, the exam room can send the answer so far
(the recognizer's finished segments plus its current partial) to `SessionOrchestrator.NotePartialAnswerAsync`. Once those
words have stood still for `Gadfly:LiveTurn:Prefetch:DebounceMs` (default 700 ms) and are at least `MinWords` long
(default 4), the whole next step (the fast model's provisional judgment, plan, fast-model question, guard; GAD-T508: the panel only if the fast model fails, and never on an earlier answer still pending) runs speculatively on a copy of the
session. When the student finishes with the same words (case, punctuation and spacing aside) and nothing else changed the
session in the meantime, the answer step adopts the copy: the submitted answer, with its real timings and speech measurements,
replaces the partial on record, the recitation check is re-run on it, and the next question is ready at once. Otherwise the
copy is dropped and the answer is judged as usual. Cost limits: one speculation in flight per session, at most `MaxPerTurn`
(default 2) per question, none while a step runs. Every speculative call is metered as `exam-prefetch` and timed, used or not.
`Gadfly:LiveTurn:Prefetch=false` turns it off. The browser feeds it: `voice-room.js` sends the running answer (finished
segments plus the current partial) on each recognizer partial and final, at most every 300 ms (`PARTIAL_SEND_MS`, the latest
words always go out), only while the microphone is open for a question; VoiceRoom's `OnPartialAnswer` callback hands it to
`ExamRoom.OnPartialAnswerAsync`. Never audio, and nothing on the page changes.

**Spoken as it is written (GAD-T495).** After a spoken answer the examiner starts saying the next question while the rest of it
is still being written. The path, step by step:

1. The answer comes in (`ExamRoom.TakeVoiceAnswerAsync`, the commit time is noted). VoiceRoom opens a `QuestionVoiceStream`
   (`Gadfly.Server/Voice/QuestionVoiceStreams.cs`) for the student's own session and the exam room makes it the step's
   `QuestionSpeech` (an ambient `IQuestionSpeech`, `Gadfly/Engine/QuestionSpeech.cs`) around `SessionOrchestrator.AnswerAsync`.
   Nothing else has one: the CLI, reflection runs, tests and a speculative prefetch (GAD-T497, on its own background task) get
   the whole question at once, as before. None is opened without the talking head, for a pending request, or when the voice's
   output format is not PCM.
2. The fast model's reply streams (`ClaudeStreamingLiveTurnModel`: the Anthropic Messages API with `stream: true`, read as
   server-sent events, with the key Legion uses from MindAttic Vault, no `temperature` on the Claude 5 line and thinking turned
   off on Sonnet/Haiku 5, metered as `live-turn` with the provider's own token counts and timed like the one-shot call;
   MindAttic.Legion has no streaming call, so other providers still give the reply in one piece, and `Gadfly:LiveTurn:Stream=false`
   keeps Claude on Legion's one-shot call too). Mock mode streams the mock's template reply a word at a time.
3. `QuestionGenerator` cleans the reply as it arrives (the first line only, no label or wrapping quotes, as `Clean` does) and
   splits it into sentences (`SentenceSplitter`: not on "e.g.", "Dr." or "3.5"). Sentences are grouped into parts; the first part
   has at least four words, so a lone "Okay." waits for the next sentence.
4. Each part is checked by the same one-word leak guard on the same model, given the dialogue so far and **everything the
   student would have heard up to and including that part**: part 1, then parts 1 and 2, and so on, the last check being the
   whole question, the same check as before streaming. A question's last sentence is usually the real question, and the guard
   always judges it with the sentences before it and the conversation. Guards run while the model keeps writing; parts are
   handed to the voice strictly in order, each only after its own guard said SAFE. A part that talks about "the student" or
   asks about the student's own thought process (GAD-T445) is never said either.
5. The first part that leaks, is undecided (fail closed, GAD-T92) or is unusable stops the reply: the model is cut off and
   nothing after it is said. If nothing was said yet, the question is redrafted once and then falls back, exactly as before. If
   something was said already, the plain fallback question for the move is said after it. The question recorded is always
   exactly what she said (the parts and any fallback, joined), so the transcript, the record and Repeat the question match what
   the student heard. If the fast model fails before anything was said, the panel drafts that attempt as before; after, the
   fallback question follows what was said.
6. Each part is synthesized as soon as it is safe, one ElevenLabs with-timestamps request per part (the same HTTP client and
   mock handler as every line, so mock mode bills nothing), so the next part's audio is usually ready while the previous one
   plays. The library's WebSocket stream-input session was not used: it bypasses the mock handler and the request timing.
7. On the first part the browser is told (`speakStream`). It plays the stream through her own avatar hub connection as one
   utterance: it calls the hub's `Speak` with the stream's token, which `VoiceHubFilter` hands to `QuestionVoiceStreams.Play`
   instead of the library (only a stream of this student's, once). The parts go out on the library's own events (`speechStart`,
   `speechChunk`, `speechEnd`), each part's word timings offset by the audio sent before it, so the lip-sync and the word-lit
   caption run on as for one sentence; a `questionText` event adds each part's words to the caption (`presenter.append`) when
   that part is safe. If the next part is not ready when the audio already sent is about to run out, 200 ms of silence is sent
   at a time, so the player never runs dry (it would end the utterance) and the timings stay true. A stretch of more than 30 s
   with no next part gives up, and her voice failing at any point (no sound in 9 s, an error, a refused stream) has the
   browser read the whole question itself once the step shows it, as for any question.
8. When the step returns, the page shows the question as usual; VoiceRoom sees it is the one already being spoken
   (`streamDone`) and does not start it again. Any other `Speak` (Repeat the question, a filler) or a `Stop` ends the stream.

Expected time to her first word: the fast model's first sentence (roughly its time to first token plus a sentence of output,
about 1 to 1.5 s on Claude Sonnet 5), one guard call on that sentence (about 0.5 to 1 s), and ElevenLabs' first audio byte
(about 0.2 to 0.4 s with `eleven_flash_v2_5`): about 2 to 3 s after the answer is in, instead of waiting for the whole draft,
its guard, the step's save and the browser's own Speak round trip. The later parts' drafting, guards and synthesis run while
she speaks. Measured per turn in `ApiCallTimings`, from the answer's commit: `voice.question.firstPart` (the first part
guarded), `voice.question.firstAudio` (its first audio on the server) and `voice.question.firstAudioSent` (the first audio sent
to the browser), plus `voice.question.played` for the whole utterance and the usual `tts.elevenlabs*` rows per part.
Costs: one guard call per part instead of one per question (questions are usually one to three parts), and synthesis of a part
the student never hears only if the stream is stopped.

**Departure from the blueprint.** ElenchusAtScale has the multi-family panel word the questions and vote on the leak guard.
The owner moved both onto one model for conversational latency (docs/research/blueprint-gap-analysis.md, owner decision 7),
and with GAD-T508 the live steering too: the next move is chosen from that one model's provisional judgment. Grading still
uses the full panel: every answer's graded judgment is the panel's, and the exam does not end before it is in.

## Recognizer latency and unsure-word rate (GAD-T361)

Two in-memory counters for the status page, the same spirit as `Diagnostics/ApiCallTimings.cs` (GAD-T413) but without a
durable table (diagnostics, not an audit trail; nothing here is written to the database or survives a restart):

- **Recognizer latency**: how long each committed utterance took from the browser's `Commit` call to the recognizer's
  final transcript coming back. Timed in the browser (`voice-room.js`, `s.commitSentAt`/`s.latenciesMs`), since the hub
  pushes the final segment rather than returning it from a call the server could time itself, and sent up with the answer
  (`VoiceAnswerPayload.RecognizerLatenciesMs`, diagnostics only: never shown to the student, never stored with the answer).
- **Unsure-word rate**: the share of an answer's words below the library's low-confidence threshold, computed from the
  same `VoiceAnswerBuilder` every voice answer already goes through (no extra client work).

`VoiceRoom.razor` records both on every voice answer (`VoiceRecognitionTimingRecorder`, registered in `Program.cs`), and
`GET /healthz/voice-recognition` (staff-only, `GadflyEndpoints.cs`) reports counts, average and p50/p95 latency, and
average/max unsure-word share, the same plain-text shape as `/healthz/call-timings`. The unsure words themselves were
already marked for reviewers before this ticket (GAD-T265, GAD-T405: no read-back to hook a correction into any more, so
the recognizer's low-confidence words and its advice to check are recorded for reviewers instead, see `SpeechMarks.cs`
and the "Speech recognition was unsure of these words" note on the grading page) — this ticket only adds the two
counters above.

## Security

- Both hubs require the **Student** policy, the hub rate limit, and a server-side grant (`VoiceGrants`) that the exam room
  creates after it has checked the session is the student's and running. A hub filter (`VoiceHubFilter`) refuses
  connections without a grant, and refuses `Speak` for any text the server did not offer for that student: the avatar hub
  cannot be used as a free text-to-speech endpoint.
- Provider keys stay on the server: ElevenLabs key from Vault (`MindAttic:Vault:LLM:elevenlabs:apiKey`, already loaded),
  voice id from `MindAttic:Avatar:ElevenLabs:VoiceId`. The browser can't pick a provider or a voice.
- **Per-exam and per-organization voice (GAD-T475, live mode only).** When the exam room arms a student it reads the
  exam's voice id, else the organization's default voice id, else none (the platform voice), in `ExaminerVoice`, and
  stores it on the student's grant. `VoiceHubFilter` overwrites the `voiceId` argument of every `Speak` call with the
  grant's choice, whatever the browser sent, so `MindAttic:Avatar:Hub:AllowClientVoiceId` is switched on in live mode only
  for that filtered value. An id not shaped like an ElevenLabs voice id is skipped (logged) rather than sent. The fixed
  examiner lines are pre-warmed per voice. Mock mode is unchanged: no id is passed and the fake voice speaks.
- **Which examiner (GAD-T504, GAD-T595).** Two examiners are offered (`Gadfly.Server/Student/ExaminerCatalog.cs`): the female
  examiner (`examiner-f`, the deployment's `Gadfly:Voice:AvatarModelUrl`) and the male one (`examiner-m`,
  `wwwroot/assets/avatars/examiner-m.glb`, 9.5 MB, CC0, committed; his portrait `assets/portrait-m.png`). An exam's
  **Examiner avatar id** (`Exams.AvatarId`), else the organization's default avatar id, fixes the examiner (`examiner-f` or
  `examiner-m`; an unknown id fixes nothing); otherwise the student chooses on the get-ready page, remembered on the device
  (device-prefs.js `getExaminer`). The exam room resolves it again on the server (`ExaminerCatalog.Resolve`), so the browser's
  choice is only a preference among what the exam allows, and shows that examiner's model, portrait and portrait mouth. Each
  examiner can have **his or her own voice**: in live mode the order is the exam's voice, then the student's chosen voice (see
  [Making the examiner your own](#making-the-examiner-your-own-gad-t505-gad-t646-gad-t663-gad-t649-gad-t671)), then the examiner's
  own voice for the exam language, then the organization's default, then the platform voice for the language. The male examiner's default is
  ElevenLabs' premade "Brian" (`nPczCjzI2devNBz1zQrb`: deep, calm and warm; premade voices speak Korean through
  `eleven_flash_v2_5`/`eleven_multilingual_v2`); the female keeps the platform voice. The model was built with MPFB like hers
  (`tools/examiner-m`, provenance in `assets/avatars/examiner-m.LICENSE.md`): same Mixamo bone names and frames, LeftEye/RightEye
  bones (the gaze target), all 52 ARKit shapes and the 15 Oculus visemes, the same eye texture, so `avatar-look.js` styles him
  with the same palette.
- The transcript reaches Gadfly through the student's browser (server to browser to server), the same trust model as the
  existing `SessionHub.Answer`. The metrics and verification advice are recomputed on the server from the relayed words.
  A server-side tap on the recognizer (so the browser never carries the text) would need a hook in the speech library.

## Making the examiner your own (GAD-T505, GAD-T646, GAD-T663, GAD-T649, GAD-T671)

When the student chooses the examiner (the exam and organization don't fix one), the get-ready page's **Make the examiner your
own** panel lets them change her or his voice, skin tone, hair colour, clothing and framing, so they sit an exam with someone they
are comfortable speaking to. It is optional, never graded and never sent to graders or the AI panel.

**Only bounded choices, never free text.** `Gadfly.Server/Student/ExaminerCustomization.cs` is a record of five values, each one of
a fixed list (or null, the examiner's own default):

| Choice | Values |
|---|---|
| Voice | An ElevenLabs premade voice from the examiner's allowlist (below). |
| Skin tone | `light`, `medium-light`, `medium`, `medium-dark`, `dark` |
| Hair colour | `black`, `dark-brown`, `brown`, `auburn`, `blonde`, `grey` |
| Clothing | `navy-blazer`, `charcoal-suit`, `cardigan`, `white-shirt` (plain and professional, no logos or text) |
| Framing | `head-and-shoulders` (default), `close`, `seated` |

**Allowlisted voices.** Only ElevenLabs *premade* voices are offered, because they speak every language of the multilingual models
(English, Korean and Spanish) and Gadfly has listened to them:

- Female examiner: Rachel `21m00Tcm4TlvDq8ikWAM`, Sarah `EXAVITQu4vr4xnSDxMaL`, Charlotte `XB0fDUnXU5powFXDhCwa`, Alice
  `Xb7hH8MSUJpSbSDYk0k2`, Matilda `XrExE9yKIg1WjnnlVkGX`, Lily `pFZP5JQG7iQjIQuC4Bku`.
- Male examiner: Brian `nPczCjzI2devNBz1zQrb` (his default), Daniel `onwK4e9ZLuTAKqWW03F9`, George `JBFqnCBsd6RMkjVDRZzb`, Chris
  `iP95p4xoKVk53GoZ742B`, Eric `cjVigY5qzO86Huf0OWal`, Liam `TX3LPaxmHKxFdv7VOQHJ`.

A language can be given its own list per examiner (`LanguageVoices`; none yet). A voice is only ever offered for the examiner it
belongs to, so his voices never speak for her. The get-ready preview speaks with the browser's own voice (the line is replayed when
the voice changes); the chosen voice is heard in the exam room and the practice interview, in live mode only (mock mode's fake voice
is unchanged). The voice order becomes: the exam's voice, then the student's chosen voice, then the examiner's own voice for the
language, then the organization's default, then the platform voice. An exam that names its own voice therefore always keeps it.

**Integrity limits.** Customization must never make the examiner something the gaze check (`examiner-gaze.js`, GAD-T240) or the
student can't hold eye contact with:

- The examiner stays a recognizably human face: no free colours, no masks, glasses, hats or props, nothing that covers the face.
- Colour changes reach only the skin, hair and clothing materials (`avatar-look.js` `customLookOptions`): the chosen colour replaces
  that part's blue and becomes its own inner glow. The eyes (sclera and iris), brows, lashes, lips, mouth and the silhouette rim are
  never changed, so lip sync and the eyes read exactly as with the default look.
- The default blue look is a digital presence (`avatar-look.js` `LOOK_CIRCUIT_*`, GAD-T690): faint cyan circuit traces (right-angle
  and 45-degree runs with small pads and vias, drawn on a canvas in the browser and tiled over the UVs) glow under the blue skin and,
  more faintly, the clothing, with a slow light drifting along them (still under `prefers-reduced-motion`). The face carries only a
  few fainter traces and none around the eyes, brows and mouth (masked in the model's rest pose from the eye and teeth positions);
  the eyes, brows, lashes, teeth and tongue never carry them. A chosen skin tone or clothing drops the traces from that part.
- Every framing keeps the face and eyes in view (`FRAMINGS`: `upper`, `head`, `mid` on `<ma-avatar view>`). The portrait fallback
  gets a bounded scale about her eyes (`--portrait-scale`, 1.15 close and 0.86 seated, never enough to crop the face), and the
  moving portrait mouth follows that scale; in portrait mode the clothing colour is a tint band over the bottom of the portrait only.
- When the exam or organization fixes the examiner (GAD-T504), there is no panel and the examiner is shown exactly as configured.

**Trust.** The choice is remembered on the device with the examiner (device-prefs.js `setExaminer`/`getExaminer`), so the server
treats it as a preference: `ExaminerCustomization.Validate` drops any value not on a list (compared ignoring case and spaces,
returned in its canonical form) on the get-ready page and again in the exam room (`ExaminerCatalog.Resolve`), and the voice is
checked once more against the exam language (`ResolvedExaminer.VoiceIdFor`) before it reaches the student's voice grant, which
`VoiceHubFilter` enforces on every `Speak`. Nothing about the customization is stored on the server or in the session record.

## Voice quality vs. latency (GAD-T415, GAD-T616, GAD-T617)

ElevenLabs' text-to-speech models trade voice quality for how long the examiner takes to start speaking. An organization
admin picks the examiner's **voice quality** tier on Organization settings (Voice exam defaults), and an exam may set its
own in the exams manager's settings; the default is the fast end, per the owner's "fluid conversation now" call:

| Tier (id) | Model | Latency | Voice quality |
|---|---|---|---|
| Fast (`fast`, **default**) | `eleven_flash_v2_5` | First audio in about a quarter of a second (ElevenLabs quotes a ~100 ms median of model time; network and the first part's size add the rest); this keeps the question-and-answer rhythm of an exam feeling live. | A little flatter/clipped than the quality tier, especially on longer or more expressive sentences. |
| Quality (`quality`) | `eleven_multilingual_v2` | Visibly slower to start: often a second or more before the first chunk, which shows up as a longer pause before each question. | ElevenLabs' most natural-sounding multilingual voice; worth it for a recorded demo or a slower-paced oral, less so in a quick exchange. |

Both speak English, Korean and Spanish (`eleven_multilingual_v2` has 29 languages, the v2.5 models 32). Checked against
ElevenLabs' model docs on 2026-11-22, two tiers that were planned are not offered: a "balanced" tier on
`eleven_turbo_v2_5` (deprecated, "functionally equivalent" to `eleven_flash_v2_5`), and an "expressive" one on
`eleven_v3`/`eleven_v4` (`eleven_v4` and `eleven_v4_turbo` are served only by the Text to Dialogue API and WebSocket and have
no speed setting; `eleven_v3` is not on the stream-input WebSocket and its support on the `stream/with-timestamps` endpoint
the examiner uses is not documented). An expressive tier needs the avatar library to speak through the dialogue
endpoint with word timings first.

**Which model speaks** (`ExaminerTts.Choose`): the exam's tier, else the organization's (`SessionSpeech.VoiceTier`, read for
every attempt, English ones included); a tier whose model does not speak the exam language is skipped, with one logged note
per tier and language; then `Gadfly:Voice:TtsModels:<lang>`; then the library's model (`Gadfly:Voice:TtsModel` /
`MindAttic:Avatar:ElevenLabs:ModelId`). With no tier set, an English exam by an English speaker goes to the library's voice
exactly as before; an explicit Fast tier on a default deployment does too. A session whose model differs gets its own
`ElevenLabsTtsClient` with only the model (and speed) changed (`ExaminerTts`, GAD-T530), so organizations on different tiers
share one server safely, and no restart or library change (MAA-T20) is needed; its fixed lines are prepared in that model when
the exam room opens. Changes are audited as `settings.voice-tier.changed` and `exam.voice-tier.changed` (before/after
`{"voiceTier":...}`), and apply to attempts that start afterwards.

**Measured latency.** In live mode every streamed question also records `voice.question.firstAudio@<model>` (GAD-T617): the
time from its first part being handed to ElevenLabs to the first audio back, so it measures the model alone (the existing
`voice.question.firstAudio` counts from the answer's commit and includes writing the question). Mock mode never records it.
Organization settings shows each tier's platform-wide median and count over the last 30 days (`VoiceTierLatency`), or "No
measurements yet"; `/healthz/call-timings` lists the rows too.

## Configuration

| Setting | Meaning |
|---|---|
| `Gadfly:Voice:Mode` | `off`, `mock` or `live`. Default `mock` in Development, `off` elsewhere. |
| `Gadfly:Voice:AvatarModelUrl` | The avatar GLB. Default `/assets/avatars/mpfb.glb` when the file exists, otherwise the 2D portrait. The examiner everywhere is this one model in the blue look (`avatar-look.js`, GAD-T687): set it only to a hosted copy of the same `mpfb.glb` (e.g. blob storage), never another model. |
| `Gadfly:Voice:PortraitUrl` | 2D stand-in and fallback image (default `/assets/portrait-f.png`, Gadfly's own render of the blue examiner, GAD-T687; `tools/sync-voice-assets.ps1` no longer overwrites it with the library's untinted portrait). Leave it unset. |
| `Gadfly:Voice:Examiners:<id>:ModelUrl`, `:PortraitUrl`, `:PortraitMouth` | One examiner's model, portrait and portrait mouth (`examiner-f`, `examiner-m`; GAD-T504). Unset: hers are the settings above, his the committed `/assets/avatars/examiner-m.glb` and `/assets/portrait-m.png`. |
| `Gadfly:Voice:Examiners:<id>:VoiceId` / `:VoiceIds:<lang>` | That examiner's own ElevenLabs voice, for every language or one (live only). Unset: the male examiner's is `nPczCjzI2devNBz1zQrb` ("Brian"), hers the platform voice. |
| `Gadfly:Voice:PortraitMouth` | Where the portrait's mouth is, "x,y,w" as fractions of the image (set for the default portrait; unset for a custom one means no drawn mouth). |
| `Gadfly:Voice:TtsModel` | The examiner's ElevenLabs voice model when `MindAttic:Avatar:ElevenLabs:ModelId` isn't set (default `eleven_flash_v2_5`, low latency). |
| Organization / exam **voice quality** (database, not configuration) | The examiner's tier, `fast` or `quality` (GAD-T617): set on Organization settings and per exam; wins over `TtsModels:<lang>` and `TtsModel` when its model speaks the exam language. Unset: the settings below and above, as before. |
| `Gadfly:Voice:WarmPhrases` | Prepare the fixed examiner lines in the phrase cache when the first exam room opens (default true). |
| `Gadfly:Voice:EndOfAnswerSeconds` | Shortest silence that ends an answer (2 to 15, default 4). |
| `Gadfly:Voice:EndOfAnswerMaxSeconds` | Longest the silence may adapt to for a student who pauses long (up to 30, default 12). Both are multiplied by the extra-time accommodation. |
| `Gadfly:Voice:EndOfAnswerCompleteSeconds` | The shorter silence that ends an answer whose last words are a finished sentence (1 to the floor, default 2; GAD-T498). Multiplied by the extra-time accommodation. |
| `Gadfly:Voice:SpeechHangoverMs` | Silence after which the browser commits one stretch of speech to the recognizer (300 to 2000, default 700; GAD-T498). |
| `Gadfly:LiveTurn:Stream` | `false` keeps Claude's live turn on Legion's one-shot call, so questions are spoken whole instead of as they are written (GAD-T495). Default on. |
| `Gadfly:Voice:BackchannelMs` | The listening "Mm-hm." after a finished sentence and this much silence, without ending the turn (0 = off, the default; else 300 to 3000; GAD-T496). |
| `Gadfly:Voice:TtsModels:<lang>` / `VoiceIds:<lang>` | The examiner's model / platform voice for one exam language (GAD-T530); unset uses the usual model and voice. |
| `Gadfly:Voice:PlainSpeechSpeed` | Her speed for a second-language speaker (0.7 to 1.2, default 0.9, 1 = off; GAD-T555). |
| `Gadfly:Voice:Esl:EndOfAnswerSeconds`, `:EndOfAnswerMaxSeconds`, `:EndOfAnswerCompleteSeconds`, `:CompleteAfterAnswers`, `:CutOffBackoffFactor` | ESL-mode endpointing (defaults 6, 15, 3.5, 3, 2; GAD-T554). |
| `MindAttic:SpeechRecognition:Provider` | Live only: `Fallback` (default in live since GAD-T357: ElevenLabs Scribe, then OpenAI Whisper), or pin to `ElevenLabs` or `OpenAI` alone. |
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
The model is downloaded once per browser (GAD-T698): `wwwroot/js/model-cache.js` keeps it in Cache Storage
(`gadfly-models-v1`, keyed by its absolute URL) and hands `<ma-avatar>` one blob object URL per page lifetime, so the
get-ready page's prefetch (`voice-preload.js`), the exam room and the live examiner share it across Blazor navigations.
Both examiners' models stay cached, so switching examiner downloads the other one only the first time. A replaced model is
noticed by one HEAD request per browser session comparing its ETag/Last-Modified/Content-Length with the cached copy's; a
hosted copy needs CORS for HEAD and GET. Any failure falls back to the plain URL. The server sends `.glb` as
`public, no-cache` (revalidated) and, when the URL carries `?v=`, as `public, max-age=31536000, immutable`; the CSP allows
`connect-src blob:` for the object URL.
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
