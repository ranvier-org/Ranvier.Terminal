#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
name=${1:-subprocess-posix-linux}
report="$root/performance/$name.txt"
artifacts="$root/artifacts/posix-verification"
if [ -e "$report" ]; then
    echo "Evidence already exists: $report" >&2
    exit 1
fi
cd "$root"
dotnet build Ranvier.Terminal.slnx -c Release -m:1 --artifacts-path "$artifacts" -p:NuGetAudit=false -p:RanvierTrace=false
dotnet "$artifacts/bin/Ranvier.Terminal.Tests/release/Ranvier.Terminal.Tests.dll"
python3 tools/HostProbe/posix.py "$artifacts/bin/HostProbe/release/HostProbe.dll"
dotnet "$artifacts/bin/SubprocessProbe/release/SubprocessProbe.dll" "$report"
cat "$report"
if [ "$(uname -s)" = Linux ]; then
    dotnet "$artifacts/bin/SubprocessProbe/release/SubprocessProbe.dll" --verify-ignored-sigchld
fi
dotnet "$artifacts/bin/Utilities/release/Utilities.dll" --verify
dotnet test Ranvier.Terminal.slnx -c Release --no-build --artifacts-path "$artifacts" -p:NuGetAudit=false -p:RanvierTrace=false
