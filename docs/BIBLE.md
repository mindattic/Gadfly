---
layer: bible
status: living
---
# Gadfly — Project Bible

> Single source of truth for what Gadfly IS, is NOT, and the rules that keep it coherent.
> README says how to build and run it; this says how to think about it. The finish line lives in
> `docs/RELEASE.md`.

## 1. The one sentence {#GAD-§1}
Gadfly Exams finds out whether a student understands, not whether they can produce an answer: a talking
examiner asks Socratic follow-ups written from the student's own spoken words, grounded in the instructor's
course material, and an AI panel plus a blind human grader score it before a person approves every grade.

## 2. The product promise {#GAD-§2}
- Every question and judgment is grounded in the instructor's material, with citations.
- Every answer is kept exactly as given; a human is accountable for every grade (blind human grading,
  reconciliation of large disagreements, approval before a student sees anything).
- Integrity signals are low-confidence notes for a reviewer, never an automatic verdict; video never leaves the
  student's device (docs/PRIVACY.md).
- Fair to students who think, speak or hear differently: looking away to think costs nothing, second-language
  speakers are graded on content (English, Korean, Spanish exams).
- Free to try: development runs a clearly labelled mock AI panel with no provider key.

## 3. What it is NOT {#GAD-§3}
- Not a cloud-hosted service today: Azure was decommissioned 2026-11-26 (MAT-T297); Gadfly runs locally.
- Not an automated proctor or grader: model output is a recommendation, never a decision.
- Not a typed quiz: every exam is spoken; typing survives only as a staff-granted accommodation (GAD-T260).

## 4. Architecture canon {#GAD-§4}
```text
 Gadfly.Cli (gadfly) ──► Gadfly (engine) ◄── Gadfly.Sources (ingestion, concept map, rubric, retrieval, judge)
                             ▲                     ▲
 Gadfly.Personal ────────────┘                     │
 Gadfly.Server (Blazor Server, SessionHub, API) ───┤──► SQL Server (EF Core 10), MindAttic.Legion panel
 Gadfly.Worker (job queue, sweepers) ──────────────┘
```
### 4.1 Projects (Gadfly.slnx)
Gadfly (engine: `SocraticSession`, `QuestionPlanner`, moves), Gadfly.Personal (introspection over Claude
transcripts), Gadfly.Sources, Gadfly.Server, Gadfly.Worker, Gadfly.Cli, Gadfly.Tests (NUnit), Gadfly.Tour
(end-to-end demo tour).
### 4.2 Nouns
Organization, Exam, Assessment, Concept/objective, Rubric, ExamSession, Turn, StudentResponse, claim ledger,
AiObjectiveGrade, HumanObjectiveGrade, FinalGrade, integrity note.
### 4.3 Verbs
Ingest → build concept map and rubric → plan the next move → generate a question → judge the answer
(fast provisional model steers, the panel grades) → grade blind → reconcile → approve and publish.

## 5. The Laws {#GAD-§5}
Inherits `MindAttic.HouseRules.md` by reference.
- {#GAD-LAW-1} **Conflict order.** Trade-offs follow docs/DESIGN.md: student safety, privacy and fairness;
  transcript and evidence integrity; assessment validity; human grading accountability; functional correctness;
  maintainability; performance; additional features.
- {#GAD-LAW-2} **Rules live in code, not model output.** Scores, thresholds, permissions and state transitions
  are plain application code (`Gadfly/Assessment`); model output is schema-validated and advisory.
- {#GAD-LAW-3} **No automated accusation.** Integrity signals go to a person; nothing is cancelled or deleted
  automatically.
- {#GAD-LAW-4} **Secrets come from MindAttic.Vault,** never a literal in config (DEPLOY.md).
- {#GAD-LAW-5} **Prompt sets are versioned and hashed** into each session (`ExamPrompts.Set`, SHA-256).

## 6. Verified state {#GAD-§6}
- MS-2 reached: `gadfly exam` runs a grounded, Legion-judged exam in the terminal (GAD-T26..T38).
- Web platform parts marked built in docs/DESIGN.md §1 and docs/MASTER-PLAN-STATUS.md.
- `run.ps1 -Redeploy` deploys Server, Worker and CLI to `R:\MindAttic\Apps\Gadfly\{Debug,Release}` (80248d3).

## 7. Active frontier {#GAD-§7}
See `docs/RELEASE.md`: the local pilot with Erin (docs/PILOT.md).

## 8. Quality bar {#GAD-§8}
A change is done when `Gadfly.slnx` builds, the relevant docs (DESIGN, API, guides) match the code, and it is
committed once to main with its ticket ID. Tests run only when the owner asks.

## 9. Glossary {#GAD-§9}
- **Elenchus:** examining only what the student said and testing whether it holds together.
- **Panel:** the multi-model MindAttic.Legion judge; **fast model:** the provisional judge that steers the next
  question.
- **Calibration:** double-marked answers that decide whether the panel's grades count for an exam.
