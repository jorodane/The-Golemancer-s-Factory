@echo off
call "%~dp0Golemancer\BuildAndroid.bat" %*
exit /b %errorlevel%
