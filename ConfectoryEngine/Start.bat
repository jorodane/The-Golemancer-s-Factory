@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\InvokeProject.ps1" -Command run -Project "%~1" -Target "%~2"
exit /b %errorlevel%
