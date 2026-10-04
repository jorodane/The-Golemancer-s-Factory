param([Parameter(Mandatory=$true)][string]$Source, [Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$destinationPath = (Resolve-Path -LiteralPath $Destination).Path
if ($sourcePath -eq $destinationPath) { throw 'Choose different source and destination folders.' }
# Explicit paths only. Copy missing local data without overwriting either copy.
foreach ($folder in @('Content', 'Saves', 'EditorPacks')) {
    $from = Join-Path $sourcePath $folder
    if (-not (Test-Path -LiteralPath $from -PathType Container)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $from -File -Recurse) {
        $relative = $file.FullName.Substring($sourcePath.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
        if (($relative -split '[\\/]') | Where-Object { $_ -match '^(?i:bin|obj|builds)$' }) { continue }
        $to = Join-Path $destinationPath $relative
        if (Test-Path -LiteralPath $to) { continue }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to)) | Out-Null
        [IO.File]::Copy($file.FullName, $to, $false)
    }
}
Write-Host 'Missing local project data copied; originals and existing destination files are preserved.'
