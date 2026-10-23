; TudoMKV - instalador NSIS (por usuário, sem administrador).
;
; Substitui o kit auto-extraível do IExpress: o que o antigo
; build/installer/install.ps1 fazia na mão agora é o próprio instalador.
;   * o programa vai para %LOCALAPPDATA%\Programs\TudoMKV;
;   * TudoMKV.exe, LEIA-ME.txt e LICENSE.txt ficam na raiz e o FFmpeg em runtimes\ffmpeg;
;   * atalho no Menu Iniciar sempre e na Área de Trabalho por opção;
;   * entrada em "Aplicativos e recursos" só do usuário atual (HKCU), sem UAC;
;   * /S instala sem nenhuma janela e o desinstalador aceita o mesmo /S.
;
; Compilar:
;   makensis /DVERSION=0.5.11 /DSOURCE=dist\TudoMKV /DOUTFILE=dist\setup.exe build\installer\TudoMKV.nsi
; O build/package-installer.ps1 chama o makensis com esses parâmetros.

Unicode true

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!ifndef SOURCE
  !error "Defina SOURCE com a pasta que tem o TudoMKV.exe (ex.: /DSOURCE=dist\TudoMKV)."
!endif

!ifndef VERSION
  !define VERSION "0.0.0"
!endif

!ifndef OUTFILE
  !define OUTFILE "TudoMKV-${VERSION}-setup.exe"
!endif

!define APP_NAME "TudoMKV"
!define APP_EXE "TudoMKV.exe"
!define APP_PUBLISHER "Clediomar do Carmo Santos"
!define APP_DESCRIPTION "TudoMKV - remux de DVD, Blu-ray e vídeos para MKV"
!define APP_HOMEPAGE "https://github.com/Hayllander/TudoMKV"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\TudoMKV"

Name "${APP_NAME} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\TudoMKV"
InstallDirRegKey HKCU "${UNINST_KEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APP_NAME} ${VERSION}"
ShowInstDetails show
ShowUninstDetails show

!ifdef VI_VERSION
  VIProductVersion "${VI_VERSION}"
  VIAddVersionKey "ProductName" "${APP_NAME}"
  VIAddVersionKey "ProductVersion" "${VERSION}"
  VIAddVersionKey "FileDescription" "${APP_DESCRIPTION}"
  VIAddVersionKey "FileVersion" "${VERSION}"
  VIAddVersionKey "CompanyName" "${APP_PUBLISHER}"
  VIAddVersionKey "LegalCopyright" "${APP_PUBLISHER}"
!endif

!define MUI_ABORTWARNING
!define MUI_LICENSEPAGE_TEXT_TOP "O TudoMKV é software livre, distribuído sob a GNU GPLv3."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Abrir o TudoMKV agora"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${SOURCE}\LICENSE.txt"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "PortugueseBR"

Function .onInit
  ; Atalhos e chaves são do usuário atual, como no kit antigo (sem UAC).
  SetShellVarContext current

  ; Gravar por cima de um TudoMKV aberto falharia pela metade, então nem deixa começar.
  nsExec::ExecToStack 'cmd /c tasklist /FI "IMAGENAME eq ${APP_EXE}" /NH | find /I "${APP_EXE}"'
  Pop $0
  Pop $1
  StrCmp $0 "0" 0 install_ready
  IfSilent 0 install_warn
  SetErrorLevel 2
  Quit
install_warn:
  Abort "Feche o TudoMKV antes de instalar esta versão."
install_ready:
FunctionEnd

Section "TudoMKV (obrigatório)" SecMain
  SectionIn RO

  SetOutPath "$INSTDIR"
  File "${SOURCE}\${APP_EXE}"
  File "${SOURCE}\LEIA-ME.txt"
  File "${SOURCE}\LICENSE.txt"

  SetOutPath "$INSTDIR\runtimes\ffmpeg"
  File "${SOURCE}\runtimes\ffmpeg\*.*"

  ; O CreateShortCut usa a pasta corrente ($OUTDIR) como "Iniciar em", então ela volta
  ; para a raiz antes dos atalhos.
  SetOutPath "$INSTDIR"

  CreateShortCut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" \
    "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "${APP_DESCRIPTION}"

  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout" "${APP_HOMEPAGE}"
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1

  ; Tamanho instalado, em KB, como o Windows mostra em "Aplicativos e recursos".
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" "$0"

  WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

Section "Atalho na Área de Trabalho" SecDesktop
  CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" \
    "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "${APP_DESCRIPTION}"
SectionEnd

Function un.onInit
  SetShellVarContext current
  nsExec::ExecToStack 'cmd /c tasklist /FI "IMAGENAME eq ${APP_EXE}" /NH | find /I "${APP_EXE}"'
  Pop $0
  Pop $1
  StrCmp $0 "0" 0 uninstall_ready
  IfSilent 0 uninstall_warn
  SetErrorLevel 2
  Quit
uninstall_warn:
  Abort "Feche o TudoMKV antes de desinstalar."
uninstall_ready:
FunctionEnd

Section "Uninstall"
  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  DeleteRegKey HKCU "${UNINST_KEY}"

  ; O desinstalador roda de uma cópia em %TEMP%, então a pasta pode ser apagada
  ; inteira, com o FFmpeg dentro.
  SetOutPath "$TEMP"
  Delete "$INSTDIR\uninstall.exe"
  RMDir /r "$INSTDIR"
SectionEnd

LangString DESC_SecMain ${LANG_PORTUGUESEBR} "O programa e o FFmpeg (ffmpeg.exe, ffprobe.exe e as DLLs)."
LangString DESC_SecDesktop ${LANG_PORTUGUESEBR} "Cria um atalho do TudoMKV na Área de Trabalho."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} $(DESC_SecMain)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} $(DESC_SecDesktop)
!insertmacro MUI_FUNCTION_DESCRIPTION_END
