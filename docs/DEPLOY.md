# Deploying Gadfly to Azure

Status: done (GAD-T63; the owner said go, decision D16 in [SOCRATIC_PLATFORM.md](SOCRATIC_PLATFORM.md)). The app is live at
https://mindattic-gadfly.azurewebsites.net and runs in mock mode (D23). The steps below are how it was set up and how to
repeat it in another subscription.

Gadfly.Server runs next to Tutor: the app in the existing **MyApps** resource group on the existing App Service
plan, the database on the existing Azure SQL server in **rg-mindattic-data**, with a managed identity and no passwords
anywhere:

| Piece | Name | Notes |
|---|---|---|
| App Service | `mindattic-gadfly` (group `MyApps`) | .NET 10, WebSockets on (Blazor Server), Always On off (since 2026-11-25, GAD-T768: unloads after 20 min idle), HTTPS only |
| Database | `Gadfly` on `sql-mindattic-f65744` (group `rg-mindattic-data`) | Serverless, auto-pause after 20 min (`provision-azure.ps1` creates it so and sets it on an existing database) |
| Data Protection keys | blob container `gadfly-dp-keys`, Key Vault key `gadfly-dp-protect` | Signs cookies, launch links and webhook secrets |
| Secrets | Key Vault `kv-tutor-f65744` | Auth pepper, reset key, bootstrap token, LLM and ElevenLabs keys |
| Grading worker | continuous WebJob `GadflyWorker` in `mindattic-gadfly` | `Gadfly.Worker`: the job queue, AI grading and the sweepers, in their own process (see **The worker**) |
| Deploy | `tools/deploy-azure.ps1` (via MindAttic.Deploy) | Clean export, migrate, publish, zip deploy, health and login smoke checks |
| Deploy (CI) | `.github/workflows/azure-deploy.yml` | Manual trigger, once the repo secrets exist. Runs `tools/deploy-azure.ps1` itself, so it is the same deploy (GAD-T477); target production, staging or promote (GAD-T652) |

## One-time setup

Run in a normal **PowerShell** window. You need the Azure CLI signed in to the MindAttic subscription, and
the name of the storage account that already holds Tutor's key ring (Azure portal → Storage accounts).

```powershell
cd D:\Projects\MindAttic\Gadfly
az login
az account show --query name -o tsv

# 1. Create the resources (prints every az command as it runs; add -WhatIf to only print them)
powershell -File tools\provision-azure.ps1 -StorageAccount <tutor's storage account name>

# 2. Put the secrets in Key Vault (one line each; paste real values where it says so)
$kv = 'kv-tutor-f65744'
# 32 random bytes, base64 (RNGCryptoServiceProvider works in Windows PowerShell 5.1 and 7; RandomNumberGenerator.GetBytes is 7+ only)
function New-Secret { $b = New-Object byte[] 32; $r = New-Object Security.Cryptography.RNGCryptoServiceProvider; $r.GetBytes($b); $r.Dispose(); [Convert]::ToBase64String($b) }
az keyvault secret set --vault-name $kv -n gadfly-pepper-v1       --value (New-Secret) --query id -o tsv
az keyvault secret set --vault-name $kv -n gadfly-reset-token-key --value (New-Secret) --query id -o tsv
az keyvault secret set --vault-name $kv -n gadfly-bootstrap-token --value '<first admin password, 12+ characters>' --query id -o tsv
az keyvault secret set --vault-name $kv -n llm-claude    --value '<Anthropic API key>' --query id -o tsv
az keyvault secret set --vault-name $kv -n llm-openai    --value '<OpenAI API key>'    --query id -o tsv
az keyvault secret set --vault-name $kv -n llm-gemini    --value '<Gemini API key>'    --query id -o tsv
az keyvault secret set --vault-name $kv -n llm-deepseek  --value '<DeepSeek API key>'  --query id -o tsv
az keyvault secret set --vault-name $kv -n elevenlabs    --value '<ElevenLabs API key>' --query id -o tsv
```

Each `az keyvault secret set` prints the secret's URL; an error instead means the step failed.

3. **Database users.** In the Azure portal open SQL databases → `Gadfly` → Query editor, sign in as the
   server's Entra admin, and run the T-SQL that `provision-azure.ps1` printed at the end (it creates the
   app's user and the deploy identity's user). It answers "Commands completed successfully".
4. **Deploy permission.** In Entra ID → App registrations → `gh-mindattic-webapps-deploy`:
   Certificates & secrets → Federated credentials → Add → GitHub Actions, organization `mindattic`,
   repository `Gadfly`, entity Branch, branch `main`. Then in `mindattic-gadfly` → Access control (IAM) →
   Add role assignment → Website Contributor → that app registration.
5. **Repo secrets.** In GitHub → mindattic/Gadfly → Settings → Secrets and variables → Actions, add
   `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (the same values Tutor's repo uses).

## Deploying

Redeploys are one command, from a signed-in `az` (`az login`; your Entra account must be able to alter the `Gadfly`
database and deploy to the app):

```powershell
cd D:\Projects\MindAttic\MindAttic.Deploy
npm run deploy -- --app gadfly            # the real thing
npm run deploy -- --app gadfly --dry-run  # export, restore, publish and zip; migrates and deploys nothing
```

MindAttic.Deploy registers Gadfly as the `gadfly` entry in its `projects.json` `apps[]` and runs
`Gadfly\tools\deploy-azure.ps1` as that app's preDeploy hook (it passes `-DryRun` under `--dry-run`). The script
can also be run directly: `powershell -NoProfile -File tools\deploy-azure.ps1 [-Ref <commit>] [-DryRun]`. It:

1. refuses a commit that is not on `origin/main`, and an app whose `Gadfly__AiMode` is not `mock` (override with
   `-AllowUnpushed`, `-AllowLive`); it only reads that one setting and never changes any app setting or secret;
2. exports the commit with `git archive` into a clean scratch folder, so other people's uncommitted edits in the
   working tree are never published;
3. runs `dotnet restore`, then `dotnet ef database update` against Azure SQL with Entra auth. **If this fails the
   script stops and nothing is deployed**: new code never runs on an old database;
4. publishes (Release), publishes `Gadfly.Worker` into the same package as the WebJob (see **The worker**), zips, and runs `az webapp deploy --type zip --async true`, then waits for Kudu to report
   the deployment (status 4 is success; Kudu sometimes answers a 500 timeout for a deploy that did succeed, so the
   script reads the deployment log rather than trusting that reply);
5. polls `https://mindattic-gadfly.azurewebsites.net/healthz` until 200, then checks that `/login` is 200;
6. tags the deployed commit `vN` and pushes the tag (see **Versions** below).

### Versions

Every deploy is one whole number higher than the last: V1, V2, V3, and so on, never V1.1 or V2.0.3 (GAD-T291). The
number is the deploy count, not a semantic version.

- The record is the git tags `v1`, `v2`, ... on origin. `deploy-azure.ps1` takes the highest `vN` tag (on origin, or
  local and not yet pushed), so the deploy is N+1, and passes it to `dotnet publish` as `-p:GadflyVersion=N+1`
  (the export has no `.git`, so the build cannot work it out itself). With no `vN` tag yet the first deploy is V1.
- The app shows it as **V&lt;N&gt;** at the end of every browser tab title (`Admin · Gadfly Exams · V3`) and next to
  "Gadfly Exams" in the header (`AppVersion.Label`, baked in as the assembly metadata `GadflyVersion`).
- The tag is created and pushed **only after** the deploy is live and the health and login checks pass, so a failed
  deploy uses up no number. A dry run builds with the number it would use and tags nothing.
- Two deploys at once: the script checks again just before uploading and stops if the number was taken meanwhile;
  `git tag` and `git push` of the tag both refuse an existing tag, so two deploys cannot both claim the same N.
- A local or dev build (no `GadflyVersion`) shows the highest `vN` tag in the checkout plus `dev` (`V3 dev`), or just
  `dev` before the first tag.
- Never pass a non-whole number (the build refuses it), never create or move a `vN` tag by hand except to repair a
  deploy the script reported as live but untagged.

The manual GitHub workflow (GitHub → mindattic/Gadfly → Actions → **Build and deploy Gadfly to Azure App Service** →
Run workflow, from `main`) has no deploy steps of its own: it signs in to Azure with the repo secrets and runs
`tools/deploy-azure.ps1` on a Windows runner (GAD-T477). So a CI deploy gets the same version number and `vN` tag, the
same mock-mode refusal, migrate-first, Kudu follow-up and health and login checks as a desk deploy. Its **dry run**
box passes `-DryRun`, and its **target** picks `production` (the default), `staging` (`-Slot staging`) or `promote`
(`-Promote`), as in **Staging and production slots** below. It never passes `-AllowLive` or `-AllowUnpushed`; those
stay desk decisions. The deploy identity
needs Website Contributor on the app (it reads `Gadfly__AiMode` and the Kudu log) and its database user; the workflow's
own token has `contents: write` to push the tag. Change deploy steps in the script only: the workflow just calls it.

The first sign-in on a new deployment is `admin` with the `gadfly-bootstrap-token` password; you're made to change it
and set up MFA. Then rotate the `gadfly-bootstrap-token` secret to a new random value.

### Staging and production slots

A release can go through the App Service's `staging` slot first (GAD-T336): deploy the commit there, check it on its
own database, then promote it, which swaps the very build staging ran into production. The plain production deploy
above still works and is unchanged.

| Step | Desk | CI (Run workflow, **target**) |
|---|---|---|
| Deploy to staging | `powershell -NoProfile -File tools\deploy-azure.ps1 -Slot staging [-Ref <commit>]` | `staging` |
| Promote to production | `powershell -NoProfile -File tools\deploy-azure.ps1 -Promote` | `promote` |
| Straight to production | `npm run deploy -- --app gadfly` in MindAttic.Deploy, or the script with no `-Slot` | `production` |

`-DryRun` (the workflow's **dry run** box) works with each of them. For a CI staging deploy the deploy identity also
needs a user on the `Gadfly-staging` database that can alter it, like the one on `Gadfly`; Website Contributor on the
app covers its slots.

1. **Deploy to staging.** `-Slot staging` runs the same checks and steps as a production deploy against the slot:
   `Gadfly__AiMode` on the slot must be `mock`, the commit must be on `origin/main`, the migrations go to the
   **`Gadfly-staging`** database (never `Gadfly`), then it publishes as the next V&lt;N&gt;, zip-deploys to
   `mindattic-gadfly/staging` and waits for the slot's `/healthz`, `/login` and `/healthz/version`. It ends with
   `Deployed <sha> as V<N> to staging ... not tagged. Promote with -Promote.`
2. **Check it.** Open the slot's address (usually `https://mindattic-gadfly-staging.azurewebsites.net`; Azure has
   the real one: `az webapp show -g MyApps -n mindattic-gadfly --slot staging --query defaultHostName`) and run the
   smoke checks from the OPERATIONS.md release checklist against it. It runs on the staging database, so nothing done there
   touches production data.
3. **Promote.** `-Promote` builds nothing. It reads `V<N> <commit>` from the staging slot's `/healthz/version`,
   exports that commit and migrates the **production** database `Gadfly` with that commit's migrations (a failure
   stops it before the swap), checks staging still runs the same commit, then swaps `staging` into production
   (`az webapp deployment slot swap`; Azure warms the slot up first). It then waits for production's `/healthz` and
   `/login`, checks production's `/healthz/version` now reports that version and commit, and tags it `vN`. The mock
   check reads production's own `Gadfly__AiMode`. After a swap the staging slot runs the previous production build;
   deploy to staging again before the next promotion.

**Slot settings stay put.** The swap moves code, not configuration. The settings that tie a slot to its own
resources must be marked as deployment slot settings (sticky) on each slot: the database connection string (`Gadfly` for
production, `Gadfly-staging` for staging), the Data Protection key blob, and `Gadfly__AiMode`. So the promoted build
runs on production's database, keys and AI mode, and staging keeps its own. A setting added to production later that
must not follow a swap has to be marked as a slot setting too (Configuration → the setting → *Deployment slot
setting*).

**Versions.** A staging deploy is built as the next number (the highest `vN` tag plus one) and shows that V&lt;N&gt;,
but **tags nothing**, so staging uses up no number and can be redeployed as often as needed. Promotion keeps the
number baked into the staged build and tags `vN` on that commit only once production reports it. If a straight
production deploy has taken that number meanwhile, `-Promote` refuses: deploy to staging again and promote that.
`GET /healthz/version` (anonymous) answers `V<N> <full commit sha>` for a deployed build; the script uses it to know
what staging runs and to confirm what production runs after the swap, and it is the quickest way to see what any slot
is running.

### App settings a release must check

- **Admin desk.** `Gadfly:AdminDesk:Enabled` is off by default in Production (GAD-T247). Set
  `Gadfly__AdminDesk__Enabled=true` in the App Service settings to keep the desk. Only the organization's Owners and
  Admins can use it, and every desk admin needs MFA. `Gadfly:AdminDesk:Admins` is deprecated (GAD-T459): honoured
  through V2 only, so give the owner's account the Owner or Admin role.
- **Voice.** `Gadfly:Voice:Mode` defaults to `off` outside Development, and with voice off the get-ready page's
  **Spoken examiner** check fails and no exam can begin. A deployment that runs exams sets `Gadfly__Voice__Mode`
  (`mock` for the mock instance, `live` with `MindAttic__Avatar__ElevenLabs__VoiceId` and the ElevenLabs key; live
  without the voice id turns voice off). Neither `provision-azure.ps1` nor this page sets it for you.
- **AI mode.** `Gadfly__AiMode` must be `mock` unless the owner has approved live spending; the script refuses
  otherwise without `-AllowLive`.

The full release checklist and smoke checks are in [OPERATIONS.md](OPERATIONS.md).

## The worker

Exams are taken on the website; grading and the other queued work run in a separate process beside it, the
**Gadfly.Worker** console host, deployed as the Azure **continuous WebJob `GadflyWorker`** in the same App Service
(GAD-T700). It shares the site's service registration (`GadflyServices.AddGadflyServices`, used by both
`Gadfly.Server/Program.cs` and `Gadfly.Worker/Program.cs`), so it reads the same app settings (App Service passes them
to WebJobs as environment variables), the same connection string and Key Vault references, and uses the same managed
identity, Data Protection key ring and AI panel (`Gadfly__AiMode`).

| Runs in | What |
|---|---|
| Worker (and the web app while `Gadfly:Jobs:RunInWeb` is true) | `JobWorker` (every queued job: AI grading, source processing, email, webhooks, retention, stale-session close, LTI passback and roster refresh, reminders); `GradingSweeper` (queues grading ~15 s after an exam ends); `ReminderScheduler`, `StaleSessionScheduler`, `RetentionScheduler`, `LtiPassbackSweeper`, `LtiRosterRefreshScheduler`, `ResponseBackfillService` |
| Both, each for its own process | `ApiCallTimingWriter` and `EvidenceSyncWriter` (drain in-memory queues of the process that filled them), `DeskAdminsListWarning` (a startup log line) |
| Web app only | Pages, the exam hub and live sessions, voice, the API; the web app always enqueues jobs |

The list of queue-driven services is `GadflyServices.BackgroundWork`.

**Packaging.** `tools/deploy-azure.ps1` publishes `Gadfly.Worker` into the site's package at
`App_Data/jobs/continuous/GadflyWorker/`, so every normal deploy (desk, MindAttic.Deploy or the CI workflow, any slot)
ships the worker with the site and Azure restarts it on the new build. `run.cmd` starts it (`dotnet Gadfly.Worker.dll`);
`settings.job` makes it a singleton (one instance even if the plan scales out) and gives it 60 s to stop. The staging
slot runs its own copy against `Gadfly-staging`.

**Shutdown and logs.** Azure stops a continuous WebJob by creating the file named in `WEBJOBS_SHUTDOWN_FILE` (and with
SIGTERM / Ctrl+C); the worker watches for it and stops within 50 s, and a job cut off mid-run is put back on the queue
without using up an attempt. It logs to the console (the WebJob log in Kudu) and to the same Azure application log as
the site. Its first line is the health line: `Gadfly.Worker V<N> <commit> started: environment ..., database Gadfly on
..., AI panel mock (...), job kinds [...], background services [...], web app runs jobs too: True`. Every job handler is
built once at startup, so a missing registration stops the worker there with the error rather than failing its first job.

**Always On is off.** Since 2026-11-25 (GAD-T768) Always On is off for the app and its staging slot, so the site
unloads after 20 minutes without a request and loads again on the next one; `provision-azure.ps1` leaves it off so a
re-provision does not turn it back on. A continuous WebJob only runs while the site is loaded, so the `GadflyWorker`
WebJob stops with it, and so do the in-process schedulers the web app hosts (exam reminders, data retention, stale
sessions, the grading sweep and LTI grade passback): none of them runs while the site is idle, and queued jobs and due
sweeps wait until a request loads it again. To keep them running around the clock, turn Always On back on (the plan,
`ASP-MyApps-99fe`, is Standard or higher, which the staging slot already requires, so it has the setting):
`az webapp config set -g MyApps -n mindattic-gadfly --always-on true`.

**Idle shutdown (GAD-T775).** Gadfly is hosted on demand: the site stays stopped until `/kickstart gadfly` starts it, and
it stops itself once nobody is using it and nothing is left to do. A watchdog in the web app (`Gadfly.Server/Platform/IdleShutdown.cs`)
checks every minute; after `Gadfly__IdleShutdown__Minutes` (default `20`; `0` turns it off; always off in Development)
with no user activity it asks the database for pending work, and only when there is none does it stop its own site
through ARM with its system-assigned managed identity, logging the decision and its reason (a held shutdown logs what
held it). Pending work is: a job queued (due within the hour) or running, which covers grading and notification emails;
an ended attempt not yet graded, or a grade waiting on the AI panel; an LTI grade passback not yet sent and not out of
tries. Grades waiting on a person do not hold it. So a student can finish an exam, walk away, and still have it scored,
posted to their inbox and emailed. User activity is any HTTP request and any browser call over a hub (Blazor clicks,
voice); `/healthz*`, `/robots.txt` and App Service's own pings (health check, Always On, warm-up) do not count, Kudu
answers on the scm host, and the watchdog's own calls go to ARM. The serverless SQL database then pauses on its own
auto-pause delay. The site finds itself from App Service's environment (`WEBSITE_SITE_NAME`, `WEBSITE_RESOURCE_GROUP`,
`WEBSITE_OWNER_NAME` for the subscription, `WEBSITE_SLOT_NAME` for a slot), so nothing is hard-coded; off App Service
the watchdog stays off. Stopping needs **Website Contributor on the site itself, and nothing wider**, for the site's own
identity; `tools/provision-azure.ps1` grants it (and the slot's on the slot). A recreated site has a new identity, so
grant it again after one:

```powershell
$principal = az webapp identity show -g MyApps -n mindattic-gadfly --query principalId -o tsv
$siteId = az webapp show -g MyApps -n mindattic-gadfly --query id -o tsv
az role assignment create --assignee-object-id $principal --assignee-principal-type ServicePrincipal --role "Website Contributor" --scope $siteId
```

Without the role the stop is refused and logged as an error (the site then just keeps running); to turn the watchdog
off, set `Gadfly__IdleShutdown__Minutes=0`.

**`Gadfly:Jobs:RunInWeb`** (default `true`). While it is true the web app still runs the job worker and the sweepers
too; that is safe, since a job is claimed by an atomic row update and several processes may share the queue (the
schedulers only queue a sweep when none is waiting, and a grade record is keyed by its session, so two grading sweepers
cannot start one session twice). Set it to `false` and the web app only enqueues. **Production keeps the
default until the WebJob is confirmed running** (below); then flip it on each slot:

```powershell
az webapp config appsettings set -g MyApps -n mindattic-gadfly --settings Gadfly__Jobs__RunInWeb=false
az webapp config appsettings set -g MyApps -n mindattic-gadfly --slot staging --settings Gadfly__Jobs__RunInWeb=false
```

Changing a setting restarts the site and the WebJob. To go back, set it to `true` (or delete it).

**Confirming it runs.**

```powershell
az webapp webjob continuous list -g MyApps -n mindattic-gadfly -o table   # GadflyWorker, status Running
```

or Kudu: `https://mindattic-gadfly.scm.azurewebsites.net/azurejobs/#/jobs/continuous/GadflyWorker` (the WebJobs
dashboard; its log should show the health line above and then `Running job <id> (grading.ai), attempt 1.` lines after an exam
ends). In the portal: the app → Settings → WebJobs. With `RunInWeb=false`, a finished exam still being graded is the
end-to-end check.

**Locally**, `.un.ps1 -Worker` runs the worker in Development against LocalDB, in mock mode; start the server with
`$env:Gadfly__Jobs__RunInWeb='false'` beside it to try the split.

## Running it locally

`run.ps1` at the repo root starts Gadfly.Server in Development on `https://localhost:7311`, in mock mode (it sets
`Gadfly__AiMode=mock`, so no provider is called), against LocalDB (database `Gadfly`, migrated on start):

```powershell
.\run.ps1                  # serve
.\run.ps1 -SeedDemo        # also seed Gadfly Demo Academy (demo.owner ... demo.student)
.\run.ps1 -SeedClassroom   # also seed the classroom (Teacher_A..C, Student_A..Z)
.\run.ps1 -Tour            # run the demo tour (docs/TOUR.md) on its own database and print its result line
```

The seeders use `-Password <text>` (or `Gadfly:Demo:Password`) as every account's password, or generate one and
print it once in the log.

## After the first deploy

- Custom domain: create the DNS records first (CNAME to the app's default host name, TXT `asuid.<domain>` with the app's
  `customDomainVerificationId`), then run `tools/provision-azure.ps1 -CustomDomain www.example.com`. It adds the host
  name, creates a free managed certificate and binds it (SNI). HTTPS-only and TLS 1.2 are already set, and the app
  redirects to HTTPS and sends HSTS in production.
- If the GitHub workflow's trigger is ever switched to `push` on `main`, every push becomes a numbered release: think twice.
- Watch `/healthz/ready` (anonymous) and `/healthz/providers` (SuperAdmin only) for failing checks and LLM keys; see
  [OPERATIONS.md](OPERATIONS.md).
