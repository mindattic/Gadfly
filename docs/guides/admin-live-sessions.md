# Live sessions monitor

**Live sessions** (`/admin/live`, in the admin navigation) shows every exam in your organization that is open right now,
updated as it happens, with what staff can do to help. Only the organization's owners and admins open it.

An exam appears the moment a student opens it and leaves when it ends. The page updates on its own as each exam takes a
step (and re-reads everything every few seconds as well); the clocks count down every second. **Refresh** reads it again
straight away.

Only what the exam reports is shown: no audio or video is ever shown here or stored.

## Each exam

| Shown | What it means |
|---|---|
| Student, exam, attempt | Who is sitting what (the attempt number from the second attempt on) |
| Time left | The student's remaining time. It is highlighted in the last two minutes, and is shown as still while the clock is stopped. *Clock not started* means the examiner has not asked the first question yet |
| Status | **Reading the exam notice**, **Getting ready** (checking the microphone and camera, before the clock starts), **In progress**, **Paused by staff**, or **Paused: microphone, camera or examiner lost** |
| Question | The question number, what kind of question the examiner is asking (opening a topic, asking why, offering a counterexample...) and the topic |
| Objectives covered | How many of the exam's learning objectives the examiner has finished with, out of all of them |
| Connection | **Exam room open** (the student has the exam page open), **Active *n* minutes ago** (a step in the last three minutes), or **Nothing heard for *n* minutes** |
| Microphone and camera | *Being set up*, *working*, or **lost** (the clock is stopped until they come back) |
| Integrity notes | How many, and the three most recent. These are low-confidence notes for the reviewer, never a verdict and never part of a grade |

**Review so far** opens the session's review with everything recorded up to now.

## Stepping in

Every action except a notice needs a short reason. The reason goes into the audit log and onto the session's review
timeline, so the reviewer and the grader can see what happened and why. The student is told on their screen what you did.

| Action | What it does |
|---|---|
| Add time | Adds 1 to 60 minutes at a time, up to the most extra time an exam may be given in all; the page says if that limit cut the amount down |
| Pause | Stops the student's clock. The microphone closes, no answer is sent while paused, and anything they had already said is kept as their draft. The student sees that their instructor paused the exam |
| Resume | Restarts an exam staff paused, with the paused time added back. The microphone reopens for the same answer |
| End exam | Ends the exam now (you confirm first). The answers so far are submitted, and the attempt is graded like any other: it goes to the grading queue |
| Send a notice | Shows the student a short written message (up to 300 characters) in the exam room. It is never spoken. If their exam room is not open on this server right now, they see it if they come back within a few minutes |

Add time, Pause, Resume and End are offered once the exam's clock has started (when the examiner asks the first
question). A notice can be sent at any point.

An exam that a student abandons and that sits well past its time limit is closed automatically and graded as usual; its
review shows *Closed automatically*.

## In the audit log

`session.staff.extended`, `session.staff.paused`, `session.staff.resumed`, `session.staff.ended`,
`session.staff.notice` and, for the automatic close, `session.staff.auto-closed`. Each carries the session's id, so
**all its events** in the [audit log](admin-audit-log.md) finds them.
