# Builds the JMA Studio installer end to end:
#   1. dotnet publish JmaStudio.Service + JmaStudio.Gui, self-contained
#      single-file win-x64 (no separate .NET runtime needed on the
#      target machine -- settled decision #12).
#   2. Stages the default preset/config data + keymap.json as installer
#      payload (NOT a Python migration -- the user explicitly decided
#      the installer ships the current C# presets as-is; PythonPresetMigrator
#      stays in the repo for manual dev-time use only, never invoked here).
#   3. Compiles JmaStudio.iss with Inno Setup's ISCC.exe.
#
# Run from anywhere; paths below are all resolved relative to this
# script's own location.

$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
$windowsDir = Split-Path -Parent $installerDir
$repoRoot = Split-Path -Parent $windowsDir

$publishDir = Join-Path $installerDir "publish"
$servicePublishDir = Join-Path $publishDir "service"
$guiPublishDir = Join-Path $publishDir "gui"
$defaultDataDir = Join-Path $publishDir "defaultdata"

Write-Host "=== Publishing JmaStudio.Service ===" -ForegroundColor Cyan
dotnet publish (Join-Path $windowsDir "src\JmaStudio.Service\JmaStudio.Service.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $servicePublishDir
if ($LASTEXITCODE -ne 0) { throw "Service publish failed." }

Write-Host "=== Publishing JmaStudio.Gui ===" -ForegroundColor Cyan
dotnet publish (Join-Path $windowsDir "src\JmaStudio.Gui\JmaStudio.Gui.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $guiPublishDir
if ($LASTEXITCODE -ne 0) { throw "GUI publish failed." }

Write-Host "=== Staging default data (current C# presets, not a Python migration) ===" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $defaultDataDir | Out-Null
$dataFiles = @(
    "app-config.json", "keyboard-presets.json", "lightbar-presets.json",
    "lightbar-reactive-config.json", "controller-reactive-settings.json"
)
foreach ($f in $dataFiles) {
    $src = Join-Path $windowsDir "data\$f"
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $defaultDataDir $f) -Force
        Write-Host "  staged $f"
    } else {
        Write-Host "  WARNING: $src not found, skipping" -ForegroundColor Yellow
    }
}
Copy-Item (Join-Path $repoRoot "keymap.json") (Join-Path $defaultDataDir "keymap.json") -Force
Write-Host "  staged keymap.json"

Write-Host "=== Compiling installer with Inno Setup ===" -ForegroundColor Cyan
$iscc = "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    throw "ISCC.exe not found at $iscc -- is Inno Setup 6 installed?"
}
& $iscc (Join-Path $installerDir "JmaStudio.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }

Write-Host "=== Done. Setup.exe is in installer\output\ ===" -ForegroundColor Green
