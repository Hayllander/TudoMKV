@echo off
rem Primeiro passo do kit de instala??o: o pacote auto-extra?vel (IExpress) chama este
rem arquivo. S? repassa para o PowerShell, que faz o trabalho. Use /S para instalar sem
rem nenhuma janela (o script de desinstala??o tamb?m usa esse modo).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
exit /b %ERRORLEVEL%
