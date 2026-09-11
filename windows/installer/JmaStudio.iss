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
#define AppVersion "1.0.0"
#define AppPublisher "JMA"
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
OutputBaseFilename=JmaStudio-Setup
SetupIconFile=..\src\JmaStudio.Gui\Assets\app_icon.ico
UninstallDisplayIcon={app}\Gui\JmaStudio.Gui.exe
WizardStyle=modern
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

// ---- dark palette + branding ----
// Inno Setup's TColor is $BBGGRR (blue-green-red byte order), the
// reverse of the app's own #RRGGBB brushes (App.xaml) -- every value
// below is the app's real hex color with its bytes reversed, not a
// separate palette invented for the installer.
//
// Naming individual WizardForm controls (the first approach here)
// turned out incomplete live: several stock pages (Select Destination
// Location, the custom consent page's own body) still showed default
// white/black, because Inno Setup's per-page background panels and a
// few instructional labels aren't exposed as named WizardForm
// properties at all. Replaced with a recursive walk over every control
// on the form instead -- catches everything, named or not, including
// pages created later like ConsentPage. Native Win32 buttons/progress
// bar are still left at default system style -- real owner-drawn
// theming for those is a much deeper rabbit hole than this pass
// justifies.
const
  ThemeBgColor = $120B0B;      // #0B0B12
  ThemePanelColor = $1F1515;   // #15151F
  ThemeTextColor = $F2EAEA;    // #EAEAF2
  ThemeSubTextColor = $AB9A9A; // #9A9AAB

procedure ThemeControl(C: TObject);
var
  I: Integer;
begin
  if C is TNewNotebookPage then TNewNotebookPage(C).Color := ThemeBgColor
  else if C is TPanel then TPanel(C).Color := ThemeBgColor
  else if C is TNewStaticText then TNewStaticText(C).Font.Color := ThemeTextColor
  else if C is TNewCheckListBox then
  begin
    TNewCheckListBox(C).Color := ThemePanelColor;
    TNewCheckListBox(C).Font.Color := ThemeTextColor;
  end
  else if C is TNewEdit then
  begin
    TNewEdit(C).Color := ThemePanelColor;
    TNewEdit(C).Font.Color := ThemeTextColor;
  end
  else if C is TRichEditViewer then
  begin
    TRichEditViewer(C).Color := ThemePanelColor;
    TRichEditViewer(C).Font.Color := ThemeTextColor;
  end
  // The Restart Manager "Preparing to Install" page (shown live, this
  // session, when a leftover JmaStudio.Gui.exe process was still
  // running and locking a file Setup needed to overwrite) stayed
  // unreadable even once the walk reached its controls (confirmed via a
  // temporary Log(Name) diagnostic pass: FPreparingYesRadio/
  // FPreparingNoRadio/FPreparingMemo ARE visited). Root cause: they're
  // Inno's own TNewRadioButton/TNewMemo classes (same "New"-prefixed
  // custom-drawn control family as TNewStaticText/TNewEdit/
  // TNewCheckListBox above), not plain VCL TRadioButton/TMemo -- an
  // earlier guess at those plain VCL class names compiled fine (they're
  // valid identifiers in Inno's script engine) but silently never
  // matched these actual runtime objects via `is`.
  else if C is TNewRadioButton then TNewRadioButton(C).Font.Color := ThemeTextColor
  else if C is TNewMemo then
  begin
    TNewMemo(C).Color := ThemePanelColor;
    TNewMemo(C).Font.Color := ThemeTextColor;
  end;

  if C is TWinControl then
    for I := 0 to TWinControl(C).ControlCount - 1 do
      ThemeControl(TWinControl(C).Controls[I]);
end;

procedure ApplyDarkTheme();
begin
  WizardForm.Color := ThemeBgColor;
  ThemeControl(WizardForm);
  // A couple of description/sub-text labels read better a shade dimmer
  // than the walk's blanket text color -- reapplied individually after,
  // not instead of, the general pass.
  WizardForm.PageDescriptionLabel.Font.Color := ThemeSubTextColor;
  WizardForm.WelcomeLabel2.Font.Color := ThemeSubTextColor;
  WizardForm.BeveledLabel.Font.Color := ThemeSubTextColor;

  // The Restart Manager "Preparing to Install" page's radio buttons/memo
  // stayed unreadable even after adding TNewRadioButton/TNewMemo `is`
  // checks to the generic ThemeControl walk above (confirmed these ARE
  // the real runtime classes via a Log(Name) diagnostic pass -- so
  // either that `is` check itself doesn't behave as expected against
  // Pascal Script's registered RTTI for these particular classes, or
  // something else in the walk intercepts them first). Bypassing the
  // walk entirely for just these three: WizardForm exposes them as
  // documented public properties with a compile-time-known type, so
  // this needs no runtime type check at all, unlike the generic walk.
  WizardForm.PreparingYesRadio.Font.Color := ThemeTextColor;
  WizardForm.PreparingNoRadio.Font.Color := ThemeTextColor;
  WizardForm.PreparingMemo.Color := ThemePanelColor;
  WizardForm.PreparingMemo.Font.Color := ThemeTextColor;
end;

procedure InitializeWizard();
begin
  ConsentPage := CreateInputOptionPage(wpWelcome,
    'Acer Lighting Service', 'This installer needs to disable a Windows service',
    'JMA Studio needs full control of your keyboard and rear lightbar lighting. To do that, it will stop AcerLightingService (the service behind Acer''s own PredatorSense lighting controls) and set its startup type to Disabled -- permanently, across reboots, until you uninstall JMA Studio or re-enable it yourself.' + #13#10 + #13#10 +
    'This does not affect any other PredatorSense feature -- only its lighting control.' + #13#10 + #13#10 +
    'This is reversible: uninstalling JMA Studio will offer to re-enable AcerLightingService.',
    False, False);
  ConsentPage.Add('I understand, and want to continue.');
  ConsentPage.Values[0] := False;

  // Applied AFTER ConsentPage exists, so the walk reaches its controls
  // too -- and again on every page change as a defensive backstop, in
  // case any stock page repopulates/recreates a control later (e.g. the
  // Ready page's summary memo).
  ApplyDarkTheme();
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  ApplyDarkTheme();
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

// ---- service registration (done here, not [Run]/[Registry], so the
// create -> set-env -> start ordering is guaranteed rather than relying
// on section-processing order) ----
procedure InstallService();
var
  ServiceExe, EnvBlock, RegArgs: String;
  ResultCode: Integer;
begin
  ServiceExe := ExpandConstant('{app}\Service\JmaStudio.Service.exe');

  Exec('sc.exe', Format('create %s binPath= "%s" start= auto obj= LocalSystem DisplayName= "%s"', ['{#ServiceName}', ServiceExe, '{#ServiceDisplayName}']), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
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

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    SeedDefaultDataIfMissing();
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

// Polls for a file to become deletable (i.e. its owning process has
// actually released its handle) instead of guessing a fixed delay.
// Replaces an earlier Sleep(2000)-after-sc-stop fix that turned out to
// be a real race, not a guarantee -- confirmed live: it worked on one
// uninstall test and then still lost the race on a later one, leaving
// JmaStudio.Service.exe behind again with the same "some elements could
// not be removed" warning the original fix was meant to close out.
// Deleting the file here (rather than just checking it's unlocked) is
// deliberate: it makes Inno's own later removal pass for this same
// tracked file a harmless no-op either way.
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
