#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
stty rows 40 cols 120
exec dotnet "$root/artifacts/posix-verification/bin/Utilities/release/Utilities.dll" --smoke
