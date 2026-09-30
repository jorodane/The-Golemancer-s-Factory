@echo off
setlocal
cd /d "%~dp0"
if not exist "Builds\Windows\Golemancer.exe" (
  echo The Windows build is missing. Pull the full repository, or run Build.bat.
  pause
  exit /b 1
)
set "GOLEMANCER_ROOT=%~dp0"
start "" "Builds\Windows\Golemancer.exe"
