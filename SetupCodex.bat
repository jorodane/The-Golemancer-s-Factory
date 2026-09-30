@echo off
setlocal
where npm >nul 2>nul
if errorlevel 1 (
  echo Install Node.js LTS from https://nodejs.org/ first, then run this file again.
  pause
  exit /b 1
)
set "packengine_codex_dir=%LOCALAPPDATA%\PackEngine\Codex"
call npm install --prefix "%packengine_codex_dir%" @openai/codex@0.159.2 --no-audit --no-fund
if errorlevel 1 exit /b 1
echo Codex CLI 0.159.2 is installed for PackEngine.
echo StartEditor.bat ^> Codex connect ^> ChatGPT sign in.
pause
