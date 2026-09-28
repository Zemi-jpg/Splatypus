#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
source_dir="$PWD/dist/macos"
if [ "$#" -ne 1 ] || [ ! -d "$1" ]; then
  echo 'Usage: bash Install-Mac.sh "/absolute/path/to/Grasshopper/Libraries"' >&2
  echo 'Get this folder from Grasshopper: File > Special Folders > Components Folder.' >&2
  exit 1
fi
target="$1/Splatypus"
if [ -f "$1/RhinoSplat/RhinoSplat.gha" ] || [ -f "$1/RhinoSplat.gha" ] || [ -f "$target/RhinoSplat.gha" ]; then
  echo 'Move the previous RhinoSplat plugin outside the Components Folder before installing Splatypus; they share component IDs.' >&2
  exit 1
fi
for file in Splatypus.gha Splatypus.Core.dll; do
  test -f "$source_dir/$file" || { echo "Run bash Build-Mac.sh first." >&2; exit 1; }
done
mkdir -p "$target"
cp "$source_dir/Splatypus.gha" "$source_dir/Splatypus.Core.dll" "$target/"
printf 'Installed to %s. Restart Rhino.\n' "$target"
