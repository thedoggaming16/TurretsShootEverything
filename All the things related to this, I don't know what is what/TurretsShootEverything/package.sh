#!/usr/bin/env bash
# Builds the mod and creates the Thunderstore zip.
set -e
dotnet build -c Release
rm -rf dist && mkdir -p dist/pkg
cp bin/Release/netstandard2.1/TurretsShootEverything.dll dist/pkg/
cp thunderstore/manifest.json thunderstore/README.md thunderstore/CHANGELOG.md thunderstore/icon.png dist/pkg/
(cd dist/pkg && zip -r ../yourname-TurretsShootEverything-1.0.0.zip .)
echo "Created dist/yourname-TurretsShootEverything-1.0.0.zip"
