# Admin guide

For organization owners and admins, and for MindAttic staff who run the platform. It covers
organizations, members and roles, two-step verification, data retention, API keys, webhooks, usage, and
data export and erasure. Instructors' work is in the [Instructor guide](INSTRUCTOR_GUIDE.md).

Owners and admins manage all of this in the **organization admin console**: the links at the top of
every admin page are **Members** (`/org/members`), **Settings** (`/org/settings`), **API keys**
(`/org/api-keys`), **Webhooks** (`/org/webhooks`) and **Usage** (`/org/usage`).

## Organizations

A customer **organization** is a school, department or company. Everything in Gadfly Exams (source
material, exams, sessions, grades, API keys, webhooks) belongs to exactly one organization, and every
query is filtered to the organization you are working in. One person can belong to several
organizations, with a different role in each; the drop-down in the top bar switches between them.

MindAttic staff (the platform **SuperAdmin**) create organizations at **Organizations**
(`/platform/organizations`): a name (2 to 200 characters, unique), and the owner's user name, email and
temporary password. The owner must change the password at first sign-in and set up two-step
verification. There is no screen yet to rename or deactivate an organization.

The very first account on a new server is the bootstrap SuperAdmin: user name `admin`, with the password
from the Vault secret `Security:bootstrap-token`. It must change its password and enroll MFA at first
sign-in. Rotate the bootstrap token in the Vault afterwards.

## Members and roles

Owners and admins manage people at **Members** (`/org/members`).

| Role | Can |
|---|---|
| Owner | Everything an admin can, plus add other owners. An organization always keeps at least one active owner. |
| Admin | Add, remove and restore members; everything an instructor can. |
| Instructor | Source material, exams, assignments, review, grading and releasing results, for what they created (see **What instructors see**, below). |
| Student | Take assigned exams and see released results. |

**Add a member** with a user name, an optional email, a role and a temporary password.

- A new user name creates an account. The person changes the temporary password at first sign-in.
- Adding someone who is already a member changes their role and restores them if they were removed.
  An account that belongs to another organization cannot be added this way: invite that person by email
  (below), and they accept it themselves.
- The password must meet the sign-in library's password policy; the page shows the reason if it doesn't.

**Remove** deactivates a membership and signs the person out everywhere within about a minute (changing
an active member's role does the same, so the new role applies at once). **Restore** brings them back.
Only an owner can add an owner, change an owner's role, or remove or erase an owner, and an organization
always keeps one active owner. Their records are kept either way (see erasure below to delete them).

**Invite by email** (also on Members). Enter an email address and a role, or paste a CSV (one `email` or
`email,role` per line, a header row is fine, up to 200 rows). The person gets a single-use link, valid for
72 hours, to choose their own password; their user name is their email address unless they pick another.
Staff roles then set up two-step verification at first sign-in. Someone who already has a Gadfly account
signs in and accepts, and the organization appears in their switcher. The table under the form shows each
invitation as pending, accepted, revoked or expired, with **Resend** (a new link; the old one stops working,
at most five sends per invitation, one a minute) and **Revoke**. The page gives the same answer whether or
not the address already has an account. Invitations need the server's outgoing mail (SMTP) and
`MindAttic:Auth:Reset:PublicBaseUrl` (or `MindAttic:Auth:Organizations:Invitations:PublicBaseUrl`); without
SMTP the link is only written to the server log. The temporary-password form above stays for accounts you
create yourself, and the API still provisions students that way.

**Forgotten passwords.** The sign-in page has a **Forgot password?** link (`/forgot-password`). The person
enters their user name or email; if an account matches, they are emailed a link to `/account/reset`,
where they choose a new password and then sign in again (two-step verification still applies). The page
always gives the same answer, so it never reveals whether an account exists. The reset email needs the
server's outgoing mail (SMTP) and `MindAttic:Auth:Reset:PublicBaseUrl` to be configured; without them no
email is sent and the server logs a warning at startup.

## Two-step verification (MFA)

Owners, admins and instructors must sign in with an authenticator app (TOTP). New staff accounts are
asked to enroll at first sign-in; after that every sign-in asks for the code, and the staff pages refuse
a sign-in without it. Students don't need MFA.

On a development machine (the `Development` environment) the MFA requirement for organization roles is
off unless `Gadfly:RequireMfaInDevelopment` is `true`. It is always on in production.

## Data retention

Each organization has a retention period, **90 days** by default. A job runs once a day and, for every
session that ended longer ago than that, removes what the student said and keeps the outcome:

| Removed | Kept |
|---|---|
| Answer text (replaced with "[redacted after the retention period]") | Questions |
| Word timings, speech metrics, answer audio references | Judgments and each judge's vote |
| The claim ledger and the session transcript used for resuming | Mastery per concept |
| Integrity events' raw detail | Integrity events' type, time and summary |
| The browser information recorded at start | The AI report, grades and comments, the consent record |

Video is never stored. Students are told the retention period, in days, in the notice they accept before
every attempt.

Change the period at **Settings** (`/org/settings`): 7 to 3650 days. The page lists what is removed
and what is kept. Shortening it applies to sessions that already ended, at the next daily run, and
redaction can't be undone. See [Privacy](../PRIVACY.md) for the reasoning and the regulatory notes.

### What instructors see

Decision D18 (GAD-T170). **Settings > What instructors see** has one switch, **Instructors see and manage only what
they created**, on by default (including for existing organizations when this was introduced).

- **On:** an instructor sees only the courses, source collections and assessments whose creator is that
  instructor, and the assignments, sessions, review pages, grading queue and analytics under those assessments.
  They can only build an assessment on a course and collection of their own. Opening another instructor's item by
  its address shows "not found" and writes an `access.denied` entry to the audit log (who, which item, when).
- **Owners and admins** always see the whole organization, and so do **examiners** and **second reviewers**
  (grading is not assigned to individual examiners, so there is nothing to scope them by) and the platform API
  (keys are organization-level integrations). Students are unchanged: they see only their own assignments.
- **Off:** every instructor sees everything in the organization, as before. Turning the switch on or off is written
  to the audit log (`settings.instructor-scope.changed`) and applies within about 15 seconds.
- Ownership is the item's creator. There is no transfer of ownership yet: to hand work to another instructor,
  an owner or admin keeps access, or the switch is turned off. Items created through the API (for example
  collections) have no instructor creator and are visible to owners and admins only while the switch is on.
- Deploy note: this needs the `InstructorOwnershipScope` database migration (one new column,
  `gadfly.OrganizationSettings.InstructorsSeeOnlyOwn`, default on). No ownership data is backfilled: courses,
  collections and assessments already record their creator.

Settings also holds the **monthly cost quota** (see Usage) and the **default voice and avatar** ids for
voice-mode exams (voice exams aren't built yet; students of a voice exam answer by typing).

## API keys

Apps that integrate Gadfly call the [platform API](../API.md) with an organization **API key**:

- A key looks like `gfk_<8 hex characters>_<secret>` and is sent as `Authorization: Bearer gfk_...`.
- It acts for the whole organization (not for a person), and everything it does is scoped to that
  organization.
- Only a SHA-256 hash of the key is stored. The key is shown once, when it is created, and can't be
  recovered; lose it and you make a new one.
- A revoked key, or any key of a deactivated organization, is refused. The last time each key was used
  is recorded.

Manage keys at **API keys** (`/org/api-keys`):

- **Create a key** with a name (for example "LMS integration"). The full key is shown once, with a copy
  button; store it in a secret manager.
- The list shows each key's name, prefix, when it was created and when it was last used.
- **Revoke** (after a confirmation) stops the key working at once. It can't be undone; create a new key
  instead.

## Webhooks

A webhook tells your app when something happens, by POSTing JSON to an `https` URL you register at
**Webhooks** (`/org/webhooks`) or through the API (`POST /api/v1/webhooks`). Today there is one event,
`session.completed`, sent when an exam attempt ends.

- Each webhook gets its own signing secret (`whsec_...`), shown once when it is registered and stored
  encrypted.
- Every delivery is signed (`X-Gadfly-Signature`), so your app can check it came from Gadfly; the
  [API reference](../API.md#webhooks) shows how.
- A delivery that doesn't get a 2xx answer within 15 seconds is retried with growing waits (15 seconds,
  then doubling, at most an hour apart), up to 8 attempts.
- **Deactivate** on the Webhooks page (after a confirmation), or `DELETE /api/v1/webhooks/{id}`,
  stops a webhook; deliveries still waiting for it are dropped.

The Webhooks page also lists recent deliveries with their event, endpoint, status, attempt number and the
response code your endpoint returned, which is the first place to look when an integration says it heard nothing.

### Live exam sessions for integrators

Apps that run their own exam screen (for example a voice client) can drive a session over the
SignalR **session hub** at `/hubs/session` instead of the Gadfly exam room. It is for developers; see the
[API reference](../API.md). Nothing needs turning on for it.

## Usage

**Usage** (`/org/usage`) shows, for recent months, the provider cost and metered calls (LLM,
speech-to-text, text-to-speech, embeddings) by kind and provider, the number of sessions started and
ended, the month's cost against the **monthly cost quota** set under Settings, and the health of each LLM
provider on the judging panel.

Cost figures are metered per panel call (GAD-T47). When the month's cost reaches the quota, new exam sessions
are refused with a message to contact the instructor; sessions already running finish. The **Exams** list shows how
many students each exam is assigned to and how many sessions started and ended.

## Data export and erasure

Students can download a copy of their own data from **My exams**, and can ask for it to be deleted (see
the [Student guide](STUDENT_GUIDE.md#your-data)). On **Members**, each member has an **Export data**
link and an **Erase data** form, which asks you to type DELETE before it runs.

| Request | How | Who |
|---|---|---|
| Export | `GET /privacy/export/{userId}` downloads one JSON file: the student's assignments, every attempt with its questions, answers, judgments and votes, integrity events, reports and grades, in the current organization. | The student themselves, or an owner or admin |
| Erasure | `POST /privacy/erase/{userId}` with the form field `confirm=DELETE`. Deletes every session record of that student in the current organization (turns, judgments, votes, claims, integrity events, mastery, reports, grades, sessions and assignments) and deactivates their membership. Usage records are kept but unlinked from the session. | Owner or admin |

The student's sign-in account itself is not deleted, because the person may belong to other
organizations.

Export before erasing if the data may be needed: erasure can't be undone.

## Health

| Endpoint | What |
|---|---|
| `/healthz` | Anonymous: the app and its database are up. |
| `/healthz/providers` | SuperAdmin only: the state of each LLM provider on the panel. A provider that fails several calls in a row is logged as an error and the panel carries on without it. |

Application logs go to MindAttic.Log's database for Gadfly.Server; every exam step is logged with its
session and organization.
