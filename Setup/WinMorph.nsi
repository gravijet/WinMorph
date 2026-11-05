Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WinVer.nsh"
!ifndef AppVersion
  !error "AppVersion must match Directory.Build.props"
!endif

Name "WinMorph"
OutFile "out\WinMorph-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\WinMorph"
RequestExecutionLevel user
SetCompressor zlib
Icon "..\WinMorph.ico"
UninstallIcon "..\WinMorph.ico"
VIProductVersion "${AppVersion}.0"
VIAddVersionKey "ProductName" "WinMorph"
VIAddVersionKey "CompanyName" "Benjamin Berger"
VIAddVersionKey "FileDescription" "WinMorph Setup"
VIAddVersionKey "FileVersion" "${AppVersion}"
VIAddVersionKey "LegalCopyright" "© 2026 Benjamin Berger"

!define MUI_ICON "..\WinMorph.ico"
!define MUI_UNICON "..\WinMorph.ico"
!define MUI_ABORTWARNING
!define MUI_LANGDLL_REGISTRY_ROOT HKCU
!define MUI_LANGDLL_REGISTRY_KEY "Software\WinMorph"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!define MUI_FINISHPAGE_RUN "$INSTDIR\WinMorph.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "German"

LangString Desktop ${LANG_ENGLISH} "Desktop shortcut"
LangString Desktop ${LANG_GERMAN} "Verknüpfung auf dem Desktop"
LangString InstallFailed ${LANG_ENGLISH} "WinMorph could not finish installation. Close WinMorph and try again."
LangString InstallFailed ${LANG_GERMAN} "Die Installation konnte nicht abgeschlossen werden. Bitte WinMorph schließen und erneut versuchen."
LangString RestoreFailed ${LANG_ENGLISH} "Windows could not be fully restored. WinMorph and its backup have been kept. Open WinMorph and select Restore original, then try again."
LangString RestoreFailed ${LANG_GERMAN} "Windows konnte nicht vollständig wiederhergestellt werden. WinMorph und das Backup bleiben erhalten. Bitte in WinMorph Original wiederherstellen wählen und erneut versuchen."
LangString NeedWindows ${LANG_ENGLISH} "WinMorph requires Windows 10 or 11."
LangString NeedWindows ${LANG_GERMAN} "WinMorph benötigt Windows 10 oder 11."

Function .onInit
  SetShellVarContext current
  !insertmacro MUI_LANGDLL_DISPLAY
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "$(NeedWindows)" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
FunctionEnd

!macro RunChecked Arguments Failure
  nsExec::ExecToStack /TIMEOUT=60000 '"$INSTDIR\WinMorph.exe" ${Arguments}'
  Pop $0
  Pop $1
  ${If} $0 != 0
    DetailPrint "$1"
    MessageBox MB_ICONSTOP "${Failure}" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
!macroend

Section "WinMorph"
  SectionIn RO
  IfFileExists "$INSTDIR\WinMorph.exe" 0 copy
    !insertmacro RunChecked "--shell-stop" "$(InstallFailed)"
    !insertmacro RunChecked "--live-stop" "$(InstallFailed)"
  copy:
  SetOutPath "$INSTDIR"
  File "..\build\WinMorph.exe"
  File "..\build\WinMorph.exe.config"
  File "..\LICENSE"
  StrCpy $2 "en"
  ${If} $LANGUAGE == ${LANG_GERMAN}
    StrCpy $2 "de"
  ${EndIf}
  !insertmacro RunChecked "--post-install --lang $2" "$(InstallFailed)"
  CreateDirectory "$SMPROGRAMS\WinMorph"
  CreateShortcut "$SMPROGRAMS\WinMorph\WinMorph.lnk" "$INSTDIR\WinMorph.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph" "DisplayName" "WinMorph"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph" "DisplayVersion" "${AppVersion}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph" "Publisher" "Benjamin Berger"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph" "DisplayIcon" "$INSTDIR\WinMorph.exe,0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph" "URLInfoAbout" "https://winmorph.benjaminberger.at"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph" "NoRepair" 1
SectionEnd

Section /o "$(Desktop)"
  CreateShortcut "$DESKTOP\WinMorph.lnk" "$INSTDIR\WinMorph.exe"
SectionEnd

Function .onInstSuccess
  IfSilent 0 done
    ${GetParameters} $0
    ClearErrors
    ${GetOptions} $0 "/launch" $1
    ${IfNot} ${Errors}
      Exec '"$INSTDIR\WinMorph.exe"'
    ${EndIf}
  done:
FunctionEnd

Function un.onInit
  SetShellVarContext current
  !insertmacro MUI_UNGETLANGUAGE
FunctionEnd

Section "Uninstall"
  !insertmacro RunChecked "--uninstall-cleanup" "$(RestoreFailed)"
  Delete "$DESKTOP\WinMorph.lnk"
  Delete "$SMPROGRAMS\WinMorph\*.lnk"
  RMDir "$SMPROGRAMS\WinMorph"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph"
  Delete "$INSTDIR\WinMorph.exe"
  Delete "$INSTDIR\WinMorph.exe.config"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
