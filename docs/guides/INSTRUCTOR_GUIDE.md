# Instructor guide

Gadfly Exams checks understanding by questioning a student, one question at a time, the way Socrates did.
Each follow-up is written from the student's own previous answer, and every answer is judged against
*your* source material by a panel of AI models, with citations. The AI's grades are preliminary: a human
examiner grades independently, a second reviewer reconciles large disagreements, and an authorized person
approves before a student sees anything.

This guide covers everything an instructor does: uploading material, checking the concepts and rubrics
built from it, building and assigning exams, reviewing sessions, and grading and publishing results.
Owners and admins of an organization can do all of it too.

> **You see what you own.** By default (decision D18, GAD-T170) an instructor sees and manages only the courses,
> source collections and assessments *they created*, plus the assignments, sessions, review pages, grading queue
> and analytics under them. Another instructor's items are not listed, and opening one by its address shows
> "not found" and records the attempt in the audit log as an access refusal. Owners and admins see the whole
> organization, so ask one if you need to share or hand over an item. Your admin can switch this off in
> **Settings** so every instructor sees everything. Examiners and second reviewers are not limited this way:
> grading is not assigned to individual examiners, so they see the organization's grading queue.

> **Text, voice and uploads.** Students type their answers by default; a Voice exam adds a talking examiner
> and spoken answers, with typing always available (see [Voice](../VOICE.md)). Browser integrity signals (tab
> switches, pastes and the like) are recorded, and Strict exams add on-device webcam signals. You can upload
> `.txt`, Markdown, PDF, DOCX, EPUB and HTML.

## Signing in

| Step | What happens |
|---|---|
| First sign-in | Your organization's admin gives you a user name and a temporary password. Sign in at `/login`; you are asked to choose a new password straight away. |
| Forgot your password | Press **Forgot password?** on the sign-in page and enter your user name or email. If an account matches, you are emailed a link to choose a new password; then sign in again with it (your authenticator code is still asked for). |
| Two-step verification (MFA) | Owners, admins and instructors must use an authenticator app (any TOTP app). At first sign-in you scan a QR code; after that every sign-in asks for the 6-digit code. Students don't need it. |
| More than one organization | If you belong to several, a drop-down in the top bar switches between them. Everything you see is scoped to the organization selected there. |

The top bar shows **Source material**, **Exams** and **Review**. Owners and admins also see **Members**.

## 1. Upload source material

Open **Source material** and create a **collection**: the material one exam is grounded in, such as a
textbook chapter, lecture notes or a set of readings. Then open it and upload documents.

| Rule | Detail |
|---|---|
| File types | Plain text and Markdown (`.txt`, `.text`, `.md`, `.markdown`), PDF, Word (`.docx`), EPUB and HTML (`.html`, `.htm`). PDF passages are cited by page; the others by chapter or heading. Scanned (image-only) PDFs are not read, and a damaged file is skipped with a message while the rest are processed. |
| Size | Up to 20 MB per file, up to 50 files per pick. |
| Same name | Uploading a file with a name that's already there replaces it. The next processing run notices the change. |
| File names | Unusual characters are replaced with `_`; the page tells you when a name was changed. |

The **Documents** table shows each file and where it stands:

| Status | Meaning |
|---|---|
| Waiting to be processed | Uploaded, not read yet. |
| Processed *(date)* | Read into passages; the **Passages** column shows how many. |
| Changed; process again | The file was replaced after it was processed. |
| Processed (original not on this server) | The passages are in the database but the uploaded original isn't in this server's upload folder. |
| An error message | That file couldn't be read. |

## 2. Process it

Press **Process**. A background job:

1. reads new and changed documents (unchanged files are skipped by their content hash) and splits them
   into passages;
2. has the AI panel find the **concepts** in the material (at most 40 per collection): a title, a
   definition, key facts, common misconceptions, and the passages each comes from;
3. writes a **rubric** for each concept: the points a real understanding covers (each with a weight),
   acceptable other ways of saying it, and misconceptions to watch for, each point citing its passages.

Progress shows on the page and in the library list. While a job runs, editing is paused. Concepts are
only re-extracted when a document changed; tick **Re-run concept extraction even if no document
changed** to force it. If processing fails, the message says why (for example "None of the uploaded
files could be read", or "The panel found no concepts; check the LLM provider keys and the material").

A collection shows **Ready (map vN)** once it has a concept map. Only ready collections can be used for
exams.

## 3. Review and edit concepts and rubrics

Read every concept before you build an exam on it. Each concept card shows its title, definition, key
facts, misconceptions and source citations. Click a citation number to read the passage it came from.

| Action | What it does |
|---|---|
| **Edit concept** | Change the title, definition, key facts and misconceptions (one per line). |
| **Edit rubric** | Change the understanding points and their weights (0.1 to 100), the acceptable variants and the misconceptions. A reworded point keeps its citations. A rubric needs at least one understanding point. |
| **Archive** | Takes the concept out of new exams. It is never deleted. **Restore** brings it back. |

Saving a rubric writes a **new rubric version**; earlier versions are kept, so sessions judged with them
still show what they were judged against. Every edit also bumps the collection's **concept map version**.
New exam sessions use the latest version, and each session records the version it started on.

### What "edited" protects

An edited concept or rubric is marked **edited**, and processing then leaves it alone:

- An edited concept is never overwritten by re-extraction, even if the panel words it differently.
- An edited rubric is kept exactly as you saved it; the panel doesn't rewrite it.
- An archived concept is never brought back by re-processing.
- Restoring an archived concept also marks it as edited, so it stays as it is from then on.

Concepts you haven't edited are the panel's to manage: if a later run no longer finds one, it is archived
automatically, and its rubric is rewritten on each run (the version only goes up when the content changes).

## 4. Build an exam

Open **Exams**, then **New exam**.

| Setting | What it means |
|---|---|
| Source collection | One of your ready collections. |
| Concepts to examine | Tick the concepts to cover. The numbers show the order they're examined in: the order you ticked them. **Select all** and **Clear** help with long lists. |
| Title | What students see. Up to 200 characters. |
| Duration (minutes) | 1 to 240. The clock is kept on the server and runs from the first question; it doesn't pause if the student leaves the page. |
| Difficulty | 1 gentle, 2 easy, 3 standard, 4 demanding, 5 rigorous: how hard the follow-ups push. |
| Most follow-up questions per concept | 0 to 20, after the opening question. The examiner moves on sooner when a concept is clearly mastered or clearly not understood. The whole exam is capped at (concepts × (follow-ups + 1)) questions. |
| Mode | **Text** or **Voice**. Students take every exam spoken, whichever you pick: Begin exam goes straight to a talking examiner that asks each question aloud, the student answers aloud with a live transcript, and their microphone and camera must stay on (the exam pauses if either stops). There is no typed answer mode. The exam clock starts when the examiner begins the first question. Exams run only where the deployment has voice turned on (`Gadfly:Voice:Mode`); otherwise the student's system check fails and the exam can't begin. |
| Integrity monitoring | **Off**, **Standard** or **Strict**. **Standard** records browser signals: the exam tab hidden or the window losing focus (with how long), pastes into the answer box, copying the question, a second screen connected, and a guess that developer tools are open (information only). **Strict** also asks the browser for permission to detect extra screens (the student may decline) and offers an optional **Go full screen** button, noting when the student leaves full screen. **Strict** also turns on camera checks (the student must allow the camera; the notice they accept says exactly what is and isn't collected): a missing face, a second face, a possible change of person, a long look off-screen and reading-like eye movement are noted as low-confidence events for your review, short glances away while thinking are shown as normal, and nothing is stored but those short notes (never video). If the student declines the camera the exam goes on and a neutral note says so. Audio signals (a second voice, reading cadence) come with voice mode. **Off** records no browser signals. Students are told in the notice they accept what is noted. |
| Examiner voice id / avatar id | Free text saved with the exam. Blank means the default; the deployment's voice and avatar settings decide what students actually hear and see ([Voice](../VOICE.md)). |
| Instructions shown to students | Shown on the student's exam list. |

**Save draft** keeps your work. **Publish** checks the exam (at least one concept, valid settings, the
collection is still ready, every chosen concept still exists) and then **freezes its settings**. Editing a
published or archived exam saves your changes as a new draft copy; the original stays as it is.

**Archive** stops new attempts. A student who is part-way through an attempt can still resume and finish
it. Existing sessions and results are kept.

## 5. Assign it

A published exam shows **Assign to students** below its settings.

| Field | Detail |
|---|---|
| All students / pick students | Everyone in the organization with the Student role, or the ones you tick. |
| Opens / Closes | Optional, in your local time (the label shows your UTC offset). Students can't start before it opens or after it closes. |
| Attempts allowed | 1 to 20. |

Students who already have this exam are skipped. Students must already be members of the organization
(an admin adds or invites them under **Members**). Students find the exam under **My exams** when they sign in.

The **Assignments** table shows each student's window, attempts used, their latest session's state and
whether a result was published. A finished attempt links to its grading page (see Grade and publish): results reach
students only through the grading workflow.

## 6. What the student sees

1. **My exams** lists the exam with its window and attempts.
2. **Start** opens a get-ready page: a browser and connection check, a short explanation of Socratic
   questioning with a practice question, and a plain-language notice they must accept (what is
   recorded, that no video is stored, how long answers are kept, that you decide the grade). The exact
   wording they accept is fingerprinted (SHA-256) and stored with the session.
3. In the exam room they see one question at a time, the concept it's about, a countdown and "topics
   finished X of Y". They type an answer, or press **I don't know**. They never see scores.
4. The exam ends when every concept is closed, the question limit is reached, time runs out, or they
   press **Finish** (which uses up the attempt). Reloading the page returns them to the same question.

The [Student guide](STUDENT_GUIDE.md) is written for students; you can share it.

## 7. Review a session

Open **Review**. Each row is one attempt: student, exam, attempt number, state, when it ended, the AI
recommendation, how many answers the judges disagreed on, how many integrity flags there were, and any earlier
instructor decision (the older single-grader record; current grading is in **Grading**). Filter by exam,
**Needs a grade** (ended, not graded) or **Judges disagreed**.

Open a session to read it.

### The summary

State and end reason, start and end times, the AI recommendation and overall mastery, any earlier instructor decision,
and export links. The recommendation is one of:

| Recommendation | When |
|---|---|
| mastered | Every examined concept reached 80% mastery. |
| partial | Average mastery is at least 52.5% (halfway between the 80% mastery and 25% aporia thresholds). |
| not-demonstrated | Anything lower. |

### The timeline

Every question in order, with:

- the **move**: the kind of question it was (Elicit, Clarify, Probe, Counterexample, Contradiction,
  Apply, Synthesize);
- the student's **answer**, how long they took to start typing, and when they submitted;
- a **contradiction** warning when an answer conflicts with something they said earlier;
- the **judgment**: an overall score, the panel's agreement, a short summary, and a score per rubric
  criterion with the reason;
- **citations**: each criterion's sources open in place to show the passage from your material. If the
  passage was removed since (its document was re-processed), the page says so;
- **each judge's verdict**: every provider on the panel, its score and its reasoning. A judge whose score
  is 25 points or more away from the panel's is marked **dissents**;
- the **text style** reading (how much the answer reads like written text compared with the student's
  other answers), which never affects the score;
- mastery of the concept after the answer;
- integrity notes that happened during that answer.

**Judges disagreed.** When the panel's agreement on an answer is below 60%, or no judge returned a usable
verdict, the answer is marked "Judges disagreed — review this answer", the votes are opened for you, and
a banner at the top counts them. Read those answers yourself before grading.

### Integrity notes

The **Integrity summary** lists each kind of signal, how often, for how long, during which questions, and
what it means in plain words. Every signal also appears on the timeline next to the answer it happened
during. Signals are evidence for you to weigh, never findings: nothing fails a student automatically, and
the platform never tries to infer dishonesty or emotion.

**Looking away is normal.** People look up or away from a screen while they recall or reason; research
supports it as a sign of thinking, not lying, and "eye-accessing cues" lie detection has been tested and
found to have no effect. Gaze events like this are listed as normal behavior and not counted as flags.
The signals that can matter are a second face, a second voice, a sustained reading pattern at one
off-screen spot, or the exam tab being hidden, and even those have innocent explanations.

What is recorded today:

| Signal | From | Notes |
|---|---|---|
| Exam tab hidden / window lost focus | the browser | With how long it lasted. Brief focus changes (a notification, an alert) are common. |
| Pasted into the answer box | the browser | Text exams only, with how many characters. |
| Copied the question | the browser | Some students copy the question to re-read it. |
| Extra screen connected | the browser | On Strict, only if the student allowed screen detection. |
| Left full screen | the browser | Strict, only after the student chose **Go full screen**. |
| Developer tools may be open | the browser | A guess; information only, never counted as a flag. |
| Answer reads like written text | the server | Raised when an answer reads noticeably more like written text than the student's other answers. Weak evidence on its own. |

Browser signals are sent every few seconds and survive a page reload; nothing is blocked, and no signal
is ever rated High. The browser's clock is corrected against the server's, and repeated identical signals
are merged.

### Mastery and the AI report

**Mastery by concept** shows each concept's final estimate, the number of questions on it and how it
closed (Mastered, Aporia, Exhausted, Open). The **AI report** below it is the panel's write-up: mastery
per concept, quoted evidence with citations, and disputed answers.

## 8. Grade and publish

Grade an attempt once it has ended, from **Grading** in the navigation (the review page links to each session's
grading workspace). Results reach students only through this workflow.

1. **AI preliminary grading.** When a session ends, the AI panel grades each learning objective (levels 0 to 5, with
   rationale and cited evidence). **Retry AI grading** appears if it fails. If an answer's transcript still needs
   the student, the grade waits in **Transcript verification pending**.
2. **Human grading.** An examiner (or teacher) scores every objective independently and submits all of them
   together. Under a blind policy, the AI's evaluation stays hidden until they submit (see below).
3. **Score comparison.** The page shows AI and human scores side by side and the combined score under the exam's
   locked grading policy (AI and human weights). Large differences are flagged by the policy's thresholds.
4. **Reconciliation.** When the difference needs it, the grade is held at Reconciliation required: request a second
   review, and the second reviewer records the cause, the resolution and a resolved score. The score is recalculated.
5. **Final approval.** An authorized person (Teacher, Admin or Owner, never the Examiner) approves the final grade, or
   reopens human grading with a reason.
6. **Publish.** Publishing releases the final grade and the assessment report to the student on **My exams**.
   Republish after a change; nothing is shown to the student before this step.
7. **Appeals and amendments.** A student can appeal a published result; the teacher resolves the appeal as upheld or
   dismissed, and can amend the grade with a reason. The result stays visible during an appeal, and amended results are
   marked.

Every step is audited, and a refused attempt (for example an examiner trying to approve) is recorded as an access
refusal. The AI never publishes anything.

### Grading as the blind examiner

When the exam's grading policy is blind (the default), the examiner grades before seeing anything the AI
concluded. Until you submit your own grades for a session, Gadfly hides the AI evaluation **and** the
exam-time judge's per-answer evidence: the stance (supports, contradicts, misconception, uncertain), its
dimension and its notes (the "Recorded evidence" line on the grading page), plus, on the session review page,
the per-answer judging scores, summaries, votes, contradictions, mastery estimates and the AI report, in the
page, the CSV and Markdown exports and the status endpoint. You still see the student's original and verified
answers, clarifications, corrections and the full transcript, which is the evidence you grade. Once you submit,
everything shows as before. Owners and Admins, who do not enter scores, keep the full review view.

## 9. Export

From a session's summary:

| Export | Contents |
|---|---|
| turns (CSV) | One row per question: session, student, exam, attempt, turn, concept, move, question, answer, answer mode, times (UTC), latency, overall score, agreement, whether judges disagreed, judge summary, criterion scores, each judge's score, text-style score, mastery after, integrity events. Opens correctly in Excel. |
| report (Markdown) | The AI report plus any earlier instructor decision: grade, score, who decided and when, whether it overrides the recommendation, and the comment. |

A PDF export is not built.

## How long data is kept

Answers and transcripts are kept for your organization's retention period (90 days by default). After
that, what the student said is removed and replaced with "[redacted after the retention period]";
questions, judgments, mastery, the report and your grade stay. Video is never stored. See
[Privacy](../PRIVACY.md) for the full list.
