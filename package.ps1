param([Parameter(Mandatory=$true)][ValidatePattern('^Kinetik(?:Calendar|Clock|Dock|Feeds|Focus|Hub|Notes|Weather)?$')][string]$App)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$stage = Join-Path $PSScriptRoot ('.package-' + [Guid]::NewGuid().ToString('N'))
$candidate = Join-Path $PSScriptRoot ($App + '.candidate.zip')
$archive = Join-Path $PSScriptRoot ($App + '.zip')
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($name in @($App + '.exe', 'README.md', 'LICENSE')) {
        if (-not (Test-Path -LiteralPath $name -PathType Leaf)) { throw "Missing package input: $name" }
        Copy-Item -LiteralPath $name -Destination $stage
    }
    if ($App -eq 'Kinetik') {
        if (-not (Test-Path -LiteralPath 'lib\LibreHardwareMonitorLib.dll')) { throw 'Missing sensor library' }
        New-Item -ItemType Directory -Path (Join-Path $stage 'lib') | Out-Null
        Get-ChildItem -LiteralPath 'lib' -Filter '*.dll' | Copy-Item -Destination (Join-Path $stage 'lib')
    }
    $files = [ordered]@{}
    Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        $files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage ($App + '.exe'))).FileVersion
    $manifest = [ordered]@{ app = $App; version = $version; files = $files }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'release-manifest.json') -Encoding UTF8
    $files.GetEnumerator() | ForEach-Object { $_.Value + '  ' + $_.Key } | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS') -Encoding ASCII
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $candidate -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($candidate)
    try {
        foreach ($file in $files.GetEnumerator()) {
            $entry = @($zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $file.Key })
            if ($entry.Count -ne 1) { throw "Missing or duplicate package entry: $($file.Key)" }
            $stream = $entry[0].Open(); $hash = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $hash.Dispose() }
            if ($actual -ne $file.Value) { throw "Package checksum mismatch: $($file.Key)" }
        }
    } finally { $zip.Dispose() }
    if (Test-Path -LiteralPath $archive) {
        $old = Join-Path $PSScriptRoot '..\Old Versions\releases'
        New-Item -ItemType Directory -Path $old -Force | Out-Null
        Copy-Item -LiteralPath $archive -Destination (Join-Path $old ($App + '-' + [Guid]::NewGuid().ToString('N') + '.zip'))
    }
    Move-Item -LiteralPath $candidate -Destination $archive -Force
    $zipHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    ($zipHash + '  ' + $App + '.zip') | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ASCII
    Copy-Item -LiteralPath (Join-Path $stage 'release-manifest.json') -Destination 'release-manifest.json' -Force
    Write-Host "Verified $archive ($version)"
    Write-Host ('SHA-256 of `' + $App + '.exe`: `' + $files[($App + '.exe')] + '`')
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    if (Test-Path -LiteralPath $candidate) { Remove-Item -LiteralPath $candidate -Force }
}
