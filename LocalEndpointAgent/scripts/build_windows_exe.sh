#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
REPO_DIR="$(cd "$ROOT_DIR/.." && pwd)"
mkdir -p "$ROOT_DIR/dist/windows" "$ROOT_DIR/build/windows"
HOST_ARCH="$(uname -m)"
GATEWAY_URL="${GATEWAY_URL:-https://2.26.89.86}"
GATEWAY_TLS_INSECURE="${GATEWAY_TLS_INSECURE:-true}"
ACTIVITY_SERVICE_URL="${ACTIVITY_SERVICE_URL:-2.26.89.86:5001}"
AGENT_MANAGEMENT_URL="${AGENT_MANAGEMENT_URL:-2.26.89.86:5015}"
AGENT_AUTH_HEADER="${AGENT_AUTH_HEADER:-x-agent-token}"
AGENT_AUTH_TOKEN="${AGENT_AUTH_TOKEN:-}"

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
  -e GATEWAY_URL="$GATEWAY_URL" \
  -e GATEWAY_TLS_INSECURE="$GATEWAY_TLS_INSECURE" \
  -e ACTIVITY_SERVICE_URL="$ACTIVITY_SERVICE_URL" \
  -e AGENT_MANAGEMENT_URL="$AGENT_MANAGEMENT_URL" \
  -e AGENT_AUTH_HEADER="$AGENT_AUTH_HEADER" \
  -e AGENT_AUTH_TOKEN="$AGENT_AUTH_TOKEN" \
  -v "$REPO_DIR:/work/repo" \
  -w /work/repo/LocalEndpointAgent \
  --entrypoint /bin/bash \
  cdrx/pyinstaller-windows:python3 -lc "
set -euo pipefail
export PIP_DEFAULT_TIMEOUT=180
export PIP_DISABLE_PIP_VERSION_CHECK=1
python - <<'PY'
import os
from pathlib import Path

def esc(value: str) -> str:
    return value.replace('\\\\', '\\\\\\\\').replace(\"'\", \"\\\\'\")

Path('src/endpoint_agent/embedded_config.py').write_text(
    \"\\n\".join([
        f\"DEFAULT_GATEWAY_URL = '{esc(os.environ.get('GATEWAY_URL', 'https://2.26.89.86'))}'\",
        f\"DEFAULT_GATEWAY_TLS_INSECURE = {str(os.environ.get('GATEWAY_TLS_INSECURE', 'true')).lower() in {'1', 'true', 'yes', 'on'}}\",
        f\"DEFAULT_ACTIVITY_SERVICE_URL = '{esc(os.environ.get('ACTIVITY_SERVICE_URL', '2.26.89.86:5001'))}'\",
        f\"DEFAULT_AGENT_MANAGEMENT_URL = '{esc(os.environ.get('AGENT_MANAGEMENT_URL', '2.26.89.86:5015'))}'\",
        f\"DEFAULT_AGENT_AUTH_HEADER = '{esc(os.environ.get('AGENT_AUTH_HEADER', 'x-agent-token'))}'\",
        f\"DEFAULT_AGENT_AUTH_TOKEN = '{esc(os.environ.get('AGENT_AUTH_TOKEN', ''))}'\",
        \"\",
    ]),
    encoding='utf-8',
)
PY
trap 'rm -f src/endpoint_agent/embedded_config.py' EXIT
python -m pip install --upgrade pip
python -m pip install grpcio grpcio-tools protobuf psutil pyyaml pydantic pyinstaller
python -m pip install -e . --no-deps
bash scripts/generate_protos.sh
pyinstaller --noconfirm --clean --onefile --name endpoint-agent-windows.exe \
  --paths src \
  --collect-all grpc \
  --collect-all google.protobuf \
  --collect-all pydantic \
  --collect-all pydantic_core \
  --collect-all psutil \
  --collect-all yaml \
  --collect-submodules endpoint_agent.generated \
  --collect-submodules grpc \
  --collect-submodules google.protobuf \
  --collect-submodules pydantic \
  --collect-submodules psutil \
  --collect-submodules yaml \
  --collect-binaries grpc \
  --collect-binaries pydantic_core \
  --collect-binaries psutil \
  --hidden-import grpc._cython.cygrpc \
  --hidden-import google._upb._message \
  --hidden-import pydantic_core._pydantic_core \
  --hidden-import _yaml \
  --hidden-import _sqlite3 \
  --hidden-import _ssl \
  --hidden-import winreg \
  --hidden-import psutil._psutil_windows \
  --hidden-import select \
  --hidden-import selectors \
  --hidden-import socket \
  --hidden-import _socket \
  --hidden-import _overlapped \
  --hidden-import _multiprocessing \
  --hidden-import multiprocessing \
  --hidden-import pyexpat \
  --hidden-import xml.parsers.expat \
  --hidden-import tkinter \
  scripts/pyinstaller_entry.py
wine dist/endpoint-agent-windows.exe --help >/dev/null
wine dist/endpoint-agent-windows.exe selfcheck
cp dist/endpoint-agent-windows.exe dist/windows/endpoint-agent-windows.exe
"

if [[ ! -f "$ROOT_DIR/dist/windows/endpoint-agent-windows.exe" ]]; then
  echo "Windows build failed: output file not found" >&2
  exit 1
fi

echo "Built: $ROOT_DIR/dist/windows/endpoint-agent-windows.exe"
