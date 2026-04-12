#!/usr/bin/env bash
set -euo pipefail

ENV_FILE="${1:-.env.production}"
MODE="${2:-prepare}" # prepare | activate | retire_old

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "Env file not found: ${ENV_FILE}" >&2
  exit 1
fi

set_env_value() {
  local key="$1"
  local value="$2"
  if rg -q "^${key}=" "${ENV_FILE}"; then
    sed -i.bak "s|^${key}=.*|${key}=${value}|" "${ENV_FILE}"
  else
    printf "%s=%s\n" "${key}" "${value}" >> "${ENV_FILE}"
  fi
}

get_env_value() {
  local key="$1"
  local value
  value="$(grep -E "^${key}=" "${ENV_FILE}" | head -n 1 | cut -d'=' -f2- || true)"
  printf "%s" "${value}"
}

rand_b64() {
  openssl rand -base64 48 | tr -d '\n'
}

case "${MODE}" in
  prepare)
    jwt_v2="$(get_env_value "JWT_KEYS_V2")"
    if [[ -z "${jwt_v2}" ]]; then
      set_env_value "JWT_KEYS_V2" "$(rand_b64)"
      echo "JWT_KEYS_V2 generated."
    else
      echo "JWT_KEYS_V2 already exists; keeping current value."
    fi
    echo "Prepare done. Deploy once with both keys present (active remains unchanged)."
    ;;
  activate)
    jwt_v2="$(get_env_value "JWT_KEYS_V2")"
    if [[ -z "${jwt_v2}" ]]; then
      echo "JWT_KEYS_V2 is empty. Run prepare first." >&2
      exit 1
    fi
    set_env_value "JWT_ACTIVE_KEY_ID" "v2"
    echo "Active JWT key switched to v2. Roll out all gateway/auth replicas."
    ;;
  retire_old)
    active="$(get_env_value "JWT_ACTIVE_KEY_ID")"
    if [[ "${active}" != "v2" ]]; then
      echo "Refusing retire_old: JWT_ACTIVE_KEY_ID must be v2 first." >&2
      exit 1
    fi
    set_env_value "JWT_KEYS_V1" ""
    echo "JWT_KEYS_V1 cleared. Old signing key retired."
    ;;
  *)
    echo "Unknown mode: ${MODE}. Use prepare|activate|retire_old" >&2
    exit 1
    ;;
esac

echo "Updated ${ENV_FILE}"
