@echo off
setlocal
cd /d "%~dp0"
if not exist "Builds\Windows\Golemancer.Verification.exe" (
  echo The verification build is missing. Pull the full repository, or run Build.bat.
  pause
  exit /b 1
)
set "GOLEMANCER_ROOT=%~dp0"
set "GOLEMANCER_SAVES=%~dp0TestResults\verification-saves"
Builds\Windows\Golemancer.Verification.exe
if errorlevel 1 exit /b 1
start /wait "" "Builds\Windows\Golemancer.exe" --smoke
if errorlevel 1 exit /b 1
type TestResults\windows\result.txt
pause
