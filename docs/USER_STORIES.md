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
| GAD-T14 | 2026-10-20 | docs | open | Publish Gadfly docs to GitHub Pages via the shared Agency DocFX pipeline: run adopt-docs-site.ps1 (README.md as home page, docs/*.md as articles, API reference only where the code builds in CI), enable Pages with build_type=workflow, set the repo homepage URL, retire tools/build-readme.ps1 and README.htm, confirm the docs Actions run is green and https://mindattic.github.io/Gadfly/ returns 200. Depends on MAT-T8. | — |
| GAD-T15 | 2026-10-22 | docs | closed | Update USER_STORIES.md | — |
| GAD-T16 | 2026-10-24 | docs | closed | Update USER_STORIES.md | — |
| GAD-T17 | 2026-10-31 | feature | closed | LAMP: move Gadfly's app-level settings and keys to MindAttic.Vault.Desktop (manifest, LoadOrSeed, shared named keys, cog opens Vault) | 2e3748b Gadfly.Tests (51/51); keys via M.LEGN-T44 LampKeyTests |
| GAD-T18 | 2026-10-31 | docs | closed | Update USER_STORIES.md | — |
| GAD-T19 | 2026-11-02 | docs | open | Adopt MindAttic.Agency docs-site pipeline, publish README to GitHub Pages (HOUSE-LAW-14) | — |
| GAD-T20 | 2026-11-02 | fix | closed | Bughunt sweep, two passes: find and fix real bugs across tracked source (user request 2026-11-02) | — |
| GAD-T21 | 2026-11-02 | fix | closed | Fix the open items the 2026-11-02 bughunt left for decision (ingest login scoping, torn jsonl tail, untrusted voters) | — |
| GAD-T22 | 2026-11-10 | chore | closed | Crash recovery: commit the command-catalog sync (.agent/commands/*.md) and regenerated docs/USER_STORIES.md left uncommitted by the session crash | — |
