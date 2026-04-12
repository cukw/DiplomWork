from __future__ import annotations

import os
import platform
import shutil
import sqlite3
import tempfile
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Iterable
import logging

import psutil

from . import rust_bridge
from .models import ActivityEvent, utc_now_iso

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
            details={"idle_ms": idle_ms, "threshold_sec": threshold_sec, "agent_user_id": self.user_id},
            risk_score=0.0,
        )]


@dataclass
class BrowserHistoryCollector(Collector):
    computer_id: int
    user_id: int | None
    _last_seen: dict[str, int] = None  # type: ignore[assignment]

    def __post_init__(self) -> None:
        if self._last_seen is None:
            self._last_seen = {}

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
        last_seen = self._last_seen.get(browser, 0)
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
                details={
                    "browser": browser,
                    "title": title,
                    "visit_count": visit_count,
                    "agent_user_id": self.user_id,
                },
                risk_score=risk,
                is_blocked=risk >= 85.0,
            ))
        self._last_seen[browser] = max_seen
        return events

    def _collect_firefox(self, browser: str, db_file: Path) -> list[ActivityEvent]:
        last_seen = self._last_seen.get(browser, 0)
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
                details={"browser": browser, "title": title, "visit_count": visit_count, "agent_user_id": self.user_id},
                risk_score=risk,
                is_blocked=risk >= 85.0,
            ))
        self._last_seen[browser] = max_seen
        return events


def default_collectors(computer_id: int, user_id: int | None) -> list[Collector]:
    return [
        ProcessSnapshotCollector(computer_id=computer_id, user_id=user_id),
        ActiveWindowCollector(computer_id=computer_id, user_id=user_id),
        IdleTimeCollector(computer_id=computer_id, user_id=user_id),
        BrowserHistoryCollector(computer_id=computer_id, user_id=user_id),
    ]


def _looks_suspicious_url(url: str) -> bool:
    hay = url.lower()
    indicators = ["phish", "malware", "stealer", "credential", "free-crypto", ".ru/login"]
    return any(token in hay for token in indicators)


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
