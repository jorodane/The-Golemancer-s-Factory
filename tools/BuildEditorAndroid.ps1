param(
    [ValidateSet('arm64', 'x64')][string]$Architecture = 'arm64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$AndroidSdk = $env:ANDROID_HOME,
    [string]$JavaSdk = $env:JAVA_HOME,
    [string]$DotNet = 'dotnet',
    [switch]$InstallDependencies,
    [switch]$AcceptAndroidSdkLicenses
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'editor/PackEngine.Editor.Android/PackEngine.Editor.Android.csproj'
$output = Join-Path $projectRoot 'editor/Builds/Android'
Set-Location $projectRoot
function Invoke-DotNet([string[]]$Arguments) {
    & $DotNet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)." }
}
$version = & $DotNet --version
if ($LASTEXITCODE -ne 0 -or [int]($version.Split('.')[0]) -lt 10) { throw 'Install the .NET 10 SDK and the android workload first. See docs/EDITOR_ANDROID.md.' }
$common = @('-p:EngineTargetFramework=net10.0', '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '--nologo', '-v:minimal')
$sdk = @()
if ($AndroidSdk) { $sdk += '-p:AndroidSdkDirectory=' + [IO.Path]::GetFullPath($AndroidSdk) }
if ($JavaSdk) { $sdk += '-p:JavaSdkDirectory=' + [IO.Path]::GetFullPath($JavaSdk) }
if ($InstallDependencies) {
    if (!$AcceptAndroidSdkLicenses -or !$AndroidSdk -or !$JavaSdk) { throw 'For dependency installation specify -AndroidSdk, -JavaSdk and -AcceptAndroidSdkLicenses. See docs/EDITOR_ANDROID.md.' }
    Invoke-DotNet -Arguments (@('build', $project, '-t:InstallAndroidDependencies', '-p:AcceptAndroidSdkLicenses=true') + $common + $sdk)
}
# The sample ZIP carries both host binaries; XML and implementation source are shared.
$lab = 'editor/examples/MobileLab/PackEngine.Editor.MobileLab.csproj'
Invoke-DotNet -Arguments @('build', $lab, '-c', 'Release', '-p:EngineTargetFramework=net48', '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '--nologo', '-v:minimal')
$rid = 'android-' + $Architecture
Invoke-DotNet -Arguments (@('build', $project, '-t:SignAndroidPackage', '-c', $Configuration, ('-p:RuntimeIdentifier=' + $rid)) + $common + $sdk)
$binaryDir = Join-Path $projectRoot ('editor/PackEngine.Editor.Android/bin/' + $Configuration + '/net10.0-android/' + $rid)
$apk = Get-ChildItem $binaryDir -Filter '*-Signed.apk' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (!$apk) { throw "No signed APK was produced in $binaryDir." }
[IO.Directory]::CreateDirectory($output) | Out-Null
$destination = Join-Path $output ('PackEngine.Editor-' + $Architecture + '.apk')
Copy-Item $apk.FullName $destination -Force
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $output 'editor.lab.mobile.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath }
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    $labRoot = Join-Path $projectRoot 'editor/examples/MobileLab'
    $files = @('pack.xml', 'editor.xml', 'ui.xml', 'Commands.cs', 'PackEngine.Editor.MobileLab.csproj', 'Bin/net48/PackEngine.Editor.MobileLab.dll', 'Bin/net10.0/PackEngine.Editor.MobileLab.dll')
    foreach ($file in $files) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $labRoot $file), $file) | Out-Null }
} finally { $zip.Dispose() }
$revision = & git rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0) { $revision = 'unknown' }
@{ sourceCommit = $revision; target = 'net10.0-android'; runtime = $rid; configuration = $Configuration; deviceTested = $false; apkSha256 = (Get-FileHash $destination -Algorithm SHA256).Hash } |
    ConvertTo-Json | Set-Content (Join-Path $output 'build-info.json') -Encoding UTF8
Write-Host "APK: $destination"
Write-Host "PC/Android pack ZIP: $zipPath"
