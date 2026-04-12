from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable

from .models import ActivityEvent


@dataclass(slots=True)
class RiskDecision:
    should_block: bool
    reason: str | None = None


class RiskEngine:
    def apply_policy(self, events: Iterable[ActivityEvent], policy: dict) -> None:
        whitelist = _normalize_app_list(policy.get("whitelist_apps"))
        blacklist = _normalize_app_list(policy.get("blacklist_apps"))
        enable_whitelist = bool(policy.get("enable_whitelist", False))
        enable_blacklist = bool(policy.get("enable_blacklist", False))

        if not enable_whitelist and not enable_blacklist:
            return

        for event in events:
            app_name = _event_app_name(event)
            if not app_name:
                continue

            if enable_blacklist and _matches_app_list(app_name, blacklist):
                event.details["policy_match"] = "blacklist"
                event.details["matched_application"] = app_name
                event.risk_score = max(float(event.risk_score), 95.0)
                event.is_blocked = True
                continue

            if enable_whitelist and whitelist and not _matches_app_list(app_name, whitelist):
                event.details["policy_match"] = "not_whitelisted"
                event.details["matched_application"] = app_name
                event.risk_score = max(float(event.risk_score), 70.0)

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


def _normalize_app_list(value: object) -> list[str]:
    if not value:
        return []
    if isinstance(value, str):
        raw = [value]
    else:
        try:
            raw = list(value)  # type: ignore[arg-type]
        except Exception:
            raw = []
    return [str(item).strip().lower() for item in raw if str(item).strip()]


def _event_app_name(event: ActivityEvent) -> str:
    if event.process_name:
        return event.process_name.strip().lower()
    for key in ("process_name", "processName", "app", "application", "name"):
        value = event.details.get(key)
        if value:
            return str(value).strip().lower()
    return ""


def _matches_app_list(app_name: str, app_list: list[str]) -> bool:
    if not app_name or not app_list:
        return False
    return any(app_name == item or item in app_name for item in app_list)
