#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
REPO_DIR="$(cd "$ROOT_DIR/.." && pwd)"
GATEWAY_URL="${GATEWAY_URL:-https://2.26.89.86}"
GATEWAY_TLS_INSECURE="${GATEWAY_TLS_INSECURE:-true}"
ACTIVITY_SERVICE_URL="${ACTIVITY_SERVICE_URL:-2.26.89.86:5001}"
AGENT_MANAGEMENT_URL="${AGENT_MANAGEMENT_URL:-2.26.89.86:5015}"
AGENT_AUTH_HEADER="${AGENT_AUTH_HEADER:-x-agent-token}"
AGENT_AUTH_TOKEN="${AGENT_AUTH_TOKEN:-}"
export ROOT_DIR
if ! command -v docker >/dev/null 2>&1; then
  echo "Docker is required for Linux .deb build (docker command not found)." >&2
  exit 1
fi
if ! docker info >/dev/null 2>&1; then
  echo "Docker daemon is not running. Start Docker Desktop/Engine and retry." >&2
  exit 1
fi
VERSION="$(python3 - <<'PY'
import tomllib
from pathlib import Path
import os
p = Path(os.environ["ROOT_DIR"]) / "pyproject.toml"
data = tomllib.loads(p.read_text(encoding='utf-8'))
print(data['project']['version'])
PY
)"

mkdir -p "$ROOT_DIR/dist/linux" "$ROOT_DIR/build/linux"

# Build Linux binary + .deb inside Linux container (amd64)
docker run --rm --platform linux/amd64 \
  -e GATEWAY_URL="$GATEWAY_URL" \
  -e GATEWAY_TLS_INSECURE="$GATEWAY_TLS_INSECURE" \
  -e ACTIVITY_SERVICE_URL="$ACTIVITY_SERVICE_URL" \
  -e AGENT_MANAGEMENT_URL="$AGENT_MANAGEMENT_URL" \
  -e AGENT_AUTH_HEADER="$AGENT_AUTH_HEADER" \
  -e AGENT_AUTH_TOKEN="$AGENT_AUTH_TOKEN" \
  -v "$REPO_DIR:/work/repo" \
  -w /work/repo/LocalEndpointAgent \
  python:3.12-bookworm /bin/bash -lc "
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
apt-get update
apt-get install -y --no-install-recommends dpkg-dev ca-certificates
python -m pip install --upgrade pip
python -m pip install pyinstaller grpcio grpcio-tools protobuf psutil pyyaml pydantic
python -m pip install -e . --no-deps
bash scripts/generate_protos.sh
python -m PyInstaller --noconfirm --clean --onefile --name endpoint-agent-linux --paths src --collect-submodules endpoint_agent.generated --collect-submodules grpc --collect-binaries grpc --collect-binaries psutil --hidden-import pyexpat --hidden-import xml.parsers.expat --hidden-import tkinter scripts/pyinstaller_entry.py --distpath dist/linux --workpath build/linux/work --specpath build/linux/spec
chmod +x dist/linux/endpoint-agent-linux
dist/linux/endpoint-agent-linux --help >/dev/null
dist/linux/endpoint-agent-linux selfcheck >/dev/null
PKGROOT=/tmp/endpoint-agent-pkg
rm -rf \"\$PKGROOT\"
mkdir -p \"\$PKGROOT/DEBIAN\" \"\$PKGROOT/opt/local-endpoint-agent\" \"\$PKGROOT/usr/local/bin\"
cp dist/linux/endpoint-agent-linux \"\$PKGROOT/opt/local-endpoint-agent/endpoint-agent\"
cat > \"\$PKGROOT/usr/local/bin/endpoint-agent\" <<'SH'
#!/usr/bin/env bash
exec /opt/local-endpoint-agent/endpoint-agent \"\$@\"
SH
chmod +x \"\$PKGROOT/usr/local/bin/endpoint-agent\"
cat > \"\$PKGROOT/DEBIAN/control\" <<'CTL'
Package: local-endpoint-agent
Version: $VERSION
Section: admin
Priority: optional
Architecture: amd64
Maintainer: Activity Monitoring Team <noreply@example.com>
Depends: libc6
Description: Local Endpoint Agent
 Cross-platform endpoint agent for Activity Monitoring System.
CTL
dpkg-deb --build \"\$PKGROOT\" dist/linux/local-endpoint-agent_${VERSION}_amd64.deb
"

echo "Built: $ROOT_DIR/dist/linux/local-endpoint-agent_${VERSION}_amd64.deb"
