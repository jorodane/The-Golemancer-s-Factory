@echo off
setlocal
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "POWERSHELL_TELEMETRY_OPTOUT=1"
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\BuildEditorAndroid.ps1" %*
exit /b %errorlevel%
