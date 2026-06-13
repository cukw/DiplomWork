from __future__ import annotations

import os
import platform
import socket
from datetime import UTC, datetime
from typing import Any

import psutil

from .launcher import is_elevated


def collect_system_inventory(capabilities: dict[str, Any] | None = None) -> dict[str, Any]:
    return {
        "hostname": socket.gethostname(),
        "fqdn": socket.getfqdn(),
        "platform": platform.platform(),
        "system": platform.system(),
        "release": platform.release(),
        "version": platform.version(),
        "machine": platform.machine(),
        "processor": platform.processor(),
        "python_version": platform.python_version(),
        "boot_time": _timestamp_iso(psutil.boot_time()),
        "cpu": _cpu_info(),
        "memory": _memory_info(),
        "disks": _disk_info(),
        "network_interfaces": _network_interfaces(),
        "current_user": _current_username(),
        "is_admin": is_elevated(),
        "capabilities": capabilities or {},
    }


def _cpu_info() -> dict[str, Any]:
    try:
        load_avg = os.getloadavg()
    except (AttributeError, OSError):
        load_avg = None
    return {
        "physical_cores": psutil.cpu_count(logical=False) or 0,
        "logical_cores": psutil.cpu_count(logical=True) or 0,
        "load_average": list(load_avg) if load_avg else [],
    }


def _memory_info() -> dict[str, int]:
    memory = psutil.virtual_memory()
    swap = psutil.swap_memory()
    return {
        "total_bytes": int(memory.total),
        "available_bytes": int(memory.available),
        "used_bytes": int(memory.used),
        "swap_total_bytes": int(swap.total),
        "swap_used_bytes": int(swap.used),
    }


def _disk_info() -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for partition in psutil.disk_partitions(all=False):
        if len(rows) >= 16:
            break
        try:
            usage = psutil.disk_usage(partition.mountpoint)
        except Exception:
            continue
        rows.append({
            "device": partition.device,
            "mountpoint": partition.mountpoint,
            "fstype": partition.fstype,
            "total_bytes": int(usage.total),
            "used_bytes": int(usage.used),
            "free_bytes": int(usage.free),
            "percent": float(usage.percent),
        })
    return rows


def _network_interfaces() -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    stats = psutil.net_if_stats()
    for name, addrs in psutil.net_if_addrs().items():
        if len(rows) >= 24:
            break
        mac = ""
        ips: list[str] = []
        for addr in addrs:
            family = str(getattr(addr.family, "name", addr.family)).upper()
            value = str(addr.address or "").strip()
            if not value:
                continue
            if family in {"AF_LINK", "AF_PACKET", "-1"}:
                mac = value
            elif "INET" in family:
                ips.append(value)
        stat = stats.get(name)
        rows.append({
            "name": name,
            "mac": mac,
            "ips": ips,
            "is_up": bool(stat.isup) if stat else None,
            "speed_mbps": int(stat.speed) if stat else 0,
        })
    return rows


def _timestamp_iso(value: float) -> str:
    try:
        return datetime.fromtimestamp(value, tz=UTC).isoformat().replace("+00:00", "Z")
    except Exception:
        return ""


def _current_username() -> str:
    return os.environ.get("USERNAME") or os.environ.get("USER") or os.environ.get("LOGNAME") or "unknown"
