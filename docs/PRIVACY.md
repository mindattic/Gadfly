# Gadfly Exams: privacy, consent and compliance (draft)

Status: draft for review (GAD-T61). Not legal advice; have counsel review before the first paying
customer. Defaults follow the open decisions in [SOCRATIC_PLATFORM.md](SOCRATIC_PLATFORM.md): adult
learners (D6), audio and transcripts kept 90 days, video never stored (D7), the AI's grades are preliminary and a
human grades and an authorized person approves before a student sees a result (D13, master plan Parts 8 to 11).

## What is collected, and for how long

| Data | Why | Kept |
|---|---|---|
| Account: user name, email, password hash, MFA secret | Sign-in (MindAttic.Authentication) | While the account exists |
| Organization membership and role | Access control | While a member; erasure deactivates it |
| Exam answers (transcript), word timings, speech metrics | Judging the answer; the instructor's review | The organization's retention period (default 90 days), then redacted |
| Answer audio (voice mode) | Instructor review | Same retention period, then deleted |
| Judgments, mastery, report, grade | The result | With the session, until the organization or the student requests deletion |
| Integrity events (derived only: face count, gaze region, focus loss, second speaker) | Evidence for a human reviewer | With the session; raw detail redacted with the transcript |
| Video frames | — | **Never leave the student's device; never stored** |
| Consent record (time + SHA-256 of the exact text shown) | Proof of consent | With the session |

Redaction (`privacy.retention`, daily) keeps the outcome and removes what the student said. Export
(`GET /privacy/export/{userId}`, the student or an org admin) returns everything held about a student in
one organization as JSON. Erasure (`POST /privacy/erase/{userId}`, org admin, typed confirmation)
deletes every session record of that student in that organization (including the webhook deliveries that announced
their sessions, so one still waiting is never sent) and deactivates the membership.

### Exported transcripts (GAD-T295)

A transcript download (PDF, Word, web page, text, captions or JSON) contains the exam's title, the student's name,
the attempt, every question with its times and the examiner's move, every answer as recorded, the read-back,
clarifications, corrections (original and corrected text), the response delay and pause counts, and the words the
speech recognizer was unsure of. It never contains audio or video. Three copies exist, chosen by the server for each
download:

- the **student copy** (the student, after their result is published) and the **grader copy** (anyone grading while
  grading is blind) leave out integrity notes, voice measurements, the examiner's reasons and the AI panel's
  exam-time scores;
- the **reviewer copy** (Owners, Admins, and graders once grading is not blind; API keys) adds them, marked as
  low-confidence observations for a person, never a verdict and never part of a grade.

Each download is recorded in the audit log (`transcript.issued`: who, when, which copy and format, its verification
code and SHA-256; no transcript text). Once downloaded, a file is outside Gadfly's retention and erasure: the
organization is responsible for the copies it keeps. After redaction or erasure, the verify page
(`/transcripts/verify`, open to anyone, rate-limited) reports an earlier copy as "the record has changed since" or
"no longer held", and never shows any transcript content.

## Consent

Shown before every attempt, in plain language: what is recorded, that video is processed on the device
only, how long data is kept, that integrity signals are evidence for a person and never an automatic
result, and that an instructor decides the grade. The SHA-256 of the exact text is stored with the
session, so changing the wording is visible in the record.

### Camera checks (Strict exams, GAD-T57)

Only exams set to **Strict** integrity use the camera, and only after the student accepted a notice that
says so (the exam room compares the stored notice hash with the current wording, so an attempt consented
under older wording never turns the camera on; a neutral "camera not used" note is recorded instead). The
MindAttic.Webcam library runs in the browser from a self-hosted build (no CDN request at exam time). It
reads landmark numbers in memory; the only pixels it touches are a 16x9 brightness thumbnail averaged to one
number. The same-person face signature stays in browser memory and has no export. What is sent: type, start,
end, confidence and one sentence per event (face absent, a second face, a possible change of person, a long
look off-screen, reading-like eye movement, a short glance annotated as thinking, or "camera not used").
While one of those situations is still going on (a face out of view for the whole exam, say), the browser also
sends a short update about once a minute: the same type, a start time, a "so far" end time and a flag saying it
is ongoing, plus a plain episode id built from the type and start time (GAD-T171). The server keeps one record
per episode and grows it; the sentence for an unfinished episode is written by the server. If the browser never
sends a closing note (tab closed, crash, lost connection) the reviewer sees the episode marked as still ongoing
when the exam ended. Nothing else is added: still no frame, image, landmark or face signature.
Server side only an allow-list of those types is stored (`WebcamIntegrityRecorder`), text and times are
bounded, volume is rate-limited and capped, confidence is capped at 0.4, and severity is decided by the server
and never above Low: reviewers see them as low-confidence annotations, never a penalty. If the student declines
the camera, the exam continues. The webcam modules do not infer emotion or deception.

### Eye contact with the examiner and eye-tracking setup (voice exams, GAD-T240, GAD-T305)

Any exam whose integrity level isn't Off checks, on the device, that the student looks at the talking examiner while
answering (docs/VOICE.md). Still nothing leaves the device but numbers:

- **The setup.** The get-ready page's eye-tracking check reads landmark numbers in memory and, twice a second, a 32x24
  brightness thumbnail that is averaged to two numbers (the face's brightness and the rest of the picture's) and dropped, to
  tell a dark face from a bright light behind it. Its advice is shown to the student only and sent nowhere.
- **The calibration.** The five-dot setup keeps four numbers (how a gaze point maps onto the page) and its accuracy in
  degrees in that browser tab's `sessionStorage`, for up to two hours, so the exam room can reuse it. It is not a face
  template, is never sent, and is gone when the tab closes.
- **What is sent.** Per eye-contact episode: type, start, end, a confidence and codes only ("remarked", "peak:NN", and the
  session's tracking quality as `q:NN;m:cal|head;a:N.N`: a 0..100 score, the method and the accuracy in degrees). The server
  writes every sentence a reviewer reads and keeps the quality numbers in the event's `DataJson`, which is cleared with the
  rest of the session's integrity detail on erasure. No frame, image, landmark, gaze trace or face signature is sent.

### Voice measurements (voice exams, GAD-T232, GAD-T259)

During each spoken answer the exam room's browser measures the student's own voice from the same audio it
streams to the speech service: pitch level and variability, frame-to-frame jitter, loudness and how much of
the audio was voiced. Only those summary numbers are sent, with the answer; no audio, frame, pitch contour or
voice print is kept or sent for them. The server compares each answer with the student's own first two spoken
answers in that session (with the speech rate and pause ratio it already computes from the words) and stores
the differences with the answer's speech metrics. Reviewers see one line per answer on the review transcript
with the raw differences ("voice measurements against this student's first answers: pitch +2.1 semitones*,
speech 30% slower* ...", * marking a difference beyond the usual range), always with the caveat that effort,
nerves, a hard question, tiredness or the room change the voice. Since GAD-T259 the line names no state: it does
not say "strain", "stress" or "hesitation". It is never a lie, deception or emotion verdict, never an integrity
event, never part of the grade, hidden from blinded graders, and the student sees the same differences in short
on their own live transcript. The owner lifted the earlier blanket drop of audio stress signals for this
descriptive use (2026-11-21, decision (b)). Since GAD-T258 (the owner reversed decision (b) the same day) none of
it steers how hard the examiner probes: the one score it yields (`strain.vsBaseline`) is recorded for the reviewer
only, and so are the wait before the first word and the hesitation count. Probing comes from what the answer says
(hedging, vague wording, a missing reason or example), the judges' score and evidence, and recitation. None of it
ever changes a grade. Erasure removes it with the rest of the speech metrics.

**Off by default in the EU (GAD-T259).** An organization marked as being in the EU or EEA (Organization
settings), and any student marked as living in the EU or EEA when an exam is assigned, gets no voice
measurements unless an admin turns them on for everyone. When they are off, the server discards the browser's
voice numbers before the answer is stored, compares nothing, and does not read the wait before the first word,
hesitations, long pauses or speech rate either (GAD-T283). The read-out check, which looks at timing and fluency, is
not applied to those answers. Probing works the same as everywhere else, from the words and the judges' reading
(GAD-T258). An admin can also turn them off for everyone. Each
change is recorded in the audit log.

**What this does and does not claim.** Earlier versions of this page said "Nothing infers emotion". That was too
broad while the voice comparison exists: pitch, jitter and loudness are standard features of vocal stress and
arousal. Since GAD-T258 the comparison no longer steers the questions, but it is still measured where it is on.
Gadfly does not label a student's emotional state, but it
does process features that the EU AI Act guidelines treat as emotion recognition when used in education
(research note 06, docs/EVIDENCE_MAP.md mechanic 5). That is why it is off by default in the EU.

## Regulatory notes

- **EU AI Act.** Emotion recognition in education is prohibited (Art. 5(1)(f)), and the Commission's 2025
  guidelines read detecting emotional arousal or anxiety during online exams as that. Gadfly names no emotion and
  makes no deception finding, but the voice measurements (GAD-T232) use stress-related acoustic features (they no
  longer steer probing since GAD-T258, but are still measured and shown to reviewers), so they are off by default for EU/EEA organizations and EU/EEA-resident students (GAD-T259).
  Turning them on for EU students ("On for everyone") needs counsel's confirmation first. AI that evaluates learning outcomes or monitors students during tests is
  high-risk (Annex III, point 3): selling into the EU requires risk management, data governance,
  logging (the session record and prompt-set hash), human oversight (human grading and approval), accuracy
  documentation and registration before deployment.
- **Illinois BIPA, Texas CUBI, Washington biometric law.** Face geometry is biometric. The webcam
  modules compute it on the device and send only derived events (counts, regions, "same person");
  written consent and a published retention schedule are still required where these laws apply.
- **FERPA** (US schools receiving federal funds): exam records are education records; the
  institution is the controller, Gadfly a "school official" under contract. Needed before K-12 or
  US university customers sign.
- **COPPA** (under 13): out of scope while customers are adults (D6). K-12 needs verifiable parental
  consent first.
- **GDPR / UK GDPR:** MindAttic is the processor for customer organizations; a Data Processing
  Agreement, sub-processor list (LLM providers, ElevenLabs, Azure) and international-transfer basis
  are needed. Export and erasure above serve access and erasure requests.

## Sub-processors (to publish)

LLM providers used by the Legion panel (Anthropic, OpenAI, Google, DeepSeek as configured), ElevenLabs
(speech), Microsoft Azure (hosting, database). Answers are sent to the LLM providers for judging; no
video and no account details are sent.

## Open items

- Customer-facing privacy policy and terms of service (from this draft).
- Data Processing Agreement template.
- Audio storage and deletion once voice mode stores audio (GAD-T53).
