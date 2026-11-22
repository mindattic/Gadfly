# The demo tour (Gadfly.Tour)

The owner asked for "a demo/tour so that you can self-validate your work as you go along" (GAD-T105). `Gadfly.Tour`
is that tour: a console app that starts a real Gadfly.Server (or targets one), seeds the demo institution, and then
drives the whole workflow end to end **through the real server, as real users**: the sign-in form with its
antiforgery token, Blazor pages, the platform API with an API key, and the SessionHub over SignalR. After every
stage it checks the outcome against the API and the database and prints a checklist line:

```
✓    Student starts the exam over the SessionHub (1.1s)
      session 0bc94259… running; first question: "In your own words, what is Multiplication as equal groups, …"
SKIP Ambiguous answer gets a neutral clarification (GAD-T87) (0.0s)
      not built: GAD-T87 - answers are not captured as responses and no clarification is offered
✗    …  (the reason)
```

The exit code is 0 when every stage passed or was skipped as not built yet, and 1 when any stage failed. Run it
after every change that touches the workflow; a ✗ names exactly what broke.

## How to run it

Build the server and the tour, then run the tour from inside the repository (normal PowerShell):

```powershell
cd D:\Projects\MindAttic\Gadfly
dotnet build Gadfly.Server\Gadfly.Server.csproj
dotnet build Gadfly.Tour\Gadfly.Tour.csproj
dotnet run --project Gadfly.Tour -- --fresh
```

It prints `Tour: N passed, 0 failed, M skipped (not built yet).` when everything that exists works.

| Option | Meaning |
|---|---|
| `--fresh` | Drop and re-create the LocalDB database `GadflyTour` first (the server migrates it on start). Refuses any database whose name doesn't contain "Tour". |
| `--live` | Run on the real LLM providers instead of the mock panel (slower, costs provider credit; the mock-label checks invert: nothing may be labelled mock). |
| `--port 7375` | Port of the server the tour starts (https on localhost, development certificate). |
| `--server <exe>` | The `Gadfly.Server.exe` to start (default `Gadfly.Server\bin\Debug\net10.0\Gadfly.Server.exe`). It is copied to `gadfly-tour-host.exe` beside it and started under that name, so tools that stop `Gadfly.Server` processes never kill the tour's server. |
| `--keep` | Leave the started server running afterwards, to look around in a browser; the tour prints the demo password. |
| `--url <https://host:port>` | Target a server that is already running (started with `--seed-demo`); pass the same `--password`, `--api-key` and, if it isn't `GadflyTour`, `--db <connection string>`. |
| `--verbose` | Print every HTTP request and hub step. |

When the tour starts the server it runs it with `ASPNETCORE_ENVIRONMENT=Development`, its own database
(`ConnectionStrings__Gadfly` → `GadflyTour`), `Gadfly__AiMode=mock` (or `live`), a demo password and demo API key
generated for the run (`Gadfly__Demo__Password`, `Gadfly__Demo__ApiKey`), and `--seed-demo`. The server's output goes
to a log file whose path the tour prints. The server is stopped when the tour ends (unless `--keep`).

## Mock mode (GAD-T100)

`Gadfly:AiMode` chooses the AI panel: `live` (MindAttic.Legion providers), `mock` (a deterministic stand-in), or
`auto` (live when at least one provider answers a tiny probe at startup, otherwise mock). Unset means `auto` in
development and `live` elsewhere, so production never silently runs on the mock. The mode is decided once per
process and never flips mid-run.

The mock (`Gadfly/Socrates/Mock/MockVotingClient.cs`) recognizes every exam prompt and answers with simple,
content-aware rules: questions are templates per move built from the concept title and the student's own words;
judge scores come from keyword overlap between the answer and the rubric (a good answer scores higher than a poor
one); claims are the answer's asserted sentences; "I don't know" is recognized; concept maps and rubrics are built
from the headings and sentences of the uploaded notes; ambiguity checks look for vague references and unfinished
sentences; objective grades map keyword coverage to levels 0–5. It never simulates an integrity signal (text style).

Nothing the mock produces is an AI evaluation, and nothing pretends to be: every reply comes from provider `mock`,
every rationale, verdict, explanation and rubric point a reviewer can see starts with `[MOCK]`, AI objective grades
are stored with `IsMock`, and every page shows a sticky **MOCK MODE** banner (`Components/Shared/MockBanner.razor`).
Questions shown to the student carry no marker (the banner labels the session).

## Demo institution (GAD-T103)

`Gadfly.Server --seed-demo` (development only) seeds **Gadfly Demo Academy**:

- accounts `demo.owner` (Owner), `demo.teacher` (Instructor), `demo.examiner` (Examiner), `demo.reviewer`
  (SecondReviewer), `demo.student` (Student), all with the password from `Gadfly:Demo:Password`, or a generated one
  printed once; no forced password change in development;
- an API key (`Gadfly:Demo:ApiKey`, or a generated one printed once);
- two courses, and the master plan's two demonstration assessments:
  - **Demo: Understanding multiplication** (Part 2.2), three objectives of 10 points: multiplication as equal
    groups and repeated addition; what each factor represents; how changing a factor changes the product;
  - **Demo: Interaction effects in regression** (Part 7.2), one objective, "Understand interaction effects in
    regression", maximum score 20;
- each with source notes (`Gadfly.Server/Demo/Material/*.md`) processed by the real source job, objectives grounded
  in the concepts found, three rubric criteria per objective (conceptual understanding, reasoning, application)
  with level descriptors 0–5, a locked grading policy of AI 40% / human 60% with additional review at 10 points and
  mandatory reconciliation at 20, blind human grading, and an assignment for `demo.student`.

Accounts, courses and collections are created before the server starts; the assessments are created as soon as the
source job has built the concept maps. Running it again completes a partial demo and never duplicates one.

## What each stage proves

| Stage | Proves |
|---|---|
| Server is up | The server starts, migrates a fresh database, and answers `/healthz`. |
| Demo seeded (GAD-T103) | The institution, five roles, two courses, both assessments with 3 + 1 objectives, 12 criteria and 72 level descriptors, objectives grounded in concepts of the processed notes, the 40/60 and 10/20 policy locked, assignments, `demo.seeded` audit events. |
| Sign-in, every role | Each account signs in through the real form with the antiforgery token and lands on the app, not on a forced step. |
| MOCK banner (GAD-T100) | In mock mode the teacher's and the student's pages carry the MOCK MODE banner; in live mode no banner. |
| Access control | Anonymous users are sent to sign-in, a student can't open the review pages, the API refuses calls without a key and accepts the demo key. |
| Teacher setup check | The source library and exam list show the demo; the API reports both collections Ready with concepts; the assessment setup UI (GAD-T85) is probed and reported. |
| Public site (GAD-T308) | Anonymous visitors get every public page (landing, how it works, pricing, science, security, privacy, terms, accessibility, contact) with its title and heading, and the branded 404 for an unknown address. Every page the tour opens is also checked for mis-encoded characters. |
| System readiness (GAD-T302) | Anonymous `/healthz/ready` answers 200 "ready" with each check's name and state only (database and migrations among them, none failing); the owner's `/admin/status` says Ready with its checks, app/AI/voice and database/jobs sections; the student is refused. |
| Help centre | `/help`, a search that finds the microphone topic, a public topic for anonymous visitors, the student guide asking anonymous visitors to sign in, "Topic not found", and role limits (the student can't read the admin guide; the owner can). |
| Admin pages | For the owner: dashboard, students (lists demo.student), a student profile, exams manager with its preview link, the preview page, the exam definition export, assignments, live sessions, audit log (and its CSV), email preview. The student and examiner are refused. Interactive actions on these pages are not driven. |
| LMS, plan, usage, settings | `/org/lti`, `/org/plan`, `/org/usage` and `/org/settings` (which offers the reconciler-approver rule) for the owner; refused to the student, LTI refused to the teacher. |
| Practice interview (GAD-T294) | My exams links to it; `/my-exams/practice` opens for the student and writes no session, audit or integrity row. The spoken practice needs a browser and is not driven. |
| Opening questions and rubric approval (GAD-T436, GAD-T437) | The objectives step shows each objective's opening question; the review step's approval panel agrees with the database (approved, stale, or "published before approval was required" with an Approve button). The demo exams are seeded already published, so the tour never meets the publish gate; drafting and approving are buttons and are not driven. |
| Source processing via API | Create a collection, upload notes, process, poll the job to Succeeded, read the concept map (in mock mode: concepts follow the notes' headings). |
| Student starts the exam | The student connects to the SessionHub with their cookie, gives consent, starts the attempt; the session is Running with consent recorded; the question carries no mock marker. |
| A good answer is judged | The answer is stored on its turn and judged; in mock mode every judge vote is from `mock`, the summary is labelled, and a strong answer scores high. |
| Ambiguous answer → clarification (GAD-T87) | A vague answer gets a neutral meaning clarification (no "Do you mean", no options); the reply is stored beside the unchanged original and the response becomes Clarified. |
| Typo correction (GAD-T88) | `CorrectAnswer` fixes a typo: the original keeps the typo, a Transcription version holds the fix, and grading uses it. |
| Substantive revision (GAD-T88) | `ReviseAnswer` records a new SubstantiveRevision response pointing at the unchanged original. |
| "I don't know" | Honest aporia is recorded and never scored above zero. |
| End of exam | `Finish` ends the session and the report is written. |
| Labels | Mock: all judge votes from `mock`, the report carries `[MOCK]`, AI grades flagged `IsMock`, no student-facing question marked. Live: no mock votes or grades. |
| AI grading per objective (GAD-T94) | AI objective grades appear for every objective, scores within 0..max, mock grades labelled. |
| Blind human grading (GAD-T95) | Once AI grading has left the grade at Human Review Pending, the **examiner** reads the grading status and gets no AI score, combined score, final percentage or disagreement (`aiVisible: false`; in mock mode no `[MOCK]` explanation either), then submits a grade for every objective over HTTP. Rows are `WasBlind`, by the examiner, audited `grade.human.recorded`; afterwards the AI's evaluation is visible. |
| Deliberate disagreement (GAD-T96) | One objective was graded 40 points from the AI on purpose (the others agree): a `MandatoryReconciliation` disagreement of 40 points opens, the grade is Reconciliation Required, and the **teacher's** approval and publication are refused (`409`, two `grade.transition.refused` events, no approval record). |
| Reconciliation (GAD-T97) | The teacher requests a second review by `demo.reviewer`; the **second reviewer** sees the disagreement, documents the cause and resolution with a resolved score; the reconciliation row names them as reviewer and second reviewer, the disagreement is resolved, the score is recalculated (`grade.reconciled`, `grade.recalculated`), and the grade is Awaiting Final Approval. |
| Approval and publication (GAD-T98, Scenario J) | The **examiner** tries to approve and to publish: both `403`, two `access.denied` events, grade unchanged. The teacher approves (Finalized, Finalize record); the **student** is refused the status and the report while it is only finalized; the teacher publishes (Published, Publish record, `grade.approved` and `grade.published`). |
| Student sees the published result (GAD-T98) | The student's status shows Published with the same final percentage as the database and no reviewer names; `/my-results/{id}`, its `report.md` and `/my-exams` show the exam. |
| Appeal and amendment (GAD-T98, Scenario J) | The student appeals (Under Appeal, open appeal row). The examiner's amendment and publication and the student's amendment are refused and audited (`access.denied`, 2 + 1). The teacher amends a non-reconciled objective with the appeal id: Amended, the final percentage changes, a new human grade by the teacher supersedes the examiner's, an Amend record, the appeal Amended by the teacher; the student sees the amended result. |
| Report export | The reviewer's `report.md` (labelled `[MOCK]` in mock mode) and `turns.csv` export; a student is refused. The per-objective assessment report (GAD-T99): `/grading/{id}/report.md` with its title, objectives table and a section per objective (MOCK notice in mock mode), `report.csv` with a header and one row per objective, the student refused the reviewer copy; after the grading stages the report is not preliminary and carries the approval, reconciliation and appeal history, and the student's `/my-results/{id}/report.md` downloads without reviewer names. |
| Review pages | The review list and the session page render for the teacher. |
| Platform API results | The attempt list and the session result (state Ended, report present). |
| Analytics (GAD-T102) | `/analytics` and `/analytics/export.csv` answer the teacher and refuse a student. |
| Transcript downloads (GAD-T295) | Every format (json, pdf, docx, html, txt, vtt, srt) downloads for the teacher with its `X-Transcript-Code` (PDF, .docx, WebVTT and JSON checked by content); an unknown format is 404; the student's released copy names no reviewer; downloads are audited. Anonymous `/transcripts/verify`: the code and the JSON file verify as genuine and unchanged, an edited file is "changed after it was issued", a made-up code is refused, and the page never shows the content. |
| PDF reports and certificate (GAD-T311) | Reviewer and student `report.pdf`, the class summary PDF (student refused both reviewer files), the report page; the student's certificate PDF verifies by its code on `/transcripts/verify`; anonymous is refused the certificate. |
| Separate reconciler and approver (GAD-T444) | The grade was approved by someone other than its reconciler and second reviewer, with no waiver or refusal recorded. |
| Anonymous second marking (GAD-T428, GAD-T439) | The second reviewer's queue never names the student (lists the session code when double marking is on); the sheet is refused to the reviewer who saw the grades and to the first examiner; the owner (who doesn't grade) is refused. |
| Panel calibration (GAD-T427, GAD-T469) | Over `POST /grading/calibration/{exam}` on the statistics exam: the teacher is refused (403), the owner records 30 answers (never accepted on the mock panel); read back with its grading prompt-set version and not expired; the calibration page shows that version; the index and self-preference pages render. |
| Bias-probe cost screen (GAD-T429) | The owner's cost screen renders (in mock mode "none (mock panel)"); the tour never confirms a run and checks none was queued. |
| Research (GAD-T451) | The owner's research page and the student's research choice page; the export is either refused with "Research is not set up" or a zip with no student name or id in any file (audited); teacher and student refused. |
| Fairness (GAD-T463) | The owner's `/analytics` and CSV carry the fairness views (score gaps, path drivers, typed vs spoken) and name no student; the teacher's carry none; the speech reference page renders. |
| Audit trail | The audit actions recorded for the organization (and `response.*` events once verification runs); after the grading stages every grading action (`grade.human.recorded`, `grade.disagreement.detected`, `grade.transition.refused`, `reconciliation.second-review.requested`, `grade.reconciled`, `grade.approved`, `grade.published`, `grade.appeal.opened`, `grade.amended`, `grade.appeal.resolved`, `access.denied`) must be there. |

## How the grading stages drive the server

The human grading steps are Blazor buttons on `/grading/{id}`; the tour drives the same `GradingService` through the
grading HTTP actions (`Gadfly.Server/Grading/GradingActionEndpoints.cs`, documented in docs/API.md, "Grading
workflow"). Each demo user posts JSON with their own sign-in cookie and the antiforgery token from
`GET /grading/actions/antiforgery` in the `RequestVerificationToken` header, exactly as a browser script would, so
the permission matrix, the blind rule, the status machine and the audit trail are the ones the pages use. The tour
reads the AI grades from the database (never through the examiner) to choose a human score 40 points away. If the
server doesn't map the actions (`app.MapGradingActions()`), the grading stages SKIP with their ticket.

## How the tour grows

A stage whose feature hasn't landed prints `SKIP (not built: <ticket>)` instead of failing. It decides by probing
the running server: a SessionHub method that doesn't exist (`CorrectAnswer`, `ReviseAnswer`), a step without the
`verification` field, a route that answers 404, or no rows for the tour's session in the feature's table. When a
feature lands, its stage starts asserting automatically; a feature that needs a new user action also needs its step
written into `Gadfly.Tour/DemoTour.cs`, ideally against an HTTP action rather than a Blazor-only button (as the
grading stages do), so the tour can drive it the way a person would.
