# Students directory

**Students** (`/admin/students`, in the admin navigation) lists every student in your organization, and each student's
**profile** shows their account, every exam assigned to them and every attempt. Only the organization's owners and admins
open these pages. Every change made here is recorded in the [audit log](admin-audit-log.md).

## The list

| Column | What it means |
|---|---|
| Name | Their name (or user name if they have none); it opens their profile |
| User name, Email | As on their account |
| Status | **Active**, **Never signed in** (active, but has not signed in yet) or **Deactivated** |
| Exams | How many exams are assigned to them |
| Accommodations | *extra time* and *no eye contact expected*, if any of their assignments has them |
| Last sign-in | In UTC, or *never* |

A deactivated student's row is dimmed.

### Finding students

- **Search**: part of a name, user name or email.
- **Assigned to**: students who have one exam.
- **Status**: everyone, active, never signed in, deactivated, or **Invited, not accepted** (invitations still waiting; see
  below).
- **Accommodation**: anyone with an accommodation, extra time, or no eye contact expected.
- **Sort** by name, last sign-in, exams assigned or date added, ascending or descending.

25 students to a page by default (up to 200). The filters are part of the page's address, so the browser's Back button
and a saved link bring the same view back. **Clear filters** shows everyone again.

## Adding students

- **Add a student**: name, user name, password and (optionally) email. The account exists at once; hand the student their
  user name and password. By default they must choose their own password the first time they sign in.
- **Invite by email**: the student gets an email with a link, chooses their own password and joins your organization as a
  student. Waiting invitations are listed under **Status: Invited, not accepted**, where you can **Resend** or **Withdraw**
  them.
- **Import CSV**: upload a `.csv` file (up to 2 MB) or paste one. The first row names the columns:
  `email,name,username,password`; only `email` is required. A row with a password gets an account straight away (they
  change the password at first sign-in); a row without one is invited by email. The email is the key, so importing the
  same file again changes nothing.

  **Check the file** first: it is a dry run that shows, row by row, what would happen (create, invite, rename, unchanged,
  skipped, or an error and why) and saves nothing. **Import** then does it. A row that fails is reported and the others
  still go through; if the import stops part way, check the file again and the rows already done show as no change.

**Export CSV** downloads every student matching the current filters (not just this page), up to 50,000, with their email,
name, user name, status, exams assigned, accommodations, last sign-in and date added. Exporting personal data is itself
recorded in the audit log.

## A student's profile

Open a name to see:

- **Account**: email, when they were added, their last sign-in, whether they must choose a new password, and whether
  two-step sign-in is on.
- **Reviewer notes**: how many integrity notes their attempts carry and how many were raised for a reviewer to look at.
  These are low-confidence signals for a reviewer to weigh, never a verdict and never part of a grade; open an attempt's
  review to see them in context.
- **Exams and attempts**: each assigned exam with its status (see [Assignments](admin-assignments.md#statuses)), its
  window, attempts used of those allowed, extra time, the eye-contact accommodation and when results were released; and
  under it each attempt with its state, start and end, score and reviewer notes, and a link to its **Review and
  transcript**. An attempt that does not count (the exam changed under it) is dimmed.
- **History**: the newest 100 audit entries about the student (their account, membership, assignments and attempts),
  with who did what and when. The full record is on the [audit log](admin-audit-log.md).

**Manage assignments** opens the [assignments console](admin-assignments.md) filtered to this student.

### Changing a student

| Action | What it does |
|---|---|
| Edit name and email | Changes them on the account |
| Reset password | Sets a new password, optionally making them choose their own at next sign-in; their other sign-ins end within about a minute |
| Deactivate | Needs a reason. Signs them out within about a minute and stops them signing in to your organization. An exam they had open stops with their sign-in. Their exams, attempts and results are kept |
| Reactivate | Lets them sign in again with everything as it was |

Editing and password resets are only offered when the account belongs to your organization alone, as a student. If the
same account is also in another organization or is staff anywhere, its owner manages its name, email and password, and
the profile says so. If MindAttic support has locked an account, the profile says that too.

The audit log records these as `admin.student-invited`, `admin.invitation-resent`, `admin.invitation-revoked`,
`admin.student-edited`, `admin.student-deactivated`, `admin.student-reactivated` and `admin.students-exported`, beside
the account changes the sign-in system records itself.
