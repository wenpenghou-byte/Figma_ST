; FigmaSearch NSIS Installer
!include "MUI2.nsh"
!include "LogicLib.nsh"

!define PRODUCT_NAME     "FigmaSearch"
!define PRODUCT_SHORTCUT "肥姑妈搜"
!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION  "1.0.0"
!endif
!define PRODUCT_PUBLISHER "wenpenghou-byte"
!define PRODUCT_URL      "https://github.com/wenpenghou-byte/Figma_ST"
!define UNINST_KEY       "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define RUN_KEY          "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile ".\dist\FigmaSearch_Setup.exe"
InstallDir "$PROGRAMFILES64\${PRODUCT_NAME}"
InstallDirRegKey HKLM "Software\${PRODUCT_NAME}" "InstallDir"
RequestExecutionLevel admin
Unicode True

; MUI Settings
!define MUI_ABORTWARNING
!define MUI_ICON "..\src\FigmaSearch\Resources\app.ico"
!define MUI_UNICON "..\src\FigmaSearch\Resources\app.ico"

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\FigmaSearch.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch FigmaSearch now"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

; Main section (required)
Section "FigmaSearch Core" SEC_MAIN
    SectionIn RO
    SetOutPath "$INSTDIR"
    SetOverwrite ifnewer
    File /r ".\publish\*.*"
    WriteRegStr HKLM "${UNINST_KEY}" "DisplayName"    "${PRODUCT_NAME}"
    WriteRegStr HKLM "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKLM "${UNINST_KEY}" "DisplayVersion"  "${PRODUCT_VERSION}"
    WriteRegStr HKLM "${UNINST_KEY}" "Publisher"       "${PRODUCT_PUBLISHER}"
    WriteRegStr HKLM "${UNINST_KEY}" "URLInfoAbout"    "${PRODUCT_URL}"
    WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
    WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1
    WriteRegStr HKLM "Software\${PRODUCT_NAME}" "InstallDir" "$INSTDIR"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

Section /o "Desktop Shortcut" SEC_DESKTOP
    CreateShortcut "$DESKTOP\${PRODUCT_SHORTCUT}.lnk" "$INSTDIR\FigmaSearch.exe"
SectionEnd

Section /o "Start Menu Shortcut" SEC_STARTMENU
    CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
    CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_SHORTCUT}.lnk" "$INSTDIR\FigmaSearch.exe"
SectionEnd

Section "Run at Startup" SEC_STARTUP
    WriteRegStr HKCU "${RUN_KEY}" "${PRODUCT_NAME}" '"$INSTDIR\FigmaSearch.exe"'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN}      "Required: Install main program"
    !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESKTOP}   "Create a shortcut on Desktop"
    !insertmacro MUI_DESCRIPTION_TEXT ${SEC_STARTMENU} "Create a shortcut in Start Menu"
    !insertmacro MUI_DESCRIPTION_TEXT ${SEC_STARTUP}   "Launch FigmaSearch on Windows startup"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
    ; Kill the process if still running and wait
    ExecWait 'taskkill /f /im FigmaSearch.exe'
    Sleep 1500

    ; Remove all files in install dir (force delete)
    RMDir /r "$INSTDIR"

    ; If RMDir failed (files still locked), try again after another wait
    IfFileExists "$INSTDIR\*.*" 0 +3
        Sleep 2000
        RMDir /r "$INSTDIR"

    ; Remove shortcuts
    Delete "$DESKTOP\${PRODUCT_SHORTCUT}.lnk"
    Delete "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_SHORTCUT}.lnk"
    RMDir  "$SMPROGRAMS\${PRODUCT_NAME}"

    ; Remove AppData folder (database, settings)
    RMDir /r "$APPDATA\${PRODUCT_NAME}"

    ; Remove registry entries
    DeleteRegKey HKLM "${UNINST_KEY}"
    DeleteRegKey HKLM "Software\${PRODUCT_NAME}"
    DeleteRegValue HKCU "${RUN_KEY}" "${PRODUCT_NAME}"
SectionEnd
