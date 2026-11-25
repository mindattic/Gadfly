<#
.SYNOPSIS
Creates Gadfly's Azure resources next to Tutor's (GAD-T63). Run once, by the owner, after approving the cost.

.DESCRIPTION
Uses the existing MindAttic resource group, App Service plan and Azure SQL server. Creates:
  - App Service  mindattic-gadfly (.NET 10, Windows, WebSockets on, Always On left off (GAD-T768), HTTPS only, system identity)
  - Azure SQL database  Gadfly  (serverless, auto-pause) on the existing server
  - Storage account container  gadfly-dp-keys  and Key Vault key  gadfly-dp-protect  for the Data Protection key ring
  - Key Vault references for the secrets the app reads (auth pepper, reset key, bootstrap token, LLM and ElevenLabs keys); you set the values
  - App settings: ConnectionStrings__Gadfly (Active Directory Default), DataProtection:*, Key Vault references
  - Role assignments: the app's identity on the database (via T-SQL printed below), the blob container and the key,
    and Website Contributor on its own site only, so the idle watchdog can stop it (GAD-T775)
  - Staging slot  mindattic-gadfly/staging (GAD-T651): cloned from production, WebSockets on, Always On off, its own system identity
    with the same blob and Key Vault roles, and its own serverless database Gadfly-staging. ConnectionStrings__Gadfly,
    DataProtection__BlobUri (keys-staging.xml on staging) and Gadfly__AiMode are slot settings on both slots, so a swap
    (tools/deploy-azure.ps1 -Promote) never carries them across. Slots need a Standard or higher plan: checked first.
Nothing here deletes anything. Re-running is safe: existing resources are left as they are.

.PARAMETER WhatIf
Print the commands instead of running them.
#>
[CmdletBinding()]
param(
  [string]$ResourceGroup = 'MyApps',
  [string]$Plan = 'ASP-MyApps-99fe',
  [string]$App = 'mindattic-gadfly',
  [string]$DataResourceGroup = 'rg-mindattic-data',
  [string]$SqlServer = 'sql-mindattic-f65744',
  [string]$Database = 'Gadfly',
  [string]$KeyVault = 'kv-tutor-f65744',
  [string]$StorageAccount = 'stmindatticdbf65744',
  [string]$StagingSlot = 'staging',
  [string]$StagingDatabase = 'Gadfly-staging',
  [string]$CustomDomain = '',
  [switch]$WhatIf
)
$ErrorActionPreference = 'Stop'

# "Does it exist?" checks: a missing resource is an expected answer, not an error (PowerShell 5.1 would
# otherwise stop the script on the native command's stderr).
function Get-AzValue([string[]]$AzArgs) {
  $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try { $v = & az @AzArgs 2>$null; if ($LASTEXITCODE -ne 0) { return $null }; return $v }
  finally { $ErrorActionPreference = $old }
}

function Invoke-Az([string[]]$AzArgs) {
  Write-Host ("az " + ($AzArgs -join ' ')) -ForegroundColor Cyan
  if ($WhatIf) { return $null }
  $out = & az @AzArgs
  if ($LASTEXITCODE -ne 0) { throw "az $($AzArgs[0]) $($AzArgs[1]) failed (exit $LASTEXITCODE)." }
  return $out
}

Write-Host "== App Service" -ForegroundColor Green
$exists = Get-AzValue @('webapp','show','-g',$ResourceGroup,'-n',$App,'--query','name','-o','tsv')
if (-not $exists) {
  Invoke-Az @('webapp','create','-g',$ResourceGroup,'-p',$Plan,'-n',$App,'--runtime','dotnet:10')
}
Invoke-Az @('webapp','update','-g',$ResourceGroup,'-n',$App,'--https-only','true')
Invoke-Az @('webapp','config','set','-g',$ResourceGroup,'-n',$App,'--web-sockets-enabled','true','--always-on','false','--min-tls-version','1.2')
$principal = Invoke-Az @('webapp','identity','assign','-g',$ResourceGroup,'-n',$App,'--query','principalId','-o','tsv')

Write-Host "== Plan tier (deployment slots need Standard or higher)" -ForegroundColor Green
$tier = Get-AzValue @('appservice','plan','show','-g',$ResourceGroup,'-n',$Plan,'--query','sku.tier','-o','tsv')
if ($tier) { $tier = $tier.Trim() }
$slotTiers = @('Standard','Premium','PremiumV2','PremiumV3','PremiumV4','Isolated','IsolatedV2','WorkflowStandard')
if ($WhatIf) { Write-Host "   (WhatIf: plan $Plan tier not checked; slots need Standard or higher)" }
elseif (-not $tier) { throw "Could not read the tier of plan $Plan; cannot confirm it supports deployment slots." }
elseif ($slotTiers -notcontains $tier) { throw "Plan $Plan is tier '$tier', which does not support deployment slots (needs Standard or higher). Scale the plan up (a cost: owner decision), then re-run." }
else { Write-Host "   Plan $Plan is ${tier}: deployment slots are supported." }

Write-Host "== Azure SQL database" -ForegroundColor Green
# Auto-pause after 20 idle minutes (Azure's serverless minimum is 15), matching the site's idle shutdown (GAD-T775).
$dbAutoPauseMinutes = '20'
$db = Get-AzValue @('sql','db','show','-g',$DataResourceGroup,'-s',$SqlServer,'-n',$Database,'--query','name','-o','tsv')
if (-not $db) {
  Invoke-Az @('sql','db','create','-g',$DataResourceGroup,'-s',$SqlServer,'-n',$Database,'--edition','GeneralPurpose','--compute-model','Serverless',
              '--family','Gen5','--capacity','1','--auto-pause-delay',$dbAutoPauseMinutes,'--backup-storage-redundancy','Local')
} else {
  Invoke-Az @('sql','db','update','-g',$DataResourceGroup,'-s',$SqlServer,'-n',$Database,'--auto-pause-delay',$dbAutoPauseMinutes)
}

$stagingDb = Get-AzValue @('sql','db','show','-g',$DataResourceGroup,'-s',$SqlServer,'-n',$StagingDatabase,'--query','name','-o','tsv')
if (-not $stagingDb) {
  Invoke-Az @('sql','db','create','-g',$DataResourceGroup,'-s',$SqlServer,'-n',$StagingDatabase,'--edition','GeneralPurpose','--compute-model','Serverless',
              '--family','Gen5','--capacity','1','--auto-pause-delay','60','--backup-storage-redundancy','Local')
}

Write-Host "== Staging slot" -ForegroundColor Green
$slotExists = Get-AzValue @('webapp','deployment','slot','list','-g',$ResourceGroup,'-n',$App,'--query',"[?name=='$StagingSlot'].name",'-o','tsv')
if (-not $slotExists) {
  Invoke-Az @('webapp','deployment','slot','create','-g',$ResourceGroup,'-n',$App,'--slot',$StagingSlot,'--configuration-source',$App)
}
Invoke-Az @('webapp','config','set','-g',$ResourceGroup,'-n',$App,'--slot',$StagingSlot,'--web-sockets-enabled','true','--always-on','false','--min-tls-version','1.2')
$slotPrincipal = Invoke-Az @('webapp','identity','assign','-g',$ResourceGroup,'-n',$App,'--slot',$StagingSlot,'--query','principalId','-o','tsv')

Write-Host "== Data Protection key ring" -ForegroundColor Green
Invoke-Az @('storage','container','create','--account-name',$StorageAccount,'-n','gadfly-dp-keys','--auth-mode','login')
$keyId = Get-AzValue @('keyvault','key','show','--vault-name',$KeyVault,'-n','gadfly-dp-protect','--query','key.kid','-o','tsv')
if (-not $keyId) { $keyId = Invoke-Az @('keyvault','key','create','--vault-name',$KeyVault,'-n','gadfly-dp-protect','--kty','RSA','--size','2048','--query','key.kid','-o','tsv') }
$storageId = Invoke-Az @('storage','account','show','-n',$StorageAccount,'--query','id','-o','tsv')
$vaultId = Invoke-Az @('keyvault','show','-n',$KeyVault,'--query','id','-o','tsv')
Invoke-Az @('role','assignment','create','--assignee-object-id',$principal,'--assignee-principal-type','ServicePrincipal',
            '--role','Storage Blob Data Contributor','--scope',"$storageId/blobServices/default/containers/gadfly-dp-keys")
Invoke-Az @('role','assignment','create','--assignee-object-id',$principal,'--assignee-principal-type','ServicePrincipal',
            '--role','Key Vault Crypto User','--scope',$vaultId)
Invoke-Az @('role','assignment','create','--assignee-object-id',$principal,'--assignee-principal-type','ServicePrincipal',
            '--role','Key Vault Secrets User','--scope',$vaultId)
# The staging slot's identity gets the same roles (GAD-T651).
Invoke-Az @('role','assignment','create','--assignee-object-id',$slotPrincipal,'--assignee-principal-type','ServicePrincipal',
            '--role','Storage Blob Data Contributor','--scope',"$storageId/blobServices/default/containers/gadfly-dp-keys")
Invoke-Az @('role','assignment','create','--assignee-object-id',$slotPrincipal,'--assignee-principal-type','ServicePrincipal',
            '--role','Key Vault Crypto User','--scope',$vaultId)
Invoke-Az @('role','assignment','create','--assignee-object-id',$slotPrincipal,'--assignee-principal-type','ServicePrincipal',
            '--role','Key Vault Secrets User','--scope',$vaultId)

Write-Host "== Idle shutdown (GAD-T775): each identity may stop its own site, and nothing else" -ForegroundColor Green
# On-demand hosting: /kickstart gadfly starts the stopped site, and the site stops itself through ARM once it has been idle for
# Gadfly__IdleShutdown__Minutes with no pending work (docs/DEPLOY.md "Idle shutdown"). Website Contributor scoped to the site
# (or the slot) alone, keyed off that site's own system identity, so a recreated site (a new identity) is granted afresh on a re-run.
$siteId = Invoke-Az @('webapp','show','-g',$ResourceGroup,'-n',$App,'--query','id','-o','tsv')
Invoke-Az @('role','assignment','create','--assignee-object-id',$principal,'--assignee-principal-type','ServicePrincipal',
            '--role','Website Contributor','--scope',$siteId)
$slotId = Invoke-Az @('webapp','show','-g',$ResourceGroup,'-n',$App,'--slot',$StagingSlot,'--query','id','-o','tsv')
Invoke-Az @('role','assignment','create','--assignee-object-id',$slotPrincipal,'--assignee-principal-type','ServicePrincipal',
            '--role','Website Contributor','--scope',$slotId)

if ($CustomDomain) {
  Write-Host "== Custom domain $CustomDomain (HTTPS-only is on above; free App Service managed certificate, SNI)" -ForegroundColor Green
  $verifyId = Get-AzValue @('webapp','show','-g',$ResourceGroup,'-n',$App,'--query','customDomainVerificationId','-o','tsv')
  $default = Get-AzValue @('webapp','show','-g',$ResourceGroup,'-n',$App,'--query','defaultHostName','-o','tsv')
  Write-Host "DNS first (at the registrar): CNAME $CustomDomain -> $default, and TXT asuid.$CustomDomain = $verifyId (an apex domain uses an A record to the app's inbound IP instead of the CNAME)." -ForegroundColor Yellow
  Invoke-Az @('webapp','config','hostname','add','-g',$ResourceGroup,'--webapp-name',$App,'--hostname',$CustomDomain,'-o','none')
  $thumb = Invoke-Az @('webapp','config','ssl','create','-g',$ResourceGroup,'-n',$App,'--hostname',$CustomDomain,'--query','thumbprint','-o','tsv')
  if ($thumb) { Invoke-Az @('webapp','config','ssl','bind','-g',$ResourceGroup,'-n',$App,'--certificate-thumbprint',$thumb,'--ssl-type','SNI','-o','none') }
}

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
# az is a .cmd wrapper: values with ';' or '(' break on the command line, so the settings go in a JSON file.
# Production keeps whatever AiMode it already runs (GAD-T654): re-running provisioning must never flip a live app back to mock.
# Only a first run, where the setting is absent, starts on the labelled mock panel (no LLM spend until the owner says go live, GAD-T100).
$currentAiMode = Get-AzValue @('webapp','config','appsettings','list','-g',$ResourceGroup,'-n',$App,'--query',"[?name=='Gadfly__AiMode'].value | [0]",'-o','tsv')
if ($currentAiMode) { $currentAiMode = "$currentAiMode".Trim() }
if (-not $currentAiMode) { $currentAiMode = 'mock' }
Write-Host "   Production Gadfly__AiMode stays $currentAiMode"
$settings += @(
  "Gadfly__AiMode=$currentAiMode",
  # When Claude is in the panel, Sonnet 5 (owner, 2026-11-21).
  "Gadfly__ClaudeModel=claude-sonnet-5"
)
# These stay with their slot through a swap (GAD-T651): each slot keeps its own database, key ring blob and AI mode.
$slotSettingNames = @('ConnectionStrings__Gadfly', 'DataProtection__BlobUri', 'Gadfly__AiMode', 'Gadfly__Voice__Mode')
function Set-AppSettings([string[]]$Lines, [string[]]$SlotArgs) {
  $file = Join-Path ([IO.Path]::GetTempPath()) "gadfly-appsettings-$([guid]::NewGuid().ToString('N')).json"
  $Lines | ForEach-Object { $i = $_.IndexOf('='); $n = $_.Substring(0, $i); [pscustomobject]@{ name = $n; value = $_.Substring($i + 1); slotSetting = ($slotSettingNames -contains $n) } } |
    ConvertTo-Json | Set-Content -Path $file -Encoding UTF8
  try { Invoke-Az (@('webapp','config','appsettings','set','-g',$ResourceGroup,'-n',$App) + $SlotArgs + @('--settings',"@$file",'-o','none')) }
  finally { Remove-Item $file -ErrorAction SilentlyContinue }
}
Set-AppSettings $settings @()

# The slot was cloned from production's settings; point its three slot settings at staging. Staging is always forced to mock (no LLM spend from staging).
$stagingSettings = $settings | ForEach-Object {
  if ($_ -like 'ConnectionStrings__Gadfly=*') { $_.Replace("Database=$Database;", "Database=$StagingDatabase;") }
  elseif ($_ -like 'DataProtection__BlobUri=*') { $_.Replace('/keys.xml', '/keys-staging.xml') }
  elseif ($_ -like 'Gadfly__AiMode=*') { 'Gadfly__AiMode=mock' }
  else { $_ }
}
# Staging holds a copy of production's ElevenLabs key, so its voice is mock too (GAD-T734); sticky, so a swap never carries it to production.
$stagingSettings = @($stagingSettings | Where-Object { $_ -notlike 'Gadfly__Voice__Mode=*' }) + 'Gadfly__Voice__Mode=mock'
Set-AppSettings $stagingSettings @('--slot', $StagingSlot)

Write-Host ""
Write-Host "== Still to do by hand" -ForegroundColor Yellow
Write-Host "1. Put these secrets in Key Vault $KeyVault (az keyvault secret set --vault-name $KeyVault -n <name> --value <value>):"
Write-Host "   gadfly-pepper-v1 and gadfly-reset-token-key (each: 32 random bytes, base64 - see New-Secret in docs/DEPLOY.md; RandomNumberGenerator.GetBytes needs PowerShell 7),"
Write-Host "   gadfly-bootstrap-token (the first admin's initial password, 12+ chars), llm-claude, llm-openai, llm-gemini, llm-deepseek, elevenlabs"
Write-Host "2. In the Gadfly database (Azure portal Query editor, signed in as the server's Entra admin), run:"
Write-Host "   CREATE USER [$App] FROM EXTERNAL PROVIDER; ALTER ROLE db_datareader ADD MEMBER [$App]; ALTER ROLE db_datawriter ADD MEMBER [$App];"
Write-Host "   CREATE USER [gh-mindattic-webapps-deploy] FROM EXTERNAL PROVIDER; ALTER ROLE db_ddladmin ADD MEMBER [gh-mindattic-webapps-deploy];"
Write-Host "   ALTER ROLE db_datareader ADD MEMBER [gh-mindattic-webapps-deploy]; ALTER ROLE db_datawriter ADD MEMBER [gh-mindattic-webapps-deploy];"
Write-Host "   Staging slot - in the Gadfly-staging database (the slot identity's name is $App/slots/$StagingSlot), run:"
Write-Host "   CREATE USER [$App/slots/$StagingSlot] FROM EXTERNAL PROVIDER; ALTER ROLE db_datareader ADD MEMBER [$App/slots/$StagingSlot]; ALTER ROLE db_datawriter ADD MEMBER [$App/slots/$StagingSlot];"
Write-Host "   CREATE USER [gh-mindattic-webapps-deploy] FROM EXTERNAL PROVIDER; ALTER ROLE db_ddladmin ADD MEMBER [gh-mindattic-webapps-deploy];"
Write-Host "   ALTER ROLE db_datareader ADD MEMBER [gh-mindattic-webapps-deploy]; ALTER ROLE db_datawriter ADD MEMBER [gh-mindattic-webapps-deploy];"
Write-Host "   (If CREATE USER fails for the slot name, create it from the slot identity's object id: CREATE USER [name] WITH SID = <object id as binary>, TYPE = E.)"
Write-Host "3. Let the deploy app registration deploy here: Website Contributor on $App, and a federated credential for repo:mindattic/Gadfly:ref:refs/heads/main."
Write-Host "4. GitHub repo secrets on mindattic/Gadfly: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID."
Write-Host "5. Run the 'Build and deploy Gadfly to Azure App Service' workflow (Actions tab, Run workflow)."
