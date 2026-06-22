#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
import platform
import shutil
import socket
import subprocess
import sys
from pathlib import Path
from typing import Iterable
from urllib.parse import urlparse


SCRIPT_DIR = Path(__file__).resolve().parent
AGENT_SOURCE_ROOT = SCRIPT_DIR.parent
REPO_ROOT = AGENT_SOURCE_ROOT.parent

sys.path.insert(0, str(AGENT_SOURCE_ROOT / "src"))
try:
    from endpoint_agent.prod_defaults import (
        DEFAULT_ACTIVITY_SERVICE_URL,
        DEFAULT_AGENT_AUTH_HEADER,
        DEFAULT_AGENT_AUTH_TOKEN,
        DEFAULT_AGENT_MANAGEMENT_URL,
        DEFAULT_GATEWAY_TLS_INSECURE,
        DEFAULT_GATEWAY_URL,
    )
except Exception:
    DEFAULT_GATEWAY_URL = "https://2.26.89.86"
    DEFAULT_GATEWAY_TLS_INSECURE = True
    DEFAULT_ACTIVITY_SERVICE_URL = "2.26.89.86:5001"
    DEFAULT_AGENT_MANAGEMENT_URL = "2.26.89.86:5015"
    DEFAULT_AGENT_AUTH_HEADER = "x-agent-token"
    DEFAULT_AGENT_AUTH_TOKEN = os.environ.get("AGENT_AUTH_TOKEN", "")


def _print(msg: str) -> None:
    print(f"[installer] {msg}")


def _platform_key() -> str:
    if os.name == "nt":
        return "windows"
    sys_name = platform.system().lower()
    if sys_name == "darwin":
        return "macos"
    if sys_name == "linux":
        return "linux"
    return sys_name


def _default_install_dir() -> Path:
    key = _platform_key()
    home = Path.home()
    if key == "windows":
        local_app_data = Path(os.environ.get("LOCALAPPDATA", str(home / "AppData/Local")))
        return local_app_data / "LocalEndpointAgent"
    if key == "macos":
        return home / "Library" / "Application Support" / "LocalEndpointAgent"
    return home / ".local" / "share" / "local-endpoint-agent"


def _default_state_dir(install_root: Path) -> Path:
    return install_root / "state"


def _venv_python(venv_dir: Path) -> Path:
    if _platform_key() == "windows":
        return venv_dir / "Scripts" / "python.exe"
    return venv_dir / "bin" / "python"


def _run(
    cmd: list[str],
    *,
    cwd: Path | None = None,
    dry_run: bool = False,
    env: dict[str, str] | None = None,
    check: bool = True,
) -> subprocess.CompletedProcess[str]:
    cmd_display = " ".join([f'"{x}"' if " " in x else x for x in cmd])
    _print(f"run: {cmd_display}")
    if dry_run:
        return subprocess.CompletedProcess(cmd, 0, "", "")
    return subprocess.run(
        cmd,
        cwd=str(cwd) if cwd else None,
        env=env,
        check=check,
        text=True,
        capture_output=False,
    )


def _run_capture(
    cmd: list[str],
    *,
    cwd: Path | None = None,
    dry_run: bool = False,
    env: dict[str, str] | None = None,
    check: bool = True,
) -> subprocess.CompletedProcess[str]:
    cmd_display = " ".join([f'"{x}"' if " " in x else x for x in cmd])
    _print(f"run(capture): {cmd_display}")
    if dry_run:
        return subprocess.CompletedProcess(cmd, 0, "", "")
    return subprocess.run(
        cmd,
        cwd=str(cwd) if cwd else None,
        env=env,
        check=check,
        text=True,
        capture_output=True,
    )


def _copy_agent_source(src_root: Path, dst_root: Path, *, force: bool, dry_run: bool) -> None:
    app_dir = dst_root / "app"
    if app_dir.exists():
        if not force:
            raise RuntimeError(f"Install dir already exists: {app_dir} (use --force)")
        _print(f"Removing existing app dir: {app_dir}")
        if not dry_run:
            shutil.rmtree(app_dir)

    ignore = shutil.ignore_patterns(
        ".venv",
        "__pycache__",
        "*.pyc",
        "*.pyo",
        "build",
        "dist",
        "*.egg-info",
        ".pytest_cache",
        ".mypy_cache",
        "state",
        "logs",
    )
    _print(f"Copying agent source to {app_dir}")
    if not dry_run:
        shutil.copytree(src_root, app_dir, ignore=ignore)


def _ensure_dirs(paths: Iterable[Path], *, dry_run: bool) -> None:
    for path in paths:
        _print(f"Ensuring dir: {path}")
        if not dry_run:
            path.mkdir(parents=True, exist_ok=True)


def _create_venv(python_exe: str, venv_dir: Path, *, dry_run: bool) -> None:
    _run([python_exe, "-m", "venv", str(venv_dir)], dry_run=dry_run)


def _pip_install_basics(venv_python: Path, *, dry_run: bool) -> None:
    _run([str(venv_python), "-m", "pip", "install", "--upgrade", "pip", "setuptools", "wheel"], dry_run=dry_run)


def _pip_install_requirements(venv_python: Path, app_dir: Path, *, dry_run: bool) -> None:
    req = app_dir / "requirements.txt"
    _run([str(venv_python), "-m", "pip", "install", "-r", str(req)], dry_run=dry_run)


def _generate_stubs(venv_python: Path, app_dir: Path, repo_root: Path, *, dry_run: bool) -> None:
    out_dir = app_dir / "src" / "endpoint_agent" / "generated"
    _ensure_dirs([out_dir], dry_run=dry_run)
    init_file = out_dir / "__init__.py"
    if not dry_run and not init_file.exists():
        init_file.write_text("", encoding="utf-8")

    activity_proto_dir = repo_root / "Backend" / "services" / "ActivityService" / "Protos"
    agent_proto_dir = repo_root / "Backend" / "services" / "AgentManagementService" / "Protos"
    activity_proto = activity_proto_dir / "Activity.proto"
    agent_proto = agent_proto_dir / "agent.proto"

    if not activity_proto.exists():
        raise RuntimeError(f"Activity proto not found: {activity_proto}")
    if not agent_proto.exists():
        raise RuntimeError(f"Agent proto not found: {agent_proto}")

    _run([
        str(venv_python),
        "-m",
        "grpc_tools.protoc",
        "-I",
        str(activity_proto_dir),
        f"--python_out={out_dir}",
        f"--grpc_python_out={out_dir}",
        str(activity_proto),
    ], dry_run=dry_run)

    _run([
        str(venv_python),
        "-m",
        "grpc_tools.protoc",
        "-I",
        str(agent_proto_dir),
        f"--python_out={out_dir}",
        f"--grpc_python_out={out_dir}",
        str(agent_proto),
    ], dry_run=dry_run)

    if dry_run:
        return

    old_pb = out_dir / "Activity_pb2.py"
    old_grpc = out_dir / "Activity_pb2_grpc.py"
    new_pb = out_dir / "activity_pb2.py"
    new_grpc = out_dir / "activity_pb2_grpc.py"
    if old_pb.exists():
        old_pb.replace(new_pb)
    if old_grpc.exists():
        old_grpc.replace(new_grpc)

    for file in out_dir.glob("*_pb2_grpc.py"):
        text = file.read_text(encoding="utf-8")
        text = text.replace("import Activity_pb2 as Activity__pb2", "import activity_pb2 as Activity__pb2")
        file.write_text(text, encoding="utf-8")


def _pip_install_agent(venv_python: Path, app_dir: Path, *, dry_run: bool) -> None:
    # Editable install keeps local generated stubs and faster updates during development.
    _run([str(venv_python), "-m", "pip", "install", "-e", str(app_dir)], dry_run=dry_run)


def _try_install_rust_sysprobe(venv_python: Path, app_dir: Path, *, dry_run: bool) -> tuple[bool, str]:
    if _platform_key() == "windows":
        # Windows currently has real implementation; try build if toolchain exists.
        pass

    cargo = shutil.which("cargo")
    if not cargo:
        return False, "cargo not found; Rust sysprobe skipped (Python fallback will be used)"

    # `maturin` is used to build/install the PyO3 module into the venv.
    try:
        _run([str(venv_python), "-m", "pip", "install", "maturin"], dry_run=dry_run)
        _run(
            [str(venv_python), "-m", "maturin", "develop", "--manifest-path", str(app_dir / "rust" / "sysprobe" / "Cargo.toml")],
            cwd=app_dir,
            dry_run=dry_run,
        )
        return True, "Rust sysprobe installed successfully"
    except subprocess.CalledProcessError as exc:
        return False, f"Rust sysprobe build failed ({exc}); Python fallback will be used"


def _render_config_yaml(
    *,
    computer_id: int,
    user_id: int | None,
    device_name: str,
    gateway_url: str,
    gateway_tls_insecure: bool,
    activity_service_url: str,
    agent_management_url: str,
    agent_transport_auth_token: str | None,
    agent_transport_auth_header: str,
    state_dir: Path,
    control_plane_signing_secret: str | None,
    control_plane_signing_key_id: str,
    control_plane_allow_unsigned: bool,
    require_admin: bool,
    auto_start: bool,
) -> str:
    user_id_line = "null" if user_id is None else str(user_id)
    safe_device = device_name.replace('"', "")
    safe_gateway = gateway_url.replace('"', "")
    safe_activity = activity_service_url.replace('"', "")
    safe_agent = agent_management_url.replace('"', "")
    safe_transport_token = (agent_transport_auth_token or "").replace('"', "")
    safe_transport_header = (agent_transport_auth_header or "x-agent-token").replace('"', "")
    safe_cp_secret = (control_plane_signing_secret or "").replace('"', "")
    safe_cp_key_id = (control_plane_signing_key_id or "default").replace('"', "")
    state_dir_str = str(state_dir).replace("\\", "/").replace('"', "")
    cp_allow_unsigned = "true" if control_plane_allow_unsigned else "false"
    gateway_insecure = "true" if gateway_tls_insecure else "false"
    require_admin_value = "true" if require_admin else "false"
    auto_start_value = "true" if auto_start else "false"
    return f"""agent:
  computer_id: {computer_id}
  user_id: {user_id_line}
  session_id: null
  session_expires_at: null
  auth_refresh_token: null
  version: "0.1.0"
  device_name: "{safe_device}"

services:
  gateway_url: "{safe_gateway}"
  gateway_tls_insecure: {gateway_insecure}
  activity_service_url: "{safe_activity}"
  agent_management_url: "{safe_agent}"

runtime:
  state_dir: "{state_dir_str}"
  heartbeat_interval_sec: 15
  policy_refresh_interval_sec: 30
  flush_interval_sec: 5
  collection_interval_sec: 5
  max_batch_size: 100
  max_queue_size: 10000
  require_admin: {require_admin_value}
  auto_start: {auto_start_value}

collectors:
  processes:
    enabled: true
    snapshot_limit: 50
  browser_history:
    enabled: true
    poll_interval_sec: 10
    browsers: ["chrome", "edge", "firefox"]
  active_window:
    enabled: true
  idle_time:
    enabled: true
    idle_threshold_sec: 120
  network:
    enabled: true
    snapshot_limit: 50
  file_activity:
    enabled: true
    paths: []
    max_files_per_scan: 200
  usb_devices:
    enabled: true
    poll_interval_sec: 30
  inventory:
    enabled: true
    interval_sec: 3600
    max_apps: 200
    max_processes: 200
  session:
    enabled: true

risk:
  local_high_risk_threshold: 85.0
  enable_auto_lock: true

security:
  agent_transport_auth:
    token: "{safe_transport_token}"
    header_name: "{safe_transport_header}"
  control_plane_signing:
    secret: "{safe_cp_secret}"
    key_id: "{safe_cp_key_id}"
    allow_unsigned: {cp_allow_unsigned}
"""


def _write_runtime_files(
    install_root: Path,
    venv_python_path: Path,
    *,
    computer_id: int,
    user_id: int | None,
    device_name: str,
    gateway_url: str,
    gateway_tls_insecure: bool,
    activity_service_url: str,
    agent_management_url: str,
    agent_transport_auth_token: str | None,
    agent_transport_auth_header: str,
    control_plane_signing_secret: str | None,
    control_plane_signing_key_id: str,
    control_plane_allow_unsigned: bool,
    require_admin: bool,
    auto_start: bool,
    dry_run: bool,
) -> tuple[Path, Path]:
    app_dir = install_root / "app"
    config_dir = install_root / "config"
    state_dir = install_root / "state"
    logs_dir = install_root / "logs"
    bin_dir = install_root / "bin"
    _ensure_dirs([config_dir, state_dir, logs_dir, bin_dir], dry_run=dry_run)

    config_path = config_dir / "agent.local.yaml"
    config_text = _render_config_yaml(
        computer_id=computer_id,
        user_id=user_id,
        device_name=device_name,
        gateway_url=gateway_url,
        gateway_tls_insecure=gateway_tls_insecure,
        activity_service_url=activity_service_url,
        agent_management_url=agent_management_url,
        agent_transport_auth_token=agent_transport_auth_token,
        agent_transport_auth_header=agent_transport_auth_header,
        state_dir=state_dir,
        control_plane_signing_secret=control_plane_signing_secret,
        control_plane_signing_key_id=control_plane_signing_key_id,
        control_plane_allow_unsigned=control_plane_allow_unsigned,
        require_admin=require_admin,
        auto_start=auto_start,
    )
    _print(f"Writing config: {config_path}")
    if not dry_run:
        config_path.write_text(config_text, encoding="utf-8")

    if _platform_key() == "windows":
        launcher = bin_dir / "run-agent.cmd"
        launcher_text = (
            "@echo off\r\n"
            f"cd /d \"{app_dir}\"\r\n"
            "if \"%~1\"==\"\" (\r\n"
            f"  \"{venv_python_path}\" -m endpoint_agent.main start --config \"{config_path}\"\r\n"
            ") else (\r\n"
            f"  \"{venv_python_path}\" -m endpoint_agent.main %* --config \"{config_path}\"\r\n"
            ")\r\n"
        )
    else:
        launcher = bin_dir / "run-agent.sh"
        launcher_text = (
            "#!/usr/bin/env bash\n"
            "set -euo pipefail\n"
            f"cd \"{app_dir}\"\n"
            "if [ \"$#\" -eq 0 ]; then\n"
            "  set -- start\n"
            "fi\n"
            f"exec \"{venv_python_path}\" -m endpoint_agent.main \"$@\" --config \"{config_path}\"\n"
        )
    _print(f"Writing launcher: {launcher}")
    if not dry_run:
        launcher.write_text(launcher_text, encoding="utf-8")
        if _platform_key() != "windows":
            launcher.chmod(0o755)

    return config_path, launcher


def _install_autostart_linux(install_root: Path, venv_python_path: Path, config_path: Path, *, dry_run: bool) -> tuple[bool, str]:
    app_dir = install_root / "app"
    service_dir = Path.home() / ".config" / "systemd" / "user"
    service_file = service_dir / "local-endpoint-agent.service"
    _ensure_dirs([service_dir], dry_run=dry_run)

    service_text = f"""[Unit]
Description=Local Endpoint Activity Agent
After=network-online.target

[Service]
Type=simple
WorkingDirectory={app_dir}
ExecStart={venv_python_path} -m endpoint_agent.main run --config {config_path}
Restart=always
RestartSec=5

[Install]
WantedBy=default.target
"""
    if not dry_run:
        service_file.write_text(service_text, encoding="utf-8")

    systemctl = shutil.which("systemctl")
    if systemctl:
        try:
            _run([systemctl, "--user", "daemon-reload"], dry_run=dry_run)
            _run([systemctl, "--user", "enable", "--now", "local-endpoint-agent.service"], dry_run=dry_run)
            return True, f"Installed systemd user service: {service_file}"
        except subprocess.CalledProcessError:
            pass

    # Fallback: XDG autostart desktop entry.
    autostart_dir = Path.home() / ".config" / "autostart"
    desktop_file = autostart_dir / "local-endpoint-agent.desktop"
    _ensure_dirs([autostart_dir], dry_run=dry_run)
    desktop_text = f"""[Desktop Entry]
Type=Application
Name=Local Endpoint Agent
Exec={venv_python_path} -m endpoint_agent.main run --config {config_path}
Path={app_dir}
X-GNOME-Autostart-enabled=true
Terminal=false
"""
    if not dry_run:
        desktop_file.write_text(desktop_text, encoding="utf-8")
    return True, f"Installed XDG autostart entry: {desktop_file}"


def _install_autostart_macos(install_root: Path, venv_python_path: Path, config_path: Path, *, dry_run: bool) -> tuple[bool, str]:
    app_dir = install_root / "app"
    launch_agents_dir = Path.home() / "Library" / "LaunchAgents"
    plist_path = launch_agents_dir / "com.finalwork.localendpointagent.plist"
    _ensure_dirs([launch_agents_dir], dry_run=dry_run)

    plist_text = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>com.finalwork.localendpointagent</string>
  <key>ProgramArguments</key>
  <array>
    <string>{venv_python_path}</string>
    <string>-m</string>
    <string>endpoint_agent.main</string>
    <string>run</string>
    <string>--config</string>
    <string>{config_path}</string>
  </array>
  <key>WorkingDirectory</key>
  <string>{app_dir}</string>
  <key>RunAtLoad</key>
  <true/>
  <key>KeepAlive</key>
  <true/>
  <key>StandardOutPath</key>
  <string>{install_root / "logs" / "agent.out.log"}</string>
  <key>StandardErrorPath</key>
  <string>{install_root / "logs" / "agent.err.log"}</string>
</dict>
</plist>
"""
    if not dry_run:
        plist_path.write_text(plist_text, encoding="utf-8")

    launchctl = shutil.which("launchctl")
    if launchctl:
        try:
            # unload first if it exists; ignore failures
            _run([launchctl, "unload", str(plist_path)], dry_run=dry_run, check=False)
            _run([launchctl, "load", "-w", str(plist_path)], dry_run=dry_run)
            return True, f"Installed launchd agent: {plist_path}"
        except subprocess.CalledProcessError:
            return False, f"launchd plist written, but launchctl load failed: {plist_path}"
    return True, f"launchd plist written: {plist_path}"


def _windows_startup_folder() -> Path:
    appdata = Path(os.environ.get("APPDATA", str(Path.home() / "AppData/Roaming")))
    return appdata / "Microsoft" / "Windows" / "Start Menu" / "Programs" / "Startup"


def _install_autostart_windows(install_root: Path, launcher_path: Path, *, dry_run: bool) -> tuple[bool, str]:
    task_name = "LocalEndpointAgent"
    schtasks = shutil.which("schtasks")
    launcher_cmd = str(launcher_path)

    if schtasks:
        tr = f'"{launcher_cmd}" run'
        try:
            _run([schtasks, "/Create", "/F", "/TN", task_name, "/SC", "ONLOGON", "/RL", "HIGHEST", "/TR", tr], dry_run=dry_run)
            return True, f"Installed elevated Scheduled Task '{task_name}'"
        except subprocess.CalledProcessError:
            pass

    startup_dir = _windows_startup_folder()
    _ensure_dirs([startup_dir], dry_run=dry_run)
    startup_cmd = startup_dir / "LocalEndpointAgent.cmd"
    content = (
        "@echo off\r\n"
        f"start \"\" \"{launcher_cmd}\" start --require-admin\r\n"
    )
    if not dry_run:
        startup_cmd.write_text(content, encoding="utf-8")
    return True, f"Installed Startup folder launcher: {startup_cmd}"


def _install_autostart(install_root: Path, venv_python_path: Path, config_path: Path, launcher_path: Path, *, dry_run: bool) -> tuple[bool, str]:
    key = _platform_key()
    if key == "linux":
        return _install_autostart_linux(install_root, venv_python_path, config_path, dry_run=dry_run)
    if key == "macos":
        return _install_autostart_macos(install_root, venv_python_path, config_path, dry_run=dry_run)
    if key == "windows":
        return _install_autostart_windows(install_root, launcher_path, dry_run=dry_run)
    return False, f"Autostart is not implemented for platform '{key}'"


def _write_install_info(install_root: Path, *, app_dir: Path, venv_python_path: Path, config_path: Path, launcher_path: Path, dry_run: bool) -> None:
    info = {
        "platform": _platform_key(),
        "install_root": str(install_root),
        "app_dir": str(app_dir),
        "python": str(venv_python_path),
        "config": str(config_path),
        "launcher": str(launcher_path),
    }
    info_file = install_root / "install-info.txt"
    lines = [f"{k}: {v}" for k, v in info.items()]
    _print(f"Writing install info: {info_file}")
    if not dry_run:
        info_file.write_text("\n".join(lines) + "\n", encoding="utf-8")


def _capability_note() -> str:
    key = _platform_key()
    if key == "windows":
        return "Windows: full sysprobe path available (idle/window/lock) when Rust module builds successfully."
    if key == "macos":
        return "macOS: agent runs cross-platform; low-level features use system fallback commands (ioreg/osascript/CGSession) when Rust module is unavailable."
    if key == "linux":
        return "Linux: agent runs cross-platform; low-level features use available desktop tools (xprintidle/xdotool/loginctl/etc.) when Rust module is unavailable."
    return "Platform support is best-effort; Python collectors may still work."


def _looks_like_loopback_endpoint(raw: str) -> bool:
    value = (raw or "").strip()
    if not value:
        return False

    parsed = urlparse(value if "://" in value else f"grpc://{value}")
    host = (parsed.hostname or "").strip().lower()
    if not host and parsed.path:
        host = parsed.path.split(":", 1)[0].strip().lower()

    return host in {"localhost", "127.0.0.1", "::1"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Cross-platform installer for LocalEndpointAgent")
    parser.add_argument("--computer-id", type=int, default=0, help="Computer ID. Use 0 before running endpoint-agent enroll")
    parser.add_argument("--user-id", type=int, default=None, help="User ID (optional)")
    parser.add_argument("--device-name", default=socket.gethostname(), help="Device name for agent identity")
    parser.add_argument("--gateway-url", default=DEFAULT_GATEWAY_URL, help="Gateway URL used by local login/enrollment")
    parser.add_argument("--gateway-tls-insecure", action="store_true", default=DEFAULT_GATEWAY_TLS_INSECURE, help="Skip Gateway TLS validation for local login/enrollment")
    parser.add_argument("--gateway-tls-verify", dest="gateway_tls_insecure", action="store_false", help="Require valid Gateway TLS certificate")
    parser.add_argument("--activity-service-url", default=DEFAULT_ACTIVITY_SERVICE_URL, help="Direct gRPC endpoint for ActivityService (host:port)")
    parser.add_argument("--agent-management-url", default=DEFAULT_AGENT_MANAGEMENT_URL, help="Direct gRPC endpoint for AgentManagementService (host:port)")
    parser.add_argument("--agent-auth-token", default=DEFAULT_AGENT_AUTH_TOKEN, help="Shared gRPC metadata token for ActivityService/AgentManagementService")
    parser.add_argument("--agent-auth-header", default=DEFAULT_AGENT_AUTH_HEADER, help="Metadata header name for the shared gRPC agent token")
    parser.add_argument("--control-plane-signing-secret", default="", help="Shared secret for verifying signed agent policy/commands (optional)")
    parser.add_argument("--control-plane-signing-key-id", default="default", help="Expected control-plane signing key ID")
    parser.add_argument("--require-signed-control-plane", action="store_true", help="Reject unsigned policy/commands from AgentManagementService")
    parser.add_argument("--require-admin", action="store_true", help="Request administrator rights before starting the agent")
    parser.add_argument("--install-dir", default=None, help="Target installation directory")
    parser.add_argument("--python", default=sys.executable, help="Python interpreter to create venv with")
    parser.add_argument("--skip-autostart", action="store_true", help="Do not configure autostart")
    parser.add_argument("--skip-rust", action="store_true", help="Skip Rust sysprobe build (fallback Python mode only)")
    parser.add_argument("--force", action="store_true", help="Overwrite existing app copy inside install directory")
    parser.add_argument("--dry-run", action="store_true", help="Print actions without changing files")
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    if not (args.agent_auth_token or "").strip():
        _print(
            "WARNING: --agent-auth-token is empty. "
            "If backend services enforce AgentAuth__Token, this agent will not be able to send/heartbeat."
        )

    if _looks_like_loopback_endpoint(args.activity_service_url):
        _print(
            "WARNING: --activity-service-url points to loopback. "
            "Use server IP/DNS if this agent runs on another host."
        )

    if _looks_like_loopback_endpoint(args.agent_management_url):
        _print(
            "WARNING: --agent-management-url points to loopback. "
            "Use server IP/DNS if this agent runs on another host."
        )

    install_root = Path(args.install_dir).expanduser().resolve() if args.install_dir else _default_install_dir()
    app_dir = install_root / "app"
    venv_dir = install_root / ".venv"
    venv_python_path = _venv_python(venv_dir)

    _print(f"Detected platform: {_platform_key()}")
    _print(_capability_note())
    _print(f"Install root: {install_root}")
    _print(f"Source root: {AGENT_SOURCE_ROOT}")

    if not AGENT_SOURCE_ROOT.exists():
        print("LocalEndpointAgent source root not found.", file=sys.stderr)
        return 2
    if not (REPO_ROOT / "Backend").exists():
        print("Backend/ directory not found next to LocalEndpointAgent; installer needs repo protos to generate gRPC stubs.", file=sys.stderr)
        return 2

    try:
        _ensure_dirs([install_root], dry_run=args.dry_run)
        _copy_agent_source(AGENT_SOURCE_ROOT, install_root, force=args.force, dry_run=args.dry_run)

        if venv_dir.exists() and args.force and not args.dry_run:
            _print(f"Removing existing venv: {venv_dir}")
            shutil.rmtree(venv_dir)

        _create_venv(args.python, venv_dir, dry_run=args.dry_run)
        _pip_install_basics(venv_python_path, dry_run=args.dry_run)
        _pip_install_requirements(venv_python_path, app_dir, dry_run=args.dry_run)
        _generate_stubs(venv_python_path, app_dir, REPO_ROOT, dry_run=args.dry_run)
        _pip_install_agent(venv_python_path, app_dir, dry_run=args.dry_run)

        rust_status = "skipped by flag"
        if not args.skip_rust:
            ok, message = _try_install_rust_sysprobe(venv_python_path, app_dir, dry_run=args.dry_run)
            rust_status = message
            _print(message)
        else:
            _print("Rust sysprobe build skipped (--skip-rust). Python fallback will be used where needed.")

        config_path, launcher_path = _write_runtime_files(
            install_root,
            venv_python_path,
            computer_id=args.computer_id,
            user_id=args.user_id,
            device_name=args.device_name,
            gateway_url=args.gateway_url,
            gateway_tls_insecure=args.gateway_tls_insecure,
            activity_service_url=args.activity_service_url,
            agent_management_url=args.agent_management_url,
            agent_transport_auth_token=args.agent_auth_token or None,
            agent_transport_auth_header=args.agent_auth_header,
            control_plane_signing_secret=args.control_plane_signing_secret or None,
            control_plane_signing_key_id=args.control_plane_signing_key_id,
            control_plane_allow_unsigned=not args.require_signed_control_plane,
            require_admin=args.require_admin,
            auto_start=not args.skip_autostart,
            dry_run=args.dry_run,
        )

        autostart_status = "skipped"
        if not args.skip_autostart:
            _, msg = _install_autostart(
                install_root,
                venv_python_path,
                config_path,
                launcher_path,
                dry_run=args.dry_run,
            )
            autostart_status = msg
            _print(msg)
        else:
            _print("Autostart installation skipped (--skip-autostart)")

        _write_install_info(
            install_root,
            app_dir=app_dir,
            venv_python_path=venv_python_path,
            config_path=config_path,
            launcher_path=launcher_path,
            dry_run=args.dry_run,
        )

        print()
        print("Installation completed.")
        print(f"Platform: {_platform_key()}")
        print(f"Install root: {install_root}")
        print(f"Launcher: {launcher_path}")
        print(f"Config: {config_path}")
        print(f"Rust sysprobe: {rust_status}")
        print(f"Autostart: {autostart_status}")
        print()
        print("Manual start command:")
        print(f'  "{launcher_path}"')
        if not args.require_admin:
            print(f'  "{launcher_path}" start --require-admin')
        return 0
    except subprocess.CalledProcessError as exc:
        print(f"Installer failed (command exit code {exc.returncode})", file=sys.stderr)
        return exc.returncode or 1
    except Exception as exc:  # pragma: no cover - installer surface
        print(f"Installer failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
