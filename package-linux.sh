#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ "${1:-}" != "--skip-build" ]]; then ./build-linux.sh --test; fi
if ! git diff --quiet HEAD || [[ -n "$(git ls-files --others --exclude-standard)" ]]; then
    echo 'Commit intended source changes before packaging.' >&2; exit 1
fi
version="$(python3 -c 'import xml.etree.ElementTree as E; print(E.parse("Desktop/Desktop.csproj").findtext("PropertyGroup/Version"))')"
test -x dist/linux-x64/SkullgirlsModManager
mkdir -p artifacts
# Only the publish directory is distributed; never include library or game data.
tar -czf "artifacts/SkullgirlsModManager-$version-linux-x64.tar.gz" -C dist/linux-x64 .
git archive --format=zip --output="artifacts/SkullgirlsModManager-$version-source.zip" HEAD
sha256sum "artifacts/SkullgirlsModManager-$version-linux-x64.tar.gz" "artifacts/SkullgirlsModManager-$version-source.zip" > "artifacts/SkullgirlsModManager-$version-linux-SHA256SUMS.txt"
