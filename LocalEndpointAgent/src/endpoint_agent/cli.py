from __future__ import annotations

import argparse
import logging
import sys

from endpoint_agent.config import load_runtime_config
from endpoint_agent.runner import EndpointAgentRunner


def _configure_logging(level: str) -> None:
    logging.basicConfig(
        level=getattr(logging, level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )


def run_command(args: argparse.Namespace) -> int:
    cfg = load_runtime_config(args.config)
    _configure_logging(cfg.log_level)

    runner = EndpointAgentRunner(cfg)
    try:
        runner.run_forever()
    except KeyboardInterrupt:
        logging.getLogger("endpoint_agent").info("Shutting down by keyboard interrupt")
    finally:
        runner.close()

    return 0


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="endpoint-agent", description="Local endpoint agent")
    sub = parser.add_subparsers(dest="command", required=True)

    run_parser = sub.add_parser("run", help="Run agent")
    run_parser.add_argument("--config", default=None, help="Path to YAML config")
    run_parser.set_defaults(func=run_command)

    return parser.parse_args(argv)


def main() -> int:
    args = parse_args(sys.argv[1:])
    return int(args.func(args))


if __name__ == "__main__":
    raise SystemExit(main())

