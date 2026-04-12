#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVICE_DIR="$HOME/.config/systemd/user"
SERVICE_FILE="$SERVICE_DIR/local-endpoint-agent.service"

mkdir -p "$SERVICE_DIR"
cat > "$SERVICE_FILE" <<SERVICE
[Unit]
Description=Local Endpoint Activity Agent
After=network-online.target

[Service]
Type=simple
WorkingDirectory=$ROOT_DIR
ExecStart=python -m endpoint_agent.main --config config/agent.local.yaml
Restart=always
RestartSec=5

[Install]
WantedBy=default.target
SERVICE

systemctl --user daemon-reload
systemctl --user enable --now local-endpoint-agent.service

echo "Installed systemd user service: $SERVICE_FILE"
