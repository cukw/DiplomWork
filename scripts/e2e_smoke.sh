#!/usr/bin/env bash
set -euo pipefail

API_BASE="${API_BASE:-http://localhost:8080/api}"

RESPONSE_STATUS=""
RESPONSE_BODY=""

request_public() {
  local method="$1"
  local url="$2"
  local body="${3:-}"
  local body_file
  local headers_file
  body_file="$(mktemp)"
  headers_file="$(mktemp)"

  if [[ -n "$body" ]]; then
    curl -sS -X "$method" "$url" -H "Content-Type: application/json" -d "$body" -D "$headers_file" -o "$body_file"
  else
    curl -sS -X "$method" "$url" -H "Content-Type: application/json" -D "$headers_file" -o "$body_file"
  fi

  RESPONSE_STATUS="$(awk 'NR==1 {print $2}' "$headers_file")"
  RESPONSE_BODY="$(cat "$body_file")"
  rm -f "$body_file" "$headers_file"
}

request_auth() {
  local method="$1"
  local url="$2"
  local token="$3"
  local body="${4:-}"
  local body_file
  local headers_file
  body_file="$(mktemp)"
  headers_file="$(mktemp)"

  if [[ -n "$body" ]]; then
    curl -sS -X "$method" "$url" \
      -H "Content-Type: application/json" \
      -H "Authorization: Bearer ${token}" \
      -d "$body" \
      -D "$headers_file" -o "$body_file"
  else
    curl -sS -X "$method" "$url" \
      -H "Content-Type: application/json" \
      -H "Authorization: Bearer ${token}" \
      -D "$headers_file" -o "$body_file"
  fi

  RESPONSE_STATUS="$(awk 'NR==1 {print $2}' "$headers_file")"
  RESPONSE_BODY="$(cat "$body_file")"
  rm -f "$body_file" "$headers_file"
}

assert_status() {
  local expected="$1"
  local actual="$2"
  local step="$3"
  if [[ "$expected" != "$actual" ]]; then
    echo "[FAIL] ${step}: expected HTTP ${expected}, got ${actual}"
    echo "Response: ${RESPONSE_BODY}"
    exit 1
  fi
}

json_get() {
  local json="$1"
  local path="$2"
  python3 - "$path" <<'PY' <<<"$json"
import json
import sys

path = sys.argv[1]
obj = json.load(sys.stdin)

for part in path.split('.'):
    if isinstance(obj, dict):
        obj = obj.get(part)
    elif isinstance(obj, list):
        try:
            obj = obj[int(part)]
        except Exception:
            obj = None
    else:
        obj = None
    if obj is None:
        break

if obj is None:
    sys.exit(1)

if isinstance(obj, (dict, list)):
    print(json.dumps(obj))
else:
    print(obj)
PY
}

json_len() {
  local json="$1"
  local path="$2"
  python3 - "$path" <<'PY' <<<"$json"
import json
import sys

path = sys.argv[1]
obj = json.load(sys.stdin)
for part in path.split('.'):
    if isinstance(obj, dict):
        obj = obj.get(part)
    elif isinstance(obj, list):
        obj = obj[int(part)]
    else:
        obj = None
        break
if obj is None:
    print(0)
elif isinstance(obj, (list, dict, str)):
    print(len(obj))
else:
    print(0)
PY
}

echo "[1/7] Register and login"
SUFFIX="$(date +%s)"
USERNAME="e2e_${SUFFIX}"
EMAIL="${USERNAME}@example.com"
PASSWORD="P@ssw0rd_${SUFFIX}!"

REGISTER_PAYLOAD=$(cat <<JSON
{"username":"${USERNAME}","email":"${EMAIL}","password":"${PASSWORD}","role":"user"}
JSON
)
request_public "POST" "${API_BASE}/auth/register" "${REGISTER_PAYLOAD}"
assert_status "200" "$RESPONSE_STATUS" "register"

LOGIN_PAYLOAD=$(cat <<JSON
{"username":"${USERNAME}","password":"${PASSWORD}"}
JSON
)
request_public "POST" "${API_BASE}/auth/login" "${LOGIN_PAYLOAD}"
assert_status "200" "$RESPONSE_STATUS" "login"
TOKEN="$(json_get "$RESPONSE_BODY" "token")"

if [[ -z "$TOKEN" ]]; then
  echo "[FAIL] login: empty token"
  exit 1
fi

echo "[2/7] CRUD app settings"
SETTINGS_MARKER="E2E-${SUFFIX}"
SETTINGS_PAYLOAD=$(cat <<JSON
{
  "generalSettings": {"systemName": "${SETTINGS_MARKER}", "logLevel": "Info", "maxLogRetention": "30", "sessionTimeout": "60", "enableAuditLog": true},
  "securitySettings": {"passwordMinLength": "8", "passwordRequireSpecialChars": true, "sessionTimeoutMinutes": "30", "maxLoginAttempts": "5", "lockoutDurationMinutes": "15", "enableTwoFactor": false, "jwtExpirationHours": "24"},
  "notificationSettings": {"emailNotifications": true, "smsNotifications": false, "pushNotifications": true, "alertThreshold": "5", "notificationEmail": "admin@example.com", "smtpServer": "smtp.example.com", "smtpPort": "587"},
  "monitoringSettings": {"dataRetentionDays": "90", "realTimeMonitoring": true, "anomalyDetection": true, "monitoringInterval": "5", "enableWhitelist": true, "enableBlacklist": true},
  "whitelistEntries": [{"id": 1, "application": "chrome.exe", "description": "Browser"}],
  "blacklistEntries": [{"id": 1, "application": "torrent.exe", "description": "Blocked"}]
}
JSON
)

request_auth "PUT" "${API_BASE}/app-settings" "$TOKEN" "$SETTINGS_PAYLOAD"
assert_status "200" "$RESPONSE_STATUS" "save app settings"

request_auth "GET" "${API_BASE}/app-settings" "$TOKEN"
assert_status "200" "$RESPONSE_STATUS" "get app settings"
ACTUAL_MARKER="$(json_get "$RESPONSE_BODY" "generalSettings.systemName")"
if [[ "$ACTUAL_MARKER" != "$SETTINGS_MARKER" ]]; then
  echo "[FAIL] settings persistence mismatch: expected ${SETTINGS_MARKER}, got ${ACTUAL_MARKER}"
  exit 1
fi

echo "[3/7] Create user+computer (1:1 policy)"
AUTH_USER_ID="$((100000 + (SUFFIX % 800000)))"
MAC_SUFFIX="$(printf '%012x' "$SUFFIX" | tail -c 13)"
MAC_ADDR="${MAC_SUFFIX:0:2}:${MAC_SUFFIX:2:2}:${MAC_SUFFIX:4:2}:${MAC_SUFFIX:6:2}:${MAC_SUFFIX:8:2}:${MAC_SUFFIX:10:2}"
CREATE_USER_PAYLOAD=$(cat <<JSON
{
  "authUserId": ${AUTH_USER_ID},
  "fullName": "E2E User ${SUFFIX}",
  "department": "QA",
  "hostname": "qa-e2e-${SUFFIX}",
  "osVersion": "Ubuntu 24.04",
  "ipAddress": "10.0.0.10",
  "macAddress": "${MAC_ADDR}"
}
JSON
)
request_auth "POST" "${API_BASE}/user/users" "$TOKEN" "$CREATE_USER_PAYLOAD"
assert_status "200" "$RESPONSE_STATUS" "create user"
CREATED_USER_ID="$(json_get "$RESPONSE_BODY" "id")"
COMPUTER_ID="$(json_get "$RESPONSE_BODY" "computer.id")"
if [[ -z "$CREATED_USER_ID" || -z "$COMPUTER_ID" ]]; then
  echo "[FAIL] create user: expected linked computer in response"
  exit 1
fi

echo "[4/7] Delete user cascade"
request_auth "DELETE" "${API_BASE}/user/users/${CREATED_USER_ID}" "$TOKEN"
assert_status "200" "$RESPONSE_STATUS" "delete user"

echo "[5/7] Agent command endpoints (optional if no agents)"
request_auth "GET" "${API_BASE}/agent/agents?page=1&pageSize=1" "$TOKEN"
assert_status "200" "$RESPONSE_STATUS" "get agents"
AGENTS_COUNT="$(json_len "$RESPONSE_BODY" "agents")"
if [[ "$AGENTS_COUNT" -gt 0 ]]; then
  AGENT_ID="$(json_get "$RESPONSE_BODY" "agents.0.id")"

  request_auth "POST" "${API_BASE}/agent/agents/${AGENT_ID}/commands/block" "$TOKEN" '{"reason":"E2E block"}'
  assert_status "200" "$RESPONSE_STATUS" "block command"

  request_auth "POST" "${API_BASE}/agent/agents/${AGENT_ID}/commands/unblock" "$TOKEN" '{"reason":"E2E unblock"}'
  assert_status "200" "$RESPONSE_STATUS" "unblock command"

  request_auth "GET" "${API_BASE}/agent/agents/${AGENT_ID}/commands?page=1&pageSize=20" "$TOKEN"
  assert_status "200" "$RESPONSE_STATUS" "command history"
  COMMANDS_COUNT="$(json_len "$RESPONSE_BODY" "commands")"
  if [[ "$COMMANDS_COUNT" -le 0 ]]; then
    echo "[FAIL] command history: expected at least one command"
    exit 1
  fi
else
  echo "[WARN] No agents found, command checks skipped"
fi

echo "[6/7] Logout"
request_auth "POST" "${API_BASE}/auth/logout" "$TOKEN"
assert_status "200" "$RESPONSE_STATUS" "logout"

echo "[7/7] Smoke E2E completed successfully"
