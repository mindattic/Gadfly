# Admin guide

For organization owners and admins, and for MindAttic staff who run the platform. It covers
organizations, members and roles, two-step verification, data retention, API keys, webhooks, usage, and
data export and erasure. Instructors' work is in the [Instructor guide](INSTRUCTOR_GUIDE.md).

Several admin tasks below have no screen yet; the organization admin console (GAD-T55) will add them.
Each section says what works today.

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
| Instructor | Source material, exams, assignments, review, grading and releasing results. |
| Student | Take assigned exams and see released results. |

**Add a member** with a user name, an optional email, a role and a temporary password.

- A new user name creates an account. The person changes the temporary password at first sign-in.
- A user name that already exists (for example someone from another organization) adds that account to
  yours; their password doesn't change. Adding someone who is already a member changes their role and
  restores them if they were removed.
- The password must meet the sign-in library's password policy; the page shows the reason if it doesn't.

**Remove** deactivates a membership; the person loses access at their next sign-in. **Restore** brings
them back. Their records are kept either way (see erasure below to delete them).

There are no email invitations yet (M.AUTH-T35): give people their user name and temporary password
yourself. There is also no password-reset screen in Gadfly yet.

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

There is no screen to change the period yet. Until the admin console arrives, MindAttic staff can set it
per organization in the database (`gadfly.OrganizationSettings.RetentionDays`). See
[Privacy](../PRIVACY.md) for the reasoning and the regulatory notes.

## API keys

Apps that integrate Gadfly call the [platform API](../API.md) with an organization **API key**:

- A key looks like `gfk_<8 hex characters>_<secret>` and is sent as `Authorization: Bearer gfk_...`.
- It acts for the whole organization (not for a person), and everything it does is scoped to that
  organization.
- Only a SHA-256 hash of the key is stored. The key is shown once, when it is created, and can't be
  recovered; lose it and you make a new one.
- A revoked key, or any key of a deactivated organization, is refused. The last time each key was used
  is recorded.

There is no screen or endpoint to create, list or revoke keys yet; the admin console will add them. Until
then MindAttic staff create keys on the server.

## Webhooks

A webhook tells your app when something happens, by POSTing JSON to an `https` URL you register through
the API (`POST /api/v1/webhooks`). Today there is one event, `session.completed`, sent when an exam
attempt ends.

- Each webhook gets its own signing secret (`whsec_...`), shown once when it is registered and stored
  encrypted.
- Every delivery is signed (`X-Gadfly-Signature`), so your app can check it came from Gadfly; the
  [API reference](../API.md#webhooks) shows how.
- A delivery that doesn't get a 2xx answer within 15 seconds is retried with growing waits (15 seconds,
  then doubling, at most an hour apart), up to 8 attempts.
- `DELETE /api/v1/webhooks/{id}` deactivates a webhook; deliveries still waiting for it are dropped.

Webhooks are managed through the API only; there is no screen for them yet.

## Usage

The platform has a usage ledger for LLM, speech-to-text, text-to-speech and embedding calls (units and
cost per organization and session), and a monthly cost quota setting. Neither is filled in or enforced
yet (usage metering, GAD-T47), and there is no usage screen. The **Exams** list shows how many students
each exam is assigned to and how many sessions started and ended.

## Data export and erasure

Students can ask for a copy of their data or for it to be deleted (see the [Student guide](STUDENT_GUIDE.md#your-data)).

| Request | How | Who |
|---|---|---|
| Export | `GET /privacy/export/{userId}` downloads one JSON file: the student's assignments, every attempt with its questions, answers, judgments and votes, integrity events, reports and grades, in the current organization. | The student themselves, or an owner or admin |
| Erasure | `POST /privacy/erase/{userId}` with the form field `confirm=DELETE`. Deletes every session record of that student in the current organization (turns, judgments, votes, claims, integrity events, mastery, reports, grades, sessions and assignments) and deactivates their membership. Usage records are kept but unlinked from the session. | Owner or admin |

The student's sign-in account itself is not deleted, because the person may belong to other
organizations.

Neither has a button yet, and the Members page doesn't show user ids. Until the admin console adds them,
MindAttic staff run exports and erasures on request.

## Health

| Endpoint | What |
|---|---|
| `/healthz` | Anonymous: the app and its database are up. |
| `/healthz/providers` | SuperAdmin only: the state of each LLM provider on the panel. A provider that fails several calls in a row is logged as an error and the panel carries on without it. |

Application logs go to MindAttic.Log's database for Gadfly.Server; every exam step is logged with its
session and organization.
