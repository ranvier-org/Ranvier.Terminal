$ErrorActionPreference = 'Stop'
$root = 'C:\wk\ranvier-release\Ranvier.Terminal'
$current = "$root/tools/PerfBench/bin/Release/net10.0"
$baseline = "$root/artifacts/backlog-baseline"
Copy-Item -LiteralPath "$current/PerfBench.dll" -Destination "$baseline/PerfBench.dll"
foreach ($run in @('before-a','after-a','after-b','before-b')) {
    $bin = if ($run.StartsWith('before')) { $baseline } else { $current }
    dotnet "$bin/PerfBench.dll" "$root/performance/gaps-$run.csv" --reactive --only=output-gaps
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
