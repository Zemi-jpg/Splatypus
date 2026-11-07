#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Restore the pinned 8.34 SDK, independently of the locally installed Rhino version.
dotnet build Source/RhinoSplat.Grasshopper.Mac.csproj -c Release \
  -o "$PWD/dist/macos"
printf '\nBuilt Mac workflow in %s/dist/macos\n' "$PWD"
