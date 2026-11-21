# Gadfly

![Stack](https://img.shields.io/badge/stack-Blazor%20Server%20%2B%20SQL%20Server-512bd4) ![Framework](https://img.shields.io/badge/.NET-10-512bd4) ![License](https://img.shields.io/badge/license-MIT-green) ![Status](https://img.shields.io/badge/status-in%20development-blue)

**Exams that find out whether a student understands, not whether they can produce an answer.**

When anyone can paste a question into an AI, a correct answer proves very little. Gadfly Exams asks
questions the way a good examiner does in person: it starts with "what is it?", then follows up on the
student's own words, asking why, what if, what would change, and whether two things they said can both
be true. Each follow-up is written from the previous answer, so it can't be looked up or prepared in
advance, and understanding shows in the reasoning, not in the wording.

Instructors upload their own course material. Gadfly turns it into learning objectives and rubrics they
can edit, and every question and judgment is grounded in that material with citations. Every answer is
kept exactly as the student gave it; unclear answers get a neutral clarifying question that never hints
at the answer. A panel of AI models scores each objective, a human examiner grades independently
(blind, by default), large disagreements go to reconciliation, and nothing reaches a student until an
authorized person approves it. Integrity signals (focus loss, a second voice, reading from elsewhere) are
shown to the reviewer as evidence, never as an automatic verdict.

It's a multi-tenant web platform for schools, universities, certification bodies and corporate training:
organizations, teachers, examiners, reviewers and students, a full audit trail, privacy controls, and an
API for other applications. Built on Blazor Server, SQL Server and Azure, with
[MindAttic.Legion](https://github.com/mindattic/MindAttic.Legion)'s multi-LLM panel underneath.

The product plan is [docs/MASTER-PLAN.md](docs/MASTER-PLAN.md); what's built against it is in
[docs/MASTER-PLAN-STATUS.md](docs/MASTER-PLAN-STATUS.md).

Underneath is a general **Socratic engine** that other apps can use, and the original **personal
introspection CLI** (`gadfly`), which cross-examines you about your own Claude Code transcripts and
claude.ai export. It is documented further down.

## Why

- Know whether a student understands, from their own reasoning, when a pasted answer proves nothing.
- Keep a human accountable for every grade: the AI panel scores each objective, an examiner grades blind,
  large gaps go to a second reviewer, and an authorized person approves before a student sees anything.
- Keep the evidence intact: the original answer, every clarification and every typed correction stay on
  the record, with an append-only audit trail.
- Protect students: integrity signals are low-confidence notes for a reviewer, never a verdict, and video
  never leaves the student's device ([docs/PRIVACY.md](docs/PRIVACY.md)).
- Try it without spending anything: the default development setup runs a clearly labelled mock panel, so
  no provider key or credit is needed.

## Features

Gadfly Exams, the web platform (`Gadfly.Server`):

- **Organizations and roles.** Owner, Admin, Instructor, Examiner, Second reviewer and Student, on
  MindAttic.Authentication (Argon2id, MFA for staff, lockout), with invitations by email and an
  organization admin console: members, retention, cost quota, API keys, webhooks, usage.
- **Source material.** Upload `.txt`, Markdown, PDF, DOCX, EPUB or HTML. Gadfly turns it into an editable
  concept map and versioned rubrics with citations to the passages.
- **Assessments.** Courses, learning objectives, rubric criteria with level descriptors 0 to 5, a locked
  grading policy (AI and human weights, disagreement thresholds), assignments, and a student dashboard
  with system check and consent.
- **The exam room.** A text exam where each question is written from the previous answer, with neutral
  clarifying questions, typed corrections, "I don't know" recorded as honest aporia, resumable sessions
  and a server-side time limit.
- **Voice and avatar.** Voice exams where a talking examiner (`<ma-avatar>`) asks and the student answers
  aloud, always with a typing fallback. See [docs/VOICE.md](docs/VOICE.md).
- **Webcam and browser integrity signals.** On Strict exams, presence, a second face and gaze region are
  derived on the student's device with MindAttic.Webcam, after the student accepts a notice that says what
  is and is not collected; tab switches, pastes and answer-text style are reviewer-only context. See
  [docs/PRIVACY.md](docs/PRIVACY.md).
- **Grading.** AI preliminary grades per objective, blind human grading, combined scoring,
  reconciliation with a second reviewer, final approval, publication, appeals and amendments, with a
  Markdown and CSV report.
- **Review and analytics.** An instructor review timeline with citations and each judge's verdict, and
  educational analytics at `/analytics`.
- **Privacy.** Retention, student data export and erasure.
- **Platform API.** Organization API keys, signed launch links and signed webhooks, with an OpenAPI
  document at `/openapi/v1.json`.

## Quick start

Prerequisites: the .NET 10 SDK and SQL Server Express LocalDB. Sign-in also needs the MindAttic.Vault
`Security` bucket (the sign-in library's pepper and `bootstrap-token`); it fails closed without it.

```powershell
dotnet build Gadfly.slnx
dotnet run --project Gadfly.Server
```

Open `https://localhost:7311` (HTTP on 5311). In Development the database `Gadfly` on
`(localdb)\MSSQLLocalDB` is created and migrated on start, and the AI panel is the mock: every page shows
a MOCK MODE banner and nothing calls a provider. Seed a ready-made institution to click through (see
the seeders below), or sign in as the SuperAdmin.

**First sign-in.** When the database has no accounts, the server creates the platform SuperAdmin:
user name `admin`, password the Vault's `Security:bootstrap-token`. Sign in, choose a new password, set
up an authenticator app, and rotate the bootstrap token in the Vault. Then open **Organizations** to
create a customer organization and its owner, who adds instructors and students under **Members**.

## Mock mode and live mode

`Gadfly:AiMode` chooses the AI panel: `live` (MindAttic.Legion providers), `mock` (a deterministic,
labelled stand-in) or `auto` (live when a provider answers a startup probe, otherwise mock). Unset means
`mock` in Development and `live` elsewhere. To run on real providers locally, set it explicitly; the Vault
`LLM` bucket holds the provider keys:

```powershell
$env:Gadfly__AiMode = "live"
dotnet run --project Gadfly.Server
```

Live mode spends provider credit. Everything the mock produces is labelled `[MOCK]` and stored with an
`IsMock` flag; the details are in [docs/TOUR.md](docs/TOUR.md).

## The demo tour and the seeders

Two seeders, both safe to re-run, create accounts for trying the interface. Pass the flag as the last
argument. Set `Gadfly:Demo:Password` to choose the password they give every account; otherwise one is
generated and printed once in the log.

```powershell
# Gadfly Demo Academy: demo.owner, demo.teacher, demo.examiner, demo.reviewer, demo.student, two
# courses and the master plan's two demonstration assessments. Development only.
dotnet run --project Gadfly.Server -- --seed-demo

# Gadfly Classroom: Classroom_Owner, Teacher_A to Teacher_C and Student_A to Student_Z across three
# published assessments. Add --then-exit to seed and stop.
dotnet run --project Gadfly.Server -- --seed-classroom
```

`Gadfly:Demo:SeedClassroomOnStartup=true` seeds the classroom once the server is listening, which is how
a hosted instance is seeded.

`Gadfly.Tour` is a console app that starts the server on its own database, seeds the demo institution and
drives the whole workflow through the real server as real users, printing a checklist line per stage. It
runs in mock mode unless you pass `--live`:

```powershell
dotnet build Gadfly.Server\Gadfly.Server.csproj
dotnet build Gadfly.Tour\Gadfly.Tour.csproj
dotnet run --project Gadfly.Tour -- --fresh
```

The other options (`--keep`, `--url`, `--verbose` and more) are in [docs/TOUR.md](docs/TOUR.md).

## Configuration

| Setting | What it does |
|---|---|
| `ConnectionStrings__Gadfly` | SQL Server connection. Default `(localdb)\MSSQLLocalDB`, database `Gadfly`. Outside Development, run once with `--migrate`. |
| `Gadfly:AiMode` | `live`, `mock` or `auto`, as above. |
| `Gadfly:RequireMfaInDevelopment` | Staff are not asked for MFA in Development unless this is `true`. |
| `Gadfly:Voice:Mode` | `off`, `mock` or `live`; the other voice settings are in docs/VOICE.md. |
| `Gadfly:Demo:Password`, `Gadfly:Demo:ApiKey` | Chosen password and API key for the seeded demo accounts. |

Uploaded files are kept under `%APPDATA%\MindAttic\Gadfly\uploads\<organization>\<collection>\`. Logs go
to MindAttic.Log's database for `Gadfly.Server`. The health check is at `/healthz`.

## Deployment

Gadfly.Server deploys to Azure App Service with Azure SQL, Key Vault and a managed identity, from a
manual GitHub Actions workflow; [docs/DEPLOY.md](docs/DEPLOY.md) has the procedure. The instance at
https://mindattic-gadfly.azurewebsites.net runs in mock mode.

## Using Gadfly from your own app

Two ways, each with a runnable sample that works offline in mock mode (no keys, no cost); see
[samples/README.md](samples/README.md):

- **As packages:** `Gadfly.Core` (the engine, `ISubjectMatter`, `ISocraticVotingClient` and the mock panel) and
  `Gadfly.Sources` (documents to concepts, rubrics and the step-driven exam), both 1.0.0.
  [samples/InProcess](samples/InProcess) runs a scripted session over a markdown file with `dotnet run`.
- **As a hosted service:** the [platform API](docs/API.md) with an organization API key, signed launch links and
  signed webhooks. [samples/HostedApi](samples/HostedApi) is a stand-in LMS that does the whole flow.

## The personal introspection CLI

`gadfly` mines your own [Claude Code](https://claude.com/claude-code) transcripts and your claude.ai
conversation export into one local corpus, then cross-examines you about it, Socrates-style, through
Legion's multi-LLM voting, until you declare *Aporia*. Everything from here down is about the CLI.

## Why the CLI

Most "reflect on your own thinking" exercises run on an AI's guesswork about you, not your
actual words. Gadfly runs the exercise on real evidence: the conversations you actually had,
pulled from where you actually work.

## CLI features

- **Two ingestion sources.** Local Claude Code session transcripts (`~/.claude/projects/**/*.jsonl`,
  read directly — no export needed) and a claude.ai account data export
  (Settings → Privacy → Export data → `gadfly import <zip>` — there is no live API for claude.ai
  history, so this file is the only path).
- **One local corpus.** Everything normalizes into a flat, deduped JSONL store under
  `%APPDATA%\MindAttic\Gadfly\corpus\`. Nothing leaves your machine except the LLM calls Gadfly
  itself makes when you run `interrogate`/`reflect`/`examine`.
- **Three irrevocable prompts.** The Socratic Interrogation, the Final Reflection, and the
  three-perspective Master Prompt are hardcoded constants — no settings file, flag, or
  `legion.json` can change their wording.
- **Legion-backed, not single-model.** The interrogation loop is a real multi-LLM vote every
  turn — four providers draft a question, a judge fuses the sharpest one.

## CLI quick start

```powershell
dotnet build Gadfly.slnx

# pull in your own Claude Code history
dotnet run --project Gadfly.Cli -- ingest

# (optional) pull in a claude.ai export too
dotnet run --project Gadfly.Cli -- import path\to\export.zip

dotnet run --project Gadfly.Cli -- corpus status

# the interrogation itself — type your replies, declare "Aporia" when you reach it
dotnet run --project Gadfly.Cli -- interrogate
```

> **This tool reads and transmits your own conversation history to third-party LLM providers.**
> `interrogate`/`reflect`/`examine` send your corpus digest to whichever providers your
> `%APPDATA%\MindAttic\LLM\providers.json` has keys for. `ingest`/`import`/`corpus status` make
> no network calls at all.

## What it is and is not

The CLI is a personal introspection tool, not a product. It does not psychoanalyze you on its
own — the belief-identification is deliberately left to the LLM panel, the same way Prompt 1
asks for it. `ClaimExtractor` only surfaces phrases you've repeated verbatim across distinct
sessions, as a hint, not a verdict.

## Commands

| Command | Purpose |
|---|---|
| `gadfly ingest [opts]` | Scan `~/.claude/projects/**/*.jsonl` into the corpus. Idempotent. |
| `gadfly import <zip> [opts]` | Import a claude.ai data export by path; `gadfly import --downloads` takes the newest `*.zip` in Downloads. |
| `gadfly corpus status [--verbose]` | Message counts by source, last-ingest time, digest-cache freshness. |
| `gadfly interrogate [opts]` | Prompt 1 — Socratic Elenchus REPL until you reach Aporia. |
| `gadfly reflect [opts]` | Prompt 2 — one-shot Final Reflection. |
| `gadfly examine [opts]` | Prompt 3 — Socrates/Confucius/Buddha, three perspectives. |
| `gadfly exam --source <path> [opts]` | A Socratic exam over your own source material, judged by the panel against it. |

Run `gadfly <command> --help` for every command's full option list (tier, voters, quorum, etc).

### gadfly ingest

Scans `~/.claude/projects/**/*.jsonl`, keeping only genuinely human-typed messages and the
model's own `text` blocks (skipping `thinking`/`tool_use` blocks and tool-result content).
Project folders that only hold Claude Code's own `memory/*.md` notes — no raw transcript — are
skipped, not an error. Safe to re-run: already-ingested messages are deduped by a stable
per-message id.

It reads the **current login's** store: `%CLAUDE_CONFIG_DIR%\projects` when Claude Code runs
under a per-account config dir, else `~/.claude/projects`. `--all-accounts` scans every login on
the machine (`~/.claude` and each `~/.claude-accounts/*`). If an interrupted write leaves a torn
last line in the corpus, commands skip it with a warning and the next `ingest`/`import` sets it
aside in `messages.torn.txt` and re-ingests that message.

`interrogate --voters/--single-voter` and `examine --voters` refuse (exit 2) any provider id
outside the trusted set (claude, openai, gemini, deepseek) instead of silently dropping it.

### gadfly import

Imports an Anthropic account data export. **The export schema was not verified against a real
file as of v1** — the importer is written defensively and raises a clear error naming what
didn't match rather than silently ingesting partial data. Expect to need one fix the first time
you run this against a real export.

`--downloads` skips the explicit path and picks the newest `*.zip` in the user's Downloads
folder — for chaining after a browser-automation task (e.g. MindAttic.Automata) that requests
the claude.ai export and downloads the resulting zip. It still only picks the single newest zip;
it does not disambiguate between multiple candidates, so an unrelated recent zip in Downloads
will surface as whatever error the schema check raises rather than being skipped silently.

### gadfly interrogate

Prompt 1, run as a terminal REPL. Each turn is a real Legion vote across the trusted panel
(claude, openai, gemini, deepseek) plus a judge — four LLMs each draft a probing question
against your corpus digest and the dialogue so far, and the judge picks the sharpest one. Ends
when you type `Aporia` (case-insensitive) or `--max-turns` is hit. The transcript is saved to
`%APPDATA%\MindAttic\Gadfly\sessions\` so `gadfly reflect --last` can pick it back up.

### gadfly reflect

Prompt 2, one-shot. Add `--last` to fold in your most recently saved `interrogate` transcript.

### gadfly examine

Prompt 3, one-shot. Three named voters — Socrates, Confucius, Buddha — each a distinct LLM
provider, each reading the same corpus digest. Prints all three individual answers; there is no
synthesized consensus, since the point is three voices, not one merged one.

### gadfly exam

A text-only Socratic exam over source material in the terminal: the same engine Gadfly Exams runs,
without the web app (see [Socratic exam platform](docs/SOCRATIC_PLATFORM.md)). Point it at a folder (or
file) of notes (`.txt`, Markdown, PDF, DOCX, EPUB or HTML): the panel builds a concept map and a rubric
per concept (kept under
`%APPDATA%\MindAttic\Gadfly\sources\<collection>\`, editable, reused while the material is unchanged),
then examines you concept by concept. Each question is drafted by the panel for a planned move
(elicit, clarify, probe, counterexample, apply, contradiction, synthesize) from your previous answer;
each answer is judged against the source passages with citations; contradictions between your own
claims are put back to you. It ends at mastery, aporia, the time budget or the turn limit, and writes a
Markdown and JSON report plus the full session transcript under `%APPDATA%\MindAttic\Gadfly\exams\`.

```powershell
dotnet run --project Gadfly.Cli -- exam --source path\to\notes --list-concepts
dotnet run --project Gadfly.Cli -- exam --source path\to\notes --concepts calvin-cycle --minutes 10 --verbose
```

PDF, DOCX, EPUB and HTML are read through `Tutor.Ingestion`; a scanned, image-only PDF is not read.

## How it works

```
~/.claude/projects/**/*.jsonl ──┐                 ┌── claude.ai export .zip
                                 ▼                 ▼
                           CorpusMessage[] ──► CorpusStore (append, dedupe)
                                                        │
                                               CorpusRepository (query + CorpusHash)
                                                        │
                      ClaimExtractor ──► CorpusSummarizer (map-reduce, cached) ──► ContextBuilder
                                                        │
                                   corpus digest + the three canonical prompts
                                                        │
                       GadflyConfiguration (VotingConfiguration + legion.json + tier)
                                                        │
                                            MindAttic.Legion (LlmVotingService)
                                                        │
                InterrogationSession / ReflectionRunner / MasterExaminationRunner → console
```

## CLI configuration

| Source | What it controls |
|---|---|
| `%APPDATA%\MindAttic\LLM\providers.json` | LLM provider API keys (shared MindAttic.Vault bucket — Gadfly has no credential store of its own). |
| `legion.json` (repo root) | Voter panel (`voters`), judge (`judge`), per-provider model overrides (`models`), concurrency cap (`maxConcurrency`). **Not** tier — `"tier"` is not a real `legion.json` field; use `--tier` instead. |
| `--tier` | Model tier per command: low, medium, high, higher or highest. Default high; this is a reflective, high-stakes tool, not a cheap-and-fast one. |
| `%APPDATA%\MindAttic\Gadfly\corpus\` | The local corpus (`messages.jsonl`, `manifest.json`, `digest-cache.json`). |
| `%APPDATA%\MindAttic\Gadfly\sessions\` | Saved `interrogate` transcripts. |

## Building and testing

```powershell
dotnet build Gadfly.slnx
dotnet test Gadfly.slnx
```

`Gadfly.Tests` is NUnit. It covers the CLI's corpus code (JSONL parsing and filtering, export-zip
schema validation, dedupe, `ClaimExtractor`, digest chunking and caching, the canonical-prompt verbatim
guard, tier resolution, the interrogation loop driven by a fake voting client), the engine, the
assessment rules, and the server: an in-process `Gadfly.Server` on a uniquely named LocalDB database
that the app migrates and the run drops. The live `interrogate`, `reflect` and `examine` runs against real
provider keys stay manual; output quality isn't NUnit's job. `Gadfly.Tour` is the end-to-end check for
the web app.

## Project layout

```text
Gadfly/             Gadfly.Core: the subject-agnostic Socratic engine (session, moves, claim ledger, planner, question generator, report), the assessment rules (mastery levels, grade status machine, scoring), the Legion and mock voting clients, tier resolver, configuration
Gadfly.Sources/     Source material as a subject: ingestion and passages (including PDF, DOCX, EPUB, HTML), concept map and rubric builders, grounding retrieval, the panel answer judge, text-style assessment, the step-driven exam
Gadfly.Server/      Gadfly Exams: the Blazor Server web app, EF Core and SQL Server data, accounts and tenancy, background jobs, session orchestration, grading, review, voice, privacy, the platform API and webhooks
Gadfly.Cli/         The gadfly executable and its command classes
Gadfly.Personal/    The personal introspection subject: ingestion, corpus, mining, the three irrevocable prompts and their runners
Gadfly.Tour/        The end-to-end demo tour that drives a real server as real users
Gadfly.Tests/       NUnit tests and fixtures
samples/            InProcess (the engine as packages) and HostedApi (the platform API)
tools/              provision-azure.ps1 and sync-voice-assets.ps1
lib/                Vendored MindAttic NuGet packages used by CI builds
```

The engine knows nothing about where its subject comes from: `Gadfly.Personal` (your transcripts) and
`Gadfly.Sources` (an instructor's material) are two implementations of its `ISubjectMatter`, and the CLI
and `Gadfly.Server` are two hosts. The full plan is in [docs/SOCRATIC_PLATFORM.md](docs/SOCRATIC_PLATFORM.md).

## Glossary

- **Corpus** — the local, deduped store of your own normalized messages from both ingestion sources.
- **Corpus digest** — the map-reduced summary of the corpus actually handed to the three prompts.
- **Aporia** — the state of productive puzzlement Prompt 1's interrogation is built to reach; in an exam,
  an honest "I don't know".
- **Trusted four** — claude, openai, gemini, deepseek; the only providers Gadfly's panel will ever use.
- **Concept map** — the concepts the panel finds in a source collection, with definitions, key facts,
  misconceptions and the passages they come from.
- **Rubric** — per concept: the points a real understanding covers (weighted), acceptable variants and
  misconceptions, each citing its passages. Versioned; an instructor's edit is never overwritten.

## Limitations

- Reports export as Markdown and CSV; a PDF export is not built.
- Integration is through the platform API and the packages; there are no ready-made LMS connectors or
  billing.
- The master plan's validity studies (Part 15.3) happen with real institutions after launch; Gadfly
  makes no validity claims until then.
- The hosted instance runs in mock mode, so its questions and scores are templates, not AI judgments.

## Documentation

| What | Where |
|---|---|
| Instructors: material, concepts and rubrics, exams, review and grading | [Instructor guide](docs/guides/INSTRUCTOR_GUIDE.md) |
| Students: how the exam works, what is recorded, results | [Student guide](docs/guides/STUDENT_GUIDE.md) |
| Owners and admins: organizations, members, MFA, retention, keys, webhooks | [Admin guide](docs/guides/ADMIN_GUIDE.md) |
| Integrators: the platform API v1, launch links, webhook signatures | [API reference](docs/API.md) |
| The owner's product plan, and what is built against it | [Master plan](docs/MASTER-PLAN.md), [status](docs/MASTER-PLAN-STATUS.md) |
| Requirements traceability, architecture, schema, state machines | [Design](docs/DESIGN.md) |
| The Socratic exam platform: plan, milestones, open decisions | [Socratic platform](docs/SOCRATIC_PLATFORM.md) |
| What is collected, for how long, and the regulatory notes | [Privacy](docs/PRIVACY.md) |
| Voice exam room, avatar and audio signals | [Voice](docs/VOICE.md) |
| The demo tour and mock mode | [Tour](docs/TOUR.md) |
| Deploying to Azure | [Deploy](docs/DEPLOY.md) |
| Security review findings | [Security review](docs/SECURITY_REVIEW.md) |
| User stories (generated) | [User stories](docs/USER_STORIES.md) |
| Using the engine or the API from your own app | [Samples](samples/README.md), [Gadfly.Core](Gadfly/README.md), [Gadfly.Sources](Gadfly.Sources/README.md) |
| Agent and contributor instructions | [AGENTS.md](AGENTS.md) |

## License

MIT, as declared in the project files.

---

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic).
Related: [MindAttic.Legion](https://github.com/mindattic/MindAttic.Legion), the multi-LLM panel underneath.
