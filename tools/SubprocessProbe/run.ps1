param([string]$Name = 'subprocess-native', [string]$Dll = 'C:\wk\ranvier-release\Ranvier.Terminal\tools\SubprocessProbe\bin\Release\net10.0\SubprocessProbe.dll', [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
if ($ArtifactsPath) { $Dll = Join-Path $ArtifactsPath 'bin\SubprocessProbe\release\SubprocessProbe.dll' }
$report = "C:\wk\ranvier-release\Ranvier.Terminal\performance\$Name.txt"
if (Test-Path -LiteralPath $report) { throw "Evidence exists: $report" }
$child = Start-Process dotnet -ArgumentList @($Dll, $report) -WindowStyle Hidden -PassThru
if (-not $child.WaitForExit(120000)) { $child.Kill($true); throw 'Subprocess probe timed out.' }
Get-Content -LiteralPath $report
if ($child.ExitCode -ne 0) { throw 'Subprocess probe failed.' }
