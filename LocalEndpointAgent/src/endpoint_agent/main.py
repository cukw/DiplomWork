from __future__ import annotations

import argparse
import asyncio
import logging
import signal

from .config import load_config
from .runner import EndpointAgentRunner


def _setup_logging(level: str) -> None:
    logging.basicConfig(
        level=getattr(logging, level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)s [%(name)s] %(message)s",
    )


async def _run(config_path: str, log_level: str) -> None:
    _setup_logging(log_level)
    cfg = load_config(config_path)
    runner = EndpointAgentRunner(cfg)

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

    await runner.run()


def main() -> None:
    parser = argparse.ArgumentParser(description="Local Endpoint Activity Agent")
    parser.add_argument("--config", default="config/agent.local.yaml", help="Path to YAML config")
    parser.add_argument("--log-level", default="INFO", help="Log level")
    args = parser.parse_args()
    asyncio.run(_run(args.config, args.log_level))


if __name__ == "__main__":
    main()
