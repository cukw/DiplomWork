#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
APP_NAME="endpoint-agent-macos"
DMG_NAME="endpoint-agent-macos.dmg"
STAGE_DIR="$ROOT_DIR/build/macos/dmg_stage"

bash "$ROOT_DIR/scripts/build_macos_bin.sh"

mkdir -p "$STAGE_DIR"
cp "$ROOT_DIR/dist/macos/$APP_NAME" "$STAGE_DIR/$APP_NAME"
chmod +x "$STAGE_DIR/$APP_NAME"

rm -f "$ROOT_DIR/dist/macos/$DMG_NAME"
hdiutil create \
  -volname "EndpointAgent" \
  -srcfolder "$STAGE_DIR" \
  -ov \
  -format UDZO \
  "$ROOT_DIR/dist/macos/$DMG_NAME"

echo "Built: $ROOT_DIR/dist/macos/$DMG_NAME"
