# Operating Gadfly

The runbook for whoever runs a Gadfly deployment (MindAttic platform staff): how to see that it is healthy, how to
back it up and restore it, how to rotate its secrets, what to do when something goes wrong, and the checklist for a
release. Setting a deployment up the first time is in [DEPLOY.md](DEPLOY.md); what the app keeps about people is in
[PRIVACY.md](PRIVACY.md). Everything here was checked against the code and scripts as they are now; where a name
appears in `code`, it is the exact setting, route or table the app uses.

The live deployment, for reference:

| Piece | Name |
|---|---|
| App Service | `mindattic-gadfly` in resource group `MyApps` (https://mindattic-gadfly.azurewebsites.net) |
| Database | `Gadfly` on Azure SQL server `sql-mindattic-f65744` (resource group `rg-mindattic-data`), serverless, auto-pauses after 60 minutes idle |
| Key Vault | `kv-tutor-f65744` (secrets, and the key `gadfly-dp-protect` that wraps the Data Protection key ring) |
| Data Protection key ring | blob `gadfly-dp-keys/keys.xml` in storage account `stmindatticdbf65744` |

## 1. Monitoring

### Health endpoints

| Route | Who may call it | Answers |
|---|---|---|
| `GET /healthz` | Anyone (no sign-in) | `Healthy` (200) when the database answers, `Unhealthy` (503) when it does not. The ASP.NET Core health check with one check, `database`. Nothing else is in it. |
| `GET /healthz/ready` | Anyone (no sign-in) | JSON: `{"status":"ready"\|"unready","checks":[{"name":"Database","state":"ok"\|"warning"\|"failing"}, ...]}`. 200 while nothing is failing, 503 when a check fails. Names and states only: no counts, provider names or error text. Not cached. |
| `GET /healthz/providers` | Platform admin only | JSON snapshot of each live AI provider's health: calls, failures, consecutive failures, last error. Read from memory since the server started; it makes no provider call. |
| `GET /healthz/call-timings?session=<id>&limit=200` | Platform admin only | Plain text: the latest outside calls on live exams (one session's, or every session's without `session`), newest first; the same calls slowest first; then count, average and maximum milliseconds per call name. `limit` is 1 to 2000 (default 200). Read from `gadfly.ApiCallTimings`. |
| `GET /healthz/voice-recognition` | Platform admin only | Plain text: the speech recognizer's latency from the end of speech to its final transcript (count, average, p50, p95, max) and the share of unsure words per answer, over the last 2,000 samples. In memory only: empty after a restart. |

"Platform admin" is the MindAttic platform administrator account (the `admin` account seeded from the bootstrap token,
and anyone given that library role), not an organization's Owner or Admin.

`/healthz/ready` runs the same checks as the system status page:

| Check | Fails (503) when | Warns when |
|---|---|---|
| Database | it does not answer | |
| Database migrations | migrations are not applied (the database is behind the app), or the database is down | the migration history could not be read |
| AI panel | | the mock panel is in use outside Development, or every live provider (or some) is failing |
| Voice | | voice is off, simulated outside Development, or the latest speech call of some kind failed |
| Background jobs | | a job anywhere on the platform failed in the last 24 hours, or a queued job has waited more than 15 minutes (the worker may be stalled) |
| Webhook deliveries | | a delivery was given up in the last 24 hours |
| Errors in the log | | this server logged an error in the last hour |

Only the first two make the app unready. Point an uptime monitor at `/healthz/ready` and alert on anything but 200; a
second, slower probe of `/healthz` tells "the app is down" apart from "the database is behind".

Note that the live app runs in mock mode (`Gadfly__AiMode=mock`) until the owner says otherwise, so its AI panel check
reads *warning* by design; that does not make it unready.

### The system status page

**System status** (`/admin/status`) is the readable version, for an organization's Owners and Admins and for the
platform admin. It calls no AI or speech provider, so refreshing it costs nothing. It shows the checks above, the app's
version and environment, the AI mode and why it is in force, each provider's calls and failures, the latest outcome of
each kind of speech call, the database and its migrations (and its size, for the platform admin), the organization's
background jobs (waiting, running, and failed or cancelled in the last 7 days, with **Retry** and **Cancel**), webhook
deliveries, storage use, and how long each outside call on a live exam took in the last 24 hours. The platform admin
also sees each provider's last error and the last 50 errors this server logged (kept in memory since it started). The
admin guide's [System status and jobs](guides/admin-system-status.md) page describes every part.

### Logs

| Where | What | How long |
|---|---|---|
| App Service application log (`LogFiles/Application`, and the **Log stream** in the portal or `az webapp log tail -g MyApps -n mindattic-gadfly`) | Everything the app logs at Information and above (EF Core commands and ASP.NET Core at Warning) | Files of up to 50 MB, the 10 newest kept. Application logging to the file system must be on for the app: `az webapp log config -g MyApps -n mindattic-gadfly --application-logging filesystem --level information`. |
| MindAttic.Log's own database | The same structured log, in a SQLite file under the instance's MindAttic Vault folder (`Logging\Gadfly.Server\MindAttic.Log.db`) | Lives on the instance; not a backup. |
| System status, *Recent errors* | The last 50 errors since the server started | Memory only. |
| `gadfly.ApiCallTimings` | One row per outside call on a live exam (panel steps, the examiner's voice) | Deleted by the daily `privacy.retention` job once older than `Gadfly:Logs:CallTimingDays` (default 365). No setting can make it shorter than 184 days. |
| `gadfly.AuditEvents` | Every audited change, refusal, export and download | **Never deleted.** Retention and erasure clear the student's words and grades from an event's detail and mark it *Redacted*; the row stays. |

The six-month floor on both logs is GAD-T466's reading of the EU AI Act's log-keeping duties (Arts. 12, 19 and 26(6));
[COMPLIANCE.md](COMPLIANCE.md) marks that reading for counsel. An organization's transcript retention period (7 days
at the shortest) never shortens a log.

Useful log lines to search for:

- `Gadfly AI panel: MOCK mode.` or `Gadfly AI panel: live providers.` at startup: which panel is running, and why.
- `LLM provider {Provider} has failed {Count} calls in a row; the panel is running without it.` A provider is down.
- `Gadfly:Voice:Mode is live but MindAttic:Avatar:ElevenLabs:VoiceId is not set; voice mode is off.` (standard error at startup).
- `Log retention: deleted {Count} call timing row(s)` from the daily retention job.
- A warning naming accounts still in the deprecated `Gadfly:AdminDesk:Admins` list (see section 4.4).

## 2. Backups and restore

### What Azure does, and what Gadfly does not

Gadfly keeps everything durable in the one Azure SQL database (the `gadfly` schema, plus the `auth` schema of
MindAttic.Authentication: accounts, password hashes, MFA). **Gadfly has no backup job of its own**: it relies on Azure
SQL's automatic backups. No exam audio or video is ever stored, so there is nothing outside the database to back up
except the secrets below.

Two things live outside the database and must survive with it:

- **The Data Protection key ring** (blob `gadfly-dp-keys/keys.xml`, wrapped by Key Vault key `gadfly-dp-protect`). It
  signs sign-in cookies and launch links, and it encrypts what the database holds in protected form: **every webhook
  signing secret and Gadfly's LTI private key**. A restored database is useless for webhooks and LTI without the ring
  that was in use when those rows were written. Turn on soft delete and versioning for that storage container, and
  keep Key Vault's soft delete and purge protection on.
- **Key Vault secrets** (section 3). Key Vault keeps previous versions of each secret; never purge one.

### Azure SQL settings to check once

The database was created with `--backup-storage-redundancy Local`: backups stay in one datacenter, so **geo-restore is
not available**. Point-in-time restore keeps 7 days by default; long-term retention is not set up by the provisioning
script. To lengthen both (this has a storage cost; get the owner's approval first):

```powershell
$rg = 'rg-mindattic-data'; $server = 'sql-mindattic-f65744'; $db = 'Gadfly'
# Point-in-time restore window: up to 35 days.
az sql db str-policy set -g $rg -s $server -n $db --retention-days 35 --diffbackup-hours 12
# Long-term retention: weekly for 8 weeks, monthly for 12 months, the first weekly of each year for 5 years.
az sql db ltr-policy set -g $rg -s $server -n $db --weekly-retention P8W --monthly-retention P12M --yearly-retention P5Y --week-of-year 1
# What is configured now
az sql db str-policy show -g $rg -s $server -n $db
az sql db ltr-policy show -g $rg -s $server -n $db
```

Geo-redundant backups need the redundancy changed to `Geo` (`az sql db update ... --backup-storage-redundancy Geo`),
which applies to backups taken from then on.

### Restoring

A restore always creates a **new** database; it never overwrites `Gadfly`. Restore beside it, check it, then swap.

```powershell
$rg = 'rg-mindattic-data'; $server = 'sql-mindattic-f65744'
# 1. Point in time (UTC), into a new database
az sql db restore -g $rg -s $server -n Gadfly --dest-name Gadfly-restore --time "2026-11-22T09:30:00Z"
#    or from a long-term backup
az sql db ltr-backup list -l <region> -s $server -d Gadfly -o table
az sql db ltr-backup restore --backup-id "<id from the list>" --dest-database Gadfly-restore --dest-server $server --dest-resource-group $rg
```

2. In the restored database (portal Query editor, as the server's Entra admin), run the same `CREATE USER ... FROM
   EXTERNAL PROVIDER` lines for `mindattic-gadfly` and `gh-mindattic-webapps-deploy` that DEPLOY.md lists: a restored
   database keeps its users, but check they are there.
3. Swap it in. Either rename (`Gadfly` to `Gadfly-old`, then `Gadfly-restore` to `Gadfly`, with
   `ALTER DATABASE ... MODIFY NAME` on `master` or `az sql db rename`), or point the app at the new name by changing
   the `ConnectionStrings__Gadfly` app setting. Stop the app first (`az webapp stop -g MyApps -n mindattic-gadfly`)
   so no exam writes to the old one meanwhile.
4. Bring the schema up to the deployed code. The restored database may be older than the app. Run the deploy script's
   migration step against it, from a checkout of the commit that is deployed (its `vN` tag):
   `dotnet ef database update --project Gadfly.Server/Gadfly.Server.csproj --connection "Server=tcp:sql-mindattic-f65744.database.windows.net,1433;Database=Gadfly;Authentication=Active Directory Default;Encrypt=True;"`.
   The production app never migrates itself at startup.
5. Start the app and check `GET /healthz/ready` answers 200 with `Database` and `Database migrations` both `ok`, then
   sign in and open `/admin/status`.
6. Tell the affected organizations what was lost: everything written after the restore point (attempts, grades,
   audit events). Exams that were in progress at that moment are gone from the restored copy.

Erasures carried out after the restore point are undone by a restore. Re-run them (the audit log of the old database
shows each `privacy.erased` event) before the restored copy is used.

## 3. Rotating keys and secrets

Secrets reach the app as App Service settings that are Key Vault references
(`@Microsoft.KeyVault(VaultName=kv-tutor-f65744;SecretName=...)`), read through MindAttic.Vault's configuration
buckets (`LLM`, `Security`, `Notifications`, and `Payments` for card checkout). On a developer machine the same names
come from the local MindAttic Vault files instead. After changing a secret's value in Key Vault, restart the app
(`az webapp restart -g MyApps -n mindattic-gadfly`) so the reference is read again.

| Secret | Key Vault name | App setting | Rotating it |
|---|---|---|---|
| Password pepper | `gadfly-pepper-v1` | `MindAttic__Vault__Security__pepper-v1` | **Never replace v1 in place**: every password would stop verifying. Add a `pepper-v2` secret and setting, set `MindAttic__Auth__Crypto__CurrentPepperKeyId=v2`, keep v1. Passwords move to v2 as people sign in. See MindAttic.Authentication's OPERATIONS.md. |
| Password-reset token key | `gadfly-reset-token-key` | `MindAttic__Vault__Security__reset-token-key` | Replace any time. Reset links already sent stop working. |
| Bootstrap token | `gadfly-bootstrap-token` | `MindAttic__Vault__Security__bootstrap-token` | Used only when the database has no account at all, to create `admin` with this as a must-change password. Replace it with a long random value straight after the first sign-in, and again if it ever leaks. Don't delete it. |
| Anthropic, OpenAI, Gemini, DeepSeek keys | `llm-claude`, `llm-openai`, `llm-gemini`, `llm-deepseek` | `MindAttic__Vault__LLM__<provider>__apiKey` | Create the new key at the provider, set the secret, restart, check `/healthz/providers`, then revoke the old key. OpenAI's key also serves the backup speech recognizer. |
| ElevenLabs key | `elevenlabs` | `MindAttic__Vault__LLM__elevenlabs__apiKey` | As above; check a speech call on `/admin/status` afterwards. |
| Stripe (only if card checkout is on) | (your choice) | `MindAttic__Vault__Payments__stripe__secretKey`, `__webhookSecret` | Roll in the Stripe dashboard, set both, restart. Without `secretKey` the plan page shows no checkout. |
| Outgoing email (SMTP) | `smtp-password` (shared with mindattic-tutor) | `MindAttic__Vault__Notifications__email__password` (and `smtpHost`, `smtpPort`, `username`, `from`) | Production sends through Gmail SMTP: `smtp.gmail.com:587` as `mindatticinteractive@gmail.com`, password a Gmail app password. Create a new app password in that Google account, set the secret, restart both Gadfly and mindattic-tutor, send a test from **Email preview**, then revoke the old app password. Links in emails join `MindAttic__Auth__Reset__PublicBaseUrl` (`https://mindattic-gadfly.azurewebsites.net`). |

**Data Protection key ring.** The ring rolls its own keys every 90 days and keeps the old ones to read with; nothing
to do. Never delete the blob or the Key Vault key `gadfly-dp-protect`: losing them signs everyone out and, worse, makes
every stored webhook secret and the LTI private key unreadable.

**Gadfly's LTI signing key.** Generated once, the first time it is needed (2048-bit RSA), stored in
`gadfly.LtiToolKeys` with the private half encrypted by the Data Protection ring, and published at `/lti/jwks.json`.
One key serves every organization, and the newest row signs. **To rotate it** (GAD-T478), a platform admin opens
**Organizations → LTI signing key** (`/platform/lti-key`) and presses **Rotate the signing key**, then confirms:

1. A new key is made and signs everything from that moment (deep-linking replies, roster and gradebook token requests).
2. The old key stays in `/lti/jwks.json` next to the new one for a grace period, `Gadfly:Lti:KeyGraceDays` (default
   **7** days, 1 to 90), so an LMS that cached the old key set keeps accepting Gadfly's signatures.
3. After the grace period the old key is left out of the key set and its row, private key included, is deleted. This
   happens the next time the key set is fetched or the page is opened.

Both steps are audited: `lti.key.rotated` (who, the new and old key ids, when the old one retires) and `lti.key.retired`
(system). Key material is never written to the log. Connected LMSs fetch keys from the key-set URL, so nothing needs
re-registering in any LMS. Rotate on a schedule (yearly is plenty) or at once if the key may have leaked; after a leak,
expect the old key to stay valid for the grace period, so lower `Gadfly__Lti__KeyGraceDays` to 1 first if that matters
more than an LMS with a stale cache. If the Data Protection key ring is lost, the stored private key can't be read:
rotate, and the new key is usable at once.

**Organization API keys and webhook secrets.** Owners and Admins rotate their own on **API keys** (`/org/api-keys`) and
**Webhooks** (`/org/webhooks`); platform staff need not be involved.

**Research pseudonym key.** Each organization has its own, made when research is first set up and kept in
`gadfly.ResearchSettings` (never exported or shown). An organization's Owner or Admin replaces it on **Research**
(*Replace the study codes*); it is audited as `research.key.replaced`. Replacing it changes every code from then on:
later exports cannot be linked to earlier ones, and a student's own study code changes too. Do it only when starting a
new, separate study, never as routine rotation. Platform staff should not change it in the database.

## 4. Incidents

### 4.1 An AI provider is down

What the app does: Gadfly's AI panel is several providers voting. A provider that fails calls in a row is marked
failing (logged once as an error) and the panel carries on without it; `/admin/status` and `/healthz/ready` show
*AI panel: warning*. If every provider fails, AI grading and question generation stall or fall back (the check says
so). The mode is chosen **once at startup** and never flips during a run: with `Gadfly:AiMode=auto` the app probes the
providers at startup and uses the labelled mock panel only if none answered; with `live` it stays live.

Steps:

1. `/healthz/providers` (or the provider table on `/admin/status`) to see which provider and its last error.
2. A bad or exhausted key: rotate it (section 3) and restart. An outage at the provider: wait; the rest of the panel
   keeps working.
3. Every provider down during exams: pause or extend running exams from the live monitor (4.3) rather than let students
   sit through stalled questions. Do **not** switch a live deployment to mock to get through: mock output is labelled
   as simulated and is no evaluation at all.
4. AI preliminary grading that failed shows as failed `grading.ai` jobs; **Retry** them on `/admin/status` once the
   provider is back. A person grades every attempt anyway, so no grade is lost.

Speech: in live voice mode the recognizer falls back from ElevenLabs Scribe to OpenAI Whisper on its own, mid-session,
unless `MindAttic:SpeechRecognition:Provider` pins a single provider. If the examiner's voice (ElevenLabs) is down,
students cannot hear questions: pause running exams and tell them.

### 4.2 Answers are slow

1. `/admin/status`, *Time to the next question*: which kind of outside call is slow (a panel step, or the examiner's
   voice to first byte / to the end of the audio).
2. `/healthz/call-timings?session=<id>` for one student's session, or without `session` across all, slowest first.
3. `/healthz/voice-recognition` for the recognizer's own delay (p95 above a few seconds means students wait after they
   stop talking).
4. The first request after the serverless database has auto-paused takes about a minute. If the slowness is only at
   the start of the day, wake the database before an exam window rather than change anything.
5. A slow provider can be left out by removing its key (and restarting); fewer providers is faster but each vote
   counts more. Discuss with the owner first.

### 4.3 A session is stuck

- **Live sessions** (`/admin/live`) shows every exam in progress in the organization, with **Add time**, **Pause** /
  **Resume**, **End exam** and **Send a notice**. Each is audited. Pause holds the exam and closes the microphone;
  ending grades the attempt with the evidence so far, the same way time running out does.
- A student who closed the tab and never came back is closed automatically: every 15 minutes a sweep ends any running
  or paused session 30 minutes past its own deadline (extra time and pauses included). A session staff are holding is
  left alone.
- A student who can't get back in: they reload the page; the session resumes where it was. If the attempt is beyond
  saving, end it, then give the student another attempt from the admin desk or the assignments console.
- Background work that is stuck (a job waiting more than 15 minutes, or failed): **Retry** or **Cancel** it on
  `/admin/status`.
- Several sessions stuck at once usually means the database or a provider: check `/healthz/ready` and 4.1 first.

### 4.4 Nobody can get into the admin desk

The admin desk (`/admin/desk`) opens only when all of these hold:

1. `Gadfly:AdminDesk:Enabled` is `true`. **Production turns it off by default** (`appsettings.Production.json`), so
   the live app needs the App Service setting `Gadfly__AdminDesk__Enabled=true`.
2. The person is an **Owner or Admin** of the organization they have open. (The old `Gadfly:AdminDesk:Admins` list of
   user names is deprecated: it is honoured only up to release V2, in the organization named by
   `Gadfly:AdminDesk:Organization`, with a warning in the log, and ignored after that.)
3. They signed in with two-step verification (MFA). Without it the home page says the desk needs MFA and links to
   `/account/mfa/setup`.

The rest of the admin area (`/admin`, `/admin/live`, `/admin/status` and so on) needs only Owner or Admin with MFA,
not the desk setting.

If an Owner has lost their authenticator, they sign in with one of their recovery codes. If no Owner or Admin of an
organization can sign in, another Owner gives someone the role on **Members**; failing that, it is a MindAttic.Authentication
operator reset of that account's MFA (dual control for admin accounts, and tell the organization's owner out of band),
done by platform staff, then the role restored. Record what was done.

## 5. Release checklist

Before:

- [ ] Everything to ship is merged to `main` and pushed; the deploy script refuses a commit that is not on
      `origin/main`.
- [ ] `dotnet build Gadfly.slnx` is clean.
- [ ] The owner has run the full test suite (`/test`) on that commit and it is green. Agents never run the tests;
      failures become tickets before the release goes out.
- [ ] Migrations: list the new ones since the last tag (`git diff --stat v<N>..HEAD -- Gadfly.Server/Data/Migrations`).
      Read each one: anything that drops or rewrites data needs a fresh backup point noted (section 2) and the owner's OK.
      A dry run lists them against the live database: `npm run deploy -- --app gadfly --dry-run` in
      `MindAttic.Deploy`.
- [ ] App settings the release needs are set in the App Service *before* it goes out. Currently (GAD-T289):
      `Gadfly__AdminDesk__Enabled=true` to keep the owner's desk, and the owner's account is an Owner or Admin of
      its organization with MFA set up (the `Gadfly:AdminDesk:Admins` list no longer grants the desk after V2).
- [ ] `Gadfly__AiMode` is still `mock` unless the owner has said to go live (the script refuses otherwise; `-AllowLive`
      only on the owner's word).
- [ ] The databases are awake (`/kickstart gadfly`) so the migration step doesn't time out on a paused database.

Deploy through staging (per [DEPLOY.md](DEPLOY.md) "Staging and production slots"):

- [ ] `tools/deploy-azure.ps1 -Slot staging` (or the workflow with target `staging`): migrates `Gadfly-staging`,
      deploys to the staging slot, prints `Deployed <sha> as V<N> to staging ... not tagged`. Nothing is tagged.
- [ ] Run the smoke checks under *After* against the staging slot's address; its `/healthz/version` shows
      `V<N> <sha>`.
- [ ] `tools/deploy-azure.ps1 -Promote` (or target `promote`): migrates the production database with that commit's
      migrations, swaps staging into production (slot settings stay with their slot), checks production reports that
      commit, then tags `vN`. It printed `Deployed <sha> as V<N> ... tagged vN`.

Or straight to production (no staging step):

- [ ] `npm run deploy -- --app gadfly` from `MindAttic.Deploy` (it runs `tools/deploy-azure.ps1`): clean export of the
      commit, migrations first (a failure stops it before any code goes out), publish, zip deploy, `/healthz` and
      `/login` checks, then the `vN` tag.
- [ ] The script printed `Deployed <sha> as V<N> ... tagged vN`. The version is the next whole number after the highest
      `vN` tag (GAD-T291); a failed deploy uses no number. If it says the app is live but the tag failed, tag that
      commit `vN` by hand and push the tag; never otherwise create or move a `vN` tag.

After (smoke checks, all without spending provider credit):

- [ ] `GET /healthz` is 200 and `GET /healthz/ready` is 200 with `Database` and `Database migrations` `ok`.
- [ ] The browser tab title and header show the new **V&lt;N&gt;**, and `GET /healthz/version` answers `V<N> <sha>`.
- [ ] Sign in as the owner: the admin desk opens; `/admin` and `/admin/status` load, with no new *Errors in the log*.
- [ ] `/help` opens and a guide renders; the public site's home page loads signed out.
- [ ] If any organization uses LTI, `/lti/jwks.json` answers with a key.
- [ ] The log stream shows the expected startup lines (AI panel mode; no missing-secret or voice-off surprises).
- [ ] Note the release (version, commit, migrations applied) where the team tracks deploys.
