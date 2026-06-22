from __future__ import annotations

import json
import platform
import re
import socket
import ssl
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

import psutil
import yaml


class HttpJsonError(RuntimeError):
    def __init__(self, status_code: int, url: str, body: str) -> None:
        self.status_code = status_code
        self.url = url
        self.body = body
        self.json_body = _json_dict_or_none(body)
        self.response_message = _response_message(self.json_body, body)
        detail = self.response_message or body or "empty response body"
        super().__init__(f"HTTP {status_code} from {url}: {detail}")


def collect_device_identity() -> dict[str, str]:
    hostname = socket.gethostname().strip() or platform.node().strip() or "unknown-host"
    return {
        "hostname": hostname,
        "osVersion": platform.platform(),
        "ipAddress": _primary_ip_address(),
        "macAddress": _primary_mac_address(),
    }


def enroll_computer(
    *,
    gateway_url: str,
    username: str,
    password: str,
    config_path: str | Path,
    full_name: str = "",
    department: str = "",
    insecure_tls: bool = False,
    activity_service_url: str | None = None,
    agent_management_url: str | None = None,
    agent_auth_token: str | None = None,
    agent_auth_header: str | None = None,
) -> dict[str, Any]:
    base = _normalize_base_url(gateway_url)
    context = _ssl_context(insecure_tls)
    login = _post_json(
        f"{base}/api/auth/login",
        {"username": username, "password": password},
        context=context,
    )
    token = str(login.get("token") or "")
    if not token:
        raise RuntimeError("Login succeeded but token is empty")

    payload = collect_device_identity()
    if full_name:
        payload["fullName"] = full_name
    if department:
        payload["department"] = department

    enrollment = _enroll_with_session_conflict_recovery(
        base=base,
        payload=payload,
        token=token,
        context=context,
        config_path=config_path,
    )
    _update_agent_config(
        config_path,
        login,
        enrollment,
        payload,
        gateway_url=base,
        gateway_tls_insecure=insecure_tls,
        activity_service_url=activity_service_url,
        agent_management_url=agent_management_url,
        agent_auth_token=agent_auth_token,
        agent_auth_header=agent_auth_header,
    )
    return enrollment


def logout_computer_session(
    *,
    gateway_url: str,
    username: str | None = None,
    password: str | None = None,
    config_path: str | Path,
    insecure_tls: bool = False,
) -> dict[str, Any]:
    base = _normalize_base_url(gateway_url)
    context = _ssl_context(insecure_tls)
    raw = _read_yaml(config_path)
    agent = raw.get("agent") or {}
    token = _token_for_session_end(
        base=base,
        context=context,
        agent=agent,
        username=username,
        password=password,
    )
    session_id = _safe_int(agent.get("session_id"))
    computer_id = _safe_int(agent.get("computer_id"))
    if session_id <= 0 and computer_id <= 0:
        raise RuntimeError("Cannot end computer session: no local session identifiers")

    payload = {
        "sessionId": session_id,
        "computerId": computer_id,
    }
    result = _end_computer_session(base=base, payload=payload, token=token, context=context)
    clear_local_session(config_path)
    return result


def end_local_session_if_possible(
    *,
    gateway_url: str,
    config_path: str | Path,
    insecure_tls: bool = False,
) -> bool:
    try:
        logout_computer_session(
            gateway_url=gateway_url,
            config_path=config_path,
            insecure_tls=insecure_tls,
        )
        return True
    except Exception:
        clear_local_session(config_path)
        return False


def clear_local_session(config_path: str | Path) -> None:
    raw = _read_yaml(config_path)
    agent = raw.get("agent") or {}
    agent["user_id"] = None
    agent["session_id"] = None
    agent["session_expires_at"] = None
    agent["auth_refresh_token"] = None
    raw["agent"] = agent
    _write_yaml(config_path, raw)


def _primary_ip_address() -> str:
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.connect(("8.8.8.8", 80))
            return str(sock.getsockname()[0])
    except OSError:
        try:
            return socket.gethostbyname(socket.gethostname())
        except OSError:
            return ""


def _primary_mac_address() -> str:
    for _, addrs in psutil.net_if_addrs().items():
        for addr in addrs:
            family = str(getattr(addr.family, "name", addr.family)).upper()
            if family not in {"AF_LINK", "AF_PACKET", "-1"}:
                continue
            mac = (addr.address or "").strip().replace("-", ":").upper()
            if _looks_like_mac(mac):
                return mac
    return ""


def _looks_like_mac(value: str) -> bool:
    if not value or value == "00:00:00:00:00:00":
        return False
    parts = value.split(":")
    return len(parts) == 6 and all(len(part) == 2 for part in parts)


def _normalize_base_url(value: str) -> str:
    base = (value or "").strip().rstrip("/")
    if not base:
        raise ValueError("gateway_url is required")
    if "://" not in base:
        base = f"https://{base}"
    return base


def _ssl_context(insecure_tls: bool) -> ssl.SSLContext | None:
    return ssl._create_unverified_context() if insecure_tls else None


def _post_json(
    url: str,
    payload: dict[str, Any],
    *,
    token: str | None = None,
    context: ssl.SSLContext | None = None,
) -> dict[str, Any]:
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = json.dumps(payload).encode("utf-8")
    request = Request(url, data=data, headers=headers, method="POST")
    try:
        with urlopen(request, timeout=20, context=context) as response:
            body = response.read().decode("utf-8")
            return json.loads(body) if body else {}
    except HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        raise HttpJsonError(exc.code, url, body) from exc
    except URLError as exc:
        raise RuntimeError(f"Cannot connect to {url}: {exc.reason}") from exc


def _enroll_with_session_conflict_recovery(
    *,
    base: str,
    payload: dict[str, Any],
    token: str,
    context: ssl.SSLContext | None,
    config_path: str | Path,
) -> dict[str, Any]:
    url = f"{base}/api/user/computers/enroll"
    try:
        return _post_json(url, payload, token=token, context=context)
    except HttpJsonError as exc:
        if exc.status_code != 409:
            raise

        local_agent = _read_local_agent(config_path)
        end_payload = _session_end_payload_for_conflict(exc.response_message, local_agent)
        if end_payload is not None:
            try:
                _end_computer_session(base=base, payload=end_payload, token=token, context=context)
                return _post_json(url, payload, token=token, context=context)
            except HttpJsonError:
                raise _friendly_enrollment_conflict(exc) from exc
            except RuntimeError:
                raise _friendly_enrollment_conflict(exc) from exc

        raise _friendly_enrollment_conflict(exc) from exc


def _end_computer_session(
    *,
    base: str,
    payload: dict[str, Any],
    token: str,
    context: ssl.SSLContext | None,
) -> dict[str, Any]:
    return _post_json(
        f"{base}/api/user/computers/session/end",
        payload,
        token=token,
        context=context,
    )


def _session_end_payload_for_conflict(message: str, local_agent: dict[str, Any]) -> dict[str, int] | None:
    if "for another user" in (message or "").lower():
        return None

    local_session_id = _safe_int(local_agent.get("session_id"))
    local_computer_id = _safe_int(local_agent.get("computer_id"))
    conflict_computer_id = _active_session_computer_id(message)

    if conflict_computer_id > 0:
        session_id = local_session_id if conflict_computer_id == local_computer_id else 0
        return {"sessionId": session_id, "computerId": conflict_computer_id}

    if conflict_computer_id <= 0 and (local_session_id > 0 or local_computer_id > 0):
        return {"sessionId": local_session_id, "computerId": local_computer_id}

    if "active session conflict" in (message or "").lower():
        return {"sessionId": 0, "computerId": 0}

    return None


def _active_session_computer_id(message: str) -> int:
    match = re.search(r"active session on computer\s+(\d+)", message or "", flags=re.IGNORECASE)
    return int(match.group(1)) if match else 0


def _friendly_enrollment_conflict(exc: HttpJsonError) -> RuntimeError:
    message = exc.response_message or exc.body
    if "already has active session" in message or "Active session conflict" in message:
        return RuntimeError(
            "Вход выполнен, но сервер не разрешил создать сессию агента: "
            f"{message}. Завершите активную сессию пользователя/компьютера и повторите вход."
        )
    return RuntimeError(f"Вход выполнен, но регистрация компьютера не удалась: {message}")


def _update_agent_config(
    config_path: str | Path,
    login: dict[str, Any],
    enrollment: dict[str, Any],
    device: dict[str, str],
    *,
    gateway_url: str,
    gateway_tls_insecure: bool,
    activity_service_url: str | None,
    agent_management_url: str | None,
    agent_auth_token: str | None,
    agent_auth_header: str | None,
) -> None:
    raw = _read_yaml(config_path)
    agent = raw.setdefault("agent", {})
    services = raw.setdefault("services", {})
    security = raw.setdefault("security", {})
    transport = security.setdefault("agent_transport_auth", {})

    computer = enrollment.get("computer") or {}
    user = enrollment.get("user") or {}
    agent["computer_id"] = int(computer.get("id") or 0)
    agent["user_id"] = int(user.get("id") or 0) or None
    agent["session_id"] = int(enrollment.get("sessionId") or 0) or None
    agent["session_expires_at"] = enrollment.get("sessionExpiresAt") or None
    agent["auth_refresh_token"] = login.get("refreshToken") or None
    agent["device_name"] = device.get("hostname") or agent.get("device_name") or "unknown-device"

    services["gateway_url"] = gateway_url
    services["gateway_tls_insecure"] = gateway_tls_insecure
    if activity_service_url:
        services["activity_service_url"] = activity_service_url
    if agent_management_url:
        services["agent_management_url"] = agent_management_url
    if agent_auth_token is not None:
        transport["token"] = agent_auth_token
    if agent_auth_header:
        transport["header_name"] = agent_auth_header

    _write_yaml(config_path, raw)


def _token_for_session_end(
    *,
    base: str,
    context: ssl.SSLContext | None,
    agent: dict[str, Any],
    username: str | None,
    password: str | None,
) -> str:
    refresh_token = str(agent.get("auth_refresh_token") or "")
    if refresh_token:
        try:
            refreshed = _post_json(
                f"{base}/api/auth/refresh",
                {"refreshToken": refresh_token},
                context=context,
            )
            token = str(refreshed.get("token") or "")
            if token:
                return token
        except RuntimeError:
            if not username:
                raise

    if username and password is not None:
        login = _post_json(
            f"{base}/api/auth/login",
            {"username": username, "password": password},
            context=context,
        )
        token = str(login.get("token") or "")
        if token:
            return token

    raise RuntimeError("Cannot end computer session: no valid local auth session")


def _read_local_agent(path: str | Path) -> dict[str, Any]:
    try:
        raw = _read_yaml(path)
    except FileNotFoundError:
        return {}
    agent = raw.get("agent") if isinstance(raw, dict) else {}
    return agent if isinstance(agent, dict) else {}


def _read_yaml(path: str | Path) -> dict[str, Any]:
    config_path = Path(path).expanduser().resolve()
    if not config_path.exists():
        raise FileNotFoundError(f"Config file not found: {config_path}")
    with config_path.open("r", encoding="utf-8") as handle:
        return yaml.safe_load(handle) or {}


def _write_yaml(path: str | Path, raw: dict[str, Any]) -> None:
    config_path = Path(path).expanduser().resolve()
    config_path.parent.mkdir(parents=True, exist_ok=True)
    with config_path.open("w", encoding="utf-8") as handle:
        yaml.safe_dump(raw, handle, allow_unicode=True, sort_keys=False)


def _safe_int(value: Any) -> int:
    try:
        return int(value or 0)
    except (TypeError, ValueError):
        return 0


def _json_dict_or_none(body: str) -> dict[str, Any] | None:
    try:
        parsed = json.loads(body) if body else None
    except json.JSONDecodeError:
        return None
    return parsed if isinstance(parsed, dict) else None


def _response_message(parsed: dict[str, Any] | None, fallback: str) -> str:
    if parsed:
        for key in ("message", "error", "detail", "title"):
            value = parsed.get(key)
            if value not in (None, ""):
                return str(value)
    return fallback.strip()
