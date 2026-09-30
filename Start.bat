@echo off
setlocal
call "%~dp0Golemancer\MigrateLegacy.bat" "%~dp0"
if errorlevel 1 exit /b 1
call "%~dp0Golemancer\Start.bat" %*
exit /b %errorlevel%
