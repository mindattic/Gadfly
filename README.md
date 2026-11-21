# Gadfly

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

## Using Gadfly from your own app

Two ways, each with a runnable sample that works offline in mock mode (no keys, no cost); see
[samples/README.md](samples/README.md):

- **As packages:** `Gadfly.Core` (the engine, `ISubjectMatter`, `ISocraticVotingClient` and the mock panel) and
  `Gadfly.Sources` (documents to concepts, rubrics and the step-driven exam), both 1.0.0.
  [samples/InProcess](samples/InProcess) runs a scripted session over a markdown file with `dotnet run`.
- **As a hosted service:** the [platform API](docs/API.md) with an organization API key, signed launch links and
  signed webhooks. [samples/HostedApi](samples/HostedApi) is a stand-in LMS that does the whole flow.

## Gadfly Exams

| What | Where |
|---|---|
| Instructors: material, concepts and rubrics, exams, review and grading | [Instructor guide](docs/guides/INSTRUCTOR_GUIDE.md) |
| Students: how the exam works, what is recorded, results | [Student guide](docs/guides/STUDENT_GUIDE.md) |
| Owners and admins: organizations, members, MFA, retention, keys, webhooks | [Admin guide](docs/guides/ADMIN_GUIDE.md) |
| Integrators: the platform API v1, launch links, webhook signatures | [API reference](docs/API.md) |
| The plan, milestones and open decisions | [Socratic exam platform](docs/SOCRATIC_PLATFORM.md) |
| What is collected, for how long, and the regulatory notes | [Privacy](docs/PRIVACY.md) |

Built today: organizations and roles with MFA for staff, source upload (`.txt`/Markdown) and processing
into an editable concept map and versioned rubrics, the exam builder and assignments, the student
dashboard with system check and consent, the text exam room, instructor review (timeline, citations,
each judge's verdict, integrity notes) with grading, release and CSV/Markdown export, data retention,
export and erasure, and the platform API. Pending: voice answers, the talking avatar, webcam, audio and
browser integrity signals, PDF/DOCX/EPUB/HTML upload, usage metering, the org admin console, email
invitations, and Azure hosting.

### Running the server locally

```powershell
dotnet build Gadfly.slnx
dotnet run --project Gadfly.Server
```

Then open `https://localhost:7311` (HTTP on 5311).

| Needs | Detail |
|---|---|
| .NET 10 SDK | |
| SQL Server Express LocalDB | The default connection is `(localdb)\MSSQLLocalDB`, database `Gadfly`. In Development the database is created and migrated on start. Set `ConnectionStrings__Gadfly` to use another server; outside Development, run once with `--migrate`. |
| MindAttic.Vault files | The `Security` bucket (the sign-in library's pepper and `bootstrap-token`) and the `LLM` bucket (provider keys for the Legion panel). Sign-in fails closed without the Security secrets. |

**First sign-in.** When the database has no accounts, the server creates the platform SuperAdmin:
user name `admin`, password the Vault's `Security:bootstrap-token`. Sign in, choose a new password, set
up an authenticator app, and rotate the bootstrap token in the Vault. Then open **Organizations** to
create a customer organization and its owner, who adds instructors and students under **Members**.

In Development, organization staff aren't asked for MFA unless `Gadfly:RequireMfaInDevelopment` is
`true`. Uploaded files are kept under `%APPDATA%\MindAttic\Gadfly\uploads\<organization>\<collection>\`.
Logs go to MindAttic.Log's database for `Gadfly.Server`. The OpenAPI document is at `/openapi/v1.json`
and the health check at `/healthz`.

## The personal introspection CLI

`gadfly` mines your own [Claude Code](https://claude.com/claude-code) transcripts and your claude.ai
conversation export into one local corpus, then cross-examines you about it, Socrates-style, through
Legion's multi-LLM voting, until you declare *Aporia*. Everything from here down is about the CLI.

## Why

Most "reflect on your own thinking" exercises run on an AI's guesswork about you, not your
actual words. Gadfly runs the exercise on real evidence: the conversations you actually had,
pulled from where you actually work.

## Features

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

## Quick start

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
| `gadfly import <zip> \| --downloads [opts]` | Import a claude.ai data export, by path or newest `*.zip` in Downloads. |
| `gadfly corpus status [--verbose]` | Message counts by source, last-ingest time, digest-cache freshness. |
| `gadfly interrogate [opts]` | Prompt 1 — Socratic Elenchus REPL until you reach Aporia. |
| `gadfly reflect [opts]` | Prompt 2 — one-shot Final Reflection. |
| `gadfly examine [opts]` | Prompt 3 — Socrates/Confucius/Buddha, three perspectives. |
| `gadfly exam --source <path> [opts]` | A Socratic exam over your own source material, judged by the panel against it. |

Run `gadfly <command> --help` for every command's full option list (tier, voters, quorum, etc).

### `gadfly ingest`

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

### `gadfly import <zip>`

Imports an Anthropic account data export. **The export schema was not verified against a real
file as of v1** — the importer is written defensively and raises a clear error naming what
didn't match rather than silently ingesting partial data. Expect to need one fix the first time
you run this against a real export.

`--downloads` skips the explicit path and picks the newest `*.zip` in the user's Downloads
folder — for chaining after a browser-automation task (e.g. MindAttic.Automata) that requests
the claude.ai export and downloads the resulting zip. It still only picks the single newest zip;
it does not disambiguate between multiple candidates, so an unrelated recent zip in Downloads
will surface as whatever error the schema check raises rather than being skipped silently.

### `gadfly interrogate`

Prompt 1, run as a terminal REPL. Each turn is a real Legion vote across the trusted panel
(claude, openai, gemini, deepseek) plus a judge — four LLMs each draft a probing question
against your corpus digest and the dialogue so far, and the judge picks the sharpest one. Ends
when you type `Aporia` (case-insensitive) or `--max-turns` is hit. The transcript is saved to
`%APPDATA%\MindAttic\Gadfly\sessions\` so `gadfly reflect --last` can pick it back up.

### `gadfly reflect`

Prompt 2, one-shot. Add `--last` to fold in your most recently saved `interrogate` transcript.

### `gadfly examine`

Prompt 3, one-shot. Three named voters — Socrates, Confucius, Buddha — each a distinct LLM
provider, each reading the same corpus digest. Prints all three individual answers; there is no
synthesized consensus, since the point is three voices, not one merged one.

### `gadfly exam`

A text-only Socratic exam over source material in the terminal: the same engine Gadfly Exams runs,
without the web app (see [Socratic exam platform](docs/SOCRATIC_PLATFORM.md)). Point it at a folder (or
file) of `.txt` or Markdown notes: the panel builds a concept map and a rubric per concept (kept under
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

PDF, DOCX, EPUB and HTML arrive when Tutor's parsers are shared as `Tutor.Ingestion`.

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

## Configuration

| Source | What it controls |
|---|---|
| `%APPDATA%\MindAttic\LLM\providers.json` | LLM provider API keys (shared MindAttic.Vault bucket — Gadfly has no credential store of its own). |
| `legion.json` (repo root) | Voter panel (`voters`), judge (`judge`), per-provider model overrides (`models`), concurrency cap (`maxConcurrency`). **Not** tier — `"tier"` is not a real `legion.json` field; use `--tier` instead. |
| `--tier low\|medium\|high\|higher\|highest` | Model tier per command (default `high` — this is a reflective, high-stakes tool, not a cheap-and-fast one). |
| `%APPDATA%\MindAttic\Gadfly\corpus\` | The local corpus (`messages.jsonl`, `manifest.json`, `digest-cache.json`). |
| `%APPDATA%\MindAttic\Gadfly\sessions\` | Saved `interrogate` transcripts. |

## Building and testing

```powershell
dotnet build Gadfly.slnx
dotnet test Gadfly.slnx
```

Unit-tested: JSONL parsing/filtering, export-zip schema validation, corpus dedupe,
`ClaimExtractor`, digest chunking/caching, the canonical-prompt verbatim guard, tier resolution,
and the interrogation loop's turn mechanics and Aporia detection (driven by a fake voting client).
Left manual: the live `interrogate`/`reflect`/`examine` runs against real provider keys — output
quality isn't NUnit's job.

## Project layout

```
Gadfly/                  Gadfly.Core: the subject-agnostic Socratic engine (session, moves, claim ledger, planner, question generator, report; Legion voting client, tier resolver, configuration)
Gadfly.Sources/           Source material as a subject: ingestion and passages, concept map and rubric builders, grounding retrieval, the panel answer judge, text-style assessment
Gadfly.Server/            Gadfly Exams: the Blazor Server web app, EF Core/SQL Server data, accounts and tenancy, background jobs, session orchestration, review, privacy, the platform API and webhooks
Gadfly.Personal/          The personal introspection subject: ingestion, corpus, mining, the three irrevocable prompts and their runners
Gadfly.Cli/               The `gadfly` executable and its command classes
Gadfly.Tests/             NUnit tests + fixtures
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

## License

MIT

---

Part of MindAttic — see more at [github.com/mindattic](https://github.com/mindattic).
