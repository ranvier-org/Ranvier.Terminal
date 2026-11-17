$env:RanvierTrace = 'false'
$env:NuGetAudit = 'false'
$env:BuildInParallel = 'false'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
dotnet watch --non-interactive --project "$projectRoot\examples\Showcase\Showcase.fsproj" --no-launch-profile --no-restore -- --verify
