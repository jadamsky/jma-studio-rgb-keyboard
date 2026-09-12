# Builds the JMA Studio installer end to end:
#   1. dotnet publish JmaStudio.Service + JmaStudio.Gui, self-contained
#      single-file win-x64 (no separate .NET runtime needed on the
#      target machine -- settled decision #12).
#   2. Code-signs both published .exe files, and later the compiled
#      Setup.exe itself, with a self-signed "Jake Adamsky" certificate
#      (see this repo's own cert-creation note below) -- purely a local
#      convenience so UAC shows a real name instead of "Unknown
#      Publisher" ON THIS MACHINE specifically. This certificate is
#      NOT from a trusted CA -- it was created once via New-
#      SelfSignedCertificate and manually trusted into THIS machine's
#      LocalMachine\Root + LocalMachine\TrustedPublisher stores. On any
#      other machine (including a real end user downloading this from
#      GitHub), the signature is present but untrusted, so Windows
#      falls back to showing "Unknown Publisher" there exactly as
#      before -- this does not make the installer trusted for anyone
#      but the certificate's own machine. Signing is best-effort: if
#      the certificate isn't present (e.g. building on a different
#      machine), both exes and the final installer are left unsigned
#      with a warning, same as today.
#   3. Stages the default preset/config data + keymap.json as installer
#      payload (NOT a Python migration -- the user explicitly decided
#      the installer ships the current C# presets as-is; PythonPresetMigrator
#      stays in the repo for manual dev-time use only, never invoked here).
#   4. Compiles JmaStudio.iss with Inno Setup's ISCC.exe, which also
#      signs the resulting Setup.exe itself (via the SignTool directive
#      in [Setup] + the /S command-line arg passed to ISCC below).
#
# Run from anywhere; paths below are all resolved relative to this
# script's own location.

$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
$windowsDir = Split-Path -Parent $installerDir
$repoRoot = Split-Path -Parent $windowsDir

# ---- code signing (best-effort, local-machine-only trust -- see header) ----
$certSubject = "CN=Jake Adamsky"
$signtoolCandidates = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending
$signtoolPath = if ($signtoolCandidates) { $signtoolCandidates[0].FullName } else { $null }
$signingCert = Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1

$canSign = $signtoolPath -and $signingCert
if ($canSign) {
    Write-Host "Code signing available: $signtoolPath, cert thumbprint $($signingCert.Thumbprint)" -ForegroundColor Cyan
} else {
    Write-Host "Code signing NOT available (signtool: $([bool]$signtoolPath), cert: $([bool]$signingCert)) -- exes/installer will be unsigned." -ForegroundColor Yellow
}

function Invoke-SignIfPossible([string]$FilePath) {
    if (-not $canSign) { return }
    & $signtoolPath sign /n "Jake Adamsky" /fd SHA256 /s My /sm $FilePath
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  WARNING: signing failed for $FilePath (signtool exit $LASTEXITCODE) -- continuing unsigned." -ForegroundColor Yellow
    } else {
        Write-Host "  signed: $FilePath"
    }
}

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
Invoke-SignIfPossible (Join-Path $servicePublishDir "JmaStudio.Service.exe")

Write-Host "=== Publishing JmaStudio.Gui ===" -ForegroundColor Cyan
dotnet publish (Join-Path $windowsDir "src\JmaStudio.Gui\JmaStudio.Gui.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $guiPublishDir
if ($LASTEXITCODE -ne 0) { throw "GUI publish failed." }
Invoke-SignIfPossible (Join-Path $guiPublishDir "JmaStudio.Gui.exe")

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

$isccArgs = @((Join-Path $installerDir "JmaStudio.iss"))
if ($canSign) {
    # JmaStudio.iss only references SignTool=jmasign inside an
    # #ifdef SignInstaller block -- so both the define AND this /S sign-
    # tool definition are passed together, only when signing is actually
    # available. $q is Inno's own escape for a literal double-quote
    # inside a /S command string; $f is the file ISCC will substitute in.
    $isccArgs = @("/DSignInstaller=1", "/Sjmasign=`$q$signtoolPath`$q sign /n `$qJake Adamsky`$q /fd SHA256 /s My /sm `$f") + $isccArgs
}
& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }

Write-Host "=== Done. Setup.exe is in installer\output\ ===" -ForegroundColor Green
