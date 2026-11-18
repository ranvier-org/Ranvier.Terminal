param([string]$Name = 'utilities-watch')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$report = Join-Path $root "performance\$Name.txt"
$errors = Join-Path $root "performance\$Name.stderr.txt"
if (Test-Path -LiteralPath $report) { throw "Evidence already exists: $report" }
$arguments = @('watch', '--non-interactive', '--project', "$root\examples\Utilities\Utilities.fsproj", 'run', '--configuration', 'Debug', '--artifacts-path', "$root\artifacts\utility-watch", '--', '--verify')
$process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput $report -RedirectStandardError $errors -PassThru
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    $passed = $false
    while (-not $process.WaitForExit(200) -and $timer.Elapsed.TotalSeconds -lt 90) {
        if (Test-Path -LiteralPath $report) {
            $log = Get-Content -LiteralPath $report -Raw
            if ($log -match 'utilities headless verification passed') { $passed = $true; break }
            if ($log -match 'Build FAILED|error MSB|error FS') { break }
        }
    }
    Get-Content -LiteralPath $report
    if (Test-Path -LiteralPath $errors) { Get-Content -LiteralPath $errors }
    if (-not $passed) { throw 'Utilities did not pass under dotnet watch.' }
    if ((Get-Content -LiteralPath $errors -Raw) -match 'does not support Hot Reload') { throw 'The watch host fell back to restarting instead of hot reload.' }
    Write-Output 'measured here: utilities verification passed under the hot-reload watch host.'
} finally {
    if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
}
