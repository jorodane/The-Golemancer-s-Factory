@echo off
setlocal
set "launcher_exe=%~dp0StartEditor.exe"
if not exist "%launcher_exe%" (
  echo The launcher is missing. Pull the latest build or run BuildEditor.bat with the .NET 10 SDK installed.
  pause
  exit /b 1
)
if "%~1"=="" (
  start "" /D "%~dp0" "%launcher_exe%"
) else (
  start "" /D "%~dp0" "%launcher_exe%" "%~1"
)
