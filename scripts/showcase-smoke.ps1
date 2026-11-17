$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$report = Join-Path $projectRoot 'artifacts\showcase-smoke.txt'
$process = Start-Process -FilePath dotnet -ArgumentList @("$projectRoot\examples\Showcase\bin\Release\net10.0\Showcase.dll", '--smoke', "--report=$report") -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'Showcase smoke test timed out.' }
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath $report
    throw "Showcase failed with exit code $($process.ExitCode)"
}
Write-Output 'measured here: hidden native showcase visited every page and exited successfully.'
