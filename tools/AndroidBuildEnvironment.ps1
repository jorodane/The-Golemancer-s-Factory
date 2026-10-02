function Get-AndroidBuildStudioRoots {
    $roots = @()
    foreach ($base in @($env:ProgramFiles, [Environment]::GetEnvironmentVariable('ProgramFiles(x86)'))) {
        if ($base) { $roots += Join-Path $base 'Android/Android Studio' }
    }
    if ($env:LOCALAPPDATA) { $roots += Join-Path $env:LOCALAPPDATA 'Programs/Android Studio' }
    $studioCommand = Get-Command studio64.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($studioCommand) { $roots += Split-Path (Split-Path $studioCommand.Source -Parent) -Parent }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        foreach ($key in @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')) {
            $installed = Get-ItemProperty $key -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like 'Android Studio*' -and $_.InstallLocation }
            foreach ($item in $installed) { $roots += $item.InstallLocation }
        }
    }
    return @($roots | Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -Unique)
}

function Get-AndroidBuildJavaVersion([string]$Directory) {
    if (!$Directory) { return $null }
    $compiler = Join-Path $Directory 'bin/javac.exe'
    if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) { $compiler = Join-Path $Directory 'bin/javac' }
    $java = Join-Path $Directory 'bin/java.exe'
    if (!(Test-Path -LiteralPath $java -PathType Leaf)) { $java = Join-Path $Directory 'bin/java' }
    if (!(Test-Path -LiteralPath $compiler -PathType Leaf) -or !(Test-Path -LiteralPath $java -PathType Leaf)) { return $null }
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $versionText = (& $compiler -version 2>&1 | ForEach-Object { $_.ToString() }) -join ' '
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($code -eq 0 -and $versionText -match 'javac\s+(\d+)(\S*)') {
        return [pscustomobject]@{ Major = [int]$Matches[1]; Text = $versionText }
    }
    return $null
}

function Get-AndroidBuildDefaultToolPath([ValidateSet('AndroidSdk', 'JavaSdk')][string]$Tool) {
    $localData = $env:LOCALAPPDATA
    if (!$localData) { $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData) }
    if (!$localData) { throw '사용자 도구 설치 폴더를 찾지 못했어. 경로를 직접 지정해줘.' }
    if ($Tool -eq 'AndroidSdk') { return Join-Path $localData 'Android/Sdk' }
    return Join-Path $localData 'PackEngine/BuildTools/jdk-21'
}

function Resolve-AndroidBuildEnvironment {
    param([string]$RequestedSdk, [string]$RequestedJava, [bool]$SdkExplicit, [bool]$JavaExplicit)
    $studioRoots = @(Get-AndroidBuildStudioRoots)
    $sdkCandidates = @()
    if ($RequestedSdk) { $sdkCandidates += [pscustomobject]@{ Path = $RequestedSdk; Origin = '지정 경로 / ANDROID_HOME' } }
    if (!$SdkExplicit) {
        if ($env:ANDROID_SDK_ROOT) { $sdkCandidates += [pscustomobject]@{ Path = $env:ANDROID_SDK_ROOT; Origin = 'ANDROID_SDK_ROOT' } }
        if ($env:LOCALAPPDATA) { $sdkCandidates += [pscustomobject]@{ Path = (Join-Path $env:LOCALAPPDATA 'Android/Sdk'); Origin = 'Android Studio 기본 SDK' } }
        foreach ($base in @($env:ProgramFiles, [Environment]::GetEnvironmentVariable('ProgramFiles(x86)'))) {
            if ($base) { $sdkCandidates += [pscustomobject]@{ Path = (Join-Path $base 'Android/android-sdk'); Origin = '기존 Android SDK' } }
        }
        $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        if ($userProfile) {
            $sdkCandidates += [pscustomobject]@{ Path = (Join-Path $userProfile 'Android/Sdk'); Origin = '기존 Android SDK' }
            $sdkCandidates += [pscustomobject]@{ Path = (Join-Path $userProfile 'Library/Android/sdk'); Origin = '기존 Android SDK' }
        }
        $sdkCandidates += [pscustomobject]@{ Path = (Get-AndroidBuildDefaultToolPath AndroidSdk); Origin = '사용자 Android SDK' }
    }
    $sdk = $null
    foreach ($candidate in $sdkCandidates) {
        if ($SdkExplicit -or (Test-Path -LiteralPath $candidate.Path -PathType Container)) { $sdk = $candidate; break }
    }
    $javaCandidates = @()
    if ($RequestedJava) { $javaCandidates += [pscustomobject]@{ Path = $RequestedJava; Origin = '지정 경로 / JAVA_HOME' } }
    if (!$JavaExplicit) {
        foreach ($studio in $studioRoots) { $javaCandidates += [pscustomobject]@{ Path = (Join-Path $studio 'jbr'); Origin = 'Android Studio 내장 JDK' } }
        if ($env:STUDIO_JDK) { $javaCandidates += [pscustomobject]@{ Path = $env:STUDIO_JDK; Origin = 'STUDIO_JDK' } }
        $javaCandidates += [pscustomobject]@{ Path = (Get-AndroidBuildDefaultToolPath JavaSdk); Origin = '사용자 빌드용 JDK' }
    }
    $java = $null
    $javaVersion = $null
    $incompatibleJava = @()
    foreach ($candidate in $javaCandidates) {
        $version = Get-AndroidBuildJavaVersion $candidate.Path
        if ($JavaExplicit -or ($version -and $version.Major -eq 21)) { $java = $candidate; $javaVersion = $version; break }
        if ($version) { $incompatibleJava += ($candidate.Path + ' (' + $version.Text + ')') }
    }
    return [pscustomobject]@{
        StudioRoots = $studioRoots
        SdkPath = if ($sdk) { [IO.Path]::GetFullPath($sdk.Path) } else { $null }
        SdkOrigin = if ($sdk) { $sdk.Origin } else { $null }
        JavaPath = if ($java) { [IO.Path]::GetFullPath($java.Path) } else { $null }
        JavaOrigin = if ($java) { $java.Origin } else { $null }
        JavaVersion = $javaVersion
        IncompatibleJava = $incompatibleJava
    }
}

function Show-AndroidBuildStudioGuidance {
    Write-Host ''
    Write-Host 'Android Studio가 있다면 기존 Android SDK와 내장 JDK 21을 재사용할 수 있어.'
    Write-Host 'SDK 경로: Android Studio > Tools > SDK Manager > Android SDK Location'
    Write-Host 'JDK 경로: Android Studio 설치 폴더의 jbr (JDK 21인지 확인)'
    Write-Host '다른 위치에 설치했다면 다음과 같이 기존 경로를 지정해줘:'
    Write-Host '  .\BuildEditorAndroid.bat -AndroidSdk "기존 SDK 경로" -JavaSdk "기존 JDK 21 경로"'
    Write-Host '필요한 SDK Platform과 Build-Tools는 SDK Manager에서 추가할 수 있어.'
    Write-Host '설치와 실행 안내: docs/EDITOR_ANDROID.md'
}
