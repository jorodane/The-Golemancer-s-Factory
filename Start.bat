@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Please install .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)
dotnet build GolemancerFactory.sln -c Release -m:1 --disable-build-servers --nologo -v:minimal
if errorlevel 1 (
  pause
  exit /b 1
)
dotnet src\Golemancer.Host\bin\Release\net10.0\Golemancer.Host.dll --open
pause
