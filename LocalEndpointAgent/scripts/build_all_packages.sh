#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

bash "$ROOT_DIR/scripts/build_macos_dmg.sh"
bash "$ROOT_DIR/scripts/build_linux_deb.sh"

if [[ "$(uname -s)" == "Darwin" && "$(uname -m)" == "arm64" ]]; then
  echo "Skipping windows docker build on macOS arm64 (wine/qemu incompatibility)." >&2
  echo "Use scripts/build_windows_exe.ps1 on a Windows host for .exe output." >&2
else
  bash "$ROOT_DIR/scripts/build_windows_exe.sh"
fi

echo "All packages built in $ROOT_DIR/dist"
