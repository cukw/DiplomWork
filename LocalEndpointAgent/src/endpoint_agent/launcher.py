from __future__ import annotations

import ctypes
import os
import platform
import shlex
import shutil
import subprocess
import sys
from pathlib import Path


def agent_invocation(args: list[str]) -> list[str]:
    if getattr(sys, "frozen", False):
        return [sys.executable, *args]
    return [sys.executable, "-m", "endpoint_agent.main", *args]


def is_elevated() -> bool:
    if os.name == "nt":
        try:
            return bool(ctypes.windll.shell32.IsUserAnAdmin())
        except Exception:
            return False

    geteuid = getattr(os, "geteuid", None)
    return bool(geteuid is not None and geteuid() == 0)


def request_elevation(args: list[str], *, cwd: Path | None = None) -> bool:
    cwd = cwd or Path.cwd()
    key = _platform_key()

    if key == "windows":
        return _request_elevation_windows(args, cwd)
    if key == "macos":
        return _request_elevation_macos(args, cwd)
    if key == "linux":
        return _request_elevation_linux(args, cwd)
    return False


def start_background(args: list[str], *, log_dir: Path, cwd: Path | None = None) -> int:
    log_dir.mkdir(parents=True, exist_ok=True)
    stdout_path = log_dir / "agent.background.out.log"
    stderr_path = log_dir / "agent.background.err.log"
    command = agent_invocation(args)
    env = os.environ.copy()
    env["ENDPOINT_AGENT_BACKGROUND_CHILD"] = "1"

    stdin = subprocess.DEVNULL
    stdout = stdout_path.open("ab")
    stderr = stderr_path.open("ab")
    try:
        kwargs: dict[str, object] = {
            "cwd": str(cwd or Path.cwd()),
            "env": env,
            "stdin": stdin,
            "stdout": stdout,
            "stderr": stderr,
            "close_fds": os.name != "nt",
        }
        if os.name == "nt":
            kwargs["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.DETACHED_PROCESS
        else:
            kwargs["start_new_session"] = True

        proc = subprocess.Popen(command, **kwargs)
        return int(proc.pid)
    finally:
        stdout.close()
        stderr.close()


def _platform_key() -> str:
    if os.name == "nt":
        return "windows"
    sys_name = platform.system().lower()
    if sys_name == "darwin":
        return "macos"
    if sys_name == "linux":
        return "linux"
    return sys_name


def _request_elevation_windows(args: list[str], cwd: Path) -> bool:
    executable = sys.executable
    parameters = subprocess.list2cmdline(args if getattr(sys, "frozen", False) else ["-m", "endpoint_agent.main", *args])
    try:
        result = ctypes.windll.shell32.ShellExecuteW(
            None,
            "runas",
            executable,
            parameters,
            str(cwd),
            1,
        )
        return int(result) > 32
    except Exception:
        return False


def _request_elevation_macos(args: list[str], cwd: Path) -> bool:
    command = "cd " + shlex.quote(str(cwd)) + " && " + shlex.join(agent_invocation(args))
    script = f'do shell script {_applescript_string(command)} with administrator privileges'
    try:
        completed = subprocess.run(["osascript", "-e", script], check=False)
        return completed.returncode == 0
    except Exception:
        return False


def _request_elevation_linux(args: list[str], cwd: Path) -> bool:
    command = agent_invocation(args)
    if shutil.which("pkexec"):
        try:
            subprocess.Popen(["pkexec", *command], cwd=str(cwd))
            return True
        except Exception:
            pass

    if shutil.which("sudo"):
        try:
            completed = subprocess.run(["sudo", "-E", *command], cwd=str(cwd), check=False)
            return completed.returncode == 0
        except Exception:
            return False

    return False


def _applescript_string(value: str) -> str:
    escaped = value.replace("\\", "\\\\").replace('"', '\\"')
    return f'"{escaped}"'
