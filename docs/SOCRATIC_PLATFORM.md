# Socratic Exam Platform: plan

Status: the plan the platform was built from (GAD-T25); what is built is in
[MASTER-PLAN-STATUS.md](MASTER-PLAN-STATUS.md). Owner's stack decision 2026-11-20: Blazor Server, EF Core over SQL Server,
LocalDB first, Azure later.

## The idea

Erin's idea: a student proves they understand a subject by being **questioned out loud, Socrates-style**,
by an examiner that follows up on what they just said. A written exam can be answered by pasting into an AI.
A live spoken cross-examination is far harder to fake:

- Each follow-up question is generated from **the student's own previous answer**, so it can't be
  prepared or looked up in advance.
- Answers are **spoken**, in real time. There is no text box to paste into, and the response latency, pauses
  and cadence are measured.
- The examiner keeps a **ledger of the student's claims** and probes contradictions (the *elenchus*). Someone
  relaying another source's answers tends to contradict themselves.
- **Webcam and audio signals** (another face, a second voice, sustained reading of an off-screen
  region, loss of focus) are recorded as evidence for a human reviewer. They never fail a student
  automatically.
- Every answer is **judged against the instructor's own source material** by a panel of LLMs
  (MindAttic.Legion), with citations. One model's opinion is never treated as the verdict.

## What gets built (components)

```text
                    +---------------------------------------------------------------+
                    |  Gadfly.Exam  (Blazor Server web app)                         |
                    |  customer portal . instructor studio . student exam room      |
                    |  review timeline . org admin                                   |
                    +----+----------------+-----------------+----------------+-------+
                         | SignalR        | uses            | uses           | uses
                         v                v                 v                v
 +---------------------------+  +------------------+  +---------------+  +----------------------+
 | Gadfly.Server             |  | MindAttic.       |  | MindAttic.    |  | MindAttic.Webcam     |
 | EF Core / SQL Server      |  | SpeechRecognition|  | Avatar        |  |  core: camera, face  |
 | tenancy, SessionHub,      |  | mic capture, VAD |  | TalkingHead + |  |  modules: presence,  |
 | platform API, jobs,       |  | STT providers,   |  | ElevenLabs    |  |  head pose, gaze     |
 | metering                  |  | speech metrics   |  | TTS lip sync  |  |  region, eyetracking |
 +-------------+-------------+  +------------------+  +---------------+  +----------------------+
               | runs
               v
 +---------------------------------------------+     +-------------------------------+
 | Gadfly.Core  (the Socratic engine)          |---->| MindAttic.Legion              |
 | session . moves . claim ledger . planner    |     | multi-LLM questions + judging |
 | question generator . judge . report         |     +-------------------------------+
 | ISubjectMatter: SourceMaterial | Personal   |
 +----------------------+----------------------+     +-------------------------------+
                        | grounding                  | MindAttic.Authentication      |
                        v                            | orgs, memberships, roles,     |
 +---------------------------------------------+     | invitations, MFA              |
 | Gadfly.Sources  (via Tutor.Ingestion)       |     +-------------------------------+
 | parse . chunk . embed . concept map . rubric|
 +---------------------------------------------+
```

| Component | Repo | Status |
|---|---|---|
| Gadfly.Core: the general Socratic engine | Gadfly | refactor of today's Gadfly |
| Gadfly.Personal: today's introspection over your own transcripts | Gadfly | existing code, moved |
| Gadfly.Sources: source material ingestion, concept map, rubric | Gadfly | new |
| Gadfly.Server: hosted platform, SQL Server, API, SessionHub | Gadfly | new |
| Gadfly.Exam: the Blazor Server exam product | Gadfly (D1) | new |
| MindAttic.SpeechRecognition | new repo (created 2026-11-20, private) | new |
| MindAttic.Avatar: talking head with lip sync | new repo (created 2026-11-20, private) | new |
| MindAttic.Webcam: core and modules, with eye tracking as one module | MindAttic.Webcam (formerly MindAttic.EyeTracker, renamed in place) | restructure |
| Organizations, memberships, invitations | MindAttic.Authentication | extension |
| Tutor.Ingestion: shared parsers, chunking, embeddings | Tutor | extraction |
| Grounded judging: citations, per-voter verdicts, usage | MindAttic.Legion | extension |

## Stack

- **.NET 10**, ASP.NET Core, **Blazor Server** (interactive server rendering), the same stack as Tutor.
- **EF Core** on **SQL Server**: LocalDB in development, **Azure SQL** + **Azure App Service** later.
  Migrations from day one, so the Azure move is a connection-string change.
- **SignalR** for the live session: audio up, transcripts, avatar speech and integrity events.
- **MindAttic.Authentication** (Argon2id, TOTP MFA, audit) extended with organizations.
- **MindAttic.Legion** for every LLM call that matters: question fusion, judging, concept extraction.
- **ElevenLabs** (already paid for): TTS for the examiner's voice. Scribe for speech-to-text is the default
  STT provider, with OpenAI/Whisper as fallback. Keys stay in MindAttic.Vault on the server and never
  reach the browser.
- **TalkingHead** (met4citizen/TalkingHead, MIT, three.js): browser 3D avatar with lip sync from word
  timings. ElevenLabs' `with-timestamps` TTS returns exactly the alignment it needs.
- **MediaPipe Tasks Vision** (used by MindAttic.Webcam): the face landmarks behind presence, head pose and
  gaze.
- **MindAttic.Components**: theme and controls. **MindAttic.Log / Telemetry**: observability.
- **MindAttic.Deploy**: deployment.

## The Socratic engine (how an exam session works)

1. **Grounding.** The instructor uploads source material (PDF, DOCX, EPUB, HTML, text). It is parsed,
   chunked and embedded. Legion extracts a **concept map** (concepts, definitions, relationships, key facts,
   common misconceptions). For each concept it then builds a **rubric** (the points a real understanding
   covers, acceptable variants, misconceptions to watch for), each point citing the chunks it came from.
   The instructor reviews and edits both.
2. **Session.** The planner picks a concept and a **move**:

   | Move | Purpose |
   |---|---|
   | Elicit | "What is X? In your own words." |
   | Clarify | Pin down a vague term the student used. |
   | Probe | "Why does that follow?" / "What would happen if…" |
   | Counterexample | Offer a case that breaks the student's definition. |
   | Contradiction | Confront the student with two of their own claims that conflict. |
   | Apply / Transfer | A new situation that needs the concept rather than its definition. |
   | Synthesize | Connect two concepts. |
   | Close | End the concept: mastery shown, aporia reached, or the time budget spent. |

3. **Each turn**: the student's spoken answer → transcript plus speech metrics → **claim extraction**
   (the claim ledger) → **judge** (Legion panel: accuracy against the source, depth of reasoning, consistency
   with earlier claims, completeness; each criterion cites chunks; voters can dissent) → **mastery update** for
   the concept → the **question generator** (Legion drafts, a judge fuses the sharpest question), grounded in
   the concept's chunks, the conversation and the ledger, and never revealing the answer.
4. **Report.** Per-concept mastery, overall recommendation, quoted evidence with citations, judge dissent,
   integrity summary. **The instructor finalizes the grade** (D13). The AI recommends.

Prompt sets are versioned constants. Each session records the SHA-256 of the prompt set it ran, for audit.
Gadfly's three existing irrevocable prompts (Interrogation, Reflection, Master) are untouched: they become
the prompt set for the Personal subject. The exam mode gets its own prompt set.

**Text-only first**: `gadfly exam --source <folder>` runs the whole engine in the terminal before any web or
voice work exists. Erin can try the questioning itself early (milestone 1).

## Eye movement: what it can and can't tell you

Short answer: **looking up or away is a well-supported sign of *thinking*, not of lying.** Eye movement
can't detect lies.

- The "eye-accessing cues" idea (looking up-right means constructing a lie, up-left means remembering)
  comes from Neuro-Linguistic Programming. It was tested directly (Wiseman et al., 2012, *PLoS ONE*) and
  **found no effect**. No reliable gaze-based lie detector exists.
- **Gaze aversion during hard thinking is real.** People look away from faces and screens to reduce visual
  load while retrieving or reasoning (Glenberg et al., 1998; Doherty-Sneddon's work with children, where
  averting gaze *improved* answers). So a student looking up before answering is probably doing exactly
  what we want.
- **Pupil dilation** tracks cognitive load, but a consumer webcam can't measure it reliably.
- **What a webcam *can* usefully show for exam integrity**: no face, a second face, a different person
  partway through, and, most useful, **sustained fixation on one off-screen region with a reading pattern**
  (repeated left-to-right sweeps), which suggests reading from another screen or notes.
- **Legal**: the EU AI Act (Art. 5(1)(f)) **prohibits emotion recognition in education**, and it lists
  AI that evaluates learning outcomes or monitors students during tests as **high-risk** (Annex III). Face
  geometry is biometric data under Illinois BIPA, which requires written consent and a retention policy.

**So the platform will:** annotate "looked away while thinking" as normal, flag only behavioral integrity
signals (faces, voices, off-screen reading, focus loss), show them to the instructor as evidence with
explanations, and **never infer deception or emotion, and never auto-fail anyone** (D2).

## Anti-cheating layers

| Layer | Signal | Where |
|---|---|---|
| Format | Spoken only; adaptive follow-ups from the student's own words; server-held timer | Gadfly.Core, Gadfly.Server |
| Dialogue | Contradictions against the claim ledger; transfer questions a lookup can't answer | Gadfly.Core |
| Speech | Answer latency, pause pattern, reading-aloud cadence, second speaker, whispering | SpeechRecognition |
| Webcam | Face absent, multiple faces, identity drift, off-screen reading pattern | Webcam |
| Browser | Tab hidden, focus lost, fullscreen exit, extra screens (Window Management API) | Gadfly.Exam |
| Text | "Reads like generated text" against the student's own baseline (never on its own) | Legion judge |
| Human | Instructor review timeline with every flag's evidence; instructor finalizes | Gadfly.Exam |

Raw video never leaves the student's device. Only derived events are sent (D7).

## Data (SQL Server, EF Core)

Auth-owned tables (MindAttic.Authentication schema): users, credentials, MFA, audit, **organizations,
memberships (user, org, role), invitations**.

Gadfly tables (all tenant-scoped, with an EF global query filter on `OrganizationId`):

| Entity | Notes |
|---|---|
| `SourceCollection`, `SourceDocument`, `SourceChunk` | Uploaded material; chunks with embeddings (varbinary) and BM25 terms |
| `Concept`, `ConceptRelation`, `RubricPoint`, `Misconception` | The editable concept map and rubric, with chunk citations |
| `Exam` | Template: concepts, duration, difficulty, follow-up depth, persona and voice, integrity level, prompt-set version |
| `ExamAssignment` | Exam × student (or group), window, attempts allowed |
| `ExamSession` | One attempt: state, server timer, prompt-set hash, consent record |
| `Turn` | Move, question, transcript, word timings, speech metrics, audio blob ref |
| `Claim` | Claim-ledger entries per turn, contradiction links |
| `Judgment`, `JudgeVote` | Per turn: criterion scores, citations, per-voter verdicts, agreement |
| `ConceptMastery` | Per session × concept estimate and its history |
| `IntegrityEvent` | Type, start/end, confidence, evidence summary, source module |
| `SessionReport`, `GradeDecision` | AI recommendation; instructor's final grade and comment |
| `ApiKey`, `Webhook`, `WebhookDelivery` | Platform integrations (hashed keys, HMAC-signed payloads) |
| `UsageLedger` | LLM/STT/TTS tokens, seconds and cost per session and org |
| `Job` | Table-backed background queue (ingestion, extraction, reports) |

Roles per organization: **Owner, Admin, Instructor, Student**. MindAttic staff have a platform-level
**SuperAdmin**. MFA is required for Owner, Admin and Instructor.

## Gadfly as a platform for other apps

Any application can use the Socratic method in one of two ways:

- **NuGet**: reference `Gadfly.Core` (plus `Gadfly.Sources`) and supply an `ISubjectMatter`. This is how
  the CLI and the server use it. Both packages are 1.0.0 (`dotnet pack`; the feed copies are in
  `lib/local-packages`), and [samples/InProcess](../samples/InProcess) consumes them as packages, not projects,
  and runs a scripted session against the mock panel with no keys.
- **Hosted API**: a tenant API key creates exams from uploaded sources, assigns students, gets a
  **signed, expiring launch URL** to embed the exam room, and receives a `session.completed` webhook
  with the report. OpenAPI is served at `/openapi/v1.json`. [samples/HostedApi](../samples/HostedApi) plays a
  third-party LMS through the whole flow, including a verified webhook, against a local server in mock mode.

[samples/README.md](../samples/README.md) explains both paths in plain language.

## Milestones (build order)

| # | Milestone | Proves |
|---|---|---|
| 1 (MS-2) | **Text-only Socratic exam in the CLI** | The engine, grounding and Legion judging work, before any UI |
| 2 (MS-3) | **Voice and vision building blocks** | SpeechRecognition, Avatar and Webcam each work alone on a demo page (in parallel with 1) |
| 3 (MS-4) | **Platform backend and accounts** | SQL schema, organizations, invitations, SessionHub, jobs, API |
| 4 (MS-5) | **Voice exam web app** | Instructor builds an exam from sources; a student takes it by voice with the avatar; the instructor reviews |
| 5 (MS-6) | **Integrity** | Webcam, audio, browser and text signals become one reviewable integrity summary |
| 6 (MS-7) | **Production and pilot** | Azure deploy, privacy and consent, security review, Erin's pilot |

The tickets, epics, stories and `requires` links that implement this are in the ticket database
(`tickets milestone list`, `tickets ticket graph <ID>`). Each ticket's description says what it delivers
and how to tell it's done.

## Tickets in build order

Every ticket is approved. `requires` links enforce the order within and across repos; the queue priority follows this list. Status as of 2026-11-20, a snapshot: the live status is in the ticket database (`tickets ticket list --repo <repo>`).


### MS-2 · Text-only Socratic exam in the CLI

| # | Ticket | Repo | What | Needs | Status |
|---|---|---|---|---|---|
| 1 | GAD-T26 | Gadfly | Split Gadfly into Core and Personal | - | closed |
| 2 | TUT-T64 | Tutor | Extract Tutor.Ingestion package | - | open |
| 3 | M.LEGN-T50 | MindAttic.Legion | Grounded judging with citations | - | open |
| 4 | GAD-T27 | Gadfly | Socratic session domain model | GAD-T26 | closed |
| 5 | GAD-T28 | Gadfly | Personal subject on the engine | GAD-T27 | closed |
| 6 | GAD-T29 | Gadfly | Source material store | TUT-T64, GAD-T27 | open |
| 7 | GAD-T30 | Gadfly | Concept map extraction | GAD-T29, M.LEGN-T50 | closed |
| 8 | GAD-T31 | Gadfly | Rubric and answer key per concept | GAD-T30 | closed |
| 9 | GAD-T32 | Gadfly | Grounding retrieval | GAD-T29 | closed |
| 10 | GAD-T33 | Gadfly | Claim ledger and contradictions | GAD-T27, M.LEGN-T50 | closed |
| 11 | GAD-T34 | Gadfly | Question planner and mastery | GAD-T27 | closed |
| 12 | GAD-T35 | Gadfly | Exam question generator | GAD-T33, GAD-T34, GAD-T31, GAD-T32 | closed |
| 13 | GAD-T36 | Gadfly | Answer judge | M.LEGN-T50, GAD-T31, GAD-T32 | closed |
| 14 | GAD-T37 | Gadfly | Session report | GAD-T36, GAD-T34 | closed |
| 15 | GAD-T38 | Gadfly | gadfly exam CLI | GAD-T28, GAD-T35, GAD-T36, GAD-T37, GAD-T30 | closed |

### MS-3 · Voice and vision building blocks

| # | Ticket | Repo | What | Needs | Status |
|---|---|---|---|---|---|
| 16 | SR-T2 | MindAttic.SpeechRecognition | Bootstrap SpeechRecognition repo | - | open |
| 17 | SR-T3 | MindAttic.SpeechRecognition | Recognizer contracts | SR-T2 | open |
| 18 | SR-T4 | MindAttic.SpeechRecognition | Browser mic capture and VAD | SR-T2 | open |
| 19 | SR-T5 | MindAttic.SpeechRecognition | ElevenLabs Scribe provider | SR-T3 | open |
| 20 | SR-T6 | MindAttic.SpeechRecognition | ASP.NET Core relay and demo | SR-T4, SR-T5 | open |
| 21 | SR-T7 | MindAttic.SpeechRecognition | Speech metrics | SR-T5 | open |
| 22 | SR-T8 | MindAttic.SpeechRecognition | Fallback STT providers | SR-T3 | open |
| 23 | SR-T9 | MindAttic.SpeechRecognition | SpeechRecognition README and docs | SR-T6, SR-T7 | open |
| 24 | MAA-T2 | MindAttic.Avatar | Bootstrap Avatar repo | - | open |
| 25 | MAA-T3 | MindAttic.Avatar | ElevenLabs TTS client | MAA-T2 | open |
| 26 | MAA-T4 | MindAttic.Avatar | Talking head web component | MAA-T2 | open |
| 27 | MAA-T5 | MindAttic.Avatar | Default avatar asset | MAA-T4 | open |
| 28 | MAA-T6 | MindAttic.Avatar | Avatar server integration and demo | MAA-T3, MAA-T4 | open |
| 29 | MAA-T7 | MindAttic.Avatar | Avatar README and docs | MAA-T6, MAA-T5 | open |
| 30 | ET-T12 | MindAttic.Webcam | Rename EyeTracker to MindAttic.Webcam | - | open |
| 31 | ET-T13 | MindAttic.Webcam | Webcam core plus eyetracking module | ET-T12 | open |
| 32 | ET-T14 | MindAttic.Webcam | Presence module | ET-T13 | open |
| 33 | ET-T15 | MindAttic.Webcam | Head pose and gaze region | ET-T13 | open |
| 34 | ET-T16 | MindAttic.Webcam | Integrity event stream | ET-T14, ET-T15 | open |
| 35 | ET-T17 | MindAttic.Webcam | Gaze and deception research note | ET-T12 | open |

### MS-4 · Platform backend and accounts

| # | Ticket | Repo | What | Needs | Status |
|---|---|---|---|---|---|
| 36 | M.AUTH-T34 | MindAttic.Authentication | Organizations and memberships | - | open |
| 37 | M.AUTH-T35 | MindAttic.Authentication | Email invitations | M.AUTH-T34 | open |
| 38 | GAD-T39 | Gadfly | Gadfly.Server host and database | GAD-T26 | closed |
| 39 | GAD-T40 | Gadfly | Platform SQL schema | GAD-T39, GAD-T27 | closed |
| 40 | GAD-T41 | Gadfly | Authentication in Gadfly.Server | M.AUTH-T34, GAD-T39 | open |
| 41 | GAD-T42 | Gadfly | Tenant isolation | GAD-T41, GAD-T40 | open |
| 42 | GAD-T43 | Gadfly | Background job queue | GAD-T40 | closed |
| 43 | GAD-T44 | Gadfly | Session orchestration service | GAD-T40, GAD-T38 | open |
| 44 | GAD-T45 | Gadfly | SignalR SessionHub | GAD-T44, GAD-T42 | open |
| 45 | GAD-T46 | Gadfly | Platform API and webhooks | GAD-T44, GAD-T42 | open |
| 46 | GAD-T47 | Gadfly | Usage metering | GAD-T44, M.LEGN-T50 | open |

### MS-5 · Voice exam web app

| # | Ticket | Repo | What | Needs | Status |
|---|---|---|---|---|---|
| 47 | GAD-T48 | Gadfly | Exam web app shell | GAD-T41 | open |
| 48 | GAD-T49 | Gadfly | Instructor source library | GAD-T48, GAD-T43, GAD-T31 | open |
| 49 | GAD-T50 | Gadfly | Exam builder and assignments | GAD-T49, M.AUTH-T35 | open |
| 50 | GAD-T51 | Gadfly | Student dashboard, system check, consent | GAD-T48, SR-T6, ET-T14 | open |
| 51 | GAD-T52 | Gadfly | Exam room, text mode | GAD-T45, GAD-T51, GAD-T50 | open |
| 52 | GAD-T53 | Gadfly | Exam room, voice and avatar | GAD-T52, SR-T6, MAA-T6 | open |
| 53 | GAD-T54 | Gadfly | Instructor review and grading | GAD-T52, GAD-T37 | open |
| 54 | GAD-T55 | Gadfly | Org admin console | GAD-T48, GAD-T46, GAD-T47 | open |

### MS-6 · Exam integrity

| # | Ticket | Repo | What | Needs | Status |
|---|---|---|---|---|---|
| 55 | GAD-T56 | Gadfly | Browser integrity signals | GAD-T45, GAD-T52 | open |
| 56 | GAD-T57 | Gadfly | Webcam integrity in the exam room | ET-T16, GAD-T53 | open |
| 57 | GAD-T58 | Gadfly | Audio integrity signals | SR-T7, GAD-T53 | open |
| 58 | GAD-T59 | Gadfly | Answer-text integrity | GAD-T36 | open |
| 59 | GAD-T60 | Gadfly | Integrity summary and review | GAD-T56, GAD-T57, GAD-T58, GAD-T59, GAD-T54 | open |

### MS-7 · Production and pilot

| # | Ticket | Repo | What | Needs | Status |
|---|---|---|---|---|---|
| 60 | GAD-T61 | Gadfly | Privacy, consent and retention | GAD-T40, GAD-T51 | open |
| 61 | GAD-T62 | Gadfly | Observability | GAD-T44 | open |
| 62 | GAD-T63 | Gadfly | Azure deployment | GAD-T53, GAD-T62 | open |
| 63 | GAD-T64 | Gadfly | Security review | GAD-T46, GAD-T53, GAD-T60 | open |
| 64 | GAD-T65 | Gadfly | Guides and API docs | GAD-T54, GAD-T46 | open |
| 65 | GAD-T66 | Gadfly | Pilot with Erin | GAD-T63, GAD-T61, GAD-T64, GAD-T60, GAD-T54 | open |

Epics: Gadfly `SA` foundation, `SB` engine, `SC` sources and grounding, `SD` server and API, `SF` accounts,
`SG` exam app, `SI` integrity, `SP` production and pilot; MindAttic.SpeechRecognition `A`; MindAttic.Avatar
`A`; MindAttic.Webcam `W`; MindAttic.Authentication `O`; Tutor `I`; MindAttic.Legion `J`. Each ticket
`implements` one of their stories.

## Open decisions

Answer by code, e.g. "D3: B". Until a decision is answered, work proceeds on the **default** (bold).

| Code | Decision | Options |
|---|---|---|
| D1 | Product name, and where the exam app lives | **A: inside the Gadfly repo as Gadfly.Exam, working name "Gadfly Exams"** · B: its own branded repo (give a name) |
| D2 | What eye/face signals are used for | **A: integrity evidence plus "thinking" annotation only, no deception or emotion inference** · B: also show instructors raw gaze heatmaps |
| D3 | Avatar look | **A: stylized 3D head (TalkingHead GLB), one default examiner** · B: a Socrates-like character · C: photoreal 2D from a photo (needs a GPU server) |
| D4 | ElevenLabs usage | **A: ElevenLabs for TTS and for STT (Scribe)** · B: TTS only, OpenAI for STT. Also: which voice ID? |
| D5 | Hosting | Resolved: LocalDB for development, Azure App Service + Azure SQL live at mindattic-gadfly (owner confirmed 2026-11-20) |
| D6 | First customers | **A: adults (universities, corporate training, certification)** · B: also K-12 (adds COPPA, FERPA and parental consent work) |
| D7 | Recording retention | **A: audio + transcript kept 90 days (per-org setting), video never stored, derived events only** · B: no audio stored at all |
| D8 | Billing | **A: none in v1, manual invoicing, usage metered anyway** · B: Stripe subscriptions in v1 |
| D9 | New library repos public or private | **A: private until v1** · B: public (MIT) now, like Webcam and Legion |
| D10 | EyeTracker → Webcam | Resolved: renamed in place to MindAttic.Webcam (ET-T12; history kept, GitHub redirects old URLs) |
| D11 | Student identity | **A: login + same-face-throughout check** · B: photo-ID match at start |
| D12 | Languages | **A: English only in v1** · B: multilingual (ElevenLabs and Scribe support it) |
| D13 | Who grades | Resolved by the master plan: AI grades are preliminary; blind human grading, reconciliation and an authorized approval come before anything is published |
| D14 | Cross-repo work from the Gadfly window | Resolved: the owner typed `/crossrepo all` |
| D15 | LLM provider billing | Resolved: Anthropic credits bought (2026-11-20); OpenAI Low tier is now gpt-6-luna (M.LEGN-T51). Spending rule: mock mode everywhere, Claude pinned to Sonnet 5, no live calls without the owner's OK each time |
| D16 | Create the Azure resources | Resolved: owner said go (2026-11-20) |
| D18 | What a teacher sees | **A: teachers see only their own assessments and students; Owner/Admin see the whole organization** · B: every teacher sees the whole organization (today's behavior) | **Proceeding on default A (GAD-T170).**
| D19 | Voice consent wording | **A: keep the wording now in the exam room (consent version .4)** · B: owner rewrites it |
| D20 | Voice-only exams | **A: typing is always available (accessibility, broken mic); typed answers are marked as text** · B: allow an exam to require voice |
| D21 | When webcam notes reach the reviewer | **A: also send an update every minute while an episode lasts** · B: only when an episode ends (today's behavior). **Proceeding on default A (GAD-T171)** |
| D22 | 3D avatar on Azure | **A: upload the 37 MB model to blob storage and point Gadfly:Voice:AvatarModelUrl at it** · B: keep the 2D portrait on Azure |
| D23 | Live AI | **A: stay in mock mode until the owner says go** · B: turn on the live panel (and, with D4, ElevenLabs voice) for named test sessions |
| D24 | MFA for the classroom test staff (Teacher_A..C, Classroom_Owner) on Azure | **A: keep MFA required for staff; each tester enrolls an authenticator app at first sign-in** · B: exempt only the "Gadfly Classroom" test organization's staff from MFA while testing |
| D25 | Blind grading and exam-time evidence | **A: hide the exam-time judge's per-answer stance (Supports/Uncertain…) and its notes from the examiner until they have submitted their blind grade** · B: keep showing them (today's behavior; they're evidence, not the AI grade) | **Proceeding on default A (GAD-T218).** |
