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


SCRIPT_DIR = Path(__file__).resolve().parent
AGENT_SOURCE_ROOT = SCRIPT_DIR.parent
REPO_ROOT = AGENT_SOURCE_ROOT.parent


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
    activity_service_url: str,
    agent_management_url: str,
    state_dir: Path,
) -> str:
    user_id_line = "null" if user_id is None else str(user_id)
    safe_device = device_name.replace('"', "")
    safe_activity = activity_service_url.replace('"', "")
    safe_agent = agent_management_url.replace('"', "")
    state_dir_str = str(state_dir).replace("\\", "/").replace('"', "")
    return f"""agent:
  computer_id: {computer_id}
  user_id: {user_id_line}
  version: "0.1.0"
  device_name: "{safe_device}"

services:
  activity_service_url: "{safe_activity}"
  agent_management_url: "{safe_agent}"

runtime:
  state_dir: "{state_dir_str}"
  heartbeat_interval_sec: 15
  policy_refresh_interval_sec: 30
  flush_interval_sec: 5
  collection_interval_sec: 5
  max_batch_size: 100

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

risk:
  local_high_risk_threshold: 85.0
  enable_auto_lock: true
"""


def _write_runtime_files(
    install_root: Path,
    venv_python_path: Path,
    *,
    computer_id: int,
    user_id: int | None,
    device_name: str,
    activity_service_url: str,
    agent_management_url: str,
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
        activity_service_url=activity_service_url,
        agent_management_url=agent_management_url,
        state_dir=state_dir,
    )
    _print(f"Writing config: {config_path}")
    if not dry_run:
        config_path.write_text(config_text, encoding="utf-8")

    if _platform_key() == "windows":
        launcher = bin_dir / "run-agent.cmd"
        launcher_text = (
            "@echo off\r\n"
            f"cd /d \"{app_dir}\"\r\n"
            f"\"{venv_python_path}\" -m endpoint_agent.main --config \"{config_path}\" %*\r\n"
        )
    else:
        launcher = bin_dir / "run-agent.sh"
        launcher_text = (
            "#!/usr/bin/env bash\n"
            "set -euo pipefail\n"
            f"cd \"{app_dir}\"\n"
            f"exec \"{venv_python_path}\" -m endpoint_agent.main --config \"{config_path}\" \"$@\"\n"
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
ExecStart={venv_python_path} -m endpoint_agent.main --config {config_path}
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
Exec={venv_python_path} -m endpoint_agent.main --config {config_path}
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
        tr = f'"{launcher_cmd}"'
        try:
            _run([schtasks, "/Create", "/F", "/TN", task_name, "/SC", "ONLOGON", "/TR", tr], dry_run=dry_run)
            return True, f"Installed Scheduled Task '{task_name}'"
        except subprocess.CalledProcessError:
            pass

    startup_dir = _windows_startup_folder()
    _ensure_dirs([startup_dir], dry_run=dry_run)
    startup_cmd = startup_dir / "LocalEndpointAgent.cmd"
    content = (
        "@echo off\r\n"
        f"start \"\" \"{launcher_cmd}\"\r\n"
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


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Cross-platform installer for LocalEndpointAgent")
    parser.add_argument("--computer-id", type=int, required=True, help="Computer ID (1:1 mapping with user workstation in your system)")
    parser.add_argument("--user-id", type=int, default=None, help="User ID (optional)")
    parser.add_argument("--device-name", default=socket.gethostname(), help="Device name for agent identity")
    parser.add_argument("--activity-service-url", default="http://localhost:5001", help="Direct gRPC URL for ActivityService")
    parser.add_argument("--agent-management-url", default="http://localhost:5015", help="Direct gRPC URL for AgentManagementService")
    parser.add_argument("--install-dir", default=None, help="Target installation directory")
    parser.add_argument("--python", default=sys.executable, help="Python interpreter to create venv with")
    parser.add_argument("--skip-autostart", action="store_true", help="Do not configure autostart")
    parser.add_argument("--skip-rust", action="store_true", help="Skip Rust sysprobe build (fallback Python mode only)")
    parser.add_argument("--force", action="store_true", help="Overwrite existing app copy inside install directory")
    parser.add_argument("--dry-run", action="store_true", help="Print actions without changing files")
    return parser.parse_args()


def main() -> int:
    args = parse_args()

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
            activity_service_url=args.activity_service_url,
            agent_management_url=args.agent_management_url,
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
        return 0
    except subprocess.CalledProcessError as exc:
        print(f"Installer failed (command exit code {exc.returncode})", file=sys.stderr)
        return exc.returncode or 1
    except Exception as exc:  # pragma: no cover - installer surface
        print(f"Installer failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
