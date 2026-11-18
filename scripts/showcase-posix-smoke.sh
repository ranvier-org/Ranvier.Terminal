#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$root"
artifacts="$root/artifacts/showcase-posix"
dotnet build examples/Showcase/Showcase.fsproj -c Release --artifacts-path "$artifacts" -p:NuGetAudit=false -p:RanvierTrace=false
dotnet "$artifacts/bin/Showcase/release/Showcase.dll" --verify
stty rows 40 cols 120
timeout --foreground 30 dotnet "$artifacts/bin/Showcase/release/Showcase.dll" --smoke --report="$root/artifacts/showcase-posix-smoke.txt"
echo 'PASS native POSIX showcase pages, child input, terminal dimensions and cleanup'
