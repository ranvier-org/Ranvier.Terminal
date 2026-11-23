param([switch]$RefreshAssets)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docs = Join-Path $root 'docs'

function Run([string]$program, [string[]]$arguments) {
    & $program @arguments
    if ($LASTEXITCODE -ne 0) { throw "$program exited with code $LASTEXITCODE" }
}

if ($RefreshAssets) {
    Run pwsh @('-NoProfile', '-File', (Join-Path $docs 'live/architecture/build.ps1'))
    Run dotnet @('run', '--project', (Join-Path $docs 'tools/Previews.fsproj'), '-c', 'Release',
        '--artifacts-path', (Join-Path $root 'artifacts/docs-captures'))
}

Run dotnet @('run', '--project', (Join-Path $docs 'docs.fsproj'), '-c', 'Release',
    '--artifacts-path', (Join-Path $root 'artifacts/docs-build'), '--', 'build', '--strict')
