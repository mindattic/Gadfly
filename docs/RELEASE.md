---
layer: release
status: living
---
# Gadfly — Release charter

> The one finish line this repo is working toward. Every open ticket in this repo sits under one of the epics
> below, and every epic is part of the milestone. Work that does not fit is out of scope until this milestone is
> reached (owner, 2026-11-27, MAT-T319).

## 1. What this is
Gadfly Exams: a Blazor Server + SQL Server web platform where a talking examiner runs a spoken Socratic exam over
an instructor's own course material, an AI panel and a blind human grader score it, and a person approves every
grade. Underneath sit the Socratic engine and the original personal-introspection CLI (`gadfly`).

## 2. The milestone: Gadfly local pilot with Erin {#milestone}
- **Milestone:** MS-7, "Gadfly local pilot with Erin". It follows MS-2 ("Text-only Socratic exam in the CLI",
  all 15 members closed) and takes over the earlier "Production and pilot" plan, whose Azure hosting is gone.
- **Published means:** not a public release. Gadfly runs locally from `R:\MindAttic\Apps\Gadfly\Release`
  (`run.ps1`), on local SQL Server, reachable by Erin for the pilot. Azure was decommissioned on 2026-11-26
  (MAT-T297) and no cloud host replaces it.
- **The natural stopping point:** the product is built (docs/MASTER-PLAN-STATUS.md, docs/DESIGN.md mark almost
  every part "built"); what is missing is a real teacher using it. When Erin has built an exam from her own
  material, run at least five real voice sessions, and her feedback is filed as tickets, Gadfly has answered the
  question the pilot exists to answer (docs/PILOT.md) and the next direction can be chosen from evidence.

## 3. Done when (acceptance checks)
1. build — `Gadfly.slnx` builds clean on main.
2. exists — `R:\MindAttic\Apps\Gadfly\Release\Gadfly.Server.exe` (the local pilot host).
3. signoff — Erin's pilot: her course and exam exist, five or more real voice sessions are reviewed, and her
   feedback is filed as Gadfly tickets (GAD-T66).

## 4. Epics, in priority order
| Priority | Epic (ticket) | What it delivers | Features |
|---|---|---|---|
| 900 | GAD-T826 Pilot readiness | A local pilot host and the owner's pilot decisions | GAD-T827 Local pilot host (990), GAD-T828 Owner pilot decisions (980) |
| 800 | GAD-T829 Pilot with Erin | The pilot run end to end and the grading-quality evidence it yields | GAD-T66 Pilot with Erin (890: T664 demo org, T665 course, T666 five sessions, T667 feedback), GAD-T830 Grading quality evidence (880: T670 fast-model agreement, T614 Spanish tester validation) |

## 5. Out of scope (not before this milestone)
- Cloud hosting of any kind (Azure, Hetzner): Azure is decommissioned and apps run locally (GAD-T784..T786
  closed wont-do).
- Public launch, billing and marketing site work: wait for the pilot's verdict.
- New exam languages beyond English, Korean and Spanish; Spanish stays human-approved until a native tester
  validates it (GAD-T614).
- New examiner avatars, art or voice options beyond what ships today.
- Audit-generated test sweeps, bughunts and "continuous" re-run tickets.
- New features from the master plan's deferred list until the pilot's feedback tickets are prioritised.

## 6. Current verified state
- MS-2 reached: `gadfly exam` runs a grounded, Legion-judged Socratic exam in the terminal (GAD-T26..T38).
- The web platform's exam, grading, calibration, integrity, privacy and admin parts are built
  (docs/DESIGN.md §1, docs/MASTER-PLAN-STATUS.md); English, Korean and Spanish exams (GAD-T596's children
  GAD-T607..T613 closed).
- Shared run menu: `run.ps1 -Redeploy` puts Gadfly.Server, Gadfly.Worker and `gadfly.exe` in
  `R:\MindAttic\Apps\Gadfly\{Debug,Release}` (commit 80248d3; both folders present 2026-11-27).
- Azure deploy scripts removed after the decommission (commit 1789669, GAD-T804).

## 7. Decision record
- 2026-11-27 — charter drafted from README/DESIGN/MASTER-PLAN-STATUS/PILOT/DEPLOY and the open tickets; Legion
  vote: Approve, 9/9 voters (plurality quorum reached, consensus 1.0); no dissent (all voters routed through the xai provider).
- 2026-11-28 — owner-requested epic GAD-T833 "Professional UI overhaul" (priority 1000) worked ahead of the pilot epics and
  closed: Greek theme replaced, orb-only examiner pulsing per spoken word, login-first startup (brochure at /about), every
  published exam open to every student with a type-to-filter list, exams always spoken (Text mode removed), five
  screenshot refinement passes (GAD-T848, T850, T852, T853, T854).
- Triage: 13 open (plus this job ticket) → done 2 (GAD-T806, GAD-T596), duplicate 1 (GAD-T514 of GAD-T670), wont-do 3 (GAD-T784..T786, Hetzner hosting), kept 7, new tasks 2 (GAD-T831, GAD-T832).
