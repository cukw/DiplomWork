from __future__ import annotations

from pathlib import Path
from typing import Any

import yaml
from pydantic import BaseModel, Field


class ServicesConfig(BaseModel):
    activity_service_url: str = "http://localhost:5001"
    agent_management_url: str = "http://localhost:5015"


class RuntimeConfig(BaseModel):
    state_dir: str = "./state"
    heartbeat_interval_sec: int = 15
    policy_refresh_interval_sec: int = 30
    flush_interval_sec: int = 5
    collection_interval_sec: int = 5
    max_batch_size: int = 100


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


class CollectorsConfig(BaseModel):
    processes: ProcessCollectorConfig = Field(default_factory=ProcessCollectorConfig)
    browser_history: BrowserCollectorConfig = Field(default_factory=BrowserCollectorConfig)
    active_window: ActiveWindowCollectorConfig = Field(default_factory=ActiveWindowCollectorConfig)
    idle_time: IdleCollectorConfig = Field(default_factory=IdleCollectorConfig)


class RiskConfig(BaseModel):
    local_high_risk_threshold: float = 85.0
    enable_auto_lock: bool = True


class AgentIdentityConfig(BaseModel):
    computer_id: int
    user_id: int | None = None
    version: str = "0.1.0"
    device_name: str = "unknown-device"


class AgentConfig(BaseModel):
    agent: AgentIdentityConfig
    services: ServicesConfig = Field(default_factory=ServicesConfig)
    runtime: RuntimeConfig = Field(default_factory=RuntimeConfig)
    collectors: CollectorsConfig = Field(default_factory=CollectorsConfig)
    risk: RiskConfig = Field(default_factory=RiskConfig)

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
    "idle_threshold_sec": 120,
    "browser_poll_interval_sec": 10,
    "process_snapshot_limit": 50,
    "high_risk_threshold": 85.0,
    "auto_lock_enabled": True,
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
