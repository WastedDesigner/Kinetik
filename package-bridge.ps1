# Kinetik.zip for the first releases after the rename to Kinetik Stats. Kinetik 3.2 and earlier update themselves only
# from a Kinetik.zip holding Kinetik.exe, checked against "SHA-256 of `Kinetik.exe`" in the release notes. This is the
# same build as KinetikStats.zip under the old exe name; on its first start it copies itself to KinetikStats.exe and
# carries settings, startup and data over. Attach both zips to the release and list both checksums in its notes.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$stage = Join-Path $PSScriptRoot ('.bridge-' + [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $PSScriptRoot 'Kinetik.zip'
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($name in @('KinetikStats.exe', 'README.md', 'LICENSE')) {
        if (-not (Test-Path -LiteralPath $name -PathType Leaf)) { throw "Missing package input: $name" }
    }
    Copy-Item -LiteralPath 'KinetikStats.exe' -Destination (Join-Path $stage 'Kinetik.exe')
    Copy-Item -LiteralPath 'README.md', 'LICENSE' -Destination $stage
    New-Item -ItemType Directory -Path (Join-Path $stage 'lib') | Out-Null
    Get-ChildItem -LiteralPath 'lib' -Filter '*.dll' | Copy-Item -Destination (Join-Path $stage 'lib')
    if (Test-Path -LiteralPath $archive) {
        $old = Join-Path $PSScriptRoot '..\Old Versions\releases'
        New-Item -ItemType Directory -Path $old -Force | Out-Null
        Copy-Item -LiteralPath $archive -Destination (Join-Path $old ('Kinetik-bridge-' + [Guid]::NewGuid().ToString('N') + '.zip'))
        Remove-Item -LiteralPath $archive -Force
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $archive)
    $exeHash = Get-Sha256 (Join-Path $stage 'Kinetik.exe')
    if ($exeHash -ne (Get-Sha256 'KinetikStats.exe')) { throw 'Bridge exe differs from KinetikStats.exe' }
    ((Get-Sha256 $archive) + '  Kinetik.zip') | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ASCII
    Write-Host "Verified $archive (bridge for Kinetik 3.2 and earlier)"
    Write-Host ('SHA-256 of `Kinetik.exe`: `' + $exeHash + '`')
} finally {
    $rootPath = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
    if ([IO.Path]::GetFullPath($stage).StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $stage)) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
