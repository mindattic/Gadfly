# Assignments console

**Assignments** (`/admin/assignments`, in the admin navigation) shows who sits which exam: each student's window, attempts,
status, score and accommodations, and lets you change many at once. Only the organization's owners and admins open it.
Every change is recorded in the [audit log](admin-audit-log.md), and the changes that take something away or give an
extra chance ask for a reason first.

Dates on this page are shown and entered in your own time zone (the page shows which); they are stored in UTC.

## Two views

- **List**: one row per assignment, with filters, sorting and paging (50 to a page by default, up to 200).
- **Grid: students by exam**: students down the side, published exams across the top (the first 40; choose an exam to
  narrow it), each cell showing that student's status for that exam, attempts used of those allowed and the due date.
  Tick an empty cell (*not assigned*) to pick that student for that exam, then use **Assign an exam**. Tick an assigned
  cell to select it for the changes below.

## Statuses

| Status | Meaning |
|---|---|
| Not started | No attempt yet |
| In progress | An attempt is open right now |
| Interrupted | Attempts were made, but none reached a proper end (for example the exam stopped with an error) |
| Awaiting grading | An attempt ended and its grade is not final yet |
| Graded, not released | The grade is final, but the results have not been released to the student |
| Released | The student can see their result |

## Finding assignments (list view)

Filter by **Student** (part of a name, user name or email), **Exam**, **Status**, **Accommodation** (any, extra time, no
eye contact expected, typed answers) and **Due from** / **Due before**. Sort by student, exam, due date or date assigned.
The filters are part of the page's address, so Back and saved links work, and a student's profile links here already
filtered to them.

Each row shows the student (dimmed and marked if deactivated), the exam (marked if archived), the status, the due date
and opening time, attempts used of those allowed, the score and the accommodations.

**Export CSV** downloads every assignment matching the filters, with dates in UTC. The export is recorded in the audit log.

## Assigning an exam

**Assign an exam** opens a form:

1. Choose a **published** exam.
2. Choose who sits it: the students you ticked on the grid, a list of user names or emails (one per line or separated by
   commas; names that match no active student are listed back to you), or every active student.
3. Optionally set when it **opens** and when it is **due**.
4. Set the **attempts allowed** (1 to 20) and any **extra time** (none, 1.25x, 1.5x or 2x).

Students who already have the exam are skipped. The eye-contact and typed-answer accommodations are set afterwards, per
student, from the list.

## Changing many at once

Select rows with their tick boxes (or the box in the header for the whole page), then choose:

| Action | What it does | Reason needed |
|---|---|---|
| Set dates | Changes the opening time, the due date, or both. An empty date removes it. Rows where the window would close before it opens are left alone and listed | No |
| Move due date | Moves the due date by a number of days, earlier with a negative number (up to a year either way). Rows with no due date are skipped | No |
| Grant another attempt | Adds one attempt, up to 20 in all | Yes |
| Reset attempt | Clears the scores of the attempts so far, keeping an archived copy of every grade, report, reconciliation, approval and appeal in the audit log, and grants one more attempt so the student can sit the exam again. The attempts and their transcripts stay and still count as used, and the student is told. An attempt in progress can't be reset until it ends | Yes |
| Accommodations | Sets extra time, turns *no eye contact expected* on or off, and turns *typed answers* on or off | No |
| Unassign | Removes assignments that have no attempt yet. One with an attempt is kept, because its transcript and grades belong to it: reset the attempt instead, or leave it | Yes |

Each result says how many rows changed and lists any that were left alone, and why.

## Accommodations

Accommodations are granted per student and per exam, by staff only: a student can never switch them on.

- **Extra time** applies from the student's next attempt.
- **No eye contact expected** stops the screen fade for that student, from their next answer. See
  [Eye contact and gentle reminders](eye-contact-and-the-screen-fade.md).
- **Typed answers** lets that one student type their answers on that one exam; the camera, the spoken questions and the
  eye-contact check stay. It applies the next time they open the exam room.

See [Accommodations](accommodations.md) for what each one means for the student.

## In the audit log

`assessment.assigned`, `admin.assignment-dates-set`, `admin.assignment-due-moved`, `admin.attempt-granted`,
`admin.attempt-reset` (beside the archived copies of the cleared grades), `assessment.accommodation.time`,
`assessment.accommodation.gaze`, `assessment.accommodation.typed-answers.granted` / `.withdrawn`,
`admin.assignment-removed` and `admin.assignments-exported`.
