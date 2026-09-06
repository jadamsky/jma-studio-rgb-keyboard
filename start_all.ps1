# One-click launcher: stop AcerLightingService, start the daemon,
# start the tray icon. Stopping the service needs admin. Normally this
# runs via the "JMA Studio Autostart" Scheduled Task (At Log On, Run
# with highest privileges) -- Task Scheduler elevates it silently, no
# UAC prompt, since the elevation is pre-authorized in the task
# definition rather than requested interactively. The self-elevation
# check below is only a fallback for manually double-clicking
# start_all.bat outside the scheduled task, where it'll still prompt
# once, as expected for an interactive elevation request.

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$root = $PSScriptRoot
$python = Join-Path $root ".venv\Scripts\python.exe"

Write-Host "Stopping AcerLightingService..."
# Plain Automatic (non-delayed) start, confirmed via the service's
# registry Start/DelayedAutoStart values -- it starts during boot,
# well before this AtLogOn-triggered task runs, so it should always
# already be up here. Retrying anyway is cheap insurance against any
# unusual edge case (e.g. a slow driver init after a fast-startup
# resume) where it's still mid-start and a single Stop-Service races it.
$attempts = 0
do {
    $attempts++
    Stop-Service -Name AcerLightingService -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    $svc = Get-Service -Name AcerLightingService -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq "Stopped") {
        Write-Host "AcerLightingService stopped (attempt $attempts)."
        break
    }
    if ($attempts -lt 5) {
        Write-Host "AcerLightingService not stopped yet (attempt $attempts), retrying..."
    }
} while ($attempts -lt 5)
if ($svc -and $svc.Status -ne "Stopped") {
    Write-Host "WARNING: AcerLightingService still not stopped after $attempts attempts."
}

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
