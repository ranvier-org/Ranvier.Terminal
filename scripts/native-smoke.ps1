$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$reportDirectory = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
$report = Join-Path $reportDirectory 'native-verification.txt'
$native = Start-Process -FilePath dotnet -ArgumentList @("$projectRoot\tests\Ranvier.Terminal.Tests\bin\Release\net10.0\Ranvier.Terminal.Tests.dll", '--native', $report) -WindowStyle Hidden -PassThru
if (-not $native.WaitForExit(15000)) { $native.Kill(); throw 'Native verification timed out.' }
Get-Content -LiteralPath $report
if ($native.ExitCode -ne 0) { throw 'Native verification failed.' }
$process = Start-Process -FilePath dotnet -ArgumentList @("$projectRoot\examples\Dashboard\bin\Release\net10.0\Dashboard.dll", '--smoke', '--sync') -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(15000)) {
    $process.Kill()
    throw 'Native terminal smoke test timed out.'
}
if ($process.ExitCode -ne 0) { throw "Native terminal smoke test failed: $($process.ExitCode)" }
Write-Output 'measured here: hidden Windows terminal session smoke test exited successfully.'
