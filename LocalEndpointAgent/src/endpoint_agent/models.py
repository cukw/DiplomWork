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
        }, ensure_ascii=False)

    @classmethod
    def from_json(cls, value: str) -> "ActivityEvent":
        raw = json.loads(value)
        return cls(**raw)
