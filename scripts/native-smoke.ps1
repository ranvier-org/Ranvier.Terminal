param([string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testBinary = if ($ArtifactsPath) { "$ArtifactsPath\bin\Ranvier.Terminal.Tests\release\Ranvier.Terminal.Tests.dll" } else { "$projectRoot\tests\Ranvier.Terminal.Tests\bin\Release\net10.0\Ranvier.Terminal.Tests.dll" }
$dashboardBinary = if ($ArtifactsPath) { "$ArtifactsPath\bin\Dashboard\release\Dashboard.dll" } else { "$projectRoot\examples\Dashboard\bin\Release\net10.0\Dashboard.dll" }
$reportDirectory = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
$report = Join-Path $reportDirectory 'native-verification.txt'
$native = Start-Process -FilePath dotnet -ArgumentList @($testBinary, '--native', $report) -WindowStyle Hidden -PassThru
if (-not $native.WaitForExit(15000)) { $native.Kill(); throw 'Native verification timed out.' }
Get-Content -LiteralPath $report
if ($native.ExitCode -ne 0) { throw 'Native verification failed.' }
$process = Start-Process -FilePath dotnet -ArgumentList @($dashboardBinary, '--smoke', '--sync') -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(15000)) {
    $process.Kill()
    throw 'Native terminal smoke test timed out.'
}
if ($process.ExitCode -ne 0) { throw "Native terminal smoke test failed: $($process.ExitCode)" }
Write-Output 'measured here: hidden Windows terminal session smoke test exited successfully.'
