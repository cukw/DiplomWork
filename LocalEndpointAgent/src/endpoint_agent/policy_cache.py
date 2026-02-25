from __future__ import annotations

from pathlib import Path
from typing import Any
import json

from .config import DEFAULT_POLICY


class PolicyCache:
    def __init__(self, state_dir: Path) -> None:
        self.path = state_dir / "policy_cache.json"

    def load(self) -> dict[str, Any]:
        if not self.path.exists():
            return dict(DEFAULT_POLICY)
        try:
            data = json.loads(self.path.read_text(encoding="utf-8"))
            merged = dict(DEFAULT_POLICY)
            merged.update(data)
            return merged
        except Exception:
            return dict(DEFAULT_POLICY)

    def save(self, policy: dict[str, Any]) -> None:
        merged = dict(DEFAULT_POLICY)
        merged.update(policy)
        self.path.write_text(json.dumps(merged, ensure_ascii=False, indent=2), encoding="utf-8")
