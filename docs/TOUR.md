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
| Blind human grading, disagreement, reconciliation, approval and publication, student sees result, appeal and amendment (GAD-T95..T98) | Asserted from their tables once the features record rows for the tour's session; until the tour can drive them over HTTP they SKIP with the ticket. |
| Report export | The reviewer's `report.md` (labelled `[MOCK]` in mock mode) and `turns.csv` export; a student is refused. The per-objective assessment report (GAD-T99) is not checked yet. |
| Review pages | The review list and the session page render for the teacher. |
| Platform API results | The attempt list and the session result (state Ended, report present). |
| Analytics (GAD-T102) | `/analytics` and `/analytics/export.csv` answer the teacher and refuse a student. |
| Audit trail | The audit actions recorded for the organization (and `response.*` events once verification runs). |

## How the tour grows

A stage whose feature hasn't landed prints `SKIP (not built: <ticket>)` instead of failing. It decides by probing
the running server: a SessionHub method that doesn't exist (`CorrectAnswer`, `ReviseAnswer`), a step without the
`verification` field, a route that answers 404, or no rows for the tour's session in the feature's table. When a
feature lands, its stage starts asserting automatically; a feature that needs a new user action (the human grading
steps) also needs its step written into `Gadfly.Tour/DemoTour.cs`, ideally against an HTTP action rather than a
Blazor-only button, so the tour can drive it the way a person would.
