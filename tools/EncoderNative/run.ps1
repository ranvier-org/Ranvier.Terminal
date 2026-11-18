$ErrorActionPreference = 'Stop'
$root = 'C:\wk\ranvier-release\Ranvier.Terminal'
$child = Start-Process dotnet -ArgumentList @("$root\tools\EncoderNative\bin\Release\net10.0\EncoderNative.dll", "$root\performance\encoder-native.txt") -WindowStyle Hidden -PassThru
if (-not $child.WaitForExit(15000)) { $child.Kill(); throw 'Hidden protocol test timeout' }
Get-Content -LiteralPath "$root\performance\encoder-native.txt"
if ($child.ExitCode -ne 0) { throw 'Hidden protocol test failed' }
