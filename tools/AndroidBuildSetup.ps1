function Test-AndroidBuildInteractive([bool]$NoPause, [bool]$NonInteractive) {
    return !$NoPause -and !$NonInteractive -and [Environment]::UserInteractive -and ![Console]::IsInputRedirected
}

function Confirm-AndroidBuildAction([string]$Message, [bool]$Interactive) {
    if (!$Interactive) { return $false }
    $answer = Read-Host ($Message + ' [y/N]')
    return $answer -and $answer.Trim() -match '^(y|yes|예|네)$'
}

function Read-AndroidBuildToolPath([ValidateSet('AndroidSdk', 'JavaSdk')][string]$Tool) {
    $label = if ($Tool -eq 'AndroidSdk') { '기존 Android SDK 폴더' } else { '기존 JDK 21 폴더 (bin의 상위 폴더)' }
    while ($true) {
        $answer = Read-Host ($label + ' 경로를 붙여넣어줘. Enter는 건너뛰기')
        if (!$answer -or !$answer.Trim()) { return $null }
        try {
            $path = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($answer.Trim().Trim('"').Trim("'")))
            if (!(Test-Path -LiteralPath $path -PathType Container)) {
                Write-Host '그 폴더를 찾지 못했어. 경로를 다시 확인해줘.'
                continue
            }
            if ($Tool -eq 'JavaSdk') {
                $version = Get-AndroidBuildJavaVersion $path
                if (!$version -or $version.Major -ne 21) {
                    Write-Host ('JDK 21 폴더가 필요해. 확인된 버전: ' + $(if ($version) { $version.Text } else { '없음' }))
                    continue
                }
            }
            return $path
        } catch { Write-Host ('경로를 읽지 못했어: ' + $_.Exception.Message) }
    }
}

function Select-AndroidBuildTools {
    param($Tools, [bool]$Interactive, [bool]$InstallRequested)
    $sdk = $Tools.SdkPath
    $java = $Tools.JavaPath
    $sdkReady = $sdk -and (Test-Path -LiteralPath $sdk -PathType Container)
    $javaReady = $Tools.JavaVersion -and $Tools.JavaVersion.Major -eq 21
    if (!$sdkReady -and !$InstallRequested) {
        Write-Host 'Android SDK 경로를 찾지 못했어. Studio의 Tools > SDK Manager에서 기존 경로를 확인할 수 있어.'
        if (Confirm-AndroidBuildAction '기존 SDK 경로를 이 창에서 지정할까?' $Interactive) {
            $selected = Read-AndroidBuildToolPath AndroidSdk
            if ($selected) { $sdk = $selected; $sdkReady = $true }
        }
    }
    if (!$javaReady -and !$InstallRequested) {
        foreach ($found in $Tools.IncompatibleJava) { Write-Host "확인한 다른 JDK: $found" }
        if ($java) { Write-Host "지정한 JDK 경로: $java" }
        Write-Host '기존 JDK 21을 찾지 못했어. Studio 설치 폴더의 jbr도 사용할 수 있어.'
        if (Confirm-AndroidBuildAction '기존 JDK 21 경로를 이 창에서 지정할까?' $Interactive) {
            $selected = Read-AndroidBuildToolPath JavaSdk
            if ($selected) { $java = $selected; $javaReady = $true }
        }
    }
    if ((!$sdkReady -or !$javaReady) -and !$Interactive -and !$InstallRequested) {
        Show-AndroidBuildStudioGuidance
        throw '빌드 도구가 부족해. 대화형으로 실행하거나 기존 경로와 설치 옵션을 지정해줘.'
    }
    if (!$sdk) { $sdk = Get-AndroidBuildDefaultToolPath AndroidSdk }
    if (!$javaReady) {
        # Preserve custom destinations for an explicitly requested fresh installation.
        # Existing incompatible JDKs, including Studio's jbr, are never overwritten.
        if ($InstallRequested -and $java) {
            foreach ($executable in @('bin/java', 'bin/java.exe', 'bin/javac', 'bin/javac.exe')) {
                if (Test-Path -LiteralPath (Join-Path $java $executable) -PathType Leaf) {
                    throw '지정한 기존 JDK에서 21 버전을 확인하지 못했어. JDK 21 또는 새 설치 폴더를 지정해줘.'
                }
            }
        }
        if (!$InstallRequested -or !$java) { $java = Get-AndroidBuildDefaultToolPath JavaSdk }
    }
    return [pscustomobject]@{
        SdkPath = [IO.Path]::GetFullPath($sdk)
        JavaPath = [IO.Path]::GetFullPath($java)
        Ready = $sdkReady -and $javaReady
        InstallJava = !$javaReady
    }
}

function Test-AndroidBuildPackage {
    param([string]$Sdk, [string]$Package)
    $directory = Join-Path $Sdk $Package
    if ($Package -like 'platforms/*') { return Test-Path -LiteralPath (Join-Path $directory 'android.jar') -PathType Leaf }
    if ($Package -like 'build-tools/*') {
        foreach ($tool in @('aapt2', 'zipalign')) {
            if (!(Test-Path -LiteralPath (Join-Path $directory ($tool + '.exe')) -PathType Leaf) -and !(Test-Path -LiteralPath (Join-Path $directory $tool) -PathType Leaf)) { return $false }
        }
        return Test-Path -LiteralPath (Join-Path $directory 'lib/apksigner.jar') -PathType Leaf
    }
    if ($Package -eq 'platform-tools') {
        return (Test-Path -LiteralPath (Join-Path $directory 'adb.exe') -PathType Leaf) -or (Test-Path -LiteralPath (Join-Path $directory 'adb') -PathType Leaf)
    }
    if ($Package -like 'cmdline-tools/*') {
        foreach ($root in @($directory, (Join-Path $Sdk 'cmdline-tools/latest'))) {
            if ((Test-Path -LiteralPath (Join-Path $root 'bin/sdkmanager.bat') -PathType Leaf) -or (Test-Path -LiteralPath (Join-Path $root 'bin/sdkmanager') -PathType Leaf)) { return $true }
        }
        return $false
    }
    return Test-Path -LiteralPath (Join-Path $directory 'source.properties') -PathType Leaf
}

function Get-AndroidBuildMissingPackages {
    param([string]$Sdk, [object[]]$Dependencies, [switch]$BuildOnly)
    foreach ($dependency in $Dependencies) {
        # The installer also lists SDK Manager / adb, which are not needed to produce an APK.
        if ($BuildOnly -and $dependency.Identity -notlike 'platforms/*' -and $dependency.Identity -notlike 'build-tools/*') { continue }
        if (!(Test-AndroidBuildPackage -Sdk $Sdk -Package $dependency.Identity)) { $dependency.Identity }
    }
}

function Confirm-AndroidBuildInstallation {
    param($Tools, [string[]]$MissingPackages, [bool]$Interactive, [bool]$InstallRequested, [bool]$LicensesAccepted)
    Write-Host ''
    Write-Host "SDK 설치/추가 위치: $($Tools.SdkPath)"
    Write-Host "JDK 위치: $($Tools.JavaPath)"
    if ($Tools.InstallJava) { Write-Host '이 위치에 빌드용 JDK 21을 설치할 거야.' }
    else { Write-Host '기존 JDK 21을 사용할 거야.' }
    if ($MissingPackages.Count -gt 0) { Write-Host ('필요한 Android SDK 구성 요소: ' + ($MissingPackages -join ', ')) }
    if ($InstallRequested -and $LicensesAccepted) { return $true }
    if (!$Interactive) {
        Write-Host '설치를 진행하려면 -InstallDependencies와 -AcceptAndroidSdkLicenses를 함께 지정해줘.'
        return $false
    }
    if (!$LicensesAccepted) {
        Write-Host 'Android SDK 라이선스: https://developer.android.com/studio/terms'
        return Confirm-AndroidBuildAction '라이선스에 동의하고 위 도구를 설치한 뒤 빌드를 계속할까?' $Interactive
    }
    return Confirm-AndroidBuildAction '위 도구를 설치한 뒤 빌드를 계속할까?' $Interactive
}
