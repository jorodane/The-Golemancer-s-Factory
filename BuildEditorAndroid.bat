@echo off
setlocal EnableExtensions
chcp 65001 >nul
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "POWERSHELL_TELEMETRY_OPTOUT=1"
set "editor_android_no_pause="
for %%A in (%*) do if /I "%%~A"=="-NoPause" set "editor_android_no_pause=1"
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\BuildEditorAndroid.ps1" %*
set "editor_android_exit=%errorlevel%"
echo.
if "%editor_android_exit%"=="0" (
    echo Android editor build completed.
) else (
    echo Android editor build failed. Exit code: %editor_android_exit%
    echo Read the error and installation steps above. See docs\EDITOR_ANDROID.md.
)
if not defined editor_android_no_pause pause
exit /b %editor_android_exit%
