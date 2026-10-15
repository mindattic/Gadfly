---
codex: 1
project: Gadfly
code: GAD
layer: stories
status: living
updated: 2026-10-13
---
<!-- workflow-db: this repo's stories and tickets live in the MindAttic.Workflow database -->

# Gadfly — User Stories

This repo's stories and tickets (GAD-US-…, GAD-T…) live in the MindAttic.Workflow database
(`MindAttic.Workflow/data/workflow.db`), not in this file. Use the `workflow` CLI:

- Read: `workflow story list --repo Gadfly`, `workflow ticket list --repo Gadfly`, `workflow ticket show <ID>`
- Change: `workflow ticket new` / `close`, `workflow story status <ID> done|partial|planned`, `workflow ticket link <A> requires <B>`
- This file as it was, rebuilt from the database: `workflow db export --repo Gadfly`
