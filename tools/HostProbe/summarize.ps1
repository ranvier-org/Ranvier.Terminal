$lines = Get-Content -LiteralPath 'C:\wk\ranvier-release\Ranvier.Terminal\performance\host-native-first.txt'
foreach ($mode in @('poll', 'event')) {
    $values = @($lines | Where-Object { $_ -like "input_readback,$mode,*" } | ForEach-Object { [double]($_ -split 'ms=')[1] } | Sort-Object)
    [pscustomobject]@{ Mode = $mode; Samples = $values.Count; MedianMs = ($values[19] + $values[20]) / 2; P95Ms = $values[37]; MaxMs = $values[39] }
}
