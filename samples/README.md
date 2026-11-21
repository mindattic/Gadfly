# Gadfly samples: using the Socratic method in your own app

Gadfly is two things: a **Socratic engine** (it plans each question from what the student just said,
judges answers against your material with citations, and reports mastery) and a **web platform** around
that engine (organizations, accounts, exam room, review and grading). An outside application can use it in
either of two ways. Each has a runnable sample here, and both run **without any AI provider key and without
spending anything**, because they use Gadfly's labelled mock panel.

| | Path 1: in-process (NuGet) | Path 2: hosted (API) |
|---|---|---|
| You get | The engine as a library | The whole platform as a service |
| Your app | Is the exam room: it asks the questions and takes the answers | Sends students to Gadfly's exam room and hears back |
| You provide | Source material (or your own `ISubjectMatter`), an AI panel, your own UI and storage | An API key, and an HTTPS endpoint for webhooks |
| You don't build | Question planning, judging, claim tracking, reports | Accounts, exam room, integrity signals, grading workflow |
| Sample | [InProcess](InProcess) | [HostedApi](HostedApi) |
| Reference | [Gadfly.Core](../Gadfly/README.md), [Gadfly.Sources](../Gadfly.Sources/README.md) | [Platform API](../docs/API.md) |

## Path 1: in-process, as packages

You reference two NuGet packages, **Gadfly.Core** (the engine) and **Gadfly.Sources** (reading your
documents, building concepts and rubrics, and the step-driven exam), plus whatever shows the questions to
your students. Your code decides who the student is, where the transcript goes and what happens with the
report.

[InProcess](InProcess) reads a short markdown file, has the (mock) panel build concepts and rubrics, and
runs a scripted session with canned student answers, printing each question, the judgment and the final
report:

```powershell
cd samples/InProcess
dotnet run
```

It uses `PackageReference`, not `ProjectReference`: the packages restore from `lib/local-packages`
(git-tracked, so a clean checkout works) or `C:\LocalNuGet`, exactly as an outside app's feed would.
To use the real multi-LLM panel, replace `MockVotingClient` with `LegionVotingClient` built from a
`GadflyConfiguration`; nothing else changes, because everything depends on `ISocraticVotingClient`.

To examine something other than documents (a codebase, a transcript archive, a database of cases),
implement `ISubjectMatter`: it supplies the concepts to cover and the grounding text for each.

## Path 2: hosted, through the API

Your app (an LMS, a training portal) never runs the engine. A Gadfly organization gives you an **API
key**; with it your app uploads source material, has it processed, creates and publishes an exam, assigns a
student, and asks for a **signed launch link**: a URL that expires after 15 minutes and sends the student
into Gadfly's exam room. When the attempt ends Gadfly calls your **webhook** with a signed
`session.completed` message, and your app reads the result. The instructor, not the AI, decides the grade.

[HostedApi](HostedApi) plays the LMS end to end against a locally running Gadfly.Server in mock mode,
including receiving a real webhook delivery and verifying its HMAC signature. See its README for how to
start the server and provision the key.

## Why these are not in `Gadfly.slnx`

The samples are deliberately **outside the solution**. `InProcess` consumes the *published* package
versions, so it must not rebuild when the engine changes (it proves a consumer on version 1.0.0 still
works), and `HostedApi` needs a running server. Build them by hand:
`dotnet build samples/InProcess` and `dotnet build samples/HostedApi`. When the engine's public API
changes, bump the version, `dotnet pack` Gadfly and Gadfly.Sources, copy the `.nupkg` files into
`lib/local-packages/` (and `C:\LocalNuGet`), and update the sample's `PackageReference`.
