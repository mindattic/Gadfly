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
| Starting quickly | Speech is streamed: she starts on the first audio chunk. The examiner's voice model defaults to ElevenLabs' low-latency `eleven_flash_v2_5` unless `MindAttic:Avatar:ElevenLabs:ModelId` is set (`Gadfly:Voice:TtsModel` changes the default). Faster first audio options are MAA-T15. Quality-vs-latency tiers are below (GAD-T415). |
| Prepared ahead | Fixed lines (the wait fillers first, then the eye-contact remarks) are synthesized once per server into the library's phrase cache when the first exam room opens (`ExaminerSpeechWarmup`, off with `Gadfly:Voice:WarmPhrases=false`), so they start at once. The cache is bounded and questions go into it too, so an exam room opened ten minutes or more after the last check checks the lines again: a line still cached costs nothing (and is kept), and only one pushed out is synthesized again (GAD-T329). Questions are cached after their first playing, so **Repeat the question** and a reload replay instantly. The next question itself is written only after the answer is judged; with no read-back (GAD-T405) judging starts the moment the answer is in, and the wait fillers cover that wait after every answer. A supported prefetch is MAA-T14. |
| Acknowledging the answer | The moment an answer is in (the end-of-answer silence or **Done answering**), she says a short acknowledgement, the backchannel a listener gives when someone stops talking (GAD-T496): "Mm, okay.", "Okay.", "Mm-hm.", "I see.", "Alright.", "Okay, thank you.", "Mm. Okay." or "Thank you." (`ExaminerFiller.Acknowledgements`). Receipt, never assessment: no "good", "right" or "correct", and the same lines follow strong and weak answers alike. The server picks the next one (never the same twice running) and offers it when each answer window opens, so the browser plays it with no round trip; it goes through the avatar hub like every line, so she lip-syncs it, and it is prepared ahead per examiner voice with the other fixed lines (in mock mode by the mock voice), so it costs nothing per turn. The microphone is already shut, so it is never heard as part of the answer. A question ready before it has made a sound skips it; the wait filler below holds off until 1.5 s after it. A kept answer sent again is not acknowledged twice. Optionally (`Gadfly:Voice:BackchannelMs`, off by default), while the student still holds the floor, a finished sentence followed by that many milliseconds of silence (500 is a natural choice) gets a soft "Mm." or "Mm-hm." that does not end the turn: the countdown runs on and speaking again stops her at once. Once per answer; off by default because the microphone is open while she makes it and only the browser's echo cancellation keeps her sound out of the answer. |
| Filling the wait | While the panel judges an answer and writes the next question (GAD-T290), she says one short neutral line if the question isn't ready within 2 seconds ("Let me think about that.", "Okay, give me a moment.", ...) and one more if the wait passes 20 seconds ("Just a moment more."), over a very quiet distant-typing sound (GAD-T409; typing at gain 0.06 instead of page turns since GAD-T502). The lines are fixed in `Voice/ExaminerFiller.cs`, picked at random (never the same first line twice running), never about the answer (no "good" or "interesting"), spoken with the microphone shut, and never put in the transcript or the record. A question that arrives mid-line waits for the line to finish; the rest are dropped. They are prepared ahead (above), so the first one plays at once. |
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
- **Live indicator.** "Looking at the examiner", "Looking away from the examiner", reading, no face, more than one face, the
  camera picture stopped, or "can't follow your eyes right now".
- **While answering** (the avatar is listening): after `fadeStartMs` (2 s) of continuous look-away the whole exam screen starts
  fading toward a heavy dim and blur over `fadeRampMs` (3 s) with "Please look at the examiner", and restores smoothly
  (`fadeRestoreMs`) when the gaze returns. After `remarkAfterMs` (5 s), or at once for a reading pattern, no face or a second
  face, the examiner says so out loud ("Please look at me while you answer"), at most once per `remarkGapMs` (15 s). The words
  are fixed server-side (`WebcamIntegrityRecorder.ExaminerRemarks`) and offered to the avatar hub like any question.
  One voice at a time (GAD-T472): **Repeat the question** pressed while she is saying a remark waits for the remark to finish
  (bounded at 13 s, a little past the remark's own 12 s cut-off) and then asks the question; once a question is on its way no
  remark can start, since she is no longer listening.
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

1. The answer is recorded, verified (a spoken answer is taken as heard: no call) and judged by the panel, as before.
2. The planner picks the move (local, no call).
3. **One fast model** words the question (`ILiveTurnModel`, `Gadfly/Socrates/LiveTurnModel.cs`; `QuestionGenerator.LiveModel`):
   `Gadfly:LiveTurn:Provider` (default `claude`) and `Gadfly:LiveTurn:Model` (default Claude Sonnet 5, `claude-sonnet-5`; an
   Opus id is refused and replaced by Sonnet 5). Timed as `live.questionGeneration.LiveDraft`.
4. **The same model runs the leak guard** on that question, a one-word LEAKS/SAFE reply (`live.questionGeneration.CheckLeakLive`).
   The question is returned only after the guard says SAFE. LEAKS redrafts once and then uses the plain fallback question,
   and a reply that is neither word counts as undecided and also falls back (fail closed), exactly as with the panel guard (GAD-T92).
5. The question goes to the examiner's voice.

If the fast model fails (provider down, timeout), that attempt goes back to the panel vote and the panel guard, so a
provider outage costs speed, never safety. Every call is still metered (`PanelCall`, kind `live-turn`) and timed
(`ApiCallTimings`, with a `.firstChunk` row for the streaming seam below). Mock mode (`Gadfly:AiMode=mock`) uses the mock
panel's own template replies, so nothing is called or billed.

**Streaming seam (GAD-T495).** `ILiveTurnModel.StreamAsync` returns the reply as an `IAsyncEnumerable<string>` of text deltas.
MindAttic.Legion has no streaming call yet, so today the Legion-backed model yields the whole reply as one delta. A
streaming provider call fits behind the same interface. The generator still guards the whole question before returning it,
so speaking the text while it is still being generated also needs the guard to run on each sentence before that sentence
goes to the voice.

**Departure from the blueprint.** ElenchusAtScale has the multi-family panel word the questions and vote on the leak guard.
The owner moved both onto one model for conversational latency (docs/research/blueprint-gap-analysis.md, owner decision 7).
The panel still grades every answer.

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
  examiner lines are pre-warmed per voice. Mock mode is unchanged: no id is passed and the fake voice speaks. The avatar
  model is still the deployment's one (`Gadfly:Voice:AvatarModelUrl`); a per-exam avatar needs an avatar catalog in
  MindAttic.Avatar.
- The transcript reaches Gadfly through the student's browser (server to browser to server), the same trust model as the
  existing `SessionHub.Answer`. The metrics and verification advice are recomputed on the server from the relayed words.
  A server-side tap on the recognizer (so the browser never carries the text) would need a hook in the speech library.

## Voice quality vs. latency (GAD-T415)

ElevenLabs offers several text-to-speech model tiers that trade voice quality for how long the examiner takes to start
speaking. `Gadfly:Voice:TtsModel` already picks the tier (GAD-T303), and today defaults to the fast end of that
tradeoff, per the owner's "fluid conversation now" call:

| Tier | Example model ids | Latency | Voice quality |
|---|---|---|---|
| Fast / low-latency (**default**) | `eleven_flash_v2_5`, `eleven_turbo_v2_5` | First audio in well under a second; this is what keeps the question-and-answer rhythm of an exam feeling live. | Noticeably more robotic/clipped than the quality tier, especially on longer or more expressive sentences. |
| Quality | `eleven_multilingual_v2` (the library's own upstream default) | Visibly slower to start — often a second or more before the first chunk, which shows up as a longer pause before each question. | ElevenLabs' most natural-sounding voice; worth it for a recorded demo or a review, less so mid-exam. |

**Current limitation — not yet admin-selectable per organization or per exam.** `ElevenLabsOptions.ModelId`
(`MindAttic.Avatar.ElevenLabs`) is bound once from configuration into a singleton when the server starts
(`AddMindAtticAvatar` in `MindAttic.Avatar.AspNetCore`) and stays fixed for the life of the process; every organization's
voice exams on that process share it. Unlike the voice id, which `AvatarSpeechService.StreamAsync`/`AvatarSpeechHub.Speak`
already accept as a per-call override, there is no per-call or per-session override for the model id, so Gadfly cannot
safely let one organization run "quality" while another runs "fast" at the same time on a shared server, and changing the
tier at all still means editing `Gadfly:Voice:TtsModel` (or `MindAttic:Avatar:ElevenLabs:ModelId`) and restarting the
app — there's no admin screen for it yet. Filed against the library as MAA-T20 (add a per-call `modelId` override
mirroring `voiceId`, and make the option hot-reloadable) so Gadfly can later expose a real per-organization picker in
`OrgSettings.razor` next to the EU voice-measures toggle (GAD-T259); tracked on the Gadfly side as GAD-T415, left open
pending that library change.

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
| `Gadfly:Voice:EndOfAnswerCompleteSeconds` | The shorter silence that ends an answer whose last words are a finished sentence (1 to the floor, default 2; GAD-T498). Multiplied by the extra-time accommodation. |
| `Gadfly:Voice:SpeechHangoverMs` | Silence after which the browser commits one stretch of speech to the recognizer (300 to 2000, default 700; GAD-T498). |
| `Gadfly:Voice:BackchannelMs` | The listening "Mm-hm." after a finished sentence and this much silence, without ending the turn (0 = off, the default; else 300 to 3000; GAD-T496). |
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
