from __future__ import annotations

import asyncio
import base64
import hashlib
import hmac
import logging
import json
import struct
from typing import Any

import grpc

from .models import ActivityEvent

logger = logging.getLogger(__name__)
_CONTROL_PLANE_SIGNING_ALG = "hmac-sha256-v1"


class ProtoUnavailableError(RuntimeError):
    pass


def _import_generated():
    try:
        from endpoint_agent.generated import activity_pb2, activity_pb2_grpc, agent_pb2, agent_pb2_grpc  # type: ignore
        return activity_pb2, activity_pb2_grpc, agent_pb2, agent_pb2_grpc
    except Exception as exc:
        raise ProtoUnavailableError(
            "gRPC stubs are not generated. Run LocalEndpointAgent/scripts/generate_protos.sh"
        ) from exc


class ActivityServiceDirectClient:
    def __init__(self, url: str) -> None:
        self.url = url
        self._channel: grpc.aio.Channel | None = None
        self._stub = None
        self._pb = None

    async def connect(self) -> None:
        if self._channel:
            return
        activity_pb2, activity_pb2_grpc, _, _ = _import_generated()
        self._pb = activity_pb2
        self._channel = grpc.aio.insecure_channel(self.url)
        self._stub = activity_pb2_grpc.ActivityGrpcServiceStub(self._channel)
        logger.info("ActivityService gRPC connected: %s", self.url)

    async def close(self) -> None:
        if self._channel:
            await self._channel.close()

    async def send_activity(self, event: ActivityEvent) -> bool:
        await self.connect()
        assert self._pb is not None and self._stub is not None
        payload = event.to_activity_reply_payload()
        activity_msg = self._pb.ActivityReply(
            computer_id=payload["computer_id"],
            timestamp=payload["timestamp"],
            activity_type=payload["activity_type"],
            details=payload["details"],
            duration_ms=(payload["duration_ms"] or 0),
            url=payload["url"],
            process_name=payload["process_name"],
            is_blocked=payload["is_blocked"],
            risk_score=float(payload["risk_score"]),
            Synced=True,
        )
        req = self._pb.CreateActivityRequest(activity=activity_msg)
        try:
            await self._stub.CreateActivity(req, timeout=5)
            return True
        except Exception as exc:
            logger.warning("Failed to send activity (%s): %s", event.activity_type, exc)
            return False


class AgentManagementDirectClient:
    def __init__(
        self,
        url: str,
        computer_id: int,
        version: str,
        *,
        signing_secret: str | None = None,
        signing_key_id: str = "default",
        allow_unsigned: bool = True,
    ) -> None:
        self.url = url
        self.computer_id = computer_id
        self.version = version
        self.agent_id: int | None = None
        self.config_version = "1"
        self._signing_secret = (signing_secret or "").strip()
        self._signing_key_id = (signing_key_id or "").strip()
        self._allow_unsigned = bool(allow_unsigned)
        self._channel: grpc.aio.Channel | None = None
        self._stub = None
        self._pb = None

    async def connect(self) -> None:
        if self._channel:
            return
        _, _, agent_pb2, agent_pb2_grpc = _import_generated()
        self._pb = agent_pb2
        self._channel = grpc.aio.insecure_channel(self.url)
        self._stub = agent_pb2_grpc.AgentManagementServiceStub(self._channel)
        logger.info("AgentManagementService gRPC connected: %s", self.url)
        logger.info(
            "Control-plane signature verification: secret=%s, expected_key_id=%s, allow_unsigned=%s",
            "configured" if self._signing_secret else "not-configured",
            self._signing_key_id or "(any)",
            self._allow_unsigned,
        )

    async def close(self) -> None:
        if self._channel:
            await self._channel.close()

    async def ensure_registered(self) -> int | None:
        await self.connect()
        assert self._pb is not None and self._stub is not None

        if self.agent_id:
            return self.agent_id

        try:
            resp = await self._stub.RegisterAgent(
                self._pb.RegisterAgentRequest(
                    computer_id=self.computer_id,
                    version=self.version,
                    config_version=self.config_version,
                ),
                timeout=5,
            )
            if resp.success and resp.agent and resp.agent.id:
                self.agent_id = int(resp.agent.id)
                return self.agent_id

            # Existing agent for computer; fallback to lookup.
            lookup = await self._stub.GetAgentsByComputer(
                self._pb.GetAgentsByComputerRequest(computer_id=self.computer_id),
                timeout=5,
            )
            if lookup.success and lookup.agents:
                self.agent_id = int(lookup.agents[0].id)
                return self.agent_id
        except Exception as exc:
            logger.warning("Failed to register agent: %s", exc)
        return None

    async def heartbeat(self, status: str = "online") -> bool:
        await self.connect()
        assert self._pb is not None and self._stub is not None
        agent_id = await self.ensure_registered()
        if not agent_id:
            return False
        try:
            resp = await self._stub.UpdateAgentStatus(
                self._pb.UpdateAgentStatusRequest(
                    agent_id=agent_id,
                    status=status,
                    config_version=self.config_version,
                ),
                timeout=5,
            )
            return bool(resp.success)
        except Exception as exc:
            logger.warning("Heartbeat failed: %s", exc)
            return False

    async def fetch_policy(self) -> dict[str, Any] | None:
        await self.connect()
        assert self._pb is not None and self._stub is not None
        agent_id = await self.ensure_registered()
        if not agent_id:
            return None
        try:
            resp = await self._stub.GetAgentPolicy(
                self._pb.GetAgentPolicyRequest(agent_id=agent_id),
                timeout=5,
            )
            if not resp.success or not resp.policy or int(getattr(resp.policy, "agent_id", 0) or 0) <= 0:
                return None

            p = resp.policy
            if not self._verify_policy_signature(p):
                logger.error("Rejected policy from control plane due to invalid signature (agent_id=%s)", agent_id)
                return None
            return {
                "version": p.policy_version or "1",
                "updated_at": p.updated_at or None,
                "collection_interval_sec": int(p.collection_interval_sec or 5),
                "heartbeat_interval_sec": int(p.heartbeat_interval_sec or 15),
                "flush_interval_sec": int(p.flush_interval_sec or 5),
                "enable_process_collection": bool(p.enable_process_collection),
                "enable_browser_collection": bool(p.enable_browser_collection),
                "enable_active_window_collection": bool(p.enable_active_window_collection),
                "enable_idle_collection": bool(p.enable_idle_collection),
                "idle_threshold_sec": int(p.idle_threshold_sec or 120),
                "browser_poll_interval_sec": int(p.browser_poll_interval_sec or 10),
                "process_snapshot_limit": int(p.process_snapshot_limit or 50),
                "high_risk_threshold": float(p.high_risk_threshold or 85.0),
                "auto_lock_enabled": bool(p.auto_lock_enabled),
                "admin_blocked": bool(p.admin_blocked),
                "blocked_reason": (p.blocked_reason or None),
                "browsers": list(p.browsers or []),
                "_signature": str(getattr(p, "signature", "") or ""),
                "_signature_key_id": str(getattr(p, "signature_key_id", "") or ""),
                "_signature_alg": str(getattr(p, "signature_alg", "") or ""),
            }
        except Exception as exc:
            logger.warning("Policy fetch failed: %s", exc)
            return None

    async def fetch_commands(self) -> list[dict[str, Any]]:
        await self.connect()
        assert self._pb is not None and self._stub is not None
        agent_id = await self.ensure_registered()
        if not agent_id:
            return []
        try:
            resp = await self._stub.GetPendingAgentCommands(
                self._pb.GetPendingAgentCommandsRequest(agent_id=agent_id, limit=20),
                timeout=5,
            )
            if not resp.success:
                return []
            commands: list[dict[str, Any]] = []
            for cmd in resp.commands:
                if not self._verify_command_signature(cmd):
                    cmd_id = str(getattr(cmd, "id", "") or "")
                    logger.error("Rejected command from control plane due to invalid signature (id=%s)", cmd_id or "unknown")
                    if cmd_id:
                        await self.ack_command(cmd_id, "failed", "Invalid command signature")
                    continue
                payload_obj: dict[str, Any] = {}
                if getattr(cmd, "payload_json", ""):
                    try:
                        parsed = json.loads(cmd.payload_json)
                        if isinstance(parsed, dict):
                            payload_obj = parsed
                        else:
                            payload_obj = {"value": parsed}
                    except Exception:
                        payload_obj = {"raw": cmd.payload_json}
                commands.append({
                    "id": str(cmd.id),
                    "agent_id": int(cmd.agent_id),
                    "type": str(cmd.type or ""),
                    "payload": payload_obj,
                    "status": str(cmd.status or ""),
                    "requested_by": str(cmd.requested_by or ""),
                    "created_at": str(cmd.created_at or ""),
                    "payload_json": str(getattr(cmd, "payload_json", "") or ""),
                    "_signature": str(getattr(cmd, "signature", "") or ""),
                    "_signature_key_id": str(getattr(cmd, "signature_key_id", "") or ""),
                    "_signature_alg": str(getattr(cmd, "signature_alg", "") or ""),
                })
            return commands
        except Exception as exc:
            logger.warning("Command fetch failed: %s", exc)
            return []

    async def ack_command(self, command_id: str, status: str, message: str = "") -> None:
        await self.connect()
        assert self._pb is not None and self._stub is not None
        try:
            await self._stub.AckAgentCommand(
                self._pb.AckAgentCommandRequest(
                    command_id=int(command_id),
                    status=status,
                    result_message=message or "",
                ),
                timeout=5,
            )
        except Exception as exc:
            logger.warning("Ack command failed (id=%s): %s", command_id, exc)

    def _verify_policy_signature(self, policy_msg: Any) -> bool:
        signature = str(getattr(policy_msg, "signature", "") or "")
        payload = _canonical_policy_payload(policy_msg)
        return self._verify_control_plane_signature(
            kind="policy",
            entity_id=str(getattr(policy_msg, "id", "") or getattr(policy_msg, "agent_id", "") or "unknown"),
            payload=payload,
            signature=signature,
            key_id=str(getattr(policy_msg, "signature_key_id", "") or ""),
            alg=str(getattr(policy_msg, "signature_alg", "") or ""),
        )

    def _verify_command_signature(self, command_msg: Any) -> bool:
        signature = str(getattr(command_msg, "signature", "") or "")
        payload = _canonical_command_payload(command_msg)
        return self._verify_control_plane_signature(
            kind="command",
            entity_id=str(getattr(command_msg, "id", "") or "unknown"),
            payload=payload,
            signature=signature,
            key_id=str(getattr(command_msg, "signature_key_id", "") or ""),
            alg=str(getattr(command_msg, "signature_alg", "") or ""),
        )

    def _verify_control_plane_signature(
        self,
        *,
        kind: str,
        entity_id: str,
        payload: bytes,
        signature: str,
        key_id: str,
        alg: str,
    ) -> bool:
        sig = (signature or "").strip().lower()
        key = (key_id or "").strip()
        alg_norm = (alg or "").strip().lower()

        if not sig:
            if self._allow_unsigned or not self._signing_secret:
                return True
            logger.error("%s %s signature missing and unsigned payloads are not allowed", kind, entity_id)
            return False

        if not self._signing_secret:
            logger.error("%s %s is signed but local signing secret is not configured", kind, entity_id)
            return False

        if alg_norm and alg_norm != _CONTROL_PLANE_SIGNING_ALG:
            logger.error("%s %s uses unsupported signature algorithm: %s", kind, entity_id, alg_norm)
            return False

        if self._signing_key_id and key and key != self._signing_key_id:
            logger.error("%s %s key id mismatch: expected=%s actual=%s", kind, entity_id, self._signing_key_id, key)
            return False

        digest = hmac.new(self._signing_secret.encode("utf-8"), payload, hashlib.sha256).hexdigest()
        if not hmac.compare_digest(sig, digest):
            logger.error("%s %s signature verification failed", kind, entity_id)
            return False

        return True


def _b64_utf8(value: str | None) -> str:
    return base64.b64encode((value or "").encode("utf-8")).decode("ascii")


def _f32_bits(value: Any) -> int:
    f = float(value or 0.0)
    return int(struct.unpack("!I", struct.pack("!f", f))[0])


def _append_kv(parts: list[str], key: str, value: str) -> None:
    parts.append(f"{key}={value}")


def _append_str(parts: list[str], key: str, value: str | None) -> None:
    _append_kv(parts, key, _b64_utf8(value))


def _append_int(parts: list[str], key: str, value: Any) -> None:
    _append_kv(parts, key, str(int(value or 0)))


def _append_bool(parts: list[str], key: str, value: Any) -> None:
    _append_kv(parts, key, "1" if bool(value) else "0")


def _canonical_policy_payload(p: Any) -> bytes:
    parts: list[str] = []
    _append_str(parts, "kind", "policy")
    _append_int(parts, "id", getattr(p, "id", 0))
    _append_int(parts, "agent_id", getattr(p, "agent_id", 0))
    _append_int(parts, "computer_id", getattr(p, "computer_id", 0))
    _append_str(parts, "policy_version", getattr(p, "policy_version", ""))
    _append_int(parts, "collection_interval_sec", getattr(p, "collection_interval_sec", 0))
    _append_int(parts, "heartbeat_interval_sec", getattr(p, "heartbeat_interval_sec", 0))
    _append_int(parts, "flush_interval_sec", getattr(p, "flush_interval_sec", 0))
    _append_bool(parts, "enable_process_collection", getattr(p, "enable_process_collection", False))
    _append_bool(parts, "enable_browser_collection", getattr(p, "enable_browser_collection", False))
    _append_bool(parts, "enable_active_window_collection", getattr(p, "enable_active_window_collection", False))
    _append_bool(parts, "enable_idle_collection", getattr(p, "enable_idle_collection", False))
    _append_int(parts, "idle_threshold_sec", getattr(p, "idle_threshold_sec", 0))
    _append_int(parts, "browser_poll_interval_sec", getattr(p, "browser_poll_interval_sec", 0))
    _append_int(parts, "process_snapshot_limit", getattr(p, "process_snapshot_limit", 0))
    _append_int(parts, "high_risk_threshold_f32bits", _f32_bits(getattr(p, "high_risk_threshold", 0.0)))
    _append_bool(parts, "auto_lock_enabled", getattr(p, "auto_lock_enabled", False))
    _append_bool(parts, "admin_blocked", getattr(p, "admin_blocked", False))
    _append_str(parts, "blocked_reason", getattr(p, "blocked_reason", ""))
    _append_str(parts, "updated_at", getattr(p, "updated_at", ""))
    browsers = list(getattr(p, "browsers", []) or [])
    _append_int(parts, "browsers_count", len(browsers))
    for idx, browser in enumerate(browsers):
        _append_str(parts, f"browsers_{idx}", str(browser or ""))
    return ("\n".join(parts) + "\n").encode("utf-8")


def _canonical_command_payload(c: Any) -> bytes:
    parts: list[str] = []
    _append_str(parts, "kind", "command")
    _append_int(parts, "id", getattr(c, "id", 0))
    _append_int(parts, "agent_id", getattr(c, "agent_id", 0))
    _append_str(parts, "type", getattr(c, "type", ""))
    _append_str(parts, "payload_json", getattr(c, "payload_json", ""))
    _append_str(parts, "status", getattr(c, "status", ""))
    _append_str(parts, "requested_by", getattr(c, "requested_by", ""))
    _append_str(parts, "result_message", getattr(c, "result_message", ""))
    _append_str(parts, "created_at", getattr(c, "created_at", ""))
    _append_str(parts, "acknowledged_at", getattr(c, "acknowledged_at", ""))
    return ("\n".join(parts) + "\n").encode("utf-8")


async def send_batch(client: ActivityServiceDirectClient, events: list[ActivityEvent]) -> tuple[list[ActivityEvent], list[ActivityEvent]]:
    sent: list[ActivityEvent] = []
    failed: list[ActivityEvent] = []
    for event in events:
        ok = await client.send_activity(event)
        (sent if ok else failed).append(event)
        if not ok:
            # Small backoff to avoid hammering service on failure burst
            await asyncio.sleep(0.05)
    return sent, failed
