@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Building source requires the .NET 10 SDK. A packaged Golemancer.exe only needs .NET Framework 4.8.
  pause
  exit /b 1
)
dotnet build GolemancerFactory.sln -c Release -m:1 --disable-build-servers --nologo -v:minimal
if errorlevel 1 (
  pause
  exit /b 1
)
start "" "src\Golemancer.Host\bin\Release\net48\Golemancer.exe"
