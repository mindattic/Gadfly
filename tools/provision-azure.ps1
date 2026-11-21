<#
.SYNOPSIS
Creates Gadfly's Azure resources next to Tutor's (GAD-T63). Run once, by the owner, after approving the cost.

.DESCRIPTION
Uses the existing MindAttic resource group, App Service plan and Azure SQL server. Creates:
  - App Service  mindattic-gadfly (.NET 10, Windows, WebSockets on, always on, HTTPS only, system identity)
  - Azure SQL database  Gadfly  (serverless, auto-pause) on the existing server
  - Storage account container  gadfly-dp-keys  and Key Vault key  gadfly-dp-protect  for the Data Protection key ring
  - Key Vault references for the secrets the app reads (auth pepper, reset key, bootstrap token, LLM and ElevenLabs keys); you set the values
  - App settings: ConnectionStrings__Gadfly (Active Directory Default), DataProtection:*, Key Vault references
  - Role assignments: the app's identity on the database (via T-SQL printed below), the blob container and the key
Nothing here deletes anything. Re-running is safe: existing resources are left as they are.

.PARAMETER WhatIf
Print the commands instead of running them.
#>
[CmdletBinding()]
param(
  [string]$ResourceGroup = 'MyApps',
  [string]$Plan = 'ASP-MyApps-99fe',
  [string]$App = 'mindattic-gadfly',
  [string]$SqlServer = 'sql-mindattic-f65744',
  [string]$Database = 'Gadfly',
  [string]$KeyVault = 'kv-tutor-f65744',
  [Parameter(Mandatory)][string]$StorageAccount,
  [switch]$WhatIf
)
$ErrorActionPreference = 'Stop'

function Invoke-Az([string[]]$AzArgs) {
  Write-Host ("az " + ($AzArgs -join ' ')) -ForegroundColor Cyan
  if ($WhatIf) { return $null }
  $out = & az @AzArgs
  if ($LASTEXITCODE -ne 0) { throw "az $($AzArgs[0]) $($AzArgs[1]) failed (exit $LASTEXITCODE)." }
  return $out
}

Write-Host "== App Service" -ForegroundColor Green
$exists = az webapp show -g $ResourceGroup -n $App --query name -o tsv 2>$null
if (-not $exists) {
  Invoke-Az @('webapp','create','-g',$ResourceGroup,'-p',$Plan,'-n',$App,'--runtime','dotnet:10')
}
Invoke-Az @('webapp','update','-g',$ResourceGroup,'-n',$App,'--https-only','true')
Invoke-Az @('webapp','config','set','-g',$ResourceGroup,'-n',$App,'--web-sockets-enabled','true','--always-on','true','--min-tls-version','1.2')
$principal = Invoke-Az @('webapp','identity','assign','-g',$ResourceGroup,'-n',$App,'--query','principalId','-o','tsv')

Write-Host "== Azure SQL database" -ForegroundColor Green
$db = az sql db show -g $ResourceGroup -s $SqlServer -n $Database --query name -o tsv 2>$null
if (-not $db) {
  Invoke-Az @('sql','db','create','-g',$ResourceGroup,'-s',$SqlServer,'-n',$Database,'--edition','GeneralPurpose','--compute-model','Serverless',
              '--family','Gen5','--capacity','1','--auto-pause-delay','60','--backup-storage-redundancy','Local')
}

Write-Host "== Data Protection key ring" -ForegroundColor Green
Invoke-Az @('storage','container','create','--account-name',$StorageAccount,'-n','gadfly-dp-keys','--auth-mode','login')
$keyId = az keyvault key show --vault-name $KeyVault -n gadfly-dp-protect --query key.kid -o tsv 2>$null
if (-not $keyId) { $keyId = Invoke-Az @('keyvault','key','create','--vault-name',$KeyVault,'-n','gadfly-dp-protect','--kty','RSA','--size','2048','--query','key.kid','-o','tsv') }
$storageId = Invoke-Az @('storage','account','show','-n',$StorageAccount,'--query','id','-o','tsv')
$vaultId = Invoke-Az @('keyvault','show','-n',$KeyVault,'--query','id','-o','tsv')
Invoke-Az @('role','assignment','create','--assignee-object-id',$principal,'--assignee-principal-type','ServicePrincipal',
            '--role','Storage Blob Data Contributor','--scope',"$storageId/blobServices/default/containers/gadfly-dp-keys")
Invoke-Az @('role','assignment','create','--assignee-object-id',$principal,'--assignee-principal-type','ServicePrincipal',
            '--role','Key Vault Crypto User','--scope',$vaultId)
Invoke-Az @('role','assignment','create','--assignee-object-id',$principal,'--assignee-principal-type','ServicePrincipal',
            '--role','Key Vault Secrets User','--scope',$vaultId)

Write-Host "== App settings" -ForegroundColor Green
$kvRef = { param($name) "@Microsoft.KeyVault(VaultName=$KeyVault;SecretName=$name)" }
$settings = @(
  "ConnectionStrings__Gadfly=Server=tcp:$SqlServer.database.windows.net,1433;Database=$Database;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=120;MultipleActiveResultSets=True;",
  "DataProtection__BlobUri=https://$StorageAccount.blob.core.windows.net/gadfly-dp-keys/keys.xml",
  "DataProtection__KeyVaultKeyId=$keyId",
  # Auth secrets (MindAttic.Authentication reads MindAttic:Vault:Security:<name>, punctuation-insensitive):
  # pepper.v1 (32+ random bytes, base64), reset-token-key (32+ random bytes, base64), bootstrap-token (first admin password).
  "MindAttic__Vault__Security__pepper-v1=$(& $kvRef 'gadfly-pepper-v1')",
  "MindAttic__Vault__Security__reset-token-key=$(& $kvRef 'gadfly-reset-token-key')",
  "MindAttic__Vault__Security__bootstrap-token=$(& $kvRef 'gadfly-bootstrap-token')",
  "MindAttic__Vault__LLM__claude__apiKey=$(& $kvRef 'llm-claude')",
  "MindAttic__Vault__LLM__openai__apiKey=$(& $kvRef 'llm-openai')",
  "MindAttic__Vault__LLM__gemini__apiKey=$(& $kvRef 'llm-gemini')",
  "MindAttic__Vault__LLM__deepseek__apiKey=$(& $kvRef 'llm-deepseek')",
  "MindAttic__Vault__LLM__elevenlabs__apiKey=$(& $kvRef 'elevenlabs')",
  "ASPNETCORE_ENVIRONMENT=Production"
)
Invoke-Az (@('webapp','config','appsettings','set','-g',$ResourceGroup,'-n',$App,'--settings') + $settings)

Write-Host ""
Write-Host "== Still to do by hand" -ForegroundColor Yellow
Write-Host "1. Put these secrets in Key Vault $KeyVault (az keyvault secret set --vault-name $KeyVault -n <name> --value <value>):"
Write-Host "   gadfly-pepper-v1 and gadfly-reset-token-key (each: [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))),"
Write-Host "   gadfly-bootstrap-token (the first admin's initial password, 12+ chars), llm-claude, llm-openai, llm-gemini, llm-deepseek, elevenlabs"
Write-Host "2. In the Gadfly database (Azure portal Query editor, signed in as the server's Entra admin), run:"
Write-Host "   CREATE USER [$App] FROM EXTERNAL PROVIDER; ALTER ROLE db_datareader ADD MEMBER [$App]; ALTER ROLE db_datawriter ADD MEMBER [$App];"
Write-Host "   CREATE USER [gh-mindattic-webapps-deploy] FROM EXTERNAL PROVIDER; ALTER ROLE db_ddladmin ADD MEMBER [gh-mindattic-webapps-deploy];"
Write-Host "   ALTER ROLE db_datareader ADD MEMBER [gh-mindattic-webapps-deploy]; ALTER ROLE db_datawriter ADD MEMBER [gh-mindattic-webapps-deploy];"
Write-Host "3. Let the deploy app registration deploy here: Website Contributor on $App, and a federated credential for repo:mindattic/Gadfly:ref:refs/heads/main."
Write-Host "4. GitHub repo secrets on mindattic/Gadfly: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID."
Write-Host "5. Run the 'Build and deploy Gadfly to Azure App Service' workflow (Actions tab, Run workflow)."
