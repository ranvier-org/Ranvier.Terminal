#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$root"
artifacts="$root/artifacts/system-monitor-posix"
dotnet build examples/SystemMonitor/SystemMonitor.fsproj -c Release --artifacts-path "$artifacts" -p:NuGetAudit=false -p:RanvierTrace=false
dotnet "$artifacts/bin/SystemMonitor/release/SystemMonitor.dll" --verify
stty rows 60 cols 120
timeout --foreground 20 dotnet "$artifacts/bin/SystemMonitor/release/SystemMonitor.dll" --smoke
timeout --foreground 20 dotnet "$artifacts/bin/SystemMonitor/release/SystemMonitor.dll" --smoke --live
echo 'PASS native POSIX system monitor demo and live modes'
