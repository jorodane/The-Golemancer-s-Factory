@echo off
setlocal
cd /d "%~dp0"
dotnet build GolemancerFactory.sln -c Release -m:1 --disable-build-servers --nologo -v:minimal
if errorlevel 1 exit /b 1
tests\Golemancer.Verification\bin\Release\net48\Golemancer.Verification.exe
if errorlevel 1 exit /b 1
start /wait "" "src\Golemancer.Host\bin\Release\net48\Golemancer.exe" --smoke
if errorlevel 1 exit /b 1
type TestResults\windows\result.txt
pause
