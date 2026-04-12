#!/usr/bin/env bash
set -euo pipefail

API_BASE="${API_BASE:-http://localhost}"
ADMIN_USERNAME="${ADMIN_USERNAME:-admin}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-}"
DESIRED_VERSION="${DESIRED_VERSION:-}"
CANARY_PERCENT="${CANARY_PERCENT:-10}"
OBSERVATION_SECONDS="${OBSERVATION_SECONDS:-30}"
FAILURE_RATE_THRESHOLD="${FAILURE_RATE_THRESHOLD:-0.3}"
MAX_FAILED_AGENTS="${MAX_FAILED_AGENTS:-1}"
REQUESTED_BY="${REQUESTED_BY:-ops-canary}"

if [[ -z "${DESIRED_VERSION}" ]]; then
  echo "Set DESIRED_VERSION, for example: DESIRED_VERSION=1.2.3" >&2
  exit 1
fi

if [[ -z "${ADMIN_PASSWORD}" ]]; then
  echo "Set ADMIN_PASSWORD for admin API authentication" >&2
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required" >&2
  exit 1
fi

login_payload="$(jq -nc --arg u "${ADMIN_USERNAME}" --arg p "${ADMIN_PASSWORD}" '{username:$u,password:$p}')"
token="$(
  curl -fsS "${API_BASE}/api/auth/login" \
    -H "Content-Type: application/json" \
    -d "${login_payload}" \
  | jq -r '.token'
)"

if [[ -z "${token}" || "${token}" == "null" ]]; then
  echo "Failed to acquire JWT token" >&2
  exit 1
fi

plan_payload="$(jq -nc \
  --arg desiredVersion "${DESIRED_VERSION}" \
  --arg strategy "canary" \
  --argjson canaryPercent "${CANARY_PERCENT}" \
  '{desiredVersion:$desiredVersion,strategy:$strategy,canaryPercent:$canaryPercent,onlineOnly:true}')"

plan_response="$(
  curl -fsS "${API_BASE}/api/agent/rollouts/plan" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer ${token}" \
    -d "${plan_payload}"
)"

canary_ids="$(printf "%s" "${plan_response}" | jq -c '.stages[0].agentIds')"
if [[ -z "${canary_ids}" || "${canary_ids}" == "null" || "${canary_ids}" == "[]" ]]; then
  echo "No canary targets found" >&2
  exit 1
fi

execute_payload="$(jq -nc \
  --arg desiredVersion "${DESIRED_VERSION}" \
  --argjson agentIds "${canary_ids}" \
  --argjson observationSeconds "${OBSERVATION_SECONDS}" \
  --argjson failureRateThreshold "${FAILURE_RATE_THRESHOLD}" \
  --argjson maxFailedAgents "${MAX_FAILED_AGENTS}" \
  --arg requestedBy "${REQUESTED_BY}" \
  '{
    desiredVersion:$desiredVersion,
    agentIds:$agentIds,
    autoRollbackEnabled:true,
    observationSeconds:$observationSeconds,
    failureRateThreshold:$failureRateThreshold,
    maxFailedAgents:$maxFailedAgents,
    enqueueSelfUpdate:true,
    requestedBy:$requestedBy
  }')"

execute_response="$(
  curl -fsS "${API_BASE}/api/agent/rollouts/execute" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer ${token}" \
    -d "${execute_payload}"
)"

printf "%s\n" "${execute_response}" | jq .

rollback_triggered="$(printf "%s" "${execute_response}" | jq -r '.autoRollback.rollbackTriggered // false')"
if [[ "${rollback_triggered}" == "true" ]]; then
  echo "Canary rollout failed: auto-rollback was triggered." >&2
  exit 2
fi

echo "Canary rollout completed without rollback."
