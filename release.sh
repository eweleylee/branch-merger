#!/usr/bin/env bash
# Build the release assets locally with Velopack. Does NOT touch GitHub --
# you create the release and upload the files in the GitHub UI yourself.
#
#   ./release.sh
#
# Then: GitHub -> Releases -> Draft a new release -> tag v<version> ->
# drop EVERY file from ./releases into the "Attach binaries" box -> Publish.
#
# The version comes from <Version> in backend/BranchMerger.Api.csproj -- the single
# source of truth. Bump it there, then run this. User data in %APPDATA%/BranchMerger
# is never touched by install or update.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
BACKEND="$ROOT/backend"
FRONTEND="$ROOT/frontend"
PUBLISH="$ROOT/publish"
RELEASES="$ROOT/releases"
CSPROJ="$BACKEND/BranchMerger.Api.csproj"
RID="${RID:-win-x64}"
PACK_ID="BranchMerger"
MAIN_EXE="BranchMerger.Api.exe"

# --- make sure vpk is reachable (dotnet global tools dir) ---------------------
export PATH="$PATH:$HOME/.dotnet/tools"
if ! command -v vpk >/dev/null 2>&1; then
  echo "ERROR: vpk not installed. Run:  dotnet tool install -g vpk" >&2
  exit 1
fi

# --- version from csproj ------------------------------------------------------
VERSION="$(grep -oE '<Version>[^<]+</Version>' "$CSPROJ" | head -1 | sed -E 's#</?Version>##g' | tr -d '[:space:]')"
if [ -z "$VERSION" ]; then
  echo "ERROR: could not read <Version> from $CSPROJ" >&2
  exit 1
fi
echo "==> Building Branch Merger v$VERSION ($RID)"

# --- frontend build -----------------------------------------------------------
echo "==> Building frontend (Vite)"
( cd "$FRONTEND" && npm install && npm run build )

# --- backend publish (self-contained, NOT single-file) ------------------------
echo "==> Publishing backend ($RID, self-contained)"
rm -rf "$PUBLISH"
( cd "$BACKEND" && dotnet publish -c Release -r "$RID" --self-contained true -p:PublishSingleFile=false -o "$PUBLISH" )

# --- copy built UI into wwwroot ----------------------------------------------
echo "==> Copying frontend build into wwwroot"
rm -rf "$PUBLISH/wwwroot"
mkdir -p "$PUBLISH/wwwroot"
cp -R "$FRONTEND/dist/." "$PUBLISH/wwwroot/"

# --- start fresh: wipe assets from previous versions -------------------------
# So 'releases' only ever holds the current version's files (full packages, no
# delta). Auto-update still works; updates just download the full package.
echo "==> Cleaning releases (start fresh)"
rm -rf "$RELEASES"

# --- pack: Setup.exe + *-full.nupkg + releases.win.json -----------------------
echo "==> Packing"
vpk pack \
  --packId      "$PACK_ID" \
  --packVersion "$VERSION" \
  --packDir     "$PUBLISH" \
  --mainExe     "$MAIN_EXE" \
  --packTitle   "Branch Merger" \
  --outputDir   "$RELEASES"

echo ""
echo "Done. Release assets for v$VERSION are in:  $RELEASES"
echo ""
echo "Next (manual): GitHub -> Releases -> Draft a new release"
echo "  * Tag:  v$VERSION   (must match <Version>)"
echo "  * Drop EVERY file from '$RELEASES' into the 'Attach binaries' box (not the description)"
echo "  * Publish"
