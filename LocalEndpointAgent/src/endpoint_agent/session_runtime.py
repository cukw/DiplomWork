from __future__ import annotations

from datetime import UTC, datetime

from .config import AgentConfig


def has_active_session(cfg: AgentConfig) -> bool:
    if cfg.agent.computer_id <= 0 or not cfg.agent.session_id:
        return False

    expires_at = parse_session_expires_at(cfg.agent.session_expires_at)
    if expires_at is None:
        return False

    return datetime.now(UTC) < expires_at


def seconds_until_session_expiry(cfg: AgentConfig) -> float:
    expires_at = parse_session_expires_at(cfg.agent.session_expires_at)
    if expires_at is None:
        return 0.0
    return max(0.0, (expires_at - datetime.now(UTC)).total_seconds())


def parse_session_expires_at(value: str | None) -> datetime | None:
    if not value:
        return None
    raw = str(value).strip()
    if not raw:
        return None
    if raw.endswith("Z"):
        raw = f"{raw[:-1]}+00:00"
    try:
        parsed = datetime.fromisoformat(raw)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        return parsed.replace(tzinfo=UTC)
    return parsed.astimezone(UTC)
