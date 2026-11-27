; Inno Setup 6.7 script for Almatter. Build it through build-installer.ps1,
; which publishes the app into publish\ and passes the version on the
; command line.
;
; Choices behind it (see the 2026-09-14 discussion):
; - Per-user install under %LOCALAPPDATA%\Programs, no admin rights needed
;   at any point.
; - Native AOT build since 2026-11-25: Almatter.exe is native code and
;   carries what it needs of .NET, so nothing else gets installed. Before
;   that, Setup downloaded and installed the .NET 10 runtime when it was
;   missing; a PC that got it that way keeps it, Almatter no longer uses it.
; - Minimal wizard: tasks (desktop icon) -> install -> finish with
;   "Launch Almatter".

#define AppName "Almatter"
#define AppExeName "Almatter.exe"

#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + "publish\" + AppExeName)
#endif

[Setup]
; Never change AppId: it is how a newer installer recognises an existing
; install and upgrades it in place instead of installing a second copy.
AppId={{FFFC9E42-8A3A-4605-AFA3-95BC09BA5B21}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=yes
DisableReadyPage=yes
ShowLanguageDialog=no
WizardStyle=modern dynamic
SetupIconFile=..\app\Almatter.App\Assets\almatter-logo.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
; Closes a running Almatter (tray included) before replacing its files.
; Not restarted automatically: the finish page offers to launch it.
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=output
OutputBaseFilename=Almatter-Setup-{#AppVersion}

[Languages]
; French first: it is the fallback when Windows' language matches neither.
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
french.DeleteUserData=Supprimer aussi vos données Almatter (connexion enregistrée, réglages et messages en cache) ?%n%nChoisissez Non si vous comptez réinstaller Almatter.
english.DeleteUserData=Also delete your Almatter data (saved sign-in, settings and cached messages)?%n%nChoose No if you plan to reinstall Almatter.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; An update from a framework-dependent version (0.15.0 and earlier) would
; otherwise leave its ~30 managed DLLs, .json and .pdb files behind: the AOT
; Almatter.exe never loads them. Runs before [Files], so the DLLs the new
; version does ship (Rust core, Skia, HarfBuzz, ANGLE) are copied back
; straight after. {app} is Almatter's own folder under
; %LOCALAPPDATA%\Programs; nothing else lives there.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.pdb"

[Files]
; No .pdb at all: a Native AOT app does not read them at run time, so they
; would not add line numbers to crash logs (which keep the method names),
; and Almatter.pdb alone is close to 90 MB.
Source: "publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
; Almatter's own licence and those of bundled third-party assets (the Open
; Sans OFL requires its text to travel with the font).
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
; The picture the shortcuts show, a copy of the blue logo to start with.
; Almatter rewrites it in the tint chosen in its settings (Services/AppIcon.cs),
; so it is only laid down when missing: an update must not undo the choice.
Source: "..\app\Almatter.App\Assets\almatter-logo.ico"; DestDir: "{app}"; DestName: "almatter-icon.ico"; Flags: onlyifdoesntexist

[Icons]
; Pointed at almatter-icon.ico rather than at the executable, whose built-in
; icon can't change once installed.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\almatter-icon.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\almatter-icon.ico"; Tasks: desktopicon

[UninstallDelete]
; Left behind only if Almatter was stopped halfway through rewriting the icon.
Type: files; Name: "{app}\almatter-icon.ico.new"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; "Restart to update" in the app runs this installer with
; /SILENT /AUTOUPDATE=1 and quits: Almatter comes back by itself once the
; files are in place. A silent install started any other way (an admin
; deploying it) still leaves the app closed.
Filename: "{app}\{#AppExeName}"; Flags: nowait; Check: IsAutoUpdate

[Code]
function IsAutoUpdate: Boolean;
begin
  Result := ExpandConstant('{param:AUTOUPDATE|0}') = '1';
end;

// With the ready page disabled, the tasks page is the last one before the
// install starts, so its button should say so.
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectTasks then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
end;

// Program files go with the uninstall; user data only on request. It all
// lives under %LOCALAPPDATA%\Almatter, but the Rust core's cache stays in its
// former home under %APPDATA%\Almatter until the app has managed to move it.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox(CustomMessage('DeleteUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\{#AppName}'), True, True, True);
      DelTree(ExpandConstant('{userappdata}\{#AppName}'), True, True, True);
    end;
end;
