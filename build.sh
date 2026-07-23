#!/usr/bin/env bash
# Build the Vue frontend + C# backend into ./output as a single runnable app.
#
#   ./build.sh                 framework-dependent (needs .NET 8 runtime installed)
#   ./build.sh osx-arm64       self-contained single file (no runtime needed)
#   ./build.sh win-x64 | linux-x64 | osx-x64  are also valid RIDs
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/output"
RID="${1:-}"

echo "==> Cleaning $OUT"
rm -rf "$OUT"

echo "==> Building frontend (Vite)"
cd "$ROOT/frontend"
npm install
npm run build

echo "==> Publishing backend (.NET)"
cd "$ROOT/backend"
if [ -n "$RID" ]; then
  echo "    self-contained single file for $RID"
  dotnet publish -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$OUT"
else
  echo "    framework-dependent (requires the .NET 8 runtime)"
  dotnet publish -c Release -o "$OUT"
fi

echo "==> Copying frontend build into wwwroot"
rm -rf "$OUT/wwwroot"
mkdir -p "$OUT/wwwroot"
cp -R "$ROOT/frontend/dist/." "$OUT/wwwroot/"

echo "==> Writing launchers"
cat > "$OUT/Start.command" << 'LAUNCH'
#!/bin/bash
cd "$(dirname "$0")"
echo "Starting Branch Merger... a browser tab will open shortly."
./BranchMerger.Api
LAUNCH
cat > "$OUT/start.sh" << 'LAUNCH'
#!/bin/bash
cd "$(dirname "$0")"
echo "Starting Branch Merger... a browser tab will open shortly."
./BranchMerger.Api
LAUNCH
chmod +x "$OUT/Start.command" "$OUT/start.sh"
chmod +x "$OUT/BranchMerger.Api" 2>/dev/null || true

echo ""
echo "✅ Done. Everything is in: $OUT"
echo "   Run it:  double-click Start.command (macOS)  or  ./output/start.sh (Linux)"
echo "   Then browse to http://localhost:5080 (it also opens automatically)."
