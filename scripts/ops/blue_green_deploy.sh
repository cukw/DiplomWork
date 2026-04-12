#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${ROOT_DIR}"

ENV_FILE="${ENV_FILE:-.env.production}"
PROJECT_PREFIX="${PROJECT_PREFIX:-netmax}"
STATE_DIR="${STATE_DIR:-.deploy}"
ACTIVE_FILE="${STATE_DIR}/active_color"
HEALTH_PATH="${HEALTH_PATH:-/api/health}"
HEALTH_TIMEOUT_SEC="${HEALTH_TIMEOUT_SEC:-240}"
CLEAN_OLD="${CLEAN_OLD:-false}"

BLUE_HTTP_PORT="${BLUE_HTTP_PORT:-18080}"
BLUE_HTTPS_PORT="${BLUE_HTTPS_PORT:-18443}"
GREEN_HTTP_PORT="${GREEN_HTTP_PORT:-28080}"
GREEN_HTTPS_PORT="${GREEN_HTTPS_PORT:-28443}"

mkdir -p "${STATE_DIR}"

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "Missing env file: ${ENV_FILE}" >&2
  exit 1
fi

active="blue"
if [[ -f "${ACTIVE_FILE}" ]]; then
  active="$(tr -d '[:space:]' < "${ACTIVE_FILE}")"
fi

if [[ "${active}" == "blue" ]]; then
  target="green"
  target_http="${GREEN_HTTP_PORT}"
  target_https="${GREEN_HTTPS_PORT}"
else
  target="blue"
  target_http="${BLUE_HTTP_PORT}"
  target_https="${BLUE_HTTPS_PORT}"
fi

echo "Active color: ${active}"
echo "Target color: ${target}"

compose_base_tmp="$(mktemp)"
trap 'rm -f "${compose_base_tmp}"' EXIT
grep -vE '^[[:space:]]*container_name:' docker-compose.yml > "${compose_base_tmp}"

compose_target=(docker compose --env-file "${ENV_FILE}" -p "${PROJECT_PREFIX}-${target}" -f "${compose_base_tmp}" -f docker-compose.prod.yml)
compose_active=(docker compose --env-file "${ENV_FILE}" -p "${PROJECT_PREFIX}-${active}" -f "${compose_base_tmp}" -f docker-compose.prod.yml)

echo "Deploying ${target} stack..."
FRONTEND_HTTP_PORT="${target_http}" FRONTEND_HTTPS_PORT="${target_https}" "${compose_target[@]}" up -d --build --remove-orphans

echo "Waiting for health on http://127.0.0.1:${target_http}${HEALTH_PATH} ..."
deadline=$((SECONDS + HEALTH_TIMEOUT_SEC))
healthy=0
while (( SECONDS < deadline )); do
  if curl -fsS "http://127.0.0.1:${target_http}${HEALTH_PATH}" >/dev/null 2>&1; then
    healthy=1
    break
  fi
  sleep 3
done

if [[ "${healthy}" != "1" ]]; then
  echo "Health check failed for ${target}. Rolling back to ${active}." >&2
  FRONTEND_HTTP_PORT="${target_http}" FRONTEND_HTTPS_PORT="${target_https}" "${compose_target[@]}" down --remove-orphans
  exit 1
fi

echo "${target}" > "${ACTIVE_FILE}"
echo "Promoted color: ${target}"
echo "Frontend URL: http://127.0.0.1:${target_http}"
echo "Frontend TLS URL: https://127.0.0.1:${target_https}"

if [[ "${CLEAN_OLD}" == "true" ]]; then
  echo "Stopping old stack ${active}..."
  old_http="${BLUE_HTTP_PORT}"
  old_https="${BLUE_HTTPS_PORT}"
  if [[ "${active}" == "green" ]]; then
    old_http="${GREEN_HTTP_PORT}"
    old_https="${GREEN_HTTPS_PORT}"
  fi
  FRONTEND_HTTP_PORT="${old_http}" FRONTEND_HTTPS_PORT="${old_https}" "${compose_active[@]}" down --remove-orphans
fi
