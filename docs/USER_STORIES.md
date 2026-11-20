---
codex: 1
project: Gadfly
code: GAD
layer: stories
status: living
updated: 2026-10-13
---
<!-- GENERATED from MindAttic.Tickets/data/Tickets.db by `tickets db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the tickets CLI. -->

# Gadfly — User Stories
> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites the test.

## Priority backlog
_Nothing recorded yet._


## Epic SA — Socratic platform: foundation
- **GAD-US-SA1 ⬜** As MindAttic, Gadfly is a reusable Socratic engine any app can embed, with today's personal introspection tool as one consumer of it.


## Epic SB — Socratic platform: the engine
- **GAD-US-SB1 ⬜** As an examiner, I can run an adaptive Socratic exam that probes a student's understanding of each concept, confronts contradictions in their own claims, and stops at mastery, aporia or the time budget.
- **GAD-US-SB2 ⬜** As an instructor, I get a per-concept report with quoted evidence, source citations and the judges' dissent, as a recommendation I finalize.


## Epic SC — Socratic platform: source material and grounding
- **GAD-US-SC1 ⬜** As an instructor, I upload my own source material and get an editable concept map and rubric that every question and judgment is grounded in, with citations.


## Epic SD — Socratic platform: hosted server and API
- **GAD-US-SD1 ⬜** As the platform, every live session runs server-side with its turns, judgments and events persisted in SQL Server, resumable after a disconnect.
- **GAD-US-SD2 ⬜** As a third-party app, I can create exams, launch sessions in an embedded room and receive results through a keyed API and webhooks.


## Epic SF — Socratic platform: customer and student accounts
- **GAD-US-SF1 ⬜** As a customer org admin, I invite instructors and students into my organization, each with a role, and nobody ever sees another organization's data.


## Epic SG — Socratic platform: exam web app
- **GAD-US-SG1 ⬜** As a student, I check my mic and camera, consent, and take a spoken exam with a talking examiner that listens and follows up on what I said.
- **GAD-US-SG2 ⬜** As an instructor, I build exams from my sources, assign them, review each session's transcript, scores and evidence, and finalize the grade.


## Epic SI — Socratic platform: integrity
- **GAD-US-SI1 ⬜** As an instructor, I see integrity evidence (faces, voices, off-screen reading, focus loss, answer cadence) explained on the session timeline, with no automated accusation and no deception or emotion inference.


## Epic SP — Socratic platform: production and pilot
- **GAD-US-SP1 ⬜** As MindAttic, the platform runs on Azure with consent, retention, security and cost controls, and has been piloted with Erin's real exams.

## Tickets
> Permanent log of every change and fix — this repo's ticket system. Rows are append-only: never
> delete or reword one; only move its Status (open → closed / wont-do) and fill in Ref. Status is
> a word, not a glyph, so tickets don't count toward the story index. Keep this section last.

| ID | Date | Type | Status | Summary | Ref |
|---|---|---|---|---|---|
| GAD-T1 | 2026-10-13 | docs | closed | Start the permanent ticket log in USER_STORIES.md | — |
| GAD-T2 | 2026-10-14 | chore | closed | NUnit tests for CorpusRepository (Gadfly/Corpus/CorpusRepository.cs): GetAll orders by TimestampUtc, GetHumanMessages drops non-Human roles, GetSince is inclusive of the boundary, and CorpusHash is identical regardless of store read order but changes when a message is added/removed - it is the digest cache key, so a wrong hash means stale or needlessly re-billed summaries | Gadfly.Tests/Corpus/CorpusRepositoryTests.cs |
| GAD-T3 | 2026-10-14 | chore | closed | NUnit tests for ContextBuilder (Gadfly/Mining/ContextBuilder.cs): BuildInterrogationContext emits the CORPUS DIGEST header and omits the DIALOGUE SO FAR block when there are no turns, renders 'Speaker: Text' lines in order otherwise; BuildCorpusDigestAsync (with FakeSocraticVotingClient) summarizes only human messages and reuses the cached digest when CorpusHash is unchanged | dd5d1ab |
| GAD-T4 | 2026-10-14 | chore | open | NUnit tests for MasterExaminationRunner and ReflectionRunner (Gadfly/Socrates) using FakeSocraticVotingClient: Master maps Socrates/Confucius/Buddha to claude/openai/gemini by default, uses a 3-item override in order, ignores a 2- or 4-item override, sends MasterPrompt, and falls back from Decision to Reasoning when blank; Reflection appends the INTERROGATION TRANSCRIPT only when prior turns exist, uses Plurality, and falls back from Consensus to NarrativeSummary | — |
| GAD-T5 | 2026-10-14 | chore | open | NUnit tests for GadflyConfiguration.Build (Gadfly/GadflyConfiguration.cs) with a temp projectDir: no legion.json gives shared credentials, judge claude and the tier's model overrides; a legion.json voter list is applied but the tier overrides and an explicit --voters/--single-voter override still win, and untrusted provider ids are filtered out - so a CLI flag always narrows the panel and cost stays bounded | — |
| GAD-T6 | 2026-10-14 | chore | open | NUnit tests for the Gadfly CLI (Gadfly.Cli/ImportCommand.cs, GadflyCli.cs, ExamineCommand/InterrogateCommand arg parsing): import with no args returns 1 and --help returns 0, a missing zip returns 1, a schema-mismatched export returns 2, --dry-run reports counts and writes nothing; examine --voters with other than 3 ids warns and keeps defaults; --tier/--max-turns/--quorum ignore invalid values (extract the parsing into a testable options method if needed) | — |
| GAD-T7 | 2026-10-16 | chore | closed | install.ps1 adds this repo's 15 .prose/commands stubs for the shared global commands, so any LLM client finds them | mindattic-agent-standard/install.ps1 run 2026-10-16 |
| GAD-T8 | 2026-10-16 | chore | open | Bug hunt: run an exhaustive /bughunt over the whole repo and fix every real bug found (each fix under its own fix ticket), then hunt again; close this only when a full pass finds nothing | user request 2026-10-16: approved repo sweep |
| GAD-T9 | 2026-10-16 | chore | open | Run the full unit test suite and fix every failure (each under its own fix ticket) until it passes; put the passing run in --ref. No test project: close this wont-do saying so | user request 2026-10-16: approved repo sweep |
| GAD-T10 | 2026-10-16 | chore | open | Clean up code and docs: remove dead code, stale comments and outdated or duplicated docs, and simplify where behaviour doesn't change (/simplify); tests stay green | user request 2026-10-16: approved repo sweep |
| GAD-T11 | 2026-10-16 | docs | open | Bring README.md up to date with the code as it is now (what it is, setup, usage, commands, current state), following codex-standard/README-STYLE.md; README.htm regenerated if the repo has one | user request 2026-10-16: approved repo sweep |
| GAD-T12 | 2026-10-16 | docs | closed | Update USER_STORIES.md | — |
| GAD-T13 | 2026-10-19 | chore | wont-do | Convert tracked source files to LF line endings via .gitattributes, per HOUSE-LAW-13 (low priority) | — |
| GAD-T14 | 2026-10-20 | docs | closed | Publish Gadfly docs to GitHub Pages via the shared Agency DocFX pipeline: run adopt-docs-site.ps1 (README.md as home page, docs/*.md as articles, API reference only where the code builds in CI), enable Pages with build_type=workflow, set the repo homepage URL, retire tools/build-readme.ps1 and README.htm, confirm the docs Actions run is green and https://mindattic.github.io/Gadfly/ returns 200. Depends on MAT-T8. | — |
| GAD-T15 | 2026-10-22 | docs | closed | Update USER_STORIES.md | — |
| GAD-T16 | 2026-10-24 | docs | closed | Update USER_STORIES.md | — |
| GAD-T17 | 2026-10-31 | feature | closed | LAMP: move Gadfly's app-level settings and keys to MindAttic.Vault.Desktop (manifest, LoadOrSeed, shared named keys, cog opens Vault) | 2e3748b Gadfly.Tests (51/51); keys via M.LEGN-T44 LampKeyTests |
| GAD-T18 | 2026-10-31 | docs | closed | Update USER_STORIES.md | — |
| GAD-T19 | 2026-11-02 | docs | closed | Adopt MindAttic.Agency docs-site pipeline, publish README to GitHub Pages (HOUSE-LAW-14) | — |
| GAD-T20 | 2026-11-02 | fix | closed | Bughunt sweep, two passes: find and fix real bugs across tracked source (user request 2026-11-02) | — |
| GAD-T21 | 2026-11-02 | fix | closed | Fix the open items the 2026-11-02 bughunt left for decision (ingest login scoping, torn jsonl tail, untrusted voters) | — |
| GAD-T22 | 2026-11-10 | chore | closed | Crash recovery: commit the command-catalog sync (.agent/commands/*.md) and regenerated docs/USER_STORIES.md left uncommitted by the session crash | — |
| GAD-T23 | 2026-11-10 | chore | closed | Adopt the shared docs-site pipeline: turn on GitHub Pages | — |
| GAD-T24 | 2026-11-17 | docs | closed | Update USER_STORIES.md | — |
| GAD-T25 | 2026-11-20 | docs | open | Plan the Socratic oral-exam platform (Erin's idea): Gadfly becomes a reusable Socratic engine + hosted platform; new MindAttic.SpeechRecognition and MindAttic.Avatar (ElevenLabs talking head); MindAttic.EyeTracker becomes MindAttic.Webcam; Legion judges answers grounded in source material; customer/student accounts via MindAttic.Authentication on SQL Server. Write the outline, epics, stories, milestones and sequenced tickets, all approved. | — |
| GAD-T26 | 2026-11-20 | chore | closed | Split the solution: Gadfly.Core (engine), Gadfly.Personal (existing corpus ingestion/mining/introspection), Gadfly.Cli; behavior unchanged | a2c096b |
| GAD-T27 | 2026-11-20 | feature | closed | Gadfly.Core domain model: SocraticSession, Turn, Move (Elicit, Clarify, Probe, Counterexample, Contradiction, Apply, Synthesize, Close), SessionPolicy, versioned prompt sets hashed into each session | 74333b0 |
| GAD-T28 | 2026-11-20 | feature | closed | ISubjectMatter abstraction with PersonalCorpusSubject: interrogate/reflect/examine run through the generic engine with their irrevocable prompts unchanged | 62bf8d5 |
| GAD-T29 | 2026-11-20 | feature | open | Gadfly.Sources: ingest documents through Tutor.Ingestion into chunks with embeddings, behind a store abstraction (file store for the CLI, SQL store later) | GAD-T25 |
| GAD-T30 | 2026-11-20 | feature | closed | Legion builds a concept map from a source collection: concepts, definitions, relationships, key facts and common misconceptions, each citing chunks; saved as editable JSON | 5761131 |
| GAD-T31 | 2026-11-20 | feature | closed | Per-concept rubric: the understanding points a real answer covers, acceptable variants, misconceptions to watch for, each citing source chunks; Legion consensus; editable | e6b6a79 |
| GAD-T32 | 2026-11-20 | feature | closed | Hybrid retrieval (BM25 + embedding similarity, reciprocal-rank fusion) of the top source chunks for a concept plus a student answer, for the question generator and the judge | 9455c14 |
| GAD-T33 | 2026-11-20 | feature | closed | Claim ledger: extract the student's assertions from each answer (Legion), detect contradictions with earlier claims, and feed Contradiction moves | 32ec373 |
| GAD-T34 | 2026-11-20 | feature | closed | Planner: per-concept mastery estimate (Elo-style from judged answers), move selection (elicit, probe, counterexample, contradiction, apply, synthesize, close), coverage and time budget, stop rules | 33d886a |
| GAD-T35 | 2026-11-20 | feature | closed | Exam-mode question generator on Legion: drafts from several providers, a judge fuses the sharpest; grounded in the concept's chunks, the conversation and the claim ledger; new versioned exam prompt set; never reveals the answer | 22df8ea |
| GAD-T36 | 2026-11-20 | feature | closed | Answer judge: Legion grounded rubric judging of each answer (accuracy against the source, reasoning depth, consistency with earlier claims, completeness) with citations, per-voter verdicts and a low-agreement flag for human review | 66941d2 |
| GAD-T37 | 2026-11-20 | feature | closed | Session report: per-concept mastery, overall recommendation, quoted evidence with source citations, judge dissent; JSON and Markdown output | 670763b |
| GAD-T38 | 2026-11-20 | feature | closed | `gadfly exam --source <folder\|file>`: an end-to-end text-only Socratic exam in the terminal, from ingestion through concept map, questioning, judging and report | d2c23e5 |
| GAD-T39 | 2026-11-20 | feature | closed | Gadfly.Server: ASP.NET Core host, EF Core SQL Server DbContext with migrations, LocalDB for development (Azure SQL later), configuration and secrets through MindAttic.Vault | 18a2a2a |
| GAD-T40 | 2026-11-20 | feature | closed | Schema per the plan: source collections/documents/chunks, concepts, rubric, exams, assignments, sessions, turns, claims, judgments and votes, mastery, integrity events, reports and grade decisions, API keys, webhooks, usage ledger, jobs | 835ed01 |
| GAD-T41 | 2026-11-20 | feature | open | Wire MindAttic.Authentication with organizations into Gadfly.Server: customer org creation by a MindAttic SuperAdmin in v1, roles Owner/Admin/Instructor/Student, MFA required for staff roles | GAD-T25 |
| GAD-T42 | 2026-11-20 | feature | open | Tenant isolation: EF global query filters on OrganizationId for every tenant-owned entity, authorization policies per role, and a guard that rejects any write whose OrganizationId differs from the caller's active org | GAD-T25 |
| GAD-T43 | 2026-11-20 | feature | closed | SQL-table-backed background job queue (BackgroundService) for ingestion, concept extraction, rubric generation and reports: retry with backoff, progress reporting, cancellation | d2bf07c |
| GAD-T44 | 2026-11-20 | feature | open | Session orchestration: runs the engine per live session, persists every turn, judgment and mastery snapshot, server-authoritative timer, resumable after a disconnect, one active session per assignment attempt | GAD-T25 |
| GAD-T45 | 2026-11-20 | feature | open | SignalR SessionHub: authenticated session channel for audio chunks up, transcripts and examiner speech (audio plus alignment) down, integrity events up, state changes both ways | GAD-T25 |
| GAD-T46 | 2026-11-20 | feature | open | Platform API for third-party apps: hashed per-org API keys; REST to upload sources, create exams, assign students, mint signed expiring launch URLs for an embeddable exam room, fetch results; HMAC-signed session.completed webhooks with retries; OpenAPI document | GAD-T25 |
| GAD-T47 | 2026-11-20 | feature | open | Usage metering: LLM tokens, STT seconds and TTS characters with estimated cost per session and organization into UsageLedger; per-org quotas that stop new sessions when exceeded | GAD-T25 |
| GAD-T48 | 2026-11-20 | feature | open | Gadfly.Exam Blazor Server app shell: MindAttic.Components theme, sign-in, organization switcher, role-based navigation (Admin, Instructor, Student) | GAD-T25 |
| GAD-T49 | 2026-11-20 | feature | open | Instructor source library: upload documents, watch ingestion progress, review and edit the concept map and rubric (with citations opening the source passage) | GAD-T25 |
| GAD-T50 | 2026-11-20 | feature | open | Exam builder: choose concepts, duration, difficulty, follow-up depth, examiner voice and avatar, integrity level and schedule window; assign to students or groups; invite students by email or CSV | GAD-T25 |
| GAD-T51 | 2026-11-20 | feature | open | Student dashboard with assignments; pre-exam system check (mic level, camera and face visible, browser support, bandwidth); consent screen for audio and biometric processing; practice session on a sample topic | GAD-T25 |
| GAD-T52 | 2026-11-20 | feature | open | Student exam room in text mode first: live Socratic dialogue over SessionHub, server timer, progress through concepts, finish and confirmation | GAD-T25 |
| GAD-T53 | 2026-11-20 | feature | open | Voice mode: questions spoken by <ma-avatar> with lip sync and captions; answers spoken through SpeechRecognition with live transcript; hands-free turn taking with an end-of-answer cue; repeat question; voice-only (typing disabled) for graded exams | GAD-T25 |
| GAD-T54 | 2026-11-20 | feature | open | Instructor review: session timeline with transcript, audio playback, each answer's judgment with citations and dissent, mastery per concept, integrity markers; override and finalize the grade with a comment; export PDF and CSV | GAD-T25 |
| GAD-T55 | 2026-11-20 | feature | open | Org admin console: members and roles, usage and cost dashboard, API keys and webhooks management, retention settings | GAD-T25 |
| GAD-T56 | 2026-11-20 | feature | open | Browser signals: tab hidden, focus lost, fullscreen exit, extra screens (Window Management API), paste attempts, devtools heuristics, each sent as an IntegrityEvent | GAD-T25 |
| GAD-T57 | 2026-11-20 | feature | open | Run MindAttic.Webcam's presence, gaze-region and reading-pattern modules in the exam room and stream their events (never frames) to the session | GAD-T25 |
| GAD-T58 | 2026-11-20 | feature | open | Audio signals from speech metrics: second speaker, whispered prompting, reading-aloud cadence, long silence followed by unusually fluent delivery | GAD-T25 |
| GAD-T59 | 2026-11-20 | feature | open | Legion assessment of whether an answer reads like generated text compared with the student's own earlier answers; low weight, never used on its own | GAD-T25 |
| GAD-T60 | 2026-11-20 | feature | open | Aggregate integrity events into a session summary (severity, plain-language explanation, evidence links on the review timeline); thinking glances shown as normal; no automatic failing | GAD-T25 |
| GAD-T61 | 2026-11-20 | feature | open | Privacy: consent records, retention jobs (audio and transcripts after the org's retention period, default 90 days; video never stored), per-student data export and deletion, privacy policy and terms drafts, EU AI Act high-risk and BIPA notes | GAD-T25 |
| GAD-T62 | 2026-11-20 | chore | open | Observability: MindAttic.Log and Telemetry wiring, per-session trace id across hub, engine and providers, cost dashboard, alerts on provider failures and quota exhaustion | GAD-T25 |
| GAD-T63 | 2026-11-20 | chore | open | Deploy to Azure: App Service (WebSockets on) and Azure SQL, staging and production slots, migrations on deploy, Vault-backed secrets, HTTPS and custom domain, run.ps1 and MindAttic.Deploy wiring | GAD-T25 |
| GAD-T64 | 2026-11-20 | chore | open | Security review of the new surfaces against OWASP ASVS L2: API keys, launch tokens, SignalR auth, uploads (parser sandboxing, size limits), tenant isolation, webhook signing; fix tickets for findings | GAD-T25 |
| GAD-T65 | 2026-11-20 | docs | open | Instructor guide, student guide and platform API docs on the docs site; README rewritten for Gadfly as engine plus platform | GAD-T25 |
| GAD-T66 | 2026-11-20 | chore | open | Pilot with Erin: a demo org, a course built from her source material, at least five real voice sessions, her feedback filed as tickets | GAD-T25 |
