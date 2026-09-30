@echo off
setlocal
cd /d "%~dp0"
dotnet msbuild tools\BuildPacks.proj -t:Build -m:1 -nologo -v:minimal %*
exit /b %errorlevel%
