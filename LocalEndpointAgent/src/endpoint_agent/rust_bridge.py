from __future__ import annotations

from functools import lru_cache
from typing import Any
import ctypes
import logging
import os
import platform
import re
import shutil
import subprocess
import time
from pathlib import Path

logger = logging.getLogger(__name__)


_WARNED: set[str] = set()


def _warn_once(key: str, message: str, *args: object) -> None:
    if key in _WARNED:
        return
    _WARNED.add(key)
    logger.warning(message, *args)


def _platform_key() -> str:
    if os.name == "nt":
        return "windows"
    sys_name = platform.system().lower()
    if sys_name == "darwin":
        return "macos"
    if sys_name == "linux":
        return "linux"
    return sys_name


def _run_capture(cmd: list[str], timeout: float = 2.0) -> str | None:
    try:
        completed = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
        if completed.returncode != 0:
            return None
        return (completed.stdout or "").strip()
    except Exception:
        return None


def _run_ok(cmd: list[str], timeout: float = 4.0) -> bool:
    try:
        completed = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
        return completed.returncode == 0
    except Exception:
        return False


def _load_rust_impl() -> Any | None:
    try:
        import agent_sysprobe  # type: ignore

        logger.info("Loaded Rust sysprobe module")
        return agent_sysprobe
    except Exception as exc:
        _warn_once("rust_missing", "Rust sysprobe unavailable (%s); using platform fallback", exc)
        return None


_RUST = _load_rust_impl()


def _rust_idle_time_ms() -> int | None:
    if _RUST is None:
        return None
    try:
        value = int(getattr(_RUST, "idle_time_ms")())
        # On non-Windows builds current Rust implementation returns 0 stub.
        return value if value > 0 else None
    except Exception:
        return None


def _rust_active_window_title() -> str | None:
    if _RUST is None:
        return None
    try:
        value = str(getattr(_RUST, "active_window_title")() or "").strip()
        return value or None
    except Exception:
        return None


def _rust_lock_workstation() -> bool | None:
    if _RUST is None:
        return None
    try:
        result = bool(getattr(_RUST, "lock_workstation")())
        # On non-Windows builds current Rust implementation returns false stub.
        return True if result else None
    except Exception:
        return None


def _windows_idle_time_ms_py() -> int | None:
    if _platform_key() != "windows":
        return None
    try:
        class LASTINPUTINFO(ctypes.Structure):
            _fields_ = [("cbSize", ctypes.c_uint), ("dwTime", ctypes.c_uint)]

        user32 = ctypes.windll.user32
        kernel32 = ctypes.windll.kernel32
        info = LASTINPUTINFO()
        info.cbSize = ctypes.sizeof(LASTINPUTINFO)
        if not user32.GetLastInputInfo(ctypes.byref(info)):
            return None
        tick = int(kernel32.GetTickCount64())
        return max(0, tick - int(info.dwTime))
    except Exception:
        return None


def _windows_active_window_title_py() -> str | None:
    if _platform_key() != "windows":
        return None
    try:
        user32 = ctypes.windll.user32
        hwnd = user32.GetForegroundWindow()
        if not hwnd:
            return None
        length = int(user32.GetWindowTextLengthW(hwnd))
        if length <= 0:
            return None
        buffer = ctypes.create_unicode_buffer(length + 1)
        copied = int(user32.GetWindowTextW(hwnd, buffer, length + 1))
        if copied <= 0:
            return None
        return buffer.value.strip() or None
    except Exception:
        return None


def _windows_lock_workstation_py() -> bool:
    if _platform_key() != "windows":
        return False
    try:
        user32 = ctypes.windll.user32
        return bool(user32.LockWorkStation())
    except Exception:
        return False


def _macos_idle_time_ms() -> int | None:
    if _platform_key() != "macos":
        return None
    # `ioreg` exposes HIDIdleTime in nanoseconds.
    output = _run_capture(["ioreg", "-c", "IOHIDSystem"])
    if not output:
        return None
    match = re.search(r'"HIDIdleTime"\s*=\s*(\d+)', output)
    if not match:
        return None
    try:
        nanos = int(match.group(1))
        return max(0, nanos // 1_000_000)
    except Exception:
        return None


def _macos_active_window_title() -> str | None:
    if _platform_key() != "macos":
        return None

    # Requires Automation/Accessibility permissions for System Events on some setups.
    scripts = [
        'tell application "System Events"',
        'set p to first process whose frontmost is true',
        'set appName to name of p',
        'try',
        'set winName to name of front window of p',
        'on error',
        'set winName to ""',
        'end try',
        'if winName is "" then',
        'return appName',
        'else',
        'return appName & " — " & winName',
        'end if',
        'end tell',
    ]
    cmd = ["osascript"]
    for line in scripts:
        cmd.extend(["-e", line])
    output = _run_capture(cmd, timeout=3.0)
    if output and "not authorized" not in output.lower():
        return output.strip() or None
    return None


def _macos_lock_workstation() -> bool:
    if _platform_key() != "macos":
        return False
    cg_session = Path("/System/Library/CoreServices/Menu Extras/User.menu/Contents/Resources/CGSession")
    if cg_session.exists():
        if _run_ok([str(cg_session), "-suspend"], timeout=3.0):
            return True
    # Fallback: sleep display (often triggers lock if password-on-wake is enabled).
    return _run_ok(["pmset", "displaysleepnow"], timeout=3.0)


def _linux_idle_time_ms() -> int | None:
    if _platform_key() != "linux":
        return None
    for cmd in (["xprintidle"], ["xssstate", "-i"]):
        output = _run_capture(cmd)
        if not output:
            continue
        try:
            return max(0, int(float(output.strip())))
        except Exception:
            continue
    return None


def _linux_active_window_title() -> str | None:
    if _platform_key() != "linux":
        return None

    # X11 path via xdotool.
    output = _run_capture(["xdotool", "getactivewindow", "getwindowname"])
    if output:
        return output.strip() or None

    # Fallback via xprop.
    root = _run_capture(["xprop", "-root", "_NET_ACTIVE_WINDOW"])
    if not root:
        return None
    match = re.search(r"window id # (0x[0-9a-fA-F]+)", root)
    if not match:
        return None
    win_id = match.group(1)
    props = _run_capture(["xprop", "-id", win_id, "_NET_WM_NAME", "WM_NAME"], timeout=2.0)
    if not props:
        return None
    # Prefer _NET_WM_NAME(UTF8_STRING) and then WM_NAME
    q = re.search(r'=\s*"(.+)"', props)
    if q:
        return q.group(1).strip() or None
    return None


def _linux_lock_workstation() -> bool:
    if _platform_key() != "linux":
        return False
    candidates = [
        ["loginctl", "lock-session"],
        ["gnome-screensaver-command", "-l"],
        ["dm-tool", "lock"],
        ["qdbus", "org.freedesktop.ScreenSaver", "/ScreenSaver", "Lock"],
        ["qdbus-qt5", "org.freedesktop.ScreenSaver", "/ScreenSaver", "Lock"],
        ["qdbus6", "org.freedesktop.ScreenSaver", "/ScreenSaver", "Lock"],
    ]
    for cmd in candidates:
        if shutil.which(cmd[0]) and _run_ok(cmd, timeout=3.0):
            return True
    return False


@lru_cache(maxsize=1)
def capabilities() -> dict[str, Any]:
    key = _platform_key()

    idle_supported = False
    active_window_supported = False
    lock_supported = False

    if key == "windows":
        idle_supported = True
        active_window_supported = True
        lock_supported = True
    elif key == "macos":
        idle_supported = shutil.which("ioreg") is not None
        active_window_supported = shutil.which("osascript") is not None
        lock_supported = (
            Path("/System/Library/CoreServices/Menu Extras/User.menu/Contents/Resources/CGSession").exists()
            or shutil.which("pmset") is not None
        )
    elif key == "linux":
        idle_supported = any(shutil.which(cmd) for cmd in ("xprintidle", "xssstate"))
        active_window_supported = any(shutil.which(cmd) for cmd in ("xdotool", "xprop"))
        lock_supported = any(shutil.which(cmd) for cmd in ("loginctl", "gnome-screensaver-command", "dm-tool", "qdbus", "qdbus-qt5", "qdbus6"))

    return {
        "platform": key,
        "rust_loaded": _RUST is not None,
        "idle_time_ms": bool(idle_supported),
        "active_window_title": bool(active_window_supported),
        "lock_workstation": bool(lock_supported or key == "windows"),
    }


def idle_time_ms() -> int:
    value = _rust_idle_time_ms()
    if value is not None:
        return value

    key = _platform_key()
    if key == "windows":
        value = _windows_idle_time_ms_py()
    elif key == "macos":
        value = _macos_idle_time_ms()
    elif key == "linux":
        value = _linux_idle_time_ms()
    else:
        value = None

    return int(value or 0)


def active_window_title() -> str:
    value = _rust_active_window_title()
    if value:
        return value

    key = _platform_key()
    if key == "windows":
        value = _windows_active_window_title_py()
    elif key == "macos":
        value = _macos_active_window_title()
    elif key == "linux":
        value = _linux_active_window_title()
    else:
        value = None

    if value is None:
        return ""
    return str(value)


def lock_workstation() -> bool:
    value = _rust_lock_workstation()
    if value is True:
        return True

    key = _platform_key()
    if key == "windows":
        ok = _windows_lock_workstation_py()
    elif key == "macos":
        ok = _macos_lock_workstation()
    elif key == "linux":
        ok = _linux_lock_workstation()
    else:
        ok = False

    if not ok:
        _warn_once("lock_not_supported", "lock_workstation is unavailable on this environment (platform=%s)", key)
    return ok


def current_username() -> str:
    return os.environ.get("USERNAME") or os.environ.get("USER") or "unknown"


def monotonic_ms() -> int:
    return int(time.monotonic() * 1000)

