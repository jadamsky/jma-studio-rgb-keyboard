# One-time setup for a fresh checkout of this repo (e.g. after
# reinstalling Windows). Creates the virtualenv, installs dependencies,
# places hidapi.dll where the `hid` package can find it, creates a
# Desktop shortcut, and registers the "JMA Studio Autostart" Scheduled
# Task so the daemon + tray start automatically at login with no UAC
# prompt. See README.md for what each of these steps means and how to
# do them by hand if you'd rather not run this script.
#
# Registering the Scheduled Task needs admin, so this re-launches
# itself elevated (one UAC prompt) if it isn't already -- same pattern
# as start_all.ps1.

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs -Wait
    exit
}

$root = $PSScriptRoot
$venvPython = Join-Path $root ".venv\Scripts\python.exe"

Write-Host "=== JMA Studio setup ===" -ForegroundColor Cyan

# ---- 1. Python venv ----
if (-not (Test-Path $venvPython)) {
    Write-Host "Creating virtual environment..."
    $pythonCmd = Get-Command python -ErrorAction SilentlyContinue
    if (-not $pythonCmd) {
        Write-Host "ERROR: Python not found on PATH. Install Python 3.10+ from python.org first (check 'Add python.exe to PATH' during install), then re-run this script." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
    & python -m venv "$root\.venv"
} else {
    Write-Host "Virtual environment already exists, skipping creation."
}

# ---- 2. Dependencies ----
Write-Host "Installing dependencies..."
& $venvPython -m pip install --quiet --upgrade pip
& $venvPython -m pip install --quiet -r "$root\requirements.txt"

# ---- 3. hidapi.dll ----
# The `hid` package is a ctypes wrapper -- it does NOT install the
# native hidapi.dll itself. It must sit next to python.exe (i.e. in
# .venv\Scripts\) or every HID call fails at import time. A copy is
# bundled at the repo root specifically so this step doesn't depend on
# any external download still being available years from now.
$dllSource = Join-Path $root "hidapi.dll"
$dllDest = Join-Path $root ".venv\Scripts\hidapi.dll"
if (Test-Path $dllSource) {
    Copy-Item $dllSource $dllDest -Force
    Write-Host "Placed hidapi.dll in .venv\Scripts\"
} else {
    Write-Host "WARNING: hidapi.dll not found at repo root -- hardware control will fail until you place a copy at $dllDest (see README.md)." -ForegroundColor Yellow
}

# ---- 4. Desktop shortcut ----
$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktop "RGB Keyboard.lnk"
$iconPath = Join-Path $root "gui\app_icon.ico"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $root "start_all.bat"
$shortcut.WorkingDirectory = $root
if (Test-Path $iconPath) {
    $shortcut.IconLocation = "$iconPath,0"
}
$shortcut.Save()
Write-Host "Created Desktop shortcut: $shortcutPath"

# ---- 5. Autostart Scheduled Task ----
# AtLogOn + RunLevel Highest lets Windows elevate this silently at
# login (the elevation is pre-authorized in the task definition, not
# requested interactively) -- this is what avoids a UAC prompt every
# single login, since start_all.ps1 needs admin to stop
# AcerLightingService. See README.md for the manual equivalent if you
# ever need to recreate just this piece.
$taskName = "JMA Studio Autostart"
$action = New-ScheduledTaskAction -Execute (Join-Path $root "start_all.bat") -WorkingDirectory $root
$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:COMPUTERNAME\$env:USERNAME"
$principal = New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\$env:USERNAME" -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Write-Host "Registered Scheduled Task: $taskName (runs at every login)"

Write-Host ""
Write-Host "=== Setup complete ===" -ForegroundColor Cyan
Write-Host "Run start_all.bat now to test it immediately, or just log out and back in."
Read-Host "Press Enter to exit"
