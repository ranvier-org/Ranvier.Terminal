param([string]$ArtifactsPath, [int]$IntervalMs=500)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$binary = if ($ArtifactsPath) { "$ArtifactsPath\bin\SystemMonitor\release\SystemMonitor.dll" } else { "$root\examples\SystemMonitor\bin\Release\net10.0\SystemMonitor.dll" }
foreach ($mode in @('demo','live')) {
    $arguments = @($binary, '--smoke', "--interval-ms=$IntervalMs")
    if ($mode -eq 'live') { $arguments += '--live' }
    $errors = Join-Path $root "artifacts\system-monitor-$mode.stderr.txt"
    $child = Start-Process dotnet -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardError $errors -PassThru
    if (-not $child.WaitForExit(20000)) { $child.Kill($true); throw "Monitor $mode smoke timed out." }
    if ($child.ExitCode -ne 0) { Get-Content -LiteralPath $errors; throw "Monitor $mode smoke failed." }
    Write-Output "measured here: hidden native monitor $mode mode rendered and cleaned up successfully."
}
