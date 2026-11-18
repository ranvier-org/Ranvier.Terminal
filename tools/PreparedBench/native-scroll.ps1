$ErrorActionPreference = 'Stop'
$root = 'C:\wk\ranvier-release\Ranvier.Terminal'
$report = "$root\performance\prepared-owned-scroll.csv"
$process = Start-Process dotnet -ArgumentList @("$root\tools\PreparedBench\bin\Release\net10.0\PreparedBench.dll", '--native-scroll', $report) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Prepared native scroll timeout' }
Get-Content -LiteralPath $report
if ($process.ExitCode -ne 0) { throw 'Prepared native scroll failed' }
