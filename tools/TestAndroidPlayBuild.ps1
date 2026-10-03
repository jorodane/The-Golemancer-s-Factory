$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AndroidPlayBuild.ps1')
$root = Split-Path $PSScriptRoot -Parent
foreach ($file in @('AndroidPlayBuild.ps1', 'BuildEditorAndroid.ps1')) {
    $tokens = $null; $errors = $null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file), [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw ($errors | Out-String) }
}
$directory = Join-Path ([IO.Path]::GetTempPath()) ('ConfectorySigningTest-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
try {
    $key = Join-Path $directory 'upload.jks'
    $password = Join-Path $directory 'password.txt'
    [IO.File]::WriteAllText($key, 'fixture; not a signing key')
    [IO.File]::WriteAllText($password, 'synthetic-secret-+;%,')
    $arguments = @{ Interactive = $false; ProjectRoot = $root; ApplicationId = 'com.example.confectory'; VersionCode = 42; VersionName = '1.2.3'; KeyStore = $key; KeyAlias = 'upload'; StorePasswordFile = $password; KeyPasswordFile = $password }
    $signing = Initialize-AndroidPlaySigning @arguments
    $properties = $signing.Properties -join "`n"
    if ($properties.Contains('synthetic-secret') -or !$properties.Contains('-p:AndroidSigningStorePass=file:') -or !$properties.Contains('-p:ApplicationVersion=42')) { throw 'Signing property/password forwarding failed.' }
    foreach ($change in @(@{ ApplicationId = 'invalid id' }, @{ VersionName = '1;bad' }, @{ KeyAlias = 'bad,alias' }, @{ KeyStore = (Join-Path $directory 'missing.jks') }, @{ StorePasswordFile = '' })) {
        $candidate = $arguments.Clone()
        foreach ($name in $change.Keys) { $candidate[$name] = $change[$name] }
        $rejected = $false
        try { Initialize-AndroidPlaySigning @candidate | Out-Null } catch { $rejected = $true }
        if (!$rejected) { throw 'Invalid signing configuration was accepted.' }
    }
    if (!(Test-Path $password)) { throw 'Caller-owned password file was removed.' }
    Write-Host 'Android Play build parser and noninteractive signing configuration checks passed.'
} finally { Remove-Item $directory -Recurse -Force }
