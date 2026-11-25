<#
  Copies the browser pieces of the voice exam room (GAD-T53) from the sibling MindAttic repos into
  Gadfly.Server/wwwroot/lib/voice (committed, ~3 MB) so a CI runner needs no sibling checkout or npm.
  Re-run after rebuilding a library's web/ folder (npm run build) or bumping three / TalkingHead / signalr.

  The 36.8 MB default avatar (mpfb.glb, CC0) is NOT committed: Gadfly.Server.csproj copies it into
  wwwroot/assets/avatars at build time when the Avatar repo is next to this one (see MindAtticAvatarRoot),
  and the exam room shows her live presence when it is absent (e.g. on CI). In production host the
  model on blob storage/CDN and set Gadfly:Voice:AvatarModelUrl.
#>
param(
  [string]$Avatar = 'D:\Projects\MindAttic\MindAttic.Avatar',
  [string]$Speech = 'D:\Projects\MindAttic\MindAttic.SpeechRecognition'
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\Gadfly.Server\wwwroot' | Resolve-Path
$dest = Join-Path $root 'lib\voice'
$out = Join-Path $root 'lib\voice.new'
$nm = Join-Path $Avatar 'web\node_modules'
foreach ($p in $nm, (Join-Path $Avatar 'web\dist'), (Join-Path $Speech 'web\dist')) { if (-not (Test-Path $p)) { throw "Not found: $p" } }
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

function Copy-To($from, $to) {
  New-Item -ItemType Directory -Force (Split-Path (Join-Path $out $to)) | Out-Null
  Copy-Item $from (Join-Path $out $to) -Force
}

# three.js (MIT): the core build plus the addons TalkingHead imports.
foreach ($f in 'three.module.js','three.core.js') { Copy-To (Join-Path $nm "three\build\$f") "three\build\$f" }
$addons = 'controls/OrbitControls.js','loaders/GLTFLoader.js','loaders/DRACOLoader.js','loaders/FBXLoader.js',
  'utils/BufferGeometryUtils.js','utils/SkeletonUtils.js','libs/meshopt_decoder.module.js','libs/fflate.module.js',
  'libs/stats.module.js','curves/NURBSCurve.js','curves/NURBSUtils.js','environments/RoomEnvironment.js'
foreach ($f in $addons) { Copy-To (Join-Path $nm "three\examples\jsm\$f") "three\examples\jsm\$f" }
Copy-To (Join-Path $nm 'three\LICENSE') 'three\LICENSE'

# TalkingHead (MIT).
Get-ChildItem (Join-Path $nm '@met4citizen\talkinghead\modules') -File | ForEach-Object { Copy-To $_.FullName "talkinghead\modules\$($_.Name)" }
Copy-To (Join-Path $nm '@met4citizen\talkinghead\LICENSE') 'talkinghead\LICENSE'

# SignalR browser client (MIT) for the two voice hubs.
Copy-To (Join-Path $nm '@microsoft\signalr\dist\browser\signalr.min.js') 'signalr\signalr.min.js'

# MindAttic browser packages (built dist, no source maps).
Get-ChildItem (Join-Path $Avatar 'web\dist') -Filter *.js -File | ForEach-Object { Copy-To $_.FullName "avatar\$($_.Name)" }
Get-ChildItem (Join-Path $Speech 'web\dist') -Filter *.js -File | ForEach-Object { Copy-To $_.FullName "speech\$($_.Name)" }

# The avatar's licence note (small); the GLB itself is copied at build time.
New-Item -ItemType Directory -Force (Join-Path $root 'assets\avatars') | Out-Null
Copy-Item (Join-Path $Avatar 'web\assets\avatars\LICENSE.md') (Join-Path $root 'assets\avatars\LICENSE.md') -Force
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Move-Item $out $dest
$out = $dest
'{0} MB written to {1}' -f [math]::Round((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 2), $out
