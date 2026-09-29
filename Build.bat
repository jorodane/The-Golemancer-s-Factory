@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Rebuilding source requires the .NET 10 SDK. Use Start.bat to run the checked-in build.
  pause
  exit /b 1
)
dotnet build GolemancerFactory.sln -c Release -m:1 --disable-build-servers --nologo -v:minimal
if errorlevel 1 exit /b 1
set "build_revision=unknown"
for /f %%i in ('git rev-parse HEAD 2^>nul') do set "build_revision=%%i"
dotnet msbuild tools\Publish.proj -t:Publish -p:Configuration=Release -p:SourceRevision=%build_revision% -nologo -v:minimal
if errorlevel 1 exit /b 1
echo Ready. Start.bat launches the updated build; Verify.bat checks it.
