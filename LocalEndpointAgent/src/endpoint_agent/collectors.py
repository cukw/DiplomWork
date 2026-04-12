from __future__ import annotations

import os
import platform
import hashlib
import ipaddress
import json
import plistlib
import shutil
import sqlite3
import subprocess
import tempfile
import time
from collections import Counter
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any
import logging

import psutil

from . import rust_bridge
from .models import ActivityEvent, utc_now_iso
from .state_store import AgentStateStore

logger = logging.getLogger(__name__)
_CAPS = rust_bridge.capabilities()


class Collector:
    def collect(self, policy: dict) -> list[ActivityEvent]:
        raise NotImplementedError


@dataclass
class ProcessSnapshotCollector(Collector):
    computer_id: int
    user_id: int | None

    def collect(self, policy: dict) -> list[ActivityEvent]:
        if not bool(policy.get("enable_process_collection", True)):
            return []

        limit = int(policy.get("process_snapshot_limit", 50) or 50)
        now = utc_now_iso()
        events: list[ActivityEvent] = []

        processes = []
        for proc in psutil.process_iter(["pid", "name", "username", "cpu_percent", "memory_info", "create_time", "cmdline"]):
            try:
                info = proc.info
                processes.append(info)
            except Exception:
                continue

        processes = sorted(processes, key=lambda p: float(p.get("cpu_percent") or 0), reverse=True)[:limit]
        for info in processes:
            proc_name = str(info.get("name") or "")
            suspicious = any(token in proc_name.lower() for token in ("mimikatz", "keylogger", "miner", "torrent"))
            risk = 90.0 if suspicious else 5.0
            details = {
                "pid": info.get("pid"),
                "user": info.get("username"),
                "cpu_percent": info.get("cpu_percent"),
                "rss": getattr(info.get("memory_info"), "rss", None),
                "cmdline": info.get("cmdline") or [],
                "started_at": _ts(info.get("create_time")),
                "agent_user_id": self.user_id,
            }
            events.append(ActivityEvent(
                computer_id=self.computer_id,
                activity_type="PROCESS_SNAPSHOT",
                timestamp=now,
                process_name=proc_name,
                collector="process_snapshot",
                details=details,
                risk_score=risk,
                is_blocked=suspicious,
            ))
        return events


@dataclass
class ActiveWindowCollector(Collector):
    computer_id: int
    user_id: int | None
    _last_title: str = ""
    _warned_unsupported: bool = False

    def collect(self, policy: dict) -> list[ActivityEvent]:
        if not bool(policy.get("enable_active_window_collection", True)):
            return []
        if not bool(_CAPS.get("active_window_title", False)):
            if not self._warned_unsupported:
                self._warned_unsupported = True
                logger.info("Active window collector disabled on platform=%s (capability unavailable)", _CAPS.get("platform"))
            return []
        title = rust_bridge.active_window_title().strip()
        if not title or title == self._last_title:
            return []
        self._last_title = title
        return [ActivityEvent(
            computer_id=self.computer_id,
            activity_type="ACTIVE_WINDOW_CHANGE",
            timestamp=utc_now_iso(),
            collector="active_window",
            details={"window_title": title, "agent_user_id": self.user_id},
            risk_score=1.0,
        )]


@dataclass
class IdleTimeCollector(Collector):
    computer_id: int
    user_id: int | None
    _idle_state: bool = False
    _warned_unsupported: bool = False

    def collect(self, policy: dict) -> list[ActivityEvent]:
        if not bool(policy.get("enable_idle_collection", True)):
            return []
        if not bool(_CAPS.get("idle_time_ms", False)):
            if not self._warned_unsupported:
                self._warned_unsupported = True
                logger.info("Idle time collector disabled on platform=%s (capability unavailable)", _CAPS.get("platform"))
            return []
        idle_ms = max(0, rust_bridge.idle_time_ms())
        threshold_sec = int(policy.get("idle_threshold_sec", 120) or 120)
        is_idle = idle_ms >= threshold_sec * 1000
        if is_idle == self._idle_state:
            return []
        self._idle_state = is_idle
        return [ActivityEvent(
            computer_id=self.computer_id,
            activity_type="USER_IDLE" if is_idle else "USER_ACTIVE",
            timestamp=utc_now_iso(),
            duration_ms=idle_ms,
            collector="idle_time",
            details={"idle_ms": idle_ms, "threshold_sec": threshold_sec, "agent_user_id": self.user_id},
            risk_score=0.0,
        )]


@dataclass
class BrowserHistoryCollector(Collector):
    computer_id: int
    user_id: int | None
    state_store: AgentStateStore
    _last_seen: dict[str, int] = None  # type: ignore[assignment]

    def __post_init__(self) -> None:
        if self._last_seen is None:
            saved = self.state_store.get("browser_last_seen", {})
            self._last_seen = saved if isinstance(saved, dict) else {}

    def collect(self, policy: dict) -> list[ActivityEvent]:
        if not bool(policy.get("enable_browser_collection", True)):
            return []

        events: list[ActivityEvent] = []
        for browser in (policy.get("browsers") or ["chrome", "edge", "firefox"]):
            browser = str(browser).lower()
            try:
                events.extend(self._collect_browser(browser))
            except Exception as exc:
                logger.debug("Browser collector error for %s: %s", browser, exc)
        return events

    def _collect_browser(self, browser: str) -> list[ActivityEvent]:
        db_path = _browser_history_path(browser)
        if not db_path or not db_path.exists():
            return []

        # Browser history DB is often locked; copy to temp first.
        with tempfile.TemporaryDirectory(prefix="agent_hist_") as tmp:
            copied = Path(tmp) / db_path.name
            shutil.copy2(db_path, copied)

            if browser in {"chrome", "edge"}:
                return self._collect_chromium(browser, copied)
            if browser == "firefox":
                return self._collect_firefox(browser, copied)
            return []

    def _collect_chromium(self, browser: str, db_file: Path) -> list[ActivityEvent]:
        last_seen = int(self._last_seen.get(browser, 0) or 0)
        if last_seen <= 0:
            with sqlite3.connect(db_file) as conn:
                row = conn.execute("SELECT MAX(last_visit_time) FROM urls").fetchone()
            self._save_last_seen(browser, int((row[0] if row else 0) or 0))
            return []

        query = (
            "SELECT url, title, visit_count, last_visit_time FROM urls "
            "WHERE last_visit_time > ? ORDER BY last_visit_time ASC LIMIT 50"
        )
        rows = []
        with sqlite3.connect(db_file) as conn:
            rows = conn.execute(query, (last_seen,)).fetchall()

        events: list[ActivityEvent] = []
        max_seen = last_seen
        for url, title, visit_count, last_visit_time in rows:
            if not url:
                continue
            ts = _webkit_ts_to_iso(int(last_visit_time))
            max_seen = max(max_seen, int(last_visit_time or 0))
            risk = 88.0 if _looks_suspicious_url(str(url)) else 2.0
            events.append(ActivityEvent(
                computer_id=self.computer_id,
                activity_type="BROWSER_VISIT",
                timestamp=ts,
                url=str(url),
                collector="browser_history",
                details={
                    "browser": browser,
                    "title": title,
                    "visit_count": visit_count,
                    "agent_user_id": self.user_id,
                },
                risk_score=risk,
                is_blocked=risk >= 85.0,
            ))
        self._save_last_seen(browser, max_seen)
        return events

    def _collect_firefox(self, browser: str, db_file: Path) -> list[ActivityEvent]:
        last_seen = int(self._last_seen.get(browser, 0) or 0)
        if last_seen <= 0:
            with sqlite3.connect(db_file) as conn:
                row = conn.execute("SELECT MAX(last_visit_date) FROM moz_places WHERE last_visit_date IS NOT NULL").fetchone()
            self._save_last_seen(browser, int((row[0] if row else 0) or 0))
            return []

        query = (
            "SELECT url, title, visit_count, last_visit_date FROM moz_places "
            "WHERE last_visit_date IS NOT NULL AND last_visit_date > ? "
            "ORDER BY last_visit_date ASC LIMIT 50"
        )
        with sqlite3.connect(db_file) as conn:
            rows = conn.execute(query, (last_seen,)).fetchall()

        events: list[ActivityEvent] = []
        max_seen = last_seen
        for url, title, visit_count, last_visit_date in rows:
            if not url:
                continue
            last_visit_date = int(last_visit_date or 0)
            max_seen = max(max_seen, last_visit_date)
            ts = datetime.fromtimestamp(last_visit_date / 1_000_000, tz=UTC).isoformat().replace("+00:00", "Z")
            risk = 88.0 if _looks_suspicious_url(str(url)) else 2.0
            events.append(ActivityEvent(
                computer_id=self.computer_id,
                activity_type="BROWSER_VISIT",
                timestamp=ts,
                url=str(url),
                collector="browser_history",
                details={"browser": browser, "title": title, "visit_count": visit_count, "agent_user_id": self.user_id},
                risk_score=risk,
                is_blocked=risk >= 85.0,
            ))
        self._save_last_seen(browser, max_seen)
        return events

    def _save_last_seen(self, browser: str, value: int) -> None:
        self._last_seen[browser] = int(value or 0)
        self.state_store.set("browser_last_seen", self._last_seen)


@dataclass
class NetworkConnectionCollector(Collector):
    computer_id: int
    user_id: int | None

    def collect(self, policy: dict) -> list[ActivityEvent]:
        if not bool(policy.get("enable_network_collection", True)):
            return []

        limit = int(policy.get("network_snapshot_limit", 50) or 50)
        suspicious_ports = {4444, 1337, 31337, 6667}
        events: list[ActivityEvent] = []

        try:
            connections = psutil.net_connections(kind="inet")
        except Exception as exc:
            logger.debug("Network collector failed to read connections: %s", exc)
            return []

        now = utc_now_iso()
        seen: set[tuple[Any, ...]] = set()
        for conn in connections:
            if len(events) >= limit:
                break
            remote = conn.raddr if conn.raddr else None
            if not remote or _is_loopback_address(getattr(remote, "ip", "")):
                continue
            local = conn.laddr if conn.laddr else None
            key = (
                getattr(local, "ip", ""),
                getattr(local, "port", 0),
                getattr(remote, "ip", ""),
                getattr(remote, "port", 0),
                conn.status,
                conn.pid,
            )
            if key in seen:
                continue
            seen.add(key)

            proc_name = _safe_process_name(conn.pid)
            remote_port = int(getattr(remote, "port", 0) or 0)
            risk = 88.0 if remote_port in suspicious_ports else (20.0 if not _is_private_address(getattr(remote, "ip", "")) else 8.0)
            events.append(ActivityEvent(
                computer_id=self.computer_id,
                activity_type="NETWORK_CONNECTION",
                timestamp=now,
                process_name=proc_name,
                url=f"tcp://{getattr(remote, 'ip', '')}:{remote_port}",
                collector="network_connections",
                details={
                    "pid": conn.pid,
                    "process_name": proc_name,
                    "local_ip": getattr(local, "ip", ""),
                    "local_port": getattr(local, "port", 0),
                    "remote_ip": getattr(remote, "ip", ""),
                    "remote_port": remote_port,
                    "status": conn.status,
                    "agent_user_id": self.user_id,
                },
                risk_score=risk,
                is_blocked=risk >= 85.0,
            ))
        return events


@dataclass
class FileActivityWatcherCollector(Collector):
    computer_id: int
    user_id: int | None
    state_store: AgentStateStore

    def collect(self, policy: dict) -> list[ActivityEvent]:
        if not bool(policy.get("enable_file_collection", True)):
            return []

        max_files = int(policy.get("file_watch_max_files", 200) or 200)
        roots = _file_watch_roots(policy)
        snapshot = _scan_files(roots, max_files=max_files)
        state = self.state_store.get("file_watch_snapshot", None)
        if not isinstance(state, dict):
            self.state_store.set("file_watch_snapshot", snapshot)
            return [ActivityEvent(
                computer_id=self.computer_id,
                activity_type="FILE_WATCHER_READY",
                timestamp=utc_now_iso(),
                collector="file_activity",
                details={
                    "roots": [str(p) for p in roots],
                    "tracked_files": len(snapshot),
                    "agent_user_id": self.user_id,
                },
                risk_score=0.0,
            )]

        events: list[ActivityEvent] = []
        now = utc_now_iso()
        for path, meta in snapshot.items():
            old = state.get(path)
            if old is None:
                events.append(self._event("FILE_CREATED", path, meta, now))
            elif old.get("mtime_ns") != meta.get("mtime_ns") or old.get("size") != meta.get("size"):
                events.append(self._event("FILE_MODIFIED", path, meta, now))
            if len(events) >= max_files:
                break

        if len(events) < max_files:
            for path, meta in state.items():
                if path not in snapshot:
                    events.append(self._event("FILE_DELETED", path, meta, now))
                if len(events) >= max_files:
                    break

        self.state_store.set("file_watch_snapshot", snapshot)
        return events

    def _event(self, activity_type: str, path: str, meta: dict[str, Any], timestamp: str) -> ActivityEvent:
        risk = 80.0 if _looks_sensitive_file(path) else 10.0
        return ActivityEvent(
            computer_id=self.computer_id,
            activity_type=activity_type,
            timestamp=timestamp,
            collector="file_activity",
            details={
                "file_path": path,
                "file_name": Path(path).name,
                "extension": Path(path).suffix.lower(),
                "size_bytes": meta.get("size"),
                "mtime_ns": meta.get("mtime_ns"),
                "agent_user_id": self.user_id,
            },
            risk_score=risk,
            is_blocked=False,
        )


@dataclass
class UsbDeviceCollector(Collector):
    computer_id: int
    user_id: int | None
    state_store: AgentStateStore

    def collect(self, policy: dict) -> list[ActivityEvent]:
        if not bool(policy.get("enable_usb_collection", True)):
            return []

        interval = int(policy.get("usb_poll_interval_sec", 30) or 30)
        state = self.state_store.get("usb_devices", {})
        if not isinstance(state, dict):
            state = {}
        now_ts = time.time()
        if "last_scan_ts" in state and now_ts - float(state.get("last_scan_ts", 0) or 0) < interval:
            return []

        current = {device["id"]: device for device in _list_usb_devices()}
        previous = state.get("devices")
        if not isinstance(previous, dict):
            self.state_store.set("usb_devices", {"last_scan_ts": now_ts, "devices": current})
            return [ActivityEvent(
                computer_id=self.computer_id,
                activity_type="USB_INVENTORY",
                timestamp=utc_now_iso(),
                collector="usb_devices",
                details={
                    "device_count": len(current),
                    "devices": list(current.values())[:50],
                    "agent_user_id": self.user_id,
                },
                risk_score=5.0 if current else 0.0,
            )]

        events: list[ActivityEvent] = []
        timestamp = utc_now_iso()
        for device_id, device in current.items():
            if device_id not in previous:
                events.append(ActivityEvent(
                    computer_id=self.computer_id,
                    activity_type="USB_DEVICE_ATTACHED",
                    timestamp=timestamp,
                    collector="usb_devices",
                    details={"device": device, "agent_user_id": self.user_id},
                    risk_score=35.0,
                ))
        for device_id, device in previous.items():
            if device_id not in current:
                events.append(ActivityEvent(
                    computer_id=self.computer_id,
                    activity_type="USB_DEVICE_REMOVED",
                    timestamp=timestamp,
                    collector="usb_devices",
                    details={"device": device, "agent_user_id": self.user_id},
                    risk_score=5.0,
                ))

        self.state_store.set("usb_devices", {"last_scan_ts": now_ts, "devices": current})
        return events


@dataclass
class InventoryCollector(Collector):
    computer_id: int
    user_id: int | None
    state_store: AgentStateStore

    def collect(self, policy: dict) -> list[ActivityEvent]:
        if not bool(policy.get("enable_inventory_collection", True)):
            return []

        interval = int(policy.get("inventory_interval_sec", 3600) or 3600)
        state = self.state_store.get("inventory", {})
        if not isinstance(state, dict):
            state = {}
        now_ts = time.time()
        if "last_run_ts" in state and now_ts - float(state.get("last_run_ts", 0) or 0) < interval:
            return []

        max_apps = int(policy.get("inventory_max_apps", 200) or 200)
        max_processes = int(policy.get("inventory_max_processes", 200) or 200)
        apps = _installed_apps(max_apps=max_apps)
        processes = _process_inventory(max_processes=max_processes)
        timestamp = utc_now_iso()

        self.state_store.set("inventory", {"last_run_ts": now_ts, "last_run_at": timestamp})
        return [
            ActivityEvent(
                computer_id=self.computer_id,
                activity_type="INSTALLED_APPS_INVENTORY",
                timestamp=timestamp,
                collector="inventory",
                details={"count": len(apps), "apps": apps, "agent_user_id": self.user_id},
                risk_score=0.0,
            ),
            ActivityEvent(
                computer_id=self.computer_id,
                activity_type="PROCESS_INVENTORY",
                timestamp=timestamp,
                collector="inventory",
                details={"count": len(processes), "processes": processes, "agent_user_id": self.user_id},
                risk_score=0.0,
            ),
        ]


@dataclass
class SessionEventCollector(Collector):
    computer_id: int
    user_id: int | None
    state_store: AgentStateStore

    def collect(self, policy: dict) -> list[ActivityEvent]:
        if not bool(policy.get("enable_session_collection", True)):
            return []

        username = rust_bridge.current_username()
        idle_ms = rust_bridge.idle_time_ms() if bool(_CAPS.get("idle_time_ms", False)) else 0
        threshold_sec = int(policy.get("idle_threshold_sec", 120) or 120)
        locked = idle_ms >= threshold_sec * 1000 if idle_ms else False
        state = self.state_store.get("session", {})
        if not isinstance(state, dict):
            state = {}

        events: list[ActivityEvent] = []
        timestamp = utc_now_iso()
        last_user = str(state.get("username") or "")
        last_locked = bool(state.get("locked", False))

        if not last_user:
            events.append(self._event("SESSION_LOGIN", timestamp, username, idle_ms, locked))
        elif last_user != username:
            events.append(self._event("SESSION_LOGOUT", timestamp, last_user, idle_ms, locked))
            events.append(self._event("SESSION_LOGIN", timestamp, username, idle_ms, locked))

        if locked != last_locked:
            events.append(self._event("SESSION_LOCKED" if locked else "SESSION_UNLOCKED", timestamp, username, idle_ms, locked))

        self.state_store.set("session", {"username": username, "locked": locked, "last_seen_at": timestamp})
        return events

    def _event(self, activity_type: str, timestamp: str, username: str, idle_ms: int, locked: bool) -> ActivityEvent:
        return ActivityEvent(
            computer_id=self.computer_id,
            activity_type=activity_type,
            timestamp=timestamp,
            collector="session_events",
            details={
                "username": username,
                "idle_ms": idle_ms,
                "locked": locked,
                "agent_user_id": self.user_id,
            },
            risk_score=0.0,
        )


def default_collectors(computer_id: int, user_id: int | None, state_store: AgentStateStore) -> list[Collector]:
    return [
        ProcessSnapshotCollector(computer_id=computer_id, user_id=user_id),
        ActiveWindowCollector(computer_id=computer_id, user_id=user_id),
        IdleTimeCollector(computer_id=computer_id, user_id=user_id),
        BrowserHistoryCollector(computer_id=computer_id, user_id=user_id, state_store=state_store),
        NetworkConnectionCollector(computer_id=computer_id, user_id=user_id),
        FileActivityWatcherCollector(computer_id=computer_id, user_id=user_id, state_store=state_store),
        UsbDeviceCollector(computer_id=computer_id, user_id=user_id, state_store=state_store),
        InventoryCollector(computer_id=computer_id, user_id=user_id, state_store=state_store),
        SessionEventCollector(computer_id=computer_id, user_id=user_id, state_store=state_store),
    ]


def _looks_suspicious_url(url: str) -> bool:
    hay = url.lower()
    indicators = ["phish", "malware", "stealer", "credential", "free-crypto", ".ru/login"]
    return any(token in hay for token in indicators)


def _safe_process_name(pid: int | None) -> str:
    if not pid:
        return ""
    try:
        return psutil.Process(pid).name()
    except Exception:
        return ""


def _is_loopback_address(value: str) -> bool:
    try:
        return ipaddress.ip_address(value).is_loopback
    except Exception:
        return value in {"localhost", "127.0.0.1", "::1"}


def _is_private_address(value: str) -> bool:
    try:
        ip = ipaddress.ip_address(value)
        return ip.is_private or ip.is_loopback or ip.is_link_local
    except Exception:
        return False


def _file_watch_roots(policy: dict) -> list[Path]:
    configured = [str(p).strip() for p in (policy.get("file_watch_paths") or []) if str(p).strip()]
    if configured:
        roots = [Path(p).expanduser() for p in configured]
    else:
        home = Path.home()
        roots = [home / "Desktop", home / "Documents", home / "Downloads"]
    return [p for p in roots if p.exists() and p.is_dir()]


def _scan_files(roots: list[Path], *, max_files: int) -> dict[str, dict[str, Any]]:
    rows: list[tuple[float, str, dict[str, Any]]] = []
    for root in roots:
        try:
            iterator = root.rglob("*")
            for path in iterator:
                if len(rows) >= max_files * max(1, len(roots)):
                    break
                try:
                    if not path.is_file():
                        continue
                    stat = path.stat()
                    rows.append((
                        stat.st_mtime,
                        str(path),
                        {
                            "size": int(stat.st_size),
                            "mtime_ns": int(stat.st_mtime_ns),
                        },
                    ))
                except Exception:
                    continue
        except Exception as exc:
            logger.debug("Skipping file root %s: %s", root, exc)

    rows.sort(key=lambda item: item[0], reverse=True)
    return {path: meta for _, path, meta in rows[:max_files]}


def _looks_sensitive_file(path: str) -> bool:
    hay = path.lower()
    return any(token in hay for token in ("password", "credential", "secret", "token", "wallet", "private", ".pem", ".key", ".pfx", ".kdbx"))


def _run_command(cmd: list[str], timeout: float = 6.0) -> str:
    try:
        completed = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, check=False)
        if completed.returncode != 0:
            return ""
        return (completed.stdout or "").strip()
    except Exception:
        return ""


def _device_id(raw: dict[str, Any]) -> str:
    preferred = "|".join(str(raw.get(k) or "") for k in ("vendor_id", "product_id", "serial", "name", "device_id"))
    return hashlib.sha256(preferred.encode("utf-8", errors="ignore")).hexdigest()[:24]


def _list_usb_devices() -> list[dict[str, Any]]:
    key = platform.system().lower()
    if os.name == "nt":
        return _windows_usb_devices()
    if key == "darwin":
        return _macos_usb_devices()
    if key == "linux":
        return _linux_usb_devices()
    return []


def _windows_usb_devices() -> list[dict[str, Any]]:
    shell = "powershell"
    if not shutil.which(shell) and shutil.which("pwsh"):
        shell = "pwsh"
    if not shutil.which(shell):
        return []
    script = (
        "Get-PnpDevice -PresentOnly | "
        "Where-Object { $_.InstanceId -like 'USB*' -or $_.Class -in @('USB','WPD','DiskDrive') } | "
        "Select-Object FriendlyName,Class,InstanceId,Status | ConvertTo-Json -Compress"
    )
    output = _run_command([shell, "-NoProfile", "-Command", script], timeout=8.0)
    if not output:
        return []
    try:
        parsed = json.loads(output)
        rows = parsed if isinstance(parsed, list) else [parsed]
    except Exception:
        return []
    devices: list[dict[str, Any]] = []
    for row in rows:
        if not isinstance(row, dict):
            continue
        item = {
            "name": row.get("FriendlyName") or row.get("InstanceId") or "USB device",
            "class": row.get("Class") or "",
            "device_id": row.get("InstanceId") or "",
            "status": row.get("Status") or "",
        }
        item["id"] = _device_id(item)
        devices.append(item)
    return devices


def _macos_usb_devices() -> list[dict[str, Any]]:
    output = _run_command(["system_profiler", "SPUSBDataType", "-json"], timeout=10.0)
    if not output:
        return []
    try:
        parsed = json.loads(output)
    except Exception:
        return []
    devices: list[dict[str, Any]] = []

    def walk(node: Any) -> None:
        if isinstance(node, dict):
            name = node.get("_name")
            if name:
                item = {
                    "name": name,
                    "vendor_id": node.get("vendor_id") or "",
                    "product_id": node.get("product_id") or "",
                    "serial": node.get("serial_num") or "",
                    "manufacturer": node.get("manufacturer") or "",
                }
                item["id"] = _device_id(item)
                devices.append(item)
            for child in node.get("_items", []) or []:
                walk(child)
        elif isinstance(node, list):
            for child in node:
                walk(child)

    walk(parsed.get("SPUSBDataType", []))
    return devices


def _linux_usb_devices() -> list[dict[str, Any]]:
    output = _run_command(["lsusb"], timeout=4.0) if shutil.which("lsusb") else ""
    devices: list[dict[str, Any]] = []
    if output:
        for line in output.splitlines():
            parts = line.split(" ", 6)
            if len(parts) < 7:
                continue
            ids = parts[5].split(":", 1)
            item = {
                "name": parts[6].strip(),
                "vendor_id": ids[0] if ids else "",
                "product_id": ids[1] if len(ids) > 1 else "",
                "device_id": line.strip(),
            }
            item["id"] = _device_id(item)
            devices.append(item)
        return devices

    sys_usb = Path("/sys/bus/usb/devices")
    if not sys_usb.exists():
        return []
    for dev in sys_usb.iterdir():
        try:
            vendor = (dev / "idVendor").read_text(encoding="utf-8").strip()
            product = (dev / "idProduct").read_text(encoding="utf-8").strip()
            name = (dev / "product").read_text(encoding="utf-8").strip() if (dev / "product").exists() else dev.name
        except Exception:
            continue
        item = {"name": name, "vendor_id": vendor, "product_id": product, "device_id": dev.name}
        item["id"] = _device_id(item)
        devices.append(item)
    return devices


def _installed_apps(*, max_apps: int) -> list[dict[str, Any]]:
    if os.name == "nt":
        apps = _windows_installed_apps()
    elif platform.system() == "Darwin":
        apps = _macos_installed_apps()
    else:
        apps = _linux_installed_apps()
    return apps[:max_apps]


def _windows_installed_apps() -> list[dict[str, Any]]:
    try:
        import winreg  # type: ignore
    except Exception:
        return []

    roots = [
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (winreg.HKEY_CURRENT_USER, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    ]
    apps: dict[str, dict[str, Any]] = {}
    for hive, key_path in roots:
        try:
            with winreg.OpenKey(hive, key_path) as key:
                for idx in range(0, winreg.QueryInfoKey(key)[0]):
                    try:
                        sub_name = winreg.EnumKey(key, idx)
                        with winreg.OpenKey(key, sub_name) as sub:
                            name = _winreg_value(sub, "DisplayName")
                            if not name:
                                continue
                            item = {
                                "name": name,
                                "version": _winreg_value(sub, "DisplayVersion"),
                                "publisher": _winreg_value(sub, "Publisher"),
                            }
                            apps[name.lower()] = item
                    except Exception:
                        continue
        except Exception:
            continue
    return sorted(apps.values(), key=lambda x: str(x.get("name", "")).lower())


def _winreg_value(key: Any, name: str) -> str:
    try:
        value, _ = __import__("winreg").QueryValueEx(key, name)
        return str(value or "").strip()
    except Exception:
        return ""


def _macos_installed_apps() -> list[dict[str, Any]]:
    apps: dict[str, dict[str, Any]] = {}
    for root in [Path("/Applications"), Path.home() / "Applications"]:
        if not root.exists():
            continue
        for app in root.glob("*.app"):
            name = app.stem
            version = ""
            info = app / "Contents" / "Info.plist"
            if info.exists():
                try:
                    with info.open("rb") as f:
                        plist = plistlib.load(f)
                    version = str(plist.get("CFBundleShortVersionString") or plist.get("CFBundleVersion") or "")
                except Exception:
                    version = ""
            apps[name.lower()] = {"name": name, "version": version, "path": str(app)}
    return sorted(apps.values(), key=lambda x: str(x.get("name", "")).lower())


def _linux_installed_apps() -> list[dict[str, Any]]:
    output = _run_command(["dpkg-query", "-W", "-f=${Package}\t${Version}\n"], timeout=8.0) if shutil.which("dpkg-query") else ""
    apps: list[dict[str, Any]] = []
    if output:
        for line in output.splitlines():
            parts = line.split("\t", 1)
            apps.append({"name": parts[0], "version": parts[1] if len(parts) > 1 else "", "source": "dpkg"})
        return sorted(apps, key=lambda x: str(x.get("name", "")).lower())

    output = _run_command(["rpm", "-qa", "--qf", "%{NAME}\t%{VERSION}-%{RELEASE}\n"], timeout=8.0) if shutil.which("rpm") else ""
    if output:
        for line in output.splitlines():
            parts = line.split("\t", 1)
            apps.append({"name": parts[0], "version": parts[1] if len(parts) > 1 else "", "source": "rpm"})
    return sorted(apps, key=lambda x: str(x.get("name", "")).lower())


def _process_inventory(*, max_processes: int) -> list[dict[str, Any]]:
    counter: Counter[str] = Counter()
    for proc in psutil.process_iter(["name"]):
        try:
            name = str(proc.info.get("name") or "").strip()
            if name:
                counter[name] += 1
        except Exception:
            continue
    return [{"name": name, "count": count} for name, count in counter.most_common(max_processes)]


def _ts(epoch_seconds: float | None) -> str | None:
    if not epoch_seconds:
        return None
    try:
        return datetime.fromtimestamp(epoch_seconds, tz=UTC).isoformat().replace("+00:00", "Z")
    except Exception:
        return None


def _webkit_ts_to_iso(value: int) -> str:
    # Chromium: microseconds since 1601-01-01 UTC
    unix_microseconds = value - 11644473600000000
    dt = datetime.fromtimestamp(unix_microseconds / 1_000_000, tz=UTC)
    return dt.isoformat().replace("+00:00", "Z")


def _browser_history_path(browser: str) -> Path | None:
    home = Path.home()
    if os.name == "nt":
        local = Path(os.environ.get("LOCALAPPDATA", ""))
        roaming = Path(os.environ.get("APPDATA", ""))
        paths = {
            "chrome": local / "Google/Chrome/User Data/Default/History",
            "edge": local / "Microsoft/Edge/User Data/Default/History",
            "firefox": _latest_firefox_places(roaming / "Mozilla/Firefox/Profiles"),
        }
    elif platform.system() == "Darwin":
        paths = {
            "chrome": home / "Library/Application Support/Google/Chrome/Default/History",
            "edge": home / "Library/Application Support/Microsoft Edge/Default/History",
            "firefox": _latest_firefox_places(home / "Library/Application Support/Firefox/Profiles"),
        }
    else:
        paths = {
            "chrome": home / ".config/google-chrome/Default/History",
            "edge": home / ".config/microsoft-edge/Default/History",
            "firefox": _latest_firefox_places(home / ".mozilla/firefox"),
        }

    path = paths.get(browser)
    if isinstance(path, Path):
        return path
    return None


def _latest_firefox_places(profiles_root: Path) -> Path | None:
    if not profiles_root.exists():
        return None
    candidates = sorted(profiles_root.glob("*.default*/places.sqlite"), key=lambda p: p.stat().st_mtime if p.exists() else 0, reverse=True)
    return candidates[0] if candidates else None
