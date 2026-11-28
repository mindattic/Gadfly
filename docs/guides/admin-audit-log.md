# Audit log

**Audit log** (`/admin/audit`, in the admin navigation under *Organization*) shows every change recorded in your
organization: who did it, when, what it changed and why. Only the organization's owners and admins can open it;
anyone else who tries is refused, and the attempt is recorded.

Rows are never edited or deleted. Gadfly records settings, members and invitations, the admin desk, exams and
assessments (including the exams manager), grading policies and their approvals, accommodations, every grading step
(including refusals and waivers), transcript corrections, exports, downloads and refused attempts.

## Finding an event

The newest events come first, 50 to a page (**Newer**, **Older**, **Newest**). Filter by any mix of:

| Filter | Finds |
|---|---|
| Search | Words in the action, the record, the reason or the before/after detail, or part of a person's user name |
| Who | Events done by one member of staff |
| Action | One action (`assessment.published`), or a whole family (`grade.*`) |
| Student | Events by the student, about them, or in one of their attempts or terms |
| Exam | Events about the exam, its objectives, its student terms and its attempts |
| Session id | One attempt's events (paste its id; **all its events** in an event's details fills it in) |
| From / To | A range of days, in UTC, both ends included |

**Apply** runs the filters; **Clear** removes them. **History** on an exam in the [exams manager](admin-exams.md) opens
the log already filtered to that exam (the address `admin/audit?exam=…`, `?student=…` or `?session=…` does the same
for any exam, student or attempt id).

## Details

**Details** opens an event: when, who and in what role, the action, the record it changed, the attempt it belongs to
(with a link to review it), the reason given, and the **Before** and **After** values in full, laid out for reading.

## Redacted events

Gadfly's privacy jobs remove a student's words even from the audit log: answers and clarifications when the
organization's retention period ends, and everything an event carried about a student whose data is erased at their
request. The event itself stays, marked **Redacted**: you can still see who did what, and when, but not the removed
detail. Events are never deleted, whatever your retention period, so the log always reaches back at least six months.
Each erasure adds its own event, **privacy.erased**, with who erased which student and when. See
[Privacy in plain words](privacy-in-plain-words.md).

## Exporting

**Export CSV** and **Export JSON** download every event that matches the current filters (up to 250,000; export a
range of dates at a time for more). The file is produced as it downloads, so a large log doesn't time out. CSV opens
in a spreadsheet (values that a spreadsheet would treat as a formula are made safe); JSON keeps the before and after
values as structured data. Every export is itself recorded in the log (`audit.exported`), with the filters used.

## For MindAttic platform staff

Platform staff see an **Organization** choice: any organization's log, or **Platform events** (the events that belong
to no organization). Opening another organization's log is recorded in that organization's log
(`audit.viewed.platform`), so its owners can see that it happened.
