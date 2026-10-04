@echo off
setlocal
cd /d "%~dp0"
dotnet build editor\Editor.slnx -c Release -p:EngineTargetFramework=net48 -p:UseSharedCompilation=false -m:1 --disable-build-servers --nologo -v:minimal
if errorlevel 1 exit /b 1
set "editor_revision=unknown"
for /f %%i in ('git rev-parse HEAD 2^>nul') do set "editor_revision=%%i"
dotnet msbuild editor\Publish.proj -t:Publish -p:Revision=%editor_revision% -nologo -v:minimal
exit /b %errorlevel%
