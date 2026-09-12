; JMA Studio installer (Phase 7). Built with Inno Setup 6.
; Run windows\installer\build.ps1 to publish both projects, stage the
; bundled default preset data, and compile this script -- don't run
; ISCC directly on a stale publish\ folder.
;
; Service name: JmaStudioService (LocalSystem, Automatic start --
; settled decision #2). Data lives at {commonappdata}\JMA Studio\ (NOT
; the dev-mode windows\data\ next to the source tree) -- settled
; decision #12's "likely ProgramData" call, made final here.
;
; Deliberately NOT included: any Python-detection or migration logic.
; The user decided (2026-09-10) that the installer ships the CURRENT
; C# preset/config data as its bundled defaults, full stop --
; PythonPresetMigrator.cs stays in the repo for manual dev-time use
; only (see JmaStudio.HardwareTest's migrate-presets command), never
; invoked by this installer.

#define AppName "JMA Studio"
#define AppVersion "2.0.0"
#define AppPublisher "Jake Adamsky"
#define ServiceName "JmaStudioService"
#define ServiceDisplayName "JMA Studio"

[Setup]
AppId={{6F2D6C2B-6B0E-4B7B-9B7B-1B9E7B7A2F41}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\JMA Studio
DefaultGroupName=JMA Studio
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=JmaStudio-Setup-V2
SetupIconFile=..\src\JmaStudio.Gui\Assets\app_icon.ico
UninstallDisplayIcon={app}\Gui\JmaStudio.Gui.exe
; "dark" = forced dark mode (matches this app's own always-dark WPF
; theme -- no light/auto toggle there either), using Inno 6.6.0+'s
; native dark-mode engine rather than the hand-rolled Pascal Script
; walk this replaced (see the [Code] section's own header comment on
; this, right above InitializeWizard, for the full story). WizardBackColor
; keeps JMA Studio's own brand background (the app's real #0B0B12,
; same value MainWindow.xaml/App.xaml use) instead of Inno's generic
; dark gray -- the custom wizard images/icon below are unaffected
; either way, dark mode still displays those as-is.
WizardStyle=modern dark
WizardBackColor=#0B0B12
WizardImageFile=assets\WizardImage.png
WizardSmallImageFile=assets\WizardSmallImage.png
; Confirmed live: Inno Setup's "modern" style image control is larger
; than the classic 164x314 size these images were generated at, so
; WizardImageStretch=no left a visible white margin around the image on
; the Welcome/Finished pages. Stretching avoids needing to guess the
; exact "modern" style control size.
WizardImageStretch=yes
Compression=lzma2
SolidCompression=yes
; Only active when build.ps1 detects a local "Jake Adamsky" self-signed
; code-signing certificate and passes /DSignInstaller=1 + a matching
; /Sjmasign=... sign-tool definition on the ISCC command line -- see
; that script's own header comment for exactly what this does and does
; not achieve (local-machine-only trust, not a real public signature).
; Compiling this .iss directly (no defines) skips signing entirely,
; same unsigned behavior as before.
#ifdef SignInstaller
SignTool=jmasign
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "publish\service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs
Source: "publish\gui\*"; DestDir: "{app}\Gui"; Flags: ignoreversion recursesubdirs
; Bundled defaults (current C# presets/config, NOT a Python migration --
; see this file's header comment). Kept permanently at {app}\DefaultData
; as a seed source, not just a transient install-time staging area, so
; a future "restore defaults" path has something to copy from.
Source: "publish\defaultdata\*"; DestDir: "{app}\DefaultData"; Flags: ignoreversion
; Desktop shortcut #2's target -- see this file's header comment on
; [Icons] for why a .bat instead of pointing straight at sc.exe.
Source: "StartJmaStudio.bat"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Two desktop shortcuts, both unconditional per explicit request (not
; gated behind an opt-in Task): one just opens the app/tray (no
; elevation -- if the Service is already running, e.g. via autostart,
; this is all that's ever needed); the other is a recovery/manual-start
; path that also (re)starts the Service itself, which requires admin --
; StartJmaStudio.bat self-elevates via UAC on launch (same pattern
; start_all.ps1 uses on the Python side) rather than needing a hacked
; "always run as administrator" flag on the .lnk file itself.
Name: "{group}\JMA Studio"; Filename: "{app}\Gui\JmaStudio.Gui.exe"
Name: "{group}\Start JMA Studio (Service + Tray)"; Filename: "{app}\StartJmaStudio.bat"; IconFilename: "{app}\Gui\JmaStudio.Gui.exe"
Name: "{group}\Uninstall JMA Studio"; Filename: "{uninstallexe}"
Name: "{autodesktop}\JMA Studio"; Filename: "{app}\Gui\JmaStudio.Gui.exe"
Name: "{autodesktop}\Start JMA Studio (Service + Tray)"; Filename: "{app}\StartJmaStudio.bat"; IconFilename: "{app}\Gui\JmaStudio.Gui.exe"

[Registry]
; GUI/tray autostart -- targets JmaStudio.Gui.exe itself (which owns the
; tray icon, see windows/HANDOFF.md's Phase 6.5), not the main window
; directly. No elevation needed for this (unlike Python's own AtLogon+
; Highest-privilege Scheduled Task): all the elevation-requiring work
; now lives entirely in the separately-registered Windows Service, so a
; plain per-user Run key is genuinely enough here.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "JMA Studio"; ValueData: """{app}\Gui\JmaStudio.Gui.exe"""; Flags: uninsdeletevalue

[Run]
; Checked-by-default "Launch JMA Studio" box on the Finish page -- this
; was missing entirely in the first build (confirmed live: the GUI/tray
; never appeared after clicking Finish, since nothing told Setup to
; start it). runasoriginaluser de-elevates the launch back to the
; interactive user even though Setup itself is running as admin --
; otherwise the GUI would inherit the installer's elevated token, the
; same avoidable elevation-creep already flagged in Phase 6.5's
; switch-to-csharp command.
Filename: "{app}\Gui\JmaStudio.Gui.exe"; Description: "Launch JMA Studio"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
var
  ConsentPage: TInputOptionWizardPage;
  InstallModePage: TInputOptionWizardPage;

const
  DataDirName = 'JMA Studio';

function DataDir(): String;
begin
  Result := ExpandConstant('{commonappdata}\' + DataDirName + '\data');
end;

function AppDataRoot(): String;
begin
  Result := ExpandConstant('{commonappdata}\' + DataDirName);
end;

// V2: detects a prior installation so the wizard can offer an Upgrade-
// vs-Clean-Install choice (only shown at all when this is True -- a
// genuine fresh install sees no extra page, same flow as before).
// Checks two independent signals since either alone could miss a
// partial/leftover state: the service registration key (survives even
// if {app} was manually deleted) and the install directory itself
// (covers the rarer case of a registered-but-not-yet-started service,
// or a service delete that failed but files remain).
function PriorInstallDetected(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\{#ServiceName}')
    or DirExists(ExpandConstant('{app}'));
end;

// ---- dark theme: now via Inno's own NATIVE dark mode (WizardStyle=
// modern dark, [Setup] section above), not hand-rolled Pascal Script.
//
// This replaces an entire generation of a hand-rolled `ThemeControl`
// recursive-walk-and-recolor approach that shipped in Phase 7 and was
// NEVER fully reliable: it needed one-off special-casing for stock
// pages, individual sub-text labels, and, worst of all, the Restart
// Manager "Preparing to Install" page's radio buttons/memo -- which
// stayed unreadable through 3 separate live-tested fix attempts (two
// wrong-class guesses, then a type-check-free direct-property write
// that STILL didn't work), backlogged in HANDOFF.md as "do not
// re-attempt without new evidence." The real evidence, found by
// actually reading this machine's installed Inno Setup version's own
// changelog rather than guessing a 4th time: Inno Setup 6.6.0 (2025-11-11)
// added genuine, first-class dark-mode support built into the compiler/
// runtime itself (`WizardStyle` appearance modes `light`/`dark`/
// `dynamic`), which correctly themes EVERY page -- including ones
// created dynamically at runtime, like Restart Manager's -- because
// it's real VCL-level styling, not a script reaching in from outside
// after the fact. Being verified live now (2026-09-12) against the
// exact page that defeated 3 prior attempts -- see HANDOFF.md for the
// outcome.
procedure InitializeWizard();
var
  ConsentAnchor: Integer;
begin
  ConsentAnchor := wpWelcome;

  // V2: Upgrade-vs-Clean-Install choice, only shown when a previous
  // install is actually detected -- a genuine fresh install skips this
  // page entirely (ConsentAnchor stays wpWelcome, so ConsentPage below
  // appears exactly where it always did). Chained via ConsentAnchor
  // (rather than both anchoring to wpWelcome directly) so the two
  // custom pages are guaranteed to appear in the right order --
  // multiple CreateInputOptionPage calls anchored to the SAME page ID
  // don't reliably order themselves by creation order otherwise.
  if PriorInstallDetected() then
  begin
    InstallModePage := CreateInputOptionPage(wpWelcome,
      'Existing Installation Found', 'Choose how to proceed',
      'JMA Studio appears to already be installed on this computer.' + #13#10 + #13#10 +
      'Upgrade keeps your saved presets and current settings -- the "rain" effect specifically is refreshed to this version''s improved defaults wherever it''s currently used, but everything else is left exactly as it is.' + #13#10 + #13#10 +
      'Clean install stops and completely removes the existing installation (service, program files) before installing fresh. You''ll still be asked separately whether to keep your presets and configuration, even with Clean install.',
      True, False);
    InstallModePage.Add('Upgrade (recommended -- keep my presets and settings)');
    InstallModePage.Add('Clean install (remove everything, then reinstall)');
    InstallModePage.SelectedValueIndex := 0;
    ConsentAnchor := InstallModePage.ID;
  end;

  ConsentPage := CreateInputOptionPage(ConsentAnchor,
    'Acer Lighting Service', 'This installer needs to disable a Windows service',
    'JMA Studio needs full control of your keyboard and rear lightbar lighting. To do that, it will stop AcerLightingService (the service behind Acer''s own PredatorSense lighting controls) and set its startup type to Disabled -- permanently, across reboots, until you uninstall JMA Studio or re-enable it yourself.' + #13#10 + #13#10 +
    'This does not affect any other PredatorSense feature -- only its lighting control.' + #13#10 + #13#10 +
    'This is reversible: uninstalling JMA Studio will offer to re-enable AcerLightingService.',
    False, False);
  ConsentPage.Add('I understand, and want to continue.');
  ConsentPage.Values[0] := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = ConsentPage.ID) and (not ConsentPage.Values[0]) then
  begin
    MsgBox('You must check the box to continue, or click Cancel to exit setup.', mbError, MB_OK);
    Result := False;
  end;
end;

// Polls for a file to become deletable (i.e. its owning process has
// actually released its handle) instead of guessing a fixed delay.
// Originally added for uninstall (see the standing note further below,
// near CurUninstallStepChanged, for the live incident that motivated
// it) and reused here for install/upgrade's own pre-copy stop, for the
// exact same reason: `sc stop`/`taskkill` return before the self-
// contained .NET process has necessarily released its own .exe file
// handle. Deleting the file here (rather than just checking it's
// unlocked) is deliberate: it makes Inno's own later file-copy/removal
// passes for this same tracked file a harmless no-op either way.
procedure WaitForFileUnlocked(const FileName: String; MaxAttempts, DelayMs: Integer);
var
  I: Integer;
begin
  if not FileExists(FileName) then Exit;
  for I := 1 to MaxAttempts do
  begin
    if DeleteFile(FileName) then Exit;
    Sleep(DelayMs);
  end;
  // Still locked after the full wait -- log it and let Inno's own
  // removal pass make one more attempt anyway, rather than looping
  // forever on a machine where something unusual is holding the handle.
  Log('WaitForFileUnlocked: gave up waiting for ' + FileName + ' to unlock.');
end;

// ---- upgrade safety: stop whatever's already running BEFORE files are
// copied, so an install-over-an-existing-install doesn't try to
// overwrite a locked, currently-running .exe (the Service runs as
// LocalSystem and isn't something Inno's own Restart Manager prompt
// handles -- that only covers ordinary user processes like the GUI).
// Called from CurStepChanged's ssInstall case, which fires after the
// wizard is done but BEFORE Inno starts copying any [Files] entries --
// harmless no-op (both Exec calls just fail silently, ResultCode
// unused) on a genuine fresh install where neither exists yet.
procedure StopExistingInstallation();
var
  ResultCode: Integer;
begin
  Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM JmaStudio.Gui.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  WaitForFileUnlocked(ExpandConstant('{app}\Service\JmaStudio.Service.exe'), 40, 250);
  WaitForFileUnlocked(ExpandConstant('{app}\Gui\JmaStudio.Gui.exe'), 40, 250);
end;

// V2: "Clean install" path, chosen on InstallModePage instead of the
// default Upgrade. Deliberately does NOT reuse the full uninstaller
// (which also asks about re-enabling AcerLightingService/Python
// autostart) -- those two questions make no sense moments before this
// SAME app reinstalls itself and immediately retakes lighting control
// again, so they'd just be confusing noise here. This is a narrower,
// purpose-built wipe: stop everything, delete the service registration
// outright (not just reconfigure -- InstallService() will recreate it
// fresh afterward), remove the whole {app} directory tree, and --
// per the user's explicit "still ask about presets and current
// effects" instruction -- separately ask whether to also wipe
// AppDataRoot() (presets/config/live-state), with the SAME Yes-keeps-
// them semantics as the uninstaller's own equivalent prompt. Runs at
// CurStepChanged's ssInstall step, i.e. before any [Files] copying, in
// place of StopExistingInstallation() (this supersedes it -- calling
// both would be redundant, not harmful, but there's no reason to).
procedure PerformCleanWipe();
var
  ResultCode: Integer;
  KeepData: Boolean;
begin
  KeepData := (MsgBox('You chose Clean Install.' + #13#10 + #13#10 +
    'Would you like to KEEP your existing JMA Studio presets and configuration (' + AppDataRoot() + ')?' + #13#10 + #13#10 +
    'Choose Yes to keep them, or No to delete them and start completely fresh.',
    mbConfirmation, MB_YESNO) = IDYES);

  Exec('taskkill.exe', '/F /IM JmaStudio.Gui.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  WaitForFileUnlocked(ExpandConstant('{app}\Service\JmaStudio.Service.exe'), 40, 250);
  WaitForFileUnlocked(ExpandConstant('{app}\Gui\JmaStudio.Gui.exe'), 40, 250);
  Exec('sc.exe', 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  DelTree(ExpandConstant('{app}'), True, True, True);

  if not KeepData then
  begin
    DelTree(AppDataRoot(), True, True, True);
  end;
end;

// ---- service registration (done here, not [Run]/[Registry], so the
// create -> set-env -> start ordering is guaranteed rather than relying
// on section-processing order) ----
// Upgrade-safe: checks whether the service already exists (`sc query`
// exits 1060/ERROR_SERVICE_DOES_NOT_EXIST if not) rather than always
// calling `sc create`, which would otherwise silently fail every time
// on an upgrade (a service name that already exists can't be
// re-created) -- ResultCode from that failed call was never even
// checked before, so this was a real latent bug, just one that
// happened not to matter yet since binPath/DisplayName never changed
// between versions. An existing registration is reconfigured (in case
// either ever does change) rather than left untouched.
procedure InstallService();
var
  ServiceExe, EnvBlock, RegArgs: String;
  ResultCode: Integer;
  AlreadyExists: Boolean;
begin
  ServiceExe := ExpandConstant('{app}\Service\JmaStudio.Service.exe');

  Exec('sc.exe', 'query {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  AlreadyExists := (ResultCode <> 1060);

  if AlreadyExists then
  begin
    Log('Service already registered -- reconfiguring instead of re-creating.');
    Exec('sc.exe', Format('config %s binPath= "%s" start= auto obj= LocalSystem DisplayName= "%s"', ['{#ServiceName}', ServiceExe, '{#ServiceDisplayName}']), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end
  else
  begin
    Exec('sc.exe', Format('create %s binPath= "%s" start= auto obj= LocalSystem DisplayName= "%s"', ['{#ServiceName}', ServiceExe, '{#ServiceDisplayName}']), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  Exec('sc.exe', Format('description %s "Drives the keyboard, rear lightbar, and controller-reactive lighting for JMA Studio."', ['{#ServiceName}']),
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Sets this specific service's process environment via its own
  // registry key -- the SCM applies this to the service's process at
  // start, same effect as an env var set for a normal process. Done via
  // reg.exe rather than a Pascal Script registry-write function: `\0`
  // typed literally in a REG_MULTI_SZ /d value is reg.exe's own
  // documented way to embed the null separator between strings from the
  // command line, well-established and simpler than guessing at Inno's
  // exact scripting API for this narrow a case.
  EnvBlock := 'JMASTUDIO_DATA_DIR=' + DataDir() + '\0JMASTUDIO_KEYMAP_PATH=' + AppDataRoot() + '\keymap.json';
  RegArgs := Format('add "HKLM\SYSTEM\CurrentControlSet\Services\%s" /v Environment /t REG_MULTI_SZ /d "%s" /f', ['{#ServiceName}', EnvBlock]);
  Exec('reg.exe', RegArgs, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec('sc.exe', 'start {#ServiceName}', '', SW_HIDE, ewNoWait, ResultCode);
end;

// Disables (does NOT delete) the Python version's own "JMA Studio
// Autostart" Scheduled Task, if present -- best-effort, silent no-op on
// the vast majority of machines that never had the Python version
// installed at all. Real incident that motivated this: on the author's
// own dev machine (which still has this task from before the C# port),
// this task launches Python's daemon+tray+gui at every logon regardless
// of whether JMA Studio C# is installed -- if both are ever enabled to
// autostart at once, they fight over the same hardware (a known,
// previously-hit bug: flickering/color-bleeding, see this project's
// earlier session history). Disabling rather than deleting keeps this
// fully reversible: the Diagnostics window's "Switch to Python" button
// (DiagnosticsManager.cs) already re-enables this exact task by name
// when a user deliberately switches stacks.
procedure DisablePythonAutostartIfPresent();
var
  ResultCode: Integer;
begin
  Exec('schtasks.exe', '/Change /TN "JMA Studio Autostart" /Disable', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// ---- default data seeding: only on a genuine fresh install (the
// ProgramData data dir doesn't exist yet at all) -- never on an
// upgrade, so an existing install's edited presets are never clobbered.
procedure SeedDefaultDataIfMissing();
var
  Seeds: TArrayOfString;
  I: Integer;
  SrcFile, DestFile: String;
begin
  if DirExists(DataDir()) then
  begin
    Log('Data directory already exists, skipping default-data seeding.');
    Exit;
  end;

  ForceDirectories(DataDir());
  SetArrayLength(Seeds, 5);
  Seeds[0] := 'app-config.json';
  Seeds[1] := 'keyboard-presets.json';
  Seeds[2] := 'lightbar-presets.json';
  Seeds[3] := 'lightbar-reactive-config.json';
  Seeds[4] := 'controller-reactive-settings.json';

  for I := 0 to GetArrayLength(Seeds) - 1 do
  begin
    SrcFile := ExpandConstant('{app}\DefaultData\') + Seeds[I];
    DestFile := DataDir() + '\' + Seeds[I];
    if FileExists(SrcFile) then
      CopyFile(SrcFile, DestFile, False);
  end;

  CopyFile(ExpandConstant('{app}\DefaultData\keymap.json'), AppDataRoot() + '\keymap.json', False);
end;

// ---- the ONE named exception to "never touch existing presets/effect
// data on an upgrade": the user explicitly asked that the "rain" effect
// specifically pick up this release's reworked defaults (Phase 9 --
// bug fix, fade-in, dynamic shower intensity, accent drop) wherever
// it's currently in use, on every install (fresh or upgrade) -- not
// just SeedDefaultDataIfMissing's fresh-install-only seeding above.
// Delegates to JmaStudio.Service.exe's own --migrate-rain-defaults mode
// (Program.cs) rather than reimplementing JSON handling in Pascal
// Script -- that mode reuses the exact same PresetStore/JSON-converter
// code the Service itself uses, so this can't drift out of sync with
// how presets/live-state are actually shaped. Runs the freshly-copied
// exe directly as a one-shot console invocation (it detects the
// migration flag before doing anything service-like and exits
// immediately) -- safe to run before the service itself is
// (re)started.
procedure RefreshRainDefaults();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{app}\Service\JmaStudio.Service.exe'),
    '--migrate-rain-defaults "' + DataDir() + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Fires after the wizard is done but BEFORE Inno copies any [Files]
  // entries -- the right moment to either stop whatever's already
  // running (Upgrade, or a genuine fresh install where both are no-ops)
  // or fully wipe the previous installation first (Clean install, only
  // reachable when InstallModePage exists AND its second option was
  // picked).
  if CurStep = ssInstall then
  begin
    if Assigned(InstallModePage) and (InstallModePage.SelectedValueIndex = 1) then
      PerformCleanWipe()
    else
      StopExistingInstallation();
  end;

  if CurStep = ssPostInstall then
  begin
    SeedDefaultDataIfMissing();
    RefreshRainDefaults();
    InstallService();
    DisablePythonAutostartIfPresent();
  end;
end;

// ---- uninstall: stop+remove the service, ask about user data, and ask
// whether to re-enable AcerLightingService. ----
// Originally this re-enabled AcerLightingService unconditionally, no
// prompt -- reasoning at the time: undoing the install-time disable by
// default is safer than leaving a typical end user's lighting broken
// with no obvious cause. Changed after a real live incident: on the
// author's own dev machine, uninstalling silently re-enabled
// AcerLightingService, and a still-enabled pre-existing "JMA Studio
// Autostart" Scheduled Task (Python's own autostart, predating this C#
// port, NOT managed by this installer) then re-stopped it again moments
// later at its own daemon startup -- confusing, and pointless work for
// anyone with another lighting controller already in charge. Most real
// end users won't have that Python fallback, so defaulting the prompt
// itself towards Yes still matches the original reasoning; the
// difference is asking first rather than assuming.
var
  KeepUserData: Boolean;
  ReEnableAcerService: Boolean;
  ReEnablePythonAutostart: Boolean;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  KeepUserData := (MsgBox('Keep your JMA Studio presets and configuration (' + AppDataRoot() + ')?' + #13#10#13#10 +
    'Choose Yes to keep them for a future reinstall, or No to delete them now.',
    mbConfirmation, MB_YESNO) = IDYES);

  // Verify AcerLightingService actually exists on this machine before
  // even asking -- `sc query` exits with 1060 (ERROR_SERVICE_DOES_NOT_EXIST)
  // if it's absent (not a PH16-71, or it was never installed), in which
  // case there's nothing to ask about at all.
  ReEnableAcerService := False;
  Exec('sc.exe', 'query AcerLightingService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 1060 then
  begin
    ReEnableAcerService := (MsgBox('JMA Studio disabled Acer''s own PredatorSense lighting service (AcerLightingService) when it was installed, so it could take full control of your keyboard and lightbar lighting.' + #13#10#13#10 +
      'Would you like to re-enable it now and set it back to Automatic startup?' + #13#10#13#10 +
      'Choose Yes if nothing else is controlling this hardware''s lighting, or No to leave it disabled.',
      mbConfirmation, MB_YESNO) = IDYES);
  end;

  // Symmetric with the AcerLightingService prompt above: only ask if the
  // Python version's own autostart task actually exists on this machine
  // at all (schtasks /Query exits 0 if found, nonzero otherwise) --
  // silent no-op on the vast majority of machines that never had the
  // Python version installed. Doesn't check whether it's currently
  // Enabled/Disabled first (that would need parsing schtasks' own text
  // output); re-enabling an already-enabled task via /Enable is a
  // harmless no-op, so this stays simple.
  ReEnablePythonAutostart := False;
  Exec('schtasks.exe', '/Query /TN "JMA Studio Autostart"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode = 0 then
  begin
    ReEnablePythonAutostart := (MsgBox('A Scheduled Task for the Python version of JMA Studio ("JMA Studio Autostart") was found on this machine.' + #13#10#13#10 +
      'Would you like to re-enable it now, so the Python version starts automatically again at your next login?' + #13#10#13#10 +
      'Choose Yes if you''d like the Python version to take back over, or No to leave it as it is.',
      mbConfirmation, MB_YESNO) = IDYES);
  end;

  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // The GUI/tray process is never stopped by anything else during
    // uninstall (no service registration of its own, no graceful-
    // shutdown path invoked here) -- confirmed live: a real uninstall
    // test run with the GUI still open left JmaStudio.Gui.exe behind too,
    // a second, previously-undiscovered leftover-file bug alongside the
    // Service one below. Best-effort force-kill; taskkill exits non-zero
    // if the process wasn't running at all, which is fine (ResultCode
    // unused here on purpose).
    Exec('taskkill.exe', '/F /IM JmaStudio.Gui.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // `sc stop` returns once the SCM reports the service stopped, but
    // the self-contained .NET process can take a moment longer to fully
    // exit and release its own .exe file. See WaitForFileUnlocked's own
    // comment above for why this used to be a fixed Sleep(2000) and why
    // that wasn't actually reliable.
    WaitForFileUnlocked(ExpandConstant('{app}\Service\JmaStudio.Service.exe'), 40, 250);
    WaitForFileUnlocked(ExpandConstant('{app}\Gui\JmaStudio.Gui.exe'), 40, 250);
    Exec('sc.exe', 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Only if the user said Yes in InitializeUninstall's prompt above --
    // see that function's comment for why this is no longer unconditional.
    if ReEnableAcerService then
    begin
      Exec('sc.exe', 'config AcerLightingService start= auto', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Exec('sc.exe', 'start AcerLightingService', '', SW_HIDE, ewNoWait, ResultCode);
    end;
    // Only if the user said Yes in InitializeUninstall's prompt above.
    if ReEnablePythonAutostart then
    begin
      Exec('schtasks.exe', '/Change /TN "JMA Studio Autostart" /Enable', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if not KeepUserData then
    begin
      DelTree(AppDataRoot(), True, True, True);
    end;

    // Confirmed live: {app}\Service and {app}\Gui were left behind as
    // empty directories after a real uninstall test, even though both
    // exe files inside them were successfully removed -- WaitForFileUnlocked
    // above deletes those files itself (via DeleteFile), ahead of Inno's
    // own built-in file-removal pass, which apparently breaks Inno's
    // normal "remove the directory once nothing's left in it" bookkeeping
    // (it never sees itself perform the deletion, so its own auto-cleanup
    // for that directory never triggers). RemoveDir only succeeds on an
    // actually-empty directory and fails silently otherwise, so this is
    // safe to call unconditionally regardless of whether the fix above
    // was even needed this time.
    RemoveDir(ExpandConstant('{app}\Service'));
    RemoveDir(ExpandConstant('{app}\Gui'));
    RemoveDir(ExpandConstant('{app}'));
  end;
end;
