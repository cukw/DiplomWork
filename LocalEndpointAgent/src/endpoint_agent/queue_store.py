from __future__ import annotations

import sqlite3
from pathlib import Path
from typing import Iterable

from .models import ActivityEvent


class OfflineQueueStore:
    def __init__(self, state_dir: Path) -> None:
        self.db_path = state_dir / "agent_queue.sqlite3"
        self._init_db()

    def _connect(self) -> sqlite3.Connection:
        conn = sqlite3.connect(self.db_path)
        conn.row_factory = sqlite3.Row
        return conn

    def _init_db(self) -> None:
        with self._connect() as conn:
            conn.executescript(
                """
                CREATE TABLE IF NOT EXISTS activity_queue (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    payload TEXT NOT NULL,
                    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                    attempts INTEGER NOT NULL DEFAULT 0,
                    last_error TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_activity_queue_created_at ON activity_queue(created_at);
                """
            )
            conn.commit()

    def enqueue_many(self, events: Iterable[ActivityEvent]) -> int:
        rows = [(event.to_json(),) for event in events]
        rows = list(rows)
        if not rows:
            return 0
        with self._connect() as conn:
            conn.executemany("INSERT INTO activity_queue(payload) VALUES (?)", rows)
            conn.commit()
        return len(rows)

    def dequeue_batch(self, limit: int) -> list[tuple[int, ActivityEvent]]:
        with self._connect() as conn:
            rows = conn.execute(
                "SELECT id, payload FROM activity_queue ORDER BY id ASC LIMIT ?",
                (limit,),
            ).fetchall()
        return [(int(row["id"]), ActivityEvent.from_json(row["payload"])) for row in rows]

    def mark_sent(self, ids: list[int]) -> None:
        if not ids:
            return
        placeholders = ",".join("?" for _ in ids)
        with self._connect() as conn:
            conn.execute(f"DELETE FROM activity_queue WHERE id IN ({placeholders})", ids)
            conn.commit()

    def mark_failed(self, ids: list[int], error: str) -> None:
        if not ids:
            return
        placeholders = ",".join("?" for _ in ids)
        with self._connect() as conn:
            conn.execute(
                f"UPDATE activity_queue SET attempts = attempts + 1, last_error = ? WHERE id IN ({placeholders})",
                [error[:500], *ids],
            )
            conn.commit()

    def size(self) -> int:
        with self._connect() as conn:
            row = conn.execute("SELECT COUNT(*) as c FROM activity_queue").fetchone()
        return int(row["c"] if row else 0)
