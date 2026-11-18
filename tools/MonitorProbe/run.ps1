param([string]$Name, [string]$ArtifactsPath, [switch]$Headless, [switch]$Tall)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$report=Join-Path $root "performance\$Name.txt"
if (Test-Path -LiteralPath $report) { throw "Evidence exists: $report" }
$dll=Join-Path $ArtifactsPath 'bin\MonitorProbe\release\MonitorProbe.dll'
$mode=if ($Headless) { '--headless' } elseif ($Tall) { '--native-tall' } else { '--native' }
$child=Start-Process dotnet -ArgumentList @($dll,$mode,$report) -WindowStyle Hidden -PassThru
if (-not $child.WaitForExit(90000)) { $child.Kill($true); throw 'Monitor probe timed out.' }
Get-Content -LiteralPath $report
if ($child.ExitCode -ne 0) { throw "Monitor probe failed: $($child.ExitCode)" }
