from __future__ import annotations

from pathlib import Path
from typing import Any

import yaml
from pydantic import BaseModel, Field

from .prod_defaults import (
    DEFAULT_ACTIVITY_SERVICE_URL,
    DEFAULT_AGENT_AUTH_HEADER,
    DEFAULT_AGENT_AUTH_TOKEN,
    DEFAULT_AGENT_MANAGEMENT_URL,
    DEFAULT_GATEWAY_TLS_INSECURE,
    DEFAULT_GATEWAY_URL,
)


class ServicesConfig(BaseModel):
    gateway_url: str = DEFAULT_GATEWAY_URL
    gateway_tls_insecure: bool = DEFAULT_GATEWAY_TLS_INSECURE
    activity_service_url: str = DEFAULT_ACTIVITY_SERVICE_URL
    agent_management_url: str = DEFAULT_AGENT_MANAGEMENT_URL


class RuntimeConfig(BaseModel):
    state_dir: str = "./state"
    heartbeat_interval_sec: int = 15
    policy_refresh_interval_sec: int = 30
    flush_interval_sec: int = 5
    collection_interval_sec: int = 5
    max_batch_size: int = 100
    max_queue_size: int = 10_000


class ProcessCollectorConfig(BaseModel):
    enabled: bool = True
    snapshot_limit: int = 50


class BrowserCollectorConfig(BaseModel):
    enabled: bool = True
    poll_interval_sec: int = 10
    browsers: list[str] = Field(default_factory=lambda: ["chrome", "edge", "firefox"])


class ActiveWindowCollectorConfig(BaseModel):
    enabled: bool = True


class IdleCollectorConfig(BaseModel):
    enabled: bool = True
    idle_threshold_sec: int = 120


class NetworkCollectorConfig(BaseModel):
    enabled: bool = True
    snapshot_limit: int = 50


class FileActivityCollectorConfig(BaseModel):
    enabled: bool = True
    paths: list[str] = Field(default_factory=list)
    max_files_per_scan: int = 200


class UsbCollectorConfig(BaseModel):
    enabled: bool = True
    poll_interval_sec: int = 30


class InventoryCollectorConfig(BaseModel):
    enabled: bool = True
    interval_sec: int = 3600
    max_apps: int = 200
    max_processes: int = 200


class SessionCollectorConfig(BaseModel):
    enabled: bool = True


class CollectorsConfig(BaseModel):
    processes: ProcessCollectorConfig = Field(default_factory=ProcessCollectorConfig)
    browser_history: BrowserCollectorConfig = Field(default_factory=BrowserCollectorConfig)
    active_window: ActiveWindowCollectorConfig = Field(default_factory=ActiveWindowCollectorConfig)
    idle_time: IdleCollectorConfig = Field(default_factory=IdleCollectorConfig)
    network: NetworkCollectorConfig = Field(default_factory=NetworkCollectorConfig)
    file_activity: FileActivityCollectorConfig = Field(default_factory=FileActivityCollectorConfig)
    usb_devices: UsbCollectorConfig = Field(default_factory=UsbCollectorConfig)
    inventory: InventoryCollectorConfig = Field(default_factory=InventoryCollectorConfig)
    session: SessionCollectorConfig = Field(default_factory=SessionCollectorConfig)


class RiskConfig(BaseModel):
    local_high_risk_threshold: float = 85.0
    enable_auto_lock: bool = True


class ControlPlaneSigningConfig(BaseModel):
    secret: str | None = None
    key_id: str = "default"
    allow_unsigned: bool = True


class AgentTransportAuthConfig(BaseModel):
    token: str | None = DEFAULT_AGENT_AUTH_TOKEN
    header_name: str = DEFAULT_AGENT_AUTH_HEADER


class SecurityConfig(BaseModel):
    control_plane_signing: ControlPlaneSigningConfig = Field(default_factory=ControlPlaneSigningConfig)
    agent_transport_auth: AgentTransportAuthConfig = Field(default_factory=AgentTransportAuthConfig)


class AgentIdentityConfig(BaseModel):
    computer_id: int = 0
    user_id: int | None = None
    session_id: int | None = None
    session_expires_at: str | None = None
    auth_refresh_token: str | None = None
    version: str = "0.1.0"
    device_name: str = "unknown-device"


class AgentConfig(BaseModel):
    agent: AgentIdentityConfig
    services: ServicesConfig = Field(default_factory=ServicesConfig)
    runtime: RuntimeConfig = Field(default_factory=RuntimeConfig)
    collectors: CollectorsConfig = Field(default_factory=CollectorsConfig)
    risk: RiskConfig = Field(default_factory=RiskConfig)
    security: SecurityConfig = Field(default_factory=SecurityConfig)

    @property
    def state_dir_path(self) -> Path:
        return Path(self.runtime.state_dir).expanduser().resolve()


DEFAULT_POLICY: dict[str, Any] = {
    "version": "local-default",
    "updated_at": None,
    "collection_interval_sec": 5,
    "heartbeat_interval_sec": 15,
    "flush_interval_sec": 5,
    "enable_process_collection": True,
    "enable_browser_collection": True,
    "enable_active_window_collection": True,
    "enable_idle_collection": True,
    "enable_network_collection": True,
    "enable_file_collection": True,
    "enable_usb_collection": True,
    "enable_inventory_collection": True,
    "enable_session_collection": True,
    "idle_threshold_sec": 120,
    "browser_poll_interval_sec": 10,
    "process_snapshot_limit": 50,
    "network_snapshot_limit": 50,
    "file_watch_paths": [],
    "file_watch_max_files": 200,
    "usb_poll_interval_sec": 30,
    "inventory_interval_sec": 3600,
    "inventory_max_apps": 200,
    "inventory_max_processes": 200,
    "high_risk_threshold": 85.0,
    "auto_lock_enabled": True,
    "enable_whitelist": False,
    "enable_blacklist": False,
    "whitelist_apps": [],
    "blacklist_apps": [],
    "admin_blocked": False,
    "blocked_reason": None,
}


def load_config(path: str | Path) -> AgentConfig:
    config_path = Path(path).expanduser().resolve()
    with config_path.open("r", encoding="utf-8") as f:
        raw = yaml.safe_load(f) or {}
    cfg = AgentConfig.model_validate(raw)
    cfg.state_dir_path.mkdir(parents=True, exist_ok=True)
    return cfg


def default_config_path() -> Path:
    local_config = Path("config/agent.local.yaml")
    if local_config.exists():
        return local_config.resolve()

    home = Path.home()
    if _is_windows():
        import os

        root = Path(os.environ.get("LOCALAPPDATA", str(home / "AppData/Local"))) / "LocalEndpointAgent"
        return root / "config" / "agent.local.yaml"

    if _is_macos():
        return home / "Library" / "Application Support" / "LocalEndpointAgent" / "config" / "agent.local.yaml"

    return home / ".local" / "share" / "local-endpoint-agent" / "config" / "agent.local.yaml"


def resolve_config_path(path: str | Path | None) -> Path:
    return Path(path).expanduser().resolve() if path else default_config_path()


def ensure_config(path: str | Path | None = None) -> Path:
    config_path = resolve_config_path(path)
    if config_path.exists():
        return config_path

    config_path.parent.mkdir(parents=True, exist_ok=True)
    state_dir = config_path.parent.parent / "state"
    raw = {
        "agent": {
            "computer_id": 0,
            "user_id": None,
            "session_id": None,
            "session_expires_at": None,
            "auth_refresh_token": None,
            "version": "0.1.0",
            "device_name": "unknown-device",
        },
        "services": {
            "gateway_url": DEFAULT_GATEWAY_URL,
            "gateway_tls_insecure": DEFAULT_GATEWAY_TLS_INSECURE,
            "activity_service_url": DEFAULT_ACTIVITY_SERVICE_URL,
            "agent_management_url": DEFAULT_AGENT_MANAGEMENT_URL,
        },
        "runtime": {
            "state_dir": str(state_dir).replace("\\", "/"),
            "heartbeat_interval_sec": 15,
            "policy_refresh_interval_sec": 30,
            "flush_interval_sec": 5,
            "collection_interval_sec": 5,
            "max_batch_size": 100,
            "max_queue_size": 10_000,
        },
        "security": {
            "agent_transport_auth": {
                "token": DEFAULT_AGENT_AUTH_TOKEN,
                "header_name": DEFAULT_AGENT_AUTH_HEADER,
            },
            "control_plane_signing": {
                "secret": "",
                "key_id": "default",
                "allow_unsigned": True,
            },
        },
    }
    with config_path.open("w", encoding="utf-8") as f:
        yaml.safe_dump(raw, f, allow_unicode=True, sort_keys=False)
    return config_path


def load_or_create_config(path: str | Path | None = None) -> tuple[Path, AgentConfig]:
    config_path = ensure_config(path)
    return config_path, load_config(config_path)


def _is_windows() -> bool:
    import os

    return os.name == "nt"


def _is_macos() -> bool:
    import platform

    return platform.system().lower() == "darwin"
