from __future__ import annotations

import json
import threading
from pathlib import Path
from typing import Any


class AgentStateStore:
    def __init__(self, state_dir: Path) -> None:
        self.path = state_dir / "agent_state.json"
        self._lock = threading.Lock()
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def get(self, key: str, default: Any = None) -> Any:
        with self._lock:
            data = self._read()
            return data.get(key, default)

    def set(self, key: str, value: Any) -> None:
        with self._lock:
            data = self._read()
            data[key] = value
            self._write(data)

    def update_section(self, key: str, values: dict[str, Any]) -> dict[str, Any]:
        with self._lock:
            data = self._read()
            section = data.get(key)
            if not isinstance(section, dict):
                section = {}
            section.update(values)
            data[key] = section
            self._write(data)
            return section

    def next_sequence(self) -> int:
        with self._lock:
            data = self._read()
            value = int(data.get("sequence", 0) or 0) + 1
            data["sequence"] = value
            self._write(data)
            return value

    def _read(self) -> dict[str, Any]:
        if not self.path.exists():
            return {}
        try:
            raw = json.loads(self.path.read_text(encoding="utf-8"))
            return raw if isinstance(raw, dict) else {}
        except Exception:
            return {}

    def _write(self, data: dict[str, Any]) -> None:
        tmp = self.path.with_suffix(".tmp")
        tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2, sort_keys=True), encoding="utf-8")
        tmp.replace(self.path)
