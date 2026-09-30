@echo off
setlocal
set "editor_exe=%~dp0editor\Builds\Windows\PackEngine.Editor.exe"
if not exist "%editor_exe%" (
  echo The editor build is missing. Run BuildEditor.bat with the .NET 10 SDK installed.
  pause
  exit /b 1
)
set "project_file=%~1"
if "%project_file%"=="" set "project_file=%~dp0Golemancer\Golemancer.packproject"
start "" /D "%~dp0" "%editor_exe%" "%project_file%"
