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

Open **Grading**. Each row is an ended attempt with its grade status. **Start grading** opens it; **Retry AI grading**
appears if the AI step failed. AI grading normally starts by itself shortly after an exam ends.

The statuses, in order: **Examination Completed**, **Transcript Verification Pending** (the transcript still waits
on the student), **AI Grading Pending**, **Human Review Pending**, **Reconciliation Required**, **Awaiting Final
Approval**, **Finalized**, **Published**, and later **Under Appeal** or **Amended**.

## 1. Your evaluation

While the grade is **Human Review Pending**, the grading page shows the full transcript (the student's original and
confirmed answers, clarifications and corrections) and, for each learning objective, a form:

- **Level** (0 to 5) and **Score** (0 to the objective's maximum);
- **Comment** (the student sees it in their report);
- **Supporting evidence** (response numbers or quotes).

Fill in **every** objective, then press **Submit evaluation for all objectives**.

> **Important.** **Blind grading** (the default) hides the AI's preliminary evaluation, the judge's per-answer
> evidence, and **all integrity notes** (eye contact, browser notes, the read-out check, the text-style estimate,
> voice measurements) until you submit. Grade the answers on their own merits first. Once you submit, everything
> appears. Only your first submission counts as blind.

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

## The audit trail

Every step is recorded with who and when. **Audit trail** at the bottom of the grading page lists the session's
events. Refused attempts (for example someone approving their own score) are recorded too. The AI never publishes
anything.

## Downloads

The grading page offers the assessment report as Markdown and the scores as CSV, and the official transcript in
several formats. While your grading is blind you get the grader copy, which leaves out integrity notes, voice
measurements and every AI score. See [Transcripts](INSTRUCTOR_GUIDE.md#transcripts) in the Instructor guide.
