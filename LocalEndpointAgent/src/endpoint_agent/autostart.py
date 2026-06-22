from __future__ import annotations

import logging
import os
import platform
import shlex
import shutil
import subprocess
from pathlib import Path
from xml.sax.saxutils import escape as xml_escape

from .launcher import agent_invocation

LOGGER = logging.getLogger("endpoint_agent.autostart")

LAUNCHD_LABEL = "com.finalwork.localendpointagent"
SYSTEMD_SERVICE = "local-endpoint-agent.service"
WINDOWS_TASK_NAME = "LocalEndpointAgent"


def ensure_user_autostart(config_path: str | Path, *, require_admin: bool = False) -> tuple[bool, str]:
    config = Path(config_path).expanduser().resolve()
    command = agent_invocation(["run", "--config", str(config)])
    if require_admin:
        command.append("--require-admin")

    key = _platform_key()
    if key == "linux":
        return _ensure_linux_autostart(command)
    if key == "macos":
        return _ensure_macos_autostart(command)
    if key == "windows":
        return _ensure_windows_autostart(command)

    return False, f"Autostart is not implemented for platform '{key}'"


def _ensure_linux_autostart(command: list[str]) -> tuple[bool, str]:
    service_dir = Path.home() / ".config" / "systemd" / "user"
    service_file = service_dir / SYSTEMD_SERVICE
    service_dir.mkdir(parents=True, exist_ok=True)

    service_text = f"""[Unit]
Description=Local Endpoint Activity Agent
After=network-online.target

[Service]
Type=simple
ExecStart={_quote_systemd_command(command)}
Restart=always
RestartSec=5

[Install]
WantedBy=default.target
"""
    _write_text_if_changed(service_file, service_text)

    systemctl = shutil.which("systemctl")
    if systemctl:
        try:
            subprocess.run([systemctl, "--user", "daemon-reload"], check=True)
            subprocess.run([systemctl, "--user", "enable", SYSTEMD_SERVICE], check=True)
            return True, f"Autostart enabled via systemd user service: {service_file}"
        except subprocess.CalledProcessError as exc:
            LOGGER.warning("Failed to enable systemd autostart, falling back to XDG autostart: %s", exc)

    autostart_dir = Path.home() / ".config" / "autostart"
    desktop_file = autostart_dir / "local-endpoint-agent.desktop"
    autostart_dir.mkdir(parents=True, exist_ok=True)
    desktop_text = f"""[Desktop Entry]
Type=Application
Name=Local Endpoint Agent
Exec={shlex.join(command)}
X-GNOME-Autostart-enabled=true
Terminal=false
"""
    _write_text_if_changed(desktop_file, desktop_text)
    return True, f"Autostart enabled via XDG desktop entry: {desktop_file}"


def _ensure_macos_autostart(command: list[str]) -> tuple[bool, str]:
    launch_agents_dir = Path.home() / "Library" / "LaunchAgents"
    logs_dir = Path.home() / "Library" / "Logs"
    plist_path = launch_agents_dir / f"{LAUNCHD_LABEL}.plist"
    launch_agents_dir.mkdir(parents=True, exist_ok=True)
    logs_dir.mkdir(parents=True, exist_ok=True)

    arguments = "\n".join(f"    <string>{xml_escape(arg)}</string>" for arg in command)
    plist_text = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>{LAUNCHD_LABEL}</string>
  <key>ProgramArguments</key>
  <array>
{arguments}
  </array>
  <key>RunAtLoad</key>
  <true/>
  <key>KeepAlive</key>
  <true/>
  <key>StandardOutPath</key>
  <string>{xml_escape(str(logs_dir / f"{LAUNCHD_LABEL}.out.log"))}</string>
  <key>StandardErrorPath</key>
  <string>{xml_escape(str(logs_dir / f"{LAUNCHD_LABEL}.err.log"))}</string>
</dict>
</plist>
"""
    _write_text_if_changed(plist_path, plist_text)
    return True, f"Autostart registered via launchd for next login: {plist_path}"


def _ensure_windows_autostart(command: list[str]) -> tuple[bool, str]:
    schtasks = shutil.which("schtasks")
    task_command = subprocess.list2cmdline(command)

    if schtasks:
        create_args = [
            schtasks,
            "/Create",
            "/F",
            "/TN",
            WINDOWS_TASK_NAME,
            "/SC",
            "ONLOGON",
            "/TR",
            task_command,
        ]
        try:
            subprocess.run(create_args, check=True)
            return True, f"Autostart enabled via Windows Scheduled Task: {WINDOWS_TASK_NAME}"
        except subprocess.CalledProcessError as exc:
            LOGGER.warning("Failed to create Scheduled Task, falling back to Startup folder: %s", exc)

    startup_dir = _windows_startup_folder()
    startup_dir.mkdir(parents=True, exist_ok=True)
    cmd_path = startup_dir / "LocalEndpointAgent.cmd"
    cmd_text = f"@echo off\r\nstart \"\" {task_command}\r\n"
    _write_text_if_changed(cmd_path, cmd_text)
    return True, f"Autostart enabled via Windows Startup folder: {cmd_path}"


def _platform_key() -> str:
    if os.name == "nt":
        return "windows"

    sys_name = platform.system().lower()
    if sys_name == "darwin":
        return "macos"
    if sys_name == "linux":
        return "linux"
    return sys_name


def _windows_startup_folder() -> Path:
    appdata = Path(os.environ.get("APPDATA", str(Path.home() / "AppData/Roaming")))
    return appdata / "Microsoft" / "Windows" / "Start Menu" / "Programs" / "Startup"


def _quote_systemd_command(command: list[str]) -> str:
    return " ".join(_quote_systemd_arg(arg) for arg in command)


def _quote_systemd_arg(value: str) -> str:
    if not value:
        return '""'

    needs_quotes = any(ch.isspace() for ch in value) or any(ch in value for ch in ['"', "'", "\\"])
    escaped = value.replace("\\", "\\\\").replace('"', '\\"')
    return f'"{escaped}"' if needs_quotes else escaped


def _write_text_if_changed(path: Path, text: str) -> None:
    if path.exists() and path.read_text(encoding="utf-8") == text:
        return
    path.write_text(text, encoding="utf-8")
