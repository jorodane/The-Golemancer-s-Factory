@echo off
setlocal
if "%~1"=="" exit /b 0
set "legacy_root=%~1"
set "game_root=%~dp0"
if not exist "%legacy_root%Engine.slnx" exit /b 0
call :CopyMissing Content
if errorlevel 1 exit /b 1
call :CopyMissing Saves
if errorlevel 1 exit /b 1
exit /b 0
:CopyMissing
if not exist "%legacy_root%%~1" exit /b 0
robocopy "%legacy_root%%~1" "%game_root%%~1" /E /XC /XN /XO /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS
if errorlevel 8 (
  echo Could not copy legacy %~1. Original files were kept. Check folder permissions and try again.
  exit /b 1
)
exit /b 0
