#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
REPO_DIR="$(cd "$ROOT_DIR/.." && pwd)"
mkdir -p "$ROOT_DIR/dist/windows" "$ROOT_DIR/build/windows"
HOST_ARCH="$(uname -m)"

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker is required for Windows .exe cross-build (docker command not found)." >&2
  exit 1
fi
if ! docker info >/dev/null 2>&1; then
  echo "Docker daemon is not running. Start Docker Desktop/Engine and retry." >&2
  exit 1
fi

if [[ "$HOST_ARCH" == "arm64" || "$HOST_ARCH" == "aarch64" ]]; then
  echo "Windows Docker cross-build is not supported on ARM hosts due wine limitations." >&2
  echo "Use scripts/build_windows_exe.ps1 on a Windows host or GitHub Actions windows-latest." >&2
  exit 1
fi

# Uses wine-based image with pyinstaller for Windows output.
docker run --rm \
  --platform linux/amd64 \
  -v "$REPO_DIR:/work/repo" \
  -w /work/repo/LocalEndpointAgent \
  --entrypoint /bin/bash \
  cdrx/pyinstaller-windows:python3 -lc "
set -euo pipefail
export PIP_DEFAULT_TIMEOUT=180
export PIP_DISABLE_PIP_VERSION_CHECK=1
python -m pip install --upgrade pip
python -m pip install grpcio grpcio-tools protobuf psutil pyyaml pydantic pyinstaller
python -m pip install -e . --no-deps
bash scripts/generate_protos.sh
pyinstaller --noconfirm --clean --onefile --name endpoint-agent-windows.exe --paths src scripts/pyinstaller_entry.py
cp dist/endpoint-agent-windows.exe dist/windows/endpoint-agent-windows.exe
"

if [[ ! -f "$ROOT_DIR/dist/windows/endpoint-agent-windows.exe" ]]; then
  echo "Windows build failed: output file not found" >&2
  exit 1
fi

echo "Built: $ROOT_DIR/dist/windows/endpoint-agent-windows.exe"
