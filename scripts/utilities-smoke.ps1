param([string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$binary = if ($ArtifactsPath) { "$ArtifactsPath\bin\Utilities\release\Utilities.dll" } else { "$root\examples\Utilities\bin\Release\net10.0\Utilities.dll" }
$layout = Start-Process dotnet -ArgumentList @($binary, '--verify', '--pty') -WindowStyle Hidden -PassThru
if (-not $layout.WaitForExit(30000)) { $layout.Kill($true); throw 'Utilities PTY layout verification timed out.' }
if ($layout.ExitCode -ne 0) { throw "Utilities PTY layout verification failed: $($layout.ExitCode)" }
$errors = Join-Path $root 'artifacts\utilities-smoke-errors.txt'
New-Item -ItemType Directory -Force -Path (Split-Path $errors) | Out-Null
$process = Start-Process dotnet -ArgumentList @($binary, '--smoke') -WindowStyle Hidden -RedirectStandardError $errors -PassThru
if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw 'Utilities native smoke timed out.' }
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath $errors
    throw "Utilities native smoke failed: $($process.ExitCode)"
}
Write-Output 'measured here: hidden utilities host exchanged input/output with its real subprocess terminal and cleaned up successfully.'
