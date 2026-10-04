@echo off
call "%~dp0BuildEditorAndroid.bat" -PlayStore %*
exit /b %errorlevel%
