#!/usr/bin/env bash
set -e
cd "$(dirname "$0")"
dotnet build -c Release
mkdir -p dist && rm -f dist/TurretsShootEverything.zip
cp bin/Release/netstandard2.1/TurretsShootEverything.dll thunderstore/
(cd thunderstore && zip -j ../dist/TurretsShootEverything.zip manifest.json README.md CHANGELOG.md icon.png TurretsShootEverything.dll)
echo "Upload dist/TurretsShootEverything.zip at https://thunderstore.io/package/create/"
