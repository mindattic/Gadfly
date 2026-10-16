---
codex: 1
project: Gadfly
code: GAD
layer: stories
status: living
updated: 2026-10-13
---
<!-- GENERATED from MindAttic.Workflow/data/workflow.db by `workflow db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the workflow CLI. -->

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
