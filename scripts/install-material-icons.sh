#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-5.35.0}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/src/SvnHub.Web/wwwroot/lib/material-icons"
ICONS_DEST="$DEST/icons"

echo "Installing material-icon-theme $VERSION into $DEST"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

TGZ_NAME="$(npm pack "material-icon-theme@$VERSION" --pack-destination "$TMP")"
TGZ_PATH="$TMP/$TGZ_NAME"

tar -xf "$TGZ_PATH" -C "$TMP"
PACKAGE_ROOT="$TMP/package"

JSON_PATH="$PACKAGE_ROOT/dist/material-icons.json"
ICONS_PATH="$PACKAGE_ROOT/icons"
if [[ ! -f "$JSON_PATH" ]]; then
  echo "Expected material-icons.json not found: $JSON_PATH" >&2
  exit 1
fi
if [[ ! -d "$ICONS_PATH" ]]; then
  echo "Expected icons folder not found: $ICONS_PATH" >&2
  exit 1
fi

mkdir -p "$DEST"
if [[ -d "$ICONS_DEST" ]]; then
  DEST_REAL="$(realpath "$DEST")"
  ICONS_REAL="$(realpath "$ICONS_DEST")"
  case "$ICONS_REAL" in
    "$DEST_REAL"/*) rm -rf "$ICONS_REAL" ;;
    *)
      echo "Refusing to delete outside material icons folder: $ICONS_REAL" >&2
      exit 1
      ;;
  esac
fi
mkdir -p "$ICONS_DEST"

cp -f "$JSON_PATH" "$DEST/material-icons.json"
cp -f "$PACKAGE_ROOT/LICENSE" "$DEST/LICENSE"
cp -f "$PACKAGE_ROOT/README.md" "$DEST/README.md"
cp -f "$ICONS_PATH/"*.svg "$ICONS_DEST/"

echo "Done."
