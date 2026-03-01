#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

python3 -m pip install --user -e "$ROOT_DIR"
bash "$ROOT_DIR/scripts/generate_protos.sh"

mkdir -p "$ROOT_DIR/dist/macos" "$ROOT_DIR/build/macos"

python3 -m PyInstaller \
  --noconfirm \
  --clean \
  --onefile \
  --name endpoint-agent-macos \
  --paths "$ROOT_DIR/src" \
  --distpath "$ROOT_DIR/dist/macos" \
  --workpath "$ROOT_DIR/build/macos/work" \
  --specpath "$ROOT_DIR/build/macos/spec" \
  "$ROOT_DIR/scripts/pyinstaller_entry.py"

chmod +x "$ROOT_DIR/dist/macos/endpoint-agent-macos"
echo "Built: $ROOT_DIR/dist/macos/endpoint-agent-macos"
