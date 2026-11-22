# Admin guide

For organization owners and admins, and for MindAttic staff who run the platform. It covers the admin pages,
members and roles, two-step verification, organization settings, data retention, API keys, webhooks, usage,
data export and erasure, and the audit trail. Instructors' work is in the [Instructor guide](INSTRUCTOR_GUIDE.md),
grading in the [Grading guide](grading-guide.md), and the short version in the [quick start](quick-start-admin.md).

Owners and admins manage all of this from the **Admin** area. Signing in takes an owner or admin straight
to the **Dashboard** (`/admin`), and **Admin** in the top bar always leads back there.

## Admin navigation

Every admin page shows the same navigation under the top bar, in labelled groups. Each person sees only the
entries their role can open.

| Group | Entries |
|---|---|
| Overview | **Dashboard** (`/admin`), **Live sessions** (`/admin/live`) |
| People | **Students** (`/admin/students`, and one student at `/admin/students/{id}`), **Members** (`/org/members`) |
| Exams | **Exams** (`/admin/exams`), **Assignments** (`/admin/assignments`), **Desk** (`/admin/desk`, when the admin desk is turned on), **Source material**, **Assessments**, **Exam builder** |
| Results | **Grading** (`/grading`), **Review** (`/review`), **Analytics** (`/analytics`) |
| Organization | **Settings**, **Usage**, **API keys**, **Webhooks**, **Research** (all under `/org/...`), **Audit log** (`/admin/audit`), **System status** (`/admin/status`) |
| Platform | **Organizations** (`/platform/organizations`), MindAttic staff only |

Instructors keep their own links in the top bar (source material, assessments, review, analytics, grading);
examiners and second reviewers see **Grading**. A page still being finished in this release says "Coming in
this release" instead of leading nowhere.

The **admin desk** (exams, students and resets on one page) moved from `/admin` to `/admin/desk`.

## Dashboard

The dashboard (`/admin`) is the owner's and admin's home. It has four parts.

**Getting started.** Until an organization has done each of them, a checklist leads: create an exam, add
students, assign the exam, and watch a practice interview (add yourself a test student, sign in as them and
take the practice interview from **My exams**). Each step links to the page where it is done and ticks
itself off; the checklist goes away once an interview has ended.

**Needs attention.** Each kind shows how many there are, the oldest few, and a link to the full list. Every
row links straight to the place to act.

| Kind | What it counts | Each row opens |
|---|---|---|
| Interviews in progress | Started and not yet ended (running or paused) | Live sessions |
| Attempts waiting to be graded | Ended attempts with no grade yet, or still before human review is done | That attempt's grading page |
| Grades to reconcile | Attempts where the AI and the grader disagree by enough to need a reconciliation | That attempt's grading page |
| Grades waiting for final approval | Grades ready to approve | That attempt's grading page |
| Open appeals | Appeals a student opened and nobody has resolved | That attempt's grading page |
| Not started, closing within 3 days | Assignments of a published exam that close in the next 3 days and the student has not started | The student |
| Background work that failed | Jobs of this organization (reading notes, AI grading, webhook delivery, retention) that failed in the last 7 days, with the error | System status |

When nothing is waiting the dashboard says so.

**The last 7 and 30 days.** Exams taken (interviews that ended), the completion rate (of the interviews
started in the period and no longer running, how many reached a proper end rather than being abandoned or
failing), the median interview length, and the grading turnaround (the median time from an interview's end to
its grade's final approval, over grades approved in the period). An attempt that never counted, because it
was cut off by an update to the examiner, is left out. A dash means there is nothing to measure yet. Hover a
figure to see what it was worked out from; **Analytics** and **Usage** have more detail.

**Quick actions.** Create an exam, add students, assign an exam, watch live, grade, and invite staff.

The dashboard only reads: it changes nothing. It shows your current organization only, and **Refresh**
reloads it.

## Exams

**Exams** (`/admin/exams`) lists every exam with its status, objectives, how many students it is assigned to, completed
attempts, average completed time and whether its window is open. From a row's **Actions** you can change its
**settings** (duration and minimum objectives are frozen once it is published; integrity level, read-out check and
examiner voice and avatar apply to attempts that start from then on, with a reason once anyone has started), see
**objective coverage** (how many attempts examined each objective), set its **window**, **open now** or **close now**
for everyone it is assigned to, **preview it as a student** (the real examiner logic on the practice panel; nothing is
saved and it never counts as an attempt), **duplicate** it (a new draft with everything but its assignments and
attempts), **export** it as JSON, see its **history**, and **archive** or **restore** it. **Import from a file** brings in
an exported exam as a draft, listing every problem in the file if it can't. Every change is in the audit log. See
[Exams manager](admin-exams.md).
## Students

**Students** (`/admin/students`) lists every student of your organization, a page at a time.

- **Search** by name, user name or email. **Filter** by the exam they are assigned, by status (active, never
  signed in, deactivated, or invited and not yet accepted) and by accommodation (extra time, no eye contact
  expected). **Sort** by name, last sign-in, number of exams or date added, either way round. The filters are
  part of the address, so a filtered list can be bookmarked or shared with another admin.
- **Add a student** gives them an account now: a name, a user name, a password you hand over and, optionally,
  an email. By default they choose their own password at first sign-in. A user name that already exists is
  refused, so the page never takes over someone else's account.
- **Invite by email** sends a link; they choose their own password and join as a student. Waiting
  invitations are under the **Invited, not accepted** status, where you can **Resend** or **Withdraw** them.
- **Import CSV** adds a whole class. The first row names the columns: `email,name,username,password`; only
  `email` is required and the columns can be in any order. A row with a password gets an account straight
  away (the student changes the password at first sign-in; the user name is the email when none is given); a
  row without one is invited by email. **Check the file** first shows what each row would do (create,
  invite, update the name, no change, skipped, or the error on that line) and saves nothing. **Import** then
  does it. The email is the key, so importing the same file twice changes nothing the second time. An email
  that already has a Gadfly account in another organization is invited rather than given a new account, a
  deactivated student is skipped (reactivate them from their profile), and a member who is staff here is
  refused. At most 5,000 rows and 2 MB per file.
- **Export CSV** downloads every student the current filters match (not just the page on screen), in the
  same columns an import reads. Each export is recorded in the audit log.

Click a name to open the student's **profile** (`/admin/students/{id}`):

- **Account**: email, when they were added, last sign-in, whether they must choose a new password, and
  whether two-step sign-in is on. **Edit name and email**, **Reset password** (their other sign-ins end within
  about a minute), **Deactivate** and **Reactivate**. Deactivating needs a reason, which is kept in the audit
  log; it signs them out and stops them signing in to your organization, and keeps their exams, attempts and
  results, which reactivating gives back. An account that also belongs to another organization, or is staff
  anywhere, can be deactivated here but its name, email and password are changed only by its owner.
- **Exams and attempts**: every assigned exam with its dates, attempts used, status (not started, in
  progress, interrupted, awaiting grading, graded but not released, released), accommodations and each
  attempt with its score and a link to its review and transcript. An attempt replaced by an update to the
  examiner is greyed out and does not count.
- **Reviewer notes**: how many integrity notes their attempts carry and how many were raised for a reviewer.
  These are low-confidence signals for a reviewer to weigh, never a verdict and never part of a grade.
- **History**: the newest 100 audit entries about this student (account changes, membership, assignments,
  grading); the full log is on **Audit log**.

Every change on these pages is recorded in the audit log with who made it.

## Assignments

**Assignments** (`/admin/assignments`) shows who sits what, in two views.

- **List**: one row per student and exam, with its status (not started, in progress, interrupted, awaiting
  grading, graded but not released, released), opening and due dates in your own time zone, attempts used of
  those allowed, the latest score and any accommodations. Filter by exam, student, status, accommodation and
  due date range, sort by student, exam, due date or date assigned, and page through. **Export CSV** downloads
  every row the filters match (the export is recorded in the audit log).
- **Grid: students by exam**: active students down the side, published exams across the top (or one exam you
  choose). Each assigned cell shows its status, attempts and due date; an empty cell says "not assigned".

**Assign an exam** takes a published exam, an optional opening and due date, the attempts allowed and extra
time, and assigns it to the students you tick on the grid (each ticked exam goes to its own students), to a
pasted list of user names or emails (names that are not active students are listed and skipped), or to every
active student. Students who already have the exam are skipped.

Tick rows in the list (or assigned cells in the grid) to change many at once:

| Action | What it does |
|---|---|
| **Set dates** | Sets or removes the opening time and the due date. A window that would close before it opens is left alone and listed. |
| **Move due date** | Moves each due date by a number of days, forward or back; assignments with no due date are skipped. |
| **Grant another attempt** | One more attempt each (at most 20). Needs a reason. |
| **Reset attempt** | Clears the scores of the attempts so far, keeping an archived copy of every grade, report, reconciliation, approval and appeal in the audit log (the same rule as the desk's **Clear scores**); the attempts and transcripts stay, still count, and are not graded again unless someone decides to; then grants one more attempt so the student can sit it again. Needs a reason; refused while an attempt is in progress or when there is no attempt yet. |
| **Accommodations** | Extra time (from the next attempt), no eye contact expected, and typed answers (both from the next answer). Granted by staff for one student on one exam; students never see a switch for them. |
| **Unassign** | Removes assignments with no attempt yet. An assignment with an attempt is kept, since its transcript and grades belong to it. Needs a reason. |

Every change is recorded in the audit log once per assignment, with the reason where one is asked for, so it
also shows in the student's history.

## The admin desk

The **admin desk** (`/admin/desk`, **Desk** in the admin navigation) puts the everyday work on one page, in three
tabs. It is on when the server setting `Gadfly:AdminDesk:Enabled` is `true` — **off by default in Production**
(GAD-T247); a pilot that wants it turns the setting on explicitly. The organization's **Owners** and **Admins** can
use it, and nobody else: no special account is needed (GAD-T459). To let someone use the desk, give them the Owner or
Admin role on the members page. Every desk admin needs two-step verification to sign in (GAD-T247).

> **Retiring `Gadfly:AdminDesk:Admins`.** Earlier versions also let accounts listed by user name in
> `Gadfly:AdminDesk:Admins` use the desk whatever their role. That list is deprecated: it is still honoured through
> release V2, in one organization only and behind two-step verification, and the server logs a warning at start-up
> naming the listed accounts. From V3 it is ignored. Before then, give each listed account the Owner or Admin role
> (or use an account that has it), then delete the setting.

**Exams.** **New exam** creates an exam straight from notes: a **Subject** and **Topic** (the title defaults to
"Subject: Topic"), the source notes pasted in or uploaded as files, a **Duration** (default 20 minutes), whether
it is a **Voice exam** (on by default), and optional instructions. The panel reads the notes ("Reading the notes…"
with progress); when they are **Ready**, **Build objectives & publish** turns the concepts it found into the
exam's objectives and publishes it. Each exam row also has **Edit** (the wording and voice mode can still change
after publishing; objectives, rubric and duration are frozen), **Archive** (students can no longer start it;
results are kept) and, for staff, **Full editor** (the assessment editor).

**Students.** **Add a student** with a name, a user name, a password and an optional email; tick **Make them
choose their own password at first sign-in** if you want them to. **Reset password** sets a new one (their other
sign-ins end within about a minute). An account that is also staff somewhere is reset from its own organization's
members page instead.

**Student exams.** **Assign an exam**: a published exam, an active student and the **Attempts allowed** (default
3; assigning an exam that is already assigned changes its attempts). The table shows each student's attempts
("2 of 3 used", click to change), status (**Not started**, **In progress**, **Finished**, **Failed**), the latest
score, and the **Transcript**. Two repair actions:

| Action | What it does |
|---|---|
| **Reset** | Removes every attempt of that student on that exam, with its transcript, scores and grades, so the student starts fresh. It can't be undone, and it is refused while an attempt is running or paused. |
| **Clear scores** | Removes the grades and reports (a copy is archived in the audit log); the attempts and transcripts stay and still count as used, and the student is told so. |

Every desk action is written to the audit log with who did it: **Reset** as `desk.assignment-reset` (with the
attempts removed), **Clear scores** as `desk.scores-cleared` plus one `desk.scores-archived` row per attempt holding a
copy of its grading records, and password resets, attempt changes and exam edits under their own `desk.*` actions.

## Organizations

A customer **organization** is a school, department or company. Everything in Gadfly Exams (source material,
exams, sessions, grades, API keys, webhooks) belongs to exactly one organization, and every query is filtered to the
organization you are working in. One person can belong to several organizations, with a different role in each;
when they do, a drop-down in the top bar switches between them.

MindAttic staff (the platform **SuperAdmin**) create organizations at **Organizations**
(`/platform/organizations`): **Name** (2 to 200 characters, unique), **Owner user name**, **Owner email** and
**Temporary password**, then **Create organization**. The owner must change the password at first sign-in and set
up two-step verification. There is no screen yet to rename or deactivate an organization.

The very first account on a new server is the bootstrap SuperAdmin: user name `admin`, with the password from the
Vault secret `Security:bootstrap-token`. It must change its password and enroll two-step verification at first
sign-in. Rotate the bootstrap token in the Vault afterwards.

## Members and roles

Owners and admins manage people at **Members** (`/org/members`).

| Role | Can |
|---|---|
| Owner | Everything an admin can, plus add, change or remove other owners. An organization always keeps at least one active owner. |
| Admin | Members, settings, API keys, webhooks, usage, privacy requests; everything an instructor can except entering human scores. |
| Instructor | Source material, assessments, assignments and accommodations, review, grading (scoring, reconciling, approving, publishing, appeals) and analytics, for what they created (see **What instructors see**). |
| Examiner | Grading: reads transcripts and enters human scores, independently of the AI. |
| SecondReviewer | Grading: reconciles disagreements between the AI and human scores. |
| Student | Takes assigned exams and sees published results. |

### Invite people by email

**Invite by email**: an **Email** and a **Role**, then **Send invitation**. **Invite many (CSV)**: paste rows of
`email` or `email,role`, one per line (a header row starting `email` is fine, blank lines and lines starting `#`
are ignored, at most 200 rows), choose the **Role for rows without one**, and press **Send invitations**. The page
reports how many were invited and the reason for each row that wasn't.

The person gets a single-use link, valid for 72 hours. On **Join your organization** a new person chooses a
password (at least 12 characters) and, optionally, a user name (it defaults to their email); someone who already
has a Gadfly account signs in and presses **Join**, and the organization appears in their switcher. Staff roles set
up two-step verification at first sign-in.

The table under the forms shows each invitation as pending, accepted, revoked or expired. **Resend** sends a new
link (the old one stops working; at most five sends per invitation, at least a minute apart) and **Revoke** cancels
it. Only an owner can invite an owner.

Invitations need the server's outgoing mail (SMTP) and a public address
(`MindAttic:Auth:Organizations:Invitations:PublicBaseUrl`, or `MindAttic:Auth:Reset:PublicBaseUrl`). Without the
public address the page refuses with "Invitations are not configured"; without SMTP the link is only written to the
server log.

### Add a member directly, change a role, remove

**Add a member or change a role**: a **User name**, an optional **Email**, a **Role** and a **Temporary password**.

- A new user name creates an account; the person changes the temporary password at first sign-in.
- The user name of someone already in this organization changes their role (and restores them if removed).
- An account that belongs to another organization can't be added this way: invite them by email instead.

In the members table, **Remove** deactivates a membership and signs the person out everywhere within about a minute
(changing an active member's role does the same, so the new role applies at once). **Restore** brings them back.
Their records are kept either way. Students can also be managed on **Students** (above).

### Forgotten passwords

**Forgot password?** on the sign-in page (`/forgot-password`) takes a user name or email; if an account matches, it
emails a link to choose a new password. The page always gives the same answer, so it never reveals whether an
account exists. It needs SMTP and `MindAttic:Auth:Reset:PublicBaseUrl`; without them no email is sent and the server
logs a warning at startup. Owners and admins can also reset a student's password on the student's profile or the
admin desk.

## Two-step verification

Every staff role (owner, admin, instructor, examiner, second reviewer) must sign in with an authenticator app
(TOTP). New staff accounts enroll at first sign-in; after that every sign-in asks for the code, and staff pages refuse
a sign-in without it. Students don't need it, except a student account listed as a desk admin: it opens the admin
desk only after a sign-in with two-step verification (the home page points it to the setup page).

On a development machine (the `Development` environment) the requirement is off unless
`Gadfly:RequireMfaInDevelopment` is `true`. It is always on in production.

## Organization settings

**Settings** (`/org/settings`) holds the organization's choices. **Save settings** saves the page; every change of a
setting with consequences is written to the audit log.

| Setting | Default | What it does |
|---|---|---|
| **Keep what students said for (days)** | 90 | How long transcripts and what the student said are kept (7 to 3650 days). See **Data retention**. Students see the number in the notice before every attempt. |
| **Provider cost ceiling per month (USD)** | no limit | When the month's AI cost reaches it, new exam sessions are refused with "Your organization has reached its monthly usage limit. Please contact your instructor."; sessions already running finish. |
| **Default voice id**, **Default avatar id** | platform default | Saved for voice exams. The deployment's own voice and avatar settings decide what students hear and see today. |
| **Instructors see and manage only what they created** | on | See **What instructors see**. |
| **Allow a single grader (small organization)** | off | See **Separation of duties in grading**. |
| **Separate reconciler and approver** | Automatic | Or **Always**, or **Off**. See **Separation of duties in grading**. |
| **This organization is in the EU or EEA, or examines students there** | off | With **Voice measurements in voice exams**, decides whether the exam room measures students' voices (see below). |
| **Voice measurements in voice exams** | Default: off in the EU/EEA, on elsewhere | Or **Off for everyone**, or **On for everyone, EU included**. |
| **Send Gadfly's own notification emails** | on | See **Email notifications**. |

### Voice measurements and the EU

During each spoken answer the browser measures pitch, its steadiness, loudness and voicing, and the server
compares them with the student's own first answers. Reviewers see the raw differences; they never name a state such
as stress, and they never change a grade. Because the EU AI Act prohibits emotion recognition in education, the
measurements are **off by default** for an organization marked as in the EU or EEA and for any student marked as
living there (instructors mark students on the assessment's assignments list). When off, the numbers are discarded
before anything is stored. Choose **On for everyone** only after counsel has confirmed it is lawful for you. Each
change is recorded in the audit log. See [Privacy in plain words](privacy-in-plain-words.md).

### What instructors see

**Instructors see and manage only what they created** is on by default.

- **On:** an instructor sees only the courses, source collections and assessments they created, and the
  assignments, sessions, review pages, grading queue and analytics under those. Opening another instructor's item
  by its address shows "not found" and writes an `access.denied` entry to the audit log.
- **Owners, admins, examiners and second reviewers** always see the whole organization (grading isn't assigned to
  individual examiners), and so does the platform API. Students see only their own assignments.
- **Off:** every instructor sees everything in the organization. The change is audited
  (`settings.instructor-scope.changed`) and applies within about 15 seconds.
- Ownership is the item's creator; there is no transfer of ownership yet. Items created through the API have no
  instructor creator and are visible only to owners and admins while the switch is on.

### Separation of duties in grading

Whoever entered a grade's human score cannot also reconcile its AI-human disagreement or approve it: someone else
must (another instructor, an admin or owner, or a second reviewer for reconciliation). A refused attempt is written to
the audit log (`grade.separation-of-duties.refused`).

**Allow a single grader (small organization)** is off by default. Turn it on only when one person does all the
grading (a one-person organization, or while trying Gadfly out alone). While it is on, the person who scored a grade
may reconcile and approve it, and each time they do the audit log records a waiver
(`grade.separation-of-duties.waived`). Turning the switch on or off is audited too (`settings.single-grader.changed`).

**Separate reconciler and approver** keeps the person who reconciled a grade's disagreement (or was named as the
second reviewer on it) from also approving that grade, so a reviewer independent of the reconciliation signs it off.

- **Automatic** (the default): refused while another active instructor, admin or owner who neither scored nor
  reconciled the grade could approve it. In a two-person team, or with **Allow a single grader** on, the reconciler
  may approve, and the audit log records a waiver (`grade.reconciler-approver.waived`).
- **Always:** always refused, even when no one else can approve.
- **Off:** the reconciler may approve; each time is still recorded as a waiver.

A refused attempt is audited (`grade.reconciler-approver.refused`), and so is changing the setting
(`settings.approver-separation.changed`).

## Email notifications

Gadfly emails students on its own (separate from invitation and password emails) when:

| Email | Sent when |
|---|---|
| **Exam assigned** | An exam is assigned to them. |
| **Reminder before the due date** | An assigned exam closes within the next 48 hours and they haven't finished it. One reminder per assignment, ever. |
| **Result released** | Their result is published. |
| **Appeal received**, **Appeal decided** | They appeal a grade, and when the appeal is decided (with the outcome and the reason). |
| **Attempt reset** | Their attempts at an exam are reset from the admin desk. |

**Send Gadfly's own notification emails** on **Settings** turns all of these off for the organization (the change is
audited as `settings.email-notifications.changed`). A student can turn off reminders only, from **Email
preferences** in their own account; the other emails still come.

Emails go out through the background job queue over the same outgoing mail (SMTP) the sign-in emails use, and a
failed send is retried with growing waits. Nothing is sent when the server has no SMTP configured, or while it runs in
mock mode (see **Mock mode**): the email is still rendered and logged, marked **Failed** or **Skipped** with the reason.

<a id="email-preview"></a>
**Email preview** (`/admin/email-preview`, owners and admins) shows each email as students would see it, with sample
data, and **Send test to my email** queues a copy to your own account's address (it ignores the organization switch
and opt-outs). The page says up front whether this server can deliver email right now. **Recent sends** lists the
last 50 emails for the organization with their status: Queued, Sent, Failed (with the error) or Skipped (with why: the
organization switch, a student's opt-out, mock mode).

## Data retention

A job runs once a day and, for every session that ended longer ago than the retention period, removes what the
student said and keeps the outcome:

| Removed | Kept |
|---|---|
| Answer text (replaced with "[redacted after the retention period]"), the answer record and clarifications | Questions |
| Word timings, speech and voice measurements | Scores: each judgment's and each judge's score |
| Judges' summaries and reasoning, quotes in the AI report and in AI grade explanations | Mastery per concept |
| The session transcript used for resuming, and the browser information recorded at start | The human grades and comments, the approval, the consent record |
| Integrity notes' raw detail | Integrity notes' type, time and summary |

A session that never ended counts from one day after it started, and is then marked as abandoned. Video and audio
are never stored. Shortening the period applies to sessions that already ended, at the next daily run, and
redaction can't be undone.

The retention period never shortens the logs. Audit log events are never deleted (only the student's words in them
go), and the record of how long each outside call took is kept for at least six months (a year by default), whatever
period you choose. An erasure keeps those events too, without the student's details, and adds an event saying who
erased whom and when.

## API keys

Apps that integrate Gadfly (an LMS, a student-information system) call the platform API with an organization **API
key** (developers: see `docs/API.md` in the repository).

- A key looks like `gfk_<8 hex characters>_<secret>` and is sent as `Authorization: Bearer gfk_...`. It acts for the
  whole organization, not for a person: it can create exams from uploaded sources, assign students, open launch links
  and read results.
- Only a SHA-256 hash of the key is stored. The key is shown once, when it is created, and can't be recovered.
- A revoked key, or any key of a deactivated organization, is refused. The last use of each key is recorded.

At **API keys** (`/org/api-keys`): **Create a key** with a **Name** (for example "LMS integration"), then **Copy** it
and store it in a secret manager before pressing **I have stored it, hide the key**. The list shows each key's name,
prefix, creation date, last use and status. **Revoke…** then **Revoke** stops a key at once; it can't be undone.

## Webhooks

A webhook tells your app when something happens, by POSTing JSON to an `https` address. Today there is one event,
`session.completed`, sent when an exam attempt ends.

At **Webhooks** (`/org/webhooks`): **Add a webhook** with the **Endpoint URL (https)** and the events. The signing
secret (`whsec_...`) is shown once; your app uses it to check the `X-Gadfly-Signature` header of each delivery. A
delivery that doesn't get a 2xx answer within 15 seconds is retried with growing waits (from 15 seconds, doubling, at
most an hour apart), up to 8 attempts. **Deactivate…** stops a webhook; deliveries still waiting for it give up.

**Recent deliveries** lists the latest 50 with their event, endpoint, status (delivered, failed and retrying, gave up,
pending), attempt and your endpoint's response code: the first place to look when an integration says it heard
nothing.

## Usage

**Usage** (`/org/usage`) shows the last 12 months: this month's cost (against the monthly ceiling, if set), sessions
started and ended, last month's cost, a month-by-month table, each month's calls and cost by kind and provider, and
**LLM provider health** (working or failing, calls and failures, last success and failure; this is for the whole
platform since the server started, not only your organization).

Today only AI panel (LLM) calls are metered; the costs are estimates from configured prices. Speech-to-text and
text-to-speech are not yet counted.

## Analytics

**Analytics** (`/analytics`) has what instructors see (see the instructor guide, section 8) plus views that only owners
and admins get. Those use the AI panel's levels and the integrity notes, which a grader who is still grading blind must not
see, and instructors, examiners and second reviewers can all be such a grader. Every view below works on the scope picked at
the top (all assessments, a course or one assessment), names no student, and withholds a group with fewer than 5 students
(its size is still shown). Groups are the accommodations each assignment declared ("No accommodation", "Typed answers",
"Eye-contact accommodation", "Extra time", or a combination), or a grouping you paste in: one line per student with their
email or user name, a comma and a group label (first language, or a self-described group from a voluntary survey). A pasted
grouping is used for that comparison only and never stored; lines that match no member are counted, never shown.

- **Speech recognition accuracy by group** (everyone with analytics). Word error rate needs a reference: what was actually
  said. Spoken answers are no longer read back to the student, so newer answers have one only once a person has transcribed
  them. **Mark reference transcripts** (owners and admins) opens a sampler: **Draw a sample** offers 10, 20 or 50 spoken
  answers without a reference, spread across students and across how many fillers and repetitions the recognizer heard.
  For each, the person who heard the answer types every word the student said, picks how they heard it (present while it
  was given, or from a recording the student agreed to that your institution keeps; Gadfly stores no audio) and confirms the
  student agreed. **Start from the recognizer's text** copies what the recognizer wrote, to correct it. A reference never
  changes the answer or the grade, is recorded in the audit log (`response.reference-transcribed`, with who and when), can
  be changed or withdrawn later under **Answers with a reference**, and is removed with the answer's words at the end of the
  retention period or on erasure. The "With a reference" column counts all references, the human ones in brackets.
- **Eye-contact flag rates by group.** Owners and admins also see **Camera episodes and tracking quality by group**: no
  face, a second face and a changed face (Strict exams only) per 10 answering minutes, and each group's eye-tracking
  quality (sessions with a score, median, share poor, share calibrated to the screen). **Group by tracking quality** groups
  the rates by each session's quality band instead. Face detection fails more often for some people than others, so a
  group with more face episodes or lower quality is a warning about the camera, not about the students.
- **What the screen fade costs** (with the eye-contact rates). For each student with both, the judged score (0 to 1) and
  length of answers during which the screen faded, minus their other answers, and the answer right after a fade minus
  their clean answers; overall and by group, each with a rough 95% range. A drop of 0.05 or more with a range clear of zero
  is flagged. It cannot tell the fade's cost from a hard question drawing both a look away and a weaker answer.
- **Fairness by group**, at the bottom of the page, with one grouping box for its three tables:
  - **Panel and human levels by group.** On objectives graded by both: mean panel and human levels, panel minus human,
    that gap in standard deviations, how often they gave the same level or within one, and how often each gave level 0.
    Groups differing in level is not a warning (groups can differ in what they learned); a panel-human gap of 0.15
    standard deviations or more in one group, or gaps a quarter of a level apart between groups, is.
  - **What made the examiner press harder, by group.** Per answer: how often the examiner pressed harder, and how often
    each cue fired (hedging, vague wording, no reason or example, not convincing, evidence against, unclear meaning,
    recited), with long pauses per spoken minute and exam time paused for a lost device. The hedging, vague-wording and
    reason checks are English word lists, so paste a first-language grouping to see whether they fire more for
    second-language speakers. A cue firing more than a quarter more often for one group (at 20 or more answers each) is
    flagged. **Cut off** is how often the student went on speaking within 3 seconds of the end-of-answer silence handing
    a spoken answer in, out of the spoken answers watched for it (hover the figure for how many); a group cut off more
    often than another by more than a quarter (20 or more watched answers each) is flagged, since the silence may be too
    short for it. It is not recorded where voice measurements are off (by default in the EU) or for answers given before
    it existed. The review page marks each such answer "The student may have been cut off"; it never changes the
    questions or the grade.
  - **Typed answers against spoken answers.** Students with the typed-answers accommodation against everyone else (this
    table ignores the grouping box): levels, panel-human agreement and integrity notes per 10 answering minutes, overall
    and by objective, and the typed-minus-spoken human level at the same objective. The typed group chose itself through a
    documented need, so a difference is not the effect of typing.

**Export CSV** at the top of the page downloads the scope's analytics. For owners and admins it also carries these
fairness tables, after the usual sections (GAD-T463): `fairness_score_gaps`, `fairness_path_drivers` (with the cut-off
rate), `fairness_typed_vs_spoken` (and its by-objective rows), `fairness_camera_episodes` (the eye-contact rates, face
episodes and tracking quality) and `fairness_fade_cost`. Each starts with its caveat and any warnings follow its rows. The
export groups by the accommodations each assignment declared; a pasted grouping is never stored, so it is not in the file.
A group under 5 students keeps its name and size and every other cell reads `withheld`. Instructors and other roles get
the export without these sections.

The thresholds and their reasons are in docs/EVIDENCE_MAP.md (mechanics 2, 4 and 7).

## Data export and erasure

| Request | Where | What it does | Who |
|---|---|---|---|
| **Export** | **Download my data** on the student's **My exams**; **Export data** on **Members** | One JSON file with everything held about that person in this organization: assignments, attempts with questions, answers, judgments and votes, integrity notes, reports, grades and appeals. | The person themselves, or an owner or admin |
| **Erasure** | **Erase data…** on **Members** (type DELETE, then **Erase**) | Deletes every exam record of that person in this organization (answers, judgments, integrity notes, reports, grades, appeals, sessions and assignments), redacts them from the audit log's details, deactivates the membership and signs them out. Usage records are kept but unlinked. | Owner or admin |

Students can't erase their own data; they ask you. The sign-in account itself is not deleted, because the person may
belong to other organizations. Only an owner can erase an owner, and never the last one. Export first if the data
may be needed: erasure can't be undone. Erasure also removes the person's research choices and study arms, so they
are never in a later research export.

## Research

**Research** (`/org/research`, owners and admins) is for an organization running a study of its Gadfly exams, such as
the pilot in docs/PILOT.md. Nothing here is on until you turn it on.

- **Research consent.** Tick **Ask students the optional research question**, give the study a name students will
  recognize, and say who runs it and how to reach them (and the ethics approval reference, if there is one). Each
  student is then asked on the get-ready page, below the exam notice, whether their de-identified exam records may be
  used. It is optional: a student who says no, or doesn't answer, takes the same exam, graded the same way, and the
  people grading them are not told what they chose. Students change their mind on **My research choice** (linked from
  **My exams**), where a student who agreed can withdraw at any time. Each choice is recorded with the time and a
  fingerprint (SHA-256) of the exact wording the student saw; changing the study's name, contact or ethics reference
  changes that wording. The page shows only how many students agreed, declined or withdrew, never who.
- **Smallest group** (default 5, 3 to 50). An exam with fewer consenting students is left out of the export, and an
  accommodation shared by fewer students than this in an exam (or missing for fewer) is blanked, so no one can be
  picked out.
- **Study arms.** Pick an exam, add its arms ("control", "relay", "feedback withheld"...), then put each student in
  one, or press **Put students without an arm into one at random** (keeps the arms even). An arm is a label carried
  into the export: it doesn't change how Gadfly runs the exam. What the participants in an arm do differently, and
  whether their results are released, is arranged by the study team with the normal assignment and release controls.
  A condition that would switch off one of Gadfly's own exam rules (for example the eye-contact fade) is not offered and
  needs the product owner's approval first. A student's arm is fixed once they start an attempt; **Close** an arm to
  stop new students going into it.
- **Research export.** A ZIP of CSV files for the analysis, for every exam or the chosen one. Only students who
  currently agree are included; names, emails, what students said or typed, audio and video never are; everyone
  appears under a study code. docs/PILOT.md ("Research export") lists the files and columns. **Replace the study
  codes** gives every later export new codes that can't be linked to earlier ones.

Every change on this page and every download is recorded in the audit log (`research.settings.changed`,
`research.arm.*`, `research.key.replaced`, `research.exported`). A student's own research choice is not, so that it
stays out of the log the graders' administrators read; it is kept in the research consent record instead.

## Audit

Gadfly records every change that matters: settings, members and invitations, the admin desk, assessments and their
grading policy, accommodations, every grading step (including refusals and waivers), corrections to transcripts,
analytics exports, transcript downloads and access refusals. Each session's events are at the bottom of its grading
page (**Audit trail**), and each student's on their profile (**History**).

**Audit log** (`/admin/audit`) is the organization-wide viewer, for owners and admins only: filter by who, action (or a
whole family such as `grade.*`), student, exam, attempt and dates, search the words in an event and its before/after
detail, open any event to read its before and after values in full, and export what matches as CSV or JSON (the export is
recorded too). Events a privacy job removed a student's words from stay in the log, marked **Redacted**. MindAttic
platform staff can open any organization's log, and that is recorded in it. See [Audit log](admin-audit-log.md).

## Mock mode

When the AI panel runs without a real provider, every page shows a **MOCK MODE** banner: "No AI provider is in use.
Questions, scores, grades and reports on this site are simulated by fixed rules to test the workflow; they are not
genuine AI evaluations and must not be used to grade anyone." It is set by the server setting `Gadfly:AiMode`
(`live`, `mock` or `auto`). A mock-mode server never sends notification emails (see **Email notifications**).

## Server settings worth knowing

| Setting | Effect |
|---|---|
| `Gadfly:OpenExams` | When `true`, every published exam is offered to every student who opens **My exams** (with 50 attempts), without assigning it. Useful for a demo; turn it off for real classes. |
| `Gadfly:AdminDesk:Enabled` | The admin desk, for the organization's Owners and Admins (see above). Off in Production unless set to `true`. |
| `Gadfly:AdminDesk:Admins`, `Gadfly:AdminDesk:Organization` | **Deprecated** (GAD-T459): the old list of extra desk accounts and the organization it applied in. Honoured through V2 with a start-up warning; ignored from V3. |
| `Gadfly:Notifications:SendInMockMode` | `true` lets a mock-mode server send notification emails, for a developer whose SMTP points at a local test inbox. Off by default. |
| `Gadfly:Voice:Mode` | `live`, `mock` or `off`. With `off`, the get-ready page's **Spoken examiner** check fails and no exam can begin. |
| `Gadfly:RequireMfaInDevelopment` | Two-step verification on a development machine. |

## Health

| Endpoint | What |
|---|---|
| `/healthz` | Anonymous: the app and its database are up. |
| `/healthz/ready` | Anonymous: the system status page's checks (database, migrations, AI panel, voice, jobs, webhooks, errors) as names and states only. 200 when ready, 503 when the database does not answer or migrations are not applied; the rest only warn. |
| `/healthz/providers` | SuperAdmin only: the state of each AI provider on the panel. A provider that fails several calls in a row is logged as an error and the panel carries on without it. |

Application logs go to MindAttic.Log's database for Gadfly.Server; every exam step is logged with its session and
organization. See [System status and jobs](admin-system-status.md) for the status page.
