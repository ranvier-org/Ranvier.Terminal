param([switch]$RefreshAssets)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docs = Join-Path $root 'docs'

function Run([string]$program, [string[]]$arguments) {
    & $program @arguments
    if ($LASTEXITCODE -ne 0) { throw "$program exited with code $LASTEXITCODE" }
}

if ($RefreshAssets) {
    $d5 = Join-Path (Split-Path $root -Parent) 'Ranvier.D5'
    $feed = Join-Path $d5 'artifacts/fickl-feed'
    $project = Join-Path $d5 'src/Ranvier.D5/Ranvier.D5.fsproj'
    if (-not (Test-Path -LiteralPath $project)) {
        throw 'Regenerating the architecture diagram requires the Ranvier.D5 checkout beside Ranvier.Terminal.'
    }
    Run dotnet @('pack', $project, '-c', 'Release', '-p:PackageVersion=0.1.2-local.1', '-o', $feed)
    Run pwsh @('-NoProfile', '-File', (Join-Path $docs 'live/architecture/build.ps1'))
    Run dotnet @('run', '--project', (Join-Path $docs 'tools/Previews.fsproj'), '-c', 'Release',
        '--artifacts-path', (Join-Path $root 'artifacts/docs-captures'))
}

Run dotnet @('run', '--project', (Join-Path $docs 'docs.fsproj'), '-c', 'Release',
    '--artifacts-path', (Join-Path $root 'artifacts/docs-build'), '--', 'build', '--strict')
