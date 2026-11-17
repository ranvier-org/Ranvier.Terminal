$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
dotnet build "$projectRoot\Ranvier.Terminal.slnx" -c Release -m:1 -p:NuGetAudit=false -p:RanvierTrace=false --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet "$projectRoot\tests\Ranvier.Terminal.Tests\bin\Release\net10.0\Ranvier.Terminal.Tests.dll"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet "$projectRoot\examples\Dashboard\bin\Release\net10.0\Dashboard.dll" --headless
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet "$projectRoot\examples\Showcase\bin\Release\net10.0\Showcase.dll" --verify
exit $LASTEXITCODE
