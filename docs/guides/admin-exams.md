# Exams manager

**Exams** (`/admin/exams`, in the admin navigation under *Exams*) lists every exam in your organization in one place,
with what you need to run them. Owners and admins open it.

## The list

Each row shows the exam's title (it opens the assessment editor), its course and source material, and:

| Column | What it means |
|---|---|
| Status | **Draft** (still being built), **Published** (every student can sit it), **Archived** (closed for good, kept for the record) |
| Objectives | How many learning objectives it examines |
| With terms | How many students have terms set for it (see [Student terms](admin-assignments.md)) |
| Completed | Attempts that reached a proper end (hover to see how many started) |
| Average time | The average length of completed attempts |
| Window | Whether students can start it now: *Open*, *Closed*, *Opens 3 Dec 09:00 UTC*, or *Open to 12 of 30* when students' terms differ |

**Find** searches titles, courses and source material. **Status** shows active exams (drafts and published) by default;
choose *Archived* or *All* to see archived ones.

## Actions

Open **Actions** on a row:

- **Settings**: see below.
- **Objective coverage**: of the attempts that started, how many examined each objective, and how many of those gave
  *independent* evidence (not after a hint, and not after a question that gave the answer away). An objective few
  attempts reach usually needs more time, fewer objectives, or a higher question limit. An exam built without
  objectives is covered by concept instead.
- **Window…**: set when the exam opens for every student and closes and closes (UTC; leave a box empty for no limit at that end).
- **Open now**: students can start straight away (an opening date still to come becomes now, and a
  closing date already past is cleared). Only for a published exam.
- **Close now**: no new attempt can start. An attempt already in progress can still be finished.
- **Preview as a student**: see below.
- **Duplicate**: a new **draft** copy with everything that describes the exam: details, settings, learning objectives,
  rubric criteria with their level descriptions, hint ladders and grading policy. Its student terms, attempts, grades and
  results stay with the original. Use it to make next term's version, or to change settings a published exam has frozen.
- **Export as JSON**: downloads the exam's definition (`.gadfly-exam.json`) to keep, share or import elsewhere.
- **History**: the exam's events in the [audit log](admin-audit-log.md).
- **Archive** / **Restore**: archiving asks first. An archived exam can't be started by students or opened to new attempts;
  its attempts and results are kept, and an attempt in progress can be finished. **Restore** puts it back as it was:
  published if it had been published, otherwise a draft.

## Settings

| Setting | Draft | Published or archived |
|---|---|---|
| Duration (minutes) | Editable | Frozen |
| Minimum objectives for a summative result | Editable | Frozen |
| Integrity level | Editable | Editable |
| Read-out check | Editable | Editable |
| Spoken language (what the student is expected to speak; also the recognizer's vocabulary-hint language), or let the recognizer detect it | Editable | Editable |
| Examiner voice id and avatar id | Editable | Editable. The voice id (an ElevenLabs voice id) is used for exam rooms opened from then on, else the organization's default, else the platform voice; the avatar id is saved but not used yet (every exam shows the platform's examiner) |

The duration and the minimum objectives shape what is measured, so once students sit an exam under them they stay as
they were; duplicate the exam to change them. Changes to a published exam apply to attempts that start from then on;
attempts already started keep their settings. Once any attempt has started, Gadfly asks you to say why, and the reason
goes into the audit log with the before and after values.

- **Minimum objectives for a summative result**: an attempt that examined fewer objectives is reported as formative
  (practice), not as a grade.
- **Integrity level**: *Off* records no integrity notes. Integrity notes are low-confidence notes for a reviewer, never a
  verdict and never part of a grade.
- **Read-out check**: notes a long, fluent answer after a long silent wait for a reviewer to look at. It never runs for
  a student with extra time.

## Preview as a student

**Preview as a student** runs the exam's real examiner logic (its objectives, rubric, source material and settings) so
you can see the order and kind of questions a student would get, and why each was asked.

- It always uses the practice AI panel, whatever this deployment normally uses: the questions follow the real plan,
  but their wording and the judgements are simple stand-ins, and nothing is sent to an AI provider.
- **Nothing is saved.** A preview is never an attempt: it doesn't use up anyone's attempts and never appears in grading,
  review, analytics, the dashboard, reports or exports. Closing the page throws it away. The audit log notes that you
  previewed the exam.
- Students answer out loud, on camera, to the talking examiner. In the preview you type what a student might say, or
  choose *"I don't know"*.

A draft can be previewed before publishing, as long as its source material has been processed.

## Import an exam

**Import from a file** (top of the page) takes a `.gadfly-exam.json` file exported from Gadfly, from your organization
or another one.

1. Choose the file.
2. Choose the **source material** the exam should examine (the file doesn't carry source material).
3. Optionally choose the **course**; otherwise Gadfly uses the course named in the file if your organization has one.
4. **Import**.

The exam arrives as a **draft**: nothing is published. If anything in the file is wrong, nothing is created
and every problem is listed, each saying where it is (for example *Objective 2 (obj-2), criterion 1: give it a name.*).
Objectives whose concept your chosen source material doesn't have arrive unlinked, with a note; link them in the
assessment editor before publishing.

## The file format

The file is JSON with `"format": "gadfly.exam"` and `"version": 1`. Under `"exam"` it holds the title, subject, topic,
instructions, assessment mode, integrity level, examiner voice and avatar, `settings` (duration, question limit,
difficulty, thresholds, minimum objectives, read-out check, concept keys), `gradingPolicy` (weights as fractions,
review and reconciliation thresholds, blind grading, what happens when evidence is inconclusive) and `objectives`
(key, title, description, concept key, weight, maximum score, hint ladder, fixed opening question, and rubric criteria
each with six level descriptions, 0 to 5). An import does not approve fixed opening questions: approve each again on the
Objectives step, where it is checked against the new source material. It never contains students, attempts, answers or grades. A newer Gadfly reads older versions; an
older one refuses a newer file with a message saying so.

All of these changes are recorded in the audit log: `assessment.duplicated`, `assessment.archived`,
`assessment.restored`, `assessment.window.opened` / `.closed` / `.set`, `assessment.settings.updated`,
`assessment.exported`, `assessment.imported` and `assessment.previewed`.
