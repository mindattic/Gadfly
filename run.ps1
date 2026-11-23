<#
.SYNOPSIS
  Runs Gadfly.Server locally in mock mode against LocalDB (Development). No LLM provider is called.

.DESCRIPTION
  Default: starts the server on https://localhost:7311 (database Gadfly on (localdb)\MSSQLLocalDB, migrated on start).
  -SeedDemo       seeds Gadfly Demo Academy (demo.owner, demo.teacher, ... demo.student), then serves.
  -SeedClassroom  seeds the classroom (Classroom_Owner, Teacher_A..C, Student_A..Z), then serves.
  -Worker         runs Gadfly.Worker instead (the job queue, grading and sweepers, docs/DEPLOY.md "The worker"), against
                  the same LocalDB. Start it beside a server running with Gadfly__Jobs__RunInWeb=false to try the split.
  -Tour           runs the demo tour (docs/TOUR.md): its own server and database GadflyTour, mock panel. Exits with
                  the tour's exit code and prints its "Tour: N passed, ..." line.
  Seeders use -Password, else Gadfly:Demo:Password from the environment, else generate one and print it in the log.
  Windows PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [switch]$SeedDemo,
    [switch]$SeedClassroom,
    [switch]$Tour,
    [switch]$Worker,
    [string]$Password
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Gadfly__AiMode = 'mock'     # never a live provider from this script
if ($Password) { $env:Gadfly__Demo__Password = $Password }

if ($Tour) {
    dotnet build Gadfly.Server\Gadfly.Server.csproj --nologo -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet build Gadfly.Tour\Gadfly.Tour.csproj --nologo -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $tourArgs = @('--fresh')
    dotnet run --no-build --project Gadfly.Tour -- @tourArgs
    exit $LASTEXITCODE
}

if ($Worker) {
    Write-Host 'Gadfly.Worker: mock mode, Development (Ctrl+C stops it)' -ForegroundColor Cyan
    dotnet run --project Gadfly.Worker
    exit $LASTEXITCODE
}

$serverArgs = @()
# Seed flags must come last (Program.cs); the classroom seeder serves afterwards unless --then-exit is added.
if ($SeedDemo) { $serverArgs += '--seed-demo' }
if ($SeedClassroom) { $serverArgs += '--seed-classroom' }
Write-Host 'Gadfly.Server: mock mode, Development, https://localhost:7311 (Ctrl+C stops it)' -ForegroundColor Cyan
dotnet run --project Gadfly.Server -- @serverArgs
exit $LASTEXITCODE
