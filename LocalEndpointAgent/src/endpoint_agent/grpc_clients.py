from __future__ import annotations

import asyncio
import logging
import json
from typing import Any

import grpc

from .models import ActivityEvent

logger = logging.getLogger(__name__)


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
    def __init__(self, url: str, computer_id: int, version: str) -> None:
        self.url = url
        self.computer_id = computer_id
        self.version = version
        self.agent_id: int | None = None
        self.config_version = "1"
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
