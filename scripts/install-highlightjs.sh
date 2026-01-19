#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-11.9.0}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/src/SvnHub.Web/wwwroot/lib/highlightjs"

echo "Installing highlight.js $VERSION into $DEST"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

ZIP="$TMP/cdn-release.zip"
URL="https://github.com/highlightjs/cdn-release/archive/refs/tags/${VERSION}.zip"

curl -L "$URL" -o "$ZIP"
unzip -q "$ZIP" -d "$TMP"

BUILD="$TMP/cdn-release-${VERSION}/build"
if [[ ! -d "$BUILD" ]]; then
  echo "Expected build folder not found: $BUILD" >&2
  exit 1
fi

mkdir -p "$DEST/languages" "$DEST/styles"
cp -f "$BUILD/highlight.min.js" "$DEST/highlight.min.js"
cp -f "$BUILD/styles/github-dark.min.css" "$DEST/styles/github-dark.min.css"

cp -f "$BUILD/languages/"*.min.js "$DEST/languages/"

echo "Done."
