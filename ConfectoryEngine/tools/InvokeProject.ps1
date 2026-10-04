param(
    [ValidateSet('build-project', 'run', 'verify', 'smoke', 'build-pack')][string]$Command = 'build-project',
    [string]$Project,
    [string]$Target,
    [string]$Dotnet = 'dotnet',
    [string]$Pack
)
$ErrorActionPreference = 'Stop'
$engineRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Project) {
    Write-Host 'Usage: Build.bat <project folder or .packproject> [target]'
    Write-Host 'Start.bat and Verify.bat accept the same explicit project path.'
    exit 2
}
$projectPath = (Resolve-Path -LiteralPath $Project).Path
Push-Location $engineRoot
try {
    & $Dotnet build 'editor/Confectory.Tool/Confectory.Tool.csproj' -c Release -p:EngineTargetFramework=net10.0 -p:UseSharedCompilation=false -m:1 --disable-build-servers --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $arguments = @((Join-Path $engineRoot 'editor/Confectory.Tool/bin/Release/net10.0/Confectory.Tool.dll'), $Command, '--project', $projectPath, '--engine-root', $engineRoot, '--dotnet', $Dotnet)
    if ($Target) { $arguments += @('--target', $Target) }
    if ($Pack) { $arguments += @('--pack', $Pack) }
    & $Dotnet @arguments
    exit $LASTEXITCODE
} finally { Pop-Location }
