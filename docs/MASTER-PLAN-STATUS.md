# Master plan: status and build order

[MASTER-PLAN.md](MASTER-PLAN.md) is the priority for Gadfly (GAD-T79). The owner's direction, 2026-11-20:
**build the whole product, not an MVP, ready to go online on Azure**, on **Blazor Server and SQL Server**,
with **exhaustive NUnit tests** and a **demo tour** that drives the whole workflow end to end so every
stage can be validated as it is built.

So the master plan's MVP list (Part 18.2) is the *first* slice, not the finish line, and its "defer" list
(Part 18.3) moves to the end of the queue instead of off it. The plan's own conflict order still decides
trade-offs: student safety, privacy and fairness; transcript and evidence integrity; assessment validity;
human grading accountability; functional correctness; maintainability; performance; additional features.

## Stack decision (Part 17)

| Concern | Choice |
|---|---|
| Frontend | Blazor Server (one deployable with the API), MindAttic look |
| Backend | ASP.NET Core 10, EF Core 10, SQL Server (LocalDB in development, Azure SQL in production) |
| Auth | MindAttic.Authentication (Argon2id, TOTP MFA, lockout, audit) with organization roles |
| AI | MindAttic.Legion multi-provider panel behind `ISocraticVotingClient`; schema-validated outputs; mock mode when no key works |
| Background work | SQL-table job queue |
| Hosting | Azure App Service + Azure SQL + Key Vault + Blob key ring (docs/DEPLOY.md) |
| Tests | NUnit (Gadfly.Tests) + the end-to-end demo tour (Gadfly.Tour) |

## What the existing build already covers

| Master plan | Built | Where |
|---|---|---|
| Part 2 adaptive Socratic engine | Planner with elicit/clarify/probe/counterexample/apply/contradiction/synthesize, Elo-style mastery, stop rules, contradiction ledger | Gadfly/Engine |
| Part 4.4 non-leading questions | Leak-guard vote on every question with redraft and safe fallback | Engine/QuestionGenerator.cs |
| Part 7 AI scoring (per concept) | Panel judge with criteria, citations, per-voter verdicts, agreement, human-review flag | Gadfly.Sources/PanelAnswerJudge.cs |
| Part 14 materials + RAG | Ingestion, concept map, cited rubrics, hybrid retrieval | Gadfly.Sources |
| Part 13.2 text examination | Exam room, resumable sessions, server time limit | Gadfly.Server |
| Part 16 isolation, RBAC basics, privacy | Tenant filter + write guard, org roles, MFA, retention, export/erase | Gadfly.Server |
| Part 12.2 review (single grader) | Review timeline, evidence, integrity summary, grade, release | Components/Pages/Review |
| Part 17 API | Platform API v1, webhooks, SessionHub | Gadfly.Server/Platform, Sessions |
| Part 19 Phase 7 deployment | Workflow, provisioning script, guide; live at mindattic-gadfly.azurewebsites.net in mock mode | docs/DEPLOY.md |

## What changes because of the master plan

- **Grading model.** "AI recommends, instructor finalizes" (decision D13) is replaced by AI preliminary
  scores per learning objective, independent (blind) human scores, an approved AI/human weighting,
  disagreement thresholds, reconciliation with a second reviewer, and an authorized approval before
  publication, under an explicit grade-status state machine.
- **Assessment model.** Concepts become *suggested* learning objectives; teachers own courses, subjects,
  topics, objectives, rubric criteria on the Part 5.1 dimensions and level descriptors 0–5, where level 0
  is Insufficient Evidence and never zero understanding.
- **Transcript integrity.** Every answer keeps an immutable original plus versions, verification states,
  clarifications and typed corrections (transcription / meaning / substantive revision).
- **Integrity signals.** Answer-text style and browser signals stay reviewer-only context; the plan forbids
  treating AI-text detection as proof, which matches how they are already shown.
- **Voice, avatar and webcam signals** (MindAttic.SpeechRecognition, MindAttic.Avatar, MindAttic.Webcam) were
  built after the core product (docs/VOICE.md, docs/PRIVACY.md): the plan makes voice optional, typing is always
  available, and webcam signals stay reviewer-only context.

## Build order

Tickets live in the ticket database under epic **MP** (`tickets ticket list --repo Gadfly`); each one names
the master-plan part it implements.

| Order | Tickets | Master plan |
|---|---|---|
| 1 | Design pack | Part 19 Phase 1 |
| 2 | Courses/objectives/rubric criteria, grading policy, roles, audit log, response versioning (one schema foundation) | Parts 5, 9, 10, 16, 17.4, 4 |
| 3 | Mock mode, demo tour, test harness | Part 22, owner's tour + tests |
| 4 | Verification engine, student confirmation UI | Part 4, 13 |
| 5 | Examination control, strategies A–H, response analysis, contamination, learning mode | Parts 2, 3, 5, 6 |
| 6 | AI grading per objective, blind human grading, combined scoring, reconciliation, approval/statuses/appeals, report | Parts 7–11 |
| 7 | Teacher setup UI, analytics, demo assessments | Part 12, 19 |
| 8 | Exhaustive NUnit tests (scenarios A–J and every rule) | Part 20 |
| 9 | Azure deployment and go-live | Part 19 Phase 7 |
| 10 | Voice, avatar, webcam (built); LMS integrations, billing (not built) | Part 13.3, 18.3 |

Nothing here claims validity results: Part 15.3's validation studies happen with real institutions after
launch, and the product says so.
