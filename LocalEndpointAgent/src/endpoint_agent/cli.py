from __future__ import annotations

import argparse
import asyncio
import logging
import signal
import sys
from getpass import getpass
from pathlib import Path

from endpoint_agent.autostart import ensure_user_autostart
from endpoint_agent.config import load_config, load_or_create_config
from endpoint_agent.enrollment import (
    clear_local_session,
    end_local_session_if_possible,
    enroll_computer,
    logout_computer_session,
)
from endpoint_agent.launcher import is_elevated, request_elevation, start_background
from endpoint_agent.login_gui import prompt_login_and_enroll
from endpoint_agent.prod_defaults import DEFAULT_AGENT_AUTH_HEADER, DEFAULT_AGENT_AUTH_TOKEN
from endpoint_agent.session_runtime import has_active_session, seconds_until_session_expiry

COMMANDS = {"run", "start", "enroll", "logout", "selfcheck"}


def _configure_logging(level: str) -> None:
    logging.basicConfig(
        level=getattr(logging, level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )


async def _ensure_active_session(config_path: str | None) -> tuple[Path, object]:
    resolved_config_path, cfg = load_or_create_config(config_path)

    if not has_active_session(cfg):
        if cfg.agent.session_id:
            ended = await asyncio.to_thread(
                end_local_session_if_possible,
                gateway_url=cfg.services.gateway_url,
                config_path=resolved_config_path,
                insecure_tls=cfg.services.gateway_tls_insecure,
            )
            if not ended:
                logging.getLogger("endpoint_agent").warning(
                    "Local session was stale; cleared local state without server confirmation"
                )
            cfg = load_config(resolved_config_path)

        if not prompt_login_and_enroll(resolved_config_path, cfg):
            raise SystemExit("Agent login was cancelled")

        cfg = load_config(resolved_config_path)
        if not has_active_session(cfg):
            raise SystemExit("Agent session was not created")

    return resolved_config_path, cfg


def _ensure_configured_autostart(config_path: Path, cfg: object, *, require_admin: bool = False) -> None:
    runtime = getattr(cfg, "runtime", None)
    if not bool(getattr(runtime, "auto_start", True)):
        logging.getLogger("endpoint_agent").info("Agent autostart is disabled in config")
        return

    try:
        ok, message = ensure_user_autostart(
            config_path,
            require_admin=require_admin or bool(getattr(runtime, "require_admin", False)),
        )
        logger = logging.getLogger("endpoint_agent")
        if ok:
            logger.info(message)
        else:
            logger.warning(message)
    except Exception as exc:
        logging.getLogger("endpoint_agent").warning("Failed to configure agent autostart: %s", exc)


async def _run_agent(config_path: str | None, log_level: str, *, require_admin: bool = False) -> None:
    _configure_logging(log_level)
    resolved_config_path, cfg = await _ensure_active_session(config_path)
    _ensure_configured_autostart(resolved_config_path, cfg, require_admin=require_admin)

    from endpoint_agent.runner import EndpointAgentRunner

    runner = EndpointAgentRunner(cfg)
    session_expired = False

    loop = asyncio.get_running_loop()
    stop_called = False

    def _stop() -> None:
        nonlocal stop_called
        if stop_called:
            return
        stop_called = True
        logging.getLogger("endpoint_agent").info("Shutdown signal received")
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
        logging.getLogger("endpoint_agent").info("Daily agent session expired")
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
            logging.getLogger("endpoint_agent").warning("Failed to end expired session cleanly: %s", exc)
            clear_local_session(resolved_config_path)


def run_command(args: argparse.Namespace) -> int:
    if args.background:
        return start_command(args)

    if _should_request_admin(args.config, bool(args.require_admin)):
        _configure_logging(args.log_level)
        resolved_config_path, cfg = asyncio.run(_ensure_active_session(args.config))
        _ensure_configured_autostart(resolved_config_path, cfg, require_admin=True)
        elevated_args = [
            "run",
            "--config",
            str(resolved_config_path),
            "--log-level",
            args.log_level,
            "--require-admin",
        ]
        return _relaunch_with_admin(elevated_args)

    asyncio.run(_run_agent(args.config, args.log_level, require_admin=bool(args.require_admin)))
    return 0


def start_command(args: argparse.Namespace) -> int:
    _configure_logging(args.log_level)
    resolved_config_path, cfg = asyncio.run(_ensure_active_session(args.config))
    require_admin = bool(getattr(args, "require_admin", False)) or cfg.runtime.require_admin
    _ensure_configured_autostart(resolved_config_path, cfg, require_admin=require_admin)
    if require_admin and not is_elevated():
        elevated_args = [
            "run",
            "--config",
            str(resolved_config_path),
            "--log-level",
            args.log_level,
            "--background",
            "--require-admin",
        ]
        return _relaunch_with_admin(elevated_args)

    child_args = [
        "run",
        "--config",
        str(resolved_config_path),
        "--log-level",
        args.log_level,
    ]
    if require_admin:
        child_args.append("--require-admin")

    log_dir = cfg.state_dir_path.parent / "logs"
    pid = start_background(child_args, log_dir=log_dir, cwd=Path.cwd())
    print(f"Agent monitoring started in background (pid={pid}, logs={log_dir})")
    return 0


def enroll_command(args: argparse.Namespace) -> int:
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
    return 0


def logout_command(args: argparse.Namespace) -> int:
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
    return 0


def selfcheck_command(args: argparse.Namespace) -> int:
    import importlib
    import psutil
    import sqlite3
    import ssl

    required_modules = [
        "endpoint_agent.autostart",
        "endpoint_agent.collectors",
        "endpoint_agent.config",
        "endpoint_agent.enrollment",
        "endpoint_agent.grpc_clients",
        "endpoint_agent.launcher",
        "endpoint_agent.login_gui",
        "endpoint_agent.policy_cache",
        "endpoint_agent.queue_store",
        "endpoint_agent.risk_engine",
        "endpoint_agent.runner",
        "endpoint_agent.rust_bridge",
        "endpoint_agent.session_runtime",
        "endpoint_agent.state_store",
        "endpoint_agent.system_control",
        "endpoint_agent.system_inventory",
        "endpoint_agent.generated.agent_pb2",
        "endpoint_agent.generated.agent_pb2_grpc",
        "grpc",
        "grpc.aio",
        "grpc._cython.cygrpc",
        "google.protobuf",
        "google._upb._message",
        "pydantic",
        "pydantic_core._pydantic_core",
        "yaml",
        "_yaml",
        "xml.parsers.expat",
        "pyexpat",
        "sqlite3",
        "_sqlite3",
        "ssl",
        "_ssl",
        "select",
        "selectors",
        "socket",
        "_socket",
    ]

    if sys.platform == "win32":
        required_modules.extend([
            "winreg",
            "_overlapped",
            "_multiprocessing",
            "psutil._psutil_windows",
        ])
    elif sys.platform.startswith("linux"):
        required_modules.extend([
            "psutil._psutil_linux",
            "psutil._psutil_posix",
        ])
    elif sys.platform == "darwin":
        required_modules.append("psutil._psutil_osx")

    failures: list[str] = []
    for module_name in required_modules:
        try:
            importlib.import_module(module_name)
        except Exception as exc:
            failures.append(f"{module_name}: {type(exc).__name__}: {exc}")

    alternatives = [
        ("Activity protobuf stub", [
            "endpoint_agent.generated.activity_pb2",
            "endpoint_agent.generated.Activity_pb2",
        ]),
        ("Activity gRPC stub", [
            "endpoint_agent.generated.activity_pb2_grpc",
            "endpoint_agent.generated.Activity_pb2_grpc",
        ]),
    ]
    for label, module_names in alternatives:
        last_error = ""
        for module_name in module_names:
            try:
                importlib.import_module(module_name)
                break
            except Exception as exc:
                last_error = f"{module_name}: {type(exc).__name__}: {exc}"
        else:
            failures.append(f"{label}: no supported module variant imported ({last_error})")

    try:
        psutil.cpu_times()
        psutil.net_if_addrs()
        ssl.create_default_context()
        with sqlite3.connect(":memory:") as conn:
            conn.execute("select 1").fetchone()
    except Exception as exc:
        failures.append(f"runtime smoke: {type(exc).__name__}: {exc}")

    if failures:
        print("endpoint-agent selfcheck failed")
        for failure in failures:
            print(f"- {failure}")
        return 1

    print("endpoint-agent selfcheck ok")
    return 0


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="endpoint-agent", description="Local endpoint agent")
    sub = parser.add_subparsers(dest="command", required=True)

    run_parser = sub.add_parser("run", help="Run agent")
    run_parser.add_argument("--config", default=None, help="Path to YAML config")
    run_parser.add_argument("--log-level", default="INFO", help="Log level")
    run_parser.add_argument("--background", action="store_true", help="Start monitoring in the background and exit")
    run_parser.add_argument("--require-admin", action="store_true", help="Ask the OS for administrator rights before running")
    run_parser.set_defaults(func=run_command)

    start_parser = sub.add_parser("start", help="Prepare session and start monitoring in background")
    start_parser.add_argument("--config", default=None, help="Path to YAML config")
    start_parser.add_argument("--log-level", default="INFO", help="Log level")
    start_parser.add_argument("--require-admin", action="store_true", help="Ask the OS for administrator rights before starting")
    start_parser.set_defaults(func=start_command)

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
    enroll_parser.set_defaults(func=enroll_command)

    logout_parser = sub.add_parser("logout", help="End the current computer session")
    logout_parser.add_argument("--config", default=None, help="Path to YAML config")
    logout_parser.add_argument("--gateway-url", default=None, help="Gateway URL, for example https://2.26.89.86")
    logout_parser.add_argument("--username", default="", help="Existing application username")
    logout_parser.add_argument("--password", default="", help="Existing application password; prompts when omitted")
    logout_parser.add_argument("--insecure", action="store_true", help="Skip TLS certificate validation")
    logout_parser.set_defaults(func=logout_command)

    selfcheck_parser = sub.add_parser("selfcheck", help="Validate packaged runtime imports")
    selfcheck_parser.set_defaults(func=selfcheck_command)

    # Backward compatibility: allow `endpoint-agent --config ...` without subcommand.
    return parser.parse_args(_normalize_argv(argv))


def _normalize_argv(raw_argv: list[str]) -> list[str]:
    argv = list(raw_argv)
    if len(argv) >= 3 and argv[0] == "--config" and argv[2] in COMMANDS:
        return [argv[2], "--config", argv[1], *argv[3:]]
    if len(argv) >= 2 and argv[0].startswith("--config=") and argv[1] in COMMANDS:
        return [argv[1], argv[0], *argv[2:]]
    if not argv:
        argv.insert(0, "start" if getattr(sys, "frozen", False) else "run")
    elif argv[0].startswith("-") and argv[0] not in {"-h", "--help"}:
        argv.insert(0, "run")
    return argv


def _should_request_admin(config_path: str | None, explicit: bool) -> bool:
    if is_elevated():
        return False
    if explicit:
        return True
    try:
        _, cfg = load_or_create_config(config_path)
        return bool(cfg.runtime.require_admin)
    except Exception:
        return False


def _relaunch_with_admin(args: list[str]) -> int:
    print("Для расширенного сбора информации о компьютере требуется подтверждение прав администратора.")
    if request_elevation(args, cwd=Path.cwd()):
        return 0
    print("Не удалось запросить права администратора. Запустите агент от имени администратора вручную.")
    return 1


def _run_args(args: argparse.Namespace, *, background: bool) -> list[str]:
    result = ["run"]
    if getattr(args, "config", None):
        result.extend(["--config", str(args.config)])
    if getattr(args, "log_level", None):
        result.extend(["--log-level", str(args.log_level)])
    if background:
        result.append("--background")
    if getattr(args, "require_admin", False):
        result.append("--require-admin")
    return result


def main() -> int:
    args = parse_args(sys.argv[1:])
    return int(args.func(args))


if __name__ == "__main__":
    raise SystemExit(main())
