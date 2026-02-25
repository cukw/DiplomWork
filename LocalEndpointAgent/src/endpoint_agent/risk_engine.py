from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable

from .models import ActivityEvent


@dataclass(slots=True)
class RiskDecision:
    should_block: bool
    reason: str | None = None


class RiskEngine:
    def evaluate(self, events: Iterable[ActivityEvent], policy: dict, default_threshold: float, default_auto_lock: bool) -> RiskDecision:
        threshold = float(policy.get("high_risk_threshold", default_threshold))
        auto_lock = bool(policy.get("auto_lock_enabled", default_auto_lock))
        admin_blocked = bool(policy.get("admin_blocked", False))
        blocked_reason = policy.get("blocked_reason") or "admin block"

        if admin_blocked:
            return RiskDecision(True, str(blocked_reason))

        if not auto_lock:
            return RiskDecision(False)

        for event in events:
            if float(event.risk_score) >= threshold:
                return RiskDecision(True, f"high risk event {event.activity_type} ({event.risk_score} >= {threshold})")

        return RiskDecision(False)
