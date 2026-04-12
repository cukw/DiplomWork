#!/usr/bin/env bash
set -euo pipefail

OUT_FILE="${1:-.env.production.generated}"

rand_b64() {
  local bytes="${1:-48}"
  openssl rand -base64 "${bytes}" | tr -d '\n'
}

cat > "${OUT_FILE}" <<EOF
# Generated at: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
# Move values to your secret manager (Vault/KMS/etc.) and do not commit this file.

DB_PASSWORD=$(rand_b64 36)
RABBITMQ_USER=netmax_rabbit
RABBITMQ_PASS=$(rand_b64 36)
RABBITMQ_VHOST=/

JWT_ACTIVE_KEY_ID=v1
JWT_KEYS_V1=$(rand_b64 48)
JWT_KEYS_V2=
JWT_REQUIRE_HTTPS_METADATA=true

AGENT_SIGNING_KEY_ID=v1
AGENT_SIGNING_SECRET=$(rand_b64 48)

AGENT_AUTH_HEADER=x-agent-token
AGENT_AUTH_TOKEN=$(rand_b64 48)

GRAFANA_ADMIN_PASSWORD=$(rand_b64 36)

FRONTEND_HTTP_PORT=80
FRONTEND_HTTPS_PORT=443
CORS_ORIGIN_0=https://app.example.com
CORS_ORIGIN_1=https://admin.example.com
EOF

chmod 600 "${OUT_FILE}"
echo "Generated ${OUT_FILE} (chmod 600). Store in Vault/KMS and remove local copy after import."
