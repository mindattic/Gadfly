# Platform API v1

The Gadfly platform API lets another application use Gadfly's Socratic exams: upload source material,
have it processed into concepts, create exams, provision and assign students, send each student a signed
launch link into the exam room, read results, and receive a signed webhook when an attempt ends.

Everything is under `/api/v1`, speaks JSON (camelCase), and acts inside one organization: the one that
owns the API key. A machine-readable description is at [`/openapi/v1.json`](#openapi).

> Voice answers, the avatar and webcam signals are not built yet; exams created through the API run in
> the text exam room. Production hosting on Azure is pending; the examples use a local server.

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

There is no screen or endpoint to create keys yet; the organization admin console will add one. Until
then MindAttic staff issue keys. See the [Admin guide](guides/ADMIN_GUIDE.md#api-keys).

## Endpoints

| Method | Path | Does |
|---|---|---|
| POST | `/api/v1/collections` | Create a source collection |
| POST | `/api/v1/collections/{id}/documents` | Upload documents (multipart) |
| POST | `/api/v1/collections/{id}/process` | Queue processing |
| GET | `/api/v1/jobs/{id}` | Processing job status |
| GET | `/api/v1/collections/{id}/concepts` | The collection's concept map |
| POST | `/api/v1/exams` | Create and publish an exam |
| POST | `/api/v1/exams/{id}/assignments` | Provision a student and assign the exam |
| POST | `/api/v1/assignments/{id}/launch` | A signed launch link for the student |
| GET | `/api/v1/assignments/{id}/sessions` | The student's attempts |
| GET | `/api/v1/sessions/{id}/result` | One attempt's result and report |
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

`400` `{ "error": "title is required" }` when the title is blank.

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

- Each file must be 1 byte to 20 MB. Keep each request under the server's request size limit (about
  28 MB by default); send large sets in several requests.
- File names are cleaned (characters not allowed in file names become `_`). A file with the same name
  as one already uploaded replaces it.
- Only `.txt`, `.text`, `.md` and `.markdown` files are read when processing; other files are stored but
  skipped. PDF, DOCX, EPUB and HTML arrive with Tutor.Ingestion.

`404` when the collection isn't in your organization. `400` `{ "error": "send multipart/form-data with one or more files" }`
or `{ "error": "<file>: files must be 1 byte to 20 MB" }`.

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
| `title` | required | |
| `conceptKeys` | every current concept | In the order they should be examined. |
| `minutes` | 20 | Clamped to 1–240. |
| `difficulty` | 3 | Clamped to 1–5 (gentle to rigorous). |
| `maxTurns` | 30 | Questions in the whole exam, clamped to 1–200. |
| `mode` | `"Text"` | `"Text"` or `"Voice"`. Voice is stored; students type until voice mode is built. |
| `instructions` | none | Shown to students. |

The exam is **published immediately**, so its settings are frozen. Each concept gets up to six
questions; integrity monitoring is Standard.

`201 Created`, `Location: /api/v1/exams/{id}`

```json
{ "id": "a4e2d5c8-0b3f-4d91-8e7a-5c6b1f2e3d40", "conceptKeys": ["diffusion", "osmosis"] }
```

`400` `{ "error": "the collection has no concept map yet; process it first" }`,
`{ "error": "unknown concept keys", "unknown": ["mitosis"] }`, or a settings error.

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

This provisions the student and assigns the exam in one call:

- An unknown `userName` creates an account with a **temporary password**, returned once in the
  response. The student must change it at first sign-in.
- A `userName` that already exists is added to your organization as a **Student** (an existing member of
  your organization becomes a Student). Its password is unchanged and `temporaryPassword` is `null`.
- If the student already has this exam, the existing assignment is returned unchanged.
- `opensAt` and `closesAt` are optional; `attemptsAllowed` defaults to 1 (at least 1).

`200 OK`

```json
{
  "assignmentId": "c2f7e1a9-5d64-4b0e-9f3a-71b8d2c4e605",
  "studentUserId": "9e3b6a1d-2c4f-4e58-b7a0-d1f6c8e2a934",
  "temporaryPassword": "Gf-q8Zk2xyP0LmA3rT9vB1sWdE4"
}
```

`404` when the exam doesn't exist or isn't published. `400` `{ "error": "..." }` when the account can't be
created (for example a user name the sign-in library rejects).

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
        "status": "Mastered",
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
| `recommendation` | The AI's recommendation: `mastered` (every concept reached 80% mastery), `partial` (average at least 52.5%) or `not-demonstrated`. `null` until the attempt ends. |
| `overallScore` | Average mastery over the examined concepts, 0 to 1. |
| `report` | The full AI report (per-concept mastery and status, quoted evidence with source citations, disputed turns), or `null` until the attempt ends. Concept `status` is `NotStarted`, `Open`, `Mastered`, `Aporia` or `Exhausted`. |
| `grade` | The instructor's latest decision, or `null`. The instructor decides the grade; the AI only recommends. |

This endpoint returns the result whether or not the instructor has released it to the student. Show
students only what your own release rules allow.

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

Store `secret`: it is shown only here. `400` `{ "error": "Webhook URLs must be absolute https URLs." }` or
`{ "error": "Subscribe to at least one of: session.completed." }`. Unknown event names are ignored.

`GET /api/v1/webhooks`

```json
[
  {
    "id": "e8b4c1d6-9a2f-4b3e-8d7c-6f5a4e3d2c1b",
    "url": "https://lms.example.edu/hooks/gadfly",
    "events": "session.completed",
    "isActive": true,
    "createdAt": "2026-11-28T09:30:00+00:00"
  }
]
```

Here `events` is a comma-separated string. Secrets are never listed.

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

The student still signs in with their Gadfly user name and password; one-click sign-in from the link
needs trusted-launch support in MindAttic.Authentication and is not built yet.

## Webhooks

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
    "recommendation": "mastered",
    "overallScore": 0.86
  }
}
```

`session.completed` is sent once per attempt, when it ends for any reason. The instructor's grade comes
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
| `500 Internal Server Error` | An unexpected server error | No stable shape |

Every id is checked against your organization, so another organization's ids answer `404`, never their
data. There is no pagination and no rate limit yet.

## OpenAPI

The OpenAPI document is served anonymously at `/openapi/v1.json` (for example
`https://localhost:7311/openapi/v1.json`). It lists every route and request body; this page documents
the response bodies, which the generated document doesn't describe in detail.
