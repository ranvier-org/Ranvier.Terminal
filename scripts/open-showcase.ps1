$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$launcher = Join-Path $projectRoot 'scripts\launch-showcase.ps1'
$terminal = Get-Command wt.exe -ErrorAction SilentlyContinue
$opened = $false
if ($terminal) {
    try {
    Start-Process -FilePath $terminal.Source -ArgumentList @('-w', 'new', '--size', '140,44', 'new-tab', '--title', '"Ranvier Terminal Showcase"', 'pwsh', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $launcher)
    $opened = $true
    } catch { Write-Host 'Windows Terminal alias unavailable; opening a console window.' }
}
if (-not $opened) {
    $windowProcess = Start-Process -FilePath pwsh -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $launcher) -PassThru
    $windowProcess.WaitForExit()
}
