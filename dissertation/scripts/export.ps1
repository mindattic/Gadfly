#Requires -Version 5.1
<#
.SYNOPSIS
    Exports this manuscript through the Provenance toolkit and the MindAttic.Export library.

.DESCRIPTION
    Thin shim over Provenance's Export-Manuscript.ps1 (see its DESIGN.md). Reads export.json
    (title page, sources, output folder, formats) and writes a versioned bundle "<fileBaseName> V<N>.*"
    plus description.txt and keywords.txt into the output folder. The previous bundle is archived into
    Archives\v<N>\ first (archive, never delete). -Publish then copies the new bundle into exports\ and
    the Pages article named in academic.json.

    The toolkit is found at $env:PROVENANCE_HOME, else ..\Provenance beside this repo's git root, else
    the legacy $env:MINDATTIC_ACADEMIC. Set PROVENANCE_DRYRUN=1 to print the folder found and exit
    without exporting.

.EXAMPLE
    powershell -File scripts\export.ps1
    powershell -File scripts\export.ps1 -Formats pdf,docx
    powershell -File scripts\export.ps1 -Publish
#>
param([string]$Formats = '', [switch]$Publish)

$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$gitRoot = $project
while ($gitRoot -and -not (Test-Path (Join-Path $gitRoot '.git'))) {
    $parent = Split-Path $gitRoot -Parent
    if (-not $parent -or $parent -eq $gitRoot) { $gitRoot = $project; break }
    $gitRoot = $parent
}
$kits = @()
if ($env:PROVENANCE_HOME) { $kits += $env:PROVENANCE_HOME }
$kits += (Join-Path (Split-Path $gitRoot -Parent) 'Provenance')
if ($env:MINDATTIC_ACADEMIC) { $kits += $env:MINDATTIC_ACADEMIC }
$kit = $kits | Where-Object { Test-Path (Join-Path $_ 'scripts\Export-Manuscript.ps1') } | Select-Object -First 1
if (-not $kit) { throw "Provenance not found; looked in: $($kits -join '; ')" }
$kit = (Resolve-Path -LiteralPath $kit).Path

if ($env:PROVENANCE_DRYRUN) {
    Write-Host "Provenance: $kit"
    exit 0
}

& (Join-Path $kit 'scripts\Export-Manuscript.ps1') -ProjectRoot $project -Formats $Formats -Publish:$Publish
exit $LASTEXITCODE
