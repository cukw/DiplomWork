from __future__ import annotations

from pathlib import Path
from typing import Any
import json


class PolicyCache:
    def __init__(self, state_dir: Path) -> None:
        self.path = state_dir / "policy_cache.json"

    def load(self) -> dict[str, Any]:
        if not self.path.exists():
            return {}
        try:
            data = json.loads(self.path.read_text(encoding="utf-8"))
            return data if isinstance(data, dict) else {}
        except Exception:
            return {}

    def save(self, policy: dict[str, Any]) -> None:
        self.path.write_text(json.dumps(policy, ensure_ascii=False, indent=2), encoding="utf-8")
