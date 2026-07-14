#!/usr/bin/env sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
ENTRY="$ROOT/scripts/repository-graph-vendor-entry.js"
DEST="$ROOT/src/SvnHub.Web/wwwroot/lib/repository-graph"
TMP="$(mktemp -d)"

cleanup() {
  rm -rf "$TMP"
}
trap cleanup EXIT INT TERM

mkdir -p "$DEST"
npm install --prefix "$TMP" --no-save --ignore-scripts \
  sigma@3.0.3 \
  graphology@0.26.0 \
  graphology-layout-forceatlas2@0.10.1 \
  esbuild@0.28.1

NODE_PATH="$TMP/node_modules" \
  "$TMP/node_modules/.bin/esbuild" "$ENTRY" \
  --bundle \
  --minify \
  --platform=browser \
  --target=es2020 \
  --format=iife \
  --outfile="$DEST/repository-graph-vendor.min.js"

: > "$DEST/THIRD-PARTY-NOTICES.txt"
append_license() {
  package="$1"
  version="$2"
  license="$3"
  {
    printf '%s\n' '================================================================================'
    printf '%s %s\n' "$package" "$version"
    printf '%s\n' '================================================================================'
    cat "$TMP/node_modules/$package/$license"
    printf '\n\n'
  } >> "$DEST/THIRD-PARTY-NOTICES.txt"
}

append_license sigma 3.0.3 LICENSE.txt
append_license graphology 0.26.0 LICENSE.txt
append_license graphology-layout-forceatlas2 0.10.1 LICENSE.txt
append_license graphology-utils 2.5.2 LICENSE.txt
append_license events 3.3.0 LICENSE
