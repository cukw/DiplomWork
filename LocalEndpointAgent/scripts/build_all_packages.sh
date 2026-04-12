#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

failed=0

if [[ "$(uname -s)" == "Darwin" ]]; then
  if ! bash "$ROOT_DIR/scripts/build_macos_dmg.sh"; then
    echo "macOS build failed" >&2
    failed=1
  fi
fi

if ! bash "$ROOT_DIR/scripts/build_linux_deb.sh"; then
  echo "Linux .deb build failed" >&2
  failed=1
fi

if [[ "$(uname -s)" == "Darwin" && "$(uname -m)" == "arm64" ]]; then
  echo "Skipping windows docker build on macOS arm64 (wine/qemu incompatibility)." >&2
  echo "Use scripts/build_windows_exe.ps1 on a Windows host for .exe output." >&2
else
  if ! bash "$ROOT_DIR/scripts/build_windows_exe.sh"; then
    echo "Windows .exe build failed" >&2
    failed=1
  fi
fi

if [[ "$failed" -ne 0 ]]; then
  echo "One or more package builds failed. Check logs above." >&2
  exit 1
fi

echo "All packages built in $ROOT_DIR/dist"
