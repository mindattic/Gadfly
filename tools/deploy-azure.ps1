<#
.SYNOPSIS
  Deploys Gadfly.Server to Azure App Service (mindattic-gadfly) the safe way (GAD-T222, docs/DEPLOY.md), straight to
  production, to the staging slot, or by promoting what staging runs into production with a slot swap (GAD-T336).

.DESCRIPTION
  1. Exports the committed ref (default HEAD) with `git archive` into a clean scratch folder. The shared working
     tree often holds other people's uncommitted edits; nothing is ever published from it.
  2. dotnet restore, then `dotnet ef database update` against Azure SQL (Entra auth as the signed-in az user).
     If this fails the script stops: new code is never deployed onto an old database.
  3. dotnet publish (Release), with Gadfly.Worker published inside it as the continuous WebJob
     App_Data/jobs/continuous/GadflyWorker, zip, `az webapp deploy` (zip, async), wait for Kudu to report the deployment.
  4. Poll /healthz until 200, then smoke check that /login is 200.
  5. Tag the deployed commit vN and push the tag. N is the highest vN tag (on origin or here) plus one, baked into
     the build as /p:GadflyVersion=N so the app shows "VN" (GAD-T291). Only a successful deploy is tagged.

  Runs on Windows PowerShell 5.1 and PowerShell 7. It reads the app's AiMode setting to refuse a deploy onto a
  live-mode app by accident, but never changes an app setting, secret or Azure resource. Never calls an LLM.

  Three targets (docs/DEPLOY.md "Staging and production slots"):
    (default)       build Ref, migrate the production database, deploy to production, tag vN. As before GAD-T336.
    -Slot staging   build Ref as the next V<N>, migrate the staging database (Gadfly-staging), deploy to the staging
                    slot, check its /healthz and /login. Tags nothing: staging uses up no number.
    -Promote        build nothing. Reads "V<N> <commit>" from the staging slot's /healthz/version, exports that commit,
                    migrates the production database with its migrations (stops if that fails), swaps staging into
                    production, checks production's /healthz, /login and that it now reports that commit, then tags vN.
                    The swap carries the exact build staging ran; connection strings and AiMode stay with their slot.

  MindAttic.Deploy runs this as the preDeploy hook of its `gadfly` app (`npm run deploy -- --app gadfly`); under
  --dry-run it passes -DryRun.

.PARAMETER Ref          Git ref to deploy (default HEAD).
.PARAMETER Slot         production (default) or staging: which slot a build is deployed to.
.PARAMETER Promote      Swap the build the staging slot runs into production (migrating production first). Ignores -Ref.
.PARAMETER DryRun       Export, restore, list migrations, publish and zip; migrate, deploy, swap and tag nothing.
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
    [string]$StagingSlot = 'staging',
    [string]$StagingDatabase = 'Gadfly-staging',
    [ValidateSet('production', 'staging')][string]$Slot = 'production',
    [switch]$Promote,
    [int]$HealthTimeoutSeconds = 600,
    [switch]$DryRun,
    [switch]$AllowUnpushed,
    [switch]$AllowLive,
    [switch]$KeepWork
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = 'Gadfly.Server/Gadfly.Server.csproj'
# The grading worker (GAD-T700), published into the site as the continuous WebJob GadflyWorker (docs/DEPLOY.md "The worker").
$workerProject = 'Gadfly.Worker/Gadfly.Worker.csproj'
if ($Promote -and $Slot -ne 'production') { throw 'deploy-azure: -Promote swaps staging into production; do not pass -Slot staging with it.' }
$toStaging = ($Slot -eq 'staging')
# The slot this run deploys to or swaps into, and the database that slot's code runs on.
$targetName = if ($toStaging) { "$App/$StagingSlot" } else { $App }
$slotArgs = if ($toStaging) { @('--slot', $StagingSlot) } else { @() }
$targetDatabase = if ($toStaging) { $StagingDatabase } else { $Database }
$connection = "Server=tcp:$SqlServer,1433;Database=$targetDatabase;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=180;"

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

# A slot's address, asked of Azure (a slot's host name is not always <app>-<slot>), with the usual name as the fallback
# for a dry run that has not signed in.
function Get-BaseUrl([string[]]$SlotArgs) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $h = & az webapp show -g $ResourceGroup -n $App @SlotArgs --query defaultHostName -o tsv 2>$null } finally { $ErrorActionPreference = $old }
    if ($LASTEXITCODE -eq 0 -and $h) { return "https://$(([string]$h).Trim())" }
    if ($SlotArgs.Count -gt 0) { return "https://$App-$($SlotArgs[1]).azurewebsites.net" }
    return "https://$App.azurewebsites.net"
}

# What a deployed build says it is: "V<N> <commit>" from /healthz/version (GAD-T336), or $null.
function Get-BuildIdentity([string]$Url) {
    try { $text = [string](Invoke-WebRequest -Uri "$Url/healthz/version" -UseBasicParsing -TimeoutSec 30).Content } catch { return $null }
    if ($text.Trim() -match '^V([1-9][0-9]*) ([0-9a-f]{40})$') { return @{ Version = [int]$Matches[1]; Commit = $Matches[2] } }
    return $null
}

$baseUrl = Get-BaseUrl $slotArgs
if ($Promote) {
    $stagingUrl = Get-BaseUrl @('--slot', $StagingSlot)
    Say "Reading the build on the staging slot ($stagingUrl/healthz/version)"
    $staged = Get-BuildIdentity $stagingUrl
    if (-not $staged) { Fail "the staging slot did not report a deployed build at $stagingUrl/healthz/version; deploy to it first (-Slot staging)." }
    $Ref = $staged.Commit
    Write-Host "   staging runs V$($staged.Version) $($staged.Commit.Substring(0,7))"
}

# --- 0. what are we deploying -------------------------------------------------------------------------------
Say "Resolving $Ref in $repo"
$sha = ([string](& git -C $repo rev-parse --verify "$Ref^{commit}")).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sha) { Fail "cannot resolve '$Ref'" }
& git -C $repo fetch origin main --quiet
& git -C $repo merge-base --is-ancestor $sha origin/main
if ($LASTEXITCODE -ne 0) {
    if ($AllowUnpushed) { Write-Warning "$sha is not on origin/main (-AllowUnpushed)." }
    else { Fail "$sha is not on origin/main; push it first (or pass -AllowUnpushed)." }
}
$subject = (& git -C $repo log -1 --format=%s $sha).Trim()
Write-Host "   $($sha.Substring(0,7))  $subject"

# --- version: every deploy is the next whole number after the highest vN tag (GAD-T291, docs/DEPLOY.md "Versions") ---
# The tags on origin are the record; local ones count too so an unpushed tag is never reused.
function Get-VersionTags {
    $names = @()
    $remote = & git -C $repo ls-remote --tags origin 'refs/tags/v*'
    if ($LASTEXITCODE -ne 0) { Fail 'cannot list the tags on origin (git ls-remote)' }
    foreach ($line in @($remote)) { if ($line -match 'refs/tags/(v[0-9]+)$') { $names += $Matches[1] } }
    foreach ($t in @(& git -C $repo tag --list 'v*')) { $names += ([string]$t).Trim() }
    return $names
}
function Get-HighestVersion {
    $max = 0
    foreach ($t in Get-VersionTags) { if ($t -match '^v([1-9][0-9]*)$' -and [int]$Matches[1] -gt $max) { $max = [int]$Matches[1] } }
    return $max
}
$version = (Get-HighestVersion) + 1
if ($Promote) {
    # The staged build has its number baked in; production may have moved past it since (a direct deploy meanwhile).
    if ($staged.Version -lt $version) { Fail "staging runs V$($staged.Version), but V$($version - 1) is already tagged; deploy to staging again (-Slot staging) and promote that." }
    $version = $staged.Version
}
$versionTag = "v$version"
if ($toStaging) { Write-Host "   version: V$version (staging: no tag; -Promote tags it when it reaches production)" }
else { Write-Host "   version: V$version (tag $versionTag after a successful deploy)" }

if (-not $DryRun) {
    $who = & az account show --query user.name -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $who) { Fail "az is not signed in (az login)" }
    Write-Host "   az user: $who"
    # Read-only guard: the slot being deployed or swapped into must be in mock mode unless the caller says otherwise.
    # Only this one setting is looked at. Gadfly__AiMode is a slot setting, so a swap never carries it across.
    $settingsJson = & az webapp config appsettings list -g $ResourceGroup -n $App @slotArgs -o json
    if ($LASTEXITCODE -ne 0) { Fail "cannot read the app settings of $targetName" }
    $mode = '(unset)'
    foreach ($setting in (ConvertFrom-Json -InputObject ($settingsJson -join "`n"))) {
        if ($setting.name -eq 'Gadfly__AiMode') { $mode = [string]$setting.value }
    }
    Write-Host "   Gadfly__AiMode on $targetName = $mode"
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
        Invoke-Native 'dotnet restore (worker)' { dotnet restore $workerProject --nologo -v q }

        if ($DryRun) {
            Say 'Migrations (dry run: listing only)'
            Invoke-Native 'dotnet ef migrations list' { dotnet ef migrations list --project $project --connection $connection }
        } else {
            Say "Applying migrations to $SqlServer/$targetDatabase (before any new code reaches $targetName)"
            Invoke-Native 'dotnet ef database update' { dotnet ef database update --project $project --connection $connection }
            Write-Host '   migrations: database is up to date' -ForegroundColor Green
        }

        # --- 3. publish, zip, deploy (a promotion builds nothing: it swaps the build staging already runs) -----
        if (-not $Promote) {
            Say "dotnet publish (Release, V$version)"
            Invoke-Native 'dotnet publish' { dotnet publish $project -c Release -o $out --nologo --no-restore -v q "-p:GadflyVersion=$version" "-p:GadflyCommit=$sha" }
            $webJob = Join-Path $out 'App_Data/jobs/continuous/GadflyWorker'
            Say 'dotnet publish worker (continuous WebJob GadflyWorker)'
            Invoke-Native 'dotnet publish (worker)' { dotnet publish $workerProject -c Release -o $webJob --nologo --no-restore -v q "-p:GadflyVersion=$version" "-p:GadflyCommit=$sha" }
            Say 'Zipping'
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
            Write-Host ("   {0:N1} MB" -f ((Get-Item $zip).Length / 1MB))
        }
    } finally { Pop-Location }

    if ($DryRun) {
        $what = if ($Promote) { 'exported and restored' } else { 'exported, restored, published and zipped' }
        Say "Dry run complete: $what $($sha.Substring(0,7)) as V$version for $targetName; nothing migrated, deployed, swapped or tagged."
        return
    }

    # Two deploys at once: if another one has claimed this number meanwhile, stop before anything goes live.
    if ((Get-HighestVersion) -ge $version) { Fail "tag $versionTag (or a higher one) appeared while this deploy was building; another deploy took V$version. Run it again." }

    $deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
    if ($Promote) {
        # The staging build must still be the one read above: a staging deploy meanwhile would swap in something else.
        $now = Get-BuildIdentity $stagingUrl
        if (-not $now -or $now.Commit -ne $sha) { Fail "the staging slot changed while production was being migrated; run -Promote again." }
        Say "Swapping $App/$StagingSlot into production (V$version $($sha.Substring(0,7)))"
        # Slot settings (connection string, Data Protection key blob, AiMode) stay put; Azure warms the slot up before it swaps.
        Invoke-Native 'az webapp deployment slot swap' { az webapp deployment slot swap -g $ResourceGroup -n $App --slot $StagingSlot --target-slot production -o none }
    } else {
    $startedUtc = [DateTime]::UtcNow
    Say "az webapp deploy -> $targetName"
    # --async true returns at once; Kudu's own wait sometimes reports a 500 timeout for a deploy that succeeds.
    & az webapp deploy -g $ResourceGroup -n $App @slotArgs --src-path $zip --type zip --async true -o none
    $azExit = $LASTEXITCODE
    if ($azExit -ne 0) { Write-Warning "az webapp deploy exited $LASTEXITCODE; checking the deployment log before deciding." }

    # Kudu deployment status: 4 = success, 3 = failed.
    $deployed = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 10
        $json = & az webapp log deployment list -g $ResourceGroup -n $App @slotArgs -o json
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
    if (-not $deployed) {
        # az itself waited and succeeded (exit 0), but the Kudu log never showed a matching record (clock skew,
        # or the record is not listed): trust az's exit code and let /healthz decide, with a fresh window.
        if ($azExit -ne 0) { Fail "no successful deployment reported within $HealthTimeoutSeconds s" }
        Write-Warning "az webapp deploy succeeded but Kudu listed no matching deployment within $HealthTimeoutSeconds s; continuing to the health check."
        $deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
    }
    }

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
    # The slot must now run this very build (not a swap that kept the old one, nor a deploy that did not land).
    $running = $null
    while ($true) {
        $running = Get-BuildIdentity $baseUrl
        if (($running -and $running.Commit -eq $sha) -or (Get-Date) -ge $deadline) { break }
        Start-Sleep -Seconds 10
    }
    if (-not $running -or $running.Commit -ne $sha -or $running.Version -ne $version) {
        Fail "$baseUrl/healthz/version does not report V$version $($sha.Substring(0,7)); check the deployment."
    }
    Write-Host "   /healthz/version -> V$version $($sha.Substring(0,7))"

    if ($toStaging) {
        Say "Deployed $($sha.Substring(0,7)) as V$version to staging $baseUrl (healthz 200, login 200; not tagged). Promote with -Promote."
        return
    }

    # --- 5. tag the deployed commit vN, only now that it is live (a failed deploy uses up no number) -----------
    Say "Tagging $($sha.Substring(0,7)) $versionTag"
    $how = if ($Promote) { "promoted from $StagingSlot to $App" } else { "deployed to $App" }
    & git -C $repo tag -a $versionTag -m "Gadfly V$version $how" $sha
    if ($LASTEXITCODE -ne 0) { Fail "V$version is live, but tag $versionTag already exists here (another deploy?); check git tag and tag $($sha.Substring(0,7)) by hand." }
    # A plain push of a new tag is refused if origin already has it, so two racing deploys cannot both claim it.
    & git -C $repo push origin "refs/tags/${versionTag}:refs/tags/$versionTag"
    if ($LASTEXITCODE -ne 0) { Fail "V$version is live, but pushing tag $versionTag to origin failed (another deploy took it?); fix the tags by hand." }
    Say "Deployed $($sha.Substring(0,7)) as V$version to $baseUrl (healthz 200, login 200, tagged $versionTag)"
} finally {
    if ($KeepWork) { Write-Host "   scratch kept: $work" }
    else { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
}
