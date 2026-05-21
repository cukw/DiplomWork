from __future__ import annotations

import argparse
import asyncio
import logging
import signal
import sys
from getpass import getpass

from .config import load_config, load_or_create_config
from .enrollment import clear_local_session, enroll_computer, logout_computer_session
from .login_gui import prompt_login_and_enroll
from .prod_defaults import DEFAULT_AGENT_AUTH_HEADER, DEFAULT_AGENT_AUTH_TOKEN
from .runner import EndpointAgentRunner
from .session_runtime import has_active_session, seconds_until_session_expiry

COMMANDS = {"run", "enroll", "logout"}


def _setup_logging(level: str) -> None:
    logging.basicConfig(
        level=getattr(logging, level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)s [%(name)s] %(message)s",
    )


async def _run(config_path: str | None, log_level: str) -> None:
    _setup_logging(log_level)
    resolved_config_path, cfg = load_or_create_config(config_path)

    if not has_active_session(cfg):
        if cfg.agent.session_id:
            clear_local_session(resolved_config_path)
            cfg = load_config(resolved_config_path)

        if not prompt_login_and_enroll(resolved_config_path, cfg):
            raise SystemExit("Agent login was cancelled")

        cfg = load_config(resolved_config_path)
        if not has_active_session(cfg):
            raise SystemExit("Agent session was not created")

    runner = EndpointAgentRunner(cfg)
    session_expired = False

    loop = asyncio.get_running_loop()
    stop_called = False

    def _stop() -> None:
        nonlocal stop_called
        if stop_called:
            return
        stop_called = True
        logging.getLogger(__name__).info("Shutdown signal received")
        runner.stop()

    for sig in (signal.SIGINT, signal.SIGTERM):
        try:
            loop.add_signal_handler(sig, _stop)
        except NotImplementedError:
            # Windows compatibility fallback (Ctrl+C still works)
            pass

    async def _stop_at_session_expiry() -> None:
        nonlocal session_expired
        await asyncio.sleep(seconds_until_session_expiry(cfg))
        session_expired = True
        logging.getLogger(__name__).info("Daily agent session expired")
        runner.stop()

    expiry_task = asyncio.create_task(_stop_at_session_expiry(), name="session-expiry")
    try:
        await runner.run()
    finally:
        expiry_task.cancel()
        await asyncio.gather(expiry_task, return_exceptions=True)

    if session_expired:
        try:
            await asyncio.to_thread(
                logout_computer_session,
                gateway_url=cfg.services.gateway_url,
                config_path=resolved_config_path,
                insecure_tls=cfg.services.gateway_tls_insecure,
            )
        except Exception as exc:
            logging.getLogger(__name__).warning("Failed to end expired session cleanly: %s", exc)
            clear_local_session(resolved_config_path)


def main() -> None:
    parser = argparse.ArgumentParser(description="Local Endpoint Activity Agent")
    sub = parser.add_subparsers(dest="command")

    run_parser = sub.add_parser("run", help="Run activity collection agent")
    run_parser.add_argument("--config", default=None, help="Path to YAML config")
    run_parser.add_argument("--log-level", default="INFO", help="Log level")

    enroll_parser = sub.add_parser("enroll", help="Login and start a computer session")
    enroll_parser.add_argument("--config", default=None, help="Path to YAML config")
    enroll_parser.add_argument("--gateway-url", default=None, help="Gateway URL, for example https://2.26.89.86")
    enroll_parser.add_argument("--username", required=True, help="Existing application username")
    enroll_parser.add_argument("--password", default="", help="Existing application password; prompts when omitted")
    enroll_parser.add_argument("--full-name", default="", help="Optional business profile name")
    enroll_parser.add_argument("--department", default="", help="Optional department")
    enroll_parser.add_argument("--activity-service-url", default=None, help="Optional ActivityService gRPC endpoint")
    enroll_parser.add_argument("--agent-management-url", default=None, help="Optional AgentManagementService gRPC endpoint")
    enroll_parser.add_argument("--agent-auth-token", default=None, help="Optional agent gRPC transport token")
    enroll_parser.add_argument("--agent-auth-header", default=None, help="Optional agent gRPC transport header")
    enroll_parser.add_argument("--insecure", action="store_true", help="Skip TLS certificate validation")

    logout_parser = sub.add_parser("logout", help="End the current computer session")
    logout_parser.add_argument("--config", default=None, help="Path to YAML config")
    logout_parser.add_argument("--gateway-url", default=None, help="Gateway URL, for example https://2.26.89.86")
    logout_parser.add_argument("--username", default="", help="Existing application username")
    logout_parser.add_argument("--password", default="", help="Existing application password; prompts when omitted")
    logout_parser.add_argument("--insecure", action="store_true", help="Skip TLS certificate validation")

    argv = _normalize_argv(sys.argv[1:])
    args = parser.parse_args(argv)

    if args.command == "enroll":
        config_path, cfg = load_or_create_config(args.config)
        password = args.password or getpass("Password: ")
        result = enroll_computer(
            gateway_url=args.gateway_url or cfg.services.gateway_url,
            username=args.username,
            password=password,
            config_path=config_path,
            full_name=args.full_name,
            department=args.department,
            insecure_tls=args.insecure or cfg.services.gateway_tls_insecure,
            activity_service_url=args.activity_service_url or cfg.services.activity_service_url,
            agent_management_url=args.agent_management_url or cfg.services.agent_management_url,
            agent_auth_token=args.agent_auth_token if args.agent_auth_token is not None else (cfg.security.agent_transport_auth.token or DEFAULT_AGENT_AUTH_TOKEN),
            agent_auth_header=args.agent_auth_header or cfg.security.agent_transport_auth.header_name or DEFAULT_AGENT_AUTH_HEADER,
        )
        computer = result.get("computer") or {}
        print(f"Enrolled computer_id={computer.get('id')} session_id={result.get('sessionId')}")
        return

    if args.command == "logout":
        config_path, cfg = load_or_create_config(args.config)
        password = args.password or (getpass("Password: ") if args.username else None)
        result = logout_computer_session(
            gateway_url=args.gateway_url or cfg.services.gateway_url,
            username=args.username or None,
            password=password,
            config_path=config_path,
            insecure_tls=args.insecure or cfg.services.gateway_tls_insecure,
        )
        print(result.get("message", "Session ended"))
        return

    asyncio.run(_run(args.config, args.log_level))


def _normalize_argv(raw_argv: list[str]) -> list[str]:
    argv = list(raw_argv)
    if len(argv) >= 3 and argv[0] == "--config" and argv[2] in COMMANDS:
        return [argv[2], "--config", argv[1], *argv[3:]]
    if len(argv) >= 2 and argv[0].startswith("--config=") and argv[1] in COMMANDS:
        return [argv[1], argv[0], *argv[2:]]
    if not argv or (argv[0].startswith("-") and argv[0] not in {"-h", "--help"}):
        argv.insert(0, "run")
    return argv


if __name__ == "__main__":
    main()
