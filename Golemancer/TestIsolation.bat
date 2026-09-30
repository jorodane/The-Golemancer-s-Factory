@echo off
setlocal
cd /d "%~dp0"
python tools\verify-isolation.py %*
exit /b %errorlevel%
