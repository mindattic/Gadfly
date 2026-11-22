# Admin dashboard

**Dashboard** (`/admin`, the first item in the admin navigation) is the admin home page: what is waiting on you, how the
last 7 and 30 days went, and shortcuts to the jobs you do most. Only the organization's owners and admins open it. It only
reads: nothing on it changes anything, so it is never recorded in the audit log. Press **Refresh** to read it again; the
time it was last read is shown beside it, in UTC.

## Getting started

A new organization sees a **Getting started** checklist at the top until every step is done. Each step ticks itself off
once it has happened, and each open step links to the page where you do it:

1. **Create an exam**: paste or upload the notes the examiner should draw on, then publish it.
2. **Add students**: give each a user name and password, or invite them by email.
3. **Assign the exam**: choose who sits it, how many attempts they get and when it closes.
4. **Watch a practice interview**: add yourself a test student, sign in as them and take the practice interview from
   **My exams**, then the exam you assigned. The step is done once any interview in your organization has ended.

## Needs attention

Each group shows how many there are, the first five (oldest first), and a link to the page with the whole list. A group
with nothing in it is left out; when nothing at all is waiting, the page says so.

| Group | What is counted | Opens |
|---|---|---|
| Interviews in progress | Exams that have started and not ended, including paused ones | [Live sessions](admin-live-sessions.md) |
| Attempts waiting to be graded | Ended attempts not graded by a person yet (still waiting for the transcript check, the AI preliminary grade or the human review) | The grading queue, or the attempt's grading page |
| Grades to reconcile | Attempts where the AI panel and the grader disagree enough that reconciliation is required, with how many objectives | The attempt's grading page |
| Grades waiting for final approval | Grades a person has finished, waiting for the final approver | The attempt's grading page |
| Open appeals | Appeals students have made that nobody has answered yet | The attempt's grading page |
| Not started, closing within 3 days | Assignments of a published exam with a due date in the next 3 days that the student has not started | The student's profile, or [Assignments](admin-assignments.md) |
| Background work that failed (last 7 days) | Your organization's background jobs (reading source notes, AI preliminary grading, webhook delivery, retention clean-up) that failed, with the error | [System status](admin-system-status.md) |

## The last 7 and 30 days

| Number | What it means |
|---|---|
| Exams taken | Interviews that ended in the period |
| Completion rate | Of the interviews started in the period and no longer in progress, how many reached a proper end (not abandoned, not stopped by an error). Hover to see the counts |
| Median interview length | From start to end, over the interviews that ended in the period |
| Grading turnaround | The median time from the end of the interview to the grade's final approval, over grades approved in the period. Hover to see how many |

Underneath: how many exams are published out of all your exams, how many active students you have and how many
assignments there are, with links to **Analytics** and **Usage** for more detail. A dash means there was nothing to
count.

## Quick actions

Shortcuts to **Create an exam** ([Exams](admin-exams.md)), **Add students** ([Students](admin-students.md)), **Assign an
exam** ([Assignments](admin-assignments.md)), **Watch live** ([Live sessions](admin-live-sessions.md)), **Grade** (the
grading queue) and **Invite staff** (Members).

## If the dashboard does not load

The page says so and offers **Try again**. If it keeps happening, look at [System status](admin-system-status.md).
