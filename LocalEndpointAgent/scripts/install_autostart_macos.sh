#!/usr/bin/env bash
set -euo pipefail

LABEL="${1:-com.local.endpoint.agent}"
EXEC_CMD="${2:-$HOME/.local/bin/endpoint-agent}"
CONFIG_PATH="${3:-$HOME/.config/local-endpoint-agent/agent.yaml}"
PLIST_PATH="$HOME/Library/LaunchAgents/${LABEL}.plist"

mkdir -p "$HOME/Library/LaunchAgents"

cat > "$PLIST_PATH" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>Label</key>
    <string>${LABEL}</string>
    <key>ProgramArguments</key>
    <array>
      <string>${EXEC_CMD}</string>
      <string>run</string>
      <string>--config</string>
      <string>${CONFIG_PATH}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>$HOME/Library/Logs/${LABEL}.out.log</string>
    <key>StandardErrorPath</key>
    <string>$HOME/Library/Logs/${LABEL}.err.log</string>
  </dict>
</plist>
PLIST

launchctl unload "$PLIST_PATH" >/dev/null 2>&1 || true
launchctl load "$PLIST_PATH"

echo "Installed launch agent: $PLIST_PATH"
