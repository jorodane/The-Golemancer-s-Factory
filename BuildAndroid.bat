@echo off
setlocal
cd /d "%~dp0"
dotnet build Golemancer.Headless.slnf -c Release -p:GolemancerTargetFramework=net10.0 -m:1 --disable-build-servers --nologo -v:minimal
if errorlevel 1 exit /b 1
dotnet tests\Golemancer.Verification\bin\Release\net10.0\Golemancer.Verification.dll
if errorlevel 1 exit /b 1
dotnet build src\Golemancer.Android\Golemancer.Android.csproj -c Release -p:GolemancerTargetFramework=net10.0 -m:1 --disable-build-servers --nologo -v:minimal %*
if errorlevel 1 exit /b 1
set "android_revision=unknown"
for /f %%i in ('git rev-parse HEAD 2^>nul') do set "android_revision=%%i"
dotnet msbuild tools\PublishAndroid.proj -t:Publish -p:SourceRevision=%android_revision% -nologo -v:minimal
if errorlevel 1 exit /b 1
echo APK ready in Builds\Android.
