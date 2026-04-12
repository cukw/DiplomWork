from __future__ import annotations

from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Any
import json


def utc_now_iso() -> str:
    return datetime.now(UTC).isoformat().replace("+00:00", "Z")


@dataclass(slots=True)
class ActivityEvent:
    computer_id: int
    activity_type: str
    timestamp: str
    details: dict[str, Any] = field(default_factory=dict)
    duration_ms: int | None = None
    url: str = ""
    process_name: str = ""
    is_blocked: bool = False
    risk_score: float = 0.0
    synced: bool = False
    user_id: int | None = None
    agent_id: int | None = None
    agent_version: str = ""
    device_name: str = ""
    collector: str = ""
    event_id: str = ""
    sequence: int = 0
    batch_id: str = ""
    source_platform: str = ""

    def to_activity_reply_payload(self) -> dict[str, Any]:
        return {
            "id": 0,
            "computer_id": self.computer_id,
            "timestamp": self.timestamp,
            "activity_type": self.activity_type,
            "details": json.dumps(self.details, ensure_ascii=False),
            "duration_ms": self.duration_ms,
            "url": self.url,
            "process_name": self.process_name,
            "is_blocked": self.is_blocked,
            "risk_score": float(self.risk_score),
            "Synced": bool(self.synced),
            "user_id": self.user_id,
            "agent_id": self.agent_id,
            "agent_version": self.agent_version,
            "device_name": self.device_name,
            "collector": self.collector,
            "event_id": self.event_id,
            "sequence": int(self.sequence or 0),
            "batch_id": self.batch_id,
            "source_platform": self.source_platform,
        }

    def to_json(self) -> str:
        return json.dumps({
            "computer_id": self.computer_id,
            "activity_type": self.activity_type,
            "timestamp": self.timestamp,
            "details": self.details,
            "duration_ms": self.duration_ms,
            "url": self.url,
            "process_name": self.process_name,
            "is_blocked": self.is_blocked,
            "risk_score": self.risk_score,
            "synced": self.synced,
            "user_id": self.user_id,
            "agent_id": self.agent_id,
            "agent_version": self.agent_version,
            "device_name": self.device_name,
            "collector": self.collector,
            "event_id": self.event_id,
            "sequence": self.sequence,
            "batch_id": self.batch_id,
            "source_platform": self.source_platform,
        }, ensure_ascii=False)

    @classmethod
    def from_json(cls, value: str) -> "ActivityEvent":
        raw = json.loads(value)
        return cls(**raw)
