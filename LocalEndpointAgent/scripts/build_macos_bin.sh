#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
export ROOT_DIR
GATEWAY_URL="${GATEWAY_URL:-https://2.26.89.86}"
GATEWAY_TLS_INSECURE="${GATEWAY_TLS_INSECURE:-true}"
ACTIVITY_SERVICE_URL="${ACTIVITY_SERVICE_URL:-2.26.89.86:5001}"
AGENT_MANAGEMENT_URL="${AGENT_MANAGEMENT_URL:-2.26.89.86:5015}"
AGENT_AUTH_HEADER="${AGENT_AUTH_HEADER:-x-agent-token}"
AGENT_AUTH_TOKEN="${AGENT_AUTH_TOKEN:-}"

python3 - <<'PY'
import os
from pathlib import Path

def esc(value: str) -> str:
    return value.replace("\\", "\\\\").replace("'", "\\'")

Path(os.environ["ROOT_DIR"], "src", "endpoint_agent", "embedded_config.py").write_text(
    "\n".join([
        f"DEFAULT_GATEWAY_URL = '{esc(os.environ.get('GATEWAY_URL', 'https://2.26.89.86'))}'",
        f"DEFAULT_GATEWAY_TLS_INSECURE = {str(os.environ.get('GATEWAY_TLS_INSECURE', 'true')).lower() in {'1', 'true', 'yes', 'on'}}",
        f"DEFAULT_ACTIVITY_SERVICE_URL = '{esc(os.environ.get('ACTIVITY_SERVICE_URL', '2.26.89.86:5001'))}'",
        f"DEFAULT_AGENT_MANAGEMENT_URL = '{esc(os.environ.get('AGENT_MANAGEMENT_URL', '2.26.89.86:5015'))}'",
        f"DEFAULT_AGENT_AUTH_HEADER = '{esc(os.environ.get('AGENT_AUTH_HEADER', 'x-agent-token'))}'",
        f"DEFAULT_AGENT_AUTH_TOKEN = '{esc(os.environ.get('AGENT_AUTH_TOKEN', ''))}'",
        "",
    ]),
    encoding="utf-8",
)
PY
trap 'rm -f "$ROOT_DIR/src/endpoint_agent/embedded_config.py"' EXIT

python3 -m pip install --upgrade pip
python3 -m pip install --user pyinstaller grpcio-tools
python3 -m pip install --user -e "$ROOT_DIR"
bash "$ROOT_DIR/scripts/generate_protos.sh"

mkdir -p "$ROOT_DIR/dist/macos" "$ROOT_DIR/build/macos"

python3 -m PyInstaller \
  --noconfirm \
  --clean \
  --onefile \
  --name endpoint-agent-macos \
  --paths "$ROOT_DIR/src" \
  --collect-submodules endpoint_agent.generated \
  --hidden-import tkinter \
  --distpath "$ROOT_DIR/dist/macos" \
  --workpath "$ROOT_DIR/build/macos/work" \
  --specpath "$ROOT_DIR/build/macos/spec" \
  "$ROOT_DIR/scripts/pyinstaller_entry.py"

chmod +x "$ROOT_DIR/dist/macos/endpoint-agent-macos"
echo "Built: $ROOT_DIR/dist/macos/endpoint-agent-macos"
