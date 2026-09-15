; Inno Setup 6.7 script for Almatter. Build it through build-installer.ps1,
; which publishes the app into publish\ and passes the version on the
; command line; compiling this file on its own only works once publish\
; already holds a published build.
;
; Choices behind it (see the 2026-09-14 discussion):
; - Per-user install under %LOCALAPPDATA%\Programs, no admin rights needed
;   for Almatter itself.
; - Framework-dependent, untrimmed build. The app only needs the base .NET
;   runtime (Microsoft.NETCore.App, not the Desktop one); if .NET 10 is
;   missing, Setup downloads Microsoft's installer, checks it against the
;   pinned SHA-256 below, and runs it. That step is the only one that asks
;   for Windows' admin permission.
; - Minimal wizard: tasks (desktop icon) -> install -> finish with
;   "Launch Almatter". The ready page only appears when .NET has to be
;   installed, to say so before anything is downloaded.

#define AppName "Almatter"
#define AppExeName "Almatter.exe"

#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + "publish\" + AppExeName)
#endif

; .NET runtime fetched when missing. Any 10.0.x satisfies the app (it asks
; for 10.0.0 and rolls forward to the newest patch), so this only needs
; bumping to pick up security fixes for fresh installs. To update: take the
; "dotnet-runtime-win-x64.exe" entry of the latest release in
; https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json,
; check the download against Microsoft's SHA-512 listed there, then put its
; SHA-256 here (Inno Setup only verifies SHA-256).
#define DotNetVersion "10.0.12"
#define DotNetUrl "https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.12/dotnet-runtime-10.0.12-win-x64.exe"
#define DotNetSha256 "02ea072c08f890f9dc0d3ac71b5fd4c2bfcdced4e1133e2d917ce5422b025585"

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
french.DotNetReadyMemo=Almatter a besoin du moteur .NET 10 de Microsoft, qui n'est pas installé sur cet ordinateur.%n%nIl sera téléchargé (environ 30 Mo) puis installé automatiquement. Windows vous demandera votre autorisation pour cette étape.
french.DotNetInstallingCaption=Installation de .NET 10
french.DotNetInstallingDescription=Installation du moteur .NET de Microsoft, nécessaire à Almatter.
french.DotNetInstallingText=Installation de .NET 10 en cours, cela peut prendre une minute…
french.DotNetDownloadError=Le téléchargement de .NET 10 a échoué :%n%1%n%nVérifiez votre connexion Internet, puis réessayez.
french.DotNetNotDownloaded=Almatter ne peut pas fonctionner sans le moteur .NET 10, qui n'a pas pu être téléchargé.
french.DotNetCancelled=L'installation de .NET 10 a été annulée. Almatter ne peut pas fonctionner sans lui.
french.DotNetFailed=L'installation de .NET 10 a échoué (code %1). Almatter ne peut pas fonctionner sans lui.
french.DeleteUserData=Supprimer aussi vos données Almatter (connexion enregistrée, réglages et messages en cache) ?%n%nChoisissez Non si vous comptez réinstaller Almatter.
english.DotNetReadyMemo=Almatter needs Microsoft's .NET 10 runtime, which is not installed on this computer.%n%nIt will be downloaded (about 30 MB) and installed automatically. Windows will ask for your permission for this step.
english.DotNetInstallingCaption=Installing .NET 10
english.DotNetInstallingDescription=Installing Microsoft's .NET runtime, which Almatter needs.
english.DotNetInstallingText=Installing .NET 10, this can take a minute…
english.DotNetDownloadError=Downloading .NET 10 failed:%n%1%n%nCheck your Internet connection, then try again.
english.DotNetNotDownloaded=Almatter cannot run without the .NET 10 runtime, which could not be downloaded.
english.DotNetCancelled=The .NET 10 installation was cancelled. Almatter cannot run without it.
english.DotNetFailed=The .NET 10 installation failed (code %1). Almatter cannot run without it.
english.DeleteUserData=Also delete your Almatter data (saved sign-in, settings and cached messages)?%n%nChoose No if you plan to reinstall Almatter.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; lib*.pdb: debug symbols of the native SkiaSharp/HarfBuzz libraries, about
; 100 MB that users never need. Almatter.pdb stays: it puts line numbers in
; crash logs.
Source: "publish\*"; DestDir: "{app}"; Excludes: "lib*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
; Almatter's own licence and those of bundled third-party assets (the Open
; Sans OFL requires its text to travel with the font).
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetFileName = 'dotnet-runtime-{#DotNetVersion}-win-x64.exe';

var
  DotNetMissing: Boolean;
  DownloadPage: TDownloadWizardPage;
  DotNetInstallPage: TOutputProgressWizardPage;

// Looks where the .NET host itself looks: a 10.x folder of the base runtime
// under Program Files\dotnet that actually holds the runtime (an uninstall
// can leave an empty version folder behind).
function IsDotNet10Installed: Boolean;
var
  SharedDir: String;
  FindRec: TFindRec;
begin
  Result := False;
  SharedDir := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.NETCore.App\');
  if FindFirst(SharedDir + '10.*', FindRec) then
  begin
    try
      repeat
        Result := ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
          FileExists(SharedDir + FindRec.Name + '\System.Private.CoreLib.dll');
      until Result or not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

function InitializeSetup: Boolean;
begin
  DotNetMissing := not IsDotNet10Installed;
  Log(Format('.NET 10 runtime missing: %d', [Ord(DotNetMissing)]));
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
  DotNetInstallPage := CreateOutputProgressPage(CustomMessage('DotNetInstallingCaption'), CustomMessage('DotNetInstallingDescription'));
end;

// The ready page only matters when there is something to announce.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = wpReady) and not DotNetMissing;
end;

// With the ready page skipped, the tasks page is the last one before the
// install starts, so its button should say so.
procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and not DotNetMissing then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := CustomMessage('DotNetReadyMemo');
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
end;

// Downloads the pinned runtime installer into {tmp}, verifying its SHA-256,
// and offers to retry on failure (no connection, corrupted download).
function DownloadDotNet: Boolean;
var
  Retry: Boolean;
begin
  Result := False;
  DownloadPage.Clear;
  DownloadPage.Add('{#DotNetUrl}', DotNetFileName, '{#DotNetSha256}');
  DownloadPage.Show;
  try
    repeat
      Retry := False;
      try
        DownloadPage.Download;
        Log('.NET download finished');
        Result := True;
      except
        Log('.NET download failed: ' + GetExceptionMessage);
        if not DownloadPage.AbortedByUser then
          Retry := SuppressibleMsgBox(FmtMessage(CustomMessage('DotNetDownloadError'), [GetExceptionMessage]),
            mbError, MB_RETRYCANCEL, IDCANCEL) = IDRETRY;
      end;
    until not Retry;
  finally
    DownloadPage.Hide;
  end;
end;

// Runs Microsoft's installer. It is a per-machine install, so it raises the
// Windows permission prompt itself; ShellExec (rather than Exec) keeps that
// working whichever way the installer asks for elevation. /passive shows
// Microsoft's own progress bar, so a long install never looks frozen.
function InstallDotNet(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  DotNetInstallPage.SetText(CustomMessage('DotNetInstallingText'), '');
  DotNetInstallPage.ProgressBar.Style := npbstMarquee;
  DotNetInstallPage.Show;
  try
    if not ShellExec('', ExpandConstant('{tmp}\' + DotNetFileName), '/install /passive /norestart', '',
      SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    begin
      Result := FmtMessage(CustomMessage('DotNetFailed'), [IntToStr(ResultCode)]);
      Exit;
    end;
    Log(Format('.NET installer exit code: %d', [ResultCode]));
    case ResultCode of
      0, 1638: ; // installed / already installed
      3010, 1641: NeedsRestart := True;
      1223, 1602: Result := CustomMessage('DotNetCancelled');
    else
      Result := FmtMessage(CustomMessage('DotNetFailed'), [IntToStr(ResultCode)]);
    end;
  finally
    DotNetInstallPage.Hide;
  end;

  if (Result = '') and not NeedsRestart and not IsDotNet10Installed then
    Result := FmtMessage(CustomMessage('DotNetFailed'), [IntToStr(ResultCode)]);
end;

// Returning a message here stops Setup before anything of Almatter is
// copied, and shows that message.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not DotNetMissing then
    Exit;
  if not DownloadDotNet then
  begin
    Result := CustomMessage('DotNetNotDownloaded');
    Exit;
  end;
  Result := InstallDotNet(NeedsRestart);
  if Result = '' then
    DotNetMissing := False;
end;

// Program files go with the uninstall; user data only on request. It lives
// in two places today: settings, session and crash log under
// %LOCALAPPDATA%\Almatter, the Rust core's cache under %APPDATA%\Almatter.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox(CustomMessage('DeleteUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\{#AppName}'), True, True, True);
      DelTree(ExpandConstant('{userappdata}\{#AppName}'), True, True, True);
    end;
end;
