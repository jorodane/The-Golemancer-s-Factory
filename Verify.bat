@echo off
setlocal
cd /d "%~dp0"
dotnet build GolemancerFactory.sln -c Release -m:1 --disable-build-servers --nologo -v:minimal
if errorlevel 1 exit /b 1
dotnet tests\Golemancer.Verification\bin\Release\net10.0\Golemancer.Verification.dll
pause
