param([string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
function BinaryPath([string]$project, [string]$folder) {
    if ($ArtifactsPath) { return "$ArtifactsPath\bin\$project\release\$project.dll" }
    return "$projectRoot\$folder\bin\Release\net10.0\$project.dll"
}
$extra = if ($ArtifactsPath) { @('--artifacts-path', $ArtifactsPath) } else { @() }
dotnet build "$projectRoot\Ranvier.Terminal.slnx" -c Release -m:1 -p:NuGetAudit=false -p:RanvierTrace=false --nologo @extra
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet (BinaryPath 'Ranvier.Terminal.Tests' 'tests\Ranvier.Terminal.Tests')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet (BinaryPath 'Dashboard' 'examples\Dashboard') --headless
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet (BinaryPath 'Showcase' 'examples\Showcase') --verify
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet (BinaryPath 'Utilities' 'examples\Utilities') --verify
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet (BinaryPath 'SystemMonitor' 'examples\SystemMonitor') --verify
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet (BinaryPath 'MonitorProbe' 'tools\MonitorProbe')
exit $LASTEXITCODE
