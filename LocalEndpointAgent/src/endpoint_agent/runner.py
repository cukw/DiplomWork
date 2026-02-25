from __future__ import annotations

import asyncio
import logging
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from .collectors import default_collectors
from .config import AgentConfig, DEFAULT_POLICY
from .grpc_clients import ActivityServiceDirectClient, AgentManagementDirectClient, ProtoUnavailableError
from .models import ActivityEvent, utc_now_iso
from .policy_cache import PolicyCache
from .queue_store import OfflineQueueStore
from .risk_engine import RiskEngine
from .system_control import SystemController
from . import rust_bridge

logger = logging.getLogger(__name__)


class EndpointAgentRunner:
    def __init__(self, cfg: AgentConfig) -> None:
        self.cfg = cfg
        self.state_dir = cfg.state_dir_path
        self.queue = OfflineQueueStore(self.state_dir)
        self.policy_cache = PolicyCache(self.state_dir)
        self.policy: dict[str, Any] = self._bootstrap_policy()
        self.system = SystemController()
        self.risk = RiskEngine()
        self.activity_client = ActivityServiceDirectClient(cfg.services.activity_service_url)
        self.agent_client = AgentManagementDirectClient(
            cfg.services.agent_management_url,
            computer_id=cfg.agent.computer_id,
            version=cfg.agent.version,
            signing_secret=cfg.security.control_plane_signing.secret,
            signing_key_id=cfg.security.control_plane_signing.key_id,
            allow_unsigned=cfg.security.control_plane_signing.allow_unsigned,
        )
        self.collectors = default_collectors(cfg.agent.computer_id, cfg.agent.user_id)
        self._stop = asyncio.Event()
        self._online = False
        self._caps = rust_bridge.capabilities()

    def _bootstrap_policy(self) -> dict[str, Any]:
        policy = self.policy_cache.load()
        policy.setdefault("high_risk_threshold", self.cfg.risk.local_high_risk_threshold)
        policy.setdefault("auto_lock_enabled", self.cfg.risk.enable_auto_lock)
        return policy

    async def run(self) -> None:
        logger.info("Endpoint agent starting for computer_id=%s user_id=%s", self.cfg.agent.computer_id, self.cfg.agent.user_id)
        logger.info("Runtime capabilities: %s", self._caps)
        await self._emit_boot_presence()
        tasks = [
            asyncio.create_task(self._collection_loop(), name="collection"),
            asyncio.create_task(self._flush_loop(), name="flush"),
            asyncio.create_task(self._heartbeat_loop(), name="heartbeat"),
            asyncio.create_task(self._policy_loop(), name="policy"),
            asyncio.create_task(self._command_loop(), name="commands"),
            asyncio.create_task(self._lock_enforcement_loop(), name="lock-enforce"),
        ]
        try:
            await self._stop.wait()
        except asyncio.CancelledError:
            pass
        finally:
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
            await self.activity_client.close()
            await self.agent_client.close()
            await self._go_offline()

    def stop(self) -> None:
        self._stop.set()

    async def _emit_boot_presence(self) -> None:
        event = ActivityEvent(
            computer_id=self.cfg.agent.computer_id,
            activity_type="SYSTEM_BOOT",
            timestamp=utc_now_iso(),
            details={
                "agent_version": self.cfg.agent.version,
                "device_name": self.cfg.agent.device_name,
                "agent_user_id": self.cfg.agent.user_id,
                "username": rust_bridge.current_username(),
                "presence": "active",
                "capabilities": self._caps,
            },
            risk_score=0.0,
        )
        self.queue.enqueue_many([event])

    async def _go_offline(self) -> None:
        try:
            await self.agent_client.heartbeat(status="offline")
        except Exception:
            pass

    async def _collection_loop(self) -> None:
        while not self._stop.is_set():
            try:
                events: list[ActivityEvent] = []
                policy_collectors = self._policy_with_runtime_defaults()
                for collector in self.collectors:
                    events.extend(collector.collect(policy_collectors))

                if self.system.lock_active:
                    events.append(ActivityEvent(
                        computer_id=self.cfg.agent.computer_id,
                        activity_type="WORKSTATION_BLOCK_ENFORCED",
                        timestamp=utc_now_iso(),
                        details={"reason": self.system.reason, "agent_user_id": self.cfg.agent.user_id},
                        risk_score=0.0,
                        is_blocked=True,
                    ))

                if events:
                    decision = self.risk.evaluate(events, self.policy, self.cfg.risk.local_high_risk_threshold, self.cfg.risk.enable_auto_lock)
                    if decision.should_block:
                        self.system.apply_block_state(True, decision.reason or "policy")
                    self.queue.enqueue_many(events)
                    logger.debug("Collected %s events; queue=%s", len(events), self.queue.size())
            except Exception as exc:
                logger.exception("Collection loop error: %s", exc)
            await asyncio.sleep(max(1, int(self.policy.get("collection_interval_sec", self.cfg.runtime.collection_interval_sec))))

    async def _flush_loop(self) -> None:
        while not self._stop.is_set():
            try:
                batch_size = int(self.cfg.runtime.max_batch_size)
                batch = self.queue.dequeue_batch(batch_size)
                if not batch:
                    await asyncio.sleep(max(1, int(self.policy.get("flush_interval_sec", self.cfg.runtime.flush_interval_sec))))
                    continue

                sent_ids: list[int] = []
                failed_ids: list[int] = []
                for row_id, event in batch:
                    ok = await self.activity_client.send_activity(event)
                    if ok:
                        sent_ids.append(row_id)
                        self._online = True
                    else:
                        failed_ids.append(row_id)
                        self._online = False
                        break

                if sent_ids:
                    self.queue.mark_sent(sent_ids)
                if failed_ids:
                    self.queue.mark_failed(failed_ids, "grpc send failed")
            except ProtoUnavailableError as exc:
                logger.error(str(exc))
                await asyncio.sleep(10)
            except Exception as exc:
                logger.exception("Flush loop error: %s", exc)
            await asyncio.sleep(max(1, int(self.policy.get("flush_interval_sec", self.cfg.runtime.flush_interval_sec))))

    async def _heartbeat_loop(self) -> None:
        while not self._stop.is_set():
            try:
                status = "online" if self._online else "degraded"
                ok = await self.agent_client.heartbeat(status=status)
                self._online = bool(ok)
            except ProtoUnavailableError as exc:
                logger.error(str(exc))
            except Exception as exc:
                logger.warning("Heartbeat loop error: %s", exc)
            await asyncio.sleep(max(5, int(self.policy.get("heartbeat_interval_sec", self.cfg.runtime.heartbeat_interval_sec))))

    async def _policy_loop(self) -> None:
        while not self._stop.is_set():
            try:
                remote_policy = await self.agent_client.fetch_policy()
                if remote_policy:
                    merged = self._policy_with_runtime_defaults()
                    merged.update(remote_policy)
                    self.policy = merged
                    self.policy_cache.save(merged)
                    logger.info("Policy updated from control plane (version=%s)", merged.get("version"))
            except ProtoUnavailableError as exc:
                logger.error(str(exc))
            except Exception as exc:
                logger.warning("Policy refresh failed, using cached policy: %s", exc)
            await asyncio.sleep(max(5, int(self.cfg.runtime.policy_refresh_interval_sec)))

    async def _command_loop(self) -> None:
        while not self._stop.is_set():
            try:
                commands = await self.agent_client.fetch_commands()
                for cmd in commands:
                    await self._handle_command(cmd)
            except ProtoUnavailableError as exc:
                logger.error(str(exc))
            except Exception as exc:
                logger.warning("Command polling error: %s", exc)
            await asyncio.sleep(5)

    async def _handle_command(self, cmd: dict[str, Any]) -> None:
        command_id = str(cmd.get("id") or "")
        command_type = str(cmd.get("type") or "").upper()
        payload = cmd.get("payload") or {}

        if command_type == "BLOCK_WORKSTATION":
            reason = str(payload.get("reason") or "admin command")
            self.policy["admin_blocked"] = True
            self.policy["blocked_reason"] = reason
            self.policy_cache.save(self.policy)
            self.system.apply_block_state(True, reason)
            await self.agent_client.ack_command(command_id, "success", "Workstation blocked")
        elif command_type == "UNBLOCK_WORKSTATION":
            self.policy["admin_blocked"] = False
            self.policy["blocked_reason"] = None
            self.policy_cache.save(self.policy)
            self.system.apply_block_state(False)
            await self.agent_client.ack_command(command_id, "success", "Workstation unblocked")
        else:
            await self.agent_client.ack_command(command_id, "ignored", f"Unsupported command: {command_type}")

    async def _lock_enforcement_loop(self) -> None:
        while not self._stop.is_set():
            try:
                admin_blocked = bool(self.policy.get("admin_blocked", False))
                if admin_blocked:
                    self.system.apply_block_state(True, str(self.policy.get("blocked_reason") or "admin block"))
            except Exception as exc:
                logger.warning("Lock enforcement error: %s", exc)
            await asyncio.sleep(2)

    def _policy_with_runtime_defaults(self) -> dict[str, Any]:
        merged = dict(DEFAULT_POLICY)
        merged.update(self.policy)
        merged.setdefault("collection_interval_sec", self.cfg.runtime.collection_interval_sec)
        merged.setdefault("heartbeat_interval_sec", self.cfg.runtime.heartbeat_interval_sec)
        merged.setdefault("flush_interval_sec", self.cfg.runtime.flush_interval_sec)
        merged.setdefault("browsers", self.cfg.collectors.browser_history.browsers)
        return merged
