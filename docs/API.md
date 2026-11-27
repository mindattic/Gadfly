# Platform API v1

The Gadfly platform API lets another application use Gadfly's Socratic exams: upload source material,
have it processed into concepts, create exams, provision and assign students, send each student a signed
launch link into the exam room, read results, and receive a signed webhook when an attempt ends.

Everything is under `/api/v1`, speaks JSON (camelCase), and acts inside one organization: the one that
owns the API key. A machine-readable description is at [`/openapi/v1.json`](#openapi).

> Every exam, however it was created, is taken in the spoken exam room, on camera: the student answers aloud to the
> talking examiner (see [VOICE.md](VOICE.md)); typed answers exist only as an accommodation staff grant per student
> and exam. The examples use a local server; the hosted instance is at https://mindattic-gadfly.azurewebsites.net.

## Authentication

Send the organization's API key as a bearer token on every call:

```http
Authorization: Bearer gfk_3f9a1c07_q2mJ0b4Xw6...
```

| Fact | Detail |
|---|---|
| Format | `gfk_<8 hex characters>_<secret>` |
| Scope | The whole organization. The key is not a person; it can do everything listed on this page within its organization and nothing outside it. |
| Storage | Only a SHA-256 hash is stored. The key is shown once at creation. |
| Refused | A missing, unknown or revoked key, or a key of a deactivated organization: `401 Unauthorized` with no body. |

Organization admins create and revoke keys at `/org/api-keys` in the admin console. See the
[Admin guide](guides/ADMIN_GUIDE.md#api-keys).

## Endpoints

| Method | Path | Does |
|---|---|---|
| GET | `/api/v1/collections` | List source collections |
| POST | `/api/v1/collections` | Create a source collection |
| GET | `/api/v1/collections/{id}` | One collection: status, documents, concept map version |
| POST | `/api/v1/collections/{id}/documents` | Upload documents (multipart) |
| POST | `/api/v1/collections/{id}/process` | Queue processing |
| GET | `/api/v1/jobs/{id}` | Processing job status |
| GET | `/api/v1/collections/{id}/concepts` | The collection's concept map |
| POST | `/api/v1/exams` | Create and publish an exam |
| POST | `/api/v1/exams/{id}/assignments` | Provision a student and assign the exam |
| POST | `/api/v1/assignments/{id}/launch` | A signed launch link for the student |
| GET | `/api/v1/assignments/{id}/sessions` | The student's attempts |
| GET | `/api/v1/sessions/{id}/result` | One attempt's result and report |
| GET | `/api/v1/sessions/{id}/transcript` | One attempt's official transcript (JSON, PDF, Word, web page, text, captions) |
| POST | `/api/v1/webhooks` | Register a webhook |
| GET | `/api/v1/webhooks` | List webhooks |
| DELETE | `/api/v1/webhooks/{id}` | Deactivate a webhook |
| GET | `/launch?token=...` | The student-facing end of a launch link (not an API call) |

Ids are GUIDs. Times are ISO 8601 with an offset (UTC).

### Create a collection

`POST /api/v1/collections`

```json
{ "title": "Biology 101: cells" }
```

`201 Created`, `Location: /api/v1/collections/{id}`

```json
{ "id": "6b1f0a52-7c1e-4a43-9d8e-2f1c5b7e9a10", "title": "Biology 101: cells" }
```

`400` `{ "error": "title is required" }` when the title is missing or blank, or
`{ "error": "title must be at most 300 characters" }`. The title is trimmed.

### List collections

`GET /api/v1/collections`

```json
[
  {
    "id": "6b1f0a52-7c1e-4a43-9d8e-2f1c5b7e9a10",
    "title": "Biology 101: cells",
    "status": "Ready",
    "documentCount": 2,
    "conceptCount": 9,
    "conceptMapVersion": 3,
    "createdAt": "2026-11-28T09:12:00+00:00",
    "latestJob": {
      "id": "0d6c2b9e-41f7-4b55-9a5e-8c3d7f210b6a",
      "state": "Succeeded",
      "progress": 1,
      "progressMessage": "9 concepts ready",
      "error": null,
      "createdAt": "2026-11-28T09:13:10+00:00",
      "completedAt": "2026-11-28T09:15:42+00:00"
    }
  }
]
```

Newest first. `status` is `Pending`, `Processing`, `Ready` or `Failed`. `documentCount` counts uploaded and
processed documents once each; `conceptCount` leaves out archived concepts. `latestJob` is the most recent
processing job (its `state` as in [Job status](#job-status)), or `null` if the collection was never
processed. An empty array when there are none.

### Get a collection

`GET /api/v1/collections/{id}` (the `Location` returned when the collection was created)

```json
{
  "id": "6b1f0a52-7c1e-4a43-9d8e-2f1c5b7e9a10",
  "title": "Biology 101: cells",
  "status": "Ready",
  "conceptMapVersion": 3,
  "createdAt": "2026-11-28T09:12:00+00:00",
  "documents": [
    {
      "fileName": "chapter1.md",
      "sizeBytes": 18234,
      "uploaded": true,
      "ingested": true,
      "changedSinceIngest": false,
      "passageCount": 14,
      "uploadedAt": "2026-11-28T09:12:30+00:00",
      "ingestedAt": "2026-11-28T09:14:02+00:00",
      "error": null
    }
  ],
  "latestJob": null
}
```

| Field | Meaning |
|---|---|
| `conceptMapVersion` | Goes up whenever the concept map or a rubric changes; `0` before the first processing. |
| `documents[].uploaded` | The file is in the upload folder (`sizeBytes` and `uploadedAt` are `null` otherwise). |
| `documents[].ingested` | The document was processed successfully. |
| `documents[].changedSinceIngest` | The uploaded file differs from what was processed; process again to pick it up. |
| `documents[].error` | Why processing this document failed, or `null`. |
| `latestJob` | As in [List collections](#list-collections). |

`404` when the collection isn't in your organization.

### Upload documents

`POST /api/v1/collections/{id}/documents` with `multipart/form-data`, one or more file parts.

```bash
curl -X POST https://localhost:7311/api/v1/collections/6b1f0a52-7c1e-4a43-9d8e-2f1c5b7e9a10/documents \
  -H "Authorization: Bearer $GADFLY_KEY" \
  -F "file=@chapter1.md" -F "file=@chapter2.md"
```

`200 OK`

```json
{ "saved": ["chapter1.md", "chapter2.md"] }
```

- Accepted: `.txt`, `.text`, `.md`, `.markdown`, `.pdf`, `.docx`, `.epub`, `.html` and `.htm` (the same as the
  source library page). A scanned, image-only PDF uploads but yields no text.
- Each file must be 1 byte to 20 MB. Keep each request under the server's request size limit (about
  28 MB by default); send large sets in several requests.
- Reserved Windows device names are refused whatever the extension: `CON`, `PRN`, `AUX`, `NUL`,
  `COM1`-`COM9` and `LPT1`-`LPT9` (so `con.txt` and `com1.notes.md` are refused too).
- File names are cleaned: folder parts are dropped and characters not allowed in file names become `_`.
  A file with the same name as one already uploaded replaces it.
- Every file in the request is checked before any is saved: if one is refused, nothing from that request
  is stored. Each file is written under a temporary name and then moved into place, so processing never
  reads a half-written file.

`404` when the collection isn't in your organization. `400` with one of:

- `{ "error": "send multipart/form-data with one or more files" }`
- `{ "error": "<file>: only .txt, .text, .md, .markdown files are accepted for now" }`
- `{ "error": "<file>: CON is a reserved device name; rename the file" }`
- `{ "error": "<file>: files must be 1 byte to 20 MB" }`
- `{ "error": "<file>: the file needs a name" }`
- `{ "error": "the upload could not be read: ..." }` (a malformed multipart body)

### Process a collection

`POST /api/v1/collections/{id}/process`

```json
{ "rebuildConceptMap": false }
```

The body is optional. Processing reads new and changed documents, has the AI panel extract the concept
map (concepts are re-extracted only when a document changed, unless `rebuildConceptMap` is `true`), and
writes a rubric per concept. Concepts and rubrics an instructor edited are kept.

`202 Accepted`, `Location: /api/v1/jobs/{jobId}`

```json
{ "jobId": "0d6c2b9e-41f7-4b55-9a5e-8c3d7f210b6a" }
```

`404` when the collection isn't in your organization.
`409` `{ "error": "this collection is already being processed (job <jobId>, Queued)" }` while a processing
job for it is `Queued` or `Running`: poll that job instead of queuing another.
`400` `{ "error": "upload at least one document first (.txt, .text, .md, .markdown)" }` when the collection
has no readable, non-empty uploaded file.

### Job status

`GET /api/v1/jobs/{id}`

```json
{
  "id": "0d6c2b9e-41f7-4b55-9a5e-8c3d7f210b6a",
  "kind": "source.process",
  "state": "Running",
  "progress": 0.62,
  "progressMessage": "Writing rubric: Osmosis",
  "error": null,
  "completedAt": null
}
```

`state` is `Queued`, `Running`, `Succeeded`, `Failed` or `Cancelled`; `progress` runs from 0 to 1. A
failed run is retried a few times with growing waits (the job goes back to `Queued` with the last
`error`); `Failed` is final. Poll every few seconds. `404` for a job that isn't your organization's.

### Concept map

`GET /api/v1/collections/{id}/concepts`

```json
{
  "version": 3,
  "concepts": [
    { "key": "osmosis", "title": "Osmosis", "definition": "The movement of water across a semi-permeable membrane from lower to higher solute concentration." },
    { "key": "diffusion", "title": "Diffusion", "definition": "The net movement of particles from higher to lower concentration." }
  ],
  "relations": [
    { "from": "diffusion", "to": "osmosis", "kind": "prerequisite" }
  ]
}
```

Archived concepts are left out. `404` until the collection has been processed.

### Create an exam

`POST /api/v1/exams`

```json
{
  "collectionId": "6b1f0a52-7c1e-4a43-9d8e-2f1c5b7e9a10",
  "title": "Cells: oral check",
  "conceptKeys": ["diffusion", "osmosis"],
  "minutes": 20,
  "difficulty": 3,
  "maxTurns": 30,
  "mode": "Text",
  "instructions": "Explain in your own words. 'I don't know' is fine."
}
```

| Field | Default | Rules |
|---|---|---|
| `collectionId` | required | A processed collection of your organization. |
| `title` | required | 1–300 characters after trimming. |
| `conceptKeys` | every current concept | In the order they should be examined; no blank keys; duplicates are dropped. |
| `minutes` | 20 | Clamped to 1–240. |
| `difficulty` | 3 | Clamped to 1–5 (gentle to rigorous). |
| `maxTurns` | 30 | Questions in the whole exam, clamped to 1–200. |
| `mode` | `"Text"` | `"Text"` or `"Voice"` (any case). Kept for compatibility and stored, but it no longer changes anything: every exam is taken spoken, on camera. |
| `instructions` | none | Shown to students. |

The exam is **published immediately**, so its settings are frozen. Each concept gets up to six
questions; integrity monitoring is Standard.

`201 Created`, `Location: /api/v1/exams/{id}`

```json
{ "id": "a4e2d5c8-0b3f-4d91-8e7a-5c6b1f2e3d40", "conceptKeys": ["diffusion", "osmosis"] }
```

`400` with one of `{ "error": "title is required" }`, `{ "error": "title must be at most 300 characters" }`,
`{ "error": "collectionId is required" }`, `{ "error": "conceptKeys must not contain blank keys" }`,
`{ "error": "mode must be \"Text\" or \"Voice\"" }`,
`{ "error": "the collection has no concept map yet; process it first" }` (also for a collection that isn't
yours), `{ "error": "unknown concept keys", "unknown": ["mitosis"] }`, or a settings error.

### Assign a student

`POST /api/v1/exams/{id}/assignments`

```json
{
  "userName": "jsmith",
  "email": "jsmith@example.edu",
  "opensAt": "2026-12-01T09:00:00Z",
  "closesAt": "2026-12-08T17:00:00Z",
  "attemptsAllowed": 1
}
```

This assigns the exam to a student of your organization, creating the student if the user name is new:

- An unknown `userName` creates an account, adds it to your organization as a **Student**, and returns a
  **temporary password** once in the response. The student must change it at first sign-in.
- A `userName` that is already an active **Student** of your organization is assigned as is; its
  password is unchanged and `temporaryPassword` is `null`.
- The API never changes anyone's role and never pulls an existing account into your organization:
  - a member of your organization with another role (Owner, Admin, Instructor) answers `409`;
  - a deactivated member answers `409` (an admin reactivates them in the console first);
  - a user name that belongs to an existing Gadfly account that is **not** a member of your organization
    answers `409`. Accounts are shared across organizations, and membership is granted only by your
    organization's admins (an invitation by email from the Members page), so pick a distinct user name, for example one
    prefixed with your app or school.
- Assigning a student who already has this exam **updates** that assignment: `opensAt` and `closesAt` are
  set to what you send (left out or `null` means no limit), and `attemptsAllowed` is changed when you send
  it (left out keeps the current number). `created` tells you which happened.
- `opensAt` and `closesAt` are optional; `closesAt` must be after `opensAt`. `attemptsAllowed` defaults to 1
  for a new assignment and must be at least 1. `email` is used only when an account is created.

`200 OK`

```json
{
  "assignmentId": "c2f7e1a9-5d64-4b0e-9f3a-71b8d2c4e605",
  "studentUserId": "9e3b6a1d-2c4f-4e58-b7a0-d1f6c8e2a934",
  "created": true,
  "temporaryPassword": "Gf-q8Zk2xyP0LmA3rT9vB1sWdE4"
}
```

`404` when the exam doesn't exist or isn't published. `400` `{ "error": "userName is required" }`,
`{ "error": "attemptsAllowed must be at least 1" }`, `{ "error": "closesAt must be after opensAt" }`, or the
reason the account can't be created (for example a user name the sign-in library rejects). `409` with an
`error` for the cases above, for example
`{ "error": "the user name 'jsmith' belongs to an existing Gadfly account that is not a member of your organization; use a distinct user name (...)" }`
or `{ "error": "'jsmith' has the Instructor role in your organization; only students can be assigned exams, and the API never changes a member's role" }`.

### Launch link

`POST /api/v1/assignments/{id}/launch` (no body)

```json
{
  "url": "https://localhost:7311/launch?token=CfDJ8...",
  "expiresInSeconds": 900
}
```

`404` when the assignment isn't your organization's. See [the launch-link flow](#launch-link-flow).

### Attempts

`GET /api/v1/assignments/{id}/sessions`

```json
[
  {
    "id": "5a7d9c2e-3f1b-4e6a-8c0d-b2e4f6a8c1d3",
    "attempt": 1,
    "state": "Ended",
    "startedAt": "2026-12-02T14:03:11+00:00",
    "endedAt": "2026-12-02T14:19:47+00:00",
    "endReason": "Mastery"
  }
]
```

`state` is `Created` (opened, notice not yet accepted), `ConsentGiven`, `Running`, `Paused`, `Ended` or
`Failed`. `endReason` is `Mastery`, `Aporia`, `TimeUp`, `TurnLimit`, `Abandoned` (the student pressed
Finish) or `Failed`, and `null` while running. An empty array when there are none.

### Result

`GET /api/v1/sessions/{id}/result`

```json
{
  "id": "5a7d9c2e-3f1b-4e6a-8c0d-b2e4f6a8c1d3",
  "assignmentId": "c2f7e1a9-5d64-4b0e-9f3a-71b8d2c4e605",
  "studentUserId": "9e3b6a1d-2c4f-4e58-b7a0-d1f6c8e2a934",
  "state": "Ended",
  "startedAt": "2026-12-02T14:03:11+00:00",
  "endedAt": "2026-12-02T14:19:47+00:00",
  "endReason": "Mastery",
  "objectiveLevels": [
    { "title": "Explain osmosis", "level": 4, "label": "Application", "insufficient": false },
    { "title": "Predict water movement", "level": 0, "label": "Insufficient Evidence for Reliable Assessment", "insufficient": true }
  ],
  "recommendation": "mastered",
  "overallScore": 0.86,
  "report": {
    "sessionId": "5a7d9c2e-3f1b-4e6a-8c0d-b2e4f6a8c1d3",
    "subjectId": "...",
    "promptSetSha256": "...",
    "startedAt": "2026-12-02T14:03:11+00:00",
    "endedAt": "2026-12-02T14:19:47+00:00",
    "endReason": "Mastery",
    "recommendation": "mastered",
    "overallScore": 0.86,
    "concepts": [
      {
        "conceptId": "osmosis",
        "title": "Osmosis",
        "mastery": 0.84,
        "status": "settled-conceptual-or-above",
        "turns": 3,
        "averageScore": 0.81,
        "evidence": [
          {
            "turnIndex": 4,
            "move": "Apply",
            "question": "A cell is placed in very salty water. What happens to it, and why?",
            "quote": "Water leaves the cell because ...",
            "score": 0.9,
            "judgeSummary": "Correct direction and reason ...",
            "citations": [{ "chunkId": "1d2e3f40a5b6c7d8e9f0a1b2c3d4e5f6", "quote": "..." }]
          }
        ],
        "disputedTurns": []
      }
    ],
    "turnsJudged": 6,
    "turnsNeedingReview": 0
  },
  "grade": {
    "grade": "A",
    "score": 88,
    "comment": "Clear on osmosis; revisit active transport.",
    "decidedAt": "2026-12-03T10:12:00+00:00"
  }
}
```

| Field | Meaning |
|---|---|
| `objectiveLevels` | The AI report's result: the level the evidence supports for each learning objective, 1 to 5, with its label. Level 0 (`insufficient: true`) means the evidence was insufficient to assess reliably, never zero understanding. Empty until the attempt ends, and for reports written before learning objectives. Use this, not the two fields below, wherever you show an AI result. |
| `recommendation` | **Deprecated; not a score.** The examiner's running-estimate band (`mastered`, `partial`, `not-demonstrated`), which only steers which question comes next. Kept so existing integrations don't break; do not show it as the student's result. `null` until the attempt ends. |
| `overallScore` | **Deprecated; not a score.** The mean of the examiner's running estimate over the examined concepts, 0 to 1: a steering aid, never a grade. |
| `report` | The full AI report (per-concept mastery and status, quoted evidence with source citations, disputed turns), or `null` until the attempt ends. Concept `status` is `not-examined`, `not-started`, `open`, `settled-conceptual-or-above` (sufficient evidence at Level 3 or above), `settled-below-conceptual` (sufficient evidence below Level 3) or `insufficient-evidence` (GAD-T823); reports saved earlier may say `mastered`, `aporia` or `exhausted`. Like `mastery`, it describes the examiner's steering, not the result. |
| `grade` | The latest decision of the older single-grader review, or `null`. The graded result comes from the grading workflow (see "Grading workflow" below): AI grades are preliminary, and a final grade exists only after human grading and approval. |

#### When `recommendation` and `overallScore` go away

Both fields are deprecated from **V2**, the first release that carries `objectiveLevels` (GAD-T425). Gadfly itself no
longer uses them: since GAD-T460 the review pages' "(override)" flag and the student's released result compare with
the objective levels too. They stay in this result, in the top-level fields and inside `report`, and in the
`session.completed` webhook, unchanged until they are removed.

**Proposed removal: V4**, two releases after V2. *Proposed, not yet confirmed by the product owner.* Release numbers
count deployments (see [DEPLOY.md](DEPLOY.md#versions)), so the owner may instead set a date; this section will name
whichever is agreed, and the release notes of the version before the removal will repeat it. Until then:

- read `objectiveLevels` (and `insufficient` for Level 0) wherever you show an AI result;
- treat `recommendation` and `overallScore` as optional now: after the removal they are simply absent from the JSON,
  so code that ignores unknown or missing fields keeps working;
- there is no replacement for `overallScore`: no average stands in for the per-objective levels.

This endpoint returns the result whether or not the grade has been published to the student. Show
students only what your own release rules allow.

### Transcript

`GET /api/v1/sessions/{id}/transcript?format=json&audience=reviewer`

The attempt's official transcript, built from the response ledger (GAD-T295): every question with its clock time,
its offset from the start of the exam and the examiner's move; every answer exactly as recorded; the read-back and
its outcome; meaning clarifications; every transcription correction as original and corrected text; the response
delay and pauses; and the words the speech recognizer was unsure of.

| Parameter | Values |
|---|---|
| `format` | `json` (default), `pdf`, `docx`, `html` (print-ready page), `txt`, `vtt` (WebVTT captions), `srt` |
| `audience` | `reviewer` (default): everything, including integrity notes, voice measurements, the examiner's reason for each question and the AI panel's exam-time reading. `blind-grader`: none of those. `student`: none of those, and only once the result has been published to the student (`409` before). |

Every response carries the copy's `X-Transcript-Code` (the verification code, e.g. `7K3M-9QXA-B2CD-EFGH`) and
`X-Transcript-Sha256` headers, and every call is recorded in the audit log as `transcript.issued`. The JSON
download is:

```json
{
  "transcript": {
    "schema": "gadfly.transcript/1",
    "audience": "reviewer",
    "sessionId": "5a7d9c2e-3f1b-4e6a-8c0d-b2e4f6a8c1d3",
    "examTitle": "Cell biology oral",
    "studentName": "ada01",
    "attempt": 1,
    "mode": "Spoken",
    "turns": [
      {
        "number": 1,
        "concept": "Osmosis",
        "move": "Opening question",
        "moveKind": "Elicit",
        "askedAt": "2026-12-02T14:03:16+00:00",
        "offsetSeconds": 5,
        "question": "What is osmosis, in your own words?",
        "responses": [
          {
            "kind": "Answer",
            "modality": "Spoken",
            "asRecorded": "Water moves a cross a membrane.",
            "verified": "Water moves across a membrane.",
            "recordStatus": "Corrected by the student",
            "delaySeconds": 4,
            "pauses": 2,
            "readBack": { "prompt": "I recorded your answer as: ...", "outcome": "The student corrected the recording." },
            "corrections": [{ "version": 2, "original": "Water moves a cross a membrane.", "corrected": "Water moves across a membrane.", "by": "the student" }],
            "unsureWords": [{ "text": "a cross", "startSeconds": 1.1, "endSeconds": 1.6, "lowestConfidence": 0.4 }],
            "words": [{ "text": "Water", "startSeconds": 0, "endSeconds": 0.5 }]
          }
        ]
      }
    ],
    "leftOut": []
  },
  "verification": {
    "sha256": "3f0c...",
    "code": "7K3M-9QXA-B2CD-EFGH",
    "generatedAt": "2026-12-03T09:00:00+00:00",
    "verifyUrl": "https://exams.example.org/transcripts/verify?code=7K3M-9QXA-B2CD-EFGH"
  }
}
```

The hash is the SHA-256 of the canonical JSON of `transcript` (object keys in ordinal order, nulls left out, no
whitespace, UTF-8); the code is its first 80 bits in Crockford base 32. Nothing that changes between downloads is
inside `transcript`, so the same record always gives the same code. Anyone can check a code or the JSON file at
`/transcripts/verify` (no sign-in, rate-limited); it says only whether the copy matches the record.

The same page also recognizes a completion certificate's code (GAD-T311/GAD-T379: `/certificates/{sessionId}/pdf`),
built the same way (a canonical, deterministic JSON record of the certificate's content, hashed and coded exactly
like a transcript) and checked against a freshly-rebuilt certificate, so a later amendment to the grade shows as "the
record has changed since" rather than silently matching. There is no separate `/certificates/verify` page: a typed
code is checked against transcripts first, then certificates. A certificate has no JSON download, so only its code
can be checked, never an uploaded file.

### Webhook registration

`POST /api/v1/webhooks`

```json
{ "url": "https://lms.example.edu/hooks/gadfly", "events": ["session.completed"] }
```

`201 Created`, `Location: /api/v1/webhooks/{id}`

```json
{
  "id": "e8b4c1d6-9a2f-4b3e-8d7c-6f5a4e3d2c1b",
  "url": "https://lms.example.edu/hooks/gadfly",
  "events": ["session.completed"],
  "secret": "whsec_4f1c9a0e7b2d6c3f8a5e1b9d0c7f2a6e4b8d3c1f5a9e7b2d"
}
```

Store `secret`: it is shown only here. A runnable receiver that verifies the signature is
[samples/HostedApi](../samples/HostedApi). For local development only, a server running in the Development
environment accepts `https://localhost` and other private targets when `Gadfly:Webhooks:AllowPrivateTargets` is
`true`; production never does. `400` `{ "error": "Webhook URLs must be absolute https URLs." }`,
`{ "error": "Webhook URLs must be at most 1000 characters." }`,
`{ "error": "Webhook URLs must name a public host." }` (localhost, `.local`, `.internal` and single-label
names), `{ "error": "Webhook URLs must not point at private, loopback or link-local addresses." }` or
`{ "error": "Subscribe to at least one of: session.completed." }` (also when `events` is missing). Unknown
event names are ignored.

`GET /api/v1/webhooks`

```json
[
  {
    "id": "e8b4c1d6-9a2f-4b3e-8d7c-6f5a4e3d2c1b",
    "url": "https://lms.example.edu/hooks/gadfly",
    "events": ["session.completed"],
    "isActive": true,
    "createdAt": "2026-11-28T09:30:00+00:00"
  }
]
```

Oldest first, including deactivated webhooks (`isActive: false`). `events` is an array, as everywhere in
the API. Secrets are never listed.

`DELETE /api/v1/webhooks/{id}` deactivates the webhook: `204 No Content`, or `404`. Deliveries still
waiting for it are dropped.

## Launch-link flow

A launch link takes a student from your app straight to the exam's get-ready page.

1. Your app assigns the exam (`POST /api/v1/exams/{id}/assignments`) and keeps the `assignmentId`.
2. When the student clicks "Start exam" in your app, call `POST /api/v1/assignments/{assignmentId}/launch`
   and redirect the student's browser to the returned `url`. Ask for a fresh link each time: it is signed
   by the server and **expires after 15 minutes**.
3. Gadfly checks the link at `/launch`:
   - not signed in: the student is sent to the sign-in page and then on to the exam;
   - signed in as the student the link was made for: straight to the exam's get-ready page
     (`/my-exams/{assignmentId}/start`);
   - signed in as someone else: "This exam link belongs to a different student";
   - expired or altered: "This exam link has expired or is not valid. Ask for a new one."
4. The student runs the system check, accepts the notice and takes the exam.
5. When the attempt ends you receive `session.completed`, then read
   `GET /api/v1/sessions/{sessionId}/result`.

The student still signs in with their Gadfly user name and password; one-click sign-in from an API launch link
needs trusted-launch support in MindAttic.Authentication and is not built. For one-click launch from an LMS, use the
LTI 1.3 connection below, which signs the student in.

## LMS connection (LTI 1.3)

Gadfly is an LTI 1.3 / LTI Advantage tool (Core 1.3, Deep Linking 2.0, Names and Role Provisioning 2.0,
Assignment and Grade Services 2.0), set up per organization on **Organization → LMS connection** (`/org/lti`);
the step-by-step guide for Canvas, Moodle, Blackboard and Brightspace is docs/guides/lti-setup.md. These are the
addresses a platform calls. None takes an API key: each is checked on LTI's own terms.

| Endpoint | Called by | What it does |
|---|---|---|
| `GET` or `POST /lti/login` | the platform (OIDC third-party-initiated login) | Takes `iss`, `login_hint`, and optionally `client_id`, `lti_deployment_id`, `lti_message_hint`, `target_link_uri`. Finds the one enabled connection they name, records a single-use `state` and `nonce` (10 minutes), sets them in a `SameSite=None; Secure; Partitioned` cookie on `/lti`, and redirects to the platform's authorization endpoint (`response_type=id_token`, `response_mode=form_post`, `prompt=none`). An unknown or ambiguous platform gets a plain error page. |
| `POST /lti/launch` | the platform (form post of `id_token` and `state`) | The redirect URI, target link URI and deep-linking URL. Validates the launch (below), then: a deep-linking request shows instructors the exam picker; a resource-link request signs a student in and opens `/my-exams/{assignmentId}/start` (inside a frame it offers an "open in a new window" button instead); an instructor is told the placement is linked and pointed at the normal MFA sign-in. |
| `GET /lti/jwks.json` | the platform | Gadfly's public key set (`{"keys":[{"kty":"RSA","kid":…,"use":"sig","alg":"RS256","n":…,"e":…}]}`), for verifying the deep-linking response and the client assertion. Cached 5 minutes. |
| `POST /lti/deep-link/submit` | the picker page | Turns the chosen exam(s) into a signed `LtiDeepLinkingResponse` and posts it to the platform's `deep_link_return_url`. Carries a protected 30-minute continuation token, never a session. |
| `POST /lti/continue` | the "open in a new window" button | Exchanges a protected, single-use, two-minute hand-off token for the student's sign-in. Single use is recorded in the database, so it holds across server instances. |
| `POST /lti/link/send` | the "Email me a link to connect it" button | Emails a single-use, 24-hour link to an existing account's verified address and sets a cookie binding that link to this browser (see People and roles). |
| `GET`, `POST /lti/link` | the emailed link | `GET` only shows what will happen; the `POST` links the LMS identity to the account, when the token is valid and the browser has the matching cookie. |

**Launch validation.** A launch is accepted only when all of these hold: the `state` is one Gadfly issued, unused
and under 10 minutes old (it is consumed atomically, so a launch posts at most once); the browser presents the same
state in its cookie (so someone else's launch can't be posted into your browser); the `id_token` is a JWS signed with
**RS256** (never `none` or HMAC) by a key in the platform's JWKS (fetched over https, cached 10 minutes, re-fetched
once when the `kid` is unknown); `iss` is the connection's issuer; `aud` contains its client id and `azp`, when present
or when there are several audiences, equals it; `exp` and `iat` are present and current (60 seconds of skew); the
`nonce` matches the one issued with this state; the LTI version is `1.3.0`; the `deployment_id` is the connection's;
and a message type and `sub` are present. A launch that passes every check but arrives **without** the state cookie
(browsers that block every cookie inside a frame) is not accepted in that browser context: Gadfly answers with an
"Open in a new window" page whose button sends the platform's own `iss`, `client_id`, `lti_deployment_id`,
`login_hint`, `lti_message_hint` and `target_link_uri` to `/lti/login` in a new top-level window, where the cookie works.
A cookie that comes back with a different state is refused.

**People and roles.** A platform user (`sub`, per connection) maps to one Gadfly account, created on first sight with
the name and email the platform sent and a random password nobody is told. Course roles map as: Instructor, Teaching
Assistant, Content Developer and course Administrator → **Instructor**; Learner → **Student**; anything else is refused.
The LMS can move a student up to instructor, never down, never to Owner or Admin, and never restore a member an admin
removed. New members take a seat under the organization's plan. An LTI sign-in (`amr=lti`) never satisfies MFA, so it
only ever reaches student pages. When the platform's email equals exactly one active member's, no account is created
and nothing is linked until it is confirmed, by an admin on the LMS connection page or by the person through a link
emailed to that account's verified address (it works only in the browser that asked for it); never by the email alone.

**Deep linking.** Each exam chosen becomes an `ltiResourceLink` content item with `custom.examId` and a `lineItem`
(`scoreMaximum` 100, labelled with the exam title). The first launch of that placement records it.

**Roster (NRPS).** An admin syncs a placement's course roster from the LMS connection page, and each connection also
refreshes its rosters through the job queue every `RosterRefreshHours` (24 by default, 0 for on request only); Gadfly
reads every page of the membership container (following `Link: rel="next"`), skips inactive and deleted members, and
maps the rest exactly as a launch would.

**Grades (AGS).** Within about two minutes of a result being released to the student (finalized and published with
approval), Gadfly posts the latest attempt's final percentage to the placement's line item: `POST {lineitem}/scores`,
`application/vnd.ims.lis.v1.score+json`, `scoreGiven` 0 to 100, `scoreMaximum` 100, `activityProgress` `Completed`,
`gradingProgress` `FullyGraded`. If the launch gave only a line-items collection, Gadfly creates the line item first.
An amended grade is sent again; an unchanged one never is. Integrity notes are never sent. A send the platform refuses
or doesn't answer is retried after 2 minutes, then 4, 8 and so on (at most 12 hours apart), up to 8 tries; then it is
marked failed on the LMS connection page until an admin retries it or the grade changes. Service tokens come from the
platform's token endpoint with a signed RS256 client assertion (RFC 7523), audience the token URL or the connection's
token audience (Brightspace).

All outgoing calls (key set, token, roster, gradebook) go only to public https hosts, checked after DNS resolution,
without following redirects.

## Webhooks

Deliveries go only to public addresses: the host is resolved at each delivery and a name that
resolves to a private, loopback, link-local or reserved address is refused (no request is sent), and
redirects are not followed (a `3xx` answer counts as a failed attempt and is retried).

### Payload

Each delivery is a `POST` with a JSON body:

```json
{
  "type": "session.completed",
  "createdAt": "2026-12-02T14:19:48.1234567+00:00",
  "data": {
    "sessionId": "5a7d9c2e-3f1b-4e6a-8c0d-b2e4f6a8c1d3",
    "assignmentId": "c2f7e1a9-5d64-4b0e-9f3a-71b8d2c4e605",
    "studentUserId": "9e3b6a1d-2c4f-4e58-b7a0-d1f6c8e2a934",
    "examId": "a4e2d5c8-0b3f-4d91-8e7a-5c6b1f2e3d40",
    "endReason": "Mastery",
    "objectiveLevels": [{ "title": "Explain osmosis", "level": 4, "label": "Application", "insufficient": false }],
    "recommendation": "mastered",
    "overallScore": 0.86
  }
}
```

`session.completed` is sent once per attempt, when it ends for any reason. `objectiveLevels` is the AI's result
per learning objective; `recommendation` and `overallScore` are deprecated steering values, not a score, proposed for removal in V4 (see Result). The instructor's grade comes
later; read it from the result endpoint.

| Header | Value |
|---|---|
| `Content-Type` | `application/json; charset=utf-8` |
| `X-Gadfly-Event` | The event type, e.g. `session.completed` |
| `X-Gadfly-Delivery` | The delivery id. Retries of the same delivery carry the same id: use it to ignore duplicates. |
| `X-Gadfly-Signature` | `t=<unix seconds>,v1=<hex HMAC-SHA256>` |
| `User-Agent` | `Gadfly-Webhooks/1` |

Answer with any `2xx` within 15 seconds. Anything else (or no answer) is retried with growing waits
(15 seconds, doubling, at most an hour apart), up to 8 attempts in all.

### Verifying the signature

`v1` is the lowercase hex HMAC-SHA256 of the string `"<t>.<body>"`, keyed with the webhook's secret (the
whole `whsec_...` string, as UTF-8). `t` is the time the attempt was signed. To verify:

1. Read the **raw** request body, exactly as received, before any JSON parsing.
2. Split the header on `,` and each part on the first `=` to get `t` and `v1`.
3. Reject the request if `t` is more than five minutes from your clock.
4. Compute the HMAC over `t + "." + body` and compare it with `v1` in constant time.

C#:

```csharp
using System.Security.Cryptography;
using System.Text;

static bool VerifyGadflySignature(string secret, string header, string rawBody, int toleranceSeconds = 300)
{
    var fields = header.Split(',')
        .Select(p => p.Split('=', 2))
        .Where(p => p.Length == 2)
        .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
    if (!fields.TryGetValue("t", out var t) || !fields.TryGetValue("v1", out var v1)) return false;
    if (!long.TryParse(t, out var ts) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts) > toleranceSeconds) return false;

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{rawBody}"))).ToLowerInvariant();
    return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(v1));
}
```

Node.js:

```js
import crypto from "node:crypto";

export function verifyGadflySignature(secret, header, rawBody, toleranceSeconds = 300) {
  const fields = {};
  for (const part of header.split(",")) {
    const i = part.indexOf("=");
    if (i > 0) fields[part.slice(0, i).trim()] = part.slice(i + 1).trim();
  }
  const { t, v1 } = fields;
  if (!t || !v1 || Math.abs(Date.now() / 1000 - Number(t)) > toleranceSeconds) return false;

  const expected = crypto.createHmac("sha256", secret).update(`${t}.${rawBody}`, "utf8").digest("hex");
  const a = Buffer.from(expected, "utf8");
  const b = Buffer.from(v1, "utf8");
  return a.length === b.length && crypto.timingSafeEqual(a, b);
}

// Express: keep the raw body for verification.
// app.post("/hooks/gadfly", express.raw({ type: "application/json" }), (req, res) => {
//   const ok = verifyGadflySignature(process.env.GADFLY_WEBHOOK_SECRET, req.get("X-Gadfly-Signature"), req.body.toString("utf8"));
//   if (!ok) return res.sendStatus(401);
//   const event = JSON.parse(req.body.toString("utf8"));
//   res.sendStatus(204);
// });
```

## Errors

| Status | When | Body |
|---|---|---|
| `400 Bad Request` | A rule above isn't met | `{ "error": "<what to fix>" }`; unknown concept keys add `"unknown": [...]` |
| `400 Bad Request` | The body isn't valid JSON or is missing | No JSON body |
| `401 Unauthorized` | Missing, unknown or revoked key; deactivated organization | Empty |
| `404 Not Found` | The id doesn't exist in your organization | Empty |
| `409 Conflict` | The request clashes with current state: processing already queued or running; assigning a non-student, a deactivated member, or an account outside your organization | `{ "error": "<what to do>" }` |
| `429 Too Many Requests` | More than the rate limit (below) | Empty; `Retry-After` gives the seconds to wait |
| `500 Internal Server Error` | An unexpected server error | No stable shape |

Every id is checked against your organization, so another organization's ids answer `404`, never their
data. There is no pagination. Each API key may make 120 calls a minute, in bursts of up to 60; past that
the API answers `429` with `Retry-After`. Launch links are limited to 30 a minute per IP address.

## OpenAPI

The OpenAPI document is served anonymously at `/openapi/v1.json` (for example
`https://localhost:7311/openapi/v1.json`). It lists every `/api/v1` route with its request body and its
response bodies as named schemas (`CollectionDetail`, `JobStatus`, `ExamCreated`, `AssignmentResult`,
`WebhookView`, `ApiError` and so on). It declares the API key as an HTTP bearer security scheme named
`ApiKey` (`components.securitySchemes.ApiKey`) and requires it on every `/api/v1` operation, so generated
clients send `Authorization: Bearer gfk_...`. The result endpoint is not given a named schema; its shape is
documented [above](#result). `/launch` is a browser page and is not in the document.

## Grading workflow

The human grading workflow (master plan Parts 8-11) can be driven over HTTP as well as from the Grading pages:
blind human grades, reconciliation, approval, publication, appeals and amendments, plus a status read. These are
**not** `/api/v1` calls: each one is an accountable act by a person, so they take the **signed-in cookie** of a
member of the organization (the API key is not accepted) and act in that member's role. The demo tour
(docs/TOUR.md) drives the whole workflow through them; an integrator can use them on behalf of a person who has
signed in.

Every rule is the grading service's, the same one the pages use: the role permission from the permission matrix
(Part 16.2), with every refused attempt written to the audit log as `access.denied`; the grade-status machine (an
invalid move is refused and audited as `grade.transition.refused`); the blind rule; and an audit event for every
change (`grade.human.recorded`, `grade.disagreement.detected`, `grade.reconciled`, `grade.approved`,
`grade.published`, `grade.appeal.opened`, `grade.amended`, and so on).

### Antiforgery

Every `POST` needs the antiforgery request token in the `RequestVerificationToken` header, sent with the cookies of
the same signed-in session:

```
GET /grading/actions/antiforgery
→ 200 { "headerName": "RequestVerificationToken", "token": "CfDJ8..." }
```

A missing or wrong token answers `400` with `{ "ok": false, "message": "Missing or invalid antiforgery token: ..." }`.

### Actions

Every path below starts with `/grading/actions/{sessionId}`. Bodies are JSON. Each action answers
`{ "ok": true|false, "message": "<what happened, in words>", "status": "<grade status after the call>" }`.

| Method and path | Who (permission) | Body | Effect |
|---|---|---|---|
| `POST .../ai-grading` | Examiner, Teacher, Admin, Owner | `{}` | Starts (or retries) AI grading of an ended session |
| `POST .../human-grades` | Examiner, Teacher (EnterHumanScore) | `{ "grades": [ { "objectiveKey" or "objectiveId", "level", "score", "comment"?, "evidenceNote"? } ] }` | Human Review Pending → Reconciliation Required or Awaiting Final Approval. One grade per objective, every objective required; earlier grades are kept and superseded |
| `POST .../human-grades/reopen` | Examiner, Teacher | `{ "reason" }` | Back to Human Review Pending, to adjust the human grades |
| `POST .../second-review` | Teacher, Admin, Owner, Second Reviewer (ReconcileDisagreement) | `{ "disagreementId", "reviewerUserId" }` | Asks a member holding the Second Reviewer role to reconcile |
| `POST .../reconciliations` | ReconcileDisagreement or ActAsSecondReviewer | `{ "disagreementId", "cause", "resolution", "resolvedAiScore"?, "resolvedHumanScore"?, "secondReviewerUserId"? }` | Documents the resolution and recalculates; Awaiting Final Approval once no mandatory disagreement is open. `cause` is one of `error-in-ai`, `error-in-human`, `transcript-ambiguity`, `rubric-interpretation`, `both-defensible`. A second reviewer who acts is recorded as the second reviewer. Refused (`409`) for the person who entered this grade's human score (separation of duties) |
| `POST .../approve` | Teacher, Admin, Owner (ApproveFinalGrade) | `{ "label"?, "note"? }` | Awaiting Final Approval → Finalized, with a Finalize approval record. Refused (`409`) while a mandatory disagreement is open, and for the person who entered this grade's human score (separation of duties) |
| `POST .../publish` | Teacher, Admin, Owner (PublishGrade) | `{ "note"? }` | Finalized → Published, with a Publish record. Refused (`409`) without a Finalize record |
| `POST .../appeals` | The student whose published result it is (AppealOwnResult) | `{ "reason" }` | Published or Amended → Under Appeal |
| `POST .../appeals/{appealId}/resolve` | Teacher, Admin, Owner (ResolveAppeal) | `{ "outcome": "Upheld" or "Dismissed", "resolution" }` | The grade stands: back to Published |
| `POST .../amend` | Teacher, Admin, Owner (AmendGrade, plus ResolveAppeal with `appealId`) | `{ "grades": [ changed objectives ], "reason", "appealId"? }` | Published or Under Appeal → Amended: new human grades supersede the old ones, the combined score is recalculated under the frozen policy, and the appeal (if given) is resolved as Amended. While an appeal is open, `appealId` is required: an amendment that doesn't name it is refused, so the appeal is never left open behind an amended grade |

**Separation of duties (GAD-T285).** Reconciliation and approval are refused (`409`, `"Refused: you entered this
grade's human score, ..."`, audited as `grade.separation-of-duties.refused`) when the caller entered any human
score of that session's grade. An organization that has turned on **Allow a single grader** in its settings (off
by default) lets them through, and each one is audited as `grade.separation-of-duties.waived`. The status read
says where the caller stands in `separationOfDuties`.

`level` is a mastery level by name or number: `InsufficientEvidence` (0), `Recognition` (1), `BasicExplanation` (2),
`ConceptualUnderstanding` (3), `Application` (4), `TransferAndCriticalReasoning` (5). `score` is on the objective's
own scale (0 to its `maxScore`).

| Status | When |
|---|---|
| `200` | Done (`ok: true`) |
| `400` | A missing or invalid field, an unknown objective or level, a missing antiforgery token, or another rule of the action (the message says which) |
| `403` | The caller's role lacks the permission (audited as `access.denied`), or a student appeals a result that isn't their own published one |
| `404` | (status read) The session isn't in the organization, or a student's result isn't published yet |
| `409` | The grade-status machine refused the move (`"Refused: ..."`, audited as `grade.transition.refused`), or separation of duties did (audited as `grade.separation-of-duties.refused`) |

Example: the examiner submits blind grades.

```
POST /grading/actions/6b72ef45-.../human-grades
RequestVerificationToken: CfDJ8...
Content-Type: application/json

{ "grades": [
    { "objectiveKey": "equal-groups", "level": "BasicExplanation", "score": 4, "comment": "Repeated addition only." },
    { "objectiveKey": "factor-roles", "level": "BasicExplanation", "score": 5 },
    { "objectiveKey": "scaling", "level": 2, "score": 5 } ] }

→ 200 { "ok": true, "status": "ReconciliationRequired",
        "message": "Human grades saved. The AI and human scores disagree substantially: reconciliation is required before this grade can be finalized." }
```

### Status

`GET /grading/actions/{sessionId}`, for a reviewer (ViewTranscripts):

```json
{
  "sessionId": "6b72ef45-...", "examTitle": "Demo: Understanding multiplication", "student": "demo.student", "attempt": 1,
  "status": "ReconciliationRequired", "statusLabel": "Reconciliation Required",
  "aiVisible": true, "isMock": true, "finalPercent": 46.67, "label": null,
  "separationOfDuties": { "youScoredThisGrade": false, "singleGraderAllowed": false, "blocksYou": false },
  "policy": { "aiWeight": 0.4, "humanWeight": 0.6, "additionalReviewAt": 10, "mandatoryReconciliationAt": 20, "blindHumanGrading": true, "source": "exam-policy" },
  "aiGradingJob": { "state": "Succeeded", "error": null },
  "responsesNeedingHuman": 0,
  "objectives": [ {
    "id": "...", "key": "equal-groups", "title": "...", "maxScore": 10, "weight": 1, "aiRecorded": true,
    "ai": { "level": "Application", "levelNumber": 4, "score": 8, "maxScore": 10, "confidence": "...", "isMock": true, "explanation": "[MOCK] ...", "excluded": false },
    "human": { "id": "...", "level": "BasicExplanation", "levelNumber": 2, "score": 4, "maxScore": 10, "wasBlind": true, "reviewer": "demo.examiner",
               "reviewerRole": "Examiner", "comment": "...", "submittedAt": "...", "supersedes": null },
    "humanHistory": 1,
    "combined": { "aiScore": 8, "humanScore": 4, "combined": 5.6, "differencePoints": 40, "band": "MandatoryReconciliation" } } ],
  "disagreements": [ { "id": "...", "objectiveId": "...", "objectiveKey": "equal-groups", "aiScore": 8, "humanScore": 4, "differencePoints": 40,
                       "band": "MandatoryReconciliation", "resolved": false, "resolvedAt": null, "needsReconciliation": true, "reconciliations": [] } ],
  "approvals": [ { "action": "Finalize", "actor": "demo.teacher", "role": "Instructor", "note": "...", "at": "..." } ],
  "appeals": [ { "id": "...", "state": "Open", "reason": "...", "resolution": null, "openedAt": "...", "resolvedAt": null } ],
  "secondReviewers": [ { "userId": "...", "name": "demo.reviewer" } ]
}
```

**Blind grading.** While the policy is blind and the grade has not passed the human evaluation (Human Review
Pending or earlier), `aiVisible` is `false` and nothing derived from the AI is returned: every objective's `ai` and
`combined` are `null`, `finalPercent` is `null`, `disagreements` is empty and `isMock` is `false`. `aiRecorded` says
only that an AI grade exists. Once the human grades are submitted, reviewers see the AI's evaluation.

A **student** asking for their own session gets `404` until the grade is approved *and* published (the attempt is
audited as `access.denied`). After that they get their result as the student's assessment report shows it, without
reviewer names, voters or audit detail:

```json
{ "sessionId": "...", "examTitle": "...", "attempt": 1, "status": "Published", "statusLabel": "Published",
  "finalPercent": 60, "label": "Tour", "isMock": true,
  "objectives": [ { "key": "...", "title": "...", "maxScore": 10, "aiScore": 8, "aiLevel": "...", "humanScore": 8, "humanLevel": "...", "combined": 8 } ],
  "knowledgeGaps": [ "..." ], "approvals": [ "2026-11-20 20:03 UTC · Final grade approved by Teacher" ],
  "appeals": [ { "id": "...", "state": "Open", "resolution": null, "openedAt": "...", "resolvedAt": null } ] }
```

Other members without a reviewer permission get `403` (audited). The per-objective assessment report downloads at
`/grading/{sessionId}/report.md`, `/grading/{sessionId}/report.csv` and `/grading/{sessionId}/report.pdf` (reviewers)
and `/my-results/{sessionId}/report.md` and `/my-results/{sessionId}/report.pdf` (the student, once published). The
PDF (GAD-T311/GAD-T377) says the same thing in the same order as the Markdown (per objective: the AI and examiner
levels, what was missing, the next level's descriptor), laid out with the same hand-written PDF engine the transcript
downloads use (GAD-T295) rather than a new PDF dependency.

`GET /grading/exam/{examId}/class-summary.pdf` (reviewers, ViewTranscripts; GAD-T311/GAD-T378): a class summary PDF
for instructors, aggregated over every student assigned to that exam — score distribution (mean, median, a 10-point
histogram), each learning objective's class average and who hasn't completed the exam yet. Only released results
(published to the student) feed the distribution and averages; an attempt that has ended but is still awaiting
grading or release is counted separately and never named, and a student with no ended attempt is listed by name so
the instructor can follow up.

`GET /certificates/{sessionId}/pdf` (GAD-T311/GAD-T379): a student's completion certificate once their result is
released (`409` with a plain-English reason before then; the same entitlement check as the transcript download, so a
session id from elsewhere looks like `404` either way). Carries its own verification code and SHA-256, checked at the
transcript's own verify page (next section) rather than a separate one.

### Reconciliation triggers

Besides the AI-human difference bands, an objective goes to reconciliation (band `MandatoryReconciliation`, status
`ReconciliationRequired`) when its panel is split (members' rubric levels differ by more than
`maxPanelSpreadLevels`, default 1) or its score sits on a boundary (the AI and human scores fall on opposite sides of
one of `scoreBoundaries`, default 50% of the objective's maximum, or the combined score is within
`boundaryMarginPoints`, default 2.5, of one). An objective already reconciled once is not sent again by these
triggers. The `grade.disagreement.detected` audit event lists the triggers that fired (GAD-T270; research note 05).

### Research export

`GET /admin/research/export.zip[?exam={examId}]` (owners and admins, `GadflyPolicies.OrgAdmin`; GAD-T451) returns a
ZIP of CSV files for a study of the organization's exams (docs/PILOT.md, "Research export", lists every file and
column), with `README.txt` and `manifest.json` (counts, the smallest group used, what was held back). `404` with a
plain reason when research has never been set up on **Research** (`/org/research`).

- Only students whose current research choice is "agreed" are included (GAD-T452). Declined, never answered, withdrawn
  and erased students are left out and only counted in the manifest.
- No name, email, user name, answer or question text, audio, video, free-text comment, appeal text or database id.
  Ids become keyed pseudonyms (`S-` student, `X-` session, `E-` exam, `O-` objective, `R-` rater, `A-` arm;
  HMAC-SHA256 with the organization's research key, stable across exports until the key is replaced). Times are
  relative to the session.
- Voice measurements are blank wherever they were off, and always for an EU/EEA organization or student.
- An exam with fewer consenting students than the organization's smallest group (default 5) is held back; a rarer
  accommodation within an exam is written `suppressed`.
- Each download is audited as `research.exported` (who, which exam, counts; no data).

### Panel calibration

A grading panel is accepted for an exam only after a blind, double-marked calibration set meets the thresholds
(Williamson et al. 2012, as reported in research note 05): panel-human QWK at least .70, no more than .10 below the
human-human QWK, and |SMD| under .15, on at least 30 answers. The panel is compared with each marker and the weaker
comparison counts. Levels are the rubric's mastery levels 0..5.

`POST /grading/calibration/{examId}` (ApproveGradingPolicyChange; antiforgery token as above):

```json
{ "panel": "claude-sonnet-5, gpt-5, gemini-3 (majority of levels)",
  "items": [ { "itemId": "s01-equal-groups", "panelLevel": 3, "humanALevel": 3, "humanBLevel": 2 } ] }

→ 200 { "ok": false, "accepted": false, "message": "Panel not accepted for this exam. ...",
        "report": { "items": 12, "humanHumanQwk": 0.81, "panelHumanQwk": 0.66, "degradation": 0.15, "smd": 0.21,
                    "accepted": false, "failures": [ "..." ], "thresholds": { "minQwk": 0.7, "maxDegradation": 0.1, "maxAbsSmd": 0.15, "minItems": 30 } } }
```

`panel` is optional: it defaults to the configured members and their models. The model id of every member is stored
with the evaluation (GAD-T429), and so is the grading prompt-set version (GAD-T469). A calibration of the mock panel is never accepted.

Every evaluation, passing or failing, is recorded as an audit event (`panel.calibration.evaluated`), and the latest
one is the exam's status: `GET /grading/calibration/{examId}` (ViewTranscripts) returns it, or `404` when none is
recorded:

```json
{ "examId": "…", "panel": "claude: claude-sonnet-5, openai: gpt-5", "accepted": true, "report": { … }, "at": "…",
  "models": { "claude": "claude-sonnet-5", "openai": "gpt-5" }, "gradingPromptVersion": 1, "source": "double-marking",
  "expired": false, "expiryReasons": [], "recheckDue": false, "effective": true }
```

`expired` is true when any panel member's model id now differs from those the calibration covered (a member swapped,
added or removed, or a calibration recorded before model ids were kept), or when the grading prompts' version differs
from `gradingPromptVersion` (GAD-T469; a calibration recorded before versions were kept counts as version 1);
`expiryReasons` says which. `effective` is
`accepted && !expired`. `recheckDue` is true once the calibration is older than `Gadfly:Grading:RecalibrateAfterDays`
(default 180): a prompt to re-run it and the bias probes; nothing re-runs by itself. `source` is `api` (posted here) or
`double-marking` (built in the app from blind second marks, GAD-T428; only answers whose AI grade came from the models the
panel runs now and the current grading prompt-set version count, GAD-T480/GAD-T484 — every AI grade records its version, and
grades made before versions were kept count as version 1).

When AI grading of a session starts, the status is frozen into its policy snapshot (`panel`, with `expired`). An exam
set to **human-only until calibrated** (GAD-T427) also freezes `gate`: `{ "applied": true, "reason": "…" }` when the
session is graded with AI weight 0 and human weight 1 because the calibration is missing, not accepted or expired. The
grading page describes what the calibration measured, never just "calibrated". The AI grade is never final either way.
Double marking, the gate setting, bias probes and the self-preference check are pages, not API endpoints
(`/grading/panel`, `/grading/second-mark/{examId}`, `/grading/bias/{examId}`, `/grading/self-preference/{examId}`);
their records are audit events (`panel.settings.updated`, `grade.second-mark.recorded`, `panel.bias-probe.*`,
`bias.human-probe.*`) readable through the audit log.
