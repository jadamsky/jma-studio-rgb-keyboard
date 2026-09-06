# One-click launcher: stop AcerLightingService, start the daemon,
# start the tray icon. Stopping the service needs admin, so this
# re-launches itself elevated (one UAC prompt) if it isn't already.

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$root = $PSScriptRoot
$python = Join-Path $root ".venv\Scripts\python.exe"

Write-Host "Stopping AcerLightingService..."
Stop-Service -Name AcerLightingService -Force -ErrorAction SilentlyContinue

$daemonUp = Test-NetConnection -ComputerName 127.0.0.1 -Port 8420 -InformationLevel Quiet -WarningAction SilentlyContinue
if ($daemonUp) {
    Write-Host "Daemon already running on port 8420, skipping."
} else {
    Write-Host "Starting daemon..."
    Start-Process -FilePath $python -ArgumentList "-m uvicorn daemon.server:app --port 8420" -WorkingDirectory $root -WindowStyle Hidden
    Start-Sleep -Seconds 3
}

$trayUp = Get-CimInstance Win32_Process -Filter "Name='python.exe'" | Where-Object { $_.CommandLine -like '*tray.py*' }
if ($trayUp) {
    Write-Host "Tray icon already running, skipping."
} else {
    Write-Host "Starting tray icon..."
    Start-Process -FilePath $python -ArgumentList "tray.py" -WorkingDirectory $root -WindowStyle Hidden
}

Write-Host "Done."
Start-Sleep -Seconds 2
