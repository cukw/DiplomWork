from __future__ import annotations

import base64
import hashlib
import hmac
from typing import Iterable


ALGORITHM = "hmac-sha256-v1"


def _append_str(lines: list[str], key: str, value: str) -> None:
    encoded = base64.b64encode(value.encode("utf-8")).decode("ascii")
    lines.append(f"{key}={encoded}")


def _append_int(lines: list[str], key: str, value: int) -> None:
    lines.append(f"{key}={value}")


def _append_bool(lines: list[str], key: str, value: bool) -> None:
    lines.append(f"{key}={'1' if value else '0'}")


def _append_float32_bits(lines: list[str], key: str, value: float) -> None:
    import struct

    bits = struct.unpack("<i", struct.pack("<f", float(value)))[0]
    lines.append(f"{key}={bits}")


def _append_list(lines: list[str], key: str, values: Iterable[str]) -> None:
    arr = list(values)
    lines.append(f"{key}_count={len(arr)}")
    for idx, item in enumerate(arr):
        _append_str(lines, f"{key}_{idx}", item)


def canonical_policy_payload(policy) -> bytes:
    # Must match AgentManagementService ControlPlaneSigningService.BuildCanonicalPolicy
    lines: list[str] = []
    _append_str(lines, "kind", "policy")
    _append_int(lines, "id", int(policy.id))
    _append_int(lines, "agent_id", int(policy.agent_id))
    _append_int(lines, "computer_id", int(policy.computer_id))
    _append_str(lines, "policy_version", policy.policy_version or "")
    _append_int(lines, "collection_interval_sec", int(policy.collection_interval_sec))
    _append_int(lines, "heartbeat_interval_sec", int(policy.heartbeat_interval_sec))
    _append_int(lines, "flush_interval_sec", int(policy.flush_interval_sec))
    _append_bool(lines, "enable_process_collection", bool(policy.enable_process_collection))
    _append_bool(lines, "enable_browser_collection", bool(policy.enable_browser_collection))
    _append_bool(lines, "enable_active_window_collection", bool(policy.enable_active_window_collection))
    _append_bool(lines, "enable_idle_collection", bool(policy.enable_idle_collection))
    _append_int(lines, "idle_threshold_sec", int(policy.idle_threshold_sec))
    _append_int(lines, "browser_poll_interval_sec", int(policy.browser_poll_interval_sec))
    _append_int(lines, "process_snapshot_limit", int(policy.process_snapshot_limit))
    _append_float32_bits(lines, "high_risk_threshold_f32bits", float(policy.high_risk_threshold))
    _append_bool(lines, "auto_lock_enabled", bool(policy.auto_lock_enabled))
    _append_bool(lines, "admin_blocked", bool(policy.admin_blocked))
    _append_str(lines, "blocked_reason", policy.blocked_reason or "")
    _append_str(lines, "updated_at", getattr(policy, "updated_at", "") or "")
    _append_list(lines, "browsers", list(policy.browsers))
    return ("\n".join(lines) + "\n").encode("utf-8")


def canonical_command_payload(command) -> bytes:
    # Must match AgentManagementService ControlPlaneSigningService.BuildCanonicalCommand
    lines: list[str] = []
    _append_str(lines, "kind", "command")
    _append_int(lines, "id", int(command.id))
    _append_int(lines, "agent_id", int(command.agent_id))
    _append_str(lines, "type", command.type or "")
    _append_str(lines, "payload_json", command.payload_json or "")
    _append_str(lines, "status", command.status or "")
    _append_str(lines, "requested_by", command.requested_by or "")
    _append_str(lines, "result_message", command.result_message or "")
    _append_str(lines, "created_at", command.created_at or "")
    _append_str(lines, "acknowledged_at", command.acknowledged_at or "")
    return ("\n".join(lines) + "\n").encode("utf-8")


def sign_payload(payload: bytes, secret: str) -> str:
    if not secret:
        return ""
    return hmac.new(secret.encode("utf-8"), payload, hashlib.sha256).hexdigest()


def verify_policy_signature(policy, secret: str) -> bool:
    if not secret:
        return True
    if not policy.signature or policy.signature_alg != ALGORITHM:
        return False
    expected = sign_payload(canonical_policy_payload(policy), secret)
    return hmac.compare_digest(expected, policy.signature.lower())


def verify_command_signature(command, secret: str) -> bool:
    if not secret:
        return True
    if not command.signature or command.signature_alg != ALGORITHM:
        return False
    expected = sign_payload(canonical_command_payload(command), secret)
    return hmac.compare_digest(expected, command.signature.lower())

