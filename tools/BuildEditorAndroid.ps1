param(
    [ValidateSet('arm64', 'x64')][string]$Architecture = 'arm64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$AndroidSdk = $env:ANDROID_HOME,
    [string]$JavaSdk = $env:JAVA_HOME,
    [string]$DotNet = 'dotnet',
    [switch]$InstallDependencies,
    [switch]$AcceptAndroidSdkLicenses,
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'editor/PackEngine.Editor.Android/PackEngine.Editor.Android.csproj'
$output = Join-Path $projectRoot 'editor/Builds/Android'
# The .bat wrapper owns the final keypress. -NoPause is accepted here for forwarding.
$buildExit = 0
$transcribing = $false
$logPath = $null
function Read-DotNet([string[]]$Arguments) {
    # Windows PowerShell 5.1 wraps redirected native stderr in ErrorRecord objects.
    # Collect diagnostics before inspecting the native exit code instead of aborting on stderr.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $DotNet @Arguments 2>&1 | ForEach-Object { $_.ToString() })
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    return [pscustomobject]@{ Lines = $lines; ExitCode = $code }
}
function Invoke-DotNet([string[]]$Arguments) {
    Write-Host ("> dotnet " + ($Arguments -join ' '))
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $DotNet @Arguments 2>&1 | ForEach-Object { Write-Host $_.ToString() }
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($code -ne 0) { throw "dotnet 명령이 실패했어. 종료 코드: $code" }
}
function Show-SdkInstallation {
    Write-Host ''
    Write-Host '빌드에는 .NET 10 SDK가 필요해. 실행용 Runtime만으로는 빌드할 수 없어.'
    Write-Host 'PowerShell에서 설치:'
    Write-Host '  winget install --id Microsoft.DotNet.SDK.10 --exact'
    Write-Host '또는 아래 페이지에서 .NET 10 SDK의 Windows 설치 프로그램을 받아줘:'
    Write-Host '  https://dotnet.microsoft.com/ko-kr/download/dotnet/10.0'
    Write-Host '설치가 끝나면 이 창을 닫고 BuildEditorAndroid.bat를 다시 실행해줘.'
    Write-Host '설치 확인: dotnet --list-sdks'
}
try {
    Set-Location $projectRoot
    try {
        $logBase = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [IO.Path]::GetTempPath() }
        $logDirectory = Join-Path $logBase 'PackEngine/Logs/AndroidBuild'
        [IO.Directory]::CreateDirectory($logDirectory) | Out-Null
        $logPath = Join-Path $logDirectory ('build-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $PID + '.log')
        Start-Transcript -Path $logPath -Force | Out-Null
        $transcribing = $true
    } catch {
        $logPath = $null
        Write-Host ('로그 파일을 만들 수 없어. 이 창에서 오류를 확인해줘: ' + $_.Exception.Message)
    }
    Write-Host 'Android 에디터 빌드 환경을 확인하고 있어.'
    if ($logPath) { Write-Host "로그: $logPath" }
    . (Join-Path $PSScriptRoot 'AndroidBuildEnvironment.ps1')
    $existingTools = Resolve-AndroidBuildEnvironment -RequestedSdk $AndroidSdk -RequestedJava $JavaSdk -SdkExplicit $PSBoundParameters.ContainsKey('AndroidSdk') -JavaExplicit $PSBoundParameters.ContainsKey('JavaSdk')
    $AndroidSdk = $existingTools.SdkPath
    $JavaSdk = $existingTools.JavaPath
    if ($existingTools.StudioRoots.Count -gt 0) { Write-Host ('기존 Android Studio: ' + ($existingTools.StudioRoots -join ', ')) }
    if ($AndroidSdk) { Write-Host "Android SDK: $AndroidSdk [$($existingTools.SdkOrigin)]" }
    if ($JavaSdk -and $existingTools.JavaVersion) { Write-Host "Java SDK: $JavaSdk [$($existingTools.JavaOrigin), $($existingTools.JavaVersion.Text)]" }
    $requiredSdk = (Get-Content (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    if (!(Get-Command $DotNet -ErrorAction SilentlyContinue)) {
        Show-SdkInstallation
        throw "dotnet 실행 파일을 찾지 못했어: $DotNet"
    }
    $versionResult = Read-DotNet -Arguments @('--version')
    $version = $versionResult.Lines | Where-Object { $_ -match '^\d+\.\d+\.\d+(-\S+)?$' } | Select-Object -Last 1
    if ($versionResult.ExitCode -ne 0 -or !$version) {
        $versionResult.Lines | ForEach-Object { Write-Host $_ }
        Write-Host ''
        Write-Host "이 프로젝트의 global.json이 요구하는 SDK: $requiredSdk (호환되는 .NET 10 SDK도 사용 가능)"
        $installed = Read-DotNet -Arguments @('--list-sdks')
        Write-Host '현재 dotnet에서 확인되는 SDK:'
        if ($installed.Lines.Count -gt 0) { $installed.Lines | ForEach-Object { Write-Host $_ } }
        else { Write-Host '  없음' }
        Show-SdkInstallation
        throw '프로젝트와 호환되는 .NET SDK를 찾지 못했어.'
    }
    Write-Host ".NET SDK: $version"
    $workloads = Read-DotNet -Arguments @('workload', 'list')
    if ($workloads.ExitCode -ne 0) {
        $workloads.Lines | ForEach-Object { Write-Host $_ }
        throw 'Android workload 설치 상태를 확인하지 못했어. dotnet workload list 명령의 오류를 먼저 해결해줘.'
    }
    if (!(($workloads.Lines -join [Environment]::NewLine) -match '(?m)^\s*(android|maui|maui-android)\s+\S+')) {
        Write-Host ''
        Write-Host '이 SDK에 Android 빌드 도구(workload)가 설치되지 않았어.'
        Write-Host '프로젝트 폴더의 PowerShell에서 아래 명령을 실행해줘:'
        Write-Host ("  & '" + $DotNet.Replace("'", "''") + "' workload install android")
        Write-Host '권한 오류가 나오면 관리자 권한 PowerShell에서 설치해줘.'
        Write-Host '설치 후 BuildEditorAndroid.bat를 다시 실행하면 돼.'
        throw 'Android workload 설치가 필요해.'
    }
    Write-Host 'Android workload: 확인 완료'
    if (!$InstallDependencies) {
        if (!$AndroidSdk -or !(Test-Path -LiteralPath $AndroidSdk -PathType Container)) {
            Show-AndroidBuildStudioGuidance
            throw 'Android SDK 경로를 찾지 못했어. SDK Manager의 기존 경로를 지정해줘.'
        }
        if (!$JavaSdk -or !$existingTools.JavaVersion -or $existingTools.JavaVersion.Major -ne 21) {
            foreach ($found in $existingTools.IncompatibleJava) { Write-Host "확인한 다른 JDK: $found" }
            if ($JavaSdk) { Write-Host "지정한 JDK 경로: $JavaSdk" }
            Show-AndroidBuildStudioGuidance
            throw 'JDK 21 경로를 찾지 못했어. 기존 JDK 21 경로를 지정해줘.'
        }
    }
    $common = @('-p:EngineTargetFramework=net10.0', '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '--nologo', '-v:minimal')
    $sdk = @()
    if ($AndroidSdk) { $sdk += '-p:AndroidSdkDirectory=' + [IO.Path]::GetFullPath($AndroidSdk) }
    if ($JavaSdk) { $sdk += '-p:JavaSdkDirectory=' + [IO.Path]::GetFullPath($JavaSdk) }
    if ($InstallDependencies) {
        if (!$AcceptAndroidSdkLicenses -or !$AndroidSdk -or !$JavaSdk) {
            Show-AndroidBuildStudioGuidance
            throw '의존성 설치에는 -AndroidSdk, -JavaSdk와 -AcceptAndroidSdkLicenses가 필요해. 기존 경로를 사용할 수 있어.'
        }
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
    $revision = 'unknown'
    try {
        $revision = & git rev-parse HEAD 2>$null
        if ($LASTEXITCODE -ne 0) { $revision = 'unknown' }
    } catch { $revision = 'unknown' }
    @{ sourceCommit = $revision; target = 'net10.0-android'; runtime = $rid; configuration = $Configuration; deviceTested = $false; apkSha256 = (Get-FileHash $destination -Algorithm SHA256).Hash } |
        ConvertTo-Json | Set-Content (Join-Path $output 'build-info.json') -Encoding UTF8
    Write-Host "APK: $destination"
    Write-Host "PC/Android pack ZIP: $zipPath"
} catch {
    $buildExit = 1
    Write-Host ''
    Write-Host ('빌드를 완료하지 못했어: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host '설치와 실행 안내: docs/EDITOR_ANDROID.md'
} finally {
    if ($transcribing) {
        Write-Host "전체 로그: $logPath"
        Stop-Transcript | Out-Null
    }
}
exit $buildExit
