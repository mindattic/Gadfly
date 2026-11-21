<#
.SYNOPSIS
  Deploys Gadfly.Server to Azure App Service (mindattic-gadfly) the safe way (GAD-T222, docs/DEPLOY.md).

.DESCRIPTION
  1. Exports the committed ref (default HEAD) with `git archive` into a clean scratch folder. The shared working
     tree often holds other people's uncommitted edits; nothing is ever published from it.
  2. dotnet restore, then `dotnet ef database update` against Azure SQL (Entra auth as the signed-in az user).
     If this fails the script stops: new code is never deployed onto an old database.
  3. dotnet publish (Release), zip, `az webapp deploy` (zip, async), wait for Kudu to report the deployment.
  4. Poll /healthz until 200, then smoke check that /login is 200.

  Runs on Windows PowerShell 5.1 and PowerShell 7. It reads the app's AiMode setting to refuse a deploy onto a
  live-mode app by accident, but never changes an app setting, secret or Azure resource. Never calls an LLM.

  MindAttic.Deploy runs this as the preDeploy hook of its `gadfly` app (`npm run deploy -- --app gadfly`); under
  --dry-run it passes -DryRun.

.PARAMETER Ref          Git ref to deploy (default HEAD).
.PARAMETER DryRun       Export, restore, list migrations, publish and zip; migrate and deploy nothing.
.PARAMETER AllowUnpushed  Deploy a ref that is not on origin/main (normally refused).
.PARAMETER AllowLive    Deploy even though the app's Gadfly__AiMode is not mock.
.PARAMETER KeepWork     Keep the scratch folder afterwards.
#>
[CmdletBinding()]
param(
    [string]$Ref = 'HEAD',
    [string]$ResourceGroup = 'MyApps',
    [string]$App = 'mindattic-gadfly',
    [string]$SqlServer = 'sql-mindattic-f65744.database.windows.net',
    [string]$Database = 'Gadfly',
    [int]$HealthTimeoutSeconds = 600,
    [switch]$DryRun,
    [switch]$AllowUnpushed,
    [switch]$AllowLive,
    [switch]$KeepWork
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$baseUrl = "https://$App.azurewebsites.net"
$project = 'Gadfly.Server/Gadfly.Server.csproj'
$connection = "Server=tcp:$SqlServer,1433;Database=$Database;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=180;"

function Say([string]$m) { Write-Host ("== " + $m) -ForegroundColor Cyan }
function Fail([string]$m) { throw ("deploy-azure: " + $m) }

# Run a native command. Its stderr goes straight to the console (no 2>&1, which turns stderr into errors in 5.1);
# success is the exit code alone.
function Invoke-Native([string]$What, [scriptblock]$Cmd) {
    & $Cmd
    if ($LASTEXITCODE -ne 0) { Fail "$What failed (exit $LASTEXITCODE)" }
}

function Get-Status([string]$Url) {
    try {
        $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 30 -MaximumRedirection 0
        return [int]$r.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return 0
    }
}

foreach ($tool in 'git', 'dotnet', 'az') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { Fail "'$tool' is not on PATH" }
}

# --- 0. what are we deploying -------------------------------------------------------------------------------
Say "Resolving $Ref in $repo"
$sha = (& git -C $repo rev-parse --verify "$Ref^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or -not $sha) { Fail "cannot resolve '$Ref'" }
& git -C $repo fetch origin main --quiet
& git -C $repo merge-base --is-ancestor $sha origin/main
if ($LASTEXITCODE -ne 0) {
    if ($AllowUnpushed) { Write-Warning "$sha is not on origin/main (-AllowUnpushed)." }
    else { Fail "$sha is not on origin/main; push it first (or pass -AllowUnpushed)." }
}
$subject = (& git -C $repo log -1 --format=%s $sha).Trim()
Write-Host "   $($sha.Substring(0,7))  $subject"

if (-not $DryRun) {
    $who = & az account show --query user.name -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $who) { Fail "az is not signed in (az login)" }
    Write-Host "   az user: $who"
    # Read-only guard: the app must be in mock mode unless the caller says otherwise. Only this one setting is looked at.
    $settingsJson = & az webapp config appsettings list -g $ResourceGroup -n $App -o json
    if ($LASTEXITCODE -ne 0) { Fail "cannot read the app settings of $App" }
    $mode = '(unset)'
    foreach ($setting in (ConvertFrom-Json -InputObject ($settingsJson -join "`n"))) {
        if ($setting.name -eq 'Gadfly__AiMode') { $mode = [string]$setting.value }
    }
    Write-Host "   Gadfly__AiMode on $App = $mode"
    if ($mode -ne 'mock' -and -not $AllowLive) { Fail "Gadfly__AiMode is '$mode', not 'mock'; pass -AllowLive to deploy anyway." }
}

# --- 1. clean export ----------------------------------------------------------------------------------------
$work = Join-Path ([IO.Path]::GetTempPath()) ("gadfly-deploy-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$src = Join-Path $work 'src'
$out = Join-Path $work 'publish'
$zip = Join-Path $work 'gadfly.zip'
New-Item -ItemType Directory -Path $src, $out | Out-Null
try {
    Say "Exporting $($sha.Substring(0,7)) with git archive to $src"
    $archive = Join-Path $work 'export.zip'
    Invoke-Native 'git archive' { git -C $repo archive --format=zip -o $archive $sha }
    Expand-Archive -Path $archive -DestinationPath $src -Force
    Remove-Item $archive

    Push-Location $src
    try {
        # --- 2. restore, then migrate; a failure here stops everything ---------------------------------------
        Say 'dotnet restore'
        Invoke-Native 'dotnet restore' { dotnet restore $project --nologo -v q }

        if ($DryRun) {
            Say 'Migrations (dry run: listing only)'
            Invoke-Native 'dotnet ef migrations list' { dotnet ef migrations list --project $project --connection $connection }
        } else {
            Say "Applying migrations to $SqlServer/$Database (before any new code is deployed)"
            Invoke-Native 'dotnet ef database update' { dotnet ef database update --project $project --connection $connection }
            Write-Host '   migrations: database is up to date' -ForegroundColor Green
        }

        # --- 3. publish, zip, deploy -------------------------------------------------------------------------
        Say 'dotnet publish (Release)'
        Invoke-Native 'dotnet publish' { dotnet publish $project -c Release -o $out --nologo --no-restore -v q }
        Say 'Zipping'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
        Write-Host ("   {0:N1} MB" -f ((Get-Item $zip).Length / 1MB))
    } finally { Pop-Location }

    if ($DryRun) {
        Say "Dry run complete: exported, restored, published and zipped $($sha.Substring(0,7)); nothing migrated or deployed."
        return
    }

    $startedUtc = [DateTime]::UtcNow
    Say "az webapp deploy -> $App"
    # --async true returns at once; Kudu's own wait sometimes reports a 500 timeout for a deploy that succeeds.
    & az webapp deploy -g $ResourceGroup -n $App --src-path $zip --type zip --async true -o none
    if ($LASTEXITCODE -ne 0) { Write-Warning "az webapp deploy exited $LASTEXITCODE; checking the deployment log before deciding." }

    # Kudu deployment status: 4 = success, 3 = failed.
    $deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
    $deployed = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 10
        $json = & az webapp log deployment list -g $ResourceGroup -n $App -o json
        if ($LASTEXITCODE -ne 0 -or -not $json) { continue }
        # foreach (not a pipeline): Windows PowerShell 5.1 emits a parsed JSON array as one object.
        $latest = $null
        foreach ($d in (ConvertFrom-Json -InputObject ($json -join "`n"))) {
            $received = ([DateTimeOffset]$d.received_time).UtcDateTime
            if ($received -ge $startedUtc.AddMinutes(-1) -and (-not $latest -or $received -gt $latestAt)) { $latest = $d; $latestAt = $received }
        }
        if (-not $latest) { continue }
        Write-Host "   deployment $($latest.id.Substring(0,8)) status $($latest.status)"
        if ($latest.status -eq 4) { $deployed = $true; break }
        if ($latest.status -eq 3) { Fail "Kudu reports the deployment failed (az webapp log deployment show -g $ResourceGroup -n $App --deployment-id $($latest.id))" }
    }
    if (-not $deployed) { Fail "no successful deployment reported within $HealthTimeoutSeconds s" }

    # --- 4. health, then smoke ---------------------------------------------------------------------------
    Say "Waiting for $baseUrl/healthz"
    $code = 0
    while ((Get-Date) -lt $deadline) {
        $code = Get-Status "$baseUrl/healthz"
        if ($code -eq 200) { break }
        Start-Sleep -Seconds 10
    }
    Write-Host "   /healthz -> $code"
    if ($code -ne 200) { Fail "/healthz did not reach 200 (last $code)" }
    # /healthz can answer before the new instance serves pages (a 503 for a few seconds), so retry /login too.
    $login = 0
    while ($true) {
        $login = Get-Status "$baseUrl/login"
        if ($login -eq 200 -or (Get-Date) -ge $deadline) { break }
        Start-Sleep -Seconds 10
    }
    Write-Host "   /login   -> $login"
    if ($login -ne 200) { Fail "smoke check failed: /login is $login" }
    Say "Deployed $($sha.Substring(0,7)) to $baseUrl (healthz 200, login 200)"
} finally {
    if ($KeepWork) { Write-Host "   scratch kept: $work" }
    else { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
}
