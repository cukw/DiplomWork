from __future__ import annotations

import logging
import time

from . import rust_bridge

logger = logging.getLogger(__name__)


class SystemController:
    def __init__(self) -> None:
        self._lock_active = False
        self._last_lock_attempt_at = 0.0
        self._reason = ""
        self._caps = rust_bridge.capabilities()
        self._warned_unsupported_lock = False

    @property
    def lock_active(self) -> bool:
        return self._lock_active

    @property
    def reason(self) -> str:
        return self._reason

    def apply_block_state(self, should_block: bool, reason: str = "") -> None:
        if not should_block:
            if self._lock_active:
                logger.info("Block state cleared by policy/command")
            self._lock_active = False
            self._reason = ""
            return

        self._lock_active = True
        self._reason = reason or "policy block"

        if not bool(self._caps.get("lock_workstation", False)):
            if not self._warned_unsupported_lock:
                self._warned_unsupported_lock = True
                logger.warning(
                    "Lock requested but lock_workstation is not supported on this environment (platform=%s). Keeping virtual block state only.",
                    self._caps.get("platform", "unknown"),
                )
            return

        now = time.monotonic()
        if now - self._last_lock_attempt_at < 3:
            return
        self._last_lock_attempt_at = now
        ok = rust_bridge.lock_workstation()
        logger.warning("Lock workstation requested (ok=%s, reason=%s)", ok, self._reason)
