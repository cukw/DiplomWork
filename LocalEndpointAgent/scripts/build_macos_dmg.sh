#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
APP_NAME="endpoint-agent-macos"
DMG_NAME="endpoint-agent-macos.dmg"
BUILD_DIR="$ROOT_DIR/build/macos"
DIST_DIR="$ROOT_DIR/dist/macos"
mkdir -p "$BUILD_DIR" "$DIST_DIR"
STAGE_DIR="$(mktemp -d "$BUILD_DIR/dmg_stage.XXXXXX")"
TMP_DMG="$DIST_DIR/$DMG_NAME.tmp.$$"
FINAL_DMG="$DIST_DIR/$DMG_NAME"

cleanup() {
  rm -rf "$STAGE_DIR" "$TMP_DMG"
}
trap cleanup EXIT

bash "$ROOT_DIR/scripts/build_macos_bin.sh"

mkdir -p "$DIST_DIR"
cp "$DIST_DIR/$APP_NAME" "$STAGE_DIR/$APP_NAME"
chmod +x "$STAGE_DIR/$APP_NAME"

rm -f "$TMP_DMG" "$FINAL_DMG"

for attempt in 1 2 3; do
  if hdiutil create \
    -volname "EndpointAgent" \
    -srcfolder "$STAGE_DIR" \
    -ov \
    -format UDZO \
    "$TMP_DMG"; then
    mv "$TMP_DMG" "$FINAL_DMG"
    echo "Built: $FINAL_DMG"
    exit 0
  fi

  echo "hdiutil create failed (attempt $attempt); retrying..." >&2
  rm -f "$TMP_DMG"
  sleep "$attempt"
done

echo "Failed to build DMG: $FINAL_DMG" >&2
exit 1
