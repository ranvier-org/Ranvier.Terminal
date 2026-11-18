param([string]$Name = 'showcase-watch')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$report = Join-Path $root "performance\$Name.txt"
$errors = Join-Path $root "performance\$Name.stderr.txt"
if (Test-Path -LiteralPath $report) { throw "Evidence already exists: $report" }
$arguments = @('watch', '--non-interactive', '--project', "$root\examples\Showcase\Showcase.fsproj", 'run', '--configuration', 'Debug', '--artifacts-path', "$root\artifacts\showcase-watch", '--', '--verify')
$child = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput $report -RedirectStandardError $errors -PassThru
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    $passed = $false
    while (-not $child.WaitForExit(200) -and $timer.Elapsed.TotalSeconds -lt 90) {
        if (Test-Path -LiteralPath $report) {
            $log = Get-Content -LiteralPath $report -Raw
            if ($log -match 'showcase verification passed') { $passed = $true; break }
            if ($log -match 'Build FAILED|error MSB|error FS') { break }
        }
    }
    Get-Content -LiteralPath $report
    if (Test-Path -LiteralPath $errors) { Get-Content -LiteralPath $errors }
    if (-not $passed) { throw 'Showcase did not pass under dotnet watch.' }
    if ((Get-Content -LiteralPath $errors -Raw) -match 'does not support Hot Reload') { throw 'Watch fell back to restarting instead of hot reload.' }
    Write-Output 'measured here: showcase verification passed under the hot-reload watch host.'
} finally {
    if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
}
