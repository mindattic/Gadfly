# Deploying Gadfly to Azure

Status: prepared, not yet run (GAD-T63). Running it creates billable Azure resources, so it waits for
the owner's go-ahead (decision D16 in [SOCRATIC_PLATFORM.md](SOCRATIC_PLATFORM.md)).

Gadfly.Server runs next to Tutor in the existing **MyApps** resource group, on the existing App Service
plan and Azure SQL server, with a managed identity and no passwords anywhere:

| Piece | Name | Notes |
|---|---|---|
| App Service | `mindattic-gadfly` | .NET 10, WebSockets on (Blazor Server), always on, HTTPS only |
| Database | `Gadfly` on `sql-mindattic-f65744` | Serverless, auto-pause after 60 min |
| Data Protection keys | blob container `gadfly-dp-keys`, Key Vault key `gadfly-dp-protect` | Signs cookies, launch links and webhook secrets |
| Secrets | Key Vault `kv-tutor-f65744` | Auth pepper, reset key, bootstrap token, LLM and ElevenLabs keys |
| Deploy | `.github/workflows/azure-deploy.yml` | Manual trigger; build, migrate (EF bundle), deploy, smoke test |

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
az keyvault secret set --vault-name $kv -n gadfly-pepper-v1       --value ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))) --query id -o tsv
az keyvault secret set --vault-name $kv -n gadfly-reset-token-key --value ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))) --query id -o tsv
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

GitHub → mindattic/Gadfly → Actions → **Build and deploy Gadfly to Azure App Service** → Run workflow.
It builds, applies migrations, deploys and waits until `https://mindattic-gadfly.azurewebsites.net/healthz`
and `/login` answer 200. The first sign-in is `admin` with the `gadfly-bootstrap-token` password; you're
made to change it and set up MFA. Then rotate the `gadfly-bootstrap-token` secret to a new random value.

## After the first deploy

- Custom domain and certificate (App Service → Custom domains), if wanted.
- Switch the workflow trigger to `push` on `main` once a deploy has gone through cleanly.
- Watch `/healthz/providers` (SuperAdmin only) for failing LLM keys.
