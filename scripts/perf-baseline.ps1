$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet build "$root\tools\PerfBench\PerfBench.fsproj" -c Release -m:1 -p:NuGetAudit=false -p:RanvierTrace=false
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$target = "$root\artifacts\perf-baseline"
if (Test-Path -LiteralPath $target) { throw 'Baseline already exists; preserve it.' }
New-Item -ItemType Directory -Path $target | Out-Null
Copy-Item -Path "$root\tools\PerfBench\bin\Release\net10.0\*" -Destination $target -Recurse
dotnet "$target\PerfBench.dll" "$root\artifacts\perf-baseline.csv"
exit $LASTEXITCODE
