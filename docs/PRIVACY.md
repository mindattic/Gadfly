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
deletes every session record of that student in that organization and deactivates the membership.

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
the camera, the exam continues. Nothing infers emotion or deception.

### Voice strain indicators (voice exams, GAD-T232)

During each spoken answer the exam room's browser measures the student's own voice from the same audio it
streams to the speech service: pitch level and variability, frame-to-frame jitter, loudness and how much of
the audio was voiced. Only those summary numbers are sent, with the answer; no audio, frame, pitch contour or
voice print is kept or sent for them. The server compares each answer with the student's own first two spoken
answers in that session (with the speech rate and pause ratio it already computes from the words) and stores
the differences with the answer's speech metrics. Reviewers see one descriptive line per answer on the review
transcript ("possible vocal strain / hesitation compared with this student's first answers: pitch +2.1
semitones, speech 30% slower ..."), always with the caveat that effort, nerves, a hard question, tiredness or
the room change the voice. It is never a lie, deception or emotion verdict, never an integrity event, never
part of the grade, hidden from blinded graders, and the student sees the same comparison in short on their own live transcript. The owner lifted the earlier
blanket drop of audio stress signals for this descriptive use (2026-11-21). The one score it yields (`strain.vsBaseline`) may make the examiner probe harder (GAD-T233), never change a grade; the EU note below still applies.
Erasure removes it with the rest of the speech metrics.

## Regulatory notes

- **EU AI Act.** Emotion recognition in education is prohibited (Art. 5(1)(f)); Gadfly infers no
  emotion and no deception. The voice strain indicators (GAD-T232) describe acoustic change against the
  student's own baseline and name no emotion, but they are derived from voice data: before selling into the EU,
  have counsel confirm they stay outside Art. 5(1)(f), or switch them off for EU tenants. AI that evaluates learning outcomes or monitors students during tests is
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
