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

The exam is a conversation, not a form. A talking examiner asks each question aloud and starts speaking the
next one within moments of the answer, while it is still being written. Students can take an exam in English
or Korean, with recognition, voice and lip-sync in that language, and a student answering in a second language
is heard and graded for what they understand, not for accent, grammar or fluency. Looking away to think is
never penalized: the exam room follows the research on attention, and anything it notices goes to a person,
never to an automatic decision.

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
- Keep the evidence intact: each answer as it was heard, with the words the recognizer was unsure of, and every
  clarification stay on the record, with an audit trail whose rows are never deleted.
- Protect students: integrity signals are low-confidence notes for a reviewer, never a verdict, and video
  never leaves the student's device ([docs/PRIVACY.md](docs/PRIVACY.md)). Nothing is cancelled or deleted
  automatically; a human decides.
- Be fair to students who think, speak or hear differently: looking away to think is normal and never costs
  anything, second-language speakers are graded on content, and fairness is measured by group and by language.
- Try it without spending anything: the default development setup runs a clearly labelled mock panel, so
  no provider key or credit is needed.

## The science behind Gadfly

Gadfly's examiner follows the Socratic *elenchus*: it examines only what the student actually said, and
tests whether those answers hold together. In Plato's *Apology*, Socrates questions a man who "thinks he
knows something when he does not" (21d). Piaget turned the same habit into a research method, the clinical
interview: vary the questions, offer counter-suggestions, give up "all idea of a fixed questionnaire", and
never dictate an answer (Piaget 1929, pp. 4, 13). One correct answer can be recalled, guessed or pasted;
explaining it, defending it against a counterexample and using it somewhere new is much harder without
understanding it. Prompting learners to explain also helps them learn (Bisra et al. 2018), so an exam like
this teaches as well as measures.

The research also sets limits, and Gadfly is built around them:

- **A contradiction shows inconsistency, not which claim is wrong** (Vlastos 1983, via Wolfsdorf 2013;
  Benson 1995). "Can both be true?" quotes the student's own two claims; the rubric and the cited course
  material decide which is right.
- **Explaining to a listener changes and slows the explainer** (Fox, Ericsson & Best 2011), so pauses and
  speech rate never enter the grade.
- **Oral exams are reliable through sampling** (van der Vleuten et al. 2010; Wass et al. 2003), and LLM
  judges carry biases (Zheng et al. 2023): several objectives, a panel of models from different families,
  blind human grading, reconciliation and human approval. Human oversight is a legal requirement for this
  use in the EU (AI Act, Annex III point 3).
- **Speech recognition errs more on some dialects and on stuttered speech** (Koenecke et al. 2020; Mujtaba
  et al. 2024): judges ignore transcription slips and never penalize accent or fluency.
- **Second-language speakers pause longer and are misheard more** ([ESL and Korean research](docs/research/esl-and-korean.md)):
  Gadfly asks for the student's primary language (never infers it), and only when it differs from the exam
  language pins the recognizer, waits longer before taking an answer, slows the examiner slightly, asks about a
  word it was unsure of instead of misjudging it, and gives the judges the answer's content without fillers and
  false starts. An English speaker taking an English exam gets exactly the same exam as before.
- **Looking away while thinking is normal and helps** (Glenberg et al. 1998; Doherty-Sneddon & Phelps 2005;
  [attention research](docs/research/attention-and-gaze.md)): nothing reacts in the thinking gap before the first
  word, anywhere on the screen counts as looking at the screen, and only a long look off the screen while answering
  gets a gentle reminder from the examiner herself. The self-view is hidden during the exam, since watching yourself
  raises appearance anxiety (Tien et al. 2023). Leaving the window pauses the exam; an automatic cancellation would
  be a solely automated decision (GDPR Art. 22; CNIL 2023), so a person reviews instead.
- **Webcam gaze is too coarse to tell reading from thinking** (Papoutsaki et al. 2016; Rayner 2009), and
  **voice-stress analysis performs at chance** (Harnsberger et al. 2009; NRC 2003): both are low-confidence
  reviewer notes, never a verdict and never part of the grade. The research recommends going further on voice
  strain than Gadfly does today; that gap is stated on the full page.
- **Waiting in silence feels longer than it is**: a natural spoken filler with a thinking gesture shortened
  perceived waits where a "processing" sound did not (Maslych et al. 2025), so the examiner thinks visibly instead
  of playing a sound. A realtime speech-to-speech model was not adopted: it would not shorten the wait for a
  grounded, leak-checked question and would break the voice, lip-sync and integrity checks
  ([speech-to-speech note](docs/research/speech-to-speech.md)).

Gadfly makes no validity claims until its own pilot measures them. The full explanation, with the
verification level of every source, is in [docs/SCIENCE.md](docs/SCIENCE.md); the research notes and
citation ledger are in [docs/research/](docs/research/README.md).

## Features

Gadfly Exams, the web platform (`Gadfly.Server`):

- **Organizations and roles.** Owner, Admin, Instructor, Examiner, Second reviewer and Student, on
  MindAttic.Authentication (Argon2id, MFA for every staff role, lockout), with invitations by email (one at a time
  or by CSV), and plans with seat and exam limits (Trial, Standard, Institution, Custom).
- **Source material.** Upload `.txt`, Markdown, PDF, DOCX, EPUB or HTML. Gadfly turns it into an editable
  concept map and versioned rubrics with citations to the passages; a person approves that version before a
  summative exam can be published.
- **Assessments.** Courses, learning objectives, rubric criteria with level descriptors 0 to 5, optional fixed
  opening questions (checked so they give nothing away), a locked grading policy (AI and human weights,
  disagreement thresholds, blind and anonymous marking), assignments with windows, attempts and accommodations, and
  a preview that runs the real examiner logic on the practice panel without saving anything.
- **The spoken exam room.** A talking examiner (`<ma-avatar>`) asks each question aloud and the student answers
  aloud, on camera: voice and camera are mandatory, and typing exists only as a typed-answers accommodation staff
  grant. Each question is written from the previous answer; unclear answers get a neutral clarifying question;
  "I don't know" is recorded as honest aporia; answers are taken as heard, with the words the recognizer was unsure
  of marked for graders. The server keeps time, a lost device pauses the exam and stops the clock, and a reload or
  dropped connection resumes the same question. The room is laid out like a video call (the examiner centred, the
  transcript as a conversation beside her), and the student can ask her to repeat a question or **say it another
  way** (a leak-checked rewording that never counts against them). See [docs/VOICE.md](docs/VOICE.md).
- **A conversational examiner.** One fast model (Claude Sonnet 5 by default, never Opus; `Gadfly:LiveTurn:*`) words
  the next question and checks it for leaks; its reply streams sentence by sentence, each sentence leak-checked
  before she says it, so she starts speaking while the question is still being written. The next step can be
  prepared from the student's words while they are still speaking. She acknowledges each answer at once with a short,
  non-judging line, and fills a longer wait with a thinking gesture and a spoken filler instead of a sound. A finished
  sentence ends the answer after about two seconds of silence instead of four. The full multi-model panel still judges
  every answer, in the background, and only its judgment is graded.
- **Exams in English and Korean.** An exam lists the languages it is offered in (English and Korean by default), and
  the student picks one before beginning. A Korean attempt is heard by a Korean recognizer, spoken in Korean, lip-synced
  with Gadfly's own Hangul-to-viseme module, and examined with Korean prompts and Korean text rules. The exam's title,
  instructions and objective titles get a machine-translated draft that an instructor edits and approves (students
  are told when a text is still an unchecked draft); the course material, concept map and rubric stay as written, so
  every language is the same exam. Graders read the original words, which are the record, with a labelled machine
  translation into English on request. The student pages, emails, consent notice and student help essentials are in
  Korean too; the exam room's own on-screen text in Korean is being finished (GAD-T606). See
  [docs/LOCALIZATION.md](docs/LOCALIZATION.md). Spanish is planned next (GAD-T596).
- **Second-language speakers.** The student states the languages they speak and the one they speak best; nothing is
  inferred from voice or name. When that differs from the exam language, the recognizer is pinned to the exam
  language, end-of-answer silences are longer (`Gadfly:Voice:Esl:*`), the examiner speaks slightly slower and in
  plainer questions, a word the recognizer was unsure of is asked about briefly instead of misjudged, and the judges
  read the answer's content without fillers, repeats and false starts (they are never told why). Fairness views,
  calibration and speech-recognition accuracy can be grouped by exam language and by stated primary language.
- **The get-ready studio.** One screen in the student's language: choose the exam language, your languages, your
  camera and microphone (with checks and a microphone test), and your examiner (a male examiner is coming, GAD-T504);
  an optional voice calibration (reading a short sentence three times, keeping only timing and confidence numbers,
  never audio); a plain statement that the camera checks presence and never grades or decides; and a free practice run
  with the same examiner and room, which stores nothing.
- **Attention, from the evidence.** No reminder in the thinking gap before the first word; anywhere on the screen
  counts as looking at it; only a long look off the screen while answering starts a gentle ladder (her head tilt and
  raised brows, then a soft dim of everything but her, then one warm spoken line at a pause, a few times per exam at
  most), and reminders never touch the grade. The student's self-view is hidden during the exam (a camera-on
  indicator, and a preview on request). Leaving the exam window pauses the exam and keeps the words already said;
  repeated or long absences mark the attempt for an instructor to review, with the student's own explanation. Nothing
  is ever cancelled or deleted automatically, and staff can grant a window-absence accommodation per student.
- **Integrity notes, never verdicts.** Eye contact, camera presence and a second face, browser focus and copying, a
  second voice and answers that sound read out are low-confidence notes for a reviewer: hidden from a blind grader,
  never part of a grade, and shown to the student with their result. Video never leaves the student's device. See
  [docs/PRIVACY.md](docs/PRIVACY.md).
- **Accessibility.** Built toward WCAG 2.2 AA and checked with axe in light and dark themes at desktop and phone
  widths: one spacing scale, 24px targets, keyboard-operable exam controls (Hold to talk with Space or Enter), a
  choice of how long a silence waits before an answer goes in, one screen-reader announcement per question, and
  `<html lang>` following the interface language. Conformance is not claimed until assistive-technology testing is
  done; see [docs/ACCESSIBILITY.md](docs/ACCESSIBILITY.md).
- **Grading with people accountable.** AI preliminary grades per objective from a multi-model panel, blind human
  grading, combined scoring, reconciliation, separation of duties (scorer, reconciler and approver), final approval,
  publication, appeals and amendments. Reviewers see the path each objective took. Reports as Markdown, CSV and
  PDF; a class-summary PDF; a completion certificate.
- **Checking the AI.** Blind double marking, per-exam panel calibration (which can hold the AI's weight at zero until
  accepted, and expires when the models or grading prompts change), bias probes for the panel and for human
  graders, and a self-preference check.
- **Transcripts.** An official transcript per session in PDF, Word, HTML, text, WebVTT, SRT and JSON, in a reviewer,
  grader or student copy, each with a verification code anyone can check at `/transcripts/verify`.
- **Review and analytics.** A review timeline with citations and each judge's verdict, and analytics at
  `/analytics` with fairness views by group (speech-recognition accuracy, eye-contact and camera rates, score gaps,
  what made the examiner press harder, typed against spoken answers) and CSV export.
- **Admin area.** Dashboard, students directory and profiles, exams manager (windows, duplicate, archive,
  export/import as JSON), assignments console, live sessions monitor (add time, pause, end, send a notice), audit
  log viewer with export, system status with job retry, plan and usage, settings, email preview, and an in-app help
  center with guided tours.
- **Privacy.** Retention (7 days to 10 years, 90 by default), audit and call-timing logs kept at least six months,
  student data export and erasure, voice measurements off by default in the EU/EEA, and an optional research consent
  with a de-identified research export.
- **Integrations.** LTI 1.3 for Canvas, Moodle, Blackboard Learn and D2L Brightspace (deep linking, roster sync, grade
  passback), the platform API v1 with organization API keys and signed launch links, and signed webhooks, with an
  OpenAPI document at `/openapi/v1.json`. Optional card checkout for plan upgrades, off until payment keys are set.
- **Public site.** Signed-out visitors at `/` see the product site (`Components/Pages/Site/`): how it works, the
  science, pricing (Trial, Standard, Institution, Custom; plan numbers in `Site/SitePlans.cs`), a request-a-demo form,
  terms, privacy notice, accessibility statement, security page, a branded 404 and an offline page, with `robots.txt`
  and `sitemap.xml`. These pages render as static HTML with no Blazor circuit and load nothing from other sites.
  Requests from the form are stored for the platform admin at `/platform/requests` (spam held off by a honeypot, a
  signed render time and a rate limit). Set `Gadfly:Site:ContactNotifyEmail` to have each one emailed too; that only
  happens when the sign-in library's SMTP sender is configured. Signed-in users still land on their dashboard.

## Quick start

Prerequisites: the .NET 10 SDK and SQL Server Express LocalDB. Sign-in also needs the MindAttic.Vault
`Security` bucket (the sign-in library's pepper and `bootstrap-token`); it fails closed without it.

```powershell
dotnet build Gadfly.slnx
dotnet run --project Gadfly.Server
```

`.\run.ps1` does the same in one step, always in mock mode; add `-SeedDemo`, `-SeedClassroom` or `-Tour` (below).

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

`.\run.ps1 -Tour` builds both and runs exactly that; `.\run.ps1 -SeedDemo` and `-SeedClassroom` start the
server with the matching seeder (`-Password` chooses the account password). The other options (`--keep`,
`--url`, `--verbose` and more) are in [docs/TOUR.md](docs/TOUR.md).

## Configuration

| Setting | What it does |
|---|---|
| `ConnectionStrings__Gadfly` | SQL Server connection. Default `(localdb)\MSSQLLocalDB`, database `Gadfly`. Outside Development, run once with `--migrate`. |
| `Gadfly:AiMode` | `live`, `mock` or `auto`, as above. |
| `Gadfly:RequireMfaInDevelopment` | Staff are not asked for MFA in Development unless this is `true`. |
| `Gadfly:Voice:Mode` | `off`, `mock` or `live`; the other voice settings are in docs/VOICE.md. |
| `Gadfly:LiveTurn:Provider`, `Gadfly:LiveTurn:Model` | The one fast model that words the next question and runs its leak guard: `claude` and Claude Sonnet 5 by default; an Opus id is refused. |
| `Gadfly:LiveTurn:Stream` | `false` speaks each question whole instead of sentence by sentence as it is written (default on). |
| `Gadfly:LiveTurn:DeferJudging` | `false` puts the panel's full judgment back on the live turn instead of in the background (default on when the live turn has its fast model). |
| `Gadfly:LiveTurn:Prefetch`, `Gadfly:LiveTurn:Prefetch:*` | Prepare the next step from the student's words while they are still speaking (`false` turns it off; `DebounceMs` and the other limits in docs/VOICE.md). |
| `Gadfly:Voice:EndOfAnswerCompleteSeconds` | The silence that ends an answer whose last words are a finished sentence (1 to the floor, default 2). |
| `Gadfly:Voice:BackchannelMs` | A soft "Mm-hm." after a finished sentence and this much silence, without ending the turn (0 = off, the default; else 300 to 3000). |
| `Gadfly:Voice:WaitTypingShare` | Share of sessions given the old typing sound instead of the thinking gesture during the judging wait, for an A/B test (0, the default, turns it off). |
| `Gadfly:Voice:TtsModels:<lang>`, `Gadfly:Voice:VoiceIds:<lang>` | The examiner's ElevenLabs model and platform voice for one exam language (for example `TtsModels:ko`); unset uses the usual model and voice, which speak Korean. |
| `Gadfly:Voice:PlainSpeechSpeed` | The examiner's speed for a second-language speaker (0.7 to 1.2, default 0.9; 1 turns it off). |
| `Gadfly:Voice:Esl:EndOfAnswerSeconds`, `:EndOfAnswerMaxSeconds`, `:EndOfAnswerCompleteSeconds`, `:CompleteAfterAnswers`, `:CutOffBackoffFactor` | End-of-answer silences for a second-language speaker (defaults 6 s, 15 s, 3.5 s after 3 uncut answers, cut-off backoff doubled). |
| `Gadfly:Demo:Password`, `Gadfly:Demo:ApiKey` | Chosen password and API key for the seeded demo accounts. |
| `Gadfly:AdminDesk:Enabled` | The admin desk (`/admin/desk`) for Owners and Admins; off by default in Production. |
| `Gadfly:Logs:CallTimingDays` | How long outside-call timings are kept: a year by default, never under 184 days. |

Uploaded files are kept under `%APPDATA%\MindAttic\Gadfly\uploads\<organization>\<collection>\`. Logs go
to MindAttic.Log's database for `Gadfly.Server`. Health is at `/healthz` and readiness at `/healthz/ready`;
[docs/OPERATIONS.md](docs/OPERATIONS.md) has the rest of the monitoring endpoints.

## Deployment

Gadfly.Server deploys to Azure App Service with Azure SQL, Key Vault and a managed identity.
`npm run deploy -- --app gadfly` in MindAttic.Deploy (which runs `tools/deploy-azure.ps1`) exports the commit
cleanly, migrates the database first, deploys, checks `/healthz` and `/login`, and tags the release `vN` (the
app shows V&lt;N&gt; in its title). A manual GitHub Actions workflow also deploys, but without the version, tag and
mock-mode check. [docs/DEPLOY.md](docs/DEPLOY.md) has the procedure and the app settings a release must check;
[docs/OPERATIONS.md](docs/OPERATIONS.md) the release checklist, monitoring, backups and incident steps. The instance at
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
tools/              deploy-azure.ps1, provision-azure.ps1 and sync-voice-assets.ps1
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

- Gadfly makes no validity claims until its own pilot ([docs/PILOT.md](docs/PILOT.md)) measures them; the master
  plan's validity studies (Part 15.3) happen with real institutions. How well the AI panel grades spoken,
  transcribed answers, whether follow-ups resist a relayed answer, and fairness across groups have measurement tools
  built in but no results yet.
- Exams run in English and Korean only. Spanish is planned (GAD-T596), waiting for a native-speaking tester. The
  exam room's on-screen text is still English in a Korean attempt until GAD-T606 lands (the examiner already speaks,
  listens and lip-syncs in Korean). Exams already live before localization need their Korean texts drafted (GAD-T541),
  and the recitation check does not yet compare across languages (GAD-T600).
- The ESL adaptations' numbers (longer silences, slower speech) are reasoned from research, not yet tuned on a pilot
  of Korean-L1 speakers.
- One examiner (female) is available; the male examiner is listed as coming soon on the get-ready page (GAD-T504).
  The exam's or organization's voice id is used, but the avatar id is saved and not used yet (GAD-T475).
- Attempts marked for instructor review (window absences, Korean scores the panel has not been calibrated on) are
  held for a person; the dedicated review queue for them is being built (GAD-T603).
- LTI sends one grade per placement (the final percentage), not one per objective; LTI 1.1 isn't supported. There is
  no institutional single sign-on (SAML or OpenID Connect) apart from the LMS launch.
- Webhooks offer one event, `session.completed`.
- Only AI panel (LLM) calls are metered for cost; speech-to-text and text-to-speech are not yet counted.
- No accessibility conformance is claimed: the app meets the WCAG 2.2 AA checks axe can make but has not had
  screen-reader, switch or voice-control testing or a formal audit (see `/accessibility`).
- The hosted instance runs in mock mode, so its questions and scores are templates, not AI judgments.

### Where the build departs from the dissertation

Gadfly follows the *Elenchus at Scale* dissertation, with owner decisions that differ from it
([docs/research/blueprint-gap-analysis.md](docs/research/blueprint-gap-analysis.md) has the full list and reasons):

- Spoken answers are judged as heard: the student isn't asked to confirm or correct the transcript, so recognizer
  doubts are flagged to graders instead.
- Voice and camera are mandatory; typing is only an accommodation staff grant.
- A long look off the screen while answering gets a gentle reminder from the examiner (a gesture, a light dim around
  her, one warm line), never in the thinking gap, softened further by the eye-contact accommodation, with its rate and
  cost measured by group.
- The next question is worded by one fast model with its own leak guard instead of a panel vote, for conversational
  speed; the panel still judges every answer and only its judgment is graded.
- Clearing an attempt's scores holds it out of grading, and the attempt still counts against the student's limit.
- The answer text-style estimate is switched off (the dissertation agrees it shouldn't be used), but kept so it could
  return behind a setting; the dissertation says not before its false-flag rate by group is measured.
- Probing pressure comes only from what the answer says, never from the voice: not a departure, since the dissertation
  argued for it.

## Documentation

| What | Where |
|---|---|
| Buyers: what Gadfly does, the exam, grading, safeguards, privacy, integrations, plans, and what isn't shown yet | [Product overview](docs/PRODUCT.md) |
| Running the service: monitoring, backups and restore, rotating secrets, incidents, the release checklist | [Operations runbook](docs/OPERATIONS.md) |
| Instructors: material, concepts and rubrics, exams, review and grading | [Instructor guide](docs/guides/INSTRUCTOR_GUIDE.md) |
| Examiners and reviewers: blind grading, reconciliation, approval, second marking | [Grading guide](docs/guides/grading-guide.md) |
| Connecting an LMS (Canvas, Moodle, Blackboard, Brightspace) | [LTI setup](docs/guides/lti-setup.md) |
| Students: how the exam works, what is recorded, results | [Student guide](docs/guides/STUDENT_GUIDE.md) |
| The science behind Gadfly: the elenchus, why follow-ups reveal understanding, the limits, citations | [Science](docs/SCIENCE.md), [research notes](docs/research/README.md) |
| The dissertation (in progress): *Elenchus at Scale: Socratic Oral Assessment by Synthetic Examiners* | [mindattic/Dissertation.ElenchusAtScale](https://github.com/mindattic/Dissertation.ElenchusAtScale), read at [mindattic.github.io/Dissertation.ElenchusAtScale](https://mindattic.github.io/Dissertation.ElenchusAtScale/) |
| Owners and admins: organizations, members, MFA, retention, keys, webhooks | [Admin guide](docs/guides/ADMIN_GUIDE.md) |
| Integrators: the platform API v1, launch links, webhook signatures | [API reference](docs/API.md) |
| The owner's product plan, and what is built against it | [Master plan](docs/MASTER-PLAN.md), [status](docs/MASTER-PLAN-STATUS.md) |
| Requirements traceability, architecture, schema, state machines | [Design](docs/DESIGN.md) |
| The Socratic exam platform: plan, milestones, open decisions | [Socratic platform](docs/SOCRATIC_PLATFORM.md) |
| What is collected, for how long, and the regulatory notes | [Privacy](docs/PRIVACY.md), [Compliance](docs/COMPLIANCE.md) |
| Each exam-room mechanic against the research for and against it, and the owner's decisions | [Evidence map](docs/EVIDENCE_MAP.md), [blueprint gap analysis](docs/research/blueprint-gap-analysis.md) |
| Voice exam room, avatar and audio signals, the live turn's speed, second-language speakers | [Voice](docs/VOICE.md) |
| Exams in English and Korean: what is translated, grading across languages, the Korean glossary | [Localization](docs/LOCALIZATION.md) |
| Accessibility: the WCAG 2.2 AA target, what was checked and fixed, known gaps | [Accessibility](docs/ACCESSIBILITY.md) |
| The research behind the exam room: attention and gaze, second-language speakers and Korean, speech-to-speech | [Attention](docs/research/attention-and-gaze.md), [ESL and Korean](docs/research/esl-and-korean.md), [Speech-to-speech](docs/research/speech-to-speech.md) |
| The demo tour and mock mode | [Tour](docs/TOUR.md) |
| Deploying to Azure | [Deploy](docs/DEPLOY.md) |
| The pilot with Erin: steps, what to watch for, feedback form | [Pilot](docs/PILOT.md) |
| Security review findings | [Security review](docs/SECURITY_REVIEW.md) |
| User stories (generated) | [User stories](docs/USER_STORIES.md) |
| Using the engine or the API from your own app | [Samples](samples/README.md), [Gadfly.Core](Gadfly/README.md), [Gadfly.Sources](Gadfly.Sources/README.md) |
| Agent and contributor instructions | [AGENTS.md](AGENTS.md) |

## License

MIT, as declared in the project files.

---

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic).
Related: [MindAttic.Legion](https://github.com/mindattic/MindAttic.Legion), the multi-LLM panel underneath.
