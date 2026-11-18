param([string]$Name = 'latest')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet build "$root/tools/PerfBench/PerfBench.fsproj" -c Release -m:1 -p:NuGetAudit=false -p:RanvierTrace=false
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet "$root/tools/PerfBench/bin/Release/net10.0/PerfBench.dll" "$root/artifacts/perf-$Name-full.csv"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet "$root/tools/PerfBench/bin/Release/net10.0/PerfBench.dll" "$root/artifacts/perf-$Name-reactive.csv" --reactive
exit $LASTEXITCODE
