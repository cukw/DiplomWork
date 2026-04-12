from __future__ import annotations

import argparse
import asyncio
import logging
import signal
import sys

from endpoint_agent.config import load_config
from endpoint_agent.runner import EndpointAgentRunner


def _configure_logging(level: str) -> None:
    logging.basicConfig(
        level=getattr(logging, level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )


async def _run_agent(config_path: str, log_level: str) -> None:
    _configure_logging(log_level)
    cfg = load_config(config_path)
    runner = EndpointAgentRunner(cfg)

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

    await runner.run()


def run_command(args: argparse.Namespace) -> int:
    asyncio.run(_run_agent(args.config, args.log_level))
    return 0


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="endpoint-agent", description="Local endpoint agent")
    sub = parser.add_subparsers(dest="command", required=True)

    run_parser = sub.add_parser("run", help="Run agent")
    run_parser.add_argument("--config", default="config/agent.local.yaml", help="Path to YAML config")
    run_parser.add_argument("--log-level", default="INFO", help="Log level")
    run_parser.set_defaults(func=run_command)

    # Backward compatibility: allow `endpoint-agent --config ...` without subcommand.
    normalized = list(argv)
    if not normalized or normalized[0].startswith("-"):
        normalized.insert(0, "run")
    return parser.parse_args(normalized)


def main() -> int:
    args = parse_args(sys.argv[1:])
    return int(args.func(args))


if __name__ == "__main__":
    raise SystemExit(main())
