# Grading guide

For everyone who grades: examiners, second reviewers, instructors, admins and owners. It follows one attempt from the
end of the exam to a published result, then appeals.

## Who can do what

| Role | Score (human evaluation) | Reconcile | Approve | Publish, amend, resolve appeals |
|---|---|---|---|---|
| Examiner | ✓ | | | |
| Second reviewer | | ✓ | | |
| Instructor | ✓ | ✓ | ✓ | ✓ |
| Admin, Owner | | ✓ | ✓ | ✓ |

**Separation of duties.** Whoever entered a grade's human score cannot also reconcile or approve it; someone else
must. The page tells you so and the button is off for you (you can still request a second review). An organization
where one person does all the grading can turn on **Allow a single grader** in Organization settings; every
reconciliation or approval it lets through is recorded in the audit log.

**A separate approver.** Whoever reconciled a grade's disagreement (or was named as the second reviewer on it)
cannot also approve that grade while another instructor, admin or owner who neither scored nor reconciled it could.
In a two-person team, or with **Allow a single grader** on, they may, and the audit log records it. An admin can make
this stricter or turn it off under **Separate reconciler and approver** in Organization settings.

Examiners and second reviewers see the organization's whole grading queue. They don't have the **Review** pages;
everything they need is on the grading page.

## The grading queue

Open **Grading**. The first time, a short guided tour points out the queue and its filters; **Show me around** replays
it, and the grading page of one attempt has its own (see [Guided tours](guided-tours.md)). Each row is an ended
attempt with its grade status. **Start grading** opens it; **Retry AI grading**
appears if the AI step failed. AI grading normally starts by itself shortly after an exam ends.

The statuses, in order: **Examination Completed**, **Transcript Verification Pending** (the transcript still waits
on the student), **AI Grading Pending**, **Human Review Pending**, **Reconciliation Required**, **Awaiting Final
Approval**, **Finalized**, **Published**, and later **Under Appeal** or **Amended**.

## 1. Your evaluation

While the grade is **Human Review Pending**, the grading page shows the full transcript (the student's original and
answers as heard, with the words the speech recognizer was unsure of marked, any clarifications, and on sessions
before November 2026 the student's confirmations and corrections) and, for each learning objective, a form:

- **Level** (0 to 5) and **Score** (0 to the objective's maximum);
- **Comment** (the student sees it in their report);
- **Supporting evidence** (response numbers or quotes).

Fill in **every** objective, then press **Submit evaluation for all objectives**.

> **Important.** **Blind grading** (the default) hides the AI's preliminary evaluation, the judge's per-answer
> evidence, and **all integrity notes** (eye contact, browser notes, the read-out check, the text-style estimate,
> voice measurements) until you submit. Grade the answers on their own merits first. Once you submit, everything
> appears. Only your first submission counts as blind.

**The path each objective took.** No two students get the same follow-up questions, so under each objective's
heading the page shows its **Path**: how many questions were asked and of which kinds (opening question, probe the
reasoning, counterexample, apply to a new situation, and so on). Once the AI's evaluation is visible to you it also
shows **Coverage**, which rubric criteria the evidence reached and what share of the objective's weight they carry
(a criterion with no evidence is left out of the score and the others reweighted, so a short path is scored on fewer
criteria), and **Exclusions**, every answer the level does not rest on and why: read as recited, came after a
question that gave the answer away, given after a hint, or left without a usable verdict for a person to grade. The
same lines appear on the review page and, for reviewers, in the assessment report. Students don't see them.

**Anonymous marking.** When the assessment's grading policy has **Anonymous marking** on, the grading queue, this
page, the assessment report, the review pages and a grader's transcript download show a session code (for example
*Session 5A7D-9C2E*) instead of the student's name, and a banner says so. The name appears once the human score is submitted, in time for
reconciliation, approval and publishing. It hides the name Gadfly shows; if a student says their name in an answer,
the transcript still has it. Owners and admins, who don't enter human scores, still see names on the review pages.

## 2. Comparison

After you submit, the page shows the AI and human scores side by side, and the combined score using the assessment's
locked policy (by default 40% AI, 60% human). If the AI found no usable evidence for an objective (level 0), its score
is set aside and yours stands alone for it.

A gap of 10 points or more (of the objective's maximum, by default) is flagged for **additional review**. A gap of
20 or more, a panel split by more than the allowed number of levels, or a score sitting on a grade boundary, holds the
grade at **Reconciliation Required**.

## 3. Reconciliation

The banner reads **Pending Grade Reconciliation**. Someone who did not enter the human score records:

- **Cause**: an error in the AI evaluation, an error in the human evaluation, transcript or meaning ambiguity, a
  different reading of the rubric, or both evaluations are defensible;
- optionally a **Resolved AI score** and **Resolved human score**;
- the **Second reviewer** and the **Resolution** in words.

**Record resolution and recalculate** applies it. **Request a second review instead** hands it to a second reviewer.

## 4. Final approval

At **Awaiting Final Approval**, an instructor, admin or owner (never the scorer, and not the reconciler while someone
else can approve) adds an optional **Grade label** and
an **Approval note** and presses **Approve final grade**. Or, under **Adjust the human grades instead**, give a
**Reason** and **Reopen human grading**.

## 5. Publish

When the grade is **Finalized**, **Publish** releases it, with the assessment report, to the student on **My exams**.
Nothing is shown to the student before this, not even the AI's view. After an amendment, **Republish**.

## 6. Appeals and amendments

A student can appeal a published result with a written reason; the grade shows **Under Appeal** and stays visible to
the student. On the grading page:

- **Grade stands (appeal upheld as reviewed)** or **Dismiss** closes the appeal without change;
- **Amend the grade**, with a reason, changes it (**Amend**, or **Amend and resolve the appeal**). The student's
  result is marked "amended". Republish it.

## Exams taken in another language

A student may take an exam in Korean (GAD-T526). The grading page, the review page and the second-mark sheet show the
student's own words, which are the record, and offer **Show a machine translation into English**. Each translation sits under
its original, labelled as machine translation; it is never saved with the record or put in a download. Grade the original
against the English rubric, as the AI panel does: understanding counts, not accent, grammar or fluency. If a translation decides
a grade, ask someone who reads the language. You are not shown the student's primary language: it is not evidence (GAD-T548).
More in [Exams in English and Korean](exams-in-other-languages.md).

## Attempts marked Needs instructor review

An attempt is marked **Needs instructor review** when the student left the exam window three or more times or once for a
minute or more (unless a window-absence accommodation changes that), or when it was taken in Korean and the AI panel is not
yet calibrated on Korean answers. The review list and the grading queue show the badge with its reason and can be filtered to
marked attempts; the review and grading pages show a panel with the reason, each absence (time, length, question) and the
student's explanation. Under blind grading the absences and the explanation stay hidden until your own score is in. The
result is held, and **Publish** is refused, until someone who approves final grades clears the mark with a note or approves
the grade with a note; both are audited. Nothing was cancelled and the mark is never a finding on its own. See
[Attempts marked for instructor review](attempts-marked-for-review.md).

## Second marking

On an exam flagged for double marking, a second examiner grades its sessions from **Second marking** (linked from
**Grading > Panel calibration**). The sheet shows a session code, the rubric and the answers, never the AI's grade, the
first examiner's grade, the student's name or integrity notes. Whoever graded a session as its examiner can't
second-mark it; a second marker is refused that session's normal grading page, sees its review page blind and gets the
blind grader copy of its transcript, and anyone who has seen its grades on the grading page, the review page (or its
downloads) or the full transcript can no longer second-mark it. Second marks never change a grade: they measure how well markers agree, for the panel's
calibration. The [Instructor guide](INSTRUCTOR_GUIDE.md#how-far-to-trust-the-ai-panel-on-an-exam) covers calibration,
bias probes and the self-preference check.

## The audit trail

Every step is recorded with who and when. **Audit trail** at the bottom of the grading page lists the session's
events. Refused attempts (for example someone approving their own score) are recorded too. The AI never publishes
anything.

## Downloads

The grading page offers the assessment report as Markdown, as a PDF, and the scores as CSV, and the official
transcript in several formats. The report's Markdown and PDF both carry each objective's **Path**, **Coverage** and
**Exclusions** lines for reviewers (the student's copy never does). While your grading is blind you get the grader copy, which leaves out integrity notes, voice
measurements and every AI score. See [Transcripts](INSTRUCTOR_GUIDE.md#transcripts) in the Instructor guide.

**The path in the CSV files.** Both reviewer CSV files carry the same path each objective took as the grading and
review pages show:

- The **scores CSV** (one row per objective) ends with `path_questions` and `path_follow_ups` (how many questions
  and follow-ups the objective got), `path_moves` (each kind of question and how many times it was asked, for
  example `opening question=1 | probe the reasoning=2`), `criteria_reached` and `criteria_not_reached` (rubric
  criteria the evidence reached, and those left at Level 0 and out of the score), `coverage_percent` (the share of
  the objective's weight the reached criteria carry) and `exclusions` (every answer the level does not rest on, as
  `turn 4: reason`, or `none` when every answer counted).
- The **session CSV** from the review page (one row per turn) ends with `objective_path` (the questions summary
  for that turn's objective), `objective_coverage` (the criteria reached and the weight covered) and
  `turn_excluded` (`no`, or `yes:` and why this answer was not counted).

While your grading is blind only the questions are filled in: the criteria, coverage and exclusion cells stay empty
until you submit your own scores, just as on the pages. An empty exclusion cell therefore means "not shown yet",
never "nothing excluded". Students never get these columns.
