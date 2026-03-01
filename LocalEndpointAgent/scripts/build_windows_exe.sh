#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT_DIR/dist/windows" "$ROOT_DIR/build/windows"

# Uses wine-based image with pyinstaller for Windows output.
docker run --rm \
  -v "$ROOT_DIR:/src" \
  -w /src \
  --entrypoint /bin/bash \
  cdrx/pyinstaller-windows:python3 -lc "
set -euo pipefail
python -m pip install --upgrade pip
python -m pip install grpcio protobuf psutil pyyaml
python -m pip install -e .
bash scripts/generate_protos.sh
pyinstaller --noconfirm --clean --onefile --name endpoint-agent-windows.exe --paths src scripts/pyinstaller_entry.py
cp dist/endpoint-agent-windows.exe dist/windows/endpoint-agent-windows.exe
"

if [[ ! -f "$ROOT_DIR/dist/windows/endpoint-agent-windows.exe" ]]; then
  echo "Windows build failed: output file not found" >&2
  exit 1
fi

echo "Built: $ROOT_DIR/dist/windows/endpoint-agent-windows.exe"
