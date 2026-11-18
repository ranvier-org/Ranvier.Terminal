param([string]$Name = 'host-native', [switch]$EdgesOnly, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$root = 'C:\wk\ranvier-release\Ranvier.Terminal'
$report = Join-Path $root "performance\$Name.txt"
if (Test-Path -LiteralPath $report) { throw "Evidence already exists: $report. Choose another Name." }
$dll = if ($ArtifactsPath) { Join-Path $ArtifactsPath 'bin\HostProbe\release\HostProbe.dll' } else { "$root\tools\HostProbe\bin\Release\net10.0\HostProbe.dll" }
$arguments = @($dll, $report)
if ($EdgesOnly) { $arguments += '--edges' }
$process = Start-Process -FilePath dotnet -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Hidden host probe timed out.' }
Get-Content -LiteralPath $report
if ($process.ExitCode -ne 0) { throw 'Hidden host probe failed.' }
