param([switch]$NoWatch)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$artifacts = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$report = Join-Path $artifacts 'showcase-live.txt'
Set-Content -LiteralPath $report -Value 'Starting showcase host...'
$Host.UI.RawUI.WindowTitle = 'Ranvier.Terminal — Feature Showcase'
if ($NoWatch) {
    dotnet "$projectRoot\examples\Showcase\bin\Release\net10.0\Showcase.dll" "--report=$report"
} else {
    $env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = 'true'
    $env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER = 'true'
    $env:RanvierTrace = 'false'
    $env:NuGetAudit = 'false'
    $env:BuildInParallel = 'false'
    dotnet watch --non-interactive --project "$projectRoot\examples\Showcase\Showcase.fsproj" --no-launch-profile --no-restore -- "--report=$report" 2> "$artifacts\showcase-host-errors.txt"
    if ($LASTEXITCODE -ne 0) {
        Add-Content -LiteralPath $report -Value "Development host failed with exit code $LASTEXITCODE; starting the verified Release build."
        dotnet "$projectRoot\examples\Showcase\bin\Release\net10.0\Showcase.dll" "--report=$report"
    }
}
if ($LASTEXITCODE -ne 0) {
    Write-Host 'The showcase stopped with an error.'
    Read-Host 'Press Enter to close'
}
