@echo off
rem Desktop shortcut target: starts the JmaStudioService (if not already
rem running) and launches the GUI/tray. Starting a Windows Service
rem needs admin -- this self-elevates via a UAC prompt exactly like
rem start_all.ps1 does on the Python side, rather than requiring the
rem user to right-click "Run as administrator" themselves every time.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

sc start JmaStudioService >nul 2>&1
start "" "%~dp0Gui\JmaStudio.Gui.exe"
