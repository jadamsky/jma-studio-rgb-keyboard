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
end;

procedure InitializeWizard();
begin
  ConsentPage := CreateInputOptionPage(wpWelcome,
    'Acer Lighting Service', 'This installer needs to disable a Windows service',
    'JMA Studio needs full control of your keyboard and rear lightbar lighting. To do that, it will stop AcerLightingService (the service behind Acer''s own PredatorSense lighting controls) and set its startup type to Disabled -- permanently, across reboots, until you uninstall JMA Studio or re-enable it yourself.' + #13#10 + #13#10 +
    'This does not affect any other PredatorSense feature -- only its lighting control.' + #13#10 + #13#10 +
    'This is reversible: uninstalling JMA Studio re-enables AcerLightingService automatically.',
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
  end;
end;

// ---- uninstall: stop+remove the service, ask about user data, and
// re-enable AcerLightingService (no separate prompt for that -- the
// user only asked about data; automatically undoing the install-time
// disable is the safer default so a fresh uninstall doesn't leave
// PredatorSense's lighting permanently broken with no obvious cause). ----
var
  KeepUserData: Boolean;

function InitializeUninstall(): Boolean;
begin
  KeepUserData := (MsgBox('Keep your JMA Studio presets and configuration (' + AppDataRoot() + ')?' + #13#10#13#10 +
    'Choose Yes to keep them for a future reinstall, or No to delete them now.',
    mbConfirmation, MB_YESNO) = IDYES);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // `sc stop` returns once the SCM reports the service stopped, but
    // the self-contained .NET process can take a moment longer to fully
    // exit and release its own .exe file -- confirmed live: without
    // this, Inno's file-delete pass ran while JmaStudio.Service.exe was
    // still locked, leaving it behind with a "some elements could not
    // be removed" warning. A short settle delay is the standard fix.
    Sleep(2000);
    Exec('sc.exe', 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Best-effort -- a no-op if AcerLightingService isn't present on
    // this machine (not a PH16-71, or it was never installed).
    Exec('sc.exe', 'config AcerLightingService start= auto', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'start AcerLightingService', '', SW_HIDE, ewNoWait, ResultCode);
  end;

  if (CurUninstallStep = usPostUninstall) and (not KeepUserData) then
  begin
    DelTree(AppDataRoot(), True, True, True);
  end;
end;
