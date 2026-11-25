# Sets up the Blender + MPFB pipeline tools/examiner-m/build.py and tools/examiner-socrates/build.py need (GAD-T729):
# a portable Blender 4.4.3, the MPFB 2.0.18 extension, and the CC0 asset packs (makehuman_system_assets, faceunits01,
# visemes02, bodyparts05), all under one durable, non-git-tracked folder so a wiped temp scratchpad doesn't lose the
# setup again (it did once already; this script exists so the next time is a five-minute re-run, not a re-discovery).
#   powershell -File setup-blender-mpfb.ps1 [-Dest D:\Tools\blender-mpfb]
# Idempotent: re-running skips any step whose target already exists. When it finishes:
#   <Dest>\blender-4.4.3-windows-x64\blender.exe
# is a fully portable install (a "portable" folder next to the exe keeps prefs/extensions off the C: drive) with MPFB
# enabled and saved into its user prefs, so `blender.exe -b --python build.py -- raw.glb` just works, exactly as
# build.py's own header describes, with no extra --python-expr setup step.
param(
    [string]$Dest = "D:\Tools\blender-mpfb"
)
$ErrorActionPreference = "Stop"

$blenderDir = Join-Path $Dest "blender-4.4.3-windows-x64"
$blenderExe = Join-Path $blenderDir "blender.exe"
$portableDir = Join-Path $blenderDir "portable"
$mpfbDir = Join-Path $portableDir "extensions\user_default\mpfb"
$userDataDir = Join-Path $portableDir "extensions\.user\user_default\mpfb\data"
$downloads = Join-Path $Dest "downloads"
New-Item -ItemType Directory -Force -Path $Dest, $downloads | Out-Null

function Get-IfMissing([string]$Url, [string]$OutFile, [string]$Sha256) {
    if (Test-Path $OutFile) { return }
    Write-Host "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
    if ($Sha256) {
        $actual = (Get-FileHash $OutFile -Algorithm SHA256).Hash.ToLower()
        if ($actual -ne $Sha256.ToLower()) {
            Remove-Item $OutFile
            throw "Checksum mismatch for $OutFile (expected $Sha256, got $actual)"
        }
    }
}

# 1. Blender 4.4.3 portable (official zip release, Blender 4.2+ is what MPFB 2.0.18 needs).
if (-not (Test-Path $blenderExe)) {
    $blenderZip = Join-Path $downloads "blender-4.4.3-windows-x64.zip"
    Get-IfMissing "https://download.blender.org/release/Blender4.4/blender-4.4.3-windows-x64.zip" $blenderZip `
        "60a9703b07f2cf42509f699ccdec4f5ede71c1932f6c14329f0e14c023e27d5c"
    Write-Host "Extracting Blender to $Dest"
    Expand-Archive -Path $blenderZip -DestinationPath $Dest -Force
}

# 2. Portable mode: an empty "portable" folder next to blender.exe (NOT "config" - that name does nothing).
New-Item -ItemType Directory -Force -Path $portableDir | Out-Null

# 3. MPFB 2.0.18 extension, dropped straight into the User Default repo's directory (no "4.4" version segment -
#    that lives one level up, in portable\extensions\.user\... - a sibling, not a parent, of extensions\user_default).
if (-not (Test-Path (Join-Path $mpfbDir "blender_manifest.toml"))) {
    $mpfbZip = Join-Path $downloads "add-on-mpfb-v2.0.18.zip"
    Get-IfMissing ("https://extensions.blender.org/download/sha256:c075b3e9b94b8e5556a5f03f4653602d02ea306de08126e8f55ed6dee28a2dad/add-on-mpfb-v2.0.18.zip?repository=%2Fapi%2Fv1%2Fextensions%2F&blender_version_min=4.2.0") `
        $mpfbZip "c075b3e9b94b8e5556a5f03f4653602d02ea306de08126e8f55ed6dee28a2dad"
    New-Item -ItemType Directory -Force -Path $mpfbDir | Out-Null
    Expand-Archive -Path $mpfbZip -DestinationPath $mpfbDir -Force
}

# 4. Enable MPFB and save it into the portable install's own user prefs, so a plain `blender -b --python build.py`
#    (no enable step of its own) can import bl_ext.user_default.mpfb straight away, same as on the original machine.
if (-not (Test-Path (Join-Path $portableDir "config\userpref.blend"))) {
    $enableScript = Join-Path $downloads "_enable_mpfb.py"
    Set-Content -Path $enableScript -Encoding utf8 -Value @'
import bpy, addon_utils
addon_utils.extensions_refresh(ensure_wheels=False)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True, persistent=True)
bpy.ops.wm.save_userpref()
'@
    & $blenderExe -b --python-exit-code 1 --python $enableScript
    if ($LASTEXITCODE -ne 0) { throw "Blender failed enabling MPFB (exit $LASTEXITCODE)" }
}

# 5. The CC0 asset packs MPFB needs, straight from MakeHuman Community's file server (each zip has a "packs/" folder
#    at its root and is meant to unpack directly into MPFB's user data dir - the same layout the "load pack from zip
#    file" button in MPFB's library settings panel produces).
$packs = @(
    @{ Name = "makehuman_system_assets_cc0.zip"; Url = "https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip"; Marker = "skins" }
    @{ Name = "faceunits01.zip"; Url = "https://files.makehumancommunity.org/functional/faceunits01.zip"; Marker = "targets\faceunits" }
    @{ Name = "visemes02.zip"; Url = "https://files.makehumancommunity.org/functional/visemes02.zip"; Marker = "targets\visemes" }
    @{ Name = "bodyparts05_cc0.zip"; Url = "https://files.makehumancommunity.org/asset_packs/bodyparts05/bodyparts05_cc0.zip"; Marker = "clothes\rehmanpolanski_beard_viking" }
)
New-Item -ItemType Directory -Force -Path $userDataDir | Out-Null
foreach ($pack in $packs) {
    if (Test-Path (Join-Path $userDataDir $pack.Marker)) { continue }
    $zip = Join-Path $downloads $pack.Name
    Get-IfMissing $pack.Url $zip $null
    Expand-Archive -Path $zip -DestinationPath $userDataDir -Force
}

# 6. Smoke test: MPFB can build a basemesh from the installed system assets (not a full character - see GAD-T730).
$smokeScript = Join-Path $downloads "_smoke_test.py"
Set-Content -Path $smokeScript -Encoding utf8 -Value @'
import bpy
from bl_ext.user_default.mpfb.services.humanservice import HumanService
info = HumanService._create_default_human_info_dict()
settings = HumanService.get_default_deserialization_settings()
settings["subdiv_levels"] = 0
basemesh = HumanService.deserialize_from_dict(info, settings)
print("SMOKE TEST OK:", basemesh.name, len(basemesh.data.vertices), "vertices")
'@
& $blenderExe -b --python-exit-code 1 --python $smokeScript
if ($LASTEXITCODE -ne 0) { throw "MPFB smoke test failed (exit $LASTEXITCODE)" }

Remove-Item -Recurse -Force $downloads
Write-Host "Blender: $blenderExe"
Write-Host "MPFB:    $mpfbDir"
Write-Host "Assets:  $userDataDir"
