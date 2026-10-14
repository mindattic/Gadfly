# Gadfly

A CLI that mines your own [Claude Code](https://claude.com/claude-code) transcripts and your
claude.ai conversation export into one local corpus, then cross-examines you about it,
Socrates-style, through [MindAttic.Legion](https://github.com/mindattic/MindAttic.Legion)'s
multi-LLM voting — until you declare *Aporia*.

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

Gadfly is a personal introspection tool, not a product. It does not psychoanalyze you on its
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

Run `gadfly <command> --help` for every command's full option list (tier, voters, quorum, etc).

### `gadfly ingest`

Scans `~/.claude/projects/**/*.jsonl`, keeping only genuinely human-typed messages and the
model's own `text` blocks (skipping `thinking`/`tool_use` blocks and tool-result content).
Project folders that only hold Claude Code's own `memory/*.md` notes — no raw transcript — are
skipped, not an error. Safe to re-run: already-ingested messages are deduped by a stable
per-message id.

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
Gadfly/                  Core library (ingestion, corpus, mining, Legion wiring, Socratic runners)
Gadfly.Cli/               The `gadfly` executable and its command classes
Gadfly.Tests/             NUnit tests + fixtures
```

## Glossary

- **Corpus** — the local, deduped store of your own normalized messages from both ingestion sources.
- **Corpus digest** — the map-reduced summary of the corpus actually handed to the three prompts.
- **Aporia** — the state of productive puzzlement Prompt 1's interrogation is built to reach.
- **Trusted four** — claude, openai, gemini, deepseek; the only providers Gadfly's panel will ever use.

## License

MIT

---

Part of MindAttic — see more at [github.com/mindattic](https://github.com/mindattic).
