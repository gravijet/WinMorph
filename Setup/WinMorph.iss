#ifndef AppVersion
  #error AppVersion must match Directory.Build.props
#endif

[Setup]
AppId=WinMorph
AppName=WinMorph
AppVersion={#AppVersion}
AppPublisher=Benjamin Berger
AppPublisherURL=https://winmorph.benjaminberger.at
AppSupportURL=https://github.com/gravijet/WinMorph/issues
DefaultDirName={localappdata}\Programs\WinMorph
DefaultGroupName=WinMorph
PrivilegesRequired=lowest
MinVersion=10.0
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\WinMorph.exe
SetupIconFile=..\WinMorph.ico
OutputDir=out
OutputBaseFilename=WinMorph-Setup
Compression=lzma2
SolidCompression=no
CloseApplications=yes
RestartApplications=no
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.DesktopShortcut=Create a desktop shortcut
de.DesktopShortcut=Verknüpfung auf dem Desktop erstellen
en.Restore=Restore original
de.Restore=Original wiederherstellen
en.Undo=Undo last look
de.Undo=Letzten Look rückgängig
en.InstallFailed=WinMorph could not finish installation. Please run Setup again.
de.InstallFailed=Die Installation konnte nicht abgeschlossen werden. Bitte Setup erneut starten.
en.StopFailed=WinMorph could not be stopped. Close it and try again.
de.StopFailed=WinMorph konnte nicht beendet werden. Bitte schließen und erneut versuchen.
en.RestoreFailed=Windows could not be fully restored. WinMorph and its backup have been kept. Open WinMorph and select Restore original, then try again.
de.RestoreFailed=Windows konnte nicht vollständig wiederhergestellt werden. WinMorph und das Backup bleiben erhalten. Bitte in WinMorph Original wiederherstellen wählen und erneut versuchen.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"

[Files]
Source: "..\build\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\WinMorph"; Filename: "{app}\WinMorph.exe"
Name: "{group}\WinMorph – {cm:Restore}"; Filename: "{app}\WinMorph.exe"; Parameters: "--restore-ui"
Name: "{group}\WinMorph – {cm:Undo}"; Filename: "{app}\WinMorph.exe"; Parameters: "--undo-ui"
Name: "{userdesktop}\WinMorph"; Filename: "{app}\WinMorph.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WinMorph.exe"; Description: "{cm:LaunchProgram,WinMorph}"; Flags: postinstall nowait skipifsilent
Filename: "{app}\WinMorph.exe"; Flags: nowait; Check: LaunchAfterUpdate

[UninstallDelete]
Type: files; Name: "{group}\*.lnk"

[Code]
function HasArg(Value: String): Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Value) = 0 then Result := True;
end;

function LaunchAfterUpdate: Boolean;
begin
  Result := WizardSilent and HasArg('/launch');
end;

function InitializeSetup: Boolean;
var Code: Integer;
begin
  Result := True;
  // Older WinMorph versions use /S. Keep their update invocation working.
  if HasArg('/S') and not WizardSilent then begin
    if not Exec(ExpandConstant('{srcexe}'), GetCmdTail + ' /VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewNoWait, Code) then
      RaiseException(CustomMessage('InstallFailed'));
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; AppExe: String;
begin
  Result := '';
  AppExe := ExpandConstant('{app}\WinMorph.exe');
  if FileExists(AppExe) then begin
    if not Exec(AppExe, '--shell-stop', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      Result := CustomMessage('StopFailed')
    else if not Exec(AppExe, '--live-stop', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      Result := CustomMessage('StopFailed');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Code: Integer;
begin
  if CurUninstallStep = usUninstall then
    if not Exec(ExpandConstant('{app}\WinMorph.exe'), '--uninstall-cleanup', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException(CustomMessage('RestoreFailed'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then
    if not Exec(ExpandConstant('{app}\WinMorph.exe'), '--post-install --lang ' + ActiveLanguage, '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException(CustomMessage('InstallFailed'));
end;
