param([Parameter(Mandatory=$true)][string]$App)
$ErrorActionPreference = 'Stop'
if ($App -in @('Kinetik', 'KinetikScreen')) { Write-Host "$App hardware integration requires a manual Windows check; no demo mode is available."; exit 0 }
$exe = Join-Path (Split-Path -Parent $PSScriptRoot) ($App + '.exe')
$process = Start-Process -FilePath $exe -ArgumentList '--demo' -PassThru -WindowStyle Hidden
try {
    Start-Sleep -Seconds 3
    if ($process.HasExited) { throw "$App exited before the demo smoke check completed." }
    $shutdown = [Threading.EventWaitHandle]::OpenExisting('Local\' + $App + 'Demo.Shutdown')
    try { $shutdown.Set() | Out-Null } finally { $shutdown.Dispose() }
    if (-not $process.WaitForExit(15000)) { throw "$App did not acknowledge graceful shutdown." }
    if ($process.ExitCode -ne 0) { throw "$App exited with code $($process.ExitCode)." }
    Write-Host "$App demo startup and graceful shutdown passed."
} finally { $process.Dispose() }
