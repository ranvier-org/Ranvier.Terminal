$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet build "$root/tools/PerfBench/PerfBench.fsproj" -c Release -m:1 -p:NuGetAudit=false -p:RanvierTrace=false
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$current = "$root/tools/PerfBench/bin/Release/net10.0"
$baseline = "$root/artifacts/backlog-baseline"
Copy-Item -LiteralPath "$current/PerfBench.dll" -Destination "$baseline/PerfBench.dll"
dotnet "$baseline/PerfBench.dll" "$root/performance/backlog-before.csv" --reactive
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet "$current/PerfBench.dll" "$root/performance/backlog-after.csv" --reactive
exit $LASTEXITCODE
