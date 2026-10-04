# Play packaging helpers; passwords are passed by file reference, never as command-line text.
function Initialize-AndroidPlaySigning {
    param([bool]$Interactive, [string]$ProjectRoot, [string]$ApplicationId,
          [int]$VersionCode, [string]$VersionName, [string]$KeyStore, [string]$KeyAlias,
          [string]$StorePasswordFile, [string]$KeyPasswordFile)
    if ($Interactive) {
        if (!$KeyStore) { $KeyStore = Read-Host '업로드 키 파일 경로 (.jks 또는 .keystore)' }
        if (!$KeyAlias) { $KeyAlias = Read-Host '업로드 키 별칭' }
    }
    if ($ApplicationId -notmatch '^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$') { throw '앱 ID는 com.example.app 형식으로 지정해줘.' }
    if ([string]::IsNullOrWhiteSpace($VersionName)) { throw '표시 버전을 지정해줘.' }
    if (!$KeyStore -or !(Test-Path -LiteralPath $KeyStore -PathType Leaf) -or !$KeyAlias) { throw '기존 업로드 키 파일과 별칭이 필요해. docs/EDITOR_ANDROID.md의 Play 빌드 안내를 확인해줘.' }
    $KeyStore = (Resolve-Path -LiteralPath $KeyStore).Path
    if ([IO.Path]::GetFileName($KeyStore) -eq 'debug.keystore') { throw '테스트용 debug.keystore는 Play 업로드 키로 사용할 수 없어.' }
    $repoPrefix = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $tempDirectory = $null
    try {
        if (!$StorePasswordFile -or !$KeyPasswordFile) {
            if (!$Interactive) { throw '자동 빌드에는 -StorePasswordFile과 -KeyPasswordFile이 필요해.' }
            $tempDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ConfectorySigning-' + [Guid]::NewGuid().ToString('N'))
            [IO.Directory]::CreateDirectory($tempDirectory) | Out-Null
            # Restrict the temporary secrets to this Windows account.
            $acl = Get-Acl -LiteralPath $tempDirectory
            $acl.SetAccessRuleProtection($true, $false)
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
            $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
            $acl.AddAccessRule($rule)
            Set-Acl -LiteralPath $tempDirectory -AclObject $acl
            foreach ($kind in @('Store', 'Key')) {
                $path = if ($kind -eq 'Store') { $StorePasswordFile } else { $KeyPasswordFile }
                if ($path) { continue }
                $secret = Read-Host "$kind 비밀번호 (화면과 로그에 표시하지 않아)" -AsSecureString
                $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
                try {
                    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
                    if (!$plain -or $plain.Contains("`n") -or $plain.Contains("`r")) { throw '비밀번호는 비어 있지 않은 한 줄이어야 해.' }
                    $path = Join-Path $tempDirectory ($kind + '.txt')
                    [IO.File]::WriteAllText($path, $plain, (New-Object Text.UTF8Encoding($false)))
                } finally {
                    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
                    $plain = $null
                    $secret.Dispose()
                }
                if ($kind -eq 'Store') { $StorePasswordFile = $path } else { $KeyPasswordFile = $path }
            }
        }
        $resolved = @()
        foreach ($path in @($KeyStore, $StorePasswordFile, $KeyPasswordFile)) {
            if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw '서명 파일을 찾지 못했어.' }
            $full = (Resolve-Path -LiteralPath $path).Path
            if ($full.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '키와 비밀번호 파일은 저장소 밖에 보관해줘.' }
            $resolved += $full
        }
        # MSBuild interprets these characters as property delimiters/escapes.
        foreach ($value in @($resolved) + @($ApplicationId, $VersionName, $KeyAlias)) {
            if ($value -match '[%;,"\r\n]') { throw '서명 경로·별칭·버전에 %, ;, 쉼표, 큰따옴표 또는 줄바꿈을 사용할 수 없어.' }
        }
        return [pscustomobject]@{
            TempDirectory = $tempDirectory
            Properties = @('-p:ConfectoryPlayBuild=true', ('-p:ApplicationId=' + $ApplicationId),
                ('-p:ApplicationVersion=' + $VersionCode), ('-p:ApplicationDisplayVersion=' + $VersionName),
                ('-p:AndroidSigningKeyStore=' + $resolved[0]), ('-p:AndroidSigningKeyAlias=' + $KeyAlias),
                ('-p:AndroidSigningStorePass=file:' + $resolved[1]), ('-p:AndroidSigningKeyPass=file:' + $resolved[2]))
        }
    } catch {
        if ($tempDirectory -and (Test-Path $tempDirectory)) { Remove-Item $tempDirectory -Recurse -Force }
        throw
    }
}

function Test-AndroidPlayBundle {
    param([string]$Bundle, [string]$JavaSdk)
    $jarsigner = Join-Path $JavaSdk 'bin/jarsigner.exe'
    if (!(Test-Path $jarsigner)) { throw 'JDK의 jarsigner.exe가 필요해.' }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $verification = @(& $jarsigner '-J-Duser.language=en' '-J-Duser.country=US' '-verify' $Bundle 2>&1 | ForEach-Object { $_.ToString() })
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    if ($code -ne 0 -or ($verification -join "`n") -notmatch 'jar verified\.') { throw 'AAB 서명 검증에 실패했어.' }
    # Upload keys are normally self-signed, so jarsigner -strict's trust-chain failure is not used.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Bundle)
    try {
        if (!$zip.GetEntry('base/manifest/AndroidManifest.xml') -or !$zip.GetEntry('BundleConfig.pb') -or !$zip.GetEntry('base/assets/Engine.zip')) { throw 'AAB에 manifest, bundle 설정 또는 통합 엔진이 없어.' }
        $libraries = @($zip.Entries | Where-Object { $_.FullName -match '^base/lib/arm64-v8a/[^/]+\.so$' })
        if (!$libraries.Count) { throw 'AAB에 arm64 네이티브 라이브러리가 없어.' }
        foreach ($entry in $libraries) {
            $stream = $entry.Open()
            $memory = New-Object IO.MemoryStream
            try { $stream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $stream.Dispose(); $memory.Dispose() }
            if ($bytes.Length -lt 64 -or $bytes[0] -ne 127 -or $bytes[1] -ne 69 -or $bytes[2] -ne 76 -or $bytes[3] -ne 70 -or $bytes[4] -ne 2 -or $bytes[5] -ne 1 -or [BitConverter]::ToUInt16($bytes, 18) -ne 183) { throw "잘못된 arm64 ELF: $($entry.FullName)" }
            $offset = [BitConverter]::ToUInt64($bytes, 32)
            $size = [BitConverter]::ToUInt16($bytes, 54)
            $count = [BitConverter]::ToUInt16($bytes, 56)
            if ($size -lt 56 -or !$count -or $offset -gt $bytes.Length -or ($count * $size) -gt ($bytes.Length - $offset)) { throw '잘못된 ELF 프로그램 헤더야.' }
            $loads = 0
            for ($index = 0; $index -lt $count; $index++) {
                $header = [int]($offset + $index * $size)
                if ([BitConverter]::ToUInt32($bytes, $header) -ne 1) { continue }
                $loads++
                $alignment = [BitConverter]::ToUInt64($bytes, $header + 48)
                $fileOffset = [BitConverter]::ToUInt64($bytes, $header + 8)
                $virtualAddress = [BitConverter]::ToUInt64($bytes, $header + 16)
                if ($alignment -lt 16384 -or ($alignment -band ($alignment - 1)) -ne 0 -or ($fileOffset % 16384) -ne ($virtualAddress % 16384)) { throw "16KB ELF 정렬이 필요해: $($entry.FullName). Android workload와 네이티브 의존성을 업데이트해줘." }
            }
            if (!$loads) { throw 'ELF 로드 세그먼트가 없어.' }
        }
        Write-Host "AAB 서명 / 엔진 포함 / arm64 ELF 16KB 정렬: 확인 완료 ($($libraries.Count)개)"
    } finally { $zip.Dispose() }
}
