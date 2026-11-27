# Deploying Gadfly

Status: the Azure deployment was decommissioned on 2026-11-26 (MAT-T297). `tools/deploy-azure.ps1`,
`tools/provision-azure.ps1`, the `azure-deploy.yml` workflow and the WebJob files (`run.cmd`, `run.sh`, `settings.job`)
were removed in GAD-T804; the resource names, slots, Key Vault and Blob key-ring steps they documented are in git history
(commit before GAD-T804). Nothing here creates billable cloud resources.

## Production configuration

- **Data Protection keys** (cookies, launch links, webhook secrets): Production no longer requires a Blob Storage / Key
  Vault key ring. Set `DataProtection:KeysPath` (env `DataProtection__KeysPath`) to a persistent, private folder so keys
  survive restarts; without it the library's default key location is used. It must be an **absolute path** that the
  web app and `Gadfly.Worker` both point at, so they share one key ring (startup throws otherwise).
- **Secrets**: every credential comes from MindAttic.Vault (never a literal).
- **Database**: `ConnectionStrings:Gadfly`, a SQL Server database. Development migrates on start; elsewhere run the
  server once with `--migrate` before the new code serves (GAD-T63).
- **Versions** (GAD-T291, GAD-T336): a release build passes `/p:GadflyVersion=N` (the next whole number after the
  highest `vN` tag) and `/p:GadflyCommit=<sha>`, and the deploy tags that commit `vN`; the app shows `V<N>` and
  `/healthz/version` answers `V<N> <sha>`. A local build shows the highest tag plus " dev".

## The worker

Exams are taken on the website; grading and the other queued work run in `Gadfly.Worker`, a console host beside it. It
shares the site's service registration (`GadflyServices.AddGadflyServices`, used by `Gadfly.Server/Program.cs` and
`Gadfly.Worker/Program.cs`), so it reads the same configuration, connection string and AI panel (`Gadfly__AiMode`).
It stops on SIGTERM / Ctrl+C or when the file named by `WEBJOBS_SHUTDOWN_FILE` appears, within 50 s; a job cut off
mid-run is put back on the queue without using up an attempt.

| Runs in | What |
|---|---|
| Worker (and the web app while `Gadfly:Jobs:RunInWeb` is true) | `JobWorker` (every queued job: AI grading, source processing, email, webhooks, retention, stale-session close, LTI passback and roster refresh, reminders); `GradingSweeper`, `ReminderScheduler`, `StaleSessionScheduler`, `RetentionScheduler`, `LtiPassbackSweeper`, `LtiRosterRefreshScheduler`, `ResponseBackfillService` |
| Both, each for its own process | `ApiCallTimingWriter` and `EvidenceSyncWriter`, `DeskAdminsListWarning` |
| Web app only | Pages, the exam hub and live sessions, voice, the API; the web app always enqueues jobs |

**`Gadfly:Jobs:RunInWeb`** (default `true`): the web app also runs the job worker and sweepers; safe, since a job is
claimed by an atomic row update. Set it to `false` to run the queue only in the worker.

**A cold database does not crash startup**: the first database call of a start (`AuthBootstrapper.SeedAdminAsync`) is
retried 8 times, 15 s apart, in case a serverless database is still resuming.

**Idle shutdown (GAD-T775)** (`Gadfly.Server/Platform/IdleShutdown.cs`) only acts on App Service; elsewhere it stays off.

**Locally**, `.\run.ps1 -Redeploy` puts `Gadfly.Worker.exe` beside the server in `R:\MindAttic\Apps\Gadfly\Debug`. Run
it from there with `--environment Development --Gadfly:AiMode=mock`, and start the server with
`.\run.ps1 -RunDebug --Gadfly:Jobs:RunInWeb=false` beside it to try the split.

## Running it locally

`run.ps1` at the repo root (or a double-click on `run.bat`) is the MindAttic standard run menu: `1` Redeploy builds
Gadfly.Server, Gadfly.Worker and the CLI (`gadfly.exe`) into `R:\MindAttic\Apps\Gadfly\Debug` and `\Release`, `2` Run
Debug starts the Debug server in this console, `3` Run Release starts the Release one on its own, and `13` redeploys
then runs Release. Both runs are Development on `https://localhost:7311` against LocalDB (database `Gadfly`, migrated
on start); Run Debug also passes `--Gadfly:AiMode=mock`, so no provider is called. Arguments after the switches go to
the Debug server, so a seed flag goes last:

```powershell
.\run.ps1                                  # the menu
.\run.ps1 -Redeploy -RunDebug              # build, deploy and serve
.\run.ps1 -RunDebug --seed-demo            # also seed Gadfly Demo Academy (demo.owner ... demo.student)
.\run.ps1 -RunDebug --seed-classroom       # also seed the classroom (Teacher_A..C, Student_A..Z)
.\run.ps1 -Redeploy -DryRun                # print what would be built and launched
```

The seeders use `Gadfly:Demo:Password` (for example `$env:Gadfly__Demo__Password`) as every account's password, or
generate one and print it once in the log. The demo tour runs from source: `dotnet run --project Gadfly.Tour -- --fresh`
(docs/TOUR.md).
