# Gadfly Exams: product overview

For universities, colleges, certification bodies and training providers deciding whether Gadfly fits their
assessment. It describes what the product does today, what protects students and the institution, and, just as
plainly, what has not yet been shown. Each section links to the deeper document behind it.

## What Gadfly does

Gadfly runs **spoken oral exams with an AI examiner**, then has **people decide the grade**.

A student sits the exam on camera, talking to an animated examiner who asks questions aloud. The examiner opens each
learning objective with "what is it?" and then writes every follow-up from the student's previous answer: what did
you mean by that word, why is that true, what would change if, here is a case that doesn't fit, and, when two of the
student's own claims conflict, "can both of these be true?". The exam is built from the instructor's own course
material, and every question and judgement is grounded in that material, with citations to the passages.

**Why that is different.** When anyone can paste a question into an AI, a correct answer proves little. A follow-up
written from the student's own last answer cannot be looked up or prepared in advance: to answer it, the student has
to explain, defend and extend what they just said. This is the Socratic *elenchus* and Piaget's clinical interview put
to work; the reasoning and its limits are set out, with the verification level of every source, in
[SCIENCE.md](SCIENCE.md). Gadfly does not claim this makes it a more valid measure than other formats (see
[What has and hasn't been shown](#what-has-and-hasnt-been-shown)).

**Who it is for.** Organizations with instructors, examiners, second reviewers, admins and students: departments and
whole institutions, certification bodies, and corporate training. Each organization's data is kept apart from every
other's.

## Building an exam

1. **Upload course material**: text, Markdown, PDF, Word (.docx), EPUB or HTML. A scanned, image-only PDF is not read.
2. **Gadfly drafts a concept map and rubrics** from it: the concepts, the points real understanding covers,
   acceptable variants and common misconceptions, each citing its passages. Instructors edit them; an instructor's edit
   is never overwritten, and every version is kept.
3. **Build the assessment**: learning objectives, rubric criteria with descriptions for levels 0 to 5 (Insufficient
   Evidence up to Transfer and Critical Reasoning), weights, an optional **fixed opening question** per objective (the
   same first question, word for word, for every student; checked so it gives nothing away before it is saved), and a
   grading policy.
4. **Approve the material.** Before a summative examination can be published, a person must read the concept map and
   rubrics the AI drafted and approve that version. If they change later, the exam starts no new attempts until the new
   version is approved.
5. **Publish and assign**, with due dates, attempt limits and per-student accommodations. Publishing locks the
   objectives, rubric and grading policy; a later policy change needs a reason and an admin's approval.

Staff can **preview an exam as a student** before anyone sits it: it runs the real question plan on a simulated panel,
calls no AI provider and saves nothing.

Guides: [Instructor guide](guides/INSTRUCTOR_GUIDE.md), [Exams manager](guides/admin-exams.md).

## The exam experience

- **Get ready.** A system check of camera, microphone and browser, a short eye-tracking setup, a practice question, and
  a plain-language notice the student accepts: what is recorded, that video stays on their device, how long data is
  kept, that integrity notes go to a person and are never an automatic result, and that people decide the grade.
- **Meet the examiner first.** A five-minute **practice interview** from *My exams* lets a student try the talking
  examiner. It is ungraded, and nothing from it is kept or counted.
- **The exam room.** The examiner speaks each question; the student answers aloud. An answer ends after a pause that
  adapts to the student's own pace. While an answer is judged, the examiner may say a short, fixed line such as "Let
  me think about that", which never hints at how the answer was judged. There is no feedback or hinting during a
  summative exam. "I don't know" is accepted and recorded as an honest answer.
- **Voice and camera are part of the exam.** If the camera or microphone is lost, the exam pauses, and the clock stops,
  until it works again. A dropped connection, a reloaded page or a closed laptop lid resumes the same question.
- **Time** is kept by the server, not the browser.
- **Accommodations**, granted per student and per exam by staff and recorded in the audit log: extra time; an
  eye-contact accommodation; and **typed answers** for a student who cannot answer aloud (the examiner still speaks
  the questions, and typed answers are stored and shown as typed).
- **Results.** After a person approves and publishes the grade, the student sees a report that says, objective by
  objective, what was missing and what the next level asks for, and how it changed since an earlier attempt. They can
  download a transcript and a completion certificate, both of which anyone can check at a public verification page,
  and can appeal a published result.

Languages: the examiner, the prompts and the answer-content cues are written for English.

Guides: [Student guide](guides/STUDENT_GUIDE.md), [How the examiner asks questions](guides/how-the-examiner-asks-questions.md),
[Accommodations](guides/accommodations.md), [Practice interview](guides/practice-interview.md), [Voice](VOICE.md).

## Grading with human accountability

The AI never decides a grade on its own and never publishes anything.

- **A panel of models, not one.** Several AI models from different providers score each objective against the rubric
  and the cited material. Their grade is **preliminary**.
- **A human examiner grades independently.** By default grading is **blind**: the examiner does not see the AI's
  evaluation or any integrity note until their own scores are in. **Anonymous marking** can also hide the student's
  name behind a session code until then.
- **Scores are combined under a locked policy** (40% AI and 60% human by default). The human weight can never be zero.
  Where the AI found no usable evidence for an objective, its score is set aside and the human's stands alone.
- **Disagreements are reconciled.** A gap of 10 points is flagged for another look and a gap of 20 points (both
  adjustable), a split panel or a score on a grade boundary holds the grade until someone reconciles it, recording the
  cause.
- **Separation of duties.** Whoever scored a grade cannot reconcile or approve it, and whoever reconciled it cannot also
  approve it while someone else could. A one-person team can allow a single grader; every such step is audited.
- **Approval, then publication.** Nothing reaches the student until an authorized instructor, admin or owner approves
  the grade and it is published. Appeals and amendments follow the same trail.
- **The path is visible.** Because every student gets different follow-ups, reviewers see for each objective how many
  questions of which kinds were asked, which rubric criteria the evidence reached and which answers the level does not
  rest on (recited, given after a hint, or after a question that gave the answer away).
- **Everything is audited.** Every grading step, refusal, waiver, setting change and download is recorded in an audit
  log whose rows are never deleted.

Reports come as PDF, Markdown and CSV; instructors also get a class summary PDF.

Guides: [Grading guide](guides/grading-guide.md), [How grading and results work](guides/grading-and-results.md).

## Fairness and integrity safeguards

**Checking the AI against people.** Gadfly includes the tools to test its own panel, per exam:

- **Blind double marking**: a second examiner grades sessions blind to the panel and to the first examiner.
- **Panel calibration**: the panel's agreement with human markers on a double-marked set, against fixed thresholds
  (panel-human QWK at least 0.70, no more than 0.10 below human-human agreement, standardized mean difference under
  0.15). A calibration expires when the models behind the panel or the grading prompts change. An exam can be set to
  give the panel **no weight at all** until it has a current, accepted calibration; this is off unless turned on.
- **Bias probes** that check whether wording, length or an instruction planted in an answer moves the grade, for the
  panel and for human markers, and a **self-preference check** for models grading questions they wrote.
- **Fairness views by student group** in analytics: score gaps, how often probing was pressed harder and on which cue,
  answers cut off by the end-of-answer pause, eye-contact and camera note rates, the cost of the screen fade, typed
  versus spoken answers, and speech-recognition accuracy measured against human reference transcripts.

**Speech recognition** is the largest documented fairness risk in a spoken exam. Answers are judged as heard; words the
recognizer was unsure of are named to the panel, so a misheard word is not scored as wrong content, and marked for the
human grader. Judges are told never to penalize accent, grammar or fluency, and that length earns nothing.

**Integrity notes** (a hidden tab, a second face, looking away from the examiner, a second voice, an answer that sounds
read out) are:

- low-confidence notes for a person to weigh, **never a verdict** and **never part of the grade**;
- hidden from a blind grader until their scores are in;
- shown to the student too, once their result is published, so they can respond in an appeal.

Camera notes are capped at low confidence. Gadfly makes no claim to detect cheating, lying, stress or emotion. The
earlier AI-text estimate is switched off. Gadfly cannot detect a hidden earpiece; its defence is the follow-up questions
themselves, and whether that is enough is one of the things the pilot is designed to test. For a high-stakes exam that
needs more certainty, run it in a supervised room.

Guides: [Reading integrity notes](guides/integrity-notes.md), [Eye contact and the screen fade](guides/eye-contact-and-the-screen-fade.md),
[Science](SCIENCE.md).

## Privacy and the EU position

- **Video never leaves the student's device and is never stored.** The camera checks run in the browser; only short
  descriptions of events (type, time, a capped confidence) are sent.
- **Audio is not stored.** It is streamed to the speech recognizer and discarded.
- **What is kept**: the transcript of answers, judgements, grades, integrity notes, the consent record (with a hash of
  the exact text shown) and the audit log. Transcripts are kept for the organization's retention period (90 days by
  default, configurable), then the student's words are removed and the outcome kept.
- **Logs are kept at least six months**, separately from retention, as the EU AI Act asks of high-risk systems. Audit
  rows are never deleted; outside-call timings are kept a year by default and never under 184 days.
- **Export and erasure.** An admin (or the student, for export) can export everything held about a student as JSON,
  and an admin can erase a student's data. The erasure itself is recorded.
- **Voice measurements are off by default in the EU.** Gadfly can measure differences in a student's pitch, loudness
  and pace for reviewers. For an organization marked as in the EU or EEA, and for any student marked as living there,
  this is off unless an admin turns it on, because the EU AI Act prohibits emotion recognition in education. These
  measurements never steer the questions and never affect a grade.
- **Research use** is a separate, optional consent, only when the organization runs a study. The research export is
  de-identified with keyed pseudonyms and holds back small groups.
- **The AI Act.** Gadfly treats itself as a **high-risk** system under Annex III, point 3 (evaluating learning
  outcomes and monitoring tests). Human grading and approval, logging, and the documented limits above are built for
  that. A review of the remaining provider and deployer duties is in [COMPLIANCE.md](COMPLIANCE.md); it is a draft for
  counsel, not legal advice.
- **Sub-processors**: the AI providers on the panel, ElevenLabs and OpenAI for speech, and Microsoft Azure for hosting
  and the database. Answers are sent to the AI providers for judging; no video and no account details are.

Still open before contracts: the customer privacy policy and terms, a Data Processing Agreement template, and counsel's
review of the drafts. Details: [PRIVACY.md](PRIVACY.md), [Privacy in plain words](guides/privacy-in-plain-words.md).

## Integrations

- **LMS (LTI 1.3 / LTI Advantage)**: Canvas, Moodle, Blackboard Learn and D2L Brightspace. Instructors add an exam from
  the LMS's content picker (Deep Linking); students open it from their course without a separate password; the course
  roster can be synced on demand and refreshes on a schedule (Names and Role Provisioning); the final percentage goes
  back to the gradebook once the result is released (Assignment and Grade Services), with retries and a visible failed
  state. One grade per placement, not per objective. [LMS setup](guides/lti-setup.md).
- **Platform API v1** with organization API keys: create source collections and upload documents, process them,
  create exams, assign students, issue signed launch links, list attempts, read results and download transcripts. An
  OpenAPI document is at `/openapi/v1.json`. [API reference](API.md).
- **Webhooks**: signed (HMAC-SHA256 with a timestamp) and retried; the event offered today is `session.completed`.
- **As software packages**: the Socratic engine (`Gadfly.Core`) and the source-material layer (`Gadfly.Sources`) can be
  used in-process. [Samples](../samples/README.md).

There is no institutional single sign-on (SAML or OpenID Connect) apart from the LMS launch.

## Admin tools

Owners and admins get one admin area:

- **Dashboard**: what needs attention (attempts awaiting grading, reconciliations, approvals, appeals, students near a
  due date who haven't started, failed background work) and the numbers for the last 7 and 30 days.
- **Students** directory with profiles, invitations and CSV import; **Exams** manager (windows, duplicate, archive,
  export and import, preview, objective coverage); **Assignments** console; a **Live sessions** monitor to add time,
  pause or end an exam in progress.
- **Members and roles** (Owner, Admin, Instructor, Examiner, Second reviewer, Student), with multi-factor sign-in
  required for staff.
- **Organization settings**: retention, the EU marker and voice measurements, grading separation, email notifications.
- **Audit log** with filters, search and CSV/JSON export; **System status** with health checks, background jobs (retry
  and cancel), webhooks and timings.
- **API keys**, **webhooks**, **LMS connection**, **research** settings and export, **email preview and test-send**,
  **plan and usage**.
- A built-in **help center** with guides for each role, and guided tours of the main pages.

Guides: [Admin guide](guides/ADMIN_GUIDE.md).

## Plans

Prices are by quote. Each plan sets default seat and exam allowances, which can be adjusted per organization:

| Plan | Suits | Staff seats | Student seats | Exams |
|---|---|---|---|---|
| Trial | One course team trying Gadfly on their own material | Up to 3 | Up to 25 | 50 in total, over 30 days |
| Standard | A department running spoken exams across a few courses | Up to 10 | Up to 250 | 500 a month |
| Institution | A faculty or whole institution | Up to 50 | Up to 2,500 | 5,000 a month |
| Custom | Certification bodies, consortia and anything larger | By agreement | By agreement | By agreement |

Owners see their plan, usage and a usage export on **Plan & usage**; warnings start at 80% of an allowance, and at the
limit new seats or new attempts are refused. Card checkout for self-serve upgrades exists but stays off until a payment
provider is configured. Every feature above is in every plan.

## What has and hasn't been shown

**Built and working in the product** is everything above. **Not yet shown** is whether it measures what it is meant to
measure:

- **No validity claims.** Gadfly makes none until its own pilot ([PILOT.md](PILOT.md)) measures them. The research
  does not show that a Socratic oral exam is a more valid measure than other formats.
- **How well the AI panel grades spoken, transcribed answers** has not been measured by any published source; the
  double-marked pilot and per-exam calibration are the route to that number. Until an exam has an accepted calibration,
  its AI grades are labelled provisional.
- **That follow-up questions reduce cheating** is the design's hypothesis, not a finding. The pilot includes an arm
  where participants relay answers on purpose.
- **Fairness across groups** (speech recognition by accent and fluency, typed versus spoken answers, the screen fade,
  the English answer cues on second-language speakers) has measurement tools built in but no results yet.
- **Accessibility**: the app was built and checked toward WCAG 2.2 AA, but has not had screen-reader testing or a
  formal audit, so no conformance is claimed (see the accessibility statement at `/accessibility` in the app).

**Where the build departs from the research that shaped it.** Gadfly's design follows the *Elenchus at Scale*
dissertation, with a few recorded owner decisions that differ from it
([blueprint-gap-analysis.md](research/blueprint-gap-analysis.md)):

1. Spoken answers are judged as heard; the student is not asked to confirm or correct the transcript, so recognition
   errors are flagged to graders instead.
2. Voice and camera are mandatory; typing is a granted accommodation, so it reaches only students who declare a need.
3. After a long look away from the screen while speaking (never while thinking), the examiner gives gentle reminders: a head
   tilt, then a light dim around her that clears at once, then at most one warm spoken line (GAD-T513, GAD-T557/T561). The
   eye-contact accommodation switches them off, and their rate and cost by group are measured.
4. Cleared scores hold an attempt out of grading, and the attempt still counts against the student's limit.

The full plan and what is built against it: [MASTER-PLAN.md](MASTER-PLAN.md), [MASTER-PLAN-STATUS.md](MASTER-PLAN-STATUS.md).
For running the service: [OPERATIONS.md](OPERATIONS.md), [DEPLOY.md](DEPLOY.md), [SECURITY_REVIEW.md](SECURITY_REVIEW.md).
