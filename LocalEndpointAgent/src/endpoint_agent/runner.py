from __future__ import annotations

import asyncio
import hashlib
import logging
import time
import urllib.request
import uuid
from datetime import UTC, datetime
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

from .collectors import default_collectors
from .config import AgentConfig, DEFAULT_POLICY
from .grpc_clients import ActivityServiceDirectClient, AgentManagementDirectClient, ProtoUnavailableError
from .models import ActivityEvent, utc_now_iso
from .policy_cache import PolicyCache
from .queue_store import OfflineQueueStore
from .risk_engine import RiskEngine
from .state_store import AgentStateStore
from .system_inventory import collect_system_inventory
from .system_control import SystemController
from . import rust_bridge

logger = logging.getLogger(__name__)


class EndpointAgentRunner:
    def __init__(self, cfg: AgentConfig) -> None:
        self.cfg = cfg
        self.state_dir = cfg.state_dir_path
        self.state_store = AgentStateStore(self.state_dir)
        self.queue = OfflineQueueStore(self.state_dir, max_size=self.cfg.runtime.max_queue_size)
        self.policy_cache = PolicyCache(self.state_dir)
        self.policy: dict[str, Any] = self._bootstrap_policy()
        self.system = SystemController()
        self.risk = RiskEngine()
        self.activity_client = ActivityServiceDirectClient(
            cfg.services.activity_service_url,
            agent_auth_token=cfg.security.agent_transport_auth.token,
            agent_auth_header=cfg.security.agent_transport_auth.header_name,
        )
        self.agent_client = AgentManagementDirectClient(
            cfg.services.agent_management_url,
            computer_id=cfg.agent.computer_id,
            version=cfg.agent.version,
            agent_auth_token=cfg.security.agent_transport_auth.token,
            agent_auth_header=cfg.security.agent_transport_auth.header_name,
            signing_secret=cfg.security.control_plane_signing.secret,
            signing_key_id=cfg.security.control_plane_signing.key_id,
            allow_unsigned=cfg.security.control_plane_signing.allow_unsigned,
        )
        self.collectors = default_collectors(cfg.agent.computer_id, cfg.agent.user_id, self.state_store)
        self._stop = asyncio.Event()
        self._online = False
        self._caps = rust_bridge.capabilities()
        self._collector_statuses: dict[str, dict[str, Any]] = {}
        self._last_collected_at = ""
        self._last_sent_at = ""
        self._last_error = ""
        self._system_inventory: dict[str, Any] = {}
        self._system_inventory_ts = 0.0
        self._refresh_system_inventory(force=True)
        self._log_connectivity_expectations()

    def _bootstrap_policy(self) -> dict[str, Any]:
        policy = self.policy_cache.load()
        policy.setdefault("version", "local-default")
        policy.setdefault("high_risk_threshold", self.cfg.risk.local_high_risk_threshold)
        policy.setdefault("auto_lock_enabled", self.cfg.risk.enable_auto_lock)
        return policy

    async def run(self) -> None:
        logger.info("Endpoint agent starting for computer_id=%s user_id=%s", self.cfg.agent.computer_id, self.cfg.agent.user_id)
        logger.info("Runtime capabilities: %s", self._caps)
        await self._ensure_registered_for_activity("startup")
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
            await self._go_offline()
            await self.activity_client.close()
            await self.agent_client.close()

    def stop(self) -> None:
        self._stop.set()

    async def _emit_boot_presence(self) -> None:
        boot_event = ActivityEvent(
            computer_id=self.cfg.agent.computer_id,
            activity_type="SYSTEM_BOOT",
            timestamp=utc_now_iso(),
            collector="runner",
            details={
                "agent_version": self.cfg.agent.version,
                "device_name": self.cfg.agent.device_name,
                "agent_user_id": self.cfg.agent.user_id,
                "agent_session_id": self.cfg.agent.session_id,
                "username": rust_bridge.current_username(),
                "presence": "active",
                "capabilities": self._caps,
            },
            risk_score=0.0,
        )
        inventory_event = ActivityEvent(
            computer_id=self.cfg.agent.computer_id,
            activity_type="SYSTEM_INVENTORY",
            timestamp=utc_now_iso(),
            collector="system_inventory",
            details={
                "inventory": self._system_inventory,
                "agent_user_id": self.cfg.agent.user_id,
            },
            risk_score=0.0,
        )
        events = [boot_event, inventory_event]
        self._decorate_events(events)
        self.queue.enqueue_many(events)

    def _log_connectivity_expectations(self) -> None:
        token_configured = bool((self.cfg.security.agent_transport_auth.token or "").strip())
        logger.info(
            "Agent transport config: activity=%s, control_plane=%s, auth_header=%s, token_configured=%s",
            self.cfg.services.activity_service_url,
            self.cfg.services.agent_management_url,
            self.cfg.security.agent_transport_auth.header_name,
            token_configured,
        )

        if not token_configured:
            logger.warning(
                "Agent transport token is empty. If backend AgentAuth__Token is configured, "
                "activity/control-plane gRPC calls will be rejected."
            )

        if _is_loopback_endpoint(self.cfg.services.activity_service_url):
            logger.warning(
                "ActivityService endpoint points to loopback (%s). This works only if agent runs on the same host as backend.",
                self.cfg.services.activity_service_url,
            )

        if _is_loopback_endpoint(self.cfg.services.agent_management_url):
            logger.warning(
                "AgentManagement endpoint points to loopback (%s). This works only if agent runs on the same host as backend.",
                self.cfg.services.agent_management_url,
            )

    async def _go_offline(self) -> None:
        try:
            await self.agent_client.heartbeat(status="offline", health=self._health_snapshot())
        except Exception:
            pass

    async def _collection_loop(self) -> None:
        while not self._stop.is_set():
            try:
                events: list[ActivityEvent] = []
                policy_collectors = self._policy_with_runtime_defaults()
                for collector in self.collectors:
                    collector_name = collector.__class__.__name__
                    try:
                        collected = collector.collect(policy_collectors)
                        events.extend(collected)
                        self._collector_statuses[collector_name] = {
                            "status": "ok",
                            "last_success_at": utc_now_iso(),
                            "last_event_count": len(collected),
                            "last_error": "",
                        }
                    except Exception as exc:
                        self._last_error = f"{collector_name}: {exc}"
                        self._collector_statuses[collector_name] = {
                            "status": "error",
                            "last_success_at": self._collector_statuses.get(collector_name, {}).get("last_success_at", ""),
                            "last_event_count": 0,
                            "last_error": str(exc)[:500],
                        }
                        logger.warning("Collector %s failed: %s", collector_name, exc)

                if self.system.lock_active:
                    events.append(ActivityEvent(
                        computer_id=self.cfg.agent.computer_id,
                        activity_type="WORKSTATION_BLOCK_ENFORCED",
                        timestamp=utc_now_iso(),
                        collector="lock_enforcement",
                        details={"reason": self.system.reason, "agent_user_id": self.cfg.agent.user_id},
                        risk_score=0.0,
                        is_blocked=True,
                    ))

                self._last_collected_at = utc_now_iso()
                if events:
                    self._decorate_events(events)
                    self.risk.apply_policy(events, policy_collectors)
                    decision = self.risk.evaluate(events, policy_collectors, self.cfg.risk.local_high_risk_threshold, self.cfg.risk.enable_auto_lock)
                    if decision.should_block:
                        source = "admin_policy" if bool(policy_collectors.get("admin_blocked", False)) else "risk"
                        self.system.apply_block_state(True, decision.reason or "policy", source=source)
                    self.queue.enqueue_many(events)
                    logger.debug("Collected %s events; queue=%s", len(events), self.queue.size())
            except Exception as exc:
                logger.exception("Collection loop error: %s", exc)
            await asyncio.sleep(max(1, int(self._policy_with_runtime_defaults().get("collection_interval_sec", self.cfg.runtime.collection_interval_sec))))

    async def _flush_loop(self) -> None:
        while not self._stop.is_set():
            try:
                sent_count, failed_count = await self._flush_once()
                if sent_count == 0 and failed_count == 0:
                    await asyncio.sleep(max(1, int(self._policy_with_runtime_defaults().get("flush_interval_sec", self.cfg.runtime.flush_interval_sec))))
                    continue
            except ProtoUnavailableError as exc:
                self._last_error = str(exc)
                logger.error(str(exc))
                await asyncio.sleep(10)
            except Exception as exc:
                self._last_error = str(exc)
                logger.exception("Flush loop error: %s", exc)
            await asyncio.sleep(max(1, int(self._policy_with_runtime_defaults().get("flush_interval_sec", self.cfg.runtime.flush_interval_sec))))

    async def _flush_once(self) -> tuple[int, int]:
        if self.queue.size() > 0 and not await self._ensure_registered_for_activity("activity flush"):
            return 0, 0

        batch_size = int(self.cfg.runtime.max_batch_size)
        batch = self.queue.dequeue_batch(batch_size)
        if not batch:
            return 0, 0

        sent_ids: list[int] = []
        failed_ids: list[int] = []
        for row_id, event in batch:
            if self.agent_client.agent_id and not event.agent_id:
                event.agent_id = self.agent_client.agent_id
            ok = await self.activity_client.send_activity(event)
            if ok:
                sent_ids.append(row_id)
                self._online = True
                self._last_sent_at = utc_now_iso()
            else:
                failed_ids.append(row_id)
                self._online = False
                self._last_error = f"grpc send failed for {event.activity_type}"
                break

        if sent_ids:
            self.queue.mark_sent(sent_ids)
            logger.info(
                "Sent %s activity event(s) to backend (agent_id=%s, remaining_queue=%s)",
                len(sent_ids),
                self.agent_client.agent_id,
                self.queue.size(),
            )
        if failed_ids:
            self.queue.mark_failed(failed_ids, self._last_error or "grpc send failed")
        return len(sent_ids), len(failed_ids)

    async def _ensure_registered_for_activity(self, reason: str) -> bool:
        if self.agent_client.agent_id:
            return True
        try:
            agent_id = await self.agent_client.ensure_registered()
        except ProtoUnavailableError as exc:
            self._online = False
            self._last_error = str(exc)
            logger.error(str(exc))
            return False
        except Exception as exc:
            self._online = False
            self._last_error = f"agent registration failed: {exc}"
            logger.warning("Agent registration failed before %s: %s", reason, exc)
            return False

        if agent_id:
            logger.info("Agent registered before %s (agent_id=%s)", reason, agent_id)
            return True

        self._online = False
        self._last_error = f"agent registration returned no agent_id before {reason}"
        logger.warning("Agent registration returned no agent_id before %s; activity flush is deferred", reason)
        return False

    async def _flush_until_empty(self, max_batches: int = 100) -> tuple[int, int]:
        total_sent = 0
        total_failed = 0
        for _ in range(max(1, max_batches)):
            sent_count, failed_count = await self._flush_once()
            total_sent += sent_count
            total_failed += failed_count
            if failed_count > 0 or sent_count == 0:
                break
        return total_sent, total_failed

    async def _heartbeat_loop(self) -> None:
        while not self._stop.is_set():
            try:
                status = "online" if self._online else "degraded"
                ok = await self.agent_client.heartbeat(status=status, health=self._health_snapshot())
                self._online = bool(ok)
            except ProtoUnavailableError as exc:
                self._last_error = str(exc)
                logger.error(str(exc))
            except Exception as exc:
                self._last_error = str(exc)
                logger.warning("Heartbeat loop error: %s", exc)
            await asyncio.sleep(max(5, int(self._policy_with_runtime_defaults().get("heartbeat_interval_sec", self.cfg.runtime.heartbeat_interval_sec))))

    async def _policy_loop(self) -> None:
        while not self._stop.is_set():
            try:
                remote_policy = await self.agent_client.fetch_policy()
                if remote_policy:
                    self._apply_remote_policy(remote_policy)
                    logger.info("Policy updated from control plane (version=%s)", remote_policy.get("version"))
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

        if command_type == "PING":
            await self.agent_client.ack_command(command_id, "success", "pong")
        elif command_type == "BLOCK_WORKSTATION":
            reason = str(payload.get("reason") or "admin command")
            self.policy["admin_blocked"] = True
            self.policy["blocked_reason"] = reason
            self.policy_cache.save(self.policy)
            self.system.apply_block_state(True, reason, source="admin_command")
            await self.agent_client.ack_command(command_id, "success", "Workstation blocked")
        elif command_type == "UNBLOCK_WORKSTATION":
            self.policy["admin_blocked"] = False
            self.policy["blocked_reason"] = None
            self.policy_cache.save(self.policy)
            self.system.apply_block_state(False)
            await self.agent_client.ack_command(command_id, "success", "Workstation unblocked")
        elif command_type == "FORCE_SYNC":
            sent_count, failed_count = await self._flush_until_empty()
            status = "success" if failed_count == 0 else "failed"
            await self.agent_client.ack_command(
                command_id,
                status,
                f"Force sync sent={sent_count}, failed={failed_count}, remaining={self.queue.size()}",
            )
        elif command_type in {"UPDATE_POLICY", "REFRESH_POLICY"}:
            refreshed = await self._refresh_policy_once()
            status = "success" if refreshed else "failed"
            message = "Policy refreshed" if refreshed else "Policy refresh returned no usable policy"
            await self.agent_client.ack_command(command_id, status, message)
        elif command_type == "SET_COLLECTION_STATE":
            status, message = self._handle_set_collection_state(payload)
            await self.agent_client.ack_command(command_id, status, message)
        elif command_type == "SET_LOG_LEVEL":
            status, message = self._handle_set_log_level(payload)
            await self.agent_client.ack_command(command_id, status, message)
        elif command_type == "RESTART_AGENT":
            await self.agent_client.ack_command(command_id, "success", "Agent restart requested")
            self.stop()
        elif command_type == "SELF_UPDATE":
            status, message = self._handle_self_update(payload)
            await self.agent_client.ack_command(command_id, status, message)
        else:
            await self.agent_client.ack_command(command_id, "ignored", f"Unsupported command: {command_type}")

    async def _refresh_policy_once(self) -> bool:
        remote_policy = await self.agent_client.fetch_policy()
        if remote_policy:
            self._apply_remote_policy(remote_policy)
            return True
        return False

    async def _lock_enforcement_loop(self) -> None:
        while not self._stop.is_set():
            try:
                self._apply_admin_policy_lock_state()
            except Exception as exc:
                logger.warning("Lock enforcement error: %s", exc)
            await asyncio.sleep(2)

    def _apply_remote_policy(self, remote_policy: dict[str, Any]) -> None:
        self.policy = remote_policy
        self.policy_cache.save(remote_policy)
        self._apply_admin_policy_lock_state()

    def _apply_admin_policy_lock_state(self) -> None:
        runtime_policy = self._policy_with_runtime_defaults()
        admin_blocked = bool(runtime_policy.get("admin_blocked", False))
        if admin_blocked:
            self.system.apply_block_state(
                True,
                str(runtime_policy.get("blocked_reason") or "admin block"),
                source="admin_policy",
            )
        elif self.system.lock_active and self.system.source in {"admin_policy", "admin_command"}:
            self.system.apply_block_state(False)

    def _policy_with_runtime_defaults(self) -> dict[str, Any]:
        merged = dict(DEFAULT_POLICY)
        merged.update({
            "collection_interval_sec": self.cfg.runtime.collection_interval_sec,
            "heartbeat_interval_sec": self.cfg.runtime.heartbeat_interval_sec,
            "flush_interval_sec": self.cfg.runtime.flush_interval_sec,
            "enable_process_collection": self.cfg.collectors.processes.enabled,
            "process_snapshot_limit": self.cfg.collectors.processes.snapshot_limit,
            "enable_browser_collection": self.cfg.collectors.browser_history.enabled,
            "browser_poll_interval_sec": self.cfg.collectors.browser_history.poll_interval_sec,
            "browsers": self.cfg.collectors.browser_history.browsers,
            "enable_active_window_collection": self.cfg.collectors.active_window.enabled,
            "enable_idle_collection": self.cfg.collectors.idle_time.enabled,
            "idle_threshold_sec": self.cfg.collectors.idle_time.idle_threshold_sec,
            "enable_network_collection": self.cfg.collectors.network.enabled,
            "network_snapshot_limit": self.cfg.collectors.network.snapshot_limit,
            "enable_file_collection": self.cfg.collectors.file_activity.enabled,
            "file_watch_paths": self.cfg.collectors.file_activity.paths,
            "file_watch_max_files": self.cfg.collectors.file_activity.max_files_per_scan,
            "enable_usb_collection": self.cfg.collectors.usb_devices.enabled,
            "usb_poll_interval_sec": self.cfg.collectors.usb_devices.poll_interval_sec,
            "enable_inventory_collection": self.cfg.collectors.inventory.enabled,
            "inventory_interval_sec": self.cfg.collectors.inventory.interval_sec,
            "inventory_max_apps": self.cfg.collectors.inventory.max_apps,
            "inventory_max_processes": self.cfg.collectors.inventory.max_processes,
            "enable_session_collection": self.cfg.collectors.session.enabled,
        })
        merged.update(self.policy)
        return merged

    def _handle_set_collection_state(self, payload: dict[str, Any]) -> tuple[str, str]:
        enabled = _payload_bool(payload.get("enabled"))
        if enabled is None:
            return "failed", "SET_COLLECTION_STATE requires boolean payload.enabled"

        collector = str(payload.get("collector") or payload.get("name") or payload.get("target") or "all").strip().lower()
        collector = collector.replace("-", "_").replace(" ", "_")
        mapping = {
            "process": ["enable_process_collection"],
            "processes": ["enable_process_collection"],
            "browser": ["enable_browser_collection"],
            "browser_history": ["enable_browser_collection"],
            "active_window": ["enable_active_window_collection"],
            "window": ["enable_active_window_collection"],
            "idle": ["enable_idle_collection"],
            "idle_time": ["enable_idle_collection"],
            "network": ["enable_network_collection"],
            "file": ["enable_file_collection"],
            "file_activity": ["enable_file_collection"],
            "usb": ["enable_usb_collection"],
            "usb_devices": ["enable_usb_collection"],
            "inventory": ["enable_inventory_collection"],
            "session": ["enable_session_collection"],
            "all": [
                "enable_process_collection",
                "enable_browser_collection",
                "enable_active_window_collection",
                "enable_idle_collection",
                "enable_network_collection",
                "enable_file_collection",
                "enable_usb_collection",
                "enable_inventory_collection",
                "enable_session_collection",
            ],
        }

        fields = mapping.get(collector)
        if not fields:
            return "failed", f"Unknown collector target: {collector}"

        for field in fields:
            self.policy[field] = enabled
        self.policy["version"] = f"local-command-{int(time.time() * 1000)}"
        self.policy_cache.save(self.policy)
        return "success", f"Collection state updated: {collector} enabled={enabled}"

    def _handle_set_log_level(self, payload: dict[str, Any]) -> tuple[str, str]:
        level = str(payload.get("level") or payload.get("logLevel") or "").strip().upper()
        allowed = {"DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"}
        if level not in allowed:
            return "failed", "SET_LOG_LEVEL requires one of DEBUG, INFO, WARNING, ERROR, CRITICAL"

        logging.getLogger().setLevel(getattr(logging, level))
        logger.setLevel(getattr(logging, level))
        return "success", f"Log level set to {level}"

    def _decorate_events(self, events: list[ActivityEvent]) -> None:
        batch_id = f"batch-{datetime.now(UTC).strftime('%Y%m%d%H%M%S')}-{uuid.uuid4().hex[:8]}"
        for event in events:
            event.user_id = event.user_id if event.user_id is not None else self.cfg.agent.user_id
            event.agent_id = event.agent_id if event.agent_id is not None else self.agent_client.agent_id
            event.agent_version = event.agent_version or self.cfg.agent.version
            event.device_name = event.device_name or self.cfg.agent.device_name
            event.details.setdefault("agent_session_id", self.cfg.agent.session_id)
            event.collector = event.collector or "unknown"
            event.event_id = event.event_id or str(uuid.uuid4())
            event.sequence = event.sequence or self.state_store.next_sequence()
            event.batch_id = event.batch_id or batch_id
            event.source_platform = event.source_platform or str(self._caps.get("platform") or "")

    def _health_snapshot(self) -> dict[str, Any]:
        self._refresh_system_inventory()
        return {
            "queue_size": self.queue.size(),
            "last_collected_at": self._last_collected_at,
            "last_sent_at": self._last_sent_at,
            "last_error": self._last_error,
            "policy_version": str(self.policy.get("version") or ""),
            "capabilities": self._caps,
            "collector_statuses": self._collector_statuses,
            "source_platform": str(self._caps.get("platform") or ""),
            "agent_version": self.cfg.agent.version,
            "device_name": self.cfg.agent.device_name,
            "system_inventory": self._system_inventory,
        }

    def _refresh_system_inventory(self, *, force: bool = False) -> None:
        now = time.monotonic()
        if not force and self._system_inventory and now - self._system_inventory_ts < 3600:
            return
        try:
            self._system_inventory = collect_system_inventory(self._caps)
            self._system_inventory_ts = now
        except Exception as exc:
            self._last_error = f"system_inventory: {exc}"
            logger.debug("System inventory refresh failed: %s", exc)

    def _handle_self_update(self, payload: dict[str, Any]) -> tuple[str, str]:
        target_version = str(payload.get("targetVersion") or payload.get("target_version") or "").strip()
        download_url = str(payload.get("downloadUrl") or payload.get("download_url") or "").strip()
        expected_sha256 = str(payload.get("sha256") or "").strip().lower()

        if target_version and target_version == self.cfg.agent.version:
            return "success", f"Already running version {target_version}"
        if not download_url:
            return "failed", "SELF_UPDATE requires payload.download_url/downloadUrl"

        try:
            updates_dir = self.state_dir / "updates"
            updates_dir.mkdir(parents=True, exist_ok=True)
            filename = Path(download_url.split("?", 1)[0]).name or f"endpoint-agent-{target_version or 'update'}"
            target = updates_dir / filename
            with urllib.request.urlopen(download_url, timeout=30) as response:
                data = response.read()
            if expected_sha256:
                actual = hashlib.sha256(data).hexdigest()
                if actual.lower() != expected_sha256:
                    return "failed", f"SELF_UPDATE checksum mismatch: expected={expected_sha256} actual={actual}"
            target.write_bytes(data)
            try:
                target.chmod(0o755)
            except Exception:
                pass
            if bool(payload.get("restart", False)):
                self.stop()
            return "success", f"Downloaded update to {target}"
        except Exception as exc:
            return "failed", f"SELF_UPDATE failed: {exc}"


def _is_loopback_endpoint(raw_url: str) -> bool:
    value = (raw_url or "").strip()
    if not value:
        return False

    parsed = urlparse(value if "://" in value else f"grpc://{value}")
    host = (parsed.hostname or "").strip().lower()
    if not host and parsed.path:
        host = parsed.path.split(":", 1)[0].strip().lower()

    return host in {"localhost", "127.0.0.1", "::1"}


def _payload_bool(value: Any) -> bool | None:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)) and value in (0, 1):
        return bool(value)
    if isinstance(value, str):
        normalized = value.strip().lower()
        if normalized in {"true", "1", "yes", "on", "enabled"}:
            return True
        if normalized in {"false", "0", "no", "off", "disabled"}:
            return False
    return None
