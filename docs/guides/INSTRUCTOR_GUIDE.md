# Instructor guide

Gadfly Exams checks understanding by questioning a student out loud, one question at a time, the way Socrates did.
Each follow-up is written from the student's own previous answer, and every answer is judged against *your* source
material by a panel of AI models, with citations. The AI's grades are preliminary: a human examiner grades
independently, large disagreements are reconciled, and an authorized person approves before a student sees anything.

This guide covers everything an instructor does: uploading material, checking the concepts and rubrics built from
it, building and assigning assessments, setting accommodations, reviewing sessions, grading and publishing results,
transcripts and analytics. Owners and admins can do all of it too. Examiners and second reviewers, who only grade,
can start with the [Grading guide](grading-guide.md).

> **Note.** **You see what you own.** By default an instructor sees and manages only the courses, source
> collections and assessments *they created*, plus the assignments, sessions, review pages, grading queue and
> analytics under them. Another instructor's items are not listed, and opening one by its address shows "not found"
> and records the attempt in the audit log. Owners and admins see the whole organization; ask one if you need to
> share or hand over an item. Your admin can switch this off in **Settings > What instructors see**. Examiners and
> second reviewers are not limited this way: they see the organization's grading queue.

## Signing in

| Step | What happens |
|---|---|
| First sign-in | Accept the invitation email and choose a password, or sign in with the user name and temporary password your admin gave you; you then choose a new password. |
| Two-step verification | Every staff role (owner, admin, instructor, examiner, second reviewer) signs in with an authenticator app. At first sign-in you scan a QR code; after that every sign-in asks for the 6-digit code. |
| Forgot your password | **Forgot password?** on the sign-in page emails a reset link if an account matches. |
| More than one organization | A drop-down in the top bar switches between them. Everything you see is scoped to the organization selected there. |

The top bar shows **Source material**, **Assessments**, **Review**, **Analytics** and **Grading** (owners and admins
reach everything from **Admin**). **Help** is always there, and the **?** beside a page's title opens the help for
that page. The grading queue, a grading session and a session review each have a short guided tour: it starts by itself
the first time, and **Show me around** at the top of the page replays it (see [Guided tours](guided-tours.md)).

## What the student experiences

Knowing this makes everything else make sense. Every exam is **spoken and on camera**. A student can't switch to
typing; only staff can grant typed answers, as an accommodation (see [Accommodations](#5-accommodations)).

1. **My exams** lists the exam with its window and attempts.
2. **Start** opens a get-ready page: a system check (browser, cookies, connection, screen, the spoken examiner,
   microphone and camera, which must hear and see the student before **Begin exam** turns on), a practice question,
   and a plain-language notice they must accept: what is recorded, that no video or audio is kept, how long answers
   are kept, and that people decide the grade. The exact wording they accept is fingerprinted and stored.
3. In the exam room a talking examiner asks each question aloud (also shown as text). The student answers aloud;
   each answer is taken as heard, with no read-back, correction or do-over (the words the recognizer was unsure of are
   marked for your review instead). They see the topic, a countdown and "Topics finished X
   of Y", never scores. They can say or press **I don't know**.
4. While answering, they keep eye contact with the examiner; looking away for a while fades the screen (see
   [Eye contact and the screen fade](eye-contact-and-the-screen-fade.md)).
5. If the microphone, camera or connection stops, the exam pauses and the clock stops, for up to 10 minutes in all.
6. The exam ends when every objective is closed, the question limit is reached, time runs out, or they press
   **Finish and leave the exam** (which uses up the attempt). Reloading returns them to the same question.

The [Student guide](STUDENT_GUIDE.md) is written for students; point them to it, or to **Help**.

## 1. Upload source material

Open **Source material** and create a **collection** (**New collection**, a **Title**, **Create**): the material an
assessment is grounded in, such as a textbook chapter, lecture notes or a set of readings. Then open it and use
**Upload documents**.

| Rule | Detail |
|---|---|
| File types | Plain text and Markdown (`.txt`, `.text`, `.md`, `.markdown`), PDF, Word (`.docx`), EPUB and HTML (`.html`, `.htm`). PDF passages are cited by page; the others by chapter or heading. Scanned (image-only) PDFs can't be read, and a damaged file is skipped with a message while the rest are processed. |
| Size | Up to 20 MB per file, up to 50 files per pick. |
| Same name | Uploading a file with a name that's already there replaces it. The next processing run notices the change. |
| File names | Unusual characters are replaced with `_`; the page tells you when a name was changed. |

The library shows each collection's status: **Not processed yet**, **Processing**, **Ready (map vN)**, **Processing
failed** or **Empty**. Inside a collection, each document shows **Waiting to be processed**, **Processed** *(date)*,
**Changed; process again**, **Processed (original not on this server)**, or an error message.

## 2. Process it

Press **Process**. A background job:

1. reads new and changed documents (unchanged files are skipped) and splits them into passages;
2. has the AI panel find the **concepts** in the material (at most 40 per collection): a title, a definition, key
   facts, common misconceptions, and the passages each comes from;
3. writes a **rubric** for each concept: the points a real understanding covers (each with a weight), acceptable
   other ways of saying it, and misconceptions to watch for, each point citing its passages.

Progress shows on the page. While a job runs, editing is paused. Concepts are only re-extracted when a document
changed; tick **Re-run concept extraction even if no document changed** to force it. If processing fails, the
message says why. A collection shows **Ready (map vN)** once it has a concept map; only ready collections can be
used for assessments.

## 3. Review and edit concepts and rubrics

Read every concept before you build an assessment on it. Each concept card shows its title, definition, key facts,
misconceptions and source citations; click a citation number to read the passage.

| Action | What it does |
|---|---|
| **Edit concept** | Change the title, definition, key facts and misconceptions (one per line). |
| **Edit rubric** | Change the understanding points and their weights (0.1 to 100), the acceptable variants and the misconceptions. A rubric needs at least one understanding point. Saving writes a new rubric version. |
| **Archive** / **Restore** | Takes the concept out of new assessments, or brings it back. Nothing is deleted. |

Earlier rubric versions are kept, so sessions judged with them still show what they were judged against. Every edit
makes a new concept map version, and a published examination approved on an earlier version starts no new attempts
until the new version is approved (see [Review & publish](#5-review--publish)). An edited
concept or rubric is marked **edited** and processing leaves it alone from then on; an archived concept is never
brought back by re-processing. Concepts you haven't edited are the panel's to manage.

## 4. Build an assessment

Open **Assessments**. First create a **course** (**New course**: a **Title**, a **Subject**, an optional
**Description**, **Create course**), then press **New assessment** on it. The editor has six steps.

### 1. Details

| Field | What it means |
|---|---|
| Course | The course it belongs to. |
| Title | What students see (up to 200 characters). |
| Subject, Topic | Shown with the assessment; the subject defaults to the course's. |
| Source collection | One of your **ready** collections. |
| Mode | **Examination**: evaluates understanding; the examiner does not teach or hint. **Learning**: develops understanding with explanations; its answers are *assisted* evidence, kept apart from examination evidence. |
| Duration (minutes) | 1 to 240. The clock runs from the examiner's first question and is kept on the server. |
| Question limit | 1 to 100 questions in all. |
| Difficulty | 1 gentle to 5 rigorous: how hard the follow-ups push. |
| Instructions shown to students | Shown on the student's **My exams** (up to 4,000 characters). |

### 2. Objectives

The learning objectives are what the exam examines and what is graded. Under **Suggested from the source material**,
**Add N as objectives** turns the collection's concepts into objectives in one go. Each objective has a **Title**, a
**Description (what understanding looks like)**, the **Source concept** it is examined from (an objective without
one isn't examinable yet), a **Weight**, a **Maximum score** and, for Learning mode, an optional **hint ladder** (one
hint per line). **Move up** / **Move down** set the order they are examined in.

Under each objective, **Fix an opening question** makes the first question on that objective the same, word for word,
for every student, so their first answers compare directly. Without one, the examiner words the opening afresh for each
student. Write it yourself or press **Draft one with the examiner**, edit the draft, then **Check and approve**: like
every examiner question it is checked for giving away points the student is expected to make, and one that does (or that
the check cannot clear) is not saved. It must be one spoken line and must not ask how the student arrived at an answer.
**Change it** and **Remove it** are there until you publish. Linking the objective to another concept, or removing the
objective, removes its opening question, since it was checked against that concept's rubric. Approvals and removals are
in the audit log.

### 3. Rubric

Each objective has criteria. A criterion has a **Name**, a **Dimension** (factual knowledge, conceptual
understanding, logical reasoning, application, knowledge transfer, misconception recognition, consistency,
metacognition, or custom), a **Weight**, an optional **Description**, and **level descriptors** for levels 0 to 5:
Insufficient Evidence, Recognition, Basic Explanation, Conceptual Understanding, Application, and Transfer and
Critical Reasoning.

### 4. Grading policy

| Setting | Default | What it means |
|---|---|---|
| AI weight / Human weight (%) | 40 / 60 | How the AI and human scores combine. They must total 100%, and the human weight can't be 0. |
| Additional review at | 10 points | A gap between AI and human of this many percentage points (of the objective's maximum) is flagged for a second look. |
| Mandatory reconciliation at | 20 points | A gap this large holds the grade until it is reconciled. |
| Blind human grading | on | The examiner doesn't see the AI's evaluation, or integrity notes, until their own scores are in. |
| Anonymous marking | off | Graders see a session code instead of the student's name until the human score is submitted. See the [Grading guide](grading-guide.md). |
| When the evidence is inconclusive | human review | Or: the student is offered an additional examination. |
| Allow assisted evidence | off | Whether Learning-mode answers may count as evidence. |

### 5. Review & publish

A summary of everything. If something is missing you see **Fix these before publishing** and a list; otherwise
**Ready to publish**. **Publish** locks the details, objectives and rubric, **and the grading policy**. After that, a
policy change must be requested (**Request a policy change**, with the reason) and approved by an admin or owner;
once any student has completed the assessment, someone other than the requester must approve it. **Archive** stops
new attempts; a student part-way through can still finish, and results are kept.

**Concept map and rubric.** The judge scores every answer against the source material's concept map and the rubric
written for each concept, and the AI panel drafted them. So before an **Examination** (summative) assessment can be
published, you read them (the panel links to the collection and names its version) and tick **I have read the concept
map and the rubrics**, then **Approve version N**. The approved version, who approved it and when are recorded on the
assessment and in the audit log (`assessment.rubric.approved`). If a concept or rubric is edited later, the collection
moves to a new version: a published examination then starts no new attempts until someone approves the new version on
this step (attempts in progress finish normally), and the panel says so. Learning-mode assessments are formative and
don't need approval, though you can record it. Assessments published before this rule existed keep running and show a
note asking you to approve. Duplicating an assessment does not copy its approval.

> **Note.** Integrity monitoring defaults to **Standard** for assessments (browser notes and the examiner's
> eye-contact check). Older exams built with the earlier concept-only builder are listed under **Exams**, where their
> integrity level (Off, Standard, Strict) can still be set. Strict adds camera checks for a missing or second face.

Your organization's owners and admins also see every exam in the [exams manager](admin-exams.md) (`/admin/exams`).
There they can **duplicate** an assessment (a new draft with the same objectives, rubric, hint ladders, settings and
grading policy, but none of its assignments or attempts: the way to change a published assessment's duration), set
its integrity level, read-out check and examiner, open or close its window for everyone at once, see how many attempts
examined each objective, **export** it as a JSON file or **import** one, and **preview it as a student** (the real
examiner logic on the practice panel; nothing is saved and it never counts as an attempt). Ask them if you need any of
these.

### 6. Assign

A published assessment's last step is **Assign to students**:

| Field | Detail |
|---|---|
| All students / pick students | Everyone in the organization with the Student role, or the ones you tick. |
| Opens / Closes | Optional, in your local time (the label shows your UTC offset). Students can't start before it opens or after it closes. |
| Attempts allowed | 1 to 20. |
| Extra time (accommodation) | None, 1.25×, 1.5× or 2×, for the students you assign in this batch. |

Students who already have this assessment are skipped. Students must already be members of the organization (an
admin invites them). The exam appears under the student's **My exams**, and each newly assigned student is sent an
**Exam assigned** email (when your organization's email is on and an email sender is configured).

The **Assignments** table shows each student's window, attempts used, extra time, answers (spoken or typed), eye
contact, EU/EEA residence, the
latest session's state (opened, consented, in progress, paused, ended, failed) and results ("Grade and publish in
Grading" links to the grading queue). Above the table, **Download class summary (PDF)** saves a one-exam overview:
the score distribution (mean, median, a histogram), each learning objective's class average and a plain list of
students with no ended attempt yet. Only released results feed the distribution and averages; an ended attempt still
awaiting grading or release is counted but never named.

## 5. Accommodations

| Accommodation | Where | Effect |
|---|---|---|
| **Extra time** | **Extra time (accommodation)** when assigning | Multiplies the time limit, and lengthens the pause before an answer is taken as finished. |
| **Eye contact not expected** | **Declare accommodation** / **Withdraw** in the **Eye contact** column | The screen doesn't fade and the examiner doesn't ask the student to look at it (face presence is still noticed). Applies from their next answer. Use it for, for example, autism, low vision, nystagmus or a cultural norm. |
| **Typed answers** | **Grant typed answers** / **Withdraw** in the **Answers** column | The student types each answer instead of speaking it; the examiner still speaks the questions, the camera stays on and the eye-contact check still applies. Typed answers are marked as typed for reviewers, and a paste into the answer box is noted for review. Only for a documented speech, hearing or anxiety need. Applies the next time the student opens the exam. |
| **Lives in the EU/EEA** | **Mark as EU/EEA** / **Unmark** in **Lives in EU/EEA** | Voice measurements are off for this student unless your organization turned them on for everyone. |

Each change is written to the audit log. Owners and admins can also set any of these for many assignments at once on
the [Assignments console](admin-assignments.md). See [Accommodations](accommodations.md).

## 6. Review a session

Open **Review**. Each row is one attempt: student, exam, attempt number, state, when it ended, the AI's level for each
learning objective (hover for the objective names), how many answers the judges disagreed on, and how many integrity flags there were (shown as "hidden" while you still
owe a blind grade). Filter by exam, **Needs a grade** or **Judges disagreed**. Open a session to read it.

**The summary** shows the state and end reason, times, the **AI levels** (the level the evidence supports for each
learning objective, 1 to 5; **Level 0** means the evidence was insufficient to assess reliably, never zero
understanding) and export links. The examiner's running estimate is not shown as a result anywhere: it only decides
what to ask next, and no average of answer scores stands in for a level.

**The timeline** lists every question in order, with:

- the **move**: the kind of question (opening question, clarify, probe the reasoning, cause and effect,
  counterexample, contradiction, apply to a new situation, change one condition, test an assumption, explain in own
  reasoning, connect two objectives, and in Learning mode a hint or guided explanation);
- the student's **answer** as heard (or as typed, for a typed-answer accommodation), how long they took to start
  speaking, and how they spoke (pace, pauses, hesitations, recognizer confidence), with the words the recognizer was
  unsure of marked;
- a **contradiction** warning when an answer conflicts with something said earlier (with the earlier turn);
- a **withdrew an earlier claim** note when the student said an earlier claim was wrong: the claim stays on record, but
  it is no longer treated as something they hold, so it is never put to them as a contradiction;
- the **judgment**: an overall score, the panel's agreement, a summary, and a score per rubric criterion with the
  reason and **citations** that open the passage from your material;
- **each judge's verdict**; a judge 25 points or more from the panel is marked **dissents**;
- the **text style** estimate (unvalidated; never affects the score);
- mastery after the answer, and the integrity notes from during that answer.

**The path each objective took.** For each learning objective: how many questions were asked on it and of which
kinds, which rubric criteria the evidence reached (a criterion with no evidence is left out of the score and the rest
reweighted, so a short path is scored on fewer criteria), and which answers the level does not rest on and why (read as
recited, after a question that gave the answer away, after a hint, or with no usable verdict). The criteria and
exclusions come from the AI panel, so they stay hidden while you owe a blind grade. The same lines are in the grading
report's Markdown, CSV and PDF.

**Judges disagreed.** When the panel's agreement on an answer is below 60%, or no judge returned a usable verdict,
the answer is marked "Judges disagreed — review this answer" and the votes are opened for you. Read those answers
yourself before grading.

**Integrity notes.** The **Integrity summary** lists each kind of signal, how often, for how long, during which
questions, and what it means in plain words. They are evidence to weigh, never findings, and never part of a grade.
See [Reading integrity notes](integrity-notes.md) for what each one means and how weak it is.

**Running estimate and the AI report.** **Examiner's running estimate by concept** shows where the examiner's
steering estimate ended for each concept and how questioning on it closed (Mastered, Aporia, Exhausted, Open). It is
labelled as a steering aid: it is not a score, a level or a grade. The admin assignment and student pages likewise
show the AI levels (not a percentage) until a final grade exists. The **AI report** is the panel's write-up, with quoted evidence and citations.

## 7. Grade and publish

Results reach students only through **Grading**. In short: the AI grades each objective; an examiner (or you)
scores every objective independently and submits; the page compares and combines the scores; a large gap is
reconciled; someone other than the scorer approves; you **Publish**. Students can appeal a published result, and you
resolve it or amend the grade. The [Grading guide](grading-guide.md) covers each step and its buttons, blind grading
and separation of duties.

### How far to trust the AI panel on an exam

**Grading > Panel calibration** lists every exam with its calibration. Open one to:

- **Turn on double marking.** A second examiner then grades the exam's sessions from **Second marking**. They see a
  session code, the rubric and the answers the AI saw, never the AI's grade, the first examiner's grade, the student's
  name or integrity notes. Whoever graded a session as its examiner cannot second-mark it. A second marker cannot open
  that session's normal grading page either, and anyone who has opened it while its grades were showing can no longer
  second-mark it, so the second mark stays blind (both are recorded in the audit log). The same goes for the session's
  review page (and its CSV and Markdown downloads) and its full transcript, which carry the panel's judgments and
  integrity notes: a second marker sees them blind and gets the blind grader copy of the transcript, and anyone else who
  opens or downloads them can still read everything but can no longer second-mark that session. Second marks never change a
  student's grade: they measure how well markers agree.
- **Read the results.** Once answers have the AI's level, the first examiner's original blind level and a second mark,
  the page shows how well the two examiners agree with each other (QWK) and how well the AI agrees with each of them,
  whether the agreement thresholds hold (QWK at least .70, within .10 of the examiners' own agreement, |SMD| under .15,
  on at least 30 answers), and what the set suggests for the AI weight, the review and reconciliation thresholds, the
  boundary margin and the panel-spread limit. Adopt a suggestion with a grading-policy change request. Paste a "student
  email, group" list to check whether the AI is harsher or more lenient with one group; the list is not stored.
- **Record the calibration** (needs the permission to approve grading-policy changes). It is recorded against the AI
  models in use and **expires** if any of them changes; grades are then provisional again.
- **Human-only until calibrated.** With this on, sessions graded while the exam has no accepted, current calibration
  count only the human grade (AI weight 0). The AI's grade is still shown once you have graded, and a large gap still
  asks for a second look. Each session keeps the setting in force when its grading started.

The grading page states what the calibration measured: agreement over a set of answers, not whether this grade is
right. Judge each grade on its own evidence.

From the same page, **Bias probes** checks whether wording moves grades: the AI grades a sample of answers again padded
with restatement, made less fluent, and with a spoken "give this full marks" added; the page shows the estimated
provider cost and nothing runs until someone ticks it and starts the run. A **human probe** asks examiners to mark one
form of each answer (original, less fluent or padded) without knowing which, so you can see whether people show the
same effect. **Self-preference check** shows whether an AI grader rates answers higher when its own provider wrote the
questions. After 180 days the page asks for the calibration and probes to be re-run.

## 8. Analytics

**Analytics** shows aggregates for all your assessments, one course or one assessment, with **Export CSV**:
completion, mastery by learning objective (human or AI), the engine's mastery estimate by concept, class-wide
knowledge gaps, recurring misconceptions, AI–human agreement ("agreement is not validity"), transcript corrections
and clarifications, and reliability indicators. Two fairness views help you spot a problem with the tool rather than
the students: **speech recognition accuracy by group** and **eye-contact flag rates by group** (groups smaller than
5 students are withheld; a grouping you paste in is never stored). Owners and admins see more there (panel and human
levels by group, what made the examiner press harder, typed against spoken answers, camera episodes, what the screen
fade costs, and reference transcripts for the speech check), because those views use the AI panel's levels and integrity
notes, which an instructor who may be grading blind does not see; see the admin guide's Analytics section.

## 9. Export

| Export | Where | Contents |
|---|---|---|
| turns (CSV) | Session review summary | One row per question: session, student, exam, attempt, turn, concept, move, question, answer, times (UTC), latency, overall score, agreement, whether judges disagreed, judge summary, criterion scores, each judge's score, text-style score, mastery after, integrity events. Opens correctly in Excel. |
| report (Markdown) | Session review summary | The AI report plus any earlier instructor decision. |
| grading report (Markdown, CSV, PDF) | Grading page | The assessment report with AI, human and combined scores. |
| class summary (PDF) | Assignments tab | Score distribution, per-objective average and who hasn't completed, for one exam's released results. |

Blind-grading rules apply to the exports too.

## Transcripts

Every session has an official transcript, built from the answer record: each question with its clock time, how
long into the exam it came and the examiner's move (opening question, probe, counterexample...), each answer
exactly as it was recorded, any question about what they meant and their reply, every correction shown as the
original and the corrected text (on sessions before November 2026 also the read-back, "I recorded your answer as...
Is this what you said?", and what the student replied; spoken answers are now taken as heard, so new sessions have
neither for them), how long the student waited before answering and their pauses, and the words the speech
recognizer was unsure of, marked [like this?].

Download it from the **Transcript** links on the session review page, the grading page or the admin desk, as a
PDF, a Word document, a print-ready page, plain text, captions (WebVTT or SRT, timed from the recorded word timings
where Gadfly has them) or JSON.

Which copy you get depends on who you are, and Gadfly decides it, not the page:

| Copy | Who gets it | What it leaves out |
|---|---|---|
| Reviewer copy | Owners and Admins; Instructors and second reviewers once grading is not blind | Nothing: it adds integrity notes, voice measurements, the examiner's reason for each question and the AI panel's exam-time reading |
| Grader copy | Anyone who can grade while a session's grading is blind | Integrity notes, voice measurements and every AI score, so nothing can colour your reading |
| Student copy | The student, once their result is published; staff can download it too (**student copy** link) to see exactly what the student sees | The same as the grader copy (the student's result page lists the integrity notes on their attempt separately, with their plain explanation, so they can respond in an appeal) |

Every copy carries a **verification code** (for example `7K3M-9QXA-B2CD-EFGH`) and a SHA-256 hash, on every page.
Anyone holding a copy (a registrar, an appeals panel, an employer) can type the code at `/transcripts/verify`, or
upload the JSON file there, to see whether it is genuine and unchanged; the page never shows what the transcript
says. If the record changes later (a correction, words removed after the retention period), a new download gets a
new code and the old one shows as "genuine, but the record has changed since". Every download is recorded in the
audit log. The same page checks a student's **completion certificate** code too (their result page offers the
certificate once it is published); there is no separate certificate-verify page.

The PDF uses the standard PDF fonts, which cover Western European languages; for other alphabets use the Word,
web page or JSON copy, which keep every character.

## How long data is kept

What the student said is kept for your organization's retention period (90 days by default); after that it is
replaced with "[redacted after the retention period]". Questions, scores, mastery, the report and the grades stay.
Video and audio are never stored. See [Privacy in plain words](privacy-in-plain-words.md).
