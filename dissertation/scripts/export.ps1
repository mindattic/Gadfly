#Requires -Version 5.1
<#
.SYNOPSIS
    Exports this manuscript through the shared MindAttic.Academic kit and MindAttic.Export library.

.DESCRIPTION
    Thin shim over MindAttic.Academic's Export-Manuscript.ps1 (see its DESIGN.md). Reads export.json
    (title page, sources, output folder, formats) and writes a versioned bundle "<fileBaseName> V<N>.*"
    plus description.txt and keywords.txt into the output folder. The previous bundle is archived into
    Archives\v<N>\ first (archive, never delete). -Publish then copies the new bundle into exports\ and
    the Pages article named in academic.json.

    The kit is found at $env:MINDATTIC_ACADEMIC, else ..\MindAttic.Academic beside this repo, else an
    academic\ folder at this repo's root.

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
if ($env:MINDATTIC_ACADEMIC) { $kits += $env:MINDATTIC_ACADEMIC }
$kits += (Join-Path (Split-Path $gitRoot -Parent) 'MindAttic.Academic')
$kits += (Join-Path $gitRoot 'academic')
$kit = $kits | Where-Object { Test-Path (Join-Path $_ 'scripts\Export-Manuscript.ps1') } | Select-Object -First 1
if (-not $kit) { throw "MindAttic.Academic not found; looked in: $($kits -join '; ')" }

& (Join-Path $kit 'scripts\Export-Manuscript.ps1') -ProjectRoot $project -Formats $Formats -Publish:$Publish
exit $LASTEXITCODE
