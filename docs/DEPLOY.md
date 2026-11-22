# Deploying Gadfly to Azure

Status: done (GAD-T63; the owner said go, decision D16 in [SOCRATIC_PLATFORM.md](SOCRATIC_PLATFORM.md)). The app is live at
https://mindattic-gadfly.azurewebsites.net and runs in mock mode (D23). The steps below are how it was set up and how to
repeat it in another subscription.

Gadfly.Server runs next to Tutor in the existing **MyApps** resource group, on the existing App Service
plan and Azure SQL server, with a managed identity and no passwords anywhere:

| Piece | Name | Notes |
|---|---|---|
| App Service | `mindattic-gadfly` | .NET 10, WebSockets on (Blazor Server), always on, HTTPS only |
| Database | `Gadfly` on `sql-mindattic-f65744` | Serverless, auto-pause after 60 min |
| Data Protection keys | blob container `gadfly-dp-keys`, Key Vault key `gadfly-dp-protect` | Signs cookies, launch links and webhook secrets |
| Secrets | Key Vault `kv-tutor-f65744` | Auth pepper, reset key, bootstrap token, LLM and ElevenLabs keys |
| Deploy | `tools/deploy-azure.ps1` (via MindAttic.Deploy) | Clean export, migrate, publish, zip deploy, health and login smoke checks |
| Deploy (CI) | `.github/workflows/azure-deploy.yml` | Manual trigger; same steps on a runner, once the repo secrets exist |

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
4. publishes (Release), zips, and runs `az webapp deploy --type zip --async true`, then waits for Kudu to report
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

The manual GitHub workflow does the same on a runner: GitHub → mindattic/Gadfly → Actions → **Build and deploy
Gadfly to Azure App Service** → Run workflow. The first sign-in is `admin` with the `gadfly-bootstrap-token`
password; you're made to change it and set up MFA. Then rotate the `gadfly-bootstrap-token` secret to a new random
value.

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

- Custom domain and certificate (App Service → Custom domains), if wanted.
- Switch the workflow trigger to `push` on `main` once a deploy has gone through cleanly.
- Watch `/healthz/providers` (SuperAdmin only) for failing LLM keys.
